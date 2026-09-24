namespace reromanlee.BlockyMesher
{
    internal readonly struct DirectMapping
    {
        public DirectMapping(BlockCollection collection, int blockIndex, int chunkAddressShift)
        {
            this.collection = collection;
            this.blockIndex = blockIndex;
            this.chunkAddressShift = chunkAddressShift;
        }

        private readonly BlockCollection collection;
        private readonly int blockIndex;
        private readonly int chunkAddressShift;

        public RuntimeBlock this[int unsafeChunkAddress]
        {
            get => collection[chunkAddressShift + unsafeChunkAddress][blockIndex];
            set => collection[chunkAddressShift + unsafeChunkAddress][blockIndex] = value;
        }
    }
}