using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.EditorTools
{
    // Asset authoring only. No automatic imports, menu entries or runtime scene construction.
    public static class GuaiwuClothPrefabAuthoring
    {
        private const string Folder = "Assets/Game/art/act/Guaiwu/";
        private const string ModelPath = Folder + "Models/Guaiwu_Rigged.fbx";
        private const string PrefabPath = Folder + "Prefabs/Guaiwu_Cloth.prefab";

        [Serializable]
        private sealed class ImportRecord
        {
            public string prefab;
            public int bones;
            public int clothParticles;
            public int fixedParticles;
            public int movingParticles;
            public int capsuleColliders;
            public float characterHeight;
            public float hemMaximumDistance;
            public float cuffMaximumDistance;
            public string constraints;
        }

        public static void Create()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Missing Guaiwu FBX model importer.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.optimizeGameObjects = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Materials/Guaiwu_Body.mat");
            var cloakMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Materials/Guaiwu_Cloak.mat");
            if (model == null || bodyMaterial == null || cloakMaterial == null)
                throw new InvalidOperationException("Guaiwu model and both authored materials are required.");

            var root = new GameObject("Guaiwu_Cloth");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.transform.SetParent(root.transform, false);
                instance.name = "Guaiwu_Rigged";
                SkinnedMeshRenderer clothing = null;
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.updateWhenOffscreen = true;
                    bool isCloak = renderer.name.IndexOf("Cloak", StringComparison.OrdinalIgnoreCase) >= 0;
                    renderer.sharedMaterial = isCloak ? cloakMaterial : bodyMaterial;
                    if (isCloak) clothing = renderer;
                }
                if (clothing == null || clothing.sharedMesh == null)
                    throw new InvalidOperationException("The FBX must contain the separate skinned Guaiwu_Cloak mesh.");

                CapsuleCollider[] capsules = GuaiwuClothColliders.Build(root.transform);
                var cloth = clothing.gameObject.AddComponent<Cloth>();
                cloth.useGravity = true;
                cloth.useTethers = true;
                cloth.stretchingStiffness = 0.95f;
                cloth.bendingStiffness = 0.45f;
                cloth.damping = 0.32f;
                cloth.friction = 0.55f;
                cloth.clothSolverFrequency = 60f;
                cloth.stiffnessFrequency = 120f;
                cloth.enableContinuousCollision = true;
                cloth.worldVelocityScale = 0.5f;
                cloth.worldAccelerationScale = 0.2f;
                cloth.externalAcceleration = new Vector3(0.7f, 0, 0.35f);
                cloth.randomAcceleration = new Vector3(0.45f, 0.12f, 0.45f);
                cloth.selfCollisionDistance = 0;
                cloth.selfCollisionStiffness = 0;
                cloth.useVirtualParticles = 0;
                cloth.SetVirtualParticleIndices(new List<uint>());
                cloth.capsuleColliders = capsules;

                ImportRecord record = ConfigureConstraints(root.transform, clothing, cloth);
                record.prefab = PrefabPath;
                record.capsuleColliders = capsules.Length;
                record.bones = 0;
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform.name == "Hips")
                    {
                        record.bones = transform.GetComponentsInChildren<Transform>(true).Length - capsules.Length;
                        break;
                    }
                Bounds bounds = clothing.localBounds;
                float rendererScale = Mathf.Max(0.0001f, Mathf.Abs(clothing.transform.lossyScale.x));
                bounds.Expand(record.hemMaximumDistance * 2.5f / rendererScale);
                clothing.localBounds = bounds;

                Directory.CreateDirectory(Folder + "Prefabs");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Could not save the configured Guaiwu cloth prefab.");
                File.WriteAllText(Folder + "Guaiwu_Cloth_ImportRecord.json", JsonUtility.ToJson(record, true));
                AssetDatabase.ImportAsset(Folder + "Guaiwu_Cloth_ImportRecord.json");
                AssetDatabase.SaveAssets();
                Debug.Log("GUAIWU_CLOTH_AUTHORED " + JsonUtility.ToJson(record));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static ImportRecord ConfigureConstraints(Transform root, SkinnedMeshRenderer renderer, Cloth cloth)
        {
            Vector3[] particles = cloth.vertices;
            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            if (particles.Length == 0 || particles.Length != coefficients.Length)
                throw new InvalidOperationException("Cloth particles have not been created for the imported mesh.");

            // Match Unity's Cloth inspector: particles use actualRootBone rotation/translation, not renderer TRS.
            PropertyInfo property = typeof(SkinnedMeshRenderer).GetProperty("actualRootBone",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Transform particleRoot = property == null ? null : property.GetValue(renderer, null) as Transform;
            if (particleRoot == null) particleRoot = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            var positions = new Vector3[particles.Length];
            Bounds bounds = new Bounds(root.InverseTransformPoint(particleRoot.rotation * particles[0] + particleRoot.position), Vector3.zero);
            for (int i = 0; i < particles.Length; ++i)
            {
                positions[i] = root.InverseTransformPoint(particleRoot.rotation * particles[i] + particleRoot.position);
                bounds.Encapsulate(positions[i]);
            }
            float height = bounds.size.y;
            if (height <= 0.01f) throw new InvalidOperationException("Clothing height is too small for the authored constraints.");
            var record = new ImportRecord
            {
                clothParticles = particles.Length,
                characterHeight = height,
                hemMaximumDistance = 0.14f * height,
                cuffMaximumDistance = 0.065f * height,
                constraints = "Hood, collar, shoulders and upper central torso fixed; graded hem and cuff motion. Backstop disabled; explicit bone capsules enabled."
            };
            for (int i = 0; i < particles.Length; ++i)
            {
                Vector3 point = positions[i];
                float y = (point.y - bounds.min.y) / height;
                float x = Mathf.Abs(point.x - bounds.center.x) / height;
                float hem = 1 - Ease(0.08f, 0.49f, y);
                float sleeve = Ease(0.17f, 0.31f, x) * Ease(0.28f, 0.39f, y) * (1 - Ease(0.59f, 0.72f, y));
                float distance = Mathf.Max(record.hemMaximumDistance * hem, record.cuffMaximumDistance * sleeve);
                if (y >= 0.74f || (x <= 0.16f && y >= 0.49f)) distance = 0;
                if (distance < 0.001f * height) distance = 0;
                coefficients[i].maxDistance = distance;
                coefficients[i].collisionSphereDistance = float.MaxValue;
                if (distance == 0) ++record.fixedParticles;
                else ++record.movingParticles;
            }
            cloth.coefficients = coefficients;
            return record;
        }

        private static float Ease(float min, float max, float value)
        {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(min, max, value));
        }
    }
}
