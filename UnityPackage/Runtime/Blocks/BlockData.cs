using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// One block type. Only the fields below are read by the package; derive from this class to add
    /// gameplay data of your own (durability, drops, sounds) and list the derived assets in a
    /// <see cref="BlockRegistry"/> as usual.
    /// </summary>
    [CreateAssetMenu(fileName = "Block", menuName = "BlockyMesher/Block")]
    public class BlockData : ScriptableObject
    {
        public const int MaxId = ushort.MaxValue;

        [Tooltip("Number saved in edits and patterns, so never change it once blocks are placed. 0 is reserved for air.")]
        [Min(1)] public int id = 1;

        [Tooltip("Layer of the registry's texture array shown on each face.")]
        public FaceTextures textures;

        [Tooltip("Opaque blocks hide the faces they touch, cast ambient occlusion and block light.")]
        public RenderPass renderPass = RenderPass.Opaque;

        [Tooltip("Whether light goes through this block. Ignored for Opaque blocks, which always block it.")]
        public bool lightPasses;

        [Tooltip("Light this block emits: 0 is none, 7 is the brightest.")]
        [Range(0, 7)] public int emission;

        [Tooltip("Color of the emitted light. A registry can hold up to 16 different light colors.")]
        public Color lightColor = Color.white;

        [Tooltip("Whether this block gets colliders.")]
        public bool collidable = true;

        [Tooltip("Hide the faces between two blocks of this type, so a wall of glass looks like one pane.")]
        public bool hideSameNeighborFaces;

        protected virtual void OnValidate()
        {
            id = Mathf.Clamp(id, 1, MaxId);
        }
    }
}
