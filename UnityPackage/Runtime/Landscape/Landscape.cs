using System;
using System.Collections.Generic;
using reromanlee.BlockyMesher.Meshing;
using reromanlee.BlockyMesher.Storage;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// A world of blocks with its own transform. On its own it is a standalone object, like a raft or
    /// a house, filled through code or a <see cref="BlockPattern"/>; with a LandscapeStreamer next to
    /// it, it becomes an endless terrain. Positions are in blocks relative to this GameObject, and y
    /// runs from 0 up to <see cref="Height"/>: move the GameObject to put the landscape anywhere.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("BlockyMesher/Landscape")]
    public sealed class Landscape : MonoBehaviour
    {
        [Tooltip("The block types this landscape uses.")]
        [SerializeField] BlockRegistry registry;

        [Tooltip("How tall the landscape is, in sections of 16 blocks.")]
        [SerializeField, Range(1, 64)] int sectionsPerColumn = 16;

        [Tooltip("Blend light across each face. Off gives every face the flat light of the block in front of it.")]
        [SerializeField] bool smoothLighting = true;

        [Tooltip("Treat everything below y = 0 as solid, which hides the bottom faces of a terrain. Leave it off for objects seen from below, like a raft.")]
        [SerializeField] bool solidBelowWorld;

        [Tooltip("Mesh colliders for a landscape that stays put, box colliders for one on a Rigidbody.")]
        [SerializeField] ColliderMode colliders;

        [Tooltip("Main-thread milliseconds per frame for building sections, and for streaming with a streamer. Edits always rebuild at once.")]
        [SerializeField, Min(0.5f)] float frameBudgetMs = 4;

        [Tooltip("Blocks the landscape starts with, placed at (0, 0, 0).")]
        [SerializeField] BlockPattern pattern;

        BlockTable table;
        BlockStorage storage;
        SectionBuilder builder;
        CrackOverlay cracks;
        int batchDepth;
        readonly FrameTimer timer = new();
        readonly RateMeter buildRate = new();

        /// <summary>Changing it starts the landscape over: its blocks are cleared.</summary>
        public BlockRegistry Registry
        {
            get => registry;
            set { registry = value; Restart(); }
        }

        /// <summary>Changing it starts the landscape over: its blocks are cleared.</summary>
        public int SectionsPerColumn
        {
            get => sectionsPerColumn;
            set { sectionsPerColumn = Mathf.Clamp(value, 1, 64); Restart(); }
        }

        /// <summary>Changing it starts the landscape over with the new pattern.</summary>
        public BlockPattern Pattern
        {
            get => pattern;
            set { pattern = value; Restart(); }
        }

        public bool SmoothLighting
        {
            get => smoothLighting;
            set { smoothLighting = value; ApplySettings(true); }
        }

        public bool SolidBelowWorld
        {
            get => solidBelowWorld;
            set { solidBelowWorld = value; ApplySettings(true); }
        }

        public ColliderMode Colliders
        {
            get => colliders;
            set { colliders = value; ApplySettings(true); }
        }

        public float FrameBudgetMs
        {
            get => frameBudgetMs;
            set { frameBudgetMs = Mathf.Max(0.5f, value); ApplySettings(false); }
        }

        /// <summary>Height in blocks. Valid y positions run from 0 to Height - 1.</summary>
        public int Height => sectionsPerColumn * Section.Size;

        /// <summary>False until a registry is assigned.</summary>
        public bool IsReady => storage != null;

        internal FrameTimer Timer => timer;
        internal BlockStorage Storage => storage;
        internal SectionBuilder Builder => builder;
        internal BlockTable Table => table;

        /// <summary>Whether edits may add columns. A streamer turns this off: it decides which columns exist.</summary>
        internal bool CreatesColumns = true;

        /// <summary>Set by a streamer to build near the player first. Otherwise the main camera is used.</summary>
        internal float3? Focus;

        internal event Action<int3, ushort> BlockChanged;
        internal event Action<int3, int3> AreaChanged;

        /// <summary>Raised before the blocks and their table are freed, so jobs still reading them can finish first.</summary>
        internal event Action ShuttingDown;

        public ushort GetBlock(Vector3Int position) => storage != null ? storage.GetBlock(ToInt3(position)) : (ushort)0;

        public bool SetBlock(Vector3Int position, BlockData block) => SetBlock(position, block != null ? (ushort)block.id : (ushort)0);

        /// <summary>Changes one block. Its section is rebuilt within the frame; far light changes follow shortly after.</summary>
        public bool SetBlock(Vector3Int position, ushort id)
        {
            if (storage == null)
                return false;
            int3 block = ToInt3(position);
            BlockEdit edit = storage.SetBlock(block, id, CreatesColumns);
            if (!edit.Changed)
                return false;

            // Faces and their shading change right around the block, so those sections are rebuilt
            // now. Light reaches up to 8 blocks further, and a moved sky start relights the column below.
            builder.MarkDirty(block - 2, block + 2, batchDepth == 0);
            builder.MarkDirty(block - Neighborhood.Border, block + Neighborhood.Border, false);
            if (edit.OldSkyStart != edit.NewSkyStart)
            {
                int low = math.min(edit.OldSkyStart, edit.NewSkyStart) - Neighborhood.Border;
                int high = math.max(edit.OldSkyStart, edit.NewSkyStart) + Neighborhood.Border;
                builder.MarkDirty(new int3(block.x - Neighborhood.Border, low, block.z - Neighborhood.Border),
                    new int3(block.x + Neighborhood.Border, high, block.z + Neighborhood.Border), false);
            }
            BlockChanged?.Invoke(block, id);
            RebuildIfEditing();
            return true;
        }

        public void Fill(BoundsInt area, BlockData block) => Fill(area, block != null ? (ushort)block.id : (ushort)0);

        /// <summary>Sets every block in <paramref name="area"/>. Sections it covers completely stay cheap in memory.</summary>
        public void Fill(BoundsInt area, ushort id)
        {
            if (storage == null || area.size.x <= 0 || area.size.y <= 0 || area.size.z <= 0)
                return;
            int3 min = ToInt3(area.min);
            int3 max = ToInt3(area.max);
            storage.Fill(min, max, id, CreatesColumns);
            MarkAreaDirty(min, max);
            AreaChanged?.Invoke(min, max);
            RebuildIfEditing();
        }

        /// <summary>
        /// Copies a pattern into the landscape with its lowest corner at <paramref name="origin"/>.
        /// The pattern's air leaves the landscape's blocks alone, unless <paramref name="includeAir"/> is set.
        /// </summary>
        public void Place(BlockPattern pattern, Vector3Int origin, bool includeAir = false)
        {
            if (storage == null || pattern == null)
                return;
            int3 min = ToInt3(origin);
            int3 max = min + ToInt3(pattern.Size);
            PlaceBlocks(pattern, min, includeAir);
            MarkAreaDirty(min, max);
            AreaChanged?.Invoke(min, max);
            RebuildIfEditing();
        }

        /// <summary>Copies the blocks in <paramref name="area"/> into a new pattern, for example to save a raft the player built.</summary>
        public BlockPattern Export(BoundsInt area)
        {
            Vector3Int size = Vector3Int.Max(area.size, Vector3Int.zero);
            var blocks = new ushort[size.x * size.y * size.z];
            int i = 0;
            for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
            for (int x = 0; x < size.x; x++)
                blocks[i++] = GetBlock(new Vector3Int(area.xMin + x, area.yMin + y, area.zMin + z));
            return BlockPattern.Create(size, blocks);
        }

        /// <summary>Removes every block.</summary>
        public void Clear()
        {
            if (storage == null)
                return;
            var positions = new List<int2>();
            foreach (Column column in storage.Columns)
                positions.Add(column.Position);
            foreach (int2 position in positions)
            {
                storage.RemoveColumn(position);
                builder.RemoveColumn(position);
            }
        }

        /// <summary>What the landscape costs right now: counts, memory and main-thread time. Cheap enough to call a few times a second.</summary>
        public LandscapeStats GetStats()
        {
            var stats = new LandscapeStats();
            if (storage == null)
                return stats;
            stats.Columns = storage.ColumnCount;
            stats.MixedSections = storage.MixedSectionCount;
            stats.BlockBytes = storage.MemoryBytes;
            builder.AddStats(ref stats);
            if (TryGetComponent(out LandscapeStreamer streamer))
                streamer.AddStats(ref stats);
            stats.MainThreadMs = timer.AverageMs;
            stats.PeakMainThreadMs = timer.PeakMs;
            stats.SectionsBuiltPerSecond = buildRate.PerSecond;
            return stats;
        }

        /// <summary>Builds every section still waiting, right away. Useful before a screenshot or enabling physics.</summary>
        public void CompleteRebuilds() => builder?.CompleteAll();

        /// <summary>Rebuilds every section, for example after changing blocks in the registry while editing.</summary>
        public void Rebuild()
        {
            builder?.MarkAllDirty();
            RebuildIfEditing();
        }

        /// <summary>The smallest box holding every block that isn't air. False when there is none.</summary>
        public bool TryGetBlockBounds(out BoundsInt bounds)
        {
            bounds = default;
            if (storage == null)
                return false;
            var min = new int3(int.MaxValue);
            var max = new int3(int.MinValue);
            foreach (Column column in storage.Columns)
            for (int y = 0; y < column.Sections.Length; y++)
            {
                SectionBlocks section = column.Sections[y];
                int3 origin = new int3(column.Position.x, y, column.Position.y) * Section.Size;
                if (section.IsUniform)
                {
                    if (section.UniformId == 0)
                        continue;
                    min = math.min(min, origin);
                    max = math.max(max, origin + Section.Size - 1);
                    continue;
                }
                for (int i = 0; i < Section.Volume; i++)
                {
                    if (section.Blocks[i] == 0)
                        continue;
                    int3 position = origin + Section.Local(i);
                    min = math.min(min, position);
                    max = math.max(max, position);
                }
            }
            if (min.x > max.x)
                return false;
            int3 size = max - min + 1;
            bounds = new BoundsInt(min.x, min.y, min.z, size.x, size.y, size.z);
            return true;
        }

        /// <summary>
        /// Groups many edits: <c>using (landscape.BatchEdits()) { ... }</c>. Outside Play Mode the
        /// sections are rebuilt once, when the batch ends, instead of after every edit. In Play Mode
        /// the edits are built within the frame budget instead of all within the next frame.
        /// </summary>
        public EditBatch BatchEdits()
        {
            batchDepth++;
            return new EditBatch(this);
        }

        public readonly struct EditBatch : IDisposable
        {
            readonly Landscape landscape;

            internal EditBatch(Landscape landscape) => this.landscape = landscape;

            public void Dispose()
            {
                if (landscape != null && --landscape.batchDepth == 0)
                    landscape.RebuildIfEditing();
            }
        }

        public Vector3Int WorldToBlock(Vector3 worldPosition) => Vector3Int.FloorToInt(transform.InverseTransformPoint(worldPosition));

        /// <summary>The center of a block, in world space.</summary>
        public Vector3 BlockToWorld(Vector3Int block) => transform.TransformPoint(block + new Vector3(0.5f, 0.5f, 0.5f));

        /// <summary>
        /// Finds the first block along a world-space ray by stepping through the grid, block by block.
        /// Needs no colliders, and follows the landscape wherever its transform moves it.
        /// </summary>
        public bool Raycast(Ray ray, float maxDistance, out BlockHit hit)
        {
            hit = default;
            if (storage == null || maxDistance <= 0)
                return false;
            Vector3 origin = transform.InverseTransformPoint(ray.origin);
            Vector3 segment = transform.InverseTransformVector(ray.direction.normalized * maxDistance);
            float length = segment.magnitude;
            if (length <= 0)
                return false;
            Vector3 direction = segment / length;

            // Amanatides & Woo: always step into whichever neighboring block the ray reaches first.
            Vector3Int block = Vector3Int.FloorToInt(origin);
            var step = new Vector3Int(Math.Sign(direction.x), Math.Sign(direction.y), Math.Sign(direction.z));
            var delta = new Vector3(Mathf.Abs(1 / direction.x), Mathf.Abs(1 / direction.y), Mathf.Abs(1 / direction.z));
            var next = new Vector3(Boundary(origin.x, direction.x, block.x), Boundary(origin.y, direction.y, block.y), Boundary(origin.z, direction.z, block.z));
            float distance = 0;
            Vector3Int normal = Vector3Int.zero;
            while (distance <= length)
            {
                ushort id = storage.GetBlock(ToInt3(block));
                if (id != 0 && table[id].Is(BlockFlags.Visible))
                {
                    hit = new BlockHit
                    {
                        Block = block,
                        Normal = normal,
                        Id = id,
                        Point = transform.TransformPoint(origin + direction * distance),
                        Distance = distance * maxDistance / length,
                    };
                    return true;
                }
                if (next.x < next.y && next.x < next.z)
                {
                    block.x += step.x;
                    distance = next.x;
                    next.x += delta.x;
                    normal = new Vector3Int(-step.x, 0, 0);
                }
                else if (next.y < next.z)
                {
                    block.y += step.y;
                    distance = next.y;
                    next.y += delta.y;
                    normal = new Vector3Int(0, -step.y, 0);
                }
                else
                {
                    block.z += step.z;
                    distance = next.z;
                    next.z += delta.z;
                    normal = new Vector3Int(0, 0, -step.z);
                }
            }
            return false;
        }

        /// <summary>Draws breaking cracks over a block. <paramref name="progress"/> runs from 0 (first crack) to 1.</summary>
        public void ShowCrack(Vector3Int block, float progress)
        {
            if (registry == null)
                return;
            cracks ??= new CrackOverlay(transform, registry);
            cracks.Show(ToInt3(block), progress);
        }

        public void HideCrack() => cracks?.Hide();

        void OnEnable() => Startup();

        void OnDisable() => Shutdown();

        void Update()
        {
            if (builder == null || !Application.isPlaying)
                return;
            timer.Begin();
            builder.Focus = Focus ?? CameraFocus();
            builder.Update();
            buildRate.Sample(builder.BuildsFinished);
            timer.End();
        }

        void LateUpdate()
        {
            if (builder == null || !Application.isPlaying)
                return;
            timer.Begin();
            builder.LateUpdate();
            timer.End();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || !isActiveAndEnabled)
                    return;
                if (Application.isPlaying) ApplySettings(true);
                else Restart();
            };
        }
#endif

        void Startup()
        {
            if (storage != null || registry == null)
                return;
            BlockLighting.Apply();
            table = registry.Bake(Allocator.Persistent);
            storage = new BlockStorage(sectionsPerColumn, table);
            builder = new SectionBuilder(transform, storage, table, registry, timer);
            ApplySettings(false);
            if (pattern != null)
                PlaceBlocks(pattern, int3.zero, true);
            builder.MarkAllDirty();
            RebuildIfEditing();
        }

        void Shutdown()
        {
            ShuttingDown?.Invoke();
            cracks?.Destroy();
            cracks = null;
            builder?.Dispose();
            builder = null;
            storage?.Dispose();
            storage = null;
            if (table.IsCreated)
                table.Dispose();
        }

        void Restart()
        {
            Shutdown();
            if (isActiveAndEnabled)
                Startup();
        }

        void ApplySettings(bool rebuild)
        {
            if (builder == null)
                return;
            builder.Settings = new BuildSettings { SmoothLighting = smoothLighting, SolidBelowWorld = solidBelowWorld };
            builder.Colliders = colliders;
            builder.BudgetMs = frameBudgetMs;
            if (!rebuild)
                return;
            builder.MarkAllDirty();
            RebuildIfEditing();
        }

        void PlaceBlocks(BlockPattern source, int3 origin, bool includeAir)
        {
            ushort[] blocks = source.GetBlocks();
            Vector3Int size = source.Size;
            int i = 0;
            for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
            for (int x = 0; x < size.x; x++, i++)
            {
                if (blocks[i] != 0 || includeAir)
                    storage.SetBlock(origin + new int3(x, y, z), blocks[i], CreatesColumns);
            }
        }

        /// <summary>Queues everything an area edit can change: the area itself, light up to 8 blocks around it, and the sky below it.</summary>
        void MarkAreaDirty(int3 min, int3 max)
        {
            bool small = math.all(max - min <= Section.Size);
            builder.MarkDirty(min - 2, max + 1, small && batchDepth == 0);
            builder.MarkDirty(new int3(min.x - Neighborhood.Border, 0, min.z - Neighborhood.Border), max - 1 + Neighborhood.Border, false);
        }

        void RebuildIfEditing()
        {
            if (!Application.isPlaying && batchDepth == 0)
                builder?.CompleteAll();
        }

        float3 CameraFocus()
        {
            Camera camera = Camera.main;
            return camera != null ? (float3)transform.InverseTransformPoint(camera.transform.position) : float3.zero;
        }

        static float Boundary(float origin, float direction, int block)
        {
            if (direction > 0) return (block + 1 - origin) / direction;
            if (direction < 0) return (origin - block) / -direction;
            return float.PositiveInfinity;
        }

        static int3 ToInt3(Vector3Int value) => new(value.x, value.y, value.z);
    }
}
