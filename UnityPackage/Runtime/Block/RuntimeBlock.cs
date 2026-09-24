namespace reromanlee.BlockyMesher
{
    public class RuntimeBlock
    {
        public BlockData data = null;

        // Lightmapping.
        public int skylightIndex = 0;
        public int blocklightIndex = 0;

        public RuntimeBlock(BlockData data)
        {
            this.data = data;
        }
    }
}