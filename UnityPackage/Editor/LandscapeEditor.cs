using UnityEditor;
using UnityEngine;

namespace reromanlee.BlockyMesher.Editor
{
    [CustomEditor(typeof(Landscape))]
    public sealed class LandscapeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var landscape = (Landscape)target;
            EditorGUILayout.Space();
            if (!landscape.IsReady)
            {
                EditorGUILayout.HelpBox("Assign a Block Registry to start the landscape.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild"))
                    landscape.Rebuild();
                if (GUILayout.Button("Save Blocks as Pattern…"))
                    SavePattern(landscape);
            }
        }

        static void SavePattern(Landscape landscape)
        {
            if (!landscape.TryGetBlockBounds(out BoundsInt bounds))
            {
                EditorUtility.DisplayDialog("Nothing to save", "The landscape has no blocks.", "OK");
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("Save Block Pattern", landscape.name, "asset",
                "The pattern starts at the lowest corner of the blocks.");
            if (string.IsNullOrEmpty(path))
                return;
            BlockPattern pattern = landscape.Export(bounds);
            AssetDatabase.CreateAsset(pattern, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(pattern);
        }
    }
}
