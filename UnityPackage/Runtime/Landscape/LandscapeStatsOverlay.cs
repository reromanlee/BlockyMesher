using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// Shows a landscape's stats and the frame rate on screen, in any build. Handy for measuring on
    /// the devices that matter, like a phone browser running a WebGL build.
    /// </summary>
    [AddComponentMenu("BlockyMesher/Landscape Stats Overlay")]
    public sealed class LandscapeStatsOverlay : MonoBehaviour
    {
        [Tooltip("Whose stats to show. The first landscape in the scene when left empty.")]
        [SerializeField] Landscape landscape;

        [SerializeField] bool visible = true;

        [Tooltip("Seconds between refreshes.")]
        [SerializeField, Min(0.1f)] float interval = 0.25f;

        GUIStyle style;
        GUIContent content = new();
        float nextRefresh;
        float frameSeconds = 1 / 60f;

        public bool Visible
        {
            get => visible;
            set => visible = value;
        }

        void Update()
        {
            frameSeconds = Mathf.Lerp(frameSeconds, Time.unscaledDeltaTime, 0.1f);
            if (!visible || Time.unscaledTime < nextRefresh)
                return;
            nextRefresh = Time.unscaledTime + interval;
            if (landscape == null)
                landscape = FindAnyObjectByType<Landscape>();
            string stats = landscape != null ? landscape.GetStats().ToString() : "No landscape in the scene.";
            content.text = $"{1 / frameSeconds:0} fps, {frameSeconds * 1000:0.0} ms per frame\n{stats}";
        }

        void OnGUI()
        {
            if (!visible)
                return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) };
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(10, 10, size.x, size.y), content, style);
        }
    }
}
