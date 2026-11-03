using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class PlayerAnimationSyncWindow : EditorWindow
    {
        private const string RecordPath = "Assets/Game/art/animation/Player/Player_Idle1_ImportRecord.json";

        [SerializeField] private GameObject sourceFbx;
        [SerializeField] private string takeName = "idle1";
        [SerializeField] private string lastSourcePath;
        private AnimationClip[] sourceClips = new AnimationClip[0];
        private string[] clipNames = new string[0];
        private GameObject targetModel;
        private AnimationClip destinationClip;
        private RuntimeAnimatorController controller;
        private SyncRecord lastRecord;
        private string sourceHash;
        private string refreshError;
        private string syncError;
        private string successMessage;
        private bool busy;
        private Vector2 scroll;

        [Serializable]
        private sealed class SyncRecord
        {
            public string source;
            public string sourceTake;
            public string sourceSha256;
            public string synchronizedLocal;
        }

        [MenuItem("Tools/角色动画同步")]
        private static void Open() => GetWindow<PlayerAnimationSyncWindow>("角色动画同步");

        private string SourcePath => sourceFbx != null ? AssetDatabase.GetAssetPath(sourceFbx) : string.Empty;

        private AnimationClip SelectedClip
        {
            get
            {
                foreach (AnimationClip clip in sourceClips)
                    if (clip != null && clip.name == takeName) return clip;
                return null;
            }
        }

        private void OnEnable()
        {
            minSize = new Vector2(480f, 540f);
            if (sourceFbx == null)
                sourceFbx = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAnimationSyncAuthoring.DefaultSourcePath);
            RefreshSource();
        }

        private void OnFocus() { if (!busy) RefreshSource(); }
        private void OnProjectChange() { if (!busy) RefreshSource(); }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("玩家动画同步", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("将源 FBX 中选定的动作同步到玩家播放片段，并自动保存 Idle1 / Idle1_Return 两个状态的播放引用。重新导出后需点击同步。", MessageType.Info);

            using (new EditorGUI.DisabledScope(busy))
            {
                EditorGUI.BeginChangeCheck();
                sourceFbx = (GameObject)EditorGUILayout.ObjectField("源 FBX", sourceFbx, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    syncError = null;
                    successMessage = null;
                    RefreshSource();
                }
                DrawClipSelection();
                AnimationClip selected = SelectedClip;
                if (selected != null)
                    EditorGUILayout.LabelField("源动作", selected.length.ToString("0.###") + " 秒  /  " + selected.frameRate.ToString("0.##") + " FPS");
                if (GUILayout.Button("刷新来源与同步记录")) RefreshSource();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("保存目标", EditorStyles.boldLabel);
            DrawTarget("玩家模型", targetModel, typeof(GameObject));
            DrawTarget("玩家播放片段", destinationClip, typeof(AnimationClip));
            DrawTarget("播放 Controller", controller, typeof(RuntimeAnimatorController));

            EditorGUILayout.Space();
            DrawRecord();
            if (!string.IsNullOrEmpty(refreshError)) EditorGUILayout.HelpBox(refreshError, MessageType.Error);

            string problem = PlayerAnimationSyncAuthoring.GetProblem(SourcePath, takeName);
            if (string.IsNullOrEmpty(problem) && SelectedClip == null)
                problem = "请选择有效的源动画片段。";
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            using (new EditorGUI.DisabledScope(busy || !string.IsNullOrEmpty(problem)))
                if (GUILayout.Button(busy ? "正在同步并保存…" : "同步到玩家并保存", GUILayout.Height(32f))) Synchronize();

            if (!string.IsNullOrEmpty(syncError)) EditorGUILayout.HelpBox(syncError, MessageType.Error);
            if (!string.IsNullOrEmpty(successMessage)) EditorGUILayout.HelpBox(successMessage, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawClipSelection()
        {
            if (sourceClips.Length == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.Popup("动画片段", 0, new[] { "未找到可用动画片段" });
                return;
            }

            int current = Array.IndexOf(clipNames, takeName);
            if (current < 0)
            {
                var choices = new string[clipNames.Length + 1];
                choices[0] = "已移除：" + takeName;
                Array.Copy(clipNames, 0, choices, 1, clipNames.Length);
                int chosen = EditorGUILayout.Popup("动画片段", 0, choices);
                if (chosen > 0) SelectTake(clipNames[chosen - 1]);
                return;
            }

            int next = EditorGUILayout.Popup("动画片段", current, clipNames);
            if (next != current) SelectTake(clipNames[next]);
        }

        private void SelectTake(string value)
        {
            takeName = value;
            syncError = null;
            successMessage = null;
        }

        private static void DrawTarget(string label, UnityEngine.Object value, Type type)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(label, value, type, false);
                using (new EditorGUI.DisabledScope(value == null))
                    if (GUILayout.Button("定位", GUILayout.Width(44f))) EditorGUIUtility.PingObject(value);
            }
        }

        private void DrawRecord()
        {
            EditorGUILayout.LabelField("上次同步", EditorStyles.boldLabel);
            if (lastRecord == null || string.IsNullOrEmpty(lastRecord.sourceSha256))
            {
                EditorGUILayout.HelpBox("尚无可用的同步记录。", MessageType.Info);
                return;
            }

            string time = lastRecord.synchronizedLocal ?? string.Empty;
            if (DateTimeOffset.TryParse(time, out DateTimeOffset synchronized))
                time = synchronized.ToString("yyyy-MM-dd HH:mm");
            EditorGUILayout.LabelField("时间", time);
            EditorGUILayout.LabelField("来源", Path.GetFileName(lastRecord.source ?? string.Empty));
            EditorGUILayout.LabelField("动作", string.IsNullOrEmpty(lastRecord.sourceTake) ? "未记录" : lastRecord.sourceTake);
            if (string.IsNullOrEmpty(SourcePath) || string.IsNullOrEmpty(sourceHash)) return;
            if (!string.Equals(SourcePath, lastRecord.source, StringComparison.Ordinal))
                EditorGUILayout.HelpBox("当前来源与上次同步使用的 FBX 不同。", MessageType.Warning);
            else if (!string.Equals(sourceHash, lastRecord.sourceSha256, StringComparison.OrdinalIgnoreCase))
                EditorGUILayout.HelpBox("源 FBX 文件已更新，尚未同步到玩家播放片段。", MessageType.Warning);
            else if (!string.Equals(takeName, lastRecord.sourceTake, StringComparison.Ordinal))
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(lastRecord.sourceTake)
                    ? "当前片段尚无对应的同步记录。"
                    : "当前选择的动画片段与上次同步不同，尚未同步此片段。", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("源文件与所选片段均与上次同步一致。", MessageType.Info);
        }

        // Hashing and record reads happen on refresh events, never during repaint.
        private void RefreshSource()
        {
            refreshError = null;
            sourceHash = null;
            lastRecord = null;
            sourceClips = new AnimationClip[0];
            clipNames = new string[0];
            try
            {
                targetModel = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAnimationSyncAuthoring.TargetModelPath);
                destinationClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerAnimationSyncAuthoring.DestinationClipPath);
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimationSyncAuthoring.ControllerPath);
                string path = SourcePath;
                bool changedSource = !string.Equals(path, lastSourcePath, StringComparison.Ordinal);
                var clips = new List<AnimationClip>();
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase)) clips.Add(clip);
                    clips.Sort((first, second) => string.CompareOrdinal(first.name, second.name));
                    sourceClips = clips.ToArray();
                    clipNames = new string[sourceClips.Length];
                    for (int i = 0; i < sourceClips.Length; i++) clipNames[i] = sourceClips[i].name;
                    using (var stream = File.OpenRead(ProjectFile(path)))
                    using (var hash = SHA256.Create())
                        sourceHash = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                }

                if (changedSource || string.IsNullOrEmpty(takeName))
                {
                    int preferred = Array.IndexOf(clipNames, "idle1");
                    takeName = preferred >= 0 ? clipNames[preferred] : clipNames.Length > 0 ? clipNames[0] : string.Empty;
                }
                lastSourcePath = path;
                string recordFile = ProjectFile(RecordPath);
                if (File.Exists(recordFile)) lastRecord = JsonUtility.FromJson<SyncRecord>(File.ReadAllText(recordFile));
            }
            catch (Exception exception)
            {
                refreshError = "无法刷新来源或同步记录：" + exception.Message;
            }
            Repaint();
        }

        private static string ProjectFile(string assetPath) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        private void Synchronize()
        {
            string problem = PlayerAnimationSyncAuthoring.GetProblem(SourcePath, takeName);
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
                PlayerAnimationSyncResult result = PlayerAnimationSyncAuthoring.Synchronize(SourcePath, takeName);
                successMessage = "已同步并自动保存「" + takeName + "」：" + result.length.ToString("0.###") + " 秒，" + result.frameRate.ToString("0.##") + " FPS。";
                RefreshSource();
                lastRecord = new SyncRecord { source = result.source, sourceTake = result.sourceTake, sourceSha256 = result.sourceSha256, synchronizedLocal = result.synchronizedLocal };
                ShowNotification(new GUIContent("玩家动画已同步并保存"));
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
