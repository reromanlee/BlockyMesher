using UnityEngine;

namespace reromanlee.BlockyMesher
{
    public static class Tables
    {
        static Tables()
        {
            cubeCases = GenerateCubeCases();
            blockData = GenerateBlockData();
            mappingSurroundingNeighbors = GenerateMappingSurroundingNeighbors();
            chunkSurroundingNeighbors = GenerateChunkSurroundingNeighbors();
            mappingFacingNeighbors = GenerateMappingFacingNeighbors();
            vertexOffsetPositions = GenerateVertexOffsetPositions();
            blockTextureUVs = GenerateBlockTextureUVs();
        }

        // ########## CONST VALUES ##########

        public const int blockLengthX = 8;
        public const int blockLengthXY = 64;
        public const int blockLengthXYZ = 512;

        public const string chunkLayerName = "Blocks";

        public const int mappingLengthX = 12;
        public const int mappingLengthXY = 144;
        public const int mappingLengthXYZ = 1728;

        public const int mappingBorderLength = 2;
        public const int mappingNegativeBorder = 2;
        public const int mappingPositiveBorder = 10;
        public const int mappingDifference = 73;

        public const int chunkVertexCount = 4 * 6 * 512;
        public const int chunkIndexCount = 2 * 3 * 6 * 512;

        public const int chunkLengthX = 2048;
        public const int chunkLengthY = 128;
        public const int chunkLengthZ = 2048;

        public const int chunkHalfLengthX = chunkLengthX / 2;
        public const int chunkHalfLengthY = chunkLengthY / 2;
        public const int chunkHalfLengthZ = chunkLengthZ / 2;

        public const int chunkLengthXY = chunkLengthX * chunkLengthY;

        // Use float instead of integer to
        // correctly process index division.
        public const float whiteLightIndex = 7;

        // ########## CHUNK ADDRESS ##########

        /// <summary>
        /// Ensure x, y, and z are within their specified ranges,
        /// x: [-1024, 1023], y: [-64, 63], z: [-1024, 1023].
        /// </summary>
        public static int CoordsToChunkAddress(int x, int y, int z)
        {
            x += chunkHalfLengthX;
            y += chunkHalfLengthY;
            z += chunkHalfLengthZ;
            return x + y * chunkLengthX + z * chunkLengthXY;
        }

        public static int CoordsToChunkShift(int x, int y, int z)
        {
            return x + y * chunkLengthX + z * chunkLengthXY;
        }

        public static int ChunkAddressToX(int chunkAddress)
        {
            return (chunkAddress % chunkLengthX) - chunkHalfLengthX;
        }

        public static int ChunkAddressToY(int chunkAddress)
        {
            return (chunkAddress / chunkLengthX % chunkLengthY) - chunkHalfLengthY;
        }

        public static int ChunkAddressToZ(int chunkAddress)
        {
            return (chunkAddress / chunkLengthXY) - chunkHalfLengthZ;
        }

        // ########## BLOCK INDICES ##########

        /// <summary>
        /// Ensure x, y, and z are within their specified ranges,
        /// x: [0, 7], y: [0, 7], z: [0, 7].
        /// </summary>
        public static int CoordsToBlockIndex(int x, int y, int z)
        {
            return x + y * blockLengthX + z * blockLengthXY;
        }

        // ########## MAPPING ADDRESS ##########

        /// <summary>
        /// Ensure x, y, and z are within their specified ranges,
        /// x: [0, 11], y: [0, 11], z: [0, 11].
        /// </summary>
        public static int CoordsToMappingAddress(int x, int y, int z)
        {
            return x + y * mappingLengthX + z * mappingLengthXY;
        }

        public static int MappingAddressToX(int mappingAddress)
        {
            return mappingAddress % mappingLengthX;
        }

        public static int MappingAddressToY(int mappingAddress)
        {
            return mappingAddress % mappingLengthXY / mappingLengthX;
        }

        public static int MappingAddressToZ(int mappingAddress)
        {
            return mappingAddress / mappingLengthXY;
        }

        // ########## CUBE VERTICES ##########

        public static readonly Vector3[] cubeVertices = {
            new(0, 0, 0), // Start or +0.
            new(1, 0, 0), // Bottom Right or +X.
            new(0, 0, 1), // Bottom Front or +Z.
            new(1, 0, 1), // Bottom Right Front or +X +Z.
            new(0, 1, 0), // Top or +Y.
            new(1, 1, 0), // Top Right or +X +Y.
            new(0, 1, 1), // Top Front or +Y +Z.
            new(1, 1, 1), // Top Right Front or +X +Y +Z.
        };

        // ########## CUBE FACES ##########

        public static readonly Quad[] cubeFaces = {
            // X-axis.
            new(1, 5, 7, 3), // 32 or +X.
            new(2, 6, 4, 0), // 16 or -X.
            // Because front is considered relative to camera, not to object,
            // Z-axis should be interpreted in inversed order to be correct.
            new(3, 7, 6, 2), // 8 or -Z.
            new(0, 4, 5, 1), // 4 or +Z.
            // Y-axis.
            new(4, 6, 7, 5), // 2 or +Y.
            new(2, 0, 1, 3)  // 1 or -Y.
        };

        // ########## VERTEX LIGHTMAP GROUPS ##########

        public static readonly int[] vertexLightmapGroups = {
            // Face 0 (+X).
            14, 5, 11, 4, 10, 2, 1,     // Vertex 0.
            14, 5, 17, 4, 16, 8, 7,     // Vertex 1.
            14, 23, 17, 22, 16, 26, 25, // Vertex 2.
            14, 23, 11, 22, 10, 20, 19, // Vertex 3.
            // Face 1 (-X).
            12, 9, 21, 10, 22, 18, 19,  // Vertex 4.
            12, 15, 21, 16, 22, 24, 25, // Vertex 5.
            12, 15, 3, 16, 4, 6, 7,     // Vertex 6.
            12, 9, 3, 10, 4, 0, 1,      // Vertex 7.
            // Face 2 (-Z).
            22, 23, 19, 14, 10, 20, 11, // Vertex 8.
            22, 23, 25, 14, 16, 26, 17, // Vertex 9.
            22, 21, 25, 12, 16, 24, 15, // Vertex 10.
            22, 21, 19, 12, 10, 18, 9,  // Vertex 11.
            // Face 3 (+Z).
            4, 3, 1, 12, 10, 0, 9,      // Vertex 12.
            4, 3, 7, 12, 16, 6, 15,     // Vertex 13.
            4, 5, 7, 14, 16, 8, 17,     // Vertex 14.
            4, 5, 1, 14, 10, 2, 11,     // Vertex 15.
            // Face 4 (+Y).
            16, 15, 7, 12, 4, 6, 3,     // Vertex 16.
            16, 15, 25, 12, 22, 24, 21, // Vertex 17.
            16, 17, 25, 14, 22, 26, 23, // Vertex 18.
            16, 17, 7, 14, 4, 8, 5,     // Vertex 19.
            // Face 5 (-Y).
            10, 19, 9, 22, 12, 18, 21,  // Vertex 20.
            10, 1, 9, 4, 12, 0, 3,      // Vertex 21.
            10, 1, 11, 4, 14, 2, 5,     // Vertex 22.
            10, 19, 11, 22, 14, 20, 23  // Vertex 23.
        };

        public static readonly int[][] vertexLightmapConnections = {
            new int[0], // No connections.
            new int[0], // D7 
            new int[0], // C6 
            new int[0], // C6 D7 
            new int[0], // C5 
            new int[0], // C5 D7 
            new int[0], // C5 C6 
            new int[0], // C5 C6 D7 
            new int[0], // C4 
            new int[0], // C4 D7 
            new int[0], // C4 C6 
            new int[0], // C4 C6 D7 
            new int[0], // C4 C5 
            new int[0], // C4 C5 D7 
            new int[0], // C4 C5 C6 
            new int[0], // C4 C5 C6 D7 
            new int[] { 2 }, // B3 
            new int[] { 2 }, // B3 D7 
            new int[] { 2, 5 }, // B3 C6 
            new int[] { 2, 5, 6 }, // B3 C6 D7 
            new int[] { 2, 4 }, // B3 C5 
            new int[] { 2, 4, 6 }, // B3 C5 D7 
            new int[] { 2, 5, 4 }, // B3 C5 C6 
            new int[] { 2, 5, 4, 6 }, // B3 C5 C6 D7 
            new int[] { 2 }, // B3 C4 
            new int[] { 2 }, // B3 C4 D7 
            new int[] { 2, 5 }, // B3 C4 C6 
            new int[] { 2, 5, 6, 3 }, // B3 C4 C6 D7 
            new int[] { 2, 4 }, // B3 C4 C5 
            new int[] { 2, 4, 6, 3 }, // B3 C4 C5 D7 
            new int[] { 2, 5, 4 }, // B3 C4 C5 C6 
            new int[] { 2, 5, 6, 4, 3 }, // B3 C4 C5 C6 D7 
            new int[] { 1 }, // B2 
            new int[] { 1 }, // B2 D7 
            new int[] { 1, 5 }, // B2 C6 
            new int[] { 1, 5, 6 }, // B2 C6 D7 
            new int[] { 1 }, // B2 C5 
            new int[] { 1 }, // B2 C5 D7 
            new int[] { 1, 5 }, // B2 C5 C6 
            new int[] { 1, 5, 6, 4 }, // B2 C5 C6 D7 
            new int[] { 1, 3 }, // B2 C4 
            new int[] { 1, 3, 6 }, // B2 C4 D7 
            new int[] { 1, 3, 5 }, // B2 C4 C6 
            new int[] { 1, 3, 5, 6 }, // B2 C4 C6 D7 
            new int[] { 1, 3 }, // B2 C4 C5 
            new int[] { 1, 3, 6, 4 }, // B2 C4 C5 D7 
            new int[] { 1, 3, 5 }, // B2 C4 C5 C6 
            new int[] { 1, 3, 5, 6, 4 }, // B2 C4 C5 C6 D7 
            new int[] { 1, 2 }, // B2 B3 
            new int[] { 1, 2 }, // B2 B3 D7 
            new int[] { 1, 2, 5 }, // B2 B3 C6 
            new int[] { 1, 2, 5, 6 }, // B2 B3 C6 D7 
            new int[] { 1, 2, 4 }, // B2 B3 C5 
            new int[] { 1, 2, 4, 6 }, // B2 B3 C5 D7 
            new int[] { 1, 2, 4, 5 }, // B2 B3 C5 C6 
            new int[] { 1, 2, 4, 5, 6 }, // B2 B3 C5 C6 D7 
            new int[] { 1, 2, 3 }, // B2 B3 C4 
            new int[] { 1, 2, 3, 6 }, // B2 B3 C4 D7 
            new int[] { 1, 2, 3, 5 }, // B2 B3 C4 C6 
            new int[] { 1, 2, 3, 5, 6 }, // B2 B3 C4 C6 D7 
            new int[] { 1, 2, 3, 4 }, // B2 B3 C4 C5 
            new int[] { 1, 2, 3, 4, 6 }, // B2 B3 C4 C5 D7 
            new int[] { 1, 2, 3, 4, 5 }, // B2 B3 C4 C5 C6 
            new int[] { 1, 2, 3, 4, 5, 6 }  // B2 B3 C4 C5 C6 D7 
        };

        // ########## FACE OCCLUSION GROUPS ##########

        public static readonly int[] faceOcclusionGroups = new int[54] {
            // X-axis.
            2, 11, 20, 5, 14, 23, 8, 17, 26, // +X.
            18, 9, 0, 21, 12, 3, 24, 15, 6, // -X.
            // Z-axis.
            20, 19, 18, 23, 22, 21, 26, 25, 24, // -Z.
            0, 1, 2, 3, 4, 5, 6, 7, 8, // +Z.
            // Y-axis.
            6, 7, 8, 15, 16, 17, 24, 25, 26, // +Y.
            18, 19, 20, 9, 10, 11, 0, 1, 2, // -Y.
        };

        // ########## CUBE CASES ##########

        public static readonly CubeCase[] cubeCases;

        private static CubeCase[] GenerateCubeCases()
        {
            CubeCase[] cubeCases = new CubeCase[64];
            for (int x = 0; x < cubeCases.Length; x++)
            {
                cubeCases[x] = new CubeCase(x, cubeVertices, cubeFaces);
            }
            return cubeCases;
        }

        // ########## BLOCK DATA ##########

        public static BlockData[] blockData;

        private static BlockData[] GenerateBlockData()
        {
            return Resources.LoadAll<BlockData>("BlockData");
        }

        // ########## MAPPING SURROUNDING NEIGHBORS ##########

        public static readonly int[] mappingSurroundingNeighbors;

        private static int[] GenerateMappingSurroundingNeighbors()
        {
            int[] mappingSurroundingNeighbors = new int[27];
            for (int y = 0; y <= 2; y++)
            {
                for (int z = 0; z <= 2; z++)
                {
                    for (int x = 0; x <= 2; x++)
                    {
                        int mappingAddress = CoordsToMappingAddress(x - 1, y - 1, z - 1);
                        mappingSurroundingNeighbors[x + y * 3 + z * 9] = mappingAddress;
                    }
                }
            }
            return mappingSurroundingNeighbors;
        }

        // ########## MAPPING FACING NEIGHBORS ##########

        public static readonly int[] mappingFacingNeighbors;

        private static int[] GenerateMappingFacingNeighbors()
        {
            return new int[6] {
                CoordsToMappingAddress(1, 0, 0),
                CoordsToMappingAddress(-1, 0, 0),
                CoordsToMappingAddress(0, 1, 0),
                CoordsToMappingAddress(0, -1, 0),
                CoordsToMappingAddress(0, 0, 1),
                CoordsToMappingAddress(0, 0, -1)
            };
        }

        // ########## CHUNK SURROUNDING NEIGHBORS ##########

        public static readonly int[] chunkSurroundingNeighbors;

        private static int[] GenerateChunkSurroundingNeighbors()
        {
            int[] chunkSurroundingNeighbors = new int[27];
            for (int y = 0; y <= 2; y++)
            {
                for (int z = 0; z <= 2; z++)
                {
                    for (int x = 0; x <= 2; x++)
                    {
                        int chunkShift = CoordsToChunkShift(x - 1, y - 1, z - 1);
                        chunkSurroundingNeighbors[x + y * 3 + z * 9] = chunkShift;
                    }
                }
            }
            return chunkSurroundingNeighbors;
        }

        // ########## VERTEX OFFSET POSITIONS ##########

        public static readonly Vector3[] vertexOffsetPositions;

        private static Vector3[] GenerateVertexOffsetPositions()
        {
            Vector3[] vertexOffsetPositions = new Vector3[blockLengthXYZ];
            for (int y = 0; y < blockLengthX; y++)
            {
                for (int x = 0; x < blockLengthX; x++)
                {
                    for (int z = 0; z < blockLengthX; z++)
                    {
                        int blockIndex = CoordsToBlockIndex(x, y, z);
                        vertexOffsetPositions[blockIndex] = new Vector3(x, y, z);
                    }
                }
            }
            return vertexOffsetPositions;
        }

        // ########## BLOCK TEXTURE UVS ##########

        public static readonly Vector2[][] blockTextureUVs;

        private static Vector2[][] GenerateBlockTextureUVs()
        {
            float cluster = 1.0f / 16.0f;
            Vector2[][] atlasMapping = new Vector2[256][];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float clusterX = cluster * x;
                    float clusterY = cluster * y;
                    atlasMapping[x + y * 16] = new Vector2[4] {
                        new(clusterX, clusterY),
                        new(clusterX, clusterY + cluster),
                        new(clusterX + cluster, clusterY + cluster),
                        new(clusterX + cluster, clusterY)
                    };
                }
            }
            return atlasMapping;
        }

        // ########## VERTEX SURROUNDING GROUPS ##########

        public static readonly int[] vertexSurroudingGroups = new int[64] {
            0, 1, 9, 10, 3, 4, 12, 13,
            1, 2, 10, 11, 4, 5, 13, 14,
            9, 10, 18, 19, 12, 13, 21, 22,
            10, 11, 19, 20, 13, 14, 22, 23,
            3, 4, 12, 13, 6, 7, 15, 16,
            4, 5, 13, 14, 7, 8, 16, 17,
            12, 13, 21, 22, 15, 16, 24 ,25,
            13, 14, 22, 23, 16, 17, 25, 26
        };
    }
}