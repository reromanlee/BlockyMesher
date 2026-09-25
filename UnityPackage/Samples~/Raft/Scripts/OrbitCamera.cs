using UnityEngine;
using UnityEngine.InputSystem;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>Hold the middle mouse button (or Alt and the left one) to orbit, scroll to zoom.</summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] Transform target;

        [Tooltip("The point to look at, in the target's own space.")]
        [SerializeField] Vector3 offset;

        [SerializeField] float distance = 18;
        [SerializeField] float sensitivity = 0.25f;

        float yaw = 30;
        float pitch = 25;
        Vector3 focus;

        void Start() => focus = Focus();

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse != null)
            {
                bool orbiting = mouse.middleButton.isPressed || (mouse.leftButton.isPressed && keyboard != null && keyboard.altKey.isPressed);
                if (orbiting)
                {
                    Vector2 delta = mouse.delta.ReadValue() * sensitivity;
                    yaw += delta.x;
                    pitch = Mathf.Clamp(pitch - delta.y, 5, 85);
                }
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f, 5, 60);
            }

            // Follow the bobbing target smoothly rather than rigidly.
            focus = Vector3.Lerp(focus, Focus(), 1 - Mathf.Exp(-5 * Time.deltaTime));
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        Vector3 Focus() => target != null ? target.TransformPoint(offset) : offset;
    }
}
