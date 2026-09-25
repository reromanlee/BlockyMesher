using System.Collections.Generic;
using reromanlee.BlockyMesher.Storage;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// Makes a <see cref="Landscape"/> endless: keeps the columns around the focus points loaded,
    /// generates missing ones with a <see cref="TerrainGenerator"/>, and drops those left far behind.
    /// Player edits survive that (see <see cref="SaveEdits"/>). Runs in Play Mode.
    /// </summary>
    [RequireComponent(typeof(Landscape)), DisallowMultipleComponent]
    [AddComponentMenu("BlockyMesher/Landscape Streamer")]
    public sealed class LandscapeStreamer : MonoBehaviour
    {
        static readonly ProfilerMarker TickMarker = new("BlockyMesher.Streaming");

        [Tooltip("Generates the terrain.")]
        [SerializeField] TerrainGenerator generator;

        [Tooltip("Terrain loads around these, usually the player. The main camera is used while the list is empty.")]
        [SerializeField] List<Transform> focusPoints = new();

        [Tooltip("How far terrain is shown, in columns of 16 blocks. A ring of columns beyond it is loaded too, since meshes need their neighbors.")]
        [SerializeField, Range(1, 32)] int viewRadius = 8;

        [Tooltip("Columns around the focus points that get colliders, if the landscape has any.")]
        [SerializeField, Range(0, 16)] int physicsRadius = 2;

        // A column's diagonal neighbors are up to √2 columns further away than it is.
        float LoadReach => viewRadius + 1.5f;
        float UnloadReach => viewRadius + 2.5f;

        sealed class Generation
        {
            public int2 Column;
            public NativeArray<ushort> Blocks;
            public NativeArray<int> UniformIds;
            public NativeArray<ushort> SkyStart;
            public JobHandle Handle;
            public BlockStorage Target;

            public void Dispose()
            {
                Handle.Complete();
                Blocks.Dispose();
                UniformIds.Dispose();
                SkyStart.Dispose();
            }
        }

        Landscape landscape;
        readonly EditStore edits = new();
        readonly List<Generation> running = new();
        readonly Stack<Generation> free = new();
        readonly HashSet<int2> generating = new();
        readonly List<int2> candidates = new();
        readonly HashSet<int2> candidateSet = new();
        readonly List<float> scores = new();
        readonly List<int2> farColumns = new();
        HashSet<int2> viewColumns = new();
        HashSet<int2> nextViewColumns = new();
        HashSet<int2> physicsColumns = new();
        HashSet<int2> nextPhysicsColumns = new();
        readonly List<float3> foci = new();
        float2 lookDirection;
        bool attached;
        long columnsLoaded;
        readonly RateMeter loadRate = new();
        float idleSince = -1;

        /// <summary>Changing it regenerates every loaded column; stored edits are kept.</summary>
        public TerrainGenerator Generator
        {
            get => generator;
            set { generator = value; UnloadAll(true); }
        }

        public List<Transform> FocusPoints => focusPoints;

        public int ViewRadius
        {
            get => viewRadius;
            set => viewRadius = Mathf.Clamp(value, 1, 32);
        }

        public int PhysicsRadius
        {
            get => physicsRadius;
            set => physicsRadius = Mathf.Clamp(value, 0, 16);
        }

        public int LoadedColumnCount => landscape != null && landscape.Storage != null ? landscape.Storage.ColumnCount : 0;
        public int GeneratingCount => running.Count;

        /// <summary>Sections holding player edits, loaded or not, and the memory those edits take.</summary>
        public int EditedSectionCount => edits.SectionCount;
        public long EditBytes => edits.Bytes;

        /// <summary>Whether the terrain under a world position is loaded, for example before dropping the player in.</summary>
        public bool IsLoaded(Vector3 worldPosition)
        {
            if (landscape == null || landscape.Storage == null)
                return false;
            Vector3 local = landscape.transform.InverseTransformPoint(worldPosition);
            return landscape.Storage.TryGetColumn(new int2(Mathf.FloorToInt(local.x / Section.Size), Mathf.FloorToInt(local.z / Section.Size)), out _);
        }

        /// <summary>
        /// Every player edit as bytes, to store wherever the game saves: a file, browser storage, a server.
        /// Edits are differences from generated terrain, so keep the generator and its seed alongside.
        /// </summary>
        public byte[] SaveEdits() => landscape != null && landscape.Storage != null ? edits.Save(landscape.Storage) : new byte[0];

        /// <summary>Replaces the current edits with saved ones. Loaded columns regenerate with them.</summary>
        public void LoadEdits(byte[] data)
        {
            edits.Load(data);
            UnloadAll(false);
        }

        public void ClearEdits()
        {
            edits.Clear();
            UnloadAll(false);
        }

        void OnEnable()
        {
            if (Application.isPlaying)
                Attach();
        }

        void OnDisable() => Detach();

        void Update()
        {
            if (!attached)
                return;
            landscape.Timer.Begin();
            Tick();
            loadRate.Sample(columnsLoaded);
            landscape.Timer.End();
        }

        internal void AddStats(ref LandscapeStats stats)
        {
            stats.GeneratingColumns = running.Count;
            stats.ColumnsLoadedPerSecond = loadRate.PerSecond;
            stats.EditBytes = edits.Bytes;
            if (landscape != null && landscape.IsReady)
                stats.WorkBufferBytes += (long)(running.Count + free.Count) * (landscape.Height * Section.Area * sizeof(ushort) + Section.Area * sizeof(ushort));
        }

        internal void Attach()
        {
            if (attached)
                return;
            landscape = GetComponent<Landscape>();
            landscape.CreatesColumns = false;
            landscape.BlockChanged += OnBlockChanged;
            landscape.AreaChanged += OnAreaChanged;
            landscape.ShuttingDown += CancelGenerations;
            attached = true;
        }

        internal void Detach()
        {
            if (!attached)
                return;
            foreach (Generation generation in running)
                generation.Dispose();
            running.Clear();
            generating.Clear();
            while (free.Count > 0)
                free.Pop().Dispose();
            viewColumns.Clear();
            landscape.BlockChanged -= OnBlockChanged;
            landscape.AreaChanged -= OnAreaChanged;
            landscape.ShuttingDown -= CancelGenerations;
            landscape.CreatesColumns = true;
            landscape.Focus = null;
            if (landscape.Builder != null)
            {
                landscape.Builder.CanBuild = null;
                landscape.Builder.WantsColliders = null;
            }
            attached = false;
        }

        /// <summary>One frame of streaming: finish generated columns, drop far ones, start near ones.</summary>
        internal void Tick()
        {
            if (!landscape.IsReady || generator == null)
                return;
            using ProfilerMarker.AutoScope scope = TickMarker.Auto();
            landscape.Builder.CanBuild = CanBuild;
            landscape.Builder.WantsColliders = WantsColliders;
            UpdateFoci();
            landscape.Focus = foci[0];

            FinishGenerations(false);
            UnloadFarColumns();
            UpdateViewColumns();
            StartGenerations();
            UpdatePhysicsColumns();
            TrimWhenIdle();
        }

        /// <summary>After a few quiet seconds, frees the column buffers a burst of loading left behind.</summary>
        void TrimWhenIdle()
        {
            if (candidates.Count > 0 || running.Count > 0)
            {
                idleSince = -1;
                return;
            }
            if (idleSince < 0)
                idleSince = Time.unscaledTime;
            else if (Time.unscaledTime - idleSince > 3)
                while (free.Count > 1) free.Pop().Dispose();
        }

        /// <summary>Test hook: generates and builds everything in reach right now.</summary>
        internal void LoadEverything()
        {
            do
            {
                Tick();
                FinishGenerations(true);
            }
            while (candidates.Count > 0);
            landscape.CompleteRebuilds();
        }

        internal EditStore Edits => edits;

        void UpdateFoci()
        {
            foci.Clear();
            Transform primary = null;
            foreach (Transform focus in focusPoints)
            {
                if (focus == null) continue;
                primary ??= focus;
                foci.Add(landscape.transform.InverseTransformPoint(focus.position));
            }
            if (primary == null && Camera.main != null)
            {
                primary = Camera.main.transform;
                foci.Add(landscape.transform.InverseTransformPoint(primary.position));
            }
            if (foci.Count == 0)
                foci.Add(float3.zero);
            Vector3 forward = primary != null ? landscape.transform.InverseTransformDirection(primary.forward) : Vector3.forward;
            lookDirection = math.normalizesafe(new float2(forward.x, forward.z));
        }

        /// <summary>Distance from a column's center to the nearest focus point, in columns.</summary>
        float ColumnDistance(int2 column)
        {
            float2 center = ((float2)column + 0.5f) * Section.Size;
            float nearest = float.MaxValue;
            foreach (float3 focus in foci)
                nearest = math.min(nearest, math.distance(center, focus.xz));
            return nearest / Section.Size;
        }

        bool CanBuild(int2 column)
        {
            if (!viewColumns.Contains(column))
                return false;
            // Meshes read 8 blocks into each neighbor, so all eight must be loaded.
            BlockStorage storage = landscape.Storage;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!storage.TryGetColumn(column + new int2(dx, dz), out _))
                    return false;
            }
            return true;
        }

        bool WantsColliders(int2 column) => ColumnDistance(column) <= physicsRadius;

        /// <summary>
        /// Loaded columns within the view radius get meshes. Columns also load a little beyond it, so
        /// one coming into view is queued for building: it may have loaded while still too far to
        /// show. One going out of view is queued too, which drops its meshes. Half a column of slack
        /// keeps a player at the edge from rebuilding it over and over.
        /// </summary>
        void UpdateViewColumns()
        {
            nextViewColumns.Clear();
            foreach (Column column in landscape.Storage.Columns)
            {
                float distance = ColumnDistance(column.Position);
                if (distance <= viewRadius || (distance <= viewRadius + 0.5f && viewColumns.Contains(column.Position)))
                    nextViewColumns.Add(column.Position);
            }
            foreach (int2 column in nextViewColumns)
                if (!viewColumns.Contains(column)) MarkColumnDirty(column);
            foreach (int2 column in viewColumns)
                if (!nextViewColumns.Contains(column)) MarkColumnDirty(column);
            (viewColumns, nextViewColumns) = (nextViewColumns, viewColumns);
        }

        void StartGenerations()
        {
            BlockStorage storage = landscape.Storage;
            candidates.Clear();
            candidateSet.Clear();
            int extent = (int)math.ceil(LoadReach) + 1;
            foreach (float3 focus in foci)
            {
                var center = (int2)math.floor(focus.xz / Section.Size);
                for (int dz = -extent; dz <= extent; dz++)
                for (int dx = -extent; dx <= extent; dx++)
                {
                    int2 column = center + new int2(dx, dz);
                    if (ColumnDistance(column) <= LoadReach && !storage.TryGetColumn(column, out _) && !generating.Contains(column) && candidateSet.Add(column))
                        candidates.Add(column);
                }
            }
            if (candidates.Count == 0)
                return;

            // Nearest first, and what the camera looks at before what is behind it.
            scores.Clear();
            foreach (int2 column in candidates)
            {
                float2 toColumn = ((float2)column + 0.5f) * Section.Size - foci[0].xz;
                float facing = math.max(0, math.dot(math.normalizesafe(toColumn), lookDirection));
                scores.Add(ColumnDistance(column) * (1.5f - 0.5f * facing));
            }
            SortByScore();

            int parallel = JobsUtility.JobWorkerCount == 0 ? 1 : JobsUtility.JobWorkerCount;
            for (int i = 0; i < candidates.Count && running.Count < parallel && landscape.Timer.CurrentMs < landscape.FrameBudgetMs; i++)
            {
                Generate(candidates[i]);
                if (JobsUtility.JobWorkerCount == 0)
                    FinishGenerations(true);
            }
            JobHandle.ScheduleBatchedJobs();
        }

        void Generate(int2 column)
        {
            Generation generation = Rent();
            generation.Column = column;
            generation.Target = landscape.Storage;
            var context = new ColumnContext { Position = column, Height = landscape.Height };
            JobHandle cleared = new ClearBlocksJob { Blocks = generation.Blocks }.Schedule();
            JobHandle generated = generator.Schedule(context, generation.Blocks, cleared);
            generation.Handle = new ColumnSummaryJob
            {
                Blocks = generation.Blocks,
                BlockInfos = landscape.Table.Blocks,
                SectionCount = landscape.SectionsPerColumn,
                UniformIds = generation.UniformIds,
                SkyStart = generation.SkyStart,
            }.Schedule(generated);
            running.Add(generation);
            generating.Add(column);
        }

        /// <summary>The generations read the landscape's block table, which is about to be freed.</summary>
        void CancelGenerations()
        {
            foreach (Generation generation in running)
            {
                generation.Handle.Complete();
                free.Push(generation);
            }
            running.Clear();
            generating.Clear();
        }

        void FinishGenerations(bool wait)
        {
            for (int i = 0; i < running.Count; i++)
            {
                Generation generation = running[i];
                if (!wait && !generation.Handle.IsCompleted)
                    continue;
                generation.Handle.Complete();
                running.RemoveAt(i--);
                generating.Remove(generation.Column);
                // The landscape may have restarted, or the player moved on, while it was generating.
                if (generation.Target == landscape.Storage && ColumnDistance(generation.Column) <= UnloadReach)
                    Load(generation);
                free.Push(generation);
            }
        }

        void Load(Generation generation)
        {
            BlockStorage storage = landscape.Storage;
            Column column = storage.LoadColumn(generation.Column, generation.Blocks, generation.UniformIds, generation.SkyStart);
            edits.Apply(storage, column);
            columnsLoaded++;

            // The column's neighbors can now build their borders too.
            int2 column0 = generation.Column;
            landscape.Builder.MarkDirty(new int3(column0.x - 1, 0, column0.y - 1) * Section.Size,
                new int3((column0.x + 2) * Section.Size - 1, landscape.Height - 1, (column0.y + 2) * Section.Size - 1), false);
        }

        void UnloadFarColumns()
        {
            BlockStorage storage = landscape.Storage;
            farColumns.Clear();
            foreach (Column column in storage.Columns)
            {
                if (ColumnDistance(column.Position) > UnloadReach)
                    farColumns.Add(column.Position);
            }
            foreach (int2 position in farColumns)
                Unload(position, true);
        }

        void UnloadAll(bool keepEdits)
        {
            if (landscape == null || landscape.Storage == null)
                return;
            farColumns.Clear();
            foreach (Column column in landscape.Storage.Columns)
                farColumns.Add(column.Position);
            foreach (int2 position in farColumns)
                Unload(position, keepEdits);
        }

        void Unload(int2 position, bool keepEdits)
        {
            BlockStorage storage = landscape.Storage;
            if (!storage.TryGetColumn(position, out Column column))
                return;
            if (keepEdits)
                edits.Unloading(storage, column);
            storage.RemoveColumn(position);
            landscape.Builder.RemoveColumn(position);
            viewColumns.Remove(position);
            physicsColumns.Remove(position);
        }

        /// <summary>Rebuilds columns that gain or lose colliders as the focus points move.</summary>
        void UpdatePhysicsColumns()
        {
            if (landscape.Colliders == ColliderMode.None)
                return;
            nextPhysicsColumns.Clear();
            int reach = physicsRadius + 1;
            foreach (float3 focus in foci)
            {
                var center = (int2)math.floor(focus.xz / Section.Size);
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int2 column = center + new int2(dx, dz);
                    if (WantsColliders(column) && landscape.Storage.TryGetColumn(column, out _))
                        nextPhysicsColumns.Add(column);
                }
            }
            foreach (int2 column in nextPhysicsColumns)
                if (!physicsColumns.Contains(column)) MarkColumnDirty(column);
            foreach (int2 column in physicsColumns)
                if (!nextPhysicsColumns.Contains(column)) MarkColumnDirty(column);
            (physicsColumns, nextPhysicsColumns) = (nextPhysicsColumns, physicsColumns);
        }

        void MarkColumnDirty(int2 column) =>
            landscape.Builder.MarkDirty(new int3(column.x * Section.Size, 0, column.y * Section.Size),
                new int3(column.x * Section.Size + Section.Size - 1, landscape.Height - 1, column.y * Section.Size + Section.Size - 1), false);

        void OnBlockChanged(int3 block, ushort id) => edits.RecordBlock(block, id);

        void OnAreaChanged(int3 min, int3 max)
        {
            // The landscape only changed columns that are loaded.
            int3 first = min >> 4;
            int3 last = (max - 1) >> 4;
            first.y = math.max(first.y, 0);
            last.y = math.min(last.y, landscape.SectionsPerColumn - 1);
            for (int z = first.z; z <= last.z; z++)
            for (int x = first.x; x <= last.x; x++)
            {
                if (!landscape.Storage.TryGetColumn(new int2(x, z), out _))
                    continue;
                for (int y = first.y; y <= last.y; y++)
                    edits.RecordSection(new int3(x, y, z));
            }
        }

        Generation Rent()
        {
            int volume = landscape.Height * Section.Area;
            while (free.Count > 0)
            {
                Generation reused = free.Pop();
                if (reused.Blocks.Length == volume)
                    return reused;
                reused.Dispose();
            }
            return new Generation
            {
                Blocks = new NativeArray<ushort>(volume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                UniformIds = new NativeArray<int>(landscape.SectionsPerColumn, Allocator.Persistent),
                SkyStart = new NativeArray<ushort>(Section.Area, Allocator.Persistent),
            };
        }

        /// <summary>Sorts the candidates by their score, lowest first. The list is small, so a simple insertion sort does.</summary>
        void SortByScore()
        {
            for (int i = 1; i < candidates.Count; i++)
            {
                int2 column = candidates[i];
                float score = scores[i];
                int j = i - 1;
                while (j >= 0 && scores[j] > score)
                {
                    candidates[j + 1] = candidates[j];
                    scores[j + 1] = scores[j];
                    j--;
                }
                candidates[j + 1] = column;
                scores[j + 1] = score;
            }
        }

    }
}
