using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace reromanlee.BlockyMesher.Editor
{
    /// <summary>Shows the texture of each face next to its layer number, taken from a registry that lists the block.</summary>
    [CustomEditor(typeof(BlockData), true), CanEditMultipleObjects]
    public sealed class BlockDataEditor : UnityEditor.Editor
    {
        static readonly string[] FaceNames = { "Right", "Left", "Up", "Down", "Forward", "Back" };
        static readonly string[] FaceFields = { "right", "left", "up", "down", "forward", "back" };

        SerializedProperty textures;
        Texture2DArray array;
        readonly Dictionary<int, Texture2D> previews = new();

        void OnEnable()
        {
            textures = serializedObject.FindProperty(nameof(BlockData.textures));
            array = FindTextures((BlockData)target);
        }

        void OnDisable()
        {
            foreach (Texture2D preview in previews.Values)
                DestroyImmediate(preview);
            previews.Clear();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, nameof(BlockData.textures));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);
            if (array == null)
                EditorGUILayout.HelpBox("List this block in a Block Registry with a texture array to see its textures here.", MessageType.None);

            for (int face = 0; face < FaceFields.Length; face++)
            {
                SerializedProperty layer = textures.FindPropertyRelative(FaceFields[face]);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(layer, new GUIContent(FaceNames[face]));
                    Rect rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    Texture2D preview = Preview(layer.intValue);
                    if (preview != null)
                        EditorGUI.DrawPreviewTexture(rect, preview);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                SerializedProperty right = textures.FindPropertyRelative("right");
                if (GUILayout.Button("Right on All Faces"))
                {
                    foreach (string field in FaceFields)
                        textures.FindPropertyRelative(field).intValue = right.intValue;
                }
                if (GUILayout.Button("Right on All Sides"))
                {
                    foreach (string field in new[] { "left", "forward", "back" })
                        textures.FindPropertyRelative(field).intValue = right.intValue;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>A texture array layer copied into a plain texture on the GPU, which the inspector can draw.</summary>
        Texture2D Preview(int layer)
        {
            if (array == null || layer < 0 || layer >= array.depth)
                return null;
            if (previews.TryGetValue(layer, out Texture2D preview) && preview != null)
                return preview;
            preview = new Texture2D(array.width, array.height, array.format, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            Graphics.CopyTexture(array, layer, 0, preview, 0, 0);
            previews[layer] = preview;
            return preview;
        }

        static Texture2DArray FindTextures(BlockData block)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BlockRegistry)))
            {
                var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null && registry.textures != null && System.Array.IndexOf(registry.blocks, block) >= 0)
                    return registry.textures;
            }
            return null;
        }
    }
}
