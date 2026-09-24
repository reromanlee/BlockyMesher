using System.Collections.Generic;

namespace reromanlee.BlockyMesher
{
    public class BlockCollection
    {
        public BlockCollection(BlockData[] blockDataTable)
        {
            this.blockDataTable = blockDataTable;
            directMapping = GenerateDirectMapping();
            blockDictionary = new();
        }

        private readonly BlockData[] blockDataTable;
        private readonly DirectMapping[] directMapping;
        private readonly Dictionary<int, RuntimeBlock[]> blockDictionary;

        // ########## INDEXERS ##########

        public RuntimeBlock[] this[int chunkAddress]
        {
            get => blockDictionary[chunkAddress];
        }

        public RuntimeBlock this[int chunkAddress, int mappingIndex]
        {
            get => directMapping[mappingIndex][chunkAddress];
            set => directMapping[mappingIndex][chunkAddress] = value;
        }

        public RuntimeBlock this[int chunkAddress, int x, int y, int z]
        {
            get
            {
                int mappingIndex = Tables.CoordsToMappingAddress(x + 2, y + 2, z + 2);
                return directMapping[mappingIndex][chunkAddress];
            }
            set
            {
                int mappingIndex = Tables.CoordsToMappingAddress(x + 2, y + 2, z + 2);
                directMapping[mappingIndex][chunkAddress] = value;
            }
        }

        // ########## FUNCTIONS ##########

        public void Initialize(int chunkAddress, int defaultBlockID)
        {
            if (blockDictionary.ContainsKey(chunkAddress))
            {
                return;
            }
            RuntimeBlock[] blockArray = new RuntimeBlock[Tables.blockLengthXYZ];
            BlockData blockData = blockDataTable[defaultBlockID];
            for (int x = 0; x < Tables.blockLengthXYZ; x++)
            {
                blockArray[x] = new(blockData);
            }
            blockDictionary[chunkAddress] = blockArray;
        }

        public void Initialize(int chunkAddress, int[] blockIDs)
        {
            if (blockDictionary.ContainsKey(chunkAddress))
            {
                return;
            }
            RuntimeBlock[] blockArray = new RuntimeBlock[Tables.blockLengthXYZ];
            for (int x = 0; x < Tables.blockLengthXYZ; x++)
            {
                BlockData blockData = blockDataTable[blockIDs[x]];
                blockArray[x] = new(blockData);
            }
            blockDictionary[chunkAddress] = blockArray;
        }

        public void InitializeWithSurrounding(int chunkAddress, int defaultBlockID)
        {
            // Initialize chunk with its surrounding chunks,
            // essentially representing 3x3x3 matrix,
            // where address position is in center.
            for (int x = 0; x < 27; x++)
            {
                Initialize(Tables.chunkSurroundingNeighbors[x] + chunkAddress, defaultBlockID);
            }
        }

        // ########## DIRECT MAPPING ##########

        private DirectMapping[] GenerateDirectMapping()
        {
            DirectMapping[] mapping = new DirectMapping[Tables.mappingLengthXYZ];
            for (int address = 0; address < Tables.mappingLengthXYZ; address++)
            {
                mapping[address] = DirectMappingFromAddress(address);
            }
            return mapping;
        }

        private DirectMapping DirectMappingFromAddress(int mappingAddress)
        {
            // Parse mapping position from mapping address.
            int x = Tables.MappingAddressToX(mappingAddress);
            int y = Tables.MappingAddressToY(mappingAddress);
            int z = Tables.MappingAddressToZ(mappingAddress);
            // Convert mapping position into array position.
            int arrayX = x - Tables.mappingBorderLength;
            int arrayY = y - Tables.mappingBorderLength;
            int arrayZ = z - Tables.mappingBorderLength;
            // Define default chunk position offset.
            int offsetX = 0;
            int offsetY = 0;
            int offsetZ = 0;
            // Negative border by X-axis.
            if (x < Tables.mappingNegativeBorder)
            {
                // Move to -X address.
                arrayX = Tables.blockLengthX - x - 1;
                offsetX = -1;
            }
            // Positive border by X-axis.
            else if (x >= Tables.mappingPositiveBorder)
            {
                // Move to +X address.
                arrayX = x % Tables.mappingPositiveBorder;
                offsetX = 1;
            }
            // Negative border by Y-axis.
            if (y < Tables.mappingNegativeBorder)
            {
                // Move to -Y address.
                arrayY = Tables.blockLengthX - y - 1;
                offsetY = -1;
            }
            // Positive border by Y-axis.
            else if (y >= Tables.mappingPositiveBorder)
            {
                // Move to +Y address.
                arrayY = y % Tables.mappingPositiveBorder;
                offsetY = 1;
            }
            // Negative border by Z-axis.
            if (z < Tables.mappingNegativeBorder)
            {
                // Move to -Z address.
                arrayZ = Tables.blockLengthX - z - 1;
                offsetZ = -1;
            }
            // Positive border by Z-axis.
            else if (z >= Tables.mappingPositiveBorder)
            {
                // Move to +Z address.
                arrayZ = z % Tables.mappingPositiveBorder;
                offsetZ = 1;
            }
            int blockIndex = Tables.CoordsToBlockIndex(arrayX, arrayY, arrayZ);
            int chunkAddressShift = Tables.CoordsToChunkShift(offsetX, offsetY, offsetZ);
            return new(this, blockIndex, chunkAddressShift);
        }
    }
}