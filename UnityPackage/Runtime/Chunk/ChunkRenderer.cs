using UnityEngine;

namespace reromanlee.BlockyMesher
{
    public class ChunkRenderer
    {
        public ChunkRenderer(int chunkAddress, Transform chunkParent)
        {
            int x = Tables.ChunkAddressToX(chunkAddress);
            int y = Tables.ChunkAddressToY(chunkAddress);
            int z = Tables.ChunkAddressToZ(chunkAddress);
            // Create a new scene object.
            GameObject gameObject = new()
            {
                name = $"{x} {y} {z}",
                layer = LayerMask.NameToLayer(Tables.chunkLayerName)
            };
            gameObject.transform.SetParent(chunkParent);
            gameObject.transform.position = new(
                x * Tables.chunkLengthX,
                y * Tables.chunkLengthX,
                z * Tables.chunkLengthX);
            // Initialize mesh filter component.
            mesh = new();
            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            // Initialize mesh renderer component.
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = Resources.Load<Material>("Materials/BlockMaterial");
            // Initialize mesh collider component.
            meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }

        private readonly Mesh mesh;
        private readonly MeshFilter meshFilter;
        private readonly MeshCollider meshCollider;

        public void Recalculate(Vector3[] vertices, int[] indices, Vector2[] textureUVs, Vector2[] lightmap)
        {
            // Assign mesh data.
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.uv = textureUVs;
            mesh.uv2 = lightmap;
            // Display mesh on MeshFilter.
            mesh.RecalculateNormals();
            meshFilter.sharedMesh = mesh;
            // Assign MeshCollider mesh.
            if (vertices.Length != 0)
            {
                meshCollider.sharedMesh = mesh;
            }
            else
            {
                meshCollider.sharedMesh = null;
            }
        }
    }
}