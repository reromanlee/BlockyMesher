using UnityEditor;
using UnityEngine;

namespace reromanlee.BlockyMesher.Editor
{
    /// <summary>Live stats of every landscape in the open scenes.</summary>
    public sealed class LandscapeStatsWindow : EditorWindow
    {
        Vector2 scroll;
        double nextRepaint;

        [MenuItem("Window/BlockyMesher/Landscape Stats")]
        public static void Open() => GetWindow<LandscapeStatsWindow>("Landscape Stats");

        void OnEnable() => EditorApplication.update += RepaintOccasionally;

        void OnDisable() => EditorApplication.update -= RepaintOccasionally;

        void RepaintOccasionally()
        {
            if (EditorApplication.timeSinceStartup < nextRepaint)
                return;
            nextRepaint = EditorApplication.timeSinceStartup + 0.25;
            Repaint();
        }

        void OnGUI()
        {
#if UNITY_6000_5_OR_NEWER
            Landscape[] landscapes = FindObjectsByType<Landscape>();
#else
            Landscape[] landscapes = FindObjectsByType<Landscape>(FindObjectsSortMode.None);
#endif
            if (landscapes.Length == 0)
            {
                EditorGUILayout.HelpBox("There is no landscape in the open scenes.", MessageType.Info);
                return;
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Main-thread time is measured in Play Mode. Counts and memory show any time.", MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Landscape landscape in landscapes)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(landscape.name, EditorStyles.boldLabel);
                if (!landscape.IsReady)
                {
                    EditorGUILayout.LabelField("Not running: it has no block registry.");
                    continue;
                }
                LandscapeStats stats = landscape.GetStats();
                Row("Main thread", $"{stats.MainThreadMs:0.00} ms per frame, worst {stats.PeakMainThreadMs:0.00} ms");
                Row("Building", $"{stats.SectionsBuiltPerSecond:0} sections/s, {stats.QueuedSections} queued, {stats.BuildingSections} running");
                Row("Streaming", $"{stats.ColumnsLoadedPerSecond:0} columns/s, {stats.GeneratingColumns} generating");
                Row("Columns", stats.Columns.ToString());
                Row("Section meshes", $"{stats.SectionMeshes}, {stats.DrawCalls} draw calls");
                Row("Triangles", $"{stats.Triangles:N0} ({stats.Vertices:N0} vertices)");
                Row("Colliders", $"{stats.SectionsWithColliders} sections");
                EditorGUILayout.Space(2);
                Row("Memory", Megabytes(stats.TotalBytes));
                Row("   Blocks", $"{Megabytes(stats.BlockBytes)}, {stats.MixedSections} sections with block arrays");
                Row("   Meshes", Megabytes(stats.MeshBytes));
                Row("   Colliders", Megabytes(stats.ColliderBytes));
                Row("   Work buffers", Megabytes(stats.WorkBufferBytes));
                Row("   Edits", Megabytes(stats.EditBytes));
            }
            EditorGUILayout.EndScrollView();
        }

        static void Row(string label, string value) => EditorGUILayout.LabelField(label, value);

        static string Megabytes(long bytes) => $"{bytes / (1024f * 1024f):0.00} MB";
    }
}
