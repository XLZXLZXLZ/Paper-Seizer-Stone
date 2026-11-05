using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation.Props;

namespace PaperSeizerStone.Presentation
{
    [Serializable]
    public sealed class ShootingPlacement
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        [Min(0.001f)] public float scale = 1f;
    }

    // All visual objects are saved in the player prefab; only the playback graph is created here.
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class PlayerShootingSequence : MonoBehaviour
    {
        [SerializeField] private PlayerShootingDefinition definition;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private Animator gunAnimator;
        [SerializeField] private Animator bulletAnimator;
        [SerializeField] private Transform propsRoot;
        [SerializeField] private SkinnedMeshRenderer rightArm;
        [SerializeField] private PlayerCardGrip grip;
        [SerializeField] private GameObject loadingBulletVisual;
        [SerializeField] private GameObject muzzleFlash;
        [SerializeField] private ShootingPlacement characterPlacement = new ShootingPlacement();
        [SerializeField] private ShootingPlacement gunPlacement = new ShootingPlacement();
        [SerializeField] private ShootingPlacement bulletPlacement = new ShootingPlacement();

        private PlayableGraph graph;
        private AnimatorControllerPlayable idle;
        private AnimationMixerPlayable characterMixer;
        private AnimationClipPlayable character;
        private AnimationClipPlayable gun;
        private AnimationClipPlayable bullet;
        private FirstPersonHandView handView;
        private Transform tableGun;
        private RuntimeAnimatorController originalController;
        private AnimatorStateInfo originalIdleState;
        private AnimatorSettings[] animatorSettings;
        private LocalPose[] placementBaselines;
        private bool active;
        private bool propsWereActive;
        private bool tableGunWasActive;
        private bool rightArmWasActive;
        private bool rightArmWasHidden;
        private bool bulletWasActive;
        private bool flashWasActive;
        private float clock;
        private float characterWeight;
        private int revision;
        private int requestedFrame;
        private int evaluatedFrame;
        private string playbackStatus = "已停止";

        private struct AnimatorSettings
        {
            public Animator Animator;
            public bool Enabled;
            public bool RootMotion;
            public AnimatorCullingMode Culling;
        }

        private struct LocalPose
        {
            public Transform Root;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        public PlayerShootingDefinition Definition => definition;
        public Animator CharacterAnimator => characterAnimator;
        public Animator GunAnimator => gunAnimator;
        public Animator BulletAnimator => bulletAnimator;
        public Transform PropsRoot => propsRoot;
        public SkinnedMeshRenderer RightArm => rightArm;
        public PlayerCardGrip Grip => grip;
        public GameObject LoadingBulletVisual => loadingBulletVisual;
        public GameObject MuzzleFlash => muzzleFlash;
        public ShootingPlacement CharacterPlacement => characterPlacement;
        public ShootingPlacement GunPlacement => gunPlacement;
        public ShootingPlacement BulletPlacement => bulletPlacement;
        public Transform CharacterPlacementRoot => characterAnimator != null ? characterAnimator.transform : null;
        public Transform GunPlacementRoot => gunAnimator != null ? gunAnimator.transform.parent : null;
        public Transform BulletPlacementRoot => bulletAnimator != null ? bulletAnimator.transform : null;
        public Transform GunPivot => gunAnimator != null ? gunAnimator.transform.Find("Revolver_Rig") : null;
        public Transform BulletPivot => bulletAnimator != null ? bulletAnimator.transform.Find("Bullet_Root") : null;
        public float CurrentTime => clock;
        public string PlaybackStatus => playbackStatus;
        public bool IsConfigured => string.IsNullOrEmpty(ConfigurationProblem);
        public bool IsActive => active;
        private bool mechanismSoundPlayed;
        private bool loadSoundPlayed;
        private bool openSoundPlayed, closeSoundPlayed;

        public string ConfigurationProblem
        {
            get
            {
                if (definition == null) return "未绑定开枪配置。";
                if (definition.CharacterClip == null) return "开枪配置缺少人物动画。";
                if (definition.GunClip == null) return "开枪配置缺少枪械动画。";
                if (definition.LoadingBulletClip == null) return "开枪配置缺少装填子弹动画。";
                if (!definition.IsConfigured) return "开枪动画长度或时序参数无效。";
                if (characterAnimator == null) return "未绑定玩家人物 Animator。";
                if (characterAnimator.runtimeAnimatorController == null && (!active || originalController == null))
                    return "玩家人物 Animator 缺少待机 Controller。";
                if (gunAnimator == null) return "未绑定枪械 Animator。";
                if (bulletAnimator == null) return "未绑定装填子弹 Animator。";
                if (propsRoot == null) return "未绑定射击道具根节点。";
                if (rightArm == null) return "未绑定第一人称右手 Renderer。";
                if (grip == null) return "未绑定同一玩家的 PlayerCardGrip。";
                if (loadingBulletVisual == null) return "未绑定装填子弹外观。";
                if (muzzleFlash == null) return "未绑定枪口闪光。";
                if (GunPlacementRoot == null || GunPivot == null) return "枪械 Animator 缺少外层 Gun 或 Revolver_Rig。";
                if (BulletPivot == null) return "装填子弹 Animator 下缺少 Bullet_Root。";
                return null;
            }
        }

        public IEnumerator Prepare(FirstPersonHandView hand, Transform restingGun)
        {
            if (!IsConfigured || !isActiveAndEnabled) yield break;
            if (hand == null || restingGun == null)
                throw new InvalidOperationException("玩家射击缺少手牌表现组件或桌面枪械引用。");
            Cancel();
            mechanismSoundPlayed = loadSoundPlayed = false;
            openSoundPlayed = closeSoundPlayed = false;
            int run = ++revision;
            bool prepared = false;
            try
            {
                CapturePresentation(hand, restingGun);
                active = true;
                playbackStatus = "准备";
                handView.SetPresentationSuspended(true);
                grip.SetShootingPresentation(true);
                tableGun.gameObject.SetActive(false);
                propsRoot.gameObject.SetActive(true);
                rightArm.gameObject.SetActive(true);
                rightArm.forceRenderingOff = false;
                SetVisible(muzzleFlash, false);
                SetVisible(loadingBulletVisual, false);
                CreateGraph();
                Queue(0, definition.EnterBlend <= 0 ? 1 : 0);
                while (active && revision == run && clock < definition.FireTime)
                {
                    yield return null;
                    if (!active || revision != run) yield break;
                    float next = Mathf.Min(definition.FireTime, clock + Time.deltaTime);
                    float weight = definition.EnterBlend <= 0 ? 1 : Mathf.Clamp01(next / definition.EnterBlend);
                    Queue(next, weight);
                }
                yield return WaitForEvaluation(requestedFrame, run);
                prepared = active && revision == run;
                if (prepared) playbackStatus = "等待击发";
            }
            finally
            {
                if (!prepared && revision == run) Cancel();
            }
        }

        public IEnumerator Finish()
        {
            if (!active) yield break;
            int run = revision;
            playbackStatus = "返回";
            try
            {
                while (active && revision == run && clock < definition.Duration)
                {
                    yield return null;
                    if (!active || revision != run) yield break;
                    float next = Mathf.Min(definition.Duration, clock + Time.deltaTime);
                    Queue(next, 1);
                }
                for (float elapsed = 0; active && revision == run && elapsed < definition.PostShotHold;)
                {
                    yield return null;
                    if (!active || revision != run) yield break;
                    float step = Mathf.Min(Time.deltaTime, definition.PostShotHold - elapsed);
                    elapsed += step;
                }
                for (float elapsed = 0; active && revision == run && elapsed < definition.ReturnBlend;)
                {
                    yield return null;
                    if (!active || revision != run) yield break;
                    float step = Mathf.Min(Time.deltaTime, definition.ReturnBlend - elapsed);
                    elapsed += step;
                    Queue(clock, 1 - elapsed / definition.ReturnBlend);
                }
                if (active && revision == run)
                {
                    Queue(clock, 0);
                    yield return WaitForEvaluation(requestedFrame, run);
                    // Hand the controller back on a single idle state, preserving its pose and time.
                    float transitionDeadline = Time.unscaledTime + definition.ReturnBlend + 0.25f;
                    while (active && revision == run && idle.IsValid() && idle.IsInTransition(0)
                        && Time.unscaledTime < transitionDeadline)
                        yield return null;
                }
            }
            finally
            {
                if (revision == run) Cancel();
            }
        }

        public void SetMuzzleFlash(bool visible) => SetVisible(muzzleFlash, active && visible);

        private void CapturePresentation(FirstPersonHandView hand, Transform restingGun)
        {
            handView = hand;
            tableGun = restingGun;
            originalController = characterAnimator.runtimeAnimatorController;
            originalIdleState = characterAnimator.GetCurrentAnimatorStateInfo(0);
            propsWereActive = propsRoot.gameObject.activeSelf;
            tableGunWasActive = tableGun.gameObject.activeSelf;
            rightArmWasActive = rightArm.gameObject.activeSelf;
            rightArmWasHidden = rightArm.forceRenderingOff;
            bulletWasActive = loadingBulletVisual.activeSelf;
            flashWasActive = muzzleFlash.activeSelf;
            animatorSettings = new[] { CaptureAnimator(characterAnimator), CaptureAnimator(gunAnimator), CaptureAnimator(bulletAnimator) };
            placementBaselines = new[] { CapturePose(CharacterPlacementRoot), CapturePose(GunPlacementRoot), CapturePose(BulletPlacementRoot) };
            clock = 0;
            requestedFrame = 0;
            evaluatedFrame = 0;
        }

        private static LocalPose CapturePose(Transform root)
        {
            return new LocalPose { Root = root, Position = root.localPosition, Rotation = root.localRotation, Scale = root.localScale };
        }

        private static AnimatorSettings CaptureAnimator(Animator animator)
        {
            return new AnimatorSettings { Animator = animator, Enabled = animator.enabled, RootMotion = animator.applyRootMotion, Culling = animator.cullingMode };
        }

        private void CreateGraph()
        {
            // The graph owns animation output until Cancel restores this controller and its state.
            characterAnimator.runtimeAnimatorController = null;
            foreach (AnimatorSettings settings in animatorSettings)
            {
                settings.Animator.enabled = true;
                settings.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                settings.Animator.applyRootMotion = false;
            }
            graph = PlayableGraph.Create("Player shooting: " + GetInstanceID());
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            idle = AnimatorControllerPlayable.Create(graph, originalController);
            character = Clip(definition.CharacterClip);
            gun = Clip(definition.GunClip);
            bullet = Clip(definition.LoadingBulletClip);
            characterMixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(idle, 0, characterMixer, 0);
            graph.Connect(character, 0, characterMixer, 1);
            characterMixer.SetInputWeight(0, 1);
            characterMixer.SetInputWeight(1, 0);
            Output("Player", characterAnimator, characterMixer);
            Output("Gun", gunAnimator, gun);
            Output("Loading bullet", bulletAnimator, bullet);
            graph.Play();
            if (originalIdleState.fullPathHash != 0 && idle.HasState(0, originalIdleState.fullPathHash))
                idle.Play(originalIdleState.fullPathHash, 0, originalIdleState.normalizedTime);
        }

        private AnimationClipPlayable Clip(AnimationClip clip)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0);
            return playable;
        }

        private void Output<T>(string name, Animator animator, T playable) where T : struct, IPlayable
        {
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, name, animator);
            output.SetSourcePlayable(playable);
            output.SetWeight(1);
        }

        private void Queue(float time, float weight)
        {
            clock = time;
            characterWeight = Mathf.Clamp01(weight);
            ++requestedFrame;
        }

        private IEnumerator WaitForEvaluation(int frame, int run)
        {
            while (active && revision == run && evaluatedFrame < frame) yield return null;
        }

        private void LateUpdate()
        {
            if (!active || !graph.IsValid()) return;
            try
            {
                RestorePlacementBaselines();
                character.SetTime(Mathf.Clamp(clock, 0, definition.CharacterClip.length));
                gun.SetTime(Mathf.Clamp(clock - definition.GunStart, 0, definition.GunClip.length));
                bullet.SetTime(Mathf.Clamp(clock - definition.BulletStart, 0, definition.LoadingBulletClip.length));
                characterMixer.SetInputWeight(0, 1 - characterWeight);
                characterMixer.SetInputWeight(1, characterWeight);
                graph.Evaluate(Time.deltaTime);
                if (Application.isPlaying)
                {
                    if (!openSoundPlayed && clock >= definition.OpenSoundTime)
                    {
                        openSoundPlayed = true;
                        PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.GunOpen);
                    }
                    if (!mechanismSoundPlayed && clock >= definition.CockSoundTime)
                    {
                        mechanismSoundPlayed = true;
                        PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.GunMechanism);
                    }
                    if (!loadSoundPlayed && clock >= definition.LoadSoundTime)
                    {
                        loadSoundPlayed = true;
                        PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.GunLoad);
                    }
                    if (!closeSoundPlayed && clock >= definition.CloseSoundTime)
                    {
                        closeSoundPlayed = true;
                        PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.GunClose);
                    }
                }
                ApplyOffset(placementBaselines[0], characterPlacement, characterWeight, null);
                ApplyOffset(placementBaselines[1], gunPlacement, 1, GunPivot);
                ApplyOffset(placementBaselines[2], bulletPlacement, 1, BulletPivot);
                // Loading is authored independently from the hit or empty-chamber result.
                SetVisible(loadingBulletVisual, clock >= definition.BulletStart && clock < definition.BulletStart + definition.LoadingBulletClip.length);
                evaluatedFrame = requestedFrame;
            }
            catch (Exception exception)
            {
                Cancel();
                playbackStatus = "已停止：播放失败";
                Debug.LogException(exception, this);
            }
        }

        private void RestorePlacementBaselines()
        {
            if (placementBaselines == null) return;
            foreach (LocalPose pose in placementBaselines)
                if (pose.Root != null)
                {
                    pose.Root.localPosition = pose.Position;
                    pose.Root.localRotation = pose.Rotation;
                    pose.Root.localScale = pose.Scale;
                }
        }

        private static void ApplyOffset(LocalPose pose, ShootingPlacement placement, float weight, Transform pivot)
        {
            if (pose.Root == null) return;
            Vector3 localPivot = pivot != null ? pose.Root.InverseTransformPoint(pivot.position) : Vector3.zero;
            ApplyPlacement(pose.Root, pose.Position, pose.Rotation, pose.Scale, placement, weight, localPivot);
        }

        public static void ApplyPlacement(Transform root, Vector3 basePos, Quaternion baseRot, Vector3 baseScale,
            ShootingPlacement p, float weight, Vector3 pivotLocal)
        {
            if (root == null) return;
            weight = Mathf.Clamp01(weight);
            Vector3 offset = p != null ? p.position : Vector3.zero;
            Vector3 angles = p != null ? p.eulerAngles : Vector3.zero;
            float factor = p != null ? Mathf.Max(0.001f, p.scale) : 1;
            Vector3 newScale = baseScale * Mathf.Lerp(1, factor, weight);
            Quaternion newRotation = baseRot * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(angles), weight);
            root.localPosition = basePos + baseRot * Vector3.Scale(baseScale, offset * weight)
                + baseRot * Vector3.Scale(baseScale, pivotLocal) - newRotation * Vector3.Scale(newScale, pivotLocal);
            root.localRotation = newRotation;
            root.localScale = newScale;
        }

        public void Cancel()
        {
            if (!active && !graph.IsValid()) return;
            active = false;
            playbackStatus = "已停止";
            ++revision;
            AnimatorStateInfo resume = originalIdleState;
            if (graph.IsValid())
            {
                if (idle.IsValid()) resume = idle.GetCurrentAnimatorStateInfo(0);
                graph.Destroy();
            }
            RestorePlacementBaselines();
            if (characterAnimator != null && characterAnimator.runtimeAnimatorController == null)
                characterAnimator.runtimeAnimatorController = originalController;
            if (characterAnimator != null && characterAnimator.isActiveAndEnabled
                && characterAnimator.runtimeAnimatorController == originalController
                && resume.fullPathHash != 0 && characterAnimator.HasState(0, resume.fullPathHash))
            {
                characterAnimator.Play(resume.fullPathHash, 0, resume.normalizedTime);
                characterAnimator.Update(0);
            }
            if (animatorSettings != null)
                foreach (AnimatorSettings settings in animatorSettings)
                    if (settings.Animator != null)
                    {
                        settings.Animator.applyRootMotion = settings.RootMotion;
                        settings.Animator.cullingMode = settings.Culling;
                        settings.Animator.enabled = settings.Enabled;
                    }
            if (rightArm != null)
            {
                rightArm.forceRenderingOff = rightArmWasHidden;
                SetVisible(rightArm.gameObject, rightArmWasActive);
            }
            SetVisible(loadingBulletVisual, bulletWasActive);
            SetVisible(muzzleFlash, flashWasActive);
            if (propsRoot != null) SetVisible(propsRoot.gameObject, propsWereActive);
            if (tableGun != null) SetVisible(tableGun.gameObject, tableGunWasActive);
            if (grip != null) grip.SetShootingPresentation(false);
            if (handView != null) handView.SetPresentationSuspended(false);
            handView = null;
            tableGun = null;
            animatorSettings = null;
            placementBaselines = null;
            originalController = null;
        }

        private static void SetVisible(GameObject target, bool visible)
        {
            if (target != null && target.activeSelf != visible) target.SetActive(visible);
        }

        private void OnDisable() { Cancel(); }
        private void OnDestroy() { Cancel(); }
    }
}
