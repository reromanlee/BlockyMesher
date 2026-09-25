using UnityEngine;
using UnityEngine.InputSystem;

namespace reromanlee.BlockyMesher.Samples
{
    /// <summary>
    /// Press T to let the sun set and rise. Vertices only store how much sky they see, so this
    /// changes a single shader color and never rebuilds a mesh. F1 shows or hides the stats.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        [SerializeField] Camera sceneCamera;
        [SerializeField] LandscapeStatsOverlay stats;
        [SerializeField, Min(1)] float secondsPerDay = 40;

        static readonly Color DayLight = new(1f, 0.97f, 0.9f);
        static readonly Color DuskLight = new(1f, 0.55f, 0.35f);
        static readonly Color NightLight = new(0.14f, 0.16f, 0.3f);
        static readonly Color DaySky = new(0.55f, 0.72f, 0.95f);
        static readonly Color DuskSky = new(0.85f, 0.5f, 0.4f);
        static readonly Color NightSky = new(0.03f, 0.04f, 0.1f);

        bool running;
        float time;

        void Start() => Apply();

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame)
                running = !running;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && stats != null)
                stats.Visible = !stats.Visible;
            if (!running)
                return;
            time = (time + Time.deltaTime / secondsPerDay) % 1;
            Apply();
        }

        /// <summary>Time 0 is noon, 0.5 midnight; dusk and dawn sit in between.</summary>
        void Apply()
        {
            float sun = Mathf.Cos(time * 2 * Mathf.PI);
            BlockLighting.SkyColor = Blend(DayLight, DuskLight, NightLight, sun);
            Color sky = Blend(DaySky, DuskSky, NightSky, sun);
            RenderSettings.fogColor = sky;
            if (sceneCamera != null)
                sceneCamera.backgroundColor = sky;
        }

        static Color Blend(Color day, Color dusk, Color night, float sun) =>
            sun >= 0 ? Color.Lerp(dusk, day, sun) : Color.Lerp(dusk, night, -sun);
    }
}
