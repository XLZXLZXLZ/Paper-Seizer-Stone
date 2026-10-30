using System.IO;
using System.Linq;
using PaperSeizerStone.Presentation.PostProcessing;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class StyleFilterWindow : EditorWindow
    {
        private const string SettingsPath = "Assets/Game/Data/PostProcessing/StyleFilterSettings.asset";
        private StyleFilterSettings settings;
        private SerializedObject serializedSettings;
        private StyleFilterPreset[] presets;
        private Vector2 scroll;

        [MenuItem("Tools/风格化后处理")]
        private static void Open() => GetWindow<StyleFilterWindow>("风格化后处理");

        private void OnEnable()
        {
            minSize = new Vector2(410, 580);
            settings = AssetDatabase.LoadAssetAtPath<StyleFilterSettings>(SettingsPath);
            RefreshPresets();
            Undo.undoRedoPerformed += RefreshPreview;
            EditorApplication.projectChanged += RefreshPresets;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshPreview;
            EditorApplication.projectChanged -= RefreshPresets;
            StyleFilterSettings.CompareOriginal = false;
            if (settings != null) AssetDatabase.SaveAssetIfDirty(settings);
            RefreshPreview();
        }

        private void OnLostFocus()
        {
            StyleFilterSettings.CompareOriginal = false;
            RefreshPreview();
        }

        private void RefreshPresets()
        {
            presets = AssetDatabase.FindAssets("t:StyleFilterPreset")
                .Select(g => AssetDatabase.LoadAssetAtPath<StyleFilterPreset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null).OrderBy(p => p.name).ToArray();
            Repaint();
        }

        private void RefreshPreview()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private void OnGUI()
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox("找不到全局滤镜配置：" + SettingsPath, MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox("在 Game 视图实时预览，无需运行。实体卡牌参与滤镜，Screen Space Overlay 界面不参与。修改先保存在工作配置，只有“保存到预设”会覆盖预设。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            StyleFilterSettings.PreviewEnabled = EditorGUILayout.Toggle("编辑器效果预览", StyleFilterSettings.PreviewEnabled);
            StyleFilterSettings.PreviewSceneView = EditorGUILayout.Toggle("同时预览 Scene 视图", StyleFilterSettings.PreviewSceneView);
            if (EditorGUI.EndChangeCheck()) RefreshPreview();
            DrawCompareButton();

            if (serializedSettings == null || serializedSettings.targetObject != settings)
                serializedSettings = new SerializedObject(settings);
            serializedSettings.Update();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("filterEnabled"), new GUIContent("启用全局滤镜", "保存到项目的总开关，影响编辑器及游戏运行。"));
            if (serializedSettings.ApplyModifiedProperties()) RefreshPreview();

            var selected = settings.selectedPreset;
            int index = System.Array.IndexOf(presets, selected);
            var names = new[] { "选择风格预设…" }.Concat(presets.Select(p => p.name)).ToArray();
            int next = EditorGUILayout.Popup("风格预设", index + 1, names);
            if (next > 0 && next != index + 1) ApplyPreset(presets[next - 1]);
            var picked = (StyleFilterPreset)EditorGUILayout.ObjectField("预设资源", settings.selectedPreset, typeof(StyleFilterPreset), false);
            if (picked != settings.selectedPreset) ApplyPreset(picked);
            if (settings.selectedPreset != null) EditorGUILayout.HelpBox(settings.selectedPreset.description, MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(settings.selectedPreset == null))
                {
                    if (GUILayout.Button("保存到预设"))
                    {
                        Undo.RecordObject(settings.selectedPreset, "保存风格预设");
                        settings.selectedPreset.parameters = settings.parameters.Copy();
                        EditorUtility.SetDirty(settings.selectedPreset);
                        AssetDatabase.SaveAssetIfDirty(settings.selectedPreset);
                        AssetDatabase.SaveAssetIfDirty(settings);
                    }
                    if (GUILayout.Button("恢复预设参数")) ApplyPreset(settings.selectedPreset);
                }
                if (GUILayout.Button("另存为新预设")) SaveAs();
            }
            EditorGUILayout.LabelField("切换预设会替换工作参数；满意的调整请先保存或另存。", EditorStyles.wordWrappedMiniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            serializedSettings.Update();
            DrawParameters(serializedSettings.FindProperty("parameters"));
            if (serializedSettings.ApplyModifiedProperties()) RefreshPreview();
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("保存当前应用配置")) AssetDatabase.SaveAssetIfDirty(settings);
        }

        private void DrawCompareButton()
        {
            var rect = GUILayoutUtility.GetRect(new GUIContent("按住查看原画面"), GUI.skin.button, GUILayout.Height(26));
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                StyleFilterSettings.CompareOriginal = true;
                RefreshPreview(); e.Use();
            }
            if (e.rawType == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                StyleFilterSettings.CompareOriginal = false;
                RefreshPreview(); e.Use();
            }
            if (e.type == EventType.Repaint)
                GUI.skin.button.Draw(rect, "按住查看原画面", rect.Contains(e.mousePosition), StyleFilterSettings.CompareOriginal, false, false);
        }

        private void ApplyPreset(StyleFilterPreset preset)
        {
            Undo.RecordObject(settings, "切换风格预设");
            settings.selectedPreset = preset;
            if (preset != null) settings.parameters = preset.parameters.Copy();
            EditorUtility.SetDirty(settings);
            serializedSettings = new SerializedObject(settings);
            RefreshPreview();
        }

        private void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject("保存风格预设", "自定义风格", "asset", "请选择预设保存位置", "Assets/Game/Data/PostProcessing");
            if (string.IsNullOrEmpty(path)) return;
            // Always create a new resource; do not replace a preset with a new GUID.
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog("请使用新名称", "覆盖已有预设请先选中它，再使用“保存到预设”。", "确定");
                return;
            }
            var preset = CreateInstance<StyleFilterPreset>();
            preset.description = "自定义组合，可自由调整风格与调色参数。";
            preset.parameters = settings.parameters.Copy();
            AssetDatabase.CreateAsset(preset, path);
            ApplyPreset(preset);
            AssetDatabase.SaveAssetIfDirty(settings);
            RefreshPresets();
        }

        internal static void DrawParameters(SerializedProperty root)
        {
            Field(root, "strength", "整体混合强度", "0 为原画面，1 为完整滤镜。所有效果均受它控制。");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("版画与印刷", EditorStyles.boldLabel);
            Field(root, "posterize", "色阶化强度", "把连续颜色归并为较大的色块；过高可能丢失卡面细节。");
            Field(root, "levels", "每通道色阶数", "越小越像版画，越大越接近原图。仅在色阶化开启时生效。");
            Field(root, "dither", "色阶抖色强度", "用固定细点打散色阶边界，不随时间闪烁。依赖色阶化。");
            Field(root, "ditherSize", "抖色点尺寸", "以1080p为参考的点尺寸，越大越粗。");
            Field(root, "halftone", "半色调网点强度", "在暗部叠加印刷墨点，0 关闭。通常不与强抖色同时使用。");
            Field(root, "dotSize", "网点间距", "以1080p像素为参考；越大越有旧海报感。");
            Field(root, "dotAngle", "网点旋转角度", "控制印刷网格方向，单位为度。");
            Field(root, "inkColor", "网点油墨颜色", "网点使用的暗色。");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("油画与纸张", EditorStyles.boldLabel);
            Field(root, "oil", "油画平滑强度", "Kuwahara 保边平滑，把碎纹理归并为色块；0 完全跳过采样。");
            Field(root, "oilRadius", "油画采样半径", "单位为实际渲染像素。越大色块越大，也越耗性能，可能抹掉牌面小字。");
            Field(root, "paper", "纸纹强度", "叠加静止的程序化纸纹，不需要额外贴图。");
            Field(root, "paperScale", "纸纹尺寸", "越大纸纹越粗，尺寸以1080p为参考。");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("全局调色", EditorStyles.boldLabel);
            Field(root, "exposure", "曝光补偿", "以档为单位调节画面亮度，0 保持不变。");
            Field(root, "contrast", "对比度", "正数增强明暗差异，负数柔化。");
            Field(root, "saturation", "饱和度", "-100 为灰度，0 保留原饱和度。");
            Field(root, "temperature", "冷暖偏移", "正数偏暖，负数偏冷，是风格化颜色偏移而非物理色温。");
            Field(root, "splitTone", "分离色调强度", "按明暗混合下方两种乘色；0 不改变颜色。");
            Field(root, "shadows", "暗部乘色", "控制阴影颜色，白色表示不改变。");
            Field(root, "highlights", "亮部乘色", "控制亮部颜色，白色表示不改变。");
            EditorGUILayout.Space(); EditorGUILayout.LabelField("暗角", EditorStyles.boldLabel);
            Field(root, "vignette", "暗角强度", "边缘向暗角颜色混合的强度；留意底部手牌与两侧筹码。");
            Field(root, "vignetteSoftness", "暗角柔和度", "越大过渡越宽、暗角越柔和。");
            Field(root, "vignetteColor", "暗角颜色", "建议使用低饱和的深色。");
            Field(root, "vignetteCenter", "暗角中心", "屏幕归一化坐标，默认 (0.5, 0.5)。");
        }

        private static void Field(SerializedProperty root, string name, string label, string tooltip)
            => EditorGUILayout.PropertyField(root.FindPropertyRelative(name), new GUIContent(label, tooltip), true);
    }

    [CustomEditor(typeof(StyleFilterPreset))]
    public sealed class StyleFilterPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"), new GUIContent("风格说明"));
            EditorGUILayout.HelpBox("这里编辑预设源文件。要实时预览与应用，请打开 Tools → 风格化后处理。", MessageType.Info);
            StyleFilterWindow.DrawParameters(serializedObject.FindProperty("parameters"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
