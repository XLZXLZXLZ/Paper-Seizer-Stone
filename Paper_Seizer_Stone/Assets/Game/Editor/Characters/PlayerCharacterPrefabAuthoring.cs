using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.EditorTools
{
    // Explicit asset authoring entry point; no startup job, menu or runtime scene generation.
    public static class PlayerCharacterPrefabAuthoring
    {
        private const string Folder = "Assets/Game/art/act/Player/";
        private const string ModelPath = Folder + "Models/Player_Rigged.fbx";
        private const string PrefabPath = Folder + "Prefabs/Player_Hair.prefab";
        private const string GuaiwuPath = "Assets/Game/art/act/Guaiwu/Prefabs/Guaiwu_Cloth.prefab";

        [Serializable]
        private sealed class CharacterRecord
        {
            public string source = "E:/asset/blend/play.blend1";
            public string prefab = PrefabPath;
            public int skinnedParts;
            public int clothComponents;
            public PlayerHairAuthoringRecord hair;
            public string bodyArtwork = "Original Body_ExactBack_Atlas_8192, active UV0";
            public string materials = "Character Painterly: original RGB, soft bands, UV anchored brush patches; double-sided corrected normals for hair and cloak";
            public bool guaiwuMaterialsUpdated;
        }

        public static void Create()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Player FBX importer is missing.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Material body = MaterialAt(Folder + "Materials/Player_Body_Painterly.mat");
            Material head = MaterialAt(Folder + "Materials/Player_Head_Painterly.mat");
            Material hairMaterial = MaterialAt(Folder + "Materials/Player_Hair_Painterly.mat");
            if (model == null) throw new InvalidOperationException("Player FBX is not available.");

            var root = new GameObject("Player_Hair");
            var record = new CharacterRecord();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform, false);
                instance.name = "Player_Rigged";
                SkinnedMeshRenderer hair = null;
                SkinnedMeshRenderer headRenderer = null;
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    ++record.skinnedParts;
                    renderer.updateWhenOffscreen = true;
                    if (renderer.name == "Player_Hair")
                    {
                        renderer.sharedMaterial = hairMaterial;
                        hair = renderer;
                    }
                    else if (renderer.name == "Player_Head")
                    {
                        renderer.sharedMaterial = head;
                        headRenderer = renderer;
                    }
                    else renderer.sharedMaterial = body;
                }
                if (hair == null || headRenderer == null)
                    throw new InvalidOperationException("Separate Player_Hair and Player_Head skinned meshes are required.");
                record.hair = PlayerHairPhysicsAuthoring.Configure(root.transform, hair, headRenderer);
                record.clothComponents = root.GetComponentsInChildren<Cloth>(true).Length;

                Directory.CreateDirectory(Folder + "Prefabs");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save the Player_Hair prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            // Only reassign the two existing renderer materials. Do not rerun cloth or collider authoring.
            GameObject guaiwu = PrefabUtility.LoadPrefabContents(GuaiwuPath);
            try
            {
                Material guaiwuBody = MaterialAt("Assets/Game/art/act/Guaiwu/Materials/Guaiwu_Body_Painterly.mat");
                Material cloak = MaterialAt("Assets/Game/art/act/Guaiwu/Materials/Guaiwu_Cloak_Painterly.mat");
                foreach (var renderer in guaiwu.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterial = renderer.name.IndexOf("Cloak", StringComparison.OrdinalIgnoreCase) >= 0 ? cloak : guaiwuBody;
                    EditorUtility.SetDirty(renderer);
                }
                PrefabUtility.SaveAsPrefabAsset(guaiwu, GuaiwuPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save Guaiwu's painterly material assignments.");
                record.guaiwuMaterialsUpdated = true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(guaiwu);
            }

            File.WriteAllText(Folder + "Player_ImportRecord.json", JsonUtility.ToJson(record, true));
            AssetDatabase.ImportAsset(Folder + "Player_ImportRecord.json");
            AssetDatabase.SaveAssets();
            Debug.Log("PLAYER_HAIR_AND_CHARACTER_MATERIALS_AUTHORED " + JsonUtility.ToJson(record));
        }

        private static Material MaterialAt(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException("Missing character material: " + path);
            return material;
        }
    }
}
