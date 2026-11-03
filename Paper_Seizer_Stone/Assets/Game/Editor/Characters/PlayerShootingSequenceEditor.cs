using System;
using System.Collections.Generic;
using System.Globalization;
using PaperSeizerStone.Presentation;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    [CustomEditor(typeof(PlayerShootingSequence))]
    public sealed class PlayerShootingSequenceEditor : UnityEditor.Editor
    {
        private static PlayerShootingSequenceEditor previewOwner;
        private readonly List<TransformState> transforms = new List<TransformState>();
        private readonly List<ActiveState> activeStates = new List<ActiveState>();
        private readonly List<RendererState> renderers = new List<RendererState>();
        private readonly List<AnimatorState> animators = new List<AnimatorState>();
        private bool showReferences;
        private bool previewing;
        private bool playing;
        private bool ownsAnimationMode;
        private float previewTime;
        private double lastTick;
        private string previewError;
        private PlayerShootingSequence previewSequence;
        private SkinnedMeshRenderer leftArm;

        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
            Undo.undoRedoPerformed += UndoRedo;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
            Undo.undoRedoPerformed -= UndoRedo;
            StopPreview();
        }

        public override void OnInspectorGUI()
        {
            var sequence = (PlayerShootingSequence)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            showReferences = EditorGUILayout.Foldout(showReferences, "开枪资源与对象引用", true);
            if (showReferences)
            {
                using (new EditorGUI.DisabledScope(previewing))
                    DrawPropertiesExcluding(serializedObject, "m_Script", "characterPlacement", "gunPlacement", "bulletPlacement");
                if (previewing) EditorGUILayout.HelpBox("先停止预览，再修改对象引用。", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("开枪位置、角度与比例", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("这些参数只调整开枪表现，保留原动作的时间与骨骼运动。修改后保存场景或预制体即可；进入下方预览后，可边拖时间边调整。运行期间的修改不会自动保存。", MessageType.Info);
            DrawPlacement("characterPlacement", "人物整体", "调整开枪时人物与双手的整体位置、角度和比例，不修改动作中的单根骨骼。");
            DrawPlacement("gunPlacement", "枪械对位", "在枪械原动作之上调整包装层，旋转和缩放围绕已绑定的枪械枢轴。");
            DrawPlacement("bulletPlacement", "装填子弹对位", "只调整装填子弹的包装层，旋转和缩放围绕已绑定的子弹枢轴，不影响命中判定。");
            if (serializedObject.ApplyModifiedProperties())
            {
                previewError = null;
                if (previewing) TrySample();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("运行时播放状态", EditorStyles.boldLabel);
            string problem = sequence.ConfigurationProblem;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("当前状态", sequence.PlaybackStatus ?? string.Empty);
                EditorGUILayout.FloatField("运行时共同时间（秒）", sequence.CurrentTime);
            }
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Error);
            else EditorGUILayout.HelpBox("开枪配置完整。实际击发仍由战斗流程和规则结果触发。", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("编辑模式三轨预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("手动开始后才采样人物、枪和装填子弹，不进入 Play，也不调用战斗规则。停止预览会恢复临时姿态与显隐；上面填写的对位参数会保留。", MessageType.Info);
            DrawPreview(sequence);
            if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Error);
        }

        private void DrawPlacement(string name, string label, string description)
        {
            SerializedProperty placement = serializedObject.FindProperty(name);
            if (placement == null)
            {
                EditorGUILayout.HelpBox("缺少对位字段：" + name, MessageType.Error);
                return;
            }
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(description, MessageType.None);
            EditorGUILayout.PropertyField(placement.FindPropertyRelative("position"), new GUIContent("位置偏移"));
            EditorGUILayout.PropertyField(placement.FindPropertyRelative("eulerAngles"), new GUIContent("角度偏移"));
            EditorGUILayout.PropertyField(placement.FindPropertyRelative("scale"), new GUIContent("比例倍率"));
        }

        private void DrawPreview(PlayerShootingSequence sequence)
        {
            if (previewing)
            {
                if (!AnimationMode.InAnimationMode())
                {
                    StopPreview();
                    return;
                }
                float duration = sequence.Definition != null ? sequence.Definition.Duration : 0;
                EditorGUI.BeginChangeCheck();
                previewTime = EditorGUILayout.Slider("共同时间（秒）", previewTime, 0, Mathf.Max(0, duration));
                if (EditorGUI.EndChangeCheck())
                {
                    lastTick = EditorApplication.timeSinceStartup;
                    TrySample();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(playing ? "暂停预览" : "播放预览"))
                    {
                        if (!playing && previewTime >= duration) previewTime = 0;
                        playing = !playing;
                        lastTick = EditorApplication.timeSinceStartup;
                        TrySample();
                    }
                    if (GUILayout.Button("停止预览并恢复")) StopPreview();
                }
                EditorGUILayout.LabelField("预览状态", playing ? "正在播放，到末帧自动暂停" : "已暂停，可拖动时间并调整对位");
                return;
            }

            string problem = PreviewProblem(sequence);
            if (!string.IsNullOrEmpty(problem)) EditorGUILayout.HelpBox(problem, MessageType.Info);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(problem)))
                if (GUILayout.Button("开始编辑预览")) StartPreview(sequence);
        }

        private string PreviewProblem(PlayerShootingSequence sequence)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "请退出 Play 后使用编辑预览。";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "请等待编译和资源刷新结束。";
            if (sequence == null || EditorUtility.IsPersistent(sequence) || !sequence.gameObject.scene.IsValid() || !sequence.gameObject.scene.isLoaded)
                return "请先打开玩家预制体的编辑模式，或选择场景里的玩家实例；不能直接在 Project 资源上采样。";
            if (!sequence.gameObject.activeInHierarchy) return "请先启用玩家对象，再开始预览。";
            if (previewOwner != null && previewOwner != this) return "另一个开枪 Inspector 正在预览，请先停止它。";
            if (AnimationMode.InAnimationMode() && !ownsAnimationMode) return "Animation 窗口或其他工具正在预览，请先关闭其 Preview／Record。";
            return sequence.ConfigurationProblem;
        }

        private void StartPreview(PlayerShootingSequence sequence)
        {
            string problem = PreviewProblem(sequence);
            if (!string.IsNullOrEmpty(problem)) { previewError = problem; return; }
            previewError = null;
            previewSequence = sequence;
            try
            {
                CaptureTransform(sequence.CharacterPlacementRoot);
                CaptureTransform(sequence.GunPlacementRoot);
                CaptureTransform(sequence.BulletPlacementRoot);
                foreach (Transform node in sequence.PropsRoot.GetComponentsInChildren<Transform>(true)) CaptureTransform(node);
                CaptureActive(sequence.PropsRoot.gameObject);
                CaptureActive(sequence.RightArm.gameObject);
                CaptureActive(sequence.LoadingBulletVisual);
                CaptureActive(sequence.MuzzleFlash);
                leftArm = FindLeftArm(sequence);
                if (leftArm != null) CaptureActive(leftArm.gameObject);
                CaptureRenderer(sequence.RightArm);
                CaptureRenderer(leftArm);
                foreach (Renderer renderer in sequence.PropsRoot.GetComponentsInChildren<Renderer>(true)) CaptureRenderer(renderer);
                CaptureAnimator(sequence.CharacterAnimator);
                CaptureAnimator(sequence.GunAnimator);
                CaptureAnimator(sequence.BulletAnimator);
                AnimationMode.StartAnimationMode();
                ownsAnimationMode = true;
                previewOwner = this;
                previewing = true;
                playing = false;
                previewTime = Mathf.Clamp(previewTime, 0, sequence.Definition.Duration);
                lastTick = EditorApplication.timeSinceStartup;
                TrySample();
            }
            catch (Exception exception)
            {
                previewError = "无法开始预览：" + exception.Message;
                StopPreview();
            }
        }

        private void TrySample()
        {
            if (!previewing) return;
            try
            {
                string problem = PreviewProblem(previewSequence);
                if (!string.IsNullOrEmpty(problem)) throw new InvalidOperationException(problem);
                if (!AnimationMode.InAnimationMode()) throw new InvalidOperationException("编辑动画模式已结束，请重新开始预览。");
                SampleCurrent();
            }
            catch (Exception exception)
            {
                previewError = "预览已停止：" + exception.Message;
                StopPreview();
            }
        }

        private void SampleCurrent()
        {
            PlayerShootingSequence sequence = previewSequence;
            var definition = sequence.Definition;
            previewTime = Mathf.Clamp(previewTime, 0, definition.Duration);
            foreach (TransformState state in transforms) state.Restore();
            AnimationMode.BeginSampling();
            try
            {
                // Keep temporary wrapper and visibility changes out of saved scene/prefab data.
                foreach (TransformState state in transforms) state.Register();
                foreach (ActiveState state in activeStates) state.Register();
                foreach (RendererState state in renderers) state.Register();
                sequence.PropsRoot.gameObject.SetActive(true);
                ShowArm(sequence.RightArm);
                ShowArm(leftArm);
                foreach (RendererState state in renderers)
                    if (state.Target != null) state.Target.forceRenderingOff = false;
                sequence.MuzzleFlash.SetActive(false);

                AnimationMode.SampleAnimationClip(sequence.CharacterAnimator.gameObject, definition.CharacterClip,
                    Mathf.Clamp(previewTime, 0, definition.CharacterClip.length));
                // Prop Animators intentionally have no Controller. Sample the clips directly and
                // register their original Transform values above for AnimationMode restoration.
                definition.GunClip.SampleAnimation(sequence.GunAnimator.gameObject,
                    Mathf.Clamp(previewTime - definition.GunStart, 0, definition.GunClip.length));
                definition.LoadingBulletClip.SampleAnimation(sequence.BulletAnimator.gameObject,
                    Mathf.Clamp(previewTime - definition.BulletStart, 0, definition.LoadingBulletClip.length));
                sequence.LoadingBulletVisual.SetActive(previewTime >= definition.BulletStart
                    && previewTime < definition.BulletStart + definition.LoadingBulletClip.length);
            }
            finally { AnimationMode.EndSampling(); }

            Vector3 gunPivot = PivotLocal(sequence.GunPlacementRoot, sequence.GunPivot);
            Vector3 bulletPivot = PivotLocal(sequence.BulletPlacementRoot, sequence.BulletPivot);
            Transform root = sequence.CharacterPlacementRoot;
            PlayerShootingSequence.ApplyPlacement(root, root.localPosition, root.localRotation, root.localScale,
                sequence.CharacterPlacement, 1f, Vector3.zero);
            root = sequence.GunPlacementRoot;
            PlayerShootingSequence.ApplyPlacement(root, root.localPosition, root.localRotation, root.localScale,
                sequence.GunPlacement, 1f, gunPivot);
            root = sequence.BulletPlacementRoot;
            PlayerShootingSequence.ApplyPlacement(root, root.localPosition, root.localRotation, root.localScale,
                sequence.BulletPlacement, 1f, bulletPivot);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Repaint();
        }

        private static Vector3 PivotLocal(Transform root, Transform pivot) => pivot != null ? root.InverseTransformPoint(pivot.position) : Vector3.zero;

        private static void ShowArm(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return;
            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
        }

        private static SkinnedMeshRenderer FindLeftArm(PlayerShootingSequence sequence)
        {
            if (sequence.Grip != null)
            {
                var grip = new SerializedObject(sequence.Grip);
                SerializedProperty field = grip.FindProperty("firstPersonArms");
                if (field != null && field.objectReferenceValue is SkinnedMeshRenderer renderer) return renderer;
            }
            foreach (SkinnedMeshRenderer renderer in sequence.CharacterAnimator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.name == "FirstPerson_LeftArm") return renderer;
            return null;
        }

        private void EditorUpdate()
        {
            if (EditorApplication.isPlaying) { Repaint(); return; }
            if (!previewing) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || previewSequence == null
                || previewSequence.Definition == null
                || !AnimationMode.InAnimationMode())
            {
                StopPreview();
                return;
            }
            if (!playing) return;
            double now = EditorApplication.timeSinceStartup;
            float step = Mathf.Max(0, (float)(now - lastTick));
            lastTick = now;
            previewTime = Mathf.Min(previewSequence.Definition.Duration, previewTime + step);
            TrySample();
            if (previewing && previewTime >= previewSequence.Definition.Duration) playing = false;
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) StopPreview();
        }

        private void UndoRedo() { if (previewing) TrySample(); Repaint(); }

        private void StopPreview()
        {
            if (!previewing && !ownsAnimationMode && transforms.Count == 0 && activeStates.Count == 0) return;
            previewing = false;
            playing = false;
            try
            {
                if (ownsAnimationMode && previewOwner == this && AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            }
            finally
            {
                ownsAnimationMode = false;
                if (previewOwner == this) previewOwner = null;
                foreach (AnimatorState state in animators) state.Restore();
                foreach (TransformState state in transforms) state.Restore();
                foreach (RendererState state in renderers) state.Restore();
                for (int i = activeStates.Count - 1; i >= 0; i--) activeStates[i].Restore();
                animators.Clear();
                transforms.Clear();
                renderers.Clear();
                activeStates.Clear();
                previewSequence = null;
                leftArm = null;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void CaptureTransform(Transform value)
        {
            if (value == null) return;
            foreach (TransformState state in transforms) if (state.Target == value) return;
            transforms.Add(new TransformState(value));
        }

        private void CaptureActive(GameObject value)
        {
            if (value == null) return;
            foreach (ActiveState state in activeStates) if (state.Target == value) return;
            activeStates.Add(new ActiveState(value));
        }

        private void CaptureRenderer(Renderer value)
        {
            if (value == null) return;
            foreach (RendererState state in renderers) if (state.Target == value) return;
            renderers.Add(new RendererState(value));
        }

        private void CaptureAnimator(Animator value)
        {
            if (value == null) return;
            foreach (AnimatorState state in animators) if (state.Target == value) return;
            animators.Add(new AnimatorState(value));
        }

        private static void Register(UnityEngine.Object target, Type type, string property, float value)
        {
            AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve(string.Empty, type, property),
                new PropertyModification { target = target, propertyPath = property, value = value.ToString("R", CultureInfo.InvariantCulture) }, true);
        }

        private sealed class TransformState
        {
            public readonly Transform Target;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            public TransformState(Transform value) { Target = value; position = value.localPosition; rotation = value.localRotation; scale = value.localScale; }
            public void Restore()
            {
                if (Target == null) return;
                Target.localPosition = position;
                Target.localRotation = rotation;
                Target.localScale = scale;
            }
            public void Register()
            {
                if (Target == null) return;
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalPosition.x", position.x);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalPosition.y", position.y);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalPosition.z", position.z);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalRotation.x", rotation.x);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalRotation.y", rotation.y);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalRotation.z", rotation.z);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalRotation.w", rotation.w);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalScale.x", scale.x);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalScale.y", scale.y);
                PlayerShootingSequenceEditor.Register(Target, typeof(Transform), "m_LocalScale.z", scale.z);
            }
        }

        private sealed class ActiveState
        {
            public readonly GameObject Target;
            private readonly bool active;
            public ActiveState(GameObject value) { Target = value; active = value.activeSelf; }
            public void Restore() { if (Target != null && Target.activeSelf != active) Target.SetActive(active); }
            public void Register() { if (Target != null) PlayerShootingSequenceEditor.Register(Target, typeof(GameObject), "m_IsActive", active ? 1 : 0); }
        }

        private sealed class RendererState
        {
            public readonly Renderer Target;
            private readonly bool enabled;
            private readonly bool hidden;
            public RendererState(Renderer value) { Target = value; enabled = value.enabled; hidden = value.forceRenderingOff; }
            public void Restore() { if (Target != null) { Target.enabled = enabled; Target.forceRenderingOff = hidden; } }
            public void Register() { if (Target != null) PlayerShootingSequenceEditor.Register(Target, Target.GetType(), "m_Enabled", enabled ? 1 : 0); }
        }

        private sealed class AnimatorState
        {
            public readonly Animator Target;
            private readonly RuntimeAnimatorController controller;
            private readonly bool enabled;
            private readonly bool rootMotion;
            private readonly AnimatorCullingMode culling;
            public AnimatorState(Animator value)
            {
                Target = value; controller = value.runtimeAnimatorController; enabled = value.enabled;
                rootMotion = value.applyRootMotion; culling = value.cullingMode;
            }
            public void Restore()
            {
                if (Target == null) return;
                if (Target.runtimeAnimatorController != controller) Target.runtimeAnimatorController = controller;
                Target.enabled = enabled;
                Target.applyRootMotion = rootMotion;
                Target.cullingMode = culling;
            }
        }
    }
}
