using UnityEditor;
using UnityEngine;

namespace LevelForge.EditorTools
{
    /// <summary>
    /// Small, game-agnostic Editor GUI helpers (colored section headers, tab buttons, stat tiles)
    /// factored out of BlockMerge3D's AI Level Designer window - none of this draws anything
    /// specific to voxels/grids/etc, so any procedural-design Editor tool in any project can reuse
    /// it instead of re-implementing the same IMGUI boilerplate.
    /// </summary>
    public static class LevelForgeGUIStyles
    {
        public static Texture2D MakeSolidTexture(Color color)
        {
            var tex = new Texture2D(2, 2);
            var pixels = new Color[4] { color, color, color, color };
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Begins a titled, colored-header card. Pair with <see cref="EndSectionCard"/>.</summary>
        public static void BeginSectionCard(string title, Color headerColor, string icon = "")
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect headerRect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, headerColor);

            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 0, 0, 0)
            };
            headerStyle.normal.textColor = Color.white;

            string fullTitle = string.IsNullOrEmpty(icon) ? title : $"{icon}  {title}";
            GUI.Label(headerRect, fullTitle, headerStyle);

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 8, 8) });
        }

        public static void EndSectionCard()
        {
            EditorGUILayout.EndVertical(); // inner content padding
            EditorGUILayout.EndVertical(); // outer helpBox
        }

        /// <summary>A small titled value tile, e.g. for showing a search engine's attempt count or score.</summary>
        public static void DrawStatBlock(string title, string value, Color valueColor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80), GUILayout.ExpandWidth(true));
            var titleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.75f, 0.8f) }
            };
            var valueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = valueColor }
            };
            EditorGUILayout.LabelField(title, titleStyle);
            EditorGUILayout.LabelField(value, valueStyle);
            EditorGUILayout.EndVertical();
        }
    }
}
