using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using PaperSeizerStone.Presentation.Props;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PaperSeizerStone.Editor
{
    public static class PlayerShootingAuthoring
    {
        private const string Folder = "Assets/Game/art/animation/Player/Shooting";
        public const string FireSourcePath = Folder + "/Sources/fire1.fbx";
        public const string GunSourcePath = Folder + "/Sources/gun1.fbx";
        public const string BulletSourcePath = Folder + "/Sources/bullet1.fbx";
        public const string DefinitionPath = Folder + "/PlayerShooting.asset";
        public const string PropsPrefabPath = "Assets/Game/Prefabs/Props/PlayerShootingProps.prefab";
        private const string PlayerPrefabPath = "Assets/Game/art/act/Player/Prefabs/Player_Hair.prefab";
        private const string FireClipPath = Folder + "/Clips/Player_Fire1.anim";
        private const string GunClipPath = Folder + "/Clips/Player_Gun1.anim";
        private const string BulletClipPath = Folder + "/Clips/Player_BulletLoad1.anim";
        private const string RightMeshPath = "Assets/Game/art/act/Player/Meshes/Player_RightForearm_Hand.asset";
        private const string RecordPath = Folder + "/PlayerShooting_ImportRecord.json";
        private const string RevolverPrefabPath = "Assets/Game/Prefabs/Props/Revolver.prefab";
        private const string BulletPrefabPath = "Assets/Game/Prefabs/Props/Bullet.prefab";
        private static readonly string[] PoseProperties =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w"
        };

        public static string GetProblem(string fire, string gun, string bullet, float gunStart, float bulletStart, float fireMoment)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "请退出 Play 模式后配置。";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "请等待 Unity 编译和资源刷新结束。";
            if (AnimationMode.InAnimationMode()) return "请先关闭 Animation 窗口的 Preview / Record。";
            if (!Finite(gunStart) || gunStart < 0f || !Finite(bulletStart) || bulletStart < 0f || !Finite(fireMoment))
                return "动画起始时间需要是有效的非负秒数。";
            try
            {
                foreach (string path in new[] { fire, gun, bullet })
                {
                    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                        || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return "请选择 Assets 中导入的三个动作 FBX。";
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || !importer.importAnimation || importer.animationType != ModelImporterAnimationType.Generic)
                        return "源动作需要开启 Import Animation 并使用 Generic：" + path;
                    UniqueClip(path);
                }
                var body = UniqueClip(fire);
                if (fireMoment > body.length + 0.0001f) return "击发时刻不能晚于人物动画末帧。";
                if (gunStart > body.length || bulletStart > body.length) return "枪与子弹的起点不能晚于人物动画末帧。";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPrefabPath) == null
                    || AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath) == null) return "找不到现有玩家、枪或子弹预制体。";
            }
            catch (Exception exception) { return exception.Message; }
            return null;
        }

        public static PlayerShootingImportResult Synchronize(string fire, string gun, string bullet, float gunStart, float bulletStart, float fireMoment)
        {
            string problem = GetProblem(fire, gun, bullet, gunStart, bulletStart, fireMoment);
            if (problem != null) throw new InvalidOperationException(problem);
            Scene preview = default(Scene);
            GameObject playerContents = null;
            ShotBackup backup = null;
            bool committing = false;
            try
            {
                var paths = new[] { fire, gun, bullet };
                var hashes = paths.Select(Hash).ToArray();
                EditorUtility.DisplayProgressBar("开枪动画配置", "导入三条源动作…", 0f);
                foreach (string path in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AnimationClip fireSource = UniqueClip(fire), gunSource = UniqueClip(gun), bulletSource = UniqueClip(bullet);
                var body = PlayerAnimationSyncAuthoring.Bake(fire, fireSource);
                preview = EditorSceneManager.NewPreviewScene();
                var props = BuildProps(preview, bullet, bulletSource, body);
                var gunData = BakeProp(preview, gun, gunSource, "Revolver_Rig", props.GunRig, props.GunAnimator.transform, body, true);
                var bulletData = BakeProp(preview, bullet, bulletSource, "Bullet_Root", props.BulletRig, props.BulletAnimator.transform, body, false);
                for (int i = 0; i < paths.Length; i++)
                    if (Hash(paths[i]) != hashes[i]) throw new InvalidOperationException("同步期间源 FBX 被重新导出，请等待导出结束后再同步。");

                EnsureFolder(Folder + "/Clips");
                backup = new ShotBackup(new[] { FireClipPath, GunClipPath, BulletClipPath, DefinitionPath, PropsPrefabPath, PlayerPrefabPath, RightMeshPath, RecordPath });
                backup.Save();
                committing = true;
                EditorUtility.DisplayProgressBar("开枪动画配置", "保存动作与演出预制体…", 0.85f);
                AnimationClip characterClip = SaveClip(FireClipPath, body.Bindings, body.Curves, fireSource);
                AnimationClip gunClip = SaveClip(GunClipPath, gunData.Bindings, gunData.Curves, gunSource);
                AnimationClip bulletClip = SaveClip(BulletClipPath, bulletData.Bindings, bulletData.Curves, bulletSource);

                gunClip.SampleAnimation(props.GunAnimator.gameObject, 0f);
                bulletClip.SampleAnimation(props.BulletAnimator.gameObject, 0f);
                props.GunAnimator.enabled = true;
                props.BulletAnimator.enabled = true;
                props.BulletVisual.SetActive(false);
                props.Flash.SetActive(false);
                if (PrefabUtility.SaveAsPrefabAsset(props.Root, PropsPrefabPath) == null)
                    throw new InvalidOperationException("演出道具预制体保存失败。");
                var definition = AssetDatabase.LoadAssetAtPath<PlayerShootingDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<PlayerShootingDefinition>();
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                var serialized = new SerializedObject(definition);
                serialized.FindProperty("characterClip").objectReferenceValue = characterClip;
                serialized.FindProperty("gunClip").objectReferenceValue = gunClip;
                serialized.FindProperty("loadingBulletClip").objectReferenceValue = bulletClip;
                serialized.FindProperty("gunStart").floatValue = gunStart;
                serialized.FindProperty("bulletStart").floatValue = bulletStart;
                serialized.FindProperty("fireMoment").floatValue = fireMoment;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);

                playerContents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
                ConfigurePlayer(playerContents, definition);
                if (PrefabUtility.SaveAsPrefabAsset(playerContents, PlayerPrefabPath) == null)
                    throw new InvalidOperationException("玩家开枪配置预制体保存失败。");
                PrefabUtility.UnloadPrefabContents(playerContents);
                playerContents = null;
                var result = new PlayerShootingImportResult
                {
                    sourceFire = fire, sourceGun = gun, sourceBullet = bullet,
                    fireSha256 = hashes[0], gunSha256 = hashes[1], bulletSha256 = hashes[2],
                    length = fireSource.length, frameRate = fireSource.frameRate, samples = body.Samples,
                    gunStart = gunStart, bulletStart = bulletStart,
                    fireTime = fireMoment < 0f ? fireSource.length : fireMoment,
                    bulletEnd = bulletStart + bulletSource.length,
                    boneLengthRatio = body.BoneLengthRatio, gunRigScale = props.GunRig.localScale,
                    characterBindings = body.Bindings.Length, gunBindings = gunData.Bindings.Length, bulletBindings = bulletData.Bindings.Length,
                    characterClip = FireClipPath, gunClip = GunClipPath, bulletClip = BulletClipPath,
                    definition = DefinitionPath, prefab = PropsPrefabPath, player = PlayerPrefabPath,
                    synchronizedLocal = DateTimeOffset.Now.ToString("o")
                };
                File.WriteAllText(ProjectFile(RecordPath), JsonUtility.ToJson(result, true) + "\n", new UTF8Encoding(false));
                AssetDatabase.ImportAsset(RecordPath, ImportAssetOptions.ForceSynchronousImport);
                return result;
            }
            catch (Exception exception)
            {
                if (playerContents != null)
                {
                    PrefabUtility.UnloadPrefabContents(playerContents);
                    playerContents = null;
                }
                if (committing && backup != null)
                {
                    try { backup.Restore(); }
                    catch (Exception restoreException)
                    {
                        Debug.LogException(restoreException);
                        throw new InvalidOperationException("配置失败，备份位于 " + backup.Folder + "。原因：" + exception.Message, exception);
                    }
                    throw new InvalidOperationException("配置未完成，已恢复原资源。原因：" + exception.Message, exception);
                }
                throw;
            }
            finally
            {
                if (playerContents != null) PrefabUtility.UnloadPrefabContents(playerContents);
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                EditorUtility.ClearProgressBar();
            }
        }

        private static PropPrototype BuildProps(Scene scene, string bulletPath, AnimationClip bulletClip, PlayerAnimationSyncAuthoring.BakedPose space)
        {
            var root = new GameObject("PlayerShootingProps");
            SceneManager.MoveGameObjectToScene(root, scene);
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RevolverPrefabPath), scene);
            PrefabUtility.UnpackPrefabInstance(gun, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            gun.name = "Gun";
            gun.transform.SetParent(root.transform, false);
            Identity(gun.transform);
            var gunAnimator = gun.GetComponentsInChildren<Animator>(true).Single();
            Identity(gunAnimator.transform);
            ConfigureAnimator(gunAnimator);
            Transform gunRig = FindUnique(gun, "Revolver_Rig");
            Transform muzzle = FindUnique(gun, "Socket_Muzzle");
            GameObject flash = FindUnique(gun, "MuzzleFlash").gameObject;
            Vector3 flashScale = flash.transform.localScale;
            flash.transform.SetParent(muzzle, false);
            Identity(flash.transform);
            flash.transform.localScale = flashScale;
            flash.SetActive(false);

            var bulletActor = new GameObject("LoadingBullet");
            bulletActor.transform.SetParent(root.transform, false);
            var bulletAnimator = bulletActor.AddComponent<Animator>();
            ConfigureAnimator(bulletAnimator);
            var bulletRig = new GameObject("Bullet_Root").transform;
            bulletRig.SetParent(bulletActor.transform, false);
            var tip = new GameObject("Bullet_Tip_End").transform;
            tip.SetParent(bulletRig, false);
            var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(bulletPath), scene);
            Vector3 direction;
            float length;
            try
            {
                foreach (var animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                bulletClip.SampleAnimation(source, 0f);
                Transform from = FindUnique(source, "Bullet_Root"), to = FindUnique(source, "Bullet_Tip_End");
                direction = to.localPosition.normalized;
                length = Vector3.Distance(from.position, to.position) * space.BoneLengthRatio;
                if (direction.sqrMagnitude < 0.5f || !Finite(length) || length <= 0f)
                    throw new InvalidOperationException("子弹骨架没有有效的 Root 到 Tip 长度。");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
            tip.localPosition = direction * length;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath), scene);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "BulletVisual";
            visual.transform.SetParent(bulletRig, false);
            Identity(visual.transform);
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            Bounds bounds = LocalMeshBounds(visual);
            float scale = length / Mathf.Max(0.000001f, bounds.size.y);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction);
            visual.transform.localRotation = rotation;
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = -(rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)) * scale;
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = PlayerVisibilityLayer.HandsLayer;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            return new PropPrototype
            {
                Root = root, GunAnimator = gunAnimator, GunRig = gunRig,
                BulletAnimator = bulletAnimator, BulletRig = bulletRig, BulletVisual = visual, Flash = flash
            };
        }

        private static PropPose BakeProp(Scene scene, string path, AnimationClip clip, string rigName, Transform targetRig,
            Transform animationRoot, PlayerAnimationSyncAuthoring.BakedPose space, bool fitGun)
        {
            var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
            try
            {
                foreach (var animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                clip.SampleAnimation(source, 0f);
                Transform sourceRig = FindUnique(source, rigName);
                var sourceBones = sourceRig.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, sourceRig), t => t, StringComparer.Ordinal);
                var targets = targetRig.GetComponentsInChildren<Transform>(true)
                    .Where(t => sourceBones.ContainsKey(AnimationUtility.CalculateTransformPath(t, targetRig)))
                    .OrderBy(t => Depth(AnimationUtility.CalculateTransformPath(t, targetRig)))
                    .ThenBy(t => AnimationUtility.CalculateTransformPath(t, targetRig), StringComparer.Ordinal).ToArray();
                if (targets.Length != sourceBones.Count) throw new InvalidOperationException("道具骨架与源动画不匹配：" + rigName);
                var matching = targets.Select(t => sourceBones[AnimationUtility.CalculateTransformPath(t, targetRig)]).ToArray();
                if (fitGun) FitGunScale(targetRig, targets, matching, space.BoneLengthRatio);
                var scales = targets.Select(t => t.localScale).ToArray();
                int samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * clip.frameRate) + 1);
                var keys = new Keyframe[targets.Length, PoseProperties.Length][];
                var previous = new Quaternion[targets.Length];
                for (int b = 0; b < targets.Length; b++)
                    for (int p = 0; p < PoseProperties.Length; p++) keys[b, p] = new Keyframe[samples];
                for (int frame = 0; frame < samples; frame++)
                {
                    float time = clip.length * frame / (samples - 1);
                    clip.SampleAnimation(source, time);
                    for (int b = 0; b < targets.Length; b++)
                    {
                        Transform bone = targets[b];
                        bone.localScale = scales[b];
                        bone.position = space.TargetAnchor + space.Alignment * ((matching[b].position - space.SourceAnchor) * space.BoneLengthRatio);
                        bone.rotation = space.Alignment * matching[b].rotation;
                        Vector3 position = bone.localPosition;
                        Quaternion rotation = bone.localRotation;
                        if (frame > 0 && Quaternion.Dot(previous[b], rotation) < 0f)
                            rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        previous[b] = rotation;
                        float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                        for (int p = 0; p < PoseProperties.Length; p++)
                        {
                            if (!Finite(values[p])) throw new InvalidOperationException("道具动画中有无效数据：" + bone.name);
                            keys[b, p][frame] = new Keyframe(time, values[p]);
                        }
                    }
                }
                var bindings = new List<EditorCurveBinding>();
                var curves = new List<AnimationCurve>();
                for (int b = 0; b < targets.Length; b++)
                {
                    string bonePath = AnimationUtility.CalculateTransformPath(targets[b], animationRoot);
                    for (int p = 0; p < PoseProperties.Length; p++)
                    {
                        var curve = new AnimationCurve(keys[b, p]);
                        for (int k = 0; k < curve.length; k++)
                        {
                            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        }
                        bindings.Add(EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), PoseProperties[p]));
                        curves.Add(curve);
                    }
                    string[] scaleNames = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
                    for (int axis = 0; axis < 3; axis++)
                    {
                        bindings.Add(EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), scaleNames[axis]));
                        curves.Add(AnimationCurve.Constant(0f, clip.length, scales[b][axis]));
                    }
                }
                return new PropPose { Bindings = bindings.ToArray(), Curves = curves.ToArray() };
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static void FitGunScale(Transform root, Transform[] target, Transform[] source, float unitRatio)
        {
            var ratios = new List<float>();
            string[] references = { "Hammer", "Trigger", "Socket_Grip", "Socket_Muzzle" };
            for (int b = 0; b < target.Length; b++)
            {
                if (!references.Contains(target[b].name)) continue;
                float from = Vector3.Distance(source[b].position, source[0].position) * unitRatio;
                float to = Vector3.Distance(target[b].position, target[0].position);
                if (from > 0.000001f && to > 0.000001f) ratios.Add(from / to);
            }
            if (ratios.Count == 0) throw new InvalidOperationException("无法匹配源枪械与现有模型的尺寸。");
            ratios.Sort();
            float scale = ratios[ratios.Count / 2];
            if (!Finite(scale) || scale <= 0f) throw new InvalidOperationException("枪械尺寸比例无效。");
            root.localScale *= scale;
        }

        private static AnimationClip SaveClip(string path, EditorCurveBinding[] bindings, AnimationCurve[] curves, AnimationClip source)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            AnimationUtility.SetEditorCurves(clip, bindings, curves);
            clip.EnsureQuaternionContinuity();
            clip.frameRate = source.frameRate;
            clip.legacy = false;
            clip.wrapMode = WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = source.length;
            settings.loopTime = false;
            settings.loopBlend = false;
            settings.loopBlendOrientation = false;
            settings.loopBlendPositionY = false;
            settings.loopBlendPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        private static void ConfigurePlayer(GameObject player, PlayerShootingDefinition definition)
        {
            Transform model = player.transform.Find("Player_Rigged");
            if (model == null) throw new InvalidOperationException("玩家预制体缺少 Player_Rigged。");
            Transform arms = model.Find("FirstPersonArms");
            if (arms == null) throw new InvalidOperationException("玩家预制体缺少现有第一人称手部容器。");
            var right = PlayerFirstPersonArmAuthoring.EnsureRightArm(player);
            Transform existing = arms.Find("PlayerShootingProps");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var props = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabPath), arms);
            props.name = "PlayerShootingProps";
            Identity(props.transform);
            var sequence = player.GetComponent<PlayerShootingSequence>();
            if (sequence == null) sequence = player.AddComponent<PlayerShootingSequence>();
            var serialized = new SerializedObject(sequence);
            SetObject(serialized, "definition", definition);
            SetObject(serialized, "characterAnimator", model.GetComponent<Animator>());
            SetObject(serialized, "gunAnimator", props.transform.Find("Gun").GetComponentInChildren<Animator>(true));
            SetObject(serialized, "bulletAnimator", props.transform.Find("LoadingBullet").GetComponent<Animator>());
            SetObject(serialized, "propsRoot", props.transform);
            SetObject(serialized, "rightArm", right);
            SetObject(serialized, "grip", player.GetComponent<PlayerCardGrip>());
            SetObject(serialized, "loadingBulletVisual", FindUnique(props, "BulletVisual").gameObject);
            SetObject(serialized, "muzzleFlash", FindUnique(props, "MuzzleFlash").gameObject);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            props.SetActive(false);
            right.gameObject.SetActive(false);
        }

        private static void SetObject(SerializedObject target, string name, UnityEngine.Object value)
        {
            var property = target.FindProperty(name);
            if (property == null) throw new InvalidOperationException("演出组件字段不存在：" + name);
            property.objectReferenceValue = value;
        }

        private static Bounds LocalMeshBounds(GameObject root)
        {
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = matrix.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("子弹预制体没有可复用的外观网格。");
            return bounds;
        }

        private static void ConfigureAnimator(Animator animator)
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;
        }

        private static AnimationClip UniqueClip(string path)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (clips.Length != 1 || !Finite(clips[0].length) || clips[0].length <= 0f || !Finite(clips[0].frameRate) || clips[0].frameRate <= 0f)
                throw new InvalidOperationException("源 FBX 需要一个有效的动作片段：" + path);
            return clips[0];
        }
        private static Transform FindUnique(GameObject root, string name)
        {
            var matches = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("找不到唯一的道具节点：" + name);
            return matches[0];
        }
        private static void Identity(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
        private static int Depth(string path) => string.IsNullOrEmpty(path) ? 0 : path.Count(c => c == '/') + 1;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(ProjectFile(path)))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private sealed class PropPrototype
        {
            public GameObject Root, BulletVisual, Flash;
            public Animator GunAnimator, BulletAnimator;
            public Transform GunRig, BulletRig;
        }
        private sealed class PropPose { public EditorCurveBinding[] Bindings; public AnimationCurve[] Curves; }

        private sealed class ShotBackup
        {
            public readonly string Folder = ProjectFile("UserSettings/PlayerShootingBackup");
            private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            public ShotBackup(IEnumerable<string> paths)
            {
                foreach (string path in paths)
                    foreach (string item in new[] { path, path + ".meta" })
                        files[item] = File.Exists(ProjectFile(item)) ? File.ReadAllBytes(ProjectFile(item)) : null;
            }
            public void Save()
            {
                Directory.CreateDirectory(Folder);
                foreach (var file in files)
                {
                    string path = Path.Combine(Folder, Path.GetFileName(file.Key));
                    if (file.Value != null) File.WriteAllBytes(path, file.Value);
                    else if (File.Exists(path)) File.Delete(path);
                }
            }
            public void Restore()
            {
                foreach (var file in files)
                {
                    if (file.Value != null) File.WriteAllBytes(ProjectFile(file.Key), file.Value);
                    else if (File.Exists(ProjectFile(file.Key))) File.Delete(ProjectFile(file.Key));
                }
                foreach (string path in files.Keys.Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)))
                    if (File.Exists(ProjectFile(path))) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }
    }

    [Serializable]
    public sealed class PlayerShootingImportResult
    {
        public string sourceFire, sourceGun, sourceBullet, fireSha256, gunSha256, bulletSha256;
        public float length, frameRate, gunStart, bulletStart, bulletEnd, fireTime;
        public int samples;
        public float boneLengthRatio;
        public Vector3 gunRigScale;
        public int characterBindings, gunBindings, bulletBindings;
        public string characterClip, gunClip, bulletClip, definition, prefab, player, synchronizedLocal;
        public string bulletRole = "Load into cylinder; visual prop only, independent of hit/empty result";
        public bool playModeRun;
    }
}
