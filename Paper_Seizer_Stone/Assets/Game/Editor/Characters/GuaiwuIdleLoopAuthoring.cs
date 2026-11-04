using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Explicit asset authoring only. The caller owns backups and merges the returned record.
    public static class GuaiwuIdleLoopAuthoring
    {
        public const string ClipPath = "Assets/Game/art/animation/Guaiwu/Clips/Guaiwu_Idle.anim";
        public const string ControllerPath = "Assets/Game/art/animation/Guaiwu/Guaiwu_Idle.controller";
        public const string RecordPath = "Assets/Game/art/animation/Guaiwu/Guaiwu_Idle_ImportRecord.json";
        public const float ClosureDuration = 0.8f;
        private const float TimeTolerance = 0.0001f;
        private const string Position = "m_LocalPosition.";
        private const string Rotation = "m_LocalRotation.";
        private const string Scale = "m_LocalScale.";

        public static GuaiwuIdleLoopResult Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请退出 Play 和 Animation Preview 后制作怪物待机闭环。");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("请等待编译和资源刷新结束。");

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (clip == null || controller == null) throw new InvalidOperationException("找不到现有怪物待机动画或 Controller。");
            string recordFile = Path.Combine(Directory.GetParent(Application.dataPath).FullName, RecordPath);
            if (!File.Exists(recordFile)) throw new InvalidOperationException("缺少原始怪物待机制作记录，不能确定追加起点。");
            var record = JsonUtility.FromJson<OriginalRecord>(File.ReadAllText(recordFile));
            if (record == null) throw new InvalidOperationException("怪物待机制作记录无效。");
            float originalLength = record.originalLength > 0f ? record.originalLength : record.length;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (!Finite(originalLength) || originalLength <= 0f || !Finite(clip.frameRate) || clip.frameRate <= 0f)
                throw new InvalidOperationException("原动作长度或采样率无效。");
            if (record.loopClosureDuration > 0f || settings.loopTime
                || clip.length > originalLength + TimeTolerance || settings.stopTime > originalLength + TimeTolerance)
                throw new InvalidOperationException("当前片段已包含闭环或已延长；请恢复原动作与记录后制作，避免重复追加。");
            if (Mathf.Abs(clip.length - originalLength) > TimeTolerance || Mathf.Abs(settings.startTime) > TimeTolerance)
                throw new InvalidOperationException("当前片段范围与制作记录的原动作长度不一致。");

            AnimatorStateMachine machine = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
            AnimatorState first = machine != null ? machine.states.Select(s => s.state).SingleOrDefault(s => s.name == "Idle") : null;
            AnimatorState repeat = machine != null ? machine.states.Select(s => s.state).SingleOrDefault(s => s.name == "Idle_Return") : null;
            if (first == null || repeat == null || machine.states.Length != 2 || machine.stateMachines.Length != 0
                || machine.anyStateTransitions.Length != 0 || machine.entryTransitions.Length != 0
                || first.transitions.Any(t => t.destinationState != repeat) || repeat.transitions.Any(t => t.destinationState != first))
                throw new InvalidOperationException("怪物 Controller 已改变，请保留当前配置并手动确认 Idle / Idle_Return 后再制作。");

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length != 670 || bindings.Any(b => b.type != typeof(Transform)))
                throw new InvalidOperationException("怪物待机应保留原有 67 根骨骼的 670 条 Transform 曲线。");
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            var source = new AnimationCurve[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                indices.Add(Key(bindings[i].path, bindings[i].propertyName), i);
                source[i] = AnimationUtility.GetEditorCurve(clip, bindings[i]);
                RequireCurve(source[i], originalLength, bindings[i].path + "/" + bindings[i].propertyName);
            }
            string[] paths = bindings.Select(b => b.path).Distinct(StringComparer.Ordinal).ToArray();
            if (paths.Length != 67) throw new InvalidOperationException("怪物待机的骨骼数量已改变。");
            int addedSamples = Math.Max(1, Mathf.RoundToInt(ClosureDuration * clip.frameRate));
            float totalLength = originalLength + ClosureDuration;
            var extended = new AnimationCurve[bindings.Length];
            foreach (string path in paths)
            {
                foreach (char axis in "xyz")
                {
                    int position = Find(indices, path, Position + axis);
                    extended[position] = ExtendScalar(source[position], originalLength, totalLength, addedSamples);
                    int scale = Find(indices, path, Scale + axis);
                    extended[scale] = ExtendConstant(source[scale], totalLength, path);
                }
                int[] rotations = "xyzw".Select(axis => Find(indices, path, Rotation + axis)).ToArray();
                AnimationCurve[] quaternion = ExtendQuaternion(rotations.Select(i => source[i]).ToArray(),
                    originalLength, totalLength, addedSamples, path);
                for (int axis = 0; axis < 4; axis++) extended[rotations[axis]] = quaternion[axis];
            }
            if (extended.Any(c => c == null)) throw new InvalidOperationException("怪物片段含有未处理的动画绑定。");

            // Prepare every curve before touching saved assets; the original values and inner segments remain intact.
            AnimationUtility.SetEditorCurves(clip, bindings, extended);
            clip.wrapMode = WrapMode.Loop;
            settings.startTime = 0f;
            settings.stopTime = totalLength;
            settings.loopTime = true;
            settings.loopBlend = false;
            settings.loopBlendOrientation = false;
            settings.loopBlendPositionY = false;
            settings.loopBlendPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            int removedTransitions = first.transitions.Length + repeat.transitions.Length;
            foreach (AnimatorStateTransition transition in first.transitions) first.RemoveTransition(transition);
            foreach (AnimatorStateTransition transition in repeat.transitions) repeat.RemoveTransition(transition);
            machine.defaultState = first;
            first.motion = clip;
            machine.RemoveState(repeat);
            if (repeat != null) UnityEngine.Object.DestroyImmediate(repeat, true);
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return new GuaiwuIdleLoopResult
            {
                clip = ClipPath, controller = ControllerPath,
                originalLength = originalLength, length = totalLength, loopClosureDuration = ClosureDuration,
                loopClosureMethod = "C1 cubic Hermite local position + normalized quaternion Hermite; original body retained",
                frameRate = clip.frameRate, bones = paths.Length, bindings = bindings.Length,
                originalSamples = Mathf.RoundToInt(originalLength * clip.frameRate) + 1,
                samples = Mathf.RoundToInt(originalLength * clip.frameRate) + addedSamples + 1,
                preservedIdleSpeed = first.speed, removedLoopTransitions = removedTransitions, playModeRun = false
            };
        }

        private static AnimationCurve ExtendScalar(AnimationCurve original, float originalLength, float totalLength, int samples)
        {
            Keyframe[] keys = original.keys;
            Keyframe first = keys[0], last = keys[keys.Length - 1];
            var added = new Keyframe[samples];
            for (int frame = 1; frame <= samples; frame++)
            {
                float u = (float)frame / samples;
                float value, velocity;
                Hermite(last.value, last.inTangent, first.value, first.outTangent, u, out value, out velocity);
                // Store the exact first value/tangent at the final frame, including float precision.
                if (frame == samples) { value = first.value; velocity = first.outTangent; }
                added[frame - 1] = new Keyframe(frame == samples ? totalLength : originalLength + ClosureDuration * u,
                    value, velocity, velocity);
            }
            return Append(original, added, first.outTangent, last.inTangent);
        }

        private static AnimationCurve[] ExtendQuaternion(AnimationCurve[] original, float originalLength,
            float totalLength, int samples, string path)
        {
            var start = new Vector4();
            var end = new Vector4();
            var startVelocity = new Vector4();
            var endVelocity = new Vector4();
            for (int axis = 0; axis < 4; axis++)
            {
                Keyframe[] keys = original[axis].keys;
                start[axis] = keys[0].value;
                end[axis] = keys[keys.Length - 1].value;
                startVelocity[axis] = keys[0].outTangent;
                endVelocity[axis] = keys[keys.Length - 1].inTangent;
            }
            // The existing idle's endpoints occupy the same hemisphere. Reject a changed take rather than rewrite its signs.
            if (Mathf.Abs(start.magnitude - 1f) > 0.001f || Mathf.Abs(end.magnitude - 1f) > 0.001f
                || Vector4.Dot(start, end) <= 0f)
                throw new InvalidOperationException("首尾四元数不满足原动作的连续半球约定：" + path);
            var appended = new Keyframe[4][];
            for (int axis = 0; axis < 4; axis++) appended[axis] = new Keyframe[samples];
            Vector4 previous = end.normalized;
            for (int frame = 1; frame <= samples; frame++)
            {
                float u = (float)frame / samples;
                var value = new Vector4();
                var velocity = new Vector4();
                for (int axis = 0; axis < 4; axis++)
                {
                    float component, derivative;
                    Hermite(end[axis], endVelocity[axis], start[axis], startVelocity[axis], u, out component, out derivative);
                    value[axis] = component;
                    velocity[axis] = derivative;
                }
                float magnitude = value.magnitude;
                if (!Finite(magnitude) || magnitude < 0.001f)
                    throw new InvalidOperationException("闭环四元数退化：" + path);
                value /= magnitude;
                velocity = (velocity - value * Vector4.Dot(value, velocity)) / magnitude;
                if (Vector4.Dot(previous, value) <= 0f)
                    throw new InvalidOperationException("闭环四元数符号不连续：" + path);
                previous = value;
                // Original endpoint components can differ from unit length by float roundoff; preserve them exactly.
                if (frame == samples) { value = start; velocity = startVelocity; }
                for (int axis = 0; axis < 4; axis++)
                    appended[axis][frame - 1] = new Keyframe(frame == samples ? totalLength : originalLength + ClosureDuration * u,
                        value[axis], velocity[axis], velocity[axis]);
            }
            var result = new AnimationCurve[4];
            for (int axis = 0; axis < 4; axis++)
                result[axis] = Append(original[axis], appended[axis], startVelocity[axis], endVelocity[axis]);
            return result;
        }

        private static void Hermite(float from, float fromVelocity, float to, float toVelocity, float u,
            out float value, out float velocity)
        {
            float u2 = u * u, u3 = u2 * u;
            value = (2f * u3 - 3f * u2 + 1f) * from + (u3 - 2f * u2 + u) * ClosureDuration * fromVelocity
                + (-2f * u3 + 3f * u2) * to + (u3 - u2) * ClosureDuration * toVelocity;
            velocity = (6f * u2 - 6f * u) * from / ClosureDuration + (3f * u2 - 4f * u + 1f) * fromVelocity
                + (-6f * u2 + 6f * u) * to / ClosureDuration + (3f * u2 - 2f * u) * toVelocity;
        }

        private static AnimationCurve ExtendConstant(AnimationCurve original, float totalLength, string path)
        {
            Keyframe[] keys = original.keys;
            float value = keys[0].value;
            if (keys.Any(k => Mathf.Abs(k.value - value) > 0.000001f
                || Mathf.Abs(k.inTangent) > 0.000001f || Mathf.Abs(k.outTangent) > 0.000001f))
                throw new InvalidOperationException("原动作含有缩放动画，不能作为恒定骨骼缩放延长：" + path);
            return Append(original, new[] { new Keyframe(totalLength, value, 0f, 0f) }, 0f, 0f);
        }

        private static AnimationCurve Append(AnimationCurve original, Keyframe[] added, float initialVelocity, float finalVelocity)
        {
            Keyframe[] existing = original.keys;
            var curve = new AnimationCurve(existing.Concat(added).ToArray())
            { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
            AnimationUtility.SetKeyBroken(curve, 0, true);
            AnimationUtility.SetKeyLeftTangentMode(curve, 0, AnimationUtility.TangentMode.Free);
            AnimationUtility.SetKeyBroken(curve, existing.Length - 1, true);
            AnimationUtility.SetKeyRightTangentMode(curve, existing.Length - 1, AnimationUtility.TangentMode.Free);
            for (int i = existing.Length; i < curve.length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Free);
            }
            // Tangent-mode setters may recalculate neighboring slopes. Restore every original inner tangent explicitly.
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < existing.Length; i++)
            {
                Keyframe key = keys[i];
                key.inTangent = existing[i].inTangent;
                key.outTangent = existing[i].outTangent;
                if (i == 0) { key.inTangent = initialVelocity; key.weightedMode &= ~WeightedMode.In; }
                if (i == existing.Length - 1) { key.outTangent = finalVelocity; key.weightedMode &= ~WeightedMode.Out; }
                keys[i] = key;
            }
            for (int i = 0; i < added.Length; i++)
            {
                Keyframe key = keys[existing.Length + i];
                key.inTangent = added[i].inTangent;
                key.outTangent = added[i].outTangent;
                keys[existing.Length + i] = key;
            }
            curve.keys = keys;
            return curve;
        }

        private static void RequireCurve(AnimationCurve curve, float length, string path)
        {
            if (curve == null || curve.length < 2) throw new InvalidOperationException("缺少原始骨骼曲线：" + path);
            Keyframe[] keys = curve.keys;
            if (Mathf.Abs(keys[0].time) > TimeTolerance || Mathf.Abs(keys[keys.Length - 1].time - length) > TimeTolerance
                || keys.Any(k => !Finite(k.time) || !Finite(k.value) || !Finite(k.inTangent) || !Finite(k.outTangent)))
                throw new InvalidOperationException("原始骨骼曲线范围或数值无效：" + path);
        }

        private static int Find(Dictionary<string, int> indices, string path, string property)
        {
            int index;
            if (!indices.TryGetValue(Key(path, property), out index))
                throw new InvalidOperationException("缺少完整骨骼曲线：" + path + "/" + property);
            return index;
        }

        private static string Key(string path, string property) => path + "\n" + property;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [Serializable]
        private sealed class OriginalRecord
        {
            public float length;
            public float originalLength;
            public float loopClosureDuration;
        }
    }

    [Serializable]
    public sealed class GuaiwuIdleLoopResult
    {
        public string clip, controller, loopClosureMethod;
        public float originalLength, length, loopClosureDuration, frameRate, preservedIdleSpeed;
        public int bones, bindings, originalSamples, samples, removedLoopTransitions;
        public bool playModeRun;
    }
}
