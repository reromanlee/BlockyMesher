using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// The block types and textures a landscape uses. Landscapes that share a registry share its
    /// materials, which keeps them batched together.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockRegistry", menuName = "BlockyMesher/Block Registry")]
    public sealed class BlockRegistry : ScriptableObject
    {
        public const int MaxLightColors = 16;

        /// <summary>WebGL 2 guarantees texture arrays of 256 layers, and vertices store the layer in a byte.</summary>
        public const int MaxTextureLayers = 256;

        public const string ShaderName = "reromanlee/BlockyMesher/Blocks";
        public const string AmbientOcclusionPath = "Packages/com.reromanlee.blockymesher/Runtime/Textures/BlockOcclusion.png";

        static readonly int TexturesId = Shader.PropertyToID("_Textures");
        static readonly int OcclusionId = Shader.PropertyToID("_Occlusion");
        static readonly int SourceBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DestinationBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int DepthWriteId = Shader.PropertyToID("_ZWrite");

        [Tooltip("Every block type a landscape can contain. Air (id 0) is built in.")]
        public BlockData[] blocks = new BlockData[0];

        [Tooltip("Block textures: a grid PNG imported with Texture Shape set to 2D Array.")]
        public Texture2DArray textures;

        [Header("Rendering")]
        [Tooltip("The block shader. Filled in automatically.")]
        public Shader shader;

        [Tooltip("The prebaked ambient occlusion tiles. Filled in automatically.")]
        public Texture2DArray ambientOcclusion;

        Material[] materials;

        /// <summary>Lists everything that would make blocks render or save incorrectly. Empty when valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var owners = new Dictionary<int, BlockData>();
            var lightColors = new HashSet<uint>();
            if (textures != null && textures.depth > MaxTextureLayers)
                problems.Add($"The texture array has {textures.depth} layers, but at most {MaxTextureLayers} can be used.");
            for (int i = 0; i < blocks.Length; i++)
            {
                BlockData block = blocks[i];
                if (block == null)
                {
                    problems.Add($"Entry {i} is empty.");
                    continue;
                }
                if (block.id < 1 || block.id > BlockData.MaxId)
                    problems.Add($"{block.name}: id {block.id} is outside 1..{BlockData.MaxId}.");
                else if (owners.TryGetValue(block.id, out BlockData owner))
                    problems.Add($"{block.name} and {owner.name} both use id {block.id}.");
                else
                    owners.Add(block.id, block);

                int layerCount = textures != null ? Mathf.Min(textures.depth, MaxTextureLayers) : MaxTextureLayers;
                for (int face = 0; face < Faces.Count; face++)
                {
                    int layer = block.textures[(Face)face];
                    if (layer >= layerCount)
                        problems.Add($"{block.name}: {(Face)face} texture layer {layer} doesn't exist, the texture array has {layerCount} layers.");
                }
                if (block.emission > 0)
                    lightColors.Add(Pack(block.lightColor));
            }
            if (lightColors.Count > MaxLightColors)
                problems.Add($"{lightColors.Count} different light colors are used, but a registry holds up to {MaxLightColors}.");
            return problems;
        }

        /// <summary>
        /// Bakes the blocks into a table the jobs can read. Invalid entries are logged and skipped,
        /// so a broken registry still renders everything it can.
        /// </summary>
        internal BlockTable Bake(Allocator allocator)
        {
            foreach (string problem in Validate())
                Debug.LogError($"BlockRegistry '{name}': {problem}", this);

            int maxId = 0;
            var palette = new List<uint>();
            foreach (BlockData block in blocks)
            {
                if (block == null || block.id < 1 || block.id > BlockData.MaxId) continue;
                maxId = Mathf.Max(maxId, block.id);
                uint color = Pack(block.lightColor);
                if (block.emission > 0 && !palette.Contains(color) && palette.Count < MaxLightColors)
                    palette.Add(color);
            }
            if (palette.Count == 0)
                palette.Add(Pack(Color.white));

            var table = new BlockTable(maxId + 1, palette.Count, allocator);
            for (int i = 0; i < palette.Count; i++)
                table.LightColors[i] = Unpack(palette[i]);

            var baked = new HashSet<int>();
            foreach (BlockData block in blocks)
            {
                if (block == null || block.id < 1 || block.id > BlockData.MaxId || !baked.Add(block.id)) continue;

                var flags = BlockFlags.Visible;
                if (block.renderPass == RenderPass.Opaque) flags |= BlockFlags.Opaque;
                else if (block.lightPasses) flags |= BlockFlags.LightPasses;
                if (block.collidable) flags |= BlockFlags.Collidable;
                if (block.hideSameNeighborFaces) flags |= BlockFlags.HideSameNeighbor;

                int colorIndex = block.emission > 0 ? Mathf.Max(0, palette.IndexOf(Pack(block.lightColor))) : 0;
                table.Blocks[block.id] = new BlockInfo
                {
                    Flags = flags,
                    Pass = block.renderPass,
                    Emission = (byte)Mathf.Clamp(block.emission, 0, 7),
                    LightColor = (byte)colorIndex,
                };
                for (int face = 0; face < Faces.Count; face++)
                    table.FaceLayers[block.id * Faces.Count + face] = (ushort)Mathf.Clamp(block.textures[(Face)face], 0, ushort.MaxValue);
            }
            return table;
        }

        /// <summary>The shared material for one render pass, created on first use.</summary>
        internal Material GetMaterial(RenderPass pass)
        {
            if (materials == null)
                CreateMaterials();
            return materials[(int)pass];
        }

        void CreateMaterials()
        {
            if (shader == null)
            {
                shader = Shader.Find(ShaderName);
                if (shader == null)
                    Debug.LogError($"BlockRegistry '{name}' has no shader, and '{ShaderName}' isn't included in the build.", this);
            }
            materials = new Material[3];
            for (int pass = 0; pass < materials.Length; pass++)
            {
                materials[pass] = new Material(shader) { name = $"{name} ({(RenderPass)pass})", hideFlags = HideFlags.DontSave };
                SetUpPass(materials[pass], (RenderPass)pass);
            }
            ApplyTextures();
        }

        static void SetUpPass(Material material, RenderPass pass)
        {
            bool transparent = pass == RenderPass.Transparent;
            material.SetFloat(SourceBlendId, (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat(DestinationBlendId, (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(DepthWriteId, transparent ? 0 : 1);
            if (pass == RenderPass.Cutout)
                material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = pass switch
            {
                RenderPass.Opaque => (int)RenderQueue.Geometry,
                RenderPass.Cutout => (int)RenderQueue.AlphaTest,
                _ => (int)RenderQueue.Transparent,
            };
            material.SetOverrideTag("RenderType", pass switch
            {
                RenderPass.Opaque => "Opaque",
                RenderPass.Cutout => "TransparentCutout",
                _ => "Transparent",
            });
        }

        void ApplyTextures()
        {
            if (materials == null)
                return;
            foreach (Material material in materials)
            {
                material.SetTexture(TexturesId, textures);
                material.SetTexture(OcclusionId, ambientOcclusion);
            }
        }

        void OnValidate()
        {
#if UNITY_EDITOR
            if (shader == null)
                shader = Shader.Find(ShaderName);
            if (ambientOcclusion == null)
                ambientOcclusion = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2DArray>(AmbientOcclusionPath);
#endif
            ApplyTextures();
        }

        void OnDisable()
        {
            if (materials == null)
                return;
            foreach (Material material in materials)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            materials = null;
        }

        static uint Pack(Color color)
        {
            Color32 c = color;
            return (uint)(c.r | c.g << 8 | c.b << 16);
        }

        static Color32 Unpack(uint color) => new((byte)color, (byte)(color >> 8), (byte)(color >> 16), 255);
    }
}
