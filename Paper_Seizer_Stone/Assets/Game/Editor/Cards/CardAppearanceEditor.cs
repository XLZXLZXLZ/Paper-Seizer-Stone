using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    [CustomEditor(typeof(CardAppearanceDefinition))]
    public sealed class CardAppearanceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                foreach (CardView view in Resources.FindObjectsOfTypeAll<CardView>())
                    if (!EditorUtility.IsPersistent(view)) view.RefreshEditorPreview();
                SceneView.RepaintAll();
            }
            var appearance = (CardAppearanceDefinition)target;
            Texture2D texture = appearance.FrontArtwork;
            if (texture == null)
            {
                EditorGUILayout.HelpBox("暂无正面图片，卡牌将显示纯色牌面、名称与说明。", MessageType.Info);
                return;
            }
            Rect rect = GUILayoutUtility.GetRect(160f, 280f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, texture, null, ScaleMode.ScaleToFit);
        }
    }
}
