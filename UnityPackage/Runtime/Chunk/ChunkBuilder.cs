using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    public class ChunkBuilder
    {
        private readonly Material blockMaterial;
        private readonly BlockData[] blockDataTable;

        public ChunkBuilder(Material blockMaterial, BlockData[] blockDataTable)
        {
            this.blockMaterial = blockMaterial;
            this.blockDataTable = blockDataTable;
            blockCollection = new(blockDataTable);
            chunkCollection = new();
            // Create array pools for memory management.
            integerPool = ArrayPool<int>.Shared;
            vector2Pool = ArrayPool<Vector2>.Shared;
            vector3Pool = ArrayPool<Vector3>.Shared;
        }

        // ########## INDEXERS ##########

        public BlockData this[int chunkAddress, int x, int y, int z]
        {
            get => blockCollection[chunkAddress, x, y, z].data;
            set => blockCollection[chunkAddress, x, y, z].data = value;
        }

        // ########## FIELDS ##########

        private readonly BlockCollection blockCollection;
        private readonly Dictionary<int, ChunkRenderer> chunkCollection;

        private readonly ArrayPool<int> integerPool;
        private readonly ArrayPool<Vector2> vector2Pool;
        private readonly ArrayPool<Vector3> vector3Pool;

        // ########## FUNCTIONS ##########

        public void InitializeBlockCollection(int chunkAddress, int[] blockIDs)
        {
            blockCollection.Initialize(chunkAddress, blockIDs);
        }

        public void CreateChunk(int chunkAddress, int defaultBlockID, Transform parent)
        {
            // Avoid duplicate chunks.
            if (chunkCollection.ContainsKey(chunkAddress)) return;
            // Create a new chunk game object.
            ChunkRenderer chunkRenderer = new(chunkAddress, parent, blockMaterial);
            chunkCollection[chunkAddress] = chunkRenderer;
            // Initialize chunk matrix with specified block type.
            blockCollection.InitializeWithSurrounding(chunkAddress, defaultBlockID);
        }

        // TODO: implement light quality settings, classic lighting and smooth lighting.
        public void RecalculateChunk(int chunkAddress)
        {

            // Dedicate arrays with maximum possible length.
            int[] indices = integerPool.Rent(Tables.chunkIndexCount);
            Vector3[] vertices = vector3Pool.Rent(Tables.chunkVertexCount);
            Vector2[] blockTexture = vector2Pool.Rent(Tables.chunkVertexCount);
            Vector2[] lightmap = vector2Pool.Rent(Tables.chunkVertexCount);

            // Define count values to trim
            // output buffers into pure data.
            int vertexCount = 0;
            int indexCount = 0;
            int blockTextureCount = 0;
            int lightmapCount = 0;

            // Iterate through all blocks to collect visual information.
            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int z = 0; z < 8; z++)
                    {

                        int mappingIndex = Tables.CoordsToMappingAddress(x + 2, y + 2, z + 2);
                        RuntimeBlock runtimeBlock = blockCollection[chunkAddress, mappingIndex];
                        BlockData blockData = runtimeBlock.data;

                        // Avoid spending CPU time on air blocks.
                        if (blockData.id == 0) continue;

                        // Collect surrounding block references.
                        RuntimeBlock[] surroundingBlocks = ResolveSurroundingBlocks(chunkAddress, mappingIndex);

                        // Resolve cube culling configuration.
                        int blockIndex = Tables.CoordsToBlockIndex(x, y, z);
                        int configuration = ResolveConfiguration(surroundingBlocks);
                        CubeCase cubeCase = Tables.cubeCases[configuration];

                        // Assign cube vertices to array.
                        int caseVertexCount = cubeCase.vertices.Length;
                        Vector3 vertexPositionOffset = Tables.vertexOffsetPositions[blockIndex];
                        for (int i = 0; i < caseVertexCount; i++)
                        {
                            vertices[vertexCount + i] = cubeCase.vertices[i] + vertexPositionOffset;
                        }

                        // Assign cube indices to array.
                        int caseIndexCount = cubeCase.indices.Length;
                        for (int i = 0; i < caseIndexCount; i++)
                        {
                            indices[indexCount + i] = cubeCase.indices[i] + vertexCount;
                        }

                        // Assign block texture to array.
                        int caseTextureCount = cubeCase.faces.Length;
                        for (int i = 0; i < caseTextureCount; i++)
                        {
                            int textureID = blockData.textureIds[cubeCase.faces[i]];
                            Vector2[] textureUVs = Tables.blockTextureUVs[textureID];
                            blockTexture[blockTextureCount + 0] = textureUVs[0];
                            blockTexture[blockTextureCount + 1] = textureUVs[1];
                            blockTexture[blockTextureCount + 2] = textureUVs[2];
                            blockTexture[blockTextureCount + 3] = textureUVs[3];
                            blockTextureCount += 4;
                        }

                        // Assign lightmap intensity to array.
                        int caseLightmapCount = cubeCase.vertices.Length;
                        for (int i = 0; i < caseLightmapCount; i++)
                        {
                            // Based on vertex type we can acquire
                            // eight blocks which are surrounding vertex.
                            int vertexType = cubeCase.vertexTypes[i];
                            int skylightIndex = ResolveSkylightIndex(surroundingBlocks, vertexType);
                            float skylightIntensity = skylightIndex / Tables.whiteLightIndex;
                            lightmap[lightmapCount + i] = new(skylightIntensity, 0);
                        }

                        // Assign occlusion to array.
                        int caseOcclusionCount = cubeCase.faces.Length;
                        for (int i = 0; i < caseOcclusionCount; i++)
                        {
                            int faceOcclusionType = ResolveOcclusionConfiguration(surroundingBlocks, cubeCase.faces[i]);

                        }

                        // Increase counters.
                        vertexCount += caseVertexCount;
                        indexCount += caseIndexCount;
                        lightmapCount += caseLightmapCount;

                    }
                }
            }

            // Trim vertex array.
            Vector3[] trimVertices = new Vector3[vertexCount];
            Array.Copy(vertices, trimVertices, vertexCount);
            vector3Pool.Return(vertices);

            // Trim index array.
            int[] trimIndices = new int[indexCount];
            Array.Copy(indices, trimIndices, indexCount);
            integerPool.Return(indices);

            // Trim texture coords array.
            Vector2[] trimBlockTexture = new Vector2[blockTextureCount];
            Array.Copy(blockTexture, trimBlockTexture, blockTextureCount);
            vector2Pool.Return(blockTexture);

            // Trim lightmap array.
            Vector2[] trimLightmap = new Vector2[lightmapCount];
            Array.Copy(lightmap, trimLightmap, lightmapCount);
            vector2Pool.Return(lightmap);

            // Render builded chunk.
            ChunkRenderer chunkRenderer = chunkCollection[chunkAddress];
            chunkRenderer.Recalculate(trimVertices, trimIndices, trimBlockTexture, trimLightmap);

        }

        // TODO: implement slider in settings for amount of light iterations, from 2 to 8.
        public void RecalculateLightmap(int chunkAddress)
        {

            // Iterate from top to bottom to simulate skylight.
            for (int x = -1; x < 9; x++)
            {
                for (int z = -1; z < 9; z++)
                {
                    for (int y = 8; y >= 0; y--)
                    {

                        int mappingIndex = Tables.CoordsToMappingAddress(x + 2, y + 2, z + 2);
                        RuntimeBlock runtimeBlock = blockCollection[chunkAddress, mappingIndex];
                        BlockData blockData = runtimeBlock.data;

                        // Stop skylight when solid block has been hit.
                        if (blockData.cullingBinary != 0) break;

                        runtimeBlock.skylightIndex = 7;

                    }
                }
            }

            for (int iteration = 0; iteration < 7; iteration++)
            {
                // Iterate through all blocks to blend lightmap values.
                for (int x = -1; x < 9; x++)
                {
                    for (int y = -1; y < 9; y++)
                    {
                        for (int z = -1; z < 9; z++)
                        {

                            int mappingIndex = Tables.CoordsToMappingAddress(x + 2, y + 2, z + 2);
                            RuntimeBlock runtimeBlock = blockCollection[chunkAddress, mappingIndex];
                            BlockData blockData = runtimeBlock.data;

                            // Avoid spending CPU time on solid blocks.
                            if (blockData.cullingBinary != 0) continue;

                            // Collect facing block references.
                            RuntimeBlock[] facingBlocks = ResolveFacingBlocks(chunkAddress, mappingIndex);

                            // Assign lightmap values to neighbor blocks.
                            int stepSkylightIndex = runtimeBlock.skylightIndex - 1;
                            for (int i = 0; i < 6; i++)
                            {
                                facingBlocks[i].skylightIndex = Mathf.Max(facingBlocks[i].skylightIndex, stepSkylightIndex);
                            }

                        }
                    }
                }
            }

        }

        private RuntimeBlock[] ResolveFacingBlocks(int chunkAddress, int mappingIndex)
        {
            RuntimeBlock[] facingBlocks = new RuntimeBlock[6];
            for (int x = 0; x < 6; x++)
            {
                int facingMappingIndex = Tables.mappingFacingNeighbors[x] + mappingIndex;
                facingBlocks[x] = blockCollection[chunkAddress, facingMappingIndex];
            }
            return facingBlocks;
        }

        private RuntimeBlock[] ResolveSurroundingBlocks(int chunkAddress, int mappingIndex)
        {
            RuntimeBlock[] surroundingBlocks = new RuntimeBlock[27];
            for (int x = 0; x < 27; x++)
            {
                int surroundingMappingIndex = Tables.mappingSurroundingNeighbors[x] + mappingIndex;
                surroundingBlocks[x] = blockCollection[chunkAddress, surroundingMappingIndex];
            }
            return surroundingBlocks;
        }

        // TODO: implement LowQuality and HighQuality interfaces.
        private int ResolveSkylightIndex(RuntimeBlock[] surroundingBlocks, int vertexType)
        {
            // Calculate index of vertex lightmap group start.
            int groupIndex = vertexType * 7;
            // Collect vertex lightmap blocks.
            RuntimeBlock[] groupBlocks = new RuntimeBlock[7];
            for (int x = 0; x < 7; x++)
            {
                groupBlocks[x] = surroundingBlocks[Tables.vertexLightmapGroups[groupIndex + x]];
            }
            // Resolve lightmap configuration.
            int configuration = ResolveLightmapConfiguration(groupBlocks);
            // Resolve vertex lightmap connections to determine which 
            // lightmap blocks are connected to the vertex front face.
            int[] connections = Tables.vertexLightmapConnections[configuration];
            // Find maximum possible intensity index out of all connected light blocks.
            int intensityIndex = groupBlocks[0].skylightIndex;
            for (int x = 0; x < connections.Length; x++)
            {
                intensityIndex = Mathf.Max(intensityIndex, groupBlocks[connections[x]].skylightIndex);
            }
            return intensityIndex;
        }

        private int ResolveOcclusionConfiguration(RuntimeBlock[] surroundingBlocks, int faceType)
        {
            int groupIndex = faceType * 9;
            return
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 0]].data.cullingBinary << 8 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 1]].data.cullingBinary << 7 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 2]].data.cullingBinary << 6 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 3]].data.cullingBinary << 5 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 4]].data.cullingBinary << 4 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 5]].data.cullingBinary << 3 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 6]].data.cullingBinary << 2 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 7]].data.cullingBinary << 1 |
                surroundingBlocks[Tables.faceOcclusionGroups[groupIndex + 8]].data.cullingBinary;
        }

        private int ResolveLightmapConfiguration(RuntimeBlock[] groupBlocks)
        {
            return
                groupBlocks[1].data.lightmapBinary << 5 |
                groupBlocks[2].data.lightmapBinary << 4 |
                groupBlocks[3].data.lightmapBinary << 3 |
                groupBlocks[4].data.lightmapBinary << 2 |
                groupBlocks[5].data.lightmapBinary << 1 |
                groupBlocks[6].data.lightmapBinary;
        }

        private int ResolveConfiguration(RuntimeBlock[] surroundingBlocks)
        {
            // Calculate cube configuration, consider
            // solid blocks as a contributors to face culling.
            return
                surroundingBlocks[14].data.cullingBinary << 5 |
                surroundingBlocks[12].data.cullingBinary << 4 |
                surroundingBlocks[22].data.cullingBinary << 3 |
                surroundingBlocks[4].data.cullingBinary << 2 |
                surroundingBlocks[16].data.cullingBinary << 1 |
                surroundingBlocks[10].data.cullingBinary;
        }
    }
}