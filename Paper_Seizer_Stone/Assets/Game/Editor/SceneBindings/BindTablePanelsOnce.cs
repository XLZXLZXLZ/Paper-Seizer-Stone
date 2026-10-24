#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PaperSeizerStone.Flow;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.EditorTools
{
    // One-time authoring migration for an already open BasicBattle scene. No menu or Play-time construction.
    [InitializeOnLoad]
    internal static class BindTablePanelsOnce
    {
        private const string Self = "Assets/Game/Editor/SceneBindings/BindTablePanelsOnce.cs";
        static BindTablePanelsOnce() { EditorApplication.update += Apply; }

        private static T Find<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();

        private static void Bind(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field != null && field.objectReferenceValue != value)
            {
                field.objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                else EditorUtility.SetDirty(target);
            }
        }

        private static void Apply()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/BasicBattle.unity");
            if (scene.IsValid() && scene.isLoaded)
            {
                BattleController controller = Find<BattleController>(scene);
                TabletopView table = Find<TabletopView>(scene);
                BattleHud hud = Find<BattleHud>(scene);
                BurnTableView burning = Find<BurnTableView>(scene);
                ShopTableView shop = Find<ShopTableView>(scene);
                if (controller == null || table == null || hud == null) return;
                if (burning == null)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Panels/BurnTable.prefab");
                    if (prefab == null) return;
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.SetActive(false);
                    burning = instance.GetComponent<BurnTableView>();
                }
                if (shop == null)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Shop/ShopTable.prefab");
                    if (prefab == null) return;
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.SetActive(false);
                    shop = instance.GetComponent<ShopTableView>();
                }
                TablePanel panel = table.GetComponent<TablePanel>();
                if (panel == null || burning == null) return;
                Bind(panel, "hud", hud.GetComponent<TableFlyGroup>());
                Bind(controller, "battlePanel", panel);
                Bind(controller, "burnView", burning);
                Bind(controller, "tableView", table);
                Bind(controller, "shopView", shop);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            EditorApplication.update -= Apply;
            AssetDatabase.DeleteAsset(Self);
        }
    }
}
#endif
