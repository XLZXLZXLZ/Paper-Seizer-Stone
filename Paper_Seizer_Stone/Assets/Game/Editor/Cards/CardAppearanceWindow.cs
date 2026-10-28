using System.IO;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class CardAppearanceWindow : EditorWindow
    {
        private const string ArtFolder = "Assets/Game/art/card";
        [SerializeField] private CardAppearanceDefinition appearance;
        [SerializeField] private CardDefinition cardDefinition;
        [SerializeField] private CardView previewCard;
        private UnityEditor.Editor appearanceEditor;
        private Vector2 scroll;

        [MenuItem("Tools/卡牌美术配置")]
        private static void Open() => GetWindow<CardAppearanceWindow>("卡牌美术配置");

        private void OnEnable()
        {
            minSize = new Vector2(380f, 540f);
            if (Selection.activeObject is CardAppearanceDefinition selected) appearance = selected;
            if (Selection.activeGameObject != null) previewCard = Selection.activeGameObject.GetComponent<CardView>();
        }

        private void OnDisable()
        {
            if (appearanceEditor != null) DestroyImmediate(appearanceEditor);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("美术 SO 集中存放在 Game/art/card。名称和说明以卡面为准；稳定规则 ID、能力和变形目标仍在原 CardDefinition 中配置。", MessageType.Info);
            appearance = (CardAppearanceDefinition)EditorGUILayout.ObjectField("美术配置", appearance, typeof(CardAppearanceDefinition), false);
            DrawLibrary();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("新建美术配置")) CreateAppearance();
                cardDefinition = (CardDefinition)EditorGUILayout.ObjectField("对应规则 SO", cardDefinition, typeof(CardDefinition), false);
                using (new EditorGUI.DisabledScope(appearance == null || cardDefinition == null))
                    if (GUILayout.Button("关联到规则 SO 并保存")) BindDefinition();
                previewCard = (CardView)EditorGUILayout.ObjectField("预览卡牌", previewCard, typeof(CardView), true);
                using (new EditorGUI.DisabledScope(appearance == null || previewCard == null || EditorUtility.IsPersistent(previewCard)))
                    if (GUILayout.Button("应用到选定的预览卡牌")) BindPreview();
            }
            EditorGUILayout.HelpBox("在 Prefab 模式或场景中选择圆角卡牌根节点，可直接修改 CardView 的 Preview Appearance、Preview Side 和 Preview Face Down。运行时由真实卡牌定义提供正面，统一卡背取自预览 SO。", MessageType.None);
            if (appearance != null)
            {
                UnityEditor.Editor.CreateCachedEditor(appearance, null, ref appearanceEditor);
                appearanceEditor.OnInspectorGUI();
                if (GUILayout.Button("保存美术配置")) AssetDatabase.SaveAssetIfDirty(appearance);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLibrary()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder)) return;
            string[] guids = AssetDatabase.FindAssets("t:CardAppearanceDefinition", new[] { ArtFolder });
            var labels = new string[guids.Length + 1];
            labels[0] = "选择已有卡面…";
            int current = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<CardAppearanceDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                labels[i + 1] = item.DisplayName + "  (" + item.name + ")";
                if (item == appearance) current = i + 1;
            }
            int chosen = EditorGUILayout.Popup("已有卡面", current, labels);
            if (chosen != current && chosen > 0)
                appearance = AssetDatabase.LoadAssetAtPath<CardAppearanceDefinition>(AssetDatabase.GUIDToAssetPath(guids[chosen - 1]));
        }

        private void CreateAppearance()
        {
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets/Game/art", "card");
            string path = AssetDatabase.GenerateUniqueAssetPath(ArtFolder + "/NewCardAppearance.asset");
            var created = CreateInstance<CardAppearanceDefinition>();
            var serialized = new SerializedObject(created);
            serialized.FindProperty("displayName").stringValue = "新卡牌";
            if (appearance != null)
            {
                serialized.FindProperty("backArtwork").objectReferenceValue = appearance.BackArtwork;
                serialized.FindProperty("playerBackTint").colorValue = appearance.BackTint(true);
                serialized.FindProperty("enemyBackTint").colorValue = appearance.BackTint(false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            created.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssetIfDirty(created);
            appearance = created;
            EditorGUIUtility.PingObject(created);
        }

        private void BindDefinition()
        {
            var serialized = new SerializedObject(cardDefinition);
            serialized.FindProperty("appearance").objectReferenceValue = appearance;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(cardDefinition);
        }

        private void BindPreview()
        {
            var serialized = new SerializedObject(previewCard);
            serialized.FindProperty("previewAppearance").objectReferenceValue = appearance;
            serialized.ApplyModifiedProperties();
            previewCard.RefreshEditorPreview();
            SceneView.RepaintAll();
        }
    }
}
