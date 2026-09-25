using System;

namespace reromanlee.BlockyMesher
{
    [Flags]
    internal enum BlockFlags : byte
    {
        None = 0,
        Visible = 1 << 0,
        Opaque = 1 << 1,
        LightPasses = 1 << 2,
        Collidable = 1 << 3,
        HideSameNeighbor = 1 << 4,
    }

    /// <summary>Everything the jobs need to know about one block type, in 4 bytes.</summary>
    internal struct BlockInfo
    {
        public static BlockInfo Air => new() { Flags = BlockFlags.LightPasses };

        public BlockFlags Flags;
        public RenderPass Pass;
        public byte Emission;
        public byte LightColor;

        public bool Is(BlockFlags flag) => (Flags & flag) != 0;
    }
}
