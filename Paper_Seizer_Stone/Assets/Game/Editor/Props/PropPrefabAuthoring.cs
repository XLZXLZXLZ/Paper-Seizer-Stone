using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaperSeizerStone.EditorTools
{
    // Explicit asset authoring only. No runtime scene construction or creation menu.
    public static class PropPrefabAuthoring
    {
        private const string ModelRoot = "Assets/Game/art/model/";
        private const string PrefabRoot = "Assets/Game/Prefabs/Props/";
        private const string RecordPath = "C:/Users/Administrator/.codex/visualizations/2026/10/27/01a17c54-f1cc-7db3-84fe-944efbd622ad/props-painterly-authoring.json";

        [Serializable]
        private sealed class RendererRecord
        {
            public string renderer;
            public string material;
            public string rendererType;
        }

        [Serializable]
        private sealed class PropRecord
        {
            public string prefab;
            public string model;
            public List<RendererRecord> renderers = new List<RendererRecord>();
        }

        [Serializable]
        private sealed class AuthoringRecord
        {
            public List<PropRecord> props = new List<PropRecord>();
            public string style = "Existing Character_Painterly Shader Graph; original artwork, soft cool shadows and warm light, subtle broad brush planes and restrained metal highlights.";
            public bool sceneChanged = false;
        }

        public static void CreatePainterlyPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Prop prefab authoring requires Edit mode.");

            var record = new AuthoringRecord();
            string bellFolder = ModelRoot + "Props/Tongling/";
            string moneyFolder = ModelRoot + "Props/Money/";
            string gunFolder = ModelRoot + "Weapons/Revolver/";
            Material bell = MaterialAt(bellFolder + "Materials/Tongling_Painterly.mat");
            Material money = MaterialAt(moneyFolder + "Materials/Money_Painterly.mat");
            Material body = MaterialAt(gunFolder + "Materials/Revolver_Body_Painterly.mat");
            Material cylinder = MaterialAt(gunFolder + "Materials/Revolver_Cylinder_Painterly.mat");
            Material hammer = MaterialAt(gunFolder + "Materials/Revolver_Hammer_Painterly.mat");
            Material trigger = MaterialAt(gunFolder + "Materials/Revolver_Trigger_Painterly.mat");

            RemapModel(bellFolder + "Tongling.fbx", "tripo_mat_424c016e", bell);
            RemapModel(moneyFolder + "Money.fbx", "tripo_mat_c5420d61", money);
            RemapModel(gunFolder + "Revolver_MechanicalRig.fbx", "tripo_mat_db5077a9", body);

            record.props.Add(SaveProp(bellFolder + "Tongling.fbx", PrefabRoot + "Tongling.prefab", "Tongling", renderer => bell));
            record.props.Add(SaveProp(moneyFolder + "Money.fbx", PrefabRoot + "Money.prefab", "Money", renderer => money));
            record.props.Add(SaveProp(gunFolder + "Revolver_MechanicalRig.fbx", PrefabRoot + "Revolver_MechanicalRig.prefab", "Revolver_MechanicalRig", renderer =>
            {
                switch (renderer.name)
                {
                    case "gun_cylinder": return cylinder;
                    case "gun_hammer": return hammer;
                    case "gun_trigger": return trigger;
                    default: return body;
                }
            }));

            File.WriteAllText(RecordPath, JsonUtility.ToJson(record, true));
            Debug.Log("PROPS_PAINTERLY_PREFABS_AUTHORED " + JsonUtility.ToJson(record));
        }

        private static Material MaterialAt(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException("Missing prop material: " + path);
            return material;
        }

        private static void RemapModel(string path, string sourceName, Material material)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing prop model importer: " + path);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sourceName), material);
            importer.SaveAndReimport();
        }

        private static PropRecord SaveProp(string modelPath, string prefabPath, string name, Func<Renderer, Material> materialFor)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var record = new PropRecord { prefab = prefabPath, model = modelPath };
            if (existing != null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    AssignMaterials(root, materialFor, record);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("Could not save prop prefab: " + prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                return record;
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new InvalidOperationException("Missing prop model: " + modelPath);
            // Isolated preview scene preserves the user's open scene and unsaved transforms.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.name = name;
                AssignMaterials(instance, materialFor, record);
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not create prop prefab: " + prefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return record;
        }

        private static void AssignMaterials(GameObject root, Func<Renderer, Material> materialFor, PropRecord record)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material material = materialFor(renderer);
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; ++i) slots[i] = material;
                renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);
                record.renderers.Add(new RendererRecord
                {
                    renderer = renderer.name,
                    material = AssetDatabase.GetAssetPath(material),
                    rendererType = renderer.GetType().Name
                });
            }
        }
    }
}
