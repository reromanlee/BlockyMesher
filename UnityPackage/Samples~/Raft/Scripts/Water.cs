using UnityEngine;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>A flat sea with a slow swell: the whole surface rises and falls together.</summary>
    public sealed class Water : MonoBehaviour
    {
        [SerializeField] float swellHeight = 0.25f;
        [SerializeField] float swellSeconds = 5;

        float baseHeight;

        public float SurfaceHeight => transform.position.y;

        void Awake() => baseHeight = transform.position.y;

        void FixedUpdate()
        {
            Vector3 position = transform.position;
            position.y = baseHeight + swellHeight * Mathf.Sin(Time.time * 2 * Mathf.PI / swellSeconds);
            transform.position = position;
        }
    }
}
