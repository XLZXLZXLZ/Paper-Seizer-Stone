using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.Editor
{
    // Explicit saved-asset authoring, no menu or runtime scene construction.
    public static class GuaiwuEntranceAuthoring
    {
        const string Folder = "Assets/Game/art/animation/Guaiwu/";
        const string BasePrefab = "Assets/Game/art/act/Guaiwu/Prefabs/Guaiwu_Cloth.prefab";
        const string RuntimePrefab = "Assets/Game/art/act/Guaiwu/Prefabs/Guaiwu_Cloth_Entrance.prefab";
        const string Controller = Folder + "Guaiwu_Entrance.controller";
        struct Pose { public Vector3 position, scale; public Quaternion rotation; }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请先退出 Play 和 Animation Preview。");
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject actor = null, source = null;
            try
            {
                actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab), scene);
                source = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/art/act/Guaiwu/Models/Guaiwu_Rigged.fbx"), scene);
                Animator animator = actor.GetComponentsInChildren<Animator>(true).Single();
                var body = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Guaiwu_Body");
                var original = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Guaiwu_Body").bones.ToDictionary(b => b.name);
                if (body.bones.Length != 67 || original.Count != 67) throw new InvalidOperationException("怪物骨架不是预期的67根骨骼。");
                AnimationClip idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(GuaiwuIdleLoopAuthoring.ClipPath);
                if (idle == null) throw new InvalidOperationException("缺少 Idle 动画。");
                idle.SampleAnimation(animator.gameObject, 0);
                Pose[] end = body.bones.Select(Read).ToArray();
                foreach (Transform b in body.bones)
                {
                    Transform from = original[b.name];
                    b.localPosition = from.localPosition; b.localRotation = from.localRotation; b.localScale = from.localScale;
                }
                // Preserve the Idle root placement and horizontal heading.
                int hipIndex = Array.FindIndex(body.bones, b => b.name == "Hips");
                Transform hip = body.bones[hipIndex];
                Quaternion a = hip.parent.rotation * hip.localRotation, z = hip.parent.rotation * end[hipIndex].rotation;
                Vector3 af = Vector3.ProjectOnPlane(a * Vector3.forward, Vector3.up), zf = Vector3.ProjectOnPlane(z * Vector3.forward, Vector3.up);
                if (af.sqrMagnitude > .0001f && zf.sqrMagnitude > .0001f)
                    hip.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(af, zf, Vector3.up), Vector3.up) * hip.rotation;
                hip.localPosition = end[hipIndex].position;
                Pose[] start = body.bones.Select(Read).ToArray();
                AnimationClip hold = MakeClip(body.bones, animator.transform, start, start, "Guaiwu_A_Pose", true);
                AnimationClip intro = MakeClip(body.bones, animator.transform, start, end, "Guaiwu_A_To_Idle", false);
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
                if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(Controller);
                var machine = controller.layers[0].stateMachine;
                if (machine.states.Length > 0) throw new InvalidOperationException("入场 Controller 已存在配置，停止覆盖。");
                AnimatorState first = machine.AddState("A_Pose"); first.motion = hold; first.writeDefaultValues = false;
                var transition = machine.AddState("A_To_Idle"); transition.motion = intro; transition.writeDefaultValues = false;
                var repeat = machine.AddState("Idle"); repeat.motion = idle; repeat.writeDefaultValues = false;
                machine.defaultState = first;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var entrance = actor.AddComponent<GuaiwuEntrance>(); entrance.Configure(animator);
                actor.name = "Guaiwu_Cloth_Entrance";
                PrefabUtility.SaveAsPrefabAsset(actor, RuntimePrefab, out bool saved);
                if (!saved) throw new InvalidOperationException("入场预制体保存失败。");
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            const string tablePath = "Assets/Game/Prefabs/BattleTable.prefab";
            var table = PrefabUtility.LoadPrefabContents(tablePath);
            try
            {
                var view = table.GetComponentInChildren<TabletopView>(true);
                if (view == null) throw new InvalidOperationException("桌面缺少 TabletopView。");
                var serialized = new SerializedObject(view);
                serialized.FindProperty("defaultOpponentRuntimePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefab);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(table, tablePath, out bool saved);
                if (!saved) throw new InvalidOperationException("桌面入场绑定保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(table); }
            File.WriteAllText(Folder + "Guaiwu_EntranceRecord.json", JsonUtility.ToJson(new Record(), true));
            AssetDatabase.Refresh();
        }
        [Serializable] sealed class Record
        {
            public string runtimePrefab = RuntimePrefab, controller = Controller;
            public string sequence = "A_Pose hold 0.5s -> A_To_Idle normalized 1s clip played over 2s -> existing looping Idle";
            public int bones = 67;
            public bool playTestRun = false, clothRepairClaimed = false;
        }
        static Pose Read(Transform b) => new Pose { position = b.localPosition, rotation = b.localRotation, scale = b.localScale };
        static AnimationClip MakeClip(Transform[] bones, Transform root, Pose[] a, Pose[] b, string name, bool loop)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            for (int bone = 0; bone < bones.Length; ++bone)
            {
                var samples = new float[10, 61];
                for (int frame = 0; frame <= 60; ++frame)
                {
                    float t = Mathf.SmoothStep(0, 1, frame / 60f);
                    Vector3 p = Vector3.Lerp(a[bone].position, b[bone].position, t), s = Vector3.Lerp(a[bone].scale, b[bone].scale, t);
                    Quaternion q = Quaternion.Slerp(a[bone].rotation, b[bone].rotation, t);
                    for (int j = 0; j < 3; ++j) { samples[j, frame] = p[j]; samples[j + 7, frame] = s[j]; }
                    for (int j = 0; j < 4; ++j) samples[j + 3, frame] = q[j];
                }
                for (int channel = 0; channel < 10; ++channel)
                {
                    var keys = new Keyframe[61];
                    for (int frame = 0; frame <= 60; ++frame)
                    {
                        float tangent = frame == 0 || frame == 60 ? 0 : (samples[channel, frame + 1] - samples[channel, frame - 1]) * 30;
                        keys[frame] = new Keyframe(frame / 60f, samples[channel, frame], tangent, tangent);
                    }
                    string property = channel < 3 ? "m_LocalPosition." + "xyz"[channel]
                        : channel < 7 ? "m_LocalRotation." + "xyzw"[channel - 3] : "m_LocalScale." + "xyz"[channel - 7];
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[bone], root), typeof(Transform), property), new AnimationCurve(keys));
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop; settings.startTime = 0; settings.stopTime = 1;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string path = Folder + "Clips/" + name + ".anim";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("动画资源已存在，停止覆盖：" + path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }
    }
}
