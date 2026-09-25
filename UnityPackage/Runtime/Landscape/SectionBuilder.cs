using System;
using System.Collections.Generic;
using reromanlee.BlockyMesher.Meshing;
using reromanlee.BlockyMesher.Storage;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// Turns dirty sections into section objects. Sections touched by an edit are rebuilt at once;
    /// everything else waits in a queue, nearest to <see cref="Focus"/> first, and is built within the
    /// landscape's time budget per frame, which it shares with a streamer. Where the Job System has
    /// worker threads, builds run on them while the frame goes on. Where it has none (the web without
    /// multithreading), each build runs to the end right away, and the budget alone decides how many
    /// happen per frame.
    /// </summary>
    internal sealed class SectionBuilder : IDisposable
    {
        static readonly ProfilerMarker UpdateMarker = new("BlockyMesher.BuildSections");
        static readonly ProfilerMarker PresentMarker = new("BlockyMesher.PresentSection");

        struct Running
        {
            public int3 Section;
            public SectionBuild Build;
        }

        readonly Transform parent;
        readonly BlockStorage storage;
        readonly BlockTable table;
        readonly BlockRegistry registry;
        readonly FrameTimer timer;

        readonly Dictionary<int3, SectionObject> objects = new();
        readonly Stack<SectionObject> freeObjects = new();
        readonly List<SectionObject> cooking = new();
        readonly Stack<SectionBuild> freeBuilds = new();
        readonly List<Running> running = new();

        // The queue can hold sections that were built since they were added; "queued" says which still need it.
        readonly List<int3> queue = new();
        readonly HashSet<int3> queued = new();
        readonly HashSet<int3> urgent = new();
        readonly List<int3> postponed = new();
        readonly DistanceComparer comparer = new();
        bool sortNeeded;
        float idleSince = -1;

        public BuildSettings Settings;
        public ColliderMode Colliders;
        public float BudgetMs = 4;
        public float3 Focus;

        /// <summary>Which columns may be built; all when null. A streamer waits until a column's neighbors are loaded.</summary>
        public Func<int2, bool> CanBuild;

        /// <summary>Which columns get colliders; all when null. A streamer limits them to the area around the player.</summary>
        public Func<int2, bool> WantsColliders;

        public SectionBuilder(Transform parent, BlockStorage storage, BlockTable table, BlockRegistry registry, FrameTimer timer)
        {
            this.parent = parent;
            this.storage = storage;
            this.table = table;
            this.registry = registry;
            this.timer = timer;
        }

        public int QueuedCount => queued.Count;
        public int RunningCount => running.Count;
        public int PooledBuildCount => freeBuilds.Count;
        public long BuildsFinished { get; private set; }
        public Dictionary<int3, SectionObject>.ValueCollection Objects => objects.Values;

        static bool SingleThreaded => JobsUtility.JobWorkerCount == 0;
        static int ParallelBuilds => SingleThreaded ? 1 : JobsUtility.JobWorkerCount + 1;

        /// <summary>Queues every section touching the blocks from <paramref name="min"/> to <paramref name="max"/>, both included.</summary>
        public void MarkDirty(int3 min, int3 max, bool isUrgent)
        {
            int3 first = min >> 4;
            int3 last = max >> 4;
            first.y = math.max(first.y, 0);
            last.y = math.min(last.y, storage.SectionsPerColumn - 1);
            for (int z = first.z; z <= last.z; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                bool exists = storage.TryGetColumn(new int2(x, z), out _);
                for (int y = first.y; y <= last.y; y++)
                {
                    var section = new int3(x, y, z);
                    if (exists || objects.ContainsKey(section))
                        Enqueue(section, isUrgent);
                }
            }
        }

        public void MarkAllDirty()
        {
            foreach (Column column in storage.Columns)
            for (int y = 0; y < storage.SectionsPerColumn; y++)
                Enqueue(new int3(column.Position.x, y, column.Position.y), false);
        }

        /// <summary>Drops the section objects of a column that left the storage.</summary>
        public void RemoveColumn(int2 column)
        {
            for (int y = 0; y < storage.SectionsPerColumn; y++)
                Release(new int3(column.x, y, column.y));
        }

        public void Update()
        {
            using ProfilerMarker.AutoScope scope = UpdateMarker.Auto();
            Harvest(false, true);
            BuildUrgent();
            if (sortNeeded || math.distancesq(Focus, comparer.Focus) > 64)
            {
                comparer.Focus = Focus;
                queue.Sort(comparer);
                sortNeeded = false;
            }

            while (queue.Count > 0 && running.Count < ParallelBuilds && timer.CurrentMs < BudgetMs)
            {
                int3 section = queue[^1];
                queue.RemoveAt(queue.Count - 1);
                if (!queued.Contains(section))
                    continue;
                if (IsRunning(section))
                {
                    postponed.Add(section);
                    continue;
                }
                queued.Remove(section);
                Start(section);
                if (SingleThreaded)
                    Harvest(true);
            }
            if (postponed.Count > 0)
            {
                queue.AddRange(postponed);
                postponed.Clear();
                sortNeeded = true;
            }
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Picks up builds that finished during the frame, and colliders PhysX is done with.</summary>
        public void LateUpdate()
        {
            Harvest(false, true);
            for (int i = cooking.Count - 1; i >= 0; i--)
            {
                cooking[i].FinishCooking(false);
                if (!cooking[i].IsCooking)
                    cooking.RemoveAt(i);
            }
            TrimWhenIdle();
        }

        /// <summary>
        /// Each build holds about half a megabyte of buffers, and a burst of streaming leaves one per
        /// worker thread behind. After a few quiet seconds, all but one are freed.
        /// </summary>
        void TrimWhenIdle()
        {
            if (queued.Count > 0 || running.Count > 0)
            {
                idleSince = -1;
                return;
            }
            if (idleSince < 0)
                idleSince = Time.unscaledTime;
            else if (Time.unscaledTime - idleSince > 3)
                while (freeBuilds.Count > 1) freeBuilds.Pop().Dispose();
        }

        /// <summary>Builds everything that is waiting, right now, in batches so worker threads share the load.</summary>
        public void CompleteAll()
        {
            Harvest(true);
            urgent.Clear();
            while (queue.Count > 0)
            {
                while (queue.Count > 0 && running.Count < ParallelBuilds)
                {
                    int3 section = queue[^1];
                    queue.RemoveAt(queue.Count - 1);
                    if (queued.Remove(section))
                        Start(section);
                }
                JobHandle.ScheduleBatchedJobs();
                Harvest(true);
            }
            queued.Clear();
            foreach (SectionObject target in cooking)
                target.FinishCooking(true);
            cooking.Clear();
        }

        public void Dispose()
        {
            foreach (Running entry in running)
                entry.Build.Dispose();
            running.Clear();
            while (freeBuilds.Count > 0)
                freeBuilds.Pop().Dispose();
            foreach (SectionObject target in objects.Values)
                target.Destroy();
            while (freeObjects.Count > 0)
                freeObjects.Pop().Destroy();
            objects.Clear();
            cooking.Clear();
            queue.Clear();
            queued.Clear();
            urgent.Clear();
        }

        public void AddStats(ref LandscapeStats stats)
        {
            foreach (SectionObject target in objects.Values)
            {
                Mesh mesh = target.Mesh;
                long indices = 0;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    indices += mesh.GetIndexCount(subMesh);
                stats.SectionMeshes++;
                stats.DrawCalls += mesh.subMeshCount;
                stats.Vertices += mesh.vertexCount;
                stats.Triangles += indices / 3;
                stats.MeshBytes += (long)mesh.vertexCount * SectionVertex.Size + indices * (mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                if (target.HasColliders)
                {
                    stats.SectionsWithColliders++;
                    stats.ColliderBytes += target.ColliderBytes;
                }
            }
            stats.QueuedSections = queued.Count;
            stats.BuildingSections = running.Count;
            stats.WorkBufferBytes += (long)(running.Count + freeBuilds.Count) * SectionBuild.MemoryBytes;
        }

        void Enqueue(int3 section, bool isUrgent)
        {
            if (queued.Add(section))
            {
                queue.Add(section);
                sortNeeded = true;
            }
            if (isUrgent)
                urgent.Add(section);
        }

        void BuildUrgent()
        {
            if (urgent.Count == 0)
                return;
            // Builds already running for these sections read older blocks: let them land first.
            foreach (int3 section in urgent)
            {
                FinishRunning(section);
                if (queued.Remove(section))
                    Start(section);
            }
            urgent.Clear();
            JobHandle.ScheduleBatchedJobs();
            Harvest(true);
        }

        void Start(int3 section)
        {
            if (!NeedsBuild(section) || (CanBuild != null && !CanBuild(section.xz)))
            {
                Release(section);
                return;
            }
            SectionBuild build = freeBuilds.Count > 0 ? freeBuilds.Pop() : new SectionBuild();
            build.CopyInputs(storage, section.xz, section.y);
            bool wantsColliders = WantsColliders == null || WantsColliders(section.xz);
            ColliderMode colliders = wantsColliders ? Colliders : ColliderMode.None;
            build.Schedule(table, Settings, colliders);
            running.Add(new Running { Section = section, Build = build });
        }

        /// <summary>Presents finished builds; all of them, or only as many as the frame budget allows.</summary>
        void Harvest(bool wait, bool withinBudget = false)
        {
            for (int i = 0; i < running.Count; i++)
            {
                if (withinBudget && timer.CurrentMs >= BudgetMs)
                    return;
                Running entry = running[i];
                if (!wait && !entry.Build.Handle.IsCompleted)
                    continue;
                entry.Build.Handle.Complete();
                Present(entry.Section, entry.Build);
                freeBuilds.Push(entry.Build);
                running.RemoveAt(i--);
                BuildsFinished++;
            }
        }

        void FinishRunning(int3 section)
        {
            for (int i = 0; i < running.Count; i++)
            {
                if (!running[i].Section.Equals(section))
                    continue;
                Running entry = running[i];
                entry.Build.Handle.Complete();
                Present(entry.Section, entry.Build);
                freeBuilds.Push(entry.Build);
                running.RemoveAt(i);
                return;
            }
        }

        bool IsRunning(int3 section)
        {
            foreach (Running entry in running)
                if (entry.Section.Equals(section)) return true;
            return false;
        }

        void Present(int3 section, SectionBuild build)
        {
            using ProfilerMarker.AutoScope scope = PresentMarker.Auto();
            if (!storage.TryGetColumn(section.xz, out _) || build.VertexCount == 0)
            {
                build.Cancel();
                Release(section);
                return;
            }
            if (!objects.TryGetValue(section, out SectionObject target))
            {
                target = freeObjects.Count > 0 ? freeObjects.Pop() : new SectionObject(parent);
                target.Show(section);
                objects.Add(section, target);
            }
            build.Apply(target.Mesh);

            // Once on the GPU, the CPU copy is dead weight: the next build replaces the mesh anyway.
            target.Mesh.UploadMeshData(true);
            target.SetMaterials(registry.GetMaterials(build.PassMask));
            target.ApplyColliders(build);
            if (target.IsCooking && !cooking.Contains(target))
                cooking.Add(target);
            build.Cancel();
        }

        void Release(int3 section)
        {
            if (!objects.Remove(section, out SectionObject target))
                return;
            target.Hide();
            cooking.Remove(target);
            freeObjects.Push(target);
        }

        /// <summary>All air, or solid blocks walled in by more solid blocks, shows nothing and needs no build.</summary>
        bool NeedsBuild(int3 section)
        {
            if (!storage.TryGetColumn(section.xz, out Column column))
                return false;
            SectionBlocks blocks = column.Sections[section.y];
            if (!blocks.IsUniform)
                return true;
            BlockInfo info = table[blocks.UniformId];
            if (!info.Is(BlockFlags.Visible))
                return false;
            if (!info.Is(BlockFlags.Opaque))
                return true;
            return !IsSolid(section + new int3(1, 0, 0)) || !IsSolid(section + new int3(-1, 0, 0))
                || !IsSolid(section + new int3(0, 1, 0)) || !IsSolid(section + new int3(0, -1, 0))
                || !IsSolid(section + new int3(0, 0, 1)) || !IsSolid(section + new int3(0, 0, -1));
        }

        bool IsSolid(int3 section)
        {
            if (section.y < 0)
                return Settings.SolidBelowWorld;
            if (section.y >= storage.SectionsPerColumn || !storage.TryGetColumn(section.xz, out Column column))
                return false;
            SectionBlocks blocks = column.Sections[section.y];
            return blocks.IsUniform && table[blocks.UniformId].Is(BlockFlags.Opaque);
        }

        sealed class DistanceComparer : IComparer<int3>
        {
            public float3 Focus = new(float.MaxValue);

            // Farthest first, so the nearest section sits at the end of the list, where removing it is cheap.
            public int Compare(int3 a, int3 b) => DistanceSq(b).CompareTo(DistanceSq(a));

            float DistanceSq(int3 section) => math.distancesq((float3)(section * Section.Size) + Section.Size / 2f, Focus);
        }
    }
}
