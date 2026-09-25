using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>The column a <see cref="GenerationStep"/> fills.</summary>
    public struct ColumnContext
    {
        /// <summary>The column, counted in columns: block x = Position.x * 16 + local x.</summary>
        public int2 Position;

        /// <summary>Height of the column in blocks.</summary>
        public int Height;

        public uint Seed;

        /// <summary>Block coordinates of the column's lowest corner.</summary>
        public int3 Origin => new(Position.x * Section.Size, 0, Position.y * Section.Size);

        /// <summary>Index of a block inside the column's array: x changes fastest, then z, then y.</summary>
        public static int Index(int x, int y, int z) => x | (z << 4) | (y << 8);
    }

    /// <summary>
    /// One step of generating terrain: it fills or changes the blocks of a column. A
    /// <see cref="TerrainGenerator"/> runs its steps in order, each one seeing what the previous
    /// ones wrote, so terrain comes first and later steps can add caves, trees or structures.
    ///
    /// A step must be deterministic: the same seed and column always give the same blocks. Player
    /// edits are stored as differences from what generation produces, and rely on it.
    /// </summary>
    public abstract class GenerationStep : ScriptableObject
    {
        /// <summary>
        /// Schedules the work for one column, after <paramref name="dependsOn"/>. A Burst job is
        /// fastest. Plain C# works too: call <c>dependsOn.Complete()</c> first, do the work, and
        /// return <c>default</c>.
        /// </summary>
        /// <param name="blocks">16 × Height × 16 block ids, see <see cref="ColumnContext.Index"/>.</param>
        public abstract JobHandle Schedule(ColumnContext column, NativeArray<ushort> blocks, JobHandle dependsOn);
    }
}
