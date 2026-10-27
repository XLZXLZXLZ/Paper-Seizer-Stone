using PaperSeizerStone.Presentation;
using PaperSeizerStone.Configuration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public sealed class BattleCameraPoseWindow : EditorWindow
    {
        private static readonly string[] PoseNames = { "操作视角", "计分视角", "射击视角" };
        private static readonly string[] PoseProperties = { "tablePose", "scoringPose", "shootingPose" };

        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private int selectedPose;
        private string lastCapture;

        [MenuItem("Tools/相机机位配置")]
        private static void Open()
        {
            GetWindow<BattleCameraPoseWindow>("相机机位配置");
        }

        // The shortcut remains available while arranging MainCamera in the Scene or Inspector.
        [Shortcut("Paper Seizer Stone/覆盖当前相机机位", KeyCode.Quote)]
        private static void CaptureShortcut()
        {
            if (!HasOpenInstances<BattleCameraPoseWindow>() || EditorGUIUtility.editingTextField)
                return;

            GetWindow<BattleCameraPoseWindow>().CapturePose();
        }

        private void OnEnable()
        {
            minSize = new Vector2(340f, 240f);
            selectedPose = Mathf.Clamp(selectedPose, 0, PoseNames.Length - 1);
            if (cameraRig == null) FindRig();
        }

        private void FindRig()
        {
            if (Selection.activeGameObject != null)
                cameraRig = Selection.activeGameObject.GetComponentInParent<BattleCameraRig>();

            if (cameraRig != null) return;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            cameraRig = stage != null
                ? stage.prefabContentsRoot.GetComponentInChildren<BattleCameraRig>(true)
                : Object.FindObjectOfType<BattleCameraRig>();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("MainCamera 机位配置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("调整绑定的 MainCamera，然后点击确认或按 '，将相对根节点的位置和旋转保存到相机配置 SO。支持撤销。", MessageType.Info);
            cameraRig = (BattleCameraRig)EditorGUILayout.ObjectField("相机控制器", cameraRig, typeof(BattleCameraRig), true);
            if (GUILayout.Button("查找相机控制器")) FindRig();
            selectedPose = EditorGUILayout.Popup("当前配置视角", selectedPose, PoseNames);

            Camera source = cameraRig != null ? cameraRig.Camera : null;
            BattleCameraDefinition target = cameraRig != null ? cameraRig.Definition : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("来源 MainCamera", source, typeof(Camera), true);
                EditorGUILayout.ObjectField("目标相机配置 SO", target, typeof(BattleCameraDefinition), false);
            }

            string problem = GetProblem(source, target);
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);

            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button("确认覆盖当前视角  ( ' )", GUILayout.Height(30f))) CapturePose();
            }

            EditorGUILayout.LabelField("确认后自动保存 SO；共用此 SO 的相机都会使用新机位。", EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(lastCapture))
                EditorGUILayout.HelpBox(lastCapture, MessageType.Info);
        }

        private string GetProblem(Camera source, BattleCameraDefinition target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "请退出运行模式后配置，避免退出 Play 时丢失机位修改。";
            if (cameraRig == null) return "请选择场景或预制体编辑模式中的 BattleCameraRig。";
            if (EditorUtility.IsPersistent(cameraRig)) return "请先打开预制体编辑模式，或使用场景中的相机控制器。";
            if (source == null) return "相机控制器尚未绑定 MainCamera。";
            if (target == null) return "请在相机控制器中绑定相机配置 SO。";
            if (!EditorUtility.IsPersistent(target)) return "请使用已保存到项目中的相机配置 SO。";
            return null;
        }

        private void CapturePose()
        {
            Camera source = cameraRig != null ? cameraRig.Camera : null;
            BattleCameraDefinition target = cameraRig != null ? cameraRig.Definition : null;
            string problem = GetProblem(source, target);
            if (problem != null)
            {
                ShowNotification(new GUIContent(problem));
                return;
            }

            var serializedDefinition = new SerializedObject(target);
            var pose = serializedDefinition.FindProperty(PoseProperties[selectedPose]);
            pose.FindPropertyRelative("localPosition").vector3Value =
                cameraRig.transform.InverseTransformPoint(source.transform.position);
            pose.FindPropertyRelative("localRotation").quaternionValue =
                Quaternion.Inverse(cameraRig.transform.rotation) * source.transform.rotation;
            serializedDefinition.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(target);
            lastCapture = "已保存「" + PoseNames[selectedPose] + "」到 " + AssetDatabase.GetAssetPath(target);
            ShowNotification(new GUIContent("已覆盖" + PoseNames[selectedPose]));
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
