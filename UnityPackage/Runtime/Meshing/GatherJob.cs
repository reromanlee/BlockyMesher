using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// Assembles the 32³ neighborhood from the 3 × 3 × 3 sections around the one being built.
    /// Afterwards every neighbor of every block is a fixed step away (±1, ±32, ±1024), no matter
    /// which section it came from.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct GatherJob : IJob
    {
        /// <summary>27 × 4096 ids, neighbor n at <c>n * 4096</c>. Only filled for mixed sections.</summary>
        [ReadOnly] public NativeArray<ushort> Sections;

        /// <summary>For each of the 27 neighbors, index <c>x + z * 3 + y * 9</c>: its single block id, or -1 when mixed.</summary>
        [ReadOnly] public NativeArray<int> UniformIds;

        /// <summary>9 × 256 sky starts of the 3 × 3 columns, column n at <c>n * 256</c>.</summary>
        [ReadOnly] public NativeArray<ushort> SkyStarts;

        public int BoxBottomY;
        public bool SolidBelowWorld;

        [WriteOnly] public NativeArray<ushort> Blocks;
        [WriteOnly] public NativeArray<byte> BoxSkyStart;

        public void Execute()
        {
            // Shifting box coordinates by 8 makes them relative to the lowest neighbor section:
            // 8..39, so ">> 4" picks the neighbor (0, 1, 2) and "& 15" the position inside it.
            for (int y = 0; y < Neighborhood.Size; y++)
            {
                int neighborY = (y + 8) >> 4;
                int localY = (y + 8) & 15;
                for (int z = 0; z < Neighborhood.Size; z++)
                {
                    int neighborZ = (z + 8) >> 4;
                    int localZ = (z + 8) & 15;
                    for (int x = 0; x < Neighborhood.Size; x++)
                    {
                        int neighbor = ((x + 8) >> 4) + neighborZ * 3 + neighborY * 9;
                        int uniformId = UniformIds[neighbor];
                        Blocks[Neighborhood.Index(x, y, z)] = uniformId >= 0
                            ? (ushort)uniformId
                            : Sections[neighbor * Section.Volume + Section.Index((x + 8) & 15, localY, localZ)];
                    }
                }
            }

            for (int z = 0; z < Neighborhood.Size; z++)
            for (int x = 0; x < Neighborhood.Size; x++)
            {
                int column = ((x + 8) >> 4) + ((z + 8) >> 4) * 3;
                int skyStart = SkyStarts[column * Section.Area + (((x + 8) & 15) | (((z + 8) & 15) << 4))];

                // A column with no blocks at all is open sky even below the world, unless the world has a floor.
                int boxSkyStart = skyStart == 0 && !SolidBelowWorld ? 0 : skyStart - BoxBottomY;
                BoxSkyStart[x + z * Neighborhood.Size] = (byte)math.clamp(boxSkyStart, 0, Neighborhood.Size);
            }
        }
    }
}
