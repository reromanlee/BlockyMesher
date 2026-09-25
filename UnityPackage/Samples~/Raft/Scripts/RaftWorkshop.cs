using UnityEngine;
using UnityEngine.InputSystem;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>
    /// Edits the raft and launches copies of it. Left-click removes a block, right-click adds one
    /// against the face under the mouse. S saves the raft as it is now into a pattern, and Space drops
    /// a new raft made from that pattern into the water. Each raft is its own landscape.
    /// </summary>
    public sealed class RaftWorkshop : MonoBehaviour
    {
        [SerializeField] Landscape raft;
        [SerializeField] Water water;
        [SerializeField] BlockData placedBlock;
        [SerializeField] Camera sceneCamera;

        BlockPattern design;
        int launched;
        string message = "";
        float messageUntil;

        void Start() => design = raft.Pattern;

        void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null || keyboard == null || raft == null)
                return;

            bool orbiting = keyboard.altKey.isPressed;
            Ray ray = sceneCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (!orbiting && mouse.leftButton.wasPressedThisFrame && raft.Raycast(ray, 100, out BlockHit hit))
                raft.SetBlock(hit.Block, 0);
            if (!orbiting && mouse.rightButton.wasPressedThisFrame && raft.Raycast(ray, 100, out hit))
                raft.SetBlock(hit.Adjacent, placedBlock);

            if (keyboard.sKey.wasPressedThisFrame && raft.TryGetBlockBounds(out BoundsInt bounds))
            {
                design = raft.Export(bounds);
                Say($"Saved the raft: {bounds.size.x} × {bounds.size.y} × {bounds.size.z} blocks, {design.CompressedBytes} bytes");
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
                Launch();
        }

        /// <summary>
        /// A new landscape from the saved pattern. It is built from scratch rather than cloned, since
        /// cloning a landscape would also copy the objects its sections are drawn with.
        /// </summary>
        void Launch()
        {
            var copy = new GameObject($"Raft {++launched}");
            copy.SetActive(false);
            float angle = launched * 137.5f * Mathf.Deg2Rad;
            copy.transform.SetPositionAndRotation(
                raft.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (16 + launched * 3) + Vector3.up * 5,
                Quaternion.Euler(0, launched * 40, 0));

            var landscape = copy.AddComponent<Landscape>();
            landscape.Registry = raft.Registry;
            landscape.SectionsPerColumn = raft.SectionsPerColumn;
            landscape.Colliders = ColliderMode.Boxes;
            landscape.Pattern = design;
            copy.AddComponent<Rigidbody>();
            copy.AddComponent<Buoyancy>().Water = water;
            copy.SetActive(true);
            Say($"Launched raft {launched}");
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.time + 3;
        }

        void OnGUI()
        {
            GUI.Box(new Rect(10, Screen.height - 34, Screen.width - 20, 24),
                "Left: remove block    Right: add block    Alt + drag or middle mouse: orbit    S: save design    Space: launch a copy");
            if (Time.time < messageUntil)
                GUI.Box(new Rect(Screen.width / 2 - 200, Screen.height - 64, 400, 24), message);
        }
    }
}
