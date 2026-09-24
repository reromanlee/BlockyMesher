using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>Light settings shared by every landscape.</summary>
    public static class BlockLighting
    {
        static readonly int SkyColorId = Shader.PropertyToID("_BlockySkyColor");
        static Color skyColor = Color.white;

        /// <summary>
        /// Color of full skylight. Vertices store only how much sky they see, so changing this over
        /// time gives a day/night cycle without rebuilding any mesh.
        /// </summary>
        public static Color SkyColor
        {
            get => skyColor;
            set
            {
                skyColor = value;
                Shader.SetGlobalColor(SkyColorId, value);
            }
        }

        internal static void Apply() => Shader.SetGlobalColor(SkyColorId, skyColor);
    }
}
