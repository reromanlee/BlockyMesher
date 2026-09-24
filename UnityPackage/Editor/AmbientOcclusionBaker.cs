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

        /// <summary>How dark the shadow is at the foot of a long wall: 0 is none, 1 is black. Corners where walls meet get darker.</summary>
        public static float Strength = 0.6f;

        /// <summary>How far a shadow reaches across the face, as a fraction of its width. Kept below 1, see <see cref="Sample"/>.</summary>
        public static float Radius = 0.9f;

        const int Steps = 32;
        static float normalizedRadius = -1;
        static float halfDiskWeight;

        /// <summary>
        /// Brightness at a point (u, v) of a face, 1 being fully lit. Each solid block around the face
        /// covers the unit square next to it in the face's plane, and the shadow at a point is how
        /// much of a small disk around it those squares cover, weighted toward the middle. So shadows
        /// are darkest where blocks meet around a point and fade out where a neighbor block ends.
        /// The disk is smaller than a face, so two faces side by side see the same blocks along their
        /// shared edge and blend without a seam.
        /// </summary>
        public static float Sample(int occlusionCase, float u, float v)
        {
            float radius = math.min(Radius, 0.99f);
            float covered = 0;
            for (int j = 0; j < Steps; j++)
            for (int i = 0; i < Steps; i++)
            {
                float2 offset = (new float2(i, j) + 0.5f) * (2 * radius / Steps) - radius;
                float weight = Kernel(math.length(offset), radius);
                if (weight > 0 && IsSolid(occlusionCase, new float2(u, v) + offset))
                    covered += weight;
            }
            return math.saturate(1 - Strength * covered / HalfDiskWeight(radius));
        }

        static float Kernel(float distance, float radius)
        {
            float t = math.saturate(1 - distance / radius);
            return t * t;
        }

        /// <summary>The weight a straight wall covers at its foot, which is what <see cref="Strength"/> refers to.</summary>
        static float HalfDiskWeight(float radius)
        {
            if (normalizedRadius == radius)
                return halfDiskWeight;
            halfDiskWeight = 0;
            for (int j = 0; j < Steps; j++)
            for (int i = Steps / 2; i < Steps; i++)
                halfDiskWeight += Kernel(math.length((new float2(i, j) + 0.5f) * (2 * radius / Steps) - radius), radius);
            normalizedRadius = radius;
            return halfDiskWeight;
        }

        /// <summary>Whether a point of the face's plane lies over a solid block. The face's own front block is always open.</summary>
        static bool IsSolid(int occlusionCase, float2 point)
        {
            int du = (int)math.floor(point.x);
            int dv = (int)math.floor(point.y);
            if ((du == 0 && dv == 0) || du < -1 || du > 1 || dv < -1 || dv > 1)
                return false;
            int index = (dv + 1) * 3 + du + 1;
            return (occlusionCase & (1 << (index < 4 ? index : index - 1))) != 0;
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
                    // Edge texels sit exactly on the face's edges (the shader samples inset by half a
                    // texel), so both faces along a shared edge read the very same values there.
                    float brightness = Sample(occlusionCase, x / (TileSize - 1f), y / (TileSize - 1f));
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
