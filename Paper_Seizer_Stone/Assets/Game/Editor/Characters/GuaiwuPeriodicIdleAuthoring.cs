using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Saved-asset authoring only. The caller supplies backups and decides when to execute.
    // No automatic callback, preview playback, or menu entry.
    public static class GuaiwuPeriodicIdleAuthoring
    {
        public const string SourcePath = "Assets/Game/art/animation/Guaiwu/Sources/guaiwu_idle.fbx";
        public const string ClipPath = "Assets/Game/art/animation/Guaiwu/Clips/Guaiwu_Idle.anim";
        public const string ControllerPath = "Assets/Game/art/animation/Guaiwu/Guaiwu_Idle.controller";
        private const string ModelPath = "Assets/Game/art/act/Guaiwu/Models/Guaiwu_Rigged.fbx";
        private const float Tolerance = 0.0001f;

        [Serializable]
        public sealed class Result
        {
            public string source = SourcePath, clip = ClipPath, controller = ControllerPath;
            public string method = "Original source prefix; fixed-length quintic Hermite tail with normalized quaternion derivatives; identical seam values and first derivatives; single-state loop";
            public float length, frameRate, preservedSourceSeconds, replacedTailSeconds;
            public int samples, bindings, bones, removedIdleReturnStates;
            public bool playModeRun = false;
        }

        public static Result Apply(float closureSeconds = 1f)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请退出 Play 和 Animation Preview 后保存周期 Idle。");
            if (!Finite(closureSeconds) || closureSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(closureSeconds));

            AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(SourcePath).OfType<AnimationClip>()
                .Single(c => !c.name.StartsWith("__preview", StringComparison.OrdinalIgnoreCase));
            var data = PlayerAnimationSyncAuthoring.Bake(SourcePath, source, ModelPath, "Guaiwu_Rig");
            GuaiwuCombatAuthoring.CorrectHeading(data, source.length);
            int tailFrames = Mathf.Max(2, Mathf.RoundToInt(closureSeconds * source.frameRate));
            int cutFrame = data.Samples - 1 - tailFrames;
            if (cutFrame < 2) throw new InvalidOperationException("闭环尾段太长，原动作主体不足。");

            var indices = data.Bindings.Select((b, i) => new { b, i })
                .ToDictionary(x => Key(x.b.path, x.b.propertyName), x => x.i, StringComparer.Ordinal);
            string[] paths = data.Bindings.Select(b => b.path).Distinct(StringComparer.Ordinal).ToArray();
            AnimationCurve[] result = (AnimationCurve[])data.Curves.Clone();
            float cutTime = -1;
            foreach (string path in paths)
            {
                foreach (char axis in "xyz")
                {
                    int index = indices[Key(path, "m_LocalPosition." + axis)];
                    result[index] = CloseScalar(data.Curves[index], cutFrame, data.Samples, source.length, out cutTime);
                }
                int[] rotation = "xyzw".Select(axis => indices[Key(path, "m_LocalRotation." + axis)]).ToArray();
                AnimationCurve[] closed = CloseQuaternion(rotation.Select(i => data.Curves[i]).ToArray(),
                    cutFrame, data.Samples, source.length, path);
                for (int axis = 0; axis < 4; axis++) result[rotation[axis]] = closed[axis];
                // Preserve the target model's original constant scale curves untouched.
            }

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (clip == null || controller == null || controller.layers.Length == 0)
                throw new InvalidOperationException("缺少怪物 Idle 片段或 Controller。");
            var machine = controller.layers[0].stateMachine;
            var first = machine.states.Single(s => s.state.name == "Idle").state;
            var returns = machine.states.Where(s => s.state.name == "Idle_Return").Select(s => s.state).ToArray();

            // Every curve is prepared before changing the destination asset.
            clip.ClearCurves();
            AnimationUtility.SetEditorCurves(clip, data.Bindings, result);
            clip.frameRate = source.frameRate;
            clip.legacy = false;
            clip.wrapMode = WrapMode.Loop;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = source.length;
            settings.loopTime = true;
            settings.loopBlend = false;
            settings.loopBlendOrientation = false;
            settings.loopBlendPositionY = false;
            settings.loopBlendPositionXZ = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            foreach (var transition in first.transitions) first.RemoveTransition(transition);
            foreach (var state in returns)
            {
                machine.RemoveState(state);
                UnityEngine.Object.DestroyImmediate(state, true);
            }
            first.motion = clip;
            first.writeDefaultValues = false;
            machine.defaultState = first;
            EditorUtility.SetDirty(clip);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return new Result
            {
                length = source.length, frameRate = source.frameRate, preservedSourceSeconds = cutTime,
                replacedTailSeconds = source.length - cutTime, samples = data.Samples,
                bindings = data.Bindings.Length, bones = paths.Length, removedIdleReturnStates = returns.Length
            };
        }

        private static AnimationCurve CloseScalar(AnimationCurve source, int cut, int count, float length, out float cutTime)
        {
            Keyframe[] old = RequireSamples(source, count, length);
            cutTime = old[cut].time;
            float duration = length - cutTime;
            float from = old[cut].value, fromVelocity = old[cut].inTangent;
            float to = old[0].value, toVelocity = old[0].outTangent;
            var keys = new Keyframe[count];
            Array.Copy(old, keys, cut + 1);
            keys[0].inTangent = toVelocity;
            keys[cut].outTangent = fromVelocity;
            for (int frame = cut + 1; frame < count; frame++)
            {
                float time = frame == count - 1 ? length : old[frame].time;
                Polynomial(from, fromVelocity, 0, to, toVelocity, 0, duration, (time - cutTime) / duration,
                    out float value, out float velocity);
                if (frame == count - 1) { value = to; velocity = toVelocity; }
                keys[frame] = new Keyframe(time, value, velocity, velocity);
            }
            return CurveWithTail(keys, cut);
        }

        private static AnimationCurve[] CloseQuaternion(AnimationCurve[] source, int cut, int count, float length, string path)
        {
            Keyframe[][] old = source.Select(c => RequireSamples(c, count, length)).ToArray();
            float cutTime = old[0][cut].time, duration = length - cutTime;
            Vector4 from = Vector4.zero, fromRawVelocity = Vector4.zero, to = Vector4.zero, toRawVelocity = Vector4.zero;
            for (int axis = 0; axis < 4; axis++)
            {
                from[axis] = old[axis][cut].value;
                fromRawVelocity[axis] = old[axis][cut].inTangent;
                to[axis] = old[axis][0].value;
                toRawVelocity[axis] = old[axis][0].outTangent;
            }
            if (Vector4.Dot(from, to) <= 0)
                throw new InvalidOperationException("所选尾段无法按原四元数半球闭合，请调整尾段起点：" + path);
            // The imported sampled curves are linear per component. Normalize their
            // endpoint jets before constructing the smooth unit-quaternion bridge.
            UnitJet(from, fromRawVelocity, out Vector4 a, out Vector4 av, out Vector4 aa);
            UnitJet(to, toRawVelocity, out Vector4 b, out Vector4 bv, out Vector4 ba);
            Keyframe[][] keys = old.Select(k => (Keyframe[])k.Clone()).ToArray();
            for (int axis = 0; axis < 4; axis++)
            {
                keys[axis][0].inTangent = toRawVelocity[axis];
                keys[axis][cut].outTangent = fromRawVelocity[axis];
            }
            Vector4 previous = from.normalized;
            for (int frame = cut + 1; frame < count; frame++)
            {
                float time = frame == count - 1 ? length : old[0][frame].time;
                float u = (time - cutTime) / duration;
                Vector4 value = Vector4.zero, velocity = Vector4.zero;
                for (int axis = 0; axis < 4; axis++)
                {
                    Polynomial(a[axis], av[axis], aa[axis], b[axis], bv[axis], ba[axis], duration, u,
                        out float component, out float derivative);
                    value[axis] = component; velocity[axis] = derivative;
                }
                float magnitude = value.magnitude;
                if (!Finite(magnitude) || magnitude < .1f)
                    throw new InvalidOperationException("周期闭合的四元数退化：" + path);
                value /= magnitude;
                velocity = (velocity - value * Vector4.Dot(value, velocity)) / magnitude;
                if (Vector4.Dot(previous, value) <= 0)
                    throw new InvalidOperationException("周期闭合的四元数发生符号跳变：" + path);
                previous = value;
                if (frame == count - 1) { value = to; velocity = toRawVelocity; }
                for (int axis = 0; axis < 4; axis++)
                    keys[axis][frame] = new Keyframe(time, value[axis], velocity[axis], velocity[axis]);
            }
            return Enumerable.Range(0, 4).Select(axis => CurveWithTail(keys[axis], cut)).ToArray();
        }

        private static void UnitJet(Vector4 raw, Vector4 rawVelocity, out Vector4 value,
            out Vector4 velocity, out Vector4 acceleration)
        {
            float magnitude = raw.magnitude;
            if (!Finite(magnitude) || magnitude < .1f) throw new InvalidOperationException("无效的源四元数。");
            value = raw / magnitude;
            float magnitudeVelocity = Vector4.Dot(value, rawVelocity);
            velocity = (rawVelocity - value * magnitudeVelocity) / magnitude;
            float magnitudeAcceleration = (Vector4.Dot(rawVelocity, rawVelocity) - magnitudeVelocity * magnitudeVelocity) / magnitude;
            acceleration = (-value * magnitudeAcceleration - 2 * velocity * magnitudeVelocity) / magnitude;
        }

        private static void Polynomial(float a, float av, float aa, float b, float bv, float ba,
            float duration, float u, out float value, out float velocity)
        {
            double c0 = a, c1 = av * duration, c2 = .5 * aa * duration * duration;
            double p = b - c0 - c1 - c2, v = bv * duration - c1 - 2 * c2;
            double acceleration = ba * duration * duration - 2 * c2;
            double c3 = 10 * p - 4 * v + .5 * acceleration;
            double c4 = -15 * p + 7 * v - acceleration;
            double c5 = 6 * p - 3 * v + .5 * acceleration;
            value = (float)(c0 + u * (c1 + u * (c2 + u * (c3 + u * (c4 + u * c5)))));
            velocity = (float)((c1 + u * (2 * c2 + u * (3 * c3 + u * (4 * c4 + u * 5 * c5)))) / duration);
        }

        private static AnimationCurve CurveWithTail(Keyframe[] keys, int cut)
        {
            var result = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
            // Keep the original inner keys and slopes. Only the seam and tail use
            // independently supplied Hermite slopes rather than Unity auto tangents.
            foreach (int index in new[] { 0, cut }.Concat(Enumerable.Range(cut + 1, keys.Length - cut - 1)))
            {
                AnimationUtility.SetKeyBroken(result, index, true);
                if (index != cut) AnimationUtility.SetKeyLeftTangentMode(result, index, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(result, index, AnimationUtility.TangentMode.Free);
            }
            Keyframe[] configured = result.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                // Preserve the tangent-mode flags selected above. Reassigning the
                // source Keyframe wholesale would restore its old Linear flags.
                configured[i].inTangent = keys[i].inTangent;
                configured[i].outTangent = keys[i].outTangent;
                configured[i].weightedMode = keys[i].weightedMode;
                configured[i].inWeight = keys[i].inWeight;
                configured[i].outWeight = keys[i].outWeight;
            }
            result.keys = configured;
            return result;
        }

        private static Keyframe[] RequireSamples(AnimationCurve curve, int count, float length)
        {
            Keyframe[] keys = curve.keys;
            if (keys.Length != count || Mathf.Abs(keys[0].time) > Tolerance
                || Mathf.Abs(keys[count - 1].time - length) > Tolerance
                || keys.Any(k => !Finite(k.value) || !Finite(k.inTangent) || !Finite(k.outTangent)))
                throw new InvalidOperationException("原动作曲线不是预期的完整采样数据。");
            return keys;
        }
        private static string Key(string path, string property) => path + "\n" + property;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
