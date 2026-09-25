using System.Text;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// What one landscape costs right now, from <see cref="Landscape.GetStats"/>.
    /// Memory is worked out from the data layouts: exact for blocks, mesh buffers and edits, but
    /// Unity's own objects and PhysX's prepared collision data aren't included. Time is measured on
    /// the main thread, which is what the frame rate depends on; the Unity Profiler shows each job
    /// on the worker threads (look for markers starting with "BlockyMesher").
    /// </summary>
    public struct LandscapeStats
    {
        public int Columns;

        /// <summary>Sections that hold an array of blocks. All the others are a single block type and cost almost nothing.</summary>
        public int MixedSections;

        public int SectionMeshes;

        /// <summary>Submeshes to draw: one per section and render pass, before Unity's culling.</summary>
        public int DrawCalls;

        public long Vertices;
        public long Triangles;
        public int SectionsWithColliders;
        public int QueuedSections;
        public int BuildingSections;
        public int GeneratingColumns;

        public long BlockBytes;

        /// <summary>Vertex and index buffers on the GPU. The CPU copies are dropped once uploaded.</summary>
        public long MeshBytes;

        public long ColliderBytes;

        /// <summary>Buffers reused from build to build, and column generation buffers.</summary>
        public long WorkBufferBytes;

        /// <summary>Player edits kept for columns that may unload, with a streamer.</summary>
        public long EditBytes;

        public long TotalBytes => BlockBytes + MeshBytes + ColliderBytes + WorkBufferBytes + EditBytes;

        /// <summary>Average main-thread time per frame over the last second, in milliseconds.</summary>
        public float MainThreadMs;

        /// <summary>The worst frame of the last second, in milliseconds.</summary>
        public float PeakMainThreadMs;

        public float SectionsBuiltPerSecond;
        public float ColumnsLoadedPerSecond;

        public override string ToString()
        {
            var text = new StringBuilder();
            text.AppendLine($"Main thread  {MainThreadMs:0.00} ms per frame, worst {PeakMainThreadMs:0.00} ms");
            text.AppendLine($"Building     {SectionsBuiltPerSecond:0} sections/s, {QueuedSections} queued, {BuildingSections} running");
            if (GeneratingColumns > 0 || ColumnsLoadedPerSecond > 0)
                text.AppendLine($"Streaming    {ColumnsLoadedPerSecond:0} columns/s, {GeneratingColumns} generating");
            text.AppendLine($"Content      {Columns} columns, {SectionMeshes} meshes, {DrawCalls} draw calls, {Triangles / 1000f:0.0}k triangles");
            text.AppendLine($"Memory       {Megabytes(TotalBytes)} total");
            text.AppendLine($"  blocks     {Megabytes(BlockBytes)} ({MixedSections} sections with block arrays)");
            text.AppendLine($"  meshes     {Megabytes(MeshBytes)}");
            if (SectionsWithColliders > 0)
                text.AppendLine($"  colliders  {Megabytes(ColliderBytes)} ({SectionsWithColliders} sections)");
            text.AppendLine($"  buffers    {Megabytes(WorkBufferBytes)}");
            if (EditBytes > 0)
                text.AppendLine($"  edits      {Megabytes(EditBytes)}");
            return text.ToString();
        }

        static string Megabytes(long bytes) => $"{bytes / (1024f * 1024f):0.00} MB";
    }
}
