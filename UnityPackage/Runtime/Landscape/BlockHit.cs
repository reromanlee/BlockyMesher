using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>What <see cref="Landscape.Raycast"/> hit. Positions are in the landscape's block coordinates.</summary>
    public struct BlockHit
    {
        public Vector3Int Block;

        /// <summary>The face that was hit, pointing out of the block. Zero when the ray started inside it.</summary>
        public Vector3Int Normal;

        public ushort Id;

        /// <summary>Where the ray hit, in world space.</summary>
        public Vector3 Point;

        /// <summary>From the ray's origin to <see cref="Point"/>, in world units.</summary>
        public float Distance;

        /// <summary>Where a block placed against the hit face would go.</summary>
        public Vector3Int Adjacent => Block + Normal;
    }
}
