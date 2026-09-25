using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>
    /// Click the game view to look around with the mouse, Esc to get the cursor back.
    /// WASD to move, Space and Ctrl to rise and sink, Shift to go fast.
    /// </summary>
    public sealed class FlyCamera : MonoBehaviour
    {
        [SerializeField] float speed = 10;
        [SerializeField] float fastSpeed = 40;
        [SerializeField] float lookSensitivity = 0.12f;

        float yaw;
        float pitch;

        void Start()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = angles.x > 180 ? angles.x - 360 : angles.x;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                Cursor.lockState = CursorLockMode.Locked;
            if (keyboard.escapeKey.wasPressedThisFrame)
                Cursor.lockState = CursorLockMode.None;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, -89, 89);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            }

            // Forward and sideways follow where the camera looks; up and down stay vertical.
            var flat = Quaternion.Euler(0, yaw, 0);
            Vector3 move = flat * new Vector3(Axis(keyboard.dKey, keyboard.aKey), 0, Axis(keyboard.wKey, keyboard.sKey))
                + Vector3.up * Axis(keyboard.spaceKey, keyboard.leftCtrlKey);
            float currentSpeed = keyboard.leftShiftKey.isPressed ? fastSpeed : speed;
            transform.position += move * (currentSpeed * Time.deltaTime);
        }

        static float Axis(ButtonControl positive, ButtonControl negative) => (positive.isPressed ? 1 : 0) - (negative.isPressed ? 1 : 0);
    }
}
