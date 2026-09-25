using reromanlee.BlockyMesher.Meshing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// Cracks drawn over one block while it is being broken: a cube a hair larger than the block,
    /// with the crack stage picked in the shader. Nothing about the block's section is rebuilt.
    /// </summary>
    internal sealed class CrackOverlay
    {
        const float Grow = 1.002f;
        static readonly int StageId = Shader.PropertyToID("_Stage");
        static readonly int CracksId = Shader.PropertyToID("_Cracks");

        readonly GameObject gameObject;
        readonly Material material;
        readonly Mesh cube;
        readonly int stageCount;

        public CrackOverlay(Transform parent, BlockRegistry registry)
        {
            Shader shader = registry.crackShader != null ? registry.crackShader : Shader.Find(BlockRegistry.CrackShaderName);
            material = new Material(shader) { name = "Block Cracks", hideFlags = HideFlags.DontSave };
            material.SetTexture(CracksId, registry.cracks);
            stageCount = registry.cracks != null ? registry.cracks.depth : 1;
            cube = CreateCube();

            gameObject = new GameObject("Cracks") { hideFlags = SectionObject.Flags, layer = parent.gameObject.layer };
            gameObject.transform.SetParent(parent, false);
            gameObject.AddComponent<MeshFilter>().sharedMesh = cube;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            gameObject.SetActive(false);
        }

        /// <summary>Shows the cracks of <paramref name="progress"/> (0 to 1) on a block.</summary>
        public void Show(int3 block, float progress)
        {
            material.SetFloat(StageId, math.clamp((int)(progress * stageCount), 0, stageCount - 1));
            gameObject.transform.localPosition = (float3)block + 0.5f;
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        public void Destroy()
        {
            DestroyObject(gameObject);
            DestroyObject(material);
            DestroyObject(cube);
        }

        /// <summary>A cube around the origin, each face's texture upright the same way as on blocks.</summary>
        static Mesh CreateCube()
        {
            var vertices = new Vector3[24];
            var uvs = new Vector2[24];
            var triangles = new int[36];
            for (int face = 0; face < Faces.Count; face++)
            {
                FaceFrame frame = FaceFrame.Of(face);
                for (int corner = 0; corner < 4; corner++)
                {
                    int2 uv = FaceFrame.Corner(corner);
                    vertices[face * 4 + corner] = ((float3)(frame.Origin + uv.x * frame.U + uv.y * frame.V) - 0.5f) * Grow;
                    uvs[face * 4 + corner] = new Vector2(uv.x, uv.y);
                }
                int first = face * 4;
                triangles[face * 6 + 0] = first;
                triangles[face * 6 + 1] = first + 1;
                triangles[face * 6 + 2] = first + 2;
                triangles[face * 6 + 3] = first + 2;
                triangles[face * 6 + 4] = first + 3;
                triangles[face * 6 + 5] = first;
            }
            var mesh = new Mesh { name = "Crack Cube", hideFlags = HideFlags.DontSave, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
