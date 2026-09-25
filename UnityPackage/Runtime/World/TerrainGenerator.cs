using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>A seed and the steps that turn it into terrain, column by column.</summary>
    [CreateAssetMenu(fileName = "TerrainGenerator", menuName = "BlockyMesher/Terrain Generator")]
    public sealed class TerrainGenerator : ScriptableObject
    {
        [Tooltip("The same seed always generates the same terrain.")]
        public uint seed = 1;

        [Tooltip("Run in order for every column; later steps see what earlier ones wrote.")]
        public GenerationStep[] steps = new GenerationStep[0];

        /// <summary>Schedules every step for one column, one after another.</summary>
        public JobHandle Schedule(ColumnContext column, NativeArray<ushort> blocks, JobHandle dependsOn = default)
        {
            column.Seed = seed;
            JobHandle handle = dependsOn;
            foreach (GenerationStep step in steps)
            {
                if (step != null)
                    handle = step.Schedule(column, blocks, handle);
            }
            return handle;
        }
    }
}
