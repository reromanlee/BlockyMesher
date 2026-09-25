using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Meshing
{
    /// <summary>
    /// Lights the whole neighborhood from scratch, which is why light is never stored anywhere.
    /// Skylight: blocks above their column's sky start get the full level, which then spreads
    /// sideways and down, one level darker per block. Block light spreads the same way from
    /// light-emitting blocks and carries their color; where lights overlap, the brightest wins.
    /// Blocks that stop light still receive it (their faces are lit by it) but don't pass it on.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    internal struct LightJob : IJob
    {
        public const int MaxLevel = 7;

        [ReadOnly] public NativeArray<ushort> Blocks;
        [ReadOnly] public NativeArray<byte> BoxSkyStart;
        [ReadOnly] public NativeArray<BlockInfo> BlockInfos;
        public int BoxBottomY;
        public bool SolidBelowWorld;

        public NativeArray<byte> Flags;
        public NativeArray<byte> Sky;

        /// <summary>Level in the low 3 bits, light palette index above them.</summary>
        public NativeArray<byte> BlockLight;

        public NativeArray<ushort> Queue;

        public void Execute()
        {
            var emitters = new NativeList<int>(Allocator.Temp);
            PrepareCells(emitters);
            SpreadSkylight();
            SpreadBlockLight(emitters);
        }

        void PrepareCells(NativeList<int> emitters)
        {
            for (int y = 0; y < Neighborhood.Size; y++)
            {
                bool belowWorld = SolidBelowWorld && BoxBottomY + y < 0;
                for (int z = 0; z < Neighborhood.Size; z++)
                for (int x = 0; x < Neighborhood.Size; x++)
                {
                    int cell = Neighborhood.Index(x, y, z);
                    ushort id = Blocks[cell];
                    BlockInfo info = id < BlockInfos.Length ? BlockInfos[id] : BlockInfos[0];
                    Flags[cell] = belowWorld ? (byte)(BlockFlags.Opaque | BlockFlags.Collidable) : (byte)info.Flags;
                    Sky[cell] = (byte)(y >= BoxSkyStart[x + z * Neighborhood.Size] ? MaxLevel : 0);
                    BlockLight[cell] = 0;
                    if (info.Emission > 0 && !belowWorld)
                        emitters.Add((info.Emission << 24) | (info.LightColor << 16) | cell);
                }
            }
        }

        void SpreadSkylight()
        {
            // Only sky blocks beside a column whose sky starts higher can light anything. Everywhere
            // else their neighbors are sky themselves, or the block that stopped the light.
            int tail = 0;
            for (int z = 0; z < Neighborhood.Size; z++)
            for (int x = 0; x < Neighborhood.Size; x++)
            {
                int start = SkyStartAt(x, z);
                int end = math.max(math.max(SkyStartAt(x - 1, z), SkyStartAt(x + 1, z)), math.max(SkyStartAt(x, z - 1), SkyStartAt(x, z + 1)));
                for (int y = start; y < end; y++)
                    Queue[tail++] = (ushort)Neighborhood.Index(x, y, z);
            }

            // Every seed is at full level, so first come is brightest: each block is lit once.
            int head = 0;
            while (head < tail)
            {
                int cell = Queue[head++];
                int level = Sky[cell] - 1;
                if (level <= 0)
                    continue;
                int3 position = Neighborhood.Position(cell);
                if (position.x > 0) LightSky(cell - Neighborhood.StepX, level, ref tail);
                if (position.x < Neighborhood.Size - 1) LightSky(cell + Neighborhood.StepX, level, ref tail);
                if (position.y > 0) LightSky(cell - Neighborhood.StepY, level, ref tail);
                if (position.y < Neighborhood.Size - 1) LightSky(cell + Neighborhood.StepY, level, ref tail);
                if (position.z > 0) LightSky(cell - Neighborhood.StepZ, level, ref tail);
                if (position.z < Neighborhood.Size - 1) LightSky(cell + Neighborhood.StepZ, level, ref tail);
            }
        }

        void SpreadBlockLight(NativeList<int> emitters)
        {
            if (emitters.Length == 0)
                return;

            // Handling one level at a time, brightest first, means a block's first light is its
            // final one: each block is lit (and queued) once, and ties keep the first color.
            emitters.Sort();
            int next = emitters.Length - 1;
            int head = 0;
            int tail = 0;
            for (int level = MaxLevel; level > 0; level--)
            {
                while (next >= 0 && emitters[next] >> 24 == level)
                {
                    int emitter = emitters[next--];
                    LightBlock(emitter & 0xFFFF, level, (emitter >> 16) & 0xFF, true, ref tail);
                }

                int levelEnd = tail;
                while (head < levelEnd)
                {
                    int cell = Queue[head++];
                    if (level == 1)
                        continue;
                    int color = BlockLight[cell] >> 3;
                    int3 position = Neighborhood.Position(cell);
                    if (position.x > 0) LightBlock(cell - Neighborhood.StepX, level - 1, color, false, ref tail);
                    if (position.x < Neighborhood.Size - 1) LightBlock(cell + Neighborhood.StepX, level - 1, color, false, ref tail);
                    if (position.y > 0) LightBlock(cell - Neighborhood.StepY, level - 1, color, false, ref tail);
                    if (position.y < Neighborhood.Size - 1) LightBlock(cell + Neighborhood.StepY, level - 1, color, false, ref tail);
                    if (position.z > 0) LightBlock(cell - Neighborhood.StepZ, level - 1, color, false, ref tail);
                    if (position.z < Neighborhood.Size - 1) LightBlock(cell + Neighborhood.StepZ, level - 1, color, false, ref tail);
                }
            }
        }

        int SkyStartAt(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Neighborhood.Size || z >= Neighborhood.Size)
                return 0;
            return BoxSkyStart[x + z * Neighborhood.Size];
        }

        void LightSky(int cell, int level, ref int tail)
        {
            if (Sky[cell] >= level)
                return;
            Sky[cell] = (byte)level;
            if (PassesLight(cell))
                Queue[tail++] = (ushort)cell;
        }

        void LightBlock(int cell, int level, int color, bool isEmitter, ref int tail)
        {
            if ((BlockLight[cell] & 7) >= level)
                return;
            BlockLight[cell] = (byte)(level | (color << 3));
            if (isEmitter || PassesLight(cell))
                Queue[tail++] = (ushort)cell;
        }

        bool PassesLight(int cell) => (Flags[cell] & (byte)BlockFlags.LightPasses) != 0;
    }
}
