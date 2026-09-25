using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>
    /// Breaks and places blocks where the camera looks. Hold the left mouse button to break,
    /// right-click to place, 1 to 9 or the mouse wheel to pick the block. F5 saves your edits,
    /// F9 loads them back (PlayerPrefs, which is browser storage on the web).
    /// </summary>
    public sealed class BlockInteractor : MonoBehaviour
    {
        const string SaveKey = "BlockyMesher.Samples.InfiniteTerrain.Edits";

        [SerializeField] Landscape landscape;
        [SerializeField] LandscapeStreamer streamer;
        [SerializeField] BlockData[] placeable = Array.Empty<BlockData>();
        [SerializeField] float reach = 8;
        [SerializeField] float secondsToBreak = 0.5f;

        int selected;
        Vector3Int breaking;
        float progress;
        string message = "";
        float messageUntil;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null || landscape == null || placeable.Length == 0)
                return;

            for (int i = 0; i < Mathf.Min(placeable.Length, 9); i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) selected = i;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
                selected = (selected + (scroll > 0 ? -1 : 1) + placeable.Length) % placeable.Length;

            if (keyboard.f5Key.wasPressedThisFrame && streamer != null)
            {
                PlayerPrefs.SetString(SaveKey, Convert.ToBase64String(streamer.SaveEdits()));
                PlayerPrefs.Save();
                Say($"Saved {streamer.EditedSectionCount} edited sections");
            }
            if (keyboard.f9Key.wasPressedThisFrame && streamer != null && PlayerPrefs.HasKey(SaveKey))
            {
                streamer.LoadEdits(Convert.FromBase64String(PlayerPrefs.GetString(SaveKey)));
                Say("Loaded your edits");
            }

            // Only while the mouse looks around: the first click just captures the cursor.
            if (Cursor.lockState != CursorLockMode.Locked || !landscape.Raycast(new Ray(transform.position, transform.forward), reach, out BlockHit hit))
            {
                StopBreaking();
                return;
            }

            if (mouse.leftButton.isPressed)
            {
                if (hit.Block != breaking)
                {
                    breaking = hit.Block;
                    progress = 0;
                }
                progress += Time.deltaTime / secondsToBreak;
                if (progress >= 1)
                {
                    landscape.SetBlock(hit.Block, 0);
                    StopBreaking();
                }
                else
                {
                    landscape.ShowCrack(hit.Block, progress);
                }
            }
            else
            {
                StopBreaking();
            }

            if (mouse.rightButton.wasPressedThisFrame)
                landscape.SetBlock(hit.Adjacent, placeable[selected]);
        }

        void StopBreaking()
        {
            progress = 0;
            breaking = new Vector3Int(int.MinValue, 0, 0);
            if (landscape != null)
                landscape.HideCrack();
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.time + 3;
        }

        void OnGUI()
        {
            if (placeable.Length == 0)
                return;
            float width = Screen.width;
            GUI.Label(new Rect(width / 2 - 6, Screen.height / 2 - 11, 20, 22), "+");
            string help = $"Placing: {placeable[selected].name}  (1-9 or wheel)    Left: break    Right: place    F5/F9: save/load    T: day/night    F1: stats";
            GUI.Box(new Rect(10, Screen.height - 34, width - 20, 24), help);
            if (Time.time < messageUntil)
                GUI.Box(new Rect(width / 2 - 150, Screen.height - 64, 300, 24), message);
        }
    }
}
