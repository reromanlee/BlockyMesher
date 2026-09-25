using UnityEditor;
using UnityEngine;

namespace reromanlee.BlockyMesher.Editor
{
    /// <summary>Import settings of the package's own textures, in case they need to be set up again.</summary>
    public static class PackageTextures
    {
        [MenuItem("Tools/BlockyMesher/Reimport Package Textures")]
        public static void Configure()
        {
            // 256 ambient occlusion cases: smooth grayscale, read as plain numbers rather than colors.
            AmbientOcclusionBaker.ConfigureImport(BlockRegistry.AmbientOcclusionPath, AmbientOcclusionBaker.Columns, AmbientOcclusionBaker.Columns,
                FilterMode.Bilinear, linear: true, singleChannel: true);

            // 8 crack stages, lightest first, as crisp pixels like the block textures.
            AmbientOcclusionBaker.ConfigureImport(BlockRegistry.CracksPath, 4, 2, FilterMode.Point, linear: false, singleChannel: false);
        }
    }
}
