using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class PlayerShootingWindow : EditorWindow
    {
        [SerializeField] private GameObject fireSource;
        [SerializeField] private GameObject gunSource;
        [SerializeField] private GameObject bulletSource;
        [SerializeField] private float gunStart = 1f / 3f;
        [SerializeField] private float bulletStart = 3f;
        [SerializeField] private bool fireOnLastFrame;
        [SerializeField] private float fireMoment = 5.3333335f;
        private AnimationClip fireClip;
        private AnimationClip gunClip;
        private AnimationClip bulletClip;
        private UnityEngine.Object definition;
        private string refreshError;
        private string syncError;
        private string successMessage;
        private bool busy;
        private Vector2 scroll;

        [MenuItem("Tools/开枪动画配置")]
        private static void Open() => GetWindow<PlayerShootingWindow>("开枪动画配置");

        private static string AssetPath(GameObject source) => source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
        private float SelectedFireMoment => fireOnLastFrame ? -1f : fireMoment;

        private void OnEnable()
        {
            minSize = new Vector2(520f, 540f);
            if (fireSource == null) fireSource = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerShootingAuthoring.FireSourcePath);
            if (gunSource == null) gunSource = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerShootingAuthoring.GunSourcePath);
            if (bulletSource == null) bulletSource = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerShootingAuthoring.BulletSourcePath);
            RefreshReadOnly();
            // Open with the saved timing so synchronizing cannot silently restore an old firing frame.
            // Focus/refresh still leave the user's unsaved edits alone.
            if (definition != null)
            {
                var saved = new SerializedObject(definition);
                gunStart = saved.FindProperty("gunStart").floatValue;
                bulletStart = saved.FindProperty("bulletStart").floatValue;
                float savedFireMoment = saved.FindProperty("fireMoment").floatValue;
                fireOnLastFrame = savedFireMoment < 0f;
                if (!fireOnLastFrame) fireMoment = savedFireMoment;
            }
        }

        private void OnFocus() { if (!busy) RefreshReadOnly(); }
        private void OnProjectChange() { if (!busy) RefreshReadOnly(); }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("玩家三轨开枪配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("人物、枪械和装填子弹共用人物动作的时间轴。重新导出源 FBX 后，在这里同步并保存；打开窗口和刷新不会自动制作资源。", MessageType.Info);

            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUI.BeginChangeCheck();
                fireSource = (GameObject)EditorGUILayout.ObjectField("人物轨（fire1）", fireSource, typeof(GameObject), false);
                gunSource = (GameObject)EditorGUILayout.ObjectField("枪械轨", gunSource, typeof(GameObject), false);
                bulletSource = (GameObject)EditorGUILayout.ObjectField("装填子弹轨", bulletSource, typeof(GameObject), false);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("时间对齐", EditorStyles.boldLabel);
                gunStart = EditorGUILayout.FloatField("枪械开始偏移（秒）", gunStart);
                bulletStart = EditorGUILayout.FloatField("子弹开始偏移（秒）", bulletStart);
                fireOnLastFrame = EditorGUILayout.Toggle("使用人物末帧击发", fireOnLastFrame);
                if (!fireOnLastFrame)
                    fireMoment = EditorGUILayout.FloatField("击发时刻（秒）", fireMoment);
                if (EditorGUI.EndChangeCheck())
                {
                    syncError = null;
                    successMessage = null;
                    RefreshReadOnly();
                }
                EditorGUILayout.HelpBox("偏移从人物动作开头的 0 秒起算。原 24 FPS 时间轴中，枪械从第 8 帧开始（约 0.333333 秒），子弹从第 72 帧开始（3 秒）；这些默认值可以修改。", MessageType.None);
                if (GUILayout.Button("刷新来源与配置（只读）")) RefreshReadOnly();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("源动作信息（只读）", EditorStyles.boldLabel);
            DrawClip("人物动作", fireClip);
            DrawClip("枪械动作", gunClip);
            DrawClip("子弹动作", bulletClip);
            if (fireOnLastFrame && fireClip != null)
                EditorGUILayout.LabelField("当前击发时刻", fireClip.length.ToString("0.######") + " 秒（人物末帧）");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("保存目标", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("开枪配置", definition, typeof(UnityEngine.Object), false);
            EditorGUILayout.HelpBox("只接入玩家开枪表现：期间暂时隐藏手牌，左右手可见，结束后回到 Idle。装填子弹仅在自己的动作区间显示；命中、空枪和平局仍由原战斗规则决定，对手沿用原开枪逻辑。", MessageType.Info);

            if (!string.IsNullOrEmpty(refreshError)) EditorGUILayout.HelpBox(refreshError, MessageType.Error);
            string problem = GetCurrentProblem();
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            using (new EditorGUI.DisabledScope(busy || !string.IsNullOrEmpty(problem)))
                if (GUILayout.Button(busy ? "正在同步并保存…" : "同步三轨并保存配置", GUILayout.Height(32f))) Synchronize();

            if (!string.IsNullOrEmpty(syncError)) EditorGUILayout.HelpBox(syncError, MessageType.Error);
            if (!string.IsNullOrEmpty(successMessage)) EditorGUILayout.HelpBox(successMessage, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawClip(string label, AnimationClip clip)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(label, clip, typeof(AnimationClip), false);
            EditorGUILayout.LabelField("时长 / 帧率", clip == null ? "未找到唯一的有效动画片段"
                : clip.length.ToString("0.######") + " 秒  /  " + clip.frameRate.ToString("0.##") + " FPS");
        }

        private static AnimationClip ReadUniqueClip(GameObject source, string label, List<string> errors)
        {
            string path = AssetPath(source);
            if (string.IsNullOrEmpty(path)) return null;
            AnimationClip found = null;
            int count = 0;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip) || clip.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase)) continue;
                found = clip;
                count++;
            }
            if (count == 1) return found;
            errors.Add(label + (count == 0 ? "未找到有效动画片段。" : "包含多个动画片段，请使用只包含一个动作的 FBX。"));
            return null;
        }

        // Refresh only inspects already imported assets; it never imports or synchronizes them.
        private void RefreshReadOnly()
        {
            refreshError = null;
            fireClip = null;
            gunClip = null;
            bulletClip = null;
            definition = null;
            try
            {
                var errors = new List<string>();
                fireClip = ReadUniqueClip(fireSource, "人物轨：", errors);
                gunClip = ReadUniqueClip(gunSource, "枪械轨：", errors);
                bulletClip = ReadUniqueClip(bulletSource, "装填子弹轨：", errors);
                definition = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(PlayerShootingAuthoring.DefinitionPath);
                if (errors.Count > 0) refreshError = string.Join("\n", errors);
            }
            catch (Exception exception)
            {
                refreshError = "无法读取来源或开枪配置：" + exception.Message;
            }
            Repaint();
        }

        private string GetCurrentProblem()
        {
            try
            {
                return PlayerShootingAuthoring.GetProblem(AssetPath(fireSource), AssetPath(gunSource), AssetPath(bulletSource),
                    gunStart, bulletStart, SelectedFireMoment);
            }
            catch (Exception exception)
            {
                return "无法检查开枪配置：" + exception.Message;
            }
        }

        private void Synchronize()
        {
            string problem = GetCurrentProblem();
            if (!string.IsNullOrEmpty(problem))
            {
                syncError = problem;
                return;
            }

            busy = true;
            syncError = null;
            successMessage = null;
            try
            {
                PlayerShootingImportResult result = PlayerShootingAuthoring.Synchronize(
                    AssetPath(fireSource), AssetPath(gunSource), AssetPath(bulletSource), gunStart, bulletStart, SelectedFireMoment);
                successMessage = "已同步三轨并保存开枪配置，人物动作长度为 " + result.length.ToString("0.######") + " 秒。";
                RefreshReadOnly();
                ShowNotification(new GUIContent("三轨开枪配置已保存"));
            }
            catch (Exception exception)
            {
                syncError = "同步失败：" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                busy = false;
                Repaint();
            }
        }
    }
}
