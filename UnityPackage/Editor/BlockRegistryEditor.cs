using System.Collections.Generic;
using UnityEditor;

namespace reromanlee.BlockyMesher.Editor
{
    /// <summary>Points out everything that would make the registry's blocks render or save incorrectly.</summary>
    [CustomEditor(typeof(BlockRegistry))]
    public sealed class BlockRegistryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var registry = (BlockRegistry)target;
            List<string> problems = registry.Validate();
            EditorGUILayout.Space();
            if (problems.Count == 0)
                EditorGUILayout.HelpBox($"{registry.blocks.Length} blocks, nothing to fix.", MessageType.Info);
            foreach (string problem in problems)
                EditorGUILayout.HelpBox(problem, MessageType.Error);
        }
    }
}
