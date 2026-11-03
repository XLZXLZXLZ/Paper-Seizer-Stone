using PaperSeizerStone.Presentation.Props;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    [CustomEditor(typeof(PlayerCardGrip))]
    public sealed class PlayerCardGripEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var grip = (PlayerCardGrip)target;
            Transform anchor = grip.Anchor;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("握牌位置与角度", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("在非运行模式调整可保存到场景或预制体。这里编辑握牌锚点相对手掌的位置和角度；牌大小与扇角仍在 BattleTable 的 FirstPersonHandView 组件中调整。", MessageType.Info);
            if (anchor == null)
            {
                EditorGUILayout.HelpBox("请先绑定 Grip Anchor。", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 localPosition = EditorGUILayout.Vector3Field("握牌局部位置", anchor.localPosition);
            bool positionChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            Vector3 localAngles = EditorGUILayout.Vector3Field("握牌局部角度", anchor.localEulerAngles);
            bool rotationChanged = EditorGUI.EndChangeCheck();

            if (positionChanged || rotationChanged)
            {
                Undo.RecordObject(anchor, "调整握牌锚点");
                if (positionChanged) anchor.localPosition = localPosition;
                if (rotationChanged) anchor.localEulerAngles = localAngles;
                if (PrefabUtility.IsPartOfPrefabInstance(anchor))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
                EditorUtility.SetDirty(anchor);
                if (!EditorApplication.isPlaying && anchor.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
                else if (EditorUtility.IsPersistent(anchor))
                    PrefabUtility.SavePrefabAsset(anchor.root.gameObject);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("选择握牌锚点"))
            {
                Selection.activeGameObject = anchor.gameObject;
                EditorGUIUtility.PingObject(anchor.gameObject);
            }
        }
    }
}
