using Unity.Mathematics;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// The 16³ cube of blocks everything is split into. Blocks are stored layer by layer:
    /// x changes fastest, then z, then y, so a column of sections is one continuous array.
    /// </summary>
    public static class Section
    {
        public const int Size = 16;
        public const int Area = Size * Size;
        public const int Volume = Area * Size;

        public static int Index(int x, int y, int z) => x | (z << 4) | (y << 8);

        public static int Index(int3 local) => local.x | (local.z << 4) | (local.y << 8);

        public static int3 Local(int index) => new(index & 15, index >> 8, (index >> 4) & 15);

        /// <summary>The column holding a block. Shifting rounds down, so negative positions work too.</summary>
        public static int2 ColumnOf(int3 block) => new(block.x >> 4, block.z >> 4);
    }
}
