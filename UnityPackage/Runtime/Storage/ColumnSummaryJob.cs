using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>Empties a reused column buffer, so generation steps start from air.</summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct ClearBlocksJob : IJob
    {
        [WriteOnly] public NativeArray<ushort> Blocks;

        public void Execute()
        {
            for (int i = 0; i < Blocks.Length; i++)
                Blocks[i] = 0;
        }
    }

    /// <summary>
    /// After generation: finds which sections of the new column hold a single kind of block (those
    /// are stored as just that id), and where the open sky starts above each block column.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct ColumnSummaryJob : IJob
    {
        static readonly ProfilerMarker Marker = new("BlockyMesher.ColumnSummary");

        [ReadOnly] public NativeArray<ushort> Blocks;
        [ReadOnly] public NativeArray<BlockInfo> BlockInfos;
        public int SectionCount;

        /// <summary>Per section: its only block id, or -1 when it holds several.</summary>
        [WriteOnly] public NativeArray<int> UniformIds;

        [WriteOnly] public NativeArray<ushort> SkyStart;

        public void Execute()
        {
            using ProfilerMarker.AutoScope scope = Marker.Auto();
            for (int section = 0; section < SectionCount; section++)
            {
                int start = section * Section.Volume;
                ushort first = Blocks[start];
                int uniform = first;
                for (int i = 1; i < Section.Volume; i++)
                {
                    if (Blocks[start + i] != first)
                    {
                        uniform = -1;
                        break;
                    }
                }
                UniformIds[section] = uniform;
            }

            int height = SectionCount * Section.Size;
            for (int column = 0; column < Section.Area; column++)
            {
                int skyStart = 0;
                for (int y = height - 1; y >= 0; y--)
                {
                    ushort id = Blocks[column + (y << 8)];
                    BlockInfo info = id < BlockInfos.Length ? BlockInfos[id] : BlockInfos[0];
                    if (!info.Is(BlockFlags.LightPasses))
                    {
                        skyStart = y + 1;
                        break;
                    }
                }
                SkyStart[column] = (ushort)skyStart;
            }
        }
    }
}
