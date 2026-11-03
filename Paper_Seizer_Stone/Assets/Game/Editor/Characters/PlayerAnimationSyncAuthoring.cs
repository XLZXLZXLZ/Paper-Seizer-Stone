using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaperSeizerStone.Editor
{
    // Asset authoring only. The window invokes this explicitly; importing an FBX
    // never changes the player's animation, scene or presentation automatically.
    public static class PlayerAnimationSyncAuthoring
    {
        public const string DefaultSourcePath = "Assets/Game/art/animation/idle1.fbx";
        public const string TargetModelPath = "Assets/Game/art/act/Player/Models/Player_Rigged.fbx";
        public const string DestinationClipPath = "Assets/Game/art/animation/Player/Clips/Player_Idle1.anim";
        public const string ControllerPath = "Assets/Game/art/animation/Player/Player_Idle1.controller";
        public const string RecordPath = "Assets/Game/art/animation/Player/Player_Idle1_ImportRecord.json";
        private const string HandRecordPath = "Assets/Game/art/animation/Player/Player_FirstPersonHand_ImportRecord.json";
        private const int BoneCount = 67;
        private static readonly string[] PoseProperties =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w"
        };

        public static string GetProblem(string sourcePath, string takeName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "请退出 Play 模式后同步动画。";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "请等待 Unity 编译和资源刷新完成。";
            if (AnimationMode.InAnimationMode()) return "请先关闭 Animation 窗口的 Preview / Record，再同步动画。";
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal)
                || !sourcePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return "请选择工程 Assets 中导入的源 FBX。";
            if (sourcePath == TargetModelPath) return "请选择导出的动作 FBX，目标玩家模型用于骨架适配。";
            var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null || AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null) return "源 FBX 尚未完成导入。";
            if (!importer.importAnimation) return "请在源 FBX 的 Animation 页开启 Import Animation 并 Apply。";
            if (importer.animationType != ModelImporterAnimationType.Generic) return "当前玩家骨架使用 Generic；请选择相同骨架的 Generic 动作 FBX。";
            if (string.IsNullOrEmpty(takeName)) return "请选择要同步的动画片段。";
            var clips = SourceClips(sourcePath).Where(c => c.name == takeName).ToArray();
            if (clips.Length != 1) return "未找到唯一的源片段「" + takeName + "」，请刷新来源并重新选择。";
            if (!PositiveFinite(clips[0].length) || !PositiveFinite(clips[0].frameRate)) return "源动画需要有效的时长和帧率。";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TargetModelPath) == null) return "找不到现有玩家骨架模型。";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(DestinationClipPath) == null) return "找不到现有 Player_Idle1.anim，请恢复该资源后再同步。";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) return "找不到现有玩家 Animator Controller。";
            try
            {
                FindIdleStates(controller);
                foreach (string path in new[] { DestinationClipPath, ControllerPath, RecordPath, HandRecordPath })
                    if (File.Exists(ProjectFile(path)) && (File.GetAttributes(ProjectFile(path)) & FileAttributes.ReadOnly) != 0)
                        return "资源为只读，无法保存：" + path;
            }
            catch (Exception exception) { return exception.Message; }
            return null;
        }

        public static PlayerAnimationSyncResult Synchronize(string sourcePath, string takeName)
        {
            string problem = GetProblem(sourcePath, takeName);
            if (problem != null) throw new InvalidOperationException(problem);
            SyncSnapshot snapshot = null;
            bool committing = false;
            try
            {
                EditorUtility.DisplayProgressBar("角色动画同步", "重新导入源动作…", 0f);
                string sourceHash = FileHash(sourcePath);
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var sourceClip = SourceClips(sourcePath).SingleOrDefault(c => c.name == takeName);
                if (sourceClip == null || !PositiveFinite(sourceClip.length) || !PositiveFinite(sourceClip.frameRate))
                    throw new InvalidOperationException("源片段已发生变化，请刷新来源并重新选择动作。");
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                var states = FindIdleStates(controller);
                var destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(DestinationClipPath);
                var baked = Bake(sourcePath, sourceClip);
                if (FileHash(sourcePath) != sourceHash)
                    throw new InvalidOperationException("源 FBX 在同步期间被重新导出；原播放片段尚未修改，请导出结束后重新同步。");

                var result = new PlayerAnimationSyncResult
                {
                    source = sourcePath, sourceTake = takeName, clip = DestinationClipPath, controller = ControllerPath,
                    bones = BoneCount, samples = baked.Samples, length = sourceClip.length, frameRate = sourceClip.frameRate,
                    boneLengthRatio = baked.BoneLengthRatio, animationBindings = baked.Bindings.Length,
                    constantScaleBindings = BoneCount * 3, sourceSha256 = sourceHash,
                    sourceBytes = new FileInfo(ProjectFile(sourcePath)).Length,
                    synchronizedLocal = DateTimeOffset.Now.ToString("o"),
                    sourceImporterCompression = ((ModelImporter)AssetImporter.GetAtPath(sourcePath)).animationCompression.ToString()
                };

                snapshot = new SyncSnapshot(destination, states);
                snapshot.WriteBackup();
                committing = true;
                EditorUtility.DisplayProgressBar("角色动画同步", "保存玩家动画与循环状态…", 0.85f);
                var settings = AnimationUtility.GetAnimationClipSettings(destination);
                destination.ClearCurves();
                AnimationUtility.SetEditorCurves(destination, baked.Bindings, baked.Curves);
                destination.EnsureQuaternionContinuity();
                destination.frameRate = result.frameRate;
                destination.legacy = false;
                destination.wrapMode = WrapMode.ClampForever;
                settings.startTime = 0f;
                settings.stopTime = result.length;
                settings.loopTime = false;
                settings.loopBlend = false;
                settings.loopBlendOrientation = false;
                settings.loopBlendPositionY = false;
                settings.loopBlendPositionXZ = false;
                AnimationUtility.SetAnimationClipSettings(destination, settings);
                EditorUtility.SetDirty(destination);
                AssetDatabase.SaveAssetIfDirty(destination);
                AssetDatabase.ImportAsset(DestinationClipPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                // Set the actual loaded states as well as saving their references.
                // A stale in-memory state can otherwise still point at the raw FBX.
                destination = AssetDatabase.LoadAssetAtPath<AnimationClip>(DestinationClipPath);
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                states = FindIdleStates(controller);
                foreach (var state in states)
                {
                    state.motion = destination;
                    EditorUtility.SetDirty(state);
                }
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                WriteRecords(result);
                return result;
            }
            catch (Exception exception)
            {
                if (!committing || snapshot == null) throw;
                try { snapshot.Restore(); }
                catch (Exception restoreException)
                {
                    Debug.LogException(restoreException);
                    throw new InvalidOperationException("同步未完成，自动恢复失败。同步前的文件备份位于：" + snapshot.BackupFolder + "。原因：" + exception.Message, exception);
                }
                throw new InvalidOperationException("同步未完成，已恢复同步前的动画、状态引用和记录。原因：" + exception.Message, exception);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        internal static BakedPose Bake(string sourcePath, AnimationClip sourceClip)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath), preview);
                var target = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TargetModelPath), preview);
                foreach (var animator in source.GetComponentsInChildren<Animator>(true).Concat(target.GetComponentsInChildren<Animator>(true)))
                    animator.enabled = false;
                sourceClip.SampleAnimation(source, 0f);
                Transform sourceHips = FindPlayerHips(source);
                Transform targetHips = FindPlayerHips(target);
                var sourceBones = sourceHips.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, sourceHips), t => t, StringComparer.Ordinal);
                var targetBones = targetHips.GetComponentsInChildren<Transform>(true)
                    .OrderBy(t => Depth(AnimationUtility.CalculateTransformPath(t, targetHips)))
                    .ThenBy(t => AnimationUtility.CalculateTransformPath(t, targetHips), StringComparer.Ordinal).ToArray();
                if (targetBones.Length != BoneCount) throw new InvalidOperationException("目标玩家骨架已改变，当前工具需要原有 67 根骨骼。");
                var matching = new Transform[targetBones.Length];
                for (int b = 0; b < targetBones.Length; b++)
                {
                    string path = AnimationUtility.CalculateTransformPath(targetBones[b], targetHips);
                    if (!sourceBones.TryGetValue(path, out matching[b]))
                        throw new InvalidOperationException("源动作缺少玩家骨骼：Hips/" + path + "。请保持导出骨架名称和层级。");
                    RequireUniformScale(targetBones[b].localScale, targetBones[b].name);
                    RequireUniformScale(matching[b].localScale, matching[b].name);
                }
                Transform sourceRoot = sourceHips.parent.parent;
                var sourceParents = Ancestors(sourceHips.parent).ToArray();
                foreach (var parent in sourceParents.Concat(Ancestors(targetHips.parent)))
                    RequireUniformScale(parent.localScale, parent.name);
                var parentScales = sourceParents.Select(t => t.localScale).ToArray();
                var sourceScales = matching.Select(t => t.localScale).ToArray();
                var restScales = targetBones.Select(t => t.localScale).ToArray();
                Vector3 sourceAnchor = sourceHips.position;
                Vector3 targetAnchor = targetHips.position;
                // The armature axes establish alignment. Using animated Hips.rotation
                // here would cancel an authored pose rather than convert coordinates.
                Quaternion alignment = targetHips.parent.parent.rotation * Quaternion.Inverse(sourceRoot.rotation);
                float ratio = BoneLengthRatio(matching, targetBones);
                double intervals = Math.Round((double)sourceClip.length * sourceClip.frameRate);
                if ((intervals + 1) * BoneCount * PoseProperties.Length > 2000000)
                    throw new InvalidOperationException("源动作的采样数据过大，请将动作分段或降低导入帧率后再同步。");
                int samples = Math.Max(2, (int)intervals + 1);
                var keys = new Keyframe[BoneCount, PoseProperties.Length][];
                var previous = new Quaternion[BoneCount];
                for (int b = 0; b < BoneCount; b++)
                    for (int p = 0; p < PoseProperties.Length; p++) keys[b, p] = new Keyframe[samples];

                for (int frame = 0; frame < samples; frame++)
                {
                    float time = sourceClip.length * frame / (samples - 1);
                    sourceClip.SampleAnimation(source, time);
                    for (int parent = 0; parent < sourceParents.Length; parent++)
                        RequireConstantScale(sourceParents[parent], parentScales[parent]);
                    for (int b = 0; b < BoneCount; b++)
                    {
                        RequireConstantScale(matching[b], sourceScales[b]);
                        Transform bone = targetBones[b];
                        bone.localScale = restScales[b];
                        bone.position = targetAnchor + alignment * ((matching[b].position - sourceAnchor) * ratio);
                        bone.rotation = alignment * matching[b].rotation;
                        Vector3 position = bone.localPosition;
                        Quaternion rotation = bone.localRotation;
                        if (frame > 0 && Quaternion.Dot(previous[b], rotation) < 0f)
                            rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                        previous[b] = rotation;
                        float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                        for (int p = 0; p < values.Length; p++)
                        {
                            if (!Finite(values[p])) throw new InvalidOperationException("源动作包含无效骨骼数据：" + bone.name);
                            keys[b, p][frame] = new Keyframe(time, values[p]);
                        }
                    }
                    if (frame % Math.Max(1, samples / 25) == 0)
                        EditorUtility.DisplayProgressBar("角色动画同步", "采样动作 " + (frame + 1) + " / " + samples, 0.1f + 0.6f * frame / (samples - 1));
                }

                var bindings = new List<EditorCurveBinding>(BoneCount * 10);
                var curves = new List<AnimationCurve>(BoneCount * 10);
                for (int b = 0; b < BoneCount; b++)
                {
                    string path = AnimationUtility.CalculateTransformPath(targetBones[b], target.transform);
                    for (int p = 0; p < PoseProperties.Length; p++)
                    {
                        var curve = new AnimationCurve(keys[b, p]);
                        for (int k = 0; k < curve.length; k++)
                        {
                            AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        }
                        bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), PoseProperties[p]));
                        curves.Add(curve);
                    }
                    string[] scaleNames = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
                    for (int axis = 0; axis < 3; axis++)
                    {
                        bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), scaleNames[axis]));
                        curves.Add(AnimationCurve.Constant(0f, sourceClip.length, restScales[b][axis]));
                    }
                }
                return new BakedPose
                {
                    Bindings = bindings.ToArray(), Curves = curves.ToArray(), Samples = samples, BoneLengthRatio = ratio,
                    SourceAnchor = sourceAnchor, TargetAnchor = targetAnchor, Alignment = alignment
                };
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static float BoneLengthRatio(Transform[] source, Transform[] target)
        {
            var ratios = new List<float>();
            for (int b = 1; b < target.Length; b++)
            {
                // Finger-root offsets can be authored translation in the take.
                // Estimate one unit conversion from the torso/limbs instead of
                // allowing small hand joints or terminal helpers to bias it.
                if (!IsUnitReferenceBone(target[b])) continue;
                float from = Vector3.Distance(source[b].position, source[b].parent.position);
                float to = Vector3.Distance(target[b].position, target[b].parent.position);
                if (from > 0.0000001f && to > 0.0001f) ratios.Add(to / from);
            }
            if (ratios.Count == 0) throw new InvalidOperationException("无法从源动作中取得有效的玩家骨架比例。");
            ratios.Sort();
            float ratio = ratios[ratios.Count / 2];
            if (!PositiveFinite(ratio)) throw new InvalidOperationException("源动作的骨架单位比例无效。");
            // These transforms may already contain the first animation pose,
            // even before SampleAnimation is called. They are not a bind-pose
            // reference: comparing every animated joint distance against the
            // target rest distance would reject valid translation animation.
            // Hierarchy, scale and finite-value checks remain in Bake().
            return ratio;
        }

        private static bool IsUnitReferenceBone(Transform bone)
        {
            if (bone.name.EndsWith("End", StringComparison.Ordinal)) return false;
            for (Transform parent = bone.parent; parent != null; parent = parent.parent)
                if (parent.name == "Left_Hand" || parent.name == "Right_Hand") return false;
            return true;
        }

        private static Transform FindPlayerHips(GameObject root)
        {
            var hips = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "Hips" && t.parent != null && t.parent.name == "Player_Rig").ToArray();
            if (hips.Length != 1 || hips[0].parent.parent == null)
                throw new InvalidOperationException("未找到唯一的 Player_Root/Player_Rig/Hips，请使用现有玩家骨架导出的 FBX。");
            return hips[0];
        }

        private static AnimatorState[] FindIdleStates(AnimatorController controller)
        {
            if (controller == null || controller.layers.Length == 0) throw new InvalidOperationException("玩家 Controller 没有有效的动画层。");
            var states = AllStates(controller.layers[0].stateMachine).ToArray();
            var first = states.Where(s => s.name == "Idle1").ToArray();
            var second = states.Where(s => s.name == "Idle1_Return").ToArray();
            if (first.Length != 1 || second.Length != 1)
                throw new InvalidOperationException("玩家 Controller 需要唯一的 Idle1 与 Idle1_Return 状态，请恢复原循环状态后再同步。");
            return new[] { first[0], second[0] };
        }

        private static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            foreach (var child in machine.states) yield return child.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in AllStates(child.stateMachine)) yield return state;
        }

        private static IEnumerable<Transform> Ancestors(Transform start)
        {
            for (Transform current = start; current != null; current = current.parent) yield return current;
        }

        private static IEnumerable<AnimationClip> SourceClips(string path) => AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase));
        private static int Depth(string path) => string.IsNullOrEmpty(path) ? 0 : path.Count(c => c == '/') + 1;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool PositiveFinite(float value) => Finite(value) && value > 0f;
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

        private static void RequireUniformScale(Vector3 scale, string name)
        {
            if (!PositiveFinite(scale.x) || !PositiveFinite(scale.y) || !PositiveFinite(scale.z)
                || Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) - Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)) > scale.magnitude * 0.001f)
                throw new InvalidOperationException("骨架包含非均匀、负值或无效缩放：" + name + "。请使用原骨架导出动作。");
        }

        private static void RequireConstantScale(Transform bone, Vector3 baseline)
        {
            Vector3 scale = bone.localScale;
            if (!Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z)
                || (scale - baseline).sqrMagnitude > Mathf.Max(0.00000001f, baseline.sqrMagnitude * 0.000001f))
                throw new InvalidOperationException("源动作包含骨骼缩放变化：" + bone.name + "。当前同步支持原骨架的位移和旋转动画。");
        }

        private static string FileHash(string path)
        {
            using (var stream = File.OpenRead(ProjectFile(path)))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteRecords(PlayerAnimationSyncResult result)
        {
            File.WriteAllText(ProjectFile(RecordPath), JsonUtility.ToJson(result, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(RecordPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (!File.Exists(ProjectFile(HandRecordPath))) return;
            var hand = JsonUtility.FromJson<HandRecord>(File.ReadAllText(ProjectFile(HandRecordPath)));
            if (hand == null) throw new InvalidOperationException("无法读取现有手部制作记录。");
            hand.source = result.source;
            hand.sourceSha256 = result.sourceSha256;
            hand.animationBindings = result.animationBindings;
            hand.animationLength = result.length;
            hand.animationSamples = result.samples;
            hand.animationFrameRate = result.frameRate;
            hand.synchronizedLocal = result.synchronizedLocal;
            File.WriteAllText(ProjectFile(HandRecordPath), JsonUtility.ToJson(hand, true) + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(HandRecordPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        internal sealed class BakedPose
        {
            public EditorCurveBinding[] Bindings;
            public AnimationCurve[] Curves;
            public int Samples;
            public float BoneLengthRatio;
            public Vector3 SourceAnchor, TargetAnchor;
            public Quaternion Alignment;
        }

        // One rolling backup lives outside Assets; it is never imported as a
        // second animation and does not accumulate a new large clip per click.
        private sealed class SyncSnapshot
        {
            public readonly string BackupFolder = ProjectFile("UserSettings/PlayerAnimationSyncBackup");
            private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            private readonly EditorCurveBinding[] bindings;
            private readonly AnimationCurve[] curves;
            private readonly EditorCurveBinding[] objectBindings;
            private readonly ObjectReferenceKeyframe[][] objectKeys;
            private readonly AnimationClipSettings settings;
            private readonly float frameRate;
            private readonly WrapMode wrapMode;
            private readonly bool legacy;
            private readonly string[] stateNames;
            private readonly Motion[] motions;
            private readonly string[] motionPaths;
            private readonly string[] motionGuids;
            private readonly long[] motionIds;

            public SyncSnapshot(AnimationClip clip, AnimatorState[] states)
            {
                stateNames = states.Select(s => s.name).ToArray();
                motions = states.Select(s => s.motion).ToArray();
                motionPaths = motions.Select(m => m != null ? AssetDatabase.GetAssetPath(m) : string.Empty).ToArray();
                motionGuids = new string[motions.Length];
                motionIds = new long[motions.Length];
                for (int i = 0; i < motions.Length; i++)
                    if (motions[i] != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(motions[i], out string guid, out long fileId))
                    {
                        motionGuids[i] = guid;
                        motionIds[i] = fileId;
                    }
                bindings = AnimationUtility.GetCurveBindings(clip);
                curves = bindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
                objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                objectKeys = objectBindings.Select(b => AnimationUtility.GetObjectReferenceCurve(clip, b)).ToArray();
                settings = AnimationUtility.GetAnimationClipSettings(clip);
                frameRate = clip.frameRate;
                wrapMode = clip.wrapMode;
                legacy = clip.legacy;
                foreach (string path in new[] { DestinationClipPath, ControllerPath, RecordPath, HandRecordPath })
                {
                    files[path] = File.Exists(ProjectFile(path)) ? File.ReadAllBytes(ProjectFile(path)) : null;
                    files[path + ".meta"] = File.Exists(ProjectFile(path + ".meta")) ? File.ReadAllBytes(ProjectFile(path + ".meta")) : null;
                }
            }

            public void WriteBackup()
            {
                Directory.CreateDirectory(BackupFolder);
                foreach (var file in files)
                {
                    string path = Path.Combine(BackupFolder, Path.GetFileName(file.Key));
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
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(DestinationClipPath);
                clip.ClearCurves();
                AnimationUtility.SetEditorCurves(clip, bindings, curves);
                for (int i = 0; i < objectBindings.Length; i++)
                    AnimationUtility.SetObjectReferenceCurve(clip, objectBindings[i], objectKeys[i]);
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                clip.frameRate = frameRate;
                clip.wrapMode = wrapMode;
                clip.legacy = legacy;
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                var states = FindIdleStates(controller);
                for (int i = 0; i < stateNames.Length; i++)
                {
                    Motion motion = motions[i];
                    string path = !string.IsNullOrEmpty(motionGuids[i]) ? AssetDatabase.GUIDToAssetPath(motionGuids[i]) : motionPaths[i];
                    if (!string.IsNullOrEmpty(path))
                        motion = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Motion>()
                            .FirstOrDefault(m => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out string guid, out long fileId) && fileId == motionIds[i]);
                    if (motion == null && !string.IsNullOrEmpty(motionPaths[i]))
                        throw new InvalidOperationException("无法恢复原动画引用：" + motionPaths[i]);
                    var state = states.Single(s => s.name == stateNames[i]);
                    state.motion = motion;
                    EditorUtility.SetDirty(state);
                }
                EditorUtility.SetDirty(clip);
                EditorUtility.SetDirty(controller);
            }
        }

        [Serializable]
        private sealed class HandRecord
        {
            public string player, clip, mesh, playback, cards, visibility;
            public int vertices, triangles, animationBindings;
            public float loopBlendSeconds;
            public Vector3 anchorLocalPosition, anchorLocalEulerAngles;
            public bool loop, playModeRun;
            public string poseAuthority, loopDriver;
            public float controllerTransitionSeconds;
            public string source, sourceSha256;
            public float animationLength;
            public int animationSamples;
            public float animationFrameRate;
            public string synchronizedLocal, gripEditor;
        }
    }

    [Serializable]
    public sealed class PlayerAnimationSyncResult
    {
        public string source, sourceTake, clip, controller, sourceSha256, synchronizedLocal;
        public int bones, samples, animationBindings, constantScaleBindings;
        public float length, frameRate, boneLengthRatio;
        public long sourceBytes;
        public string sourceImporterCompression;
        public string prefab = "Assets/Game/art/act/Player/Prefabs/Player_Hair.prefab";
        public string coordinates = "Source world poses baked into existing player bone paths; stage roots remain unchanged";
        public string poseAuthority = "Imported animation only";
        public string sampling = "Full imported player motion including both endpoints; target baseline scales; no IK or manual seam edits";
        public string loopDriver = "Animator: Idle1 <-> Idle1_Return";
        public bool loop = true;
        public bool rootMotion, playModeRun, loopPose, clipLoopTime;
    }
}
