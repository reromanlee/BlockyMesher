using System.Collections.Generic;
using UnityEngine;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>
    /// Floats a landscape on a Rigidbody. Every block of its box colliders that is under water is
    /// pushed up by the water it displaces, right where it is, so the side that dips deeper gets
    /// pushed harder and the raft rights itself.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Buoyancy : MonoBehaviour
    {
        [SerializeField] Water water;

        [Tooltip("How heavy the blocks are compared to water. Below 1 floats.")]
        [SerializeField, Range(0.05f, 2)] float density = 0.25f;

        [Tooltip("How much the water slows the raft down while it is fully under.")]
        [SerializeField, Min(0)] float waterDrag = 3;

        Rigidbody body;
        readonly List<BoxCollider> boxes = new();

        public Water Water
        {
            get => water;
            set => water = value;
        }

        void Awake() => body = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            if (water == null)
                return;
            GetComponentsInChildren(false, boxes);
            float totalVolume = 0;
            float submergedVolume = 0;
            foreach (BoxCollider box in boxes)
            {
                if (!box.enabled)
                    continue;

                // The boxes are blocks merged together, so split them back into blocks.
                Vector3Int count = Vector3Int.Max(Vector3Int.RoundToInt(box.size), Vector3Int.one);
                var block = new Vector3(box.size.x / count.x, box.size.y / count.y, box.size.z / count.z);
                Vector3 scaled = Vector3.Scale(block, box.transform.lossyScale);
                float volume = scaled.x * scaled.y * scaled.z;
                Vector3 first = box.center - (box.size - block) / 2;
                totalVolume += volume * count.x * count.y * count.z;

                for (int y = 0; y < count.y; y++)
                for (int z = 0; z < count.z; z++)
                for (int x = 0; x < count.x; x++)
                {
                    Vector3 center = box.transform.TransformPoint(first + Vector3.Scale(new Vector3(x, y, z), block));
                    float depth = Mathf.Clamp01((water.SurfaceHeight - center.y) / scaled.y + 0.5f);
                    if (depth <= 0)
                        continue;
                    submergedVolume += volume * depth;
                    body.AddForceAtPosition(-Physics.gravity * (volume * depth), center);
                }
            }
            if (totalVolume <= 0)
                return;

            // Weight follows the blocks, so an edited raft floats higher or lower.
            body.mass = totalVolume * density;
            float wet = submergedVolume / totalVolume;
            body.linearDamping = waterDrag * wet;
            body.angularDamping = waterDrag * wet;
        }
    }
}
