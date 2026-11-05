using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class GlobalFontWindow : EditorWindow
    {
        private UnityEditor.Editor configurationEditor;
        private Vector2 scroll;

        [MenuItem("Tools/字体设置")]
        private static void Open() => GetWindow<GlobalFontWindow>("字体设置");

        private void OnGUI()
        {
            var settings = GlobalFontService.Configuration;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("未找到 Resources/UI/GlobalFonts 配置。", MessageType.Error);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("拖入 TMP 字体资源。分类留空时沿用默认字体；换字体时同时使用新字体默认材质。字号、颜色及布局保持原样。", MessageType.Info);
            UnityEditor.Editor.CreateCachedEditor(settings, null, ref configurationEditor);
            configurationEditor.OnInspectorGUI();
            if (GUILayout.Button("定位字体配置")) Selection.activeObject = settings;
            if (GUILayout.Button("应用到当前打开的场景")) ApplyToScenes(settings);
            EditorGUILayout.HelpBox("编辑模式应用支持撤销，场景需自行保存。运行时自动读取配置，包含新生成的文字。贴图内的文字不受影响；静态字库缺字需补字或配置备用字体。", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private static void ApplyToScenes(GlobalFontConfiguration settings)
        {
            if (Application.isPlaying)
            {
                GlobalFontService.RequestRefresh();
                return;
            }
            if (!settings.applyGlobally) return;
            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null || !text.gameObject.scene.IsValid() || !text.gameObject.scene.isLoaded
                    || PrefabStageUtility.GetPrefabStage(text.gameObject) != null) continue;
                if (!GlobalFontService.PrepareTextRenderer(text))
                {
                    Debug.LogWarning("跳过缺少 Renderer 的3D文字：" + text.name, text);
                    continue;
                }
                TMP_FontAsset font = settings.Resolve(text);
                if (font == null || (text.font == font && text.fontSharedMaterial == font.material)) continue;
                Undo.RecordObject(text, "统一更换字体");
                GlobalFontService.Apply(text, settings);
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                EditorSceneManager.MarkSceneDirty(text.gameObject.scene);
            }
            Undo.RecordObject(TMP_Settings.instance, "统一备用字体");
            TMP_Settings.fallbackFontAssets.Clear();
            foreach (TMP_FontAsset font in settings.fallbackFonts)
                if (font != null && !TMP_Settings.fallbackFontAssets.Contains(font)) TMP_Settings.fallbackFontAssets.Add(font);
            EditorUtility.SetDirty(TMP_Settings.instance);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(TMP_Settings.instance);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private void OnDisable()
        {
            if (configurationEditor != null) DestroyImmediate(configurationEditor);
        }
    }
}
