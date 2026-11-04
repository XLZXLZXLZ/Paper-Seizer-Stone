using System.Collections;
using UnityEngine;
using DG.Tweening;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    public enum BattleCameraPose { Table, Scoring, Shooting }

    [DefaultExecutionOrder(100)]
    public sealed class BattleCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private BattleCameraDefinition definition;
        [SerializeField, Tooltip("主相机是否显示完整玩家角色；第一人称默认关闭。")] private bool showPlayerModel;
        [SerializeField, Tooltip("主相机是否显示第一人称手臂；默认开启，与完整角色显示独立控制。")] private bool showPlayerHands = true;
        [Header("第一人称微动")]
        [SerializeField] private bool subtleMotion = true;
        [SerializeField, Min(0)] private float breathPosition = 0.004f;
        [SerializeField, Min(0)] private float breathAngle = 0.12f;
        [SerializeField, Min(0.01f)] private float breathFrequency = 0.22f;
        [SerializeField] private Vector2 mousePositionOffset = new Vector2(0.018f, 0.012f);
        [SerializeField] private Vector2 mouseAngleOffset = new Vector2(0.2f, 0.35f);
        [SerializeField, Min(0.1f)] private float motionSmoothing = 8;
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private Vector2 mouseOffset;
        private bool initialized;
        private bool precisionMode;
        private bool transitioning;
        private bool deathPose;
        private BattleCameraPose currentPose;
        private float motionWeight = 1;

        [Header("命中演出")]
        [SerializeField, Min(0.01f)] private float shotTremorDuration = 4f;
        [SerializeField, Min(0)] private float impactTremorAngle = 1.25f;
        [SerializeField, Min(0.06f)] private float hitSequenceDuration = 5.5f;
        [SerializeField, Range(0, 0.08f)] private float hitDistortion = 0.024f;
        private float shakeStart = float.NegativeInfinity;
        private float hitStart = float.NegativeInfinity;

        private const float HitBuildUpSeconds = 1.85f;

        public void ShakeForShot()
        {
            shakeStart = Time.unscaledTime;
        }
        private UnityEngine.UI.Image hitOverlay;

        // One call owns the entire buildup, flash, blackout and recovery timeline.
        public void PlayHitScreenEffect()
        {
            if (!isActiveAndEnabled) return;
            hitStart = Time.unscaledTime;
            if (hitOverlay != null) return;

            var overlay = new GameObject("Hit Screen Overlay", typeof(RectTransform), typeof(Canvas));
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvas.targetDisplay = sceneCamera.targetDisplay;

            var fill = new GameObject("Screen Fill", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            fill.transform.SetParent(overlay.transform, false);
            hitOverlay = fill.GetComponent<UnityEngine.UI.Image>();
            hitOverlay.raycastTarget = false;
            hitOverlay.color = Color.clear;
            hitOverlay.rectTransform.anchorMin = Vector2.zero;
            hitOverlay.rectTransform.anchorMax = Vector2.one;
            hitOverlay.rectTransform.offsetMin = Vector2.zero;
            hitOverlay.rectTransform.offsetMax = Vector2.zero;
        }

        public bool IsHitEffectPlaying => isActiveAndEnabled
            && Time.unscaledTime - hitStart < HitBuildUpSeconds + hitSequenceDuration;

        public void ResetShotEffects()
        {
            shakeStart = hitStart = float.NegativeInfinity;
            if (hitOverlay != null)
            {
                hitOverlay.color = Color.clear;
                hitOverlay.enabled = false;
            }
        }

        public Vector4 HitScreenEffect
        {
            get
            {
                if (!IsHitEffectPlaying) return Vector4.zero;
                float elapsed = Time.unscaledTime - hitStart;
                if (elapsed < HitBuildUpSeconds)
                {
                    float buildup = Mathf.Clamp01(elapsed / 0.42f);
                    return new Vector4(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, buildup));
                }
                float impactElapsed = elapsed - HitBuildUpSeconds;
                float t = impactElapsed / hitSequenceDuration;
                const float flashDuration = 0.09f;
                const float recoveryStart = 3f / 5.5f;
                float flashProgress = Mathf.Clamp01(impactElapsed / flashDuration);
                float flash = 0.8f * (1f - flashProgress);
                float black = flashProgress < 1f ? 0f
                    : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(recoveryStart, 1f, t));
                float red = flashProgress < 1f ? 1f : black;
                return new Vector4(hitDistortion * (1f - flashProgress), flash, black, red);
            }
        }

        public void SetPrecisionMode(bool value) { precisionMode = value; }

        private void LateUpdate()
        {
            if (!IsHitEffectPlaying) hitStart = float.NegativeInfinity;
            if (hitOverlay != null)
            {
                Vector4 effect = HitScreenEffect;
                hitOverlay.color = effect.y > 0f
                    ? new Color(1f, 0.88f, 0.68f, effect.y)
                    : new Color(0f, 0f, 0f, effect.z);
                hitOverlay.enabled = effect.y > 0f || effect.z > 0f;
            }
            if (!initialized || sceneCamera == null) return;
            float blend = 1 - Mathf.Exp(-motionSmoothing * Time.unscaledDeltaTime);
            Vector2 target = Vector2.zero;
            Vector3 cursor = Input.mousePosition;
            if (Application.isFocused && Screen.width > 0 && Screen.height > 0 &&
                cursor.x >= 0 && cursor.x <= Screen.width && cursor.y >= 0 && cursor.y <= Screen.height)
                target = new Vector2(cursor.x / Screen.width * 2 - 1, cursor.y / Screen.height * 2 - 1);
            mouseOffset = Vector2.Lerp(mouseOffset, target, blend);
            float weight = !subtleMotion || deathPose ? 0 : precisionMode || transitioning || currentPose == BattleCameraPose.Shooting ? 0.2f : 1;
            motionWeight = Mathf.Lerp(motionWeight, weight, blend);
            float phase = Time.unscaledTime * breathFrequency * Mathf.PI * 2;
            float breath = Mathf.Sin(phase);
            Vector3 position = new Vector3(mouseOffset.x * mousePositionOffset.x,
                mouseOffset.y * mousePositionOffset.y + breath * breathPosition,
                Mathf.Sin(phase * 2) * breathPosition * 0.35f) * motionWeight;
            Vector3 angles = new Vector3(-mouseOffset.y * mouseAngleOffset.x + breath * breathAngle,
                mouseOffset.x * mouseAngleOffset.y, Mathf.Sin(phase + 0.8f) * breathAngle * 0.4f) * motionWeight;
            float shotElapsed = Time.unscaledTime - shakeStart;
            if (shotElapsed < shotTremorDuration)
            {
                float decay = 1f - shotElapsed / shotTremorDuration;
                float wave = shotElapsed * Mathf.Lerp(94f, 63.5f, 1f - decay);
                angles += new Vector3(Mathf.Cos(wave), Mathf.Sin(wave * 1.37f),
                    Mathf.Sin(wave * 0.83f + 2f)) * (impactTremorAngle * decay);
            }
            sceneCamera.transform.SetPositionAndRotation(basePosition + baseRotation * position,
                baseRotation * Quaternion.Euler(angles));
        }
        public bool ShowPlayerModel
        {
            get => showPlayerModel;
            set { showPlayerModel = value; ApplyPlayerVisibility(); }
        }

        public bool ShowPlayerHands
        {
            get => showPlayerHands;
            set { showPlayerHands = value; ApplyPlayerVisibility(); }
        }

        private void OnEnable() { ApplyPlayerVisibility(); }
        private void OnValidate() { ApplyPlayerVisibility(); }
        private void ApplyPlayerVisibility()
        {
            if (sceneCamera == null) return;
            int playerMask = 1 << Props.PlayerVisibilityLayer.Layer;
            int handsMask = 1 << Props.PlayerVisibilityLayer.HandsLayer;
            int mask = sceneCamera.cullingMask;
            mask = showPlayerModel ? mask | playerMask : mask & ~playerMask;
            sceneCamera.cullingMask = showPlayerHands ? mask | handsMask : mask & ~handsMask;
        }
        public Camera Camera => sceneCamera;
        public BattleCameraDefinition Definition => definition;

        public void Initialize()
        {
            ResetShotEffects();
            ApplyPlayerVisibility();
            if (sceneCamera == null || definition == null)
                throw new System.InvalidOperationException("请配置相机预制体中的 MainCamera 与相机配置 SO。");
            Pose target = GetWorldPose(BattleCameraPose.Table);
            basePosition = target.position;
            baseRotation = target.rotation;
            currentPose = BattleCameraPose.Table;
            deathPose = false;
            initialized = true;
            sceneCamera.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        public Pose GetWorldPose(BattleCameraPose pose)
        {
            Pose localPose = definition.GetLocalPose(pose);
            return new Pose(transform.TransformPoint(localPose.position), transform.rotation * localPose.rotation);
        }

        public const float OpponentFallSeconds = 1.1f;

        public IEnumerator FollowOpponentFall()
        {
            Quaternion initial = baseRotation;
            Quaternion lowered = initial * Quaternion.Euler(13f, 0f, 0f);
            float start = Time.unscaledTime;
            while (true)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / OpponentFallSeconds);
                baseRotation = Quaternion.Slerp(initial, lowered, Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f) yield break;
                yield return null;
            }
        }

        public IEnumerator PlayPlayerFall(bool lethal)
        {
            if (!initialized || sceneCamera == null) yield break;

            DOTween.Kill(this);
            transitioning = false;
            deathPose = true;
            ShakeForShot();
            motionWeight = 0f;
            const float duration = 1.25f;
            Vector3 up = transform.up;
            Vector3 startPosition = basePosition;
            Quaternion startRotation = baseRotation;
            Vector3 right = Vector3.ProjectOnPlane(startRotation * Vector3.right, up).normalized;
            Vector3 backward = Vector3.ProjectOnPlane(startRotation * Vector3.back, up).normalized;
            float drop = Mathf.Clamp(Vector3.Dot(startPosition - transform.position, up) - 0.42f, 0.55f, 2.6f);
            float fallDistance = lethal ? 1f : 0.48f;
            Vector3 landed = startPosition + (-up * drop + right * 0.78f + backward * 0.36f) * fallDistance;
            Quaternion landedRotation = startRotation * Quaternion.Euler(-14f, -9f, lethal ? 77f : 38f);
            float start = Time.unscaledTime;

            while (true)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / duration);
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.86f, t));
                float impact = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.86f, 1f, t));
                basePosition = Vector3.Lerp(startPosition, landed, fall) + up * (0.045f * impact);
                baseRotation = Quaternion.Slerp(startRotation, landedRotation, fall)
                    * Quaternion.Euler(-3f * impact, 0f, 4f * impact);
                if (t >= 1f) yield break;
                yield return null;
            }
        }

        public IEnumerator MoveTo(BattleCameraPose pose) => MoveToPose(GetWorldPose(pose), pose);

        public IEnumerator FocusScoreCounter(Transform counter)
        {
            if (counter == null) return MoveTo(BattleCameraPose.Scoring);

            var renderers = counter.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds = new Bounds(counter.position, Vector3.zero);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 up = transform.up;
            Vector3 towardPlayer = Vector3.ProjectOnPlane(GetWorldPose(BattleCameraPose.Table).position - bounds.center, up);
            if (towardPlayer.sqrMagnitude < 0.001f) towardPlayer = -transform.forward;
            float distance = Mathf.Max(1.25f, bounds.extents.magnitude * 2.8f);
            Vector3 position = bounds.center + (towardPlayer.normalized + up * 0.3f) * distance;
            return MoveToPose(new Pose(position, Quaternion.LookRotation(bounds.center - position, up)), BattleCameraPose.Scoring);
        }

        private IEnumerator MoveToPose(Pose target, BattleCameraPose pose)
        {
            float transitionDuration = definition.TransitionDuration;
            DOTween.Kill(this);
            transitioning = true;
            deathPose = false;
            currentPose = pose;
            Vector3 startPosition = basePosition;
            Quaternion startRotation = baseRotation;
            float progress = 0;
            Tween motion = DOTween.To(() => progress, value =>
            {
                progress = value;
                basePosition = Vector3.Lerp(startPosition, target.position, value);
                baseRotation = Quaternion.Slerp(startRotation, target.rotation, value);
            }, 1, transitionDuration).SetEase(Ease.InOutSine).SetTarget(this);
            yield return motion.WaitForCompletion();
            transitioning = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.65f, 0.35f);
            if (definition == null) return;
            foreach (BattleCameraPose key in new[] { BattleCameraPose.Table, BattleCameraPose.Scoring, BattleCameraPose.Shooting })
            {
                Pose pose = GetWorldPose(key);
                Gizmos.DrawWireSphere(pose.position, 0.12f);
                Gizmos.DrawRay(pose.position, pose.rotation * Vector3.forward * 0.65f);
            }
        }

        private void OnDisable()
        {
            ResetShotEffects();
            DOTween.Kill(this);
            transitioning = false;
            if (initialized && sceneCamera != null)
                sceneCamera.transform.SetPositionAndRotation(basePosition, baseRotation);
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
            if (hitOverlay != null) Destroy(hitOverlay.transform.parent.gameObject);
        }
    }
}
