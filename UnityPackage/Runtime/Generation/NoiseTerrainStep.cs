using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace reromanlee.BlockyMesher.Generation
{
    /// <summary>
    /// Rolling hills from layered noise: a surface block on top, a few subsurface blocks below it,
    /// stone underneath, and optional caves carved out of the ground.
    /// </summary>
    [CreateAssetMenu(fileName = "NoiseTerrain", menuName = "BlockyMesher/Generation/Noise Terrain")]
    public sealed class NoiseTerrainStep : GenerationStep
    {
        [Header("Blocks")]
        public BlockData surface;
        public BlockData subsurface;
        [Min(0)] public int subsurfaceDepth = 3;
        public BlockData stone;

        [Tooltip("Optional floor at y = 0.")]
        public BlockData bedrock;

        [Header("Shape")]
        [Tooltip("Average height of the ground.")]
        [Min(1)] public int baseHeight = 48;

        [Tooltip("How far hills rise above and dip below the average.")]
        [Min(0)] public float amplitude = 20;

        [Tooltip("Width of the biggest hills, in blocks.")]
        [Min(1)] public float scale = 96;

        [Tooltip("Layers of ever finer detail on top of the biggest hills.")]
        [Range(1, 8)] public int octaves = 4;

        [Tooltip("How much each finer layer counts compared to the one before.")]
        [Range(0, 1)] public float persistence = 0.5f;

        [Header("Caves")]
        [Tooltip("Carve caves out of the ground. Costs noticeably more generation time than the hills alone.")]
        public bool caves;

        [Tooltip("Size of the caves, in blocks.")]
        [Min(1)] public float caveScale = 24;

        [Tooltip("Higher means fewer, smaller caves.")]
        [Range(0, 1)] public float caveThreshold = 0.45f;

        [Tooltip("Lowest y caves reach.")]
        [Min(0)] public int caveFloor = 4;

        public override JobHandle Schedule(ColumnContext column, NativeArray<ushort> blocks, JobHandle dependsOn)
        {
            return new NoiseTerrainJob
            {
                Column = column,
                Surface = Id(surface),
                Subsurface = Id(subsurface),
                SubsurfaceDepth = subsurfaceDepth,
                Stone = Id(stone),
                Bedrock = Id(bedrock),
                BaseHeight = baseHeight,
                Amplitude = amplitude,
                Scale = scale,
                Octaves = octaves,
                Persistence = persistence,
                Caves = caves,
                CaveScale = caveScale,
                CaveThreshold = caveThreshold,
                CaveFloor = caveFloor,
                Blocks = blocks,
            }.Schedule(dependsOn);
        }

        static ushort Id(BlockData block) => block != null ? (ushort)block.id : (ushort)0;
    }

    [BurstCompile(CompileSynchronously = true)]
    struct NoiseTerrainJob : IJob
    {
        public ColumnContext Column;
        public ushort Surface;
        public ushort Subsurface;
        public int SubsurfaceDepth;
        public ushort Stone;
        public ushort Bedrock;
        public int BaseHeight;
        public float Amplitude;
        public float Scale;
        public int Octaves;
        public float Persistence;
        public bool Caves;
        public float CaveScale;
        public float CaveThreshold;
        public int CaveFloor;

        public NativeArray<ushort> Blocks;

        public void Execute()
        {
            // The seed picks where in the endless noise this world sits.
            uint hash = math.hash(new uint2(Column.Seed, 0x9E3779B9));
            float2 hillOffset = new float2(hash & 0xFFFF, hash >> 16) * 0.37f;
            float3 caveOffset = new float3(hash >> 16, hash & 0xFFFF, (hash >> 8) & 0xFFFF) * 0.29f;

            for (int z = 0; z < Section.Size; z++)
            for (int x = 0; x < Section.Size; x++)
            {
                var position = new float2(Column.Origin.x + x, Column.Origin.z + z);
                int top = (int)math.round(BaseHeight + Amplitude * Hills(position / Scale + hillOffset));
                top = math.clamp(top, 1, Column.Height - 1);
                for (int y = 0; y <= top; y++)
                {
                    ushort id = y == top ? Surface : y > top - SubsurfaceDepth ? Subsurface : Stone;
                    if (Caves && y >= CaveFloor && IsCave(new float3(position.x, y * 1.6f, position.y) / CaveScale + caveOffset))
                        id = 0;
                    if (y == 0 && Bedrock != 0)
                        id = Bedrock;
                    Blocks[ColumnContext.Index(x, y, z)] = id;
                }
            }
        }

        /// <summary>Noise at a few scales added up, from -1 to 1: big hills with smaller bumps on them.</summary>
        float Hills(float2 position)
        {
            float sum = 0, weight = 1, total = 0, frequency = 1;
            for (int octave = 0; octave < Octaves; octave++)
            {
                sum += noise.snoise(position * frequency) * weight;
                total += weight;
                weight *= Persistence;
                frequency *= 2;
            }
            return sum / total;
        }

        bool IsCave(float3 position) => noise.snoise(position) > CaveThreshold;
    }
}
