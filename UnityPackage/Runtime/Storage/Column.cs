using Unity.Collections;
using Unity.Mathematics;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>A vertical stack of sections, plus where the open sky starts above each block column.</summary>
    internal sealed class Column
    {
        public readonly SectionBlocks[] Sections;

        /// <summary>
        /// For each (x, z) at <c>x | (z &lt;&lt; 4)</c>: the lowest y from which every block up to the
        /// top of the world lets light through. Skylight falls straight down to here.
        /// </summary>
        public NativeArray<ushort> SkyStart;

        public int2 Position { get; private set; }

        public Column(int sectionCount)
        {
            Sections = new SectionBlocks[sectionCount];
            SkyStart = new NativeArray<ushort>(Section.Area, Allocator.Persistent);
        }

        /// <summary>Reuses the column at a new position. Its sections must already be uniform air.</summary>
        public void Reset(int2 position)
        {
            Position = position;
            for (int i = 0; i < SkyStart.Length; i++)
                SkyStart[i] = 0;
        }

        public void Dispose() => SkyStart.Dispose();
    }
}
