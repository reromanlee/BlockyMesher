using UnityEngine;

namespace reromanlee.BlockyMesher
{
    [CreateAssetMenu(menuName = "BlockyMesher/BlockData")]
    public class BlockData : ScriptableObject
    {

        public int id;
        public int[] textureIds;
        public int cullingBinary;
        public int lightmapBinary;
        public Color32[] particleColors;
        public int durability;

    }
}