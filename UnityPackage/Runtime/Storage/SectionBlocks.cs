using Unity.Collections;

namespace reromanlee.BlockyMesher.Storage
{
    /// <summary>
    /// The blocks of one section. Most sections are a single block type (open sky, deep stone),
    /// and those store just that id instead of 4096 copies of it.
    /// </summary>
    internal struct SectionBlocks
    {
        public ushort UniformId;
        public NativeArray<ushort> Blocks;

        public bool IsUniform => !Blocks.IsCreated;

        public ushort this[int index] => IsUniform ? UniformId : Blocks[index];
    }
}
