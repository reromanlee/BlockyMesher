using System.IO;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace reromanlee.BlockyMesher.Editor
{
    /// <summary>
    /// Bakes the ambient occlusion tiles: one per combination of solid blocks around a face (256 in
    /// all, see MeshJob.OcclusionCase), laid out in a 16 × 16 grid read left to right, top to bottom,
    /// which is how Unity slices a grid PNG into a 2D array. Tweak <see cref="Strength"/> and
    /// <see cref="Radius"/> and bake again, or paint over the PNG by hand.
    /// </summary>
    public static class AmbientOcclusionBaker
    {
        public const int TileSize = 16;
        public const int Columns = 16;

        /// <summary>How dark the shadow is right against a solid block: 0 is none, 1 is black.</summary>
        public static float Strength = 0.6f;

        /// <summary>How far a shadow reaches across the face, as a fraction of its width. At most 1, see <see cref="Sample"/>.</summary>
        public static float Radius = 0.9f;

        /// <summary>
        /// Brightness at a point (u, v) of a face, 1 being fully lit. Every solid block beside the face
        /// shades the edge it touches; a solid block at a diagonal shades its corner, unless an edge
        /// block already shades that corner. Shadows never reach further than one face, so two faces
        /// side by side see the same blocks along their shared edge and blend without a seam.
        /// </summary>
        public static float Sample(int occlusionCase, float u, float v)
        {
            bool Solid(int bit) => (occlusionCase & (1 << bit)) != 0;
            bool left = Solid(3), right = Solid(4), bottom = Solid(1), top = Solid(6);

            float light = 1;
            if (left) light *= 1 - Shadow(u);
            if (right) light *= 1 - Shadow(1 - u);
            if (bottom) light *= 1 - Shadow(v);
            if (top) light *= 1 - Shadow(1 - v);
            if (Solid(0) && !left && !bottom) light *= 1 - Shadow(math.length(new float2(u, v)));
            if (Solid(2) && !right && !bottom) light *= 1 - Shadow(math.length(new float2(1 - u, v)));
            if (Solid(5) && !left && !top) light *= 1 - Shadow(math.length(new float2(u, 1 - v)));
            if (Solid(7) && !right && !top) light *= 1 - Shadow(math.length(new float2(1 - u, 1 - v)));
            return light;
        }

        static float Shadow(float distance)
        {
            float t = math.saturate(1 - distance / Radius);
            return Strength * t * t;
        }

        /// <summary>Tile of a case inside the baked PNG, in pixels from its top-left corner.</summary>
        public static Vector2Int TileOrigin(int occlusionCase) => new(occlusionCase % Columns * TileSize, occlusionCase / Columns * TileSize);

        [MenuItem("Tools/BlockyMesher/Bake Ambient Occlusion Tiles")]
        public static void Bake()
        {
            int size = Columns * TileSize;
            var pixels = new Color32[size * size];
            for (int occlusionCase = 0; occlusionCase < 256; occlusionCase++)
            {
                Vector2Int origin = TileOrigin(occlusionCase);
                for (int y = 0; y < TileSize; y++)
                for (int x = 0; x < TileSize; x++)
                {
                    float brightness = Sample(occlusionCase, (x + 0.5f) / TileSize, (y + 0.5f) / TileSize);
                    byte value = (byte)math.round(brightness * 255);

                    // Texture rows run bottom to top, the tile grid top to bottom.
                    int row = size - 1 - (origin.y + (TileSize - 1 - y));
                    pixels[row * size + origin.x + x] = new Color32(value, value, value, 255);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            texture.SetPixels32(pixels);
            string path = BlockRegistry.AmbientOcclusionPath;
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            ConfigureImport(path, Columns, Columns, FilterMode.Bilinear, linear: true, singleChannel: true);
            Debug.Log($"Baked 256 ambient occlusion tiles into {path}");
        }

        /// <summary>Imports a grid PNG as a 2D texture array, one layer per cell.</summary>
        public static void ConfigureImport(string path, int columns, int rows, FilterMode filter, bool linear, bool singleChannel)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Default;
            settings.textureShape = TextureImporterShape.Texture2DArray;
            settings.flipbookColumns = columns;
            settings.flipbookRows = rows;
            settings.sRGBTexture = !linear;
            settings.mipmapEnabled = true;
            settings.filterMode = filter;
            settings.wrapMode = TextureWrapMode.Clamp;
            settings.alphaSource = singleChannel ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);

            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.format = singleChannel ? TextureImporterFormat.R8 : TextureImporterFormat.Automatic;
            platform.maxTextureSize = 2048;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }
    }
}
