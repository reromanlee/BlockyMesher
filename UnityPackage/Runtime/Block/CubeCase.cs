using UnityEngine;

namespace reromanlee.BlockyMesher
{
    public readonly struct CubeCase
    {
        public readonly Vector3[] vertices;
        public readonly int[] vertexTypes;
        public readonly int[] indices;
        public readonly int[] faces;

        public CubeCase(int configuration, Vector3[] cubeVertices, Quad[] cubeFaces)
        {
            // Calculate configuration data.
            int facesCount = 6;
            int[] byteArray = new int[6];
            for (int x = 5; x >= 0; x--)
            {
                byteArray[x] = configuration % 2;
                facesCount -= byteArray[x];
                configuration /= 2;
            }

            // Save configuration faces.
            int faceOffset = 0;
            faces = new int[facesCount];
            for (int x = 0; x < 6; x++)
            {
                if (byteArray[x] == 1) continue;
                faces[faceOffset] = x;
                faceOffset++;
            }

            // Allocate mesh arrays.
            vertices = new Vector3[facesCount * 4];
            vertexTypes = new int[vertices.Length];
            indices = new int[facesCount * 6];

            // Assign mesh data.
            int vertexOffset = 0;
            int indexOffset = 0;
            for (int x = 0; x < facesCount; x++)
            {
                int faceIndex = faces[x];
                Quad cubeFace = cubeFaces[faceIndex];

                // Configuration vertices.
                vertices[vertexOffset + 0] = cubeVertices[cubeFace.vertexA];
                vertices[vertexOffset + 1] = cubeVertices[cubeFace.vertexB];
                vertices[vertexOffset + 2] = cubeVertices[cubeFace.vertexC];
                vertices[vertexOffset + 3] = cubeVertices[cubeFace.vertexD];

                // Configuration vertex types.
                vertexTypes[vertexOffset + 0] = faceIndex * 4 + 0;
                vertexTypes[vertexOffset + 1] = faceIndex * 4 + 1;
                vertexTypes[vertexOffset + 2] = faceIndex * 4 + 2;
                vertexTypes[vertexOffset + 3] = faceIndex * 4 + 3;

                // Configuration indices.
                indices[indexOffset + 0] = vertexOffset + 0;
                indices[indexOffset + 1] = vertexOffset + 1;
                indices[indexOffset + 2] = vertexOffset + 2;
                indices[indexOffset + 3] = vertexOffset + 2;
                indices[indexOffset + 4] = vertexOffset + 3;
                indices[indexOffset + 5] = vertexOffset + 0;

                // Increase write offset.
                vertexOffset += 4;
                indexOffset += 6;
            }
        }
    }
}