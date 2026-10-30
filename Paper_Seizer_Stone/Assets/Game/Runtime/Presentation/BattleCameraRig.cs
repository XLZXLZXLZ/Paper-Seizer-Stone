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
        private BattleCameraPose currentPose;
        private float motionWeight = 1;

        public void SetPrecisionMode(bool value) { precisionMode = value; }

        private void LateUpdate()
        {
            if (!initialized || sceneCamera == null) return;
            float blend = 1 - Mathf.Exp(-motionSmoothing * Time.unscaledDeltaTime);
            Vector2 target = Vector2.zero;
            Vector3 cursor = Input.mousePosition;
            if (Application.isFocused && Screen.width > 0 && Screen.height > 0 &&
                cursor.x >= 0 && cursor.x <= Screen.width && cursor.y >= 0 && cursor.y <= Screen.height)
                target = new Vector2(cursor.x / Screen.width * 2 - 1, cursor.y / Screen.height * 2 - 1);
            mouseOffset = Vector2.Lerp(mouseOffset, target, blend);
            float weight = !subtleMotion ? 0 : precisionMode || transitioning || currentPose == BattleCameraPose.Shooting ? 0.2f : 1;
            motionWeight = Mathf.Lerp(motionWeight, weight, blend);
            float phase = Time.unscaledTime * breathFrequency * Mathf.PI * 2;
            float breath = Mathf.Sin(phase);
            Vector3 position = new Vector3(mouseOffset.x * mousePositionOffset.x,
                mouseOffset.y * mousePositionOffset.y + breath * breathPosition,
                Mathf.Sin(phase * 2) * breathPosition * 0.35f) * motionWeight;
            Vector3 angles = new Vector3(-mouseOffset.y * mouseAngleOffset.x + breath * breathAngle,
                mouseOffset.x * mouseAngleOffset.y, Mathf.Sin(phase + 0.8f) * breathAngle * 0.4f) * motionWeight;
            sceneCamera.transform.SetPositionAndRotation(basePosition + baseRotation * position,
                baseRotation * Quaternion.Euler(angles));
        }
        public bool ShowPlayerModel
        {
            get => showPlayerModel;
            set { showPlayerModel = value; ApplyPlayerVisibility(); }
        }

        private void OnEnable() { ApplyPlayerVisibility(); }
        private void OnValidate() { ApplyPlayerVisibility(); }
        private void ApplyPlayerVisibility()
        {
            if (sceneCamera == null) return;
            int mask = 1 << Props.PlayerVisibilityLayer.Layer;
            sceneCamera.cullingMask = showPlayerModel ? sceneCamera.cullingMask | mask : sceneCamera.cullingMask & ~mask;
        }
        public Camera Camera => sceneCamera;
        public BattleCameraDefinition Definition => definition;

        public void Initialize()
        {
            ApplyPlayerVisibility();
            if (sceneCamera == null || definition == null)
                throw new System.InvalidOperationException("请配置相机预制体中的 MainCamera 与相机配置 SO。");
            Pose target = GetWorldPose(BattleCameraPose.Table);
            basePosition = target.position;
            baseRotation = target.rotation;
            currentPose = BattleCameraPose.Table;
            initialized = true;
            sceneCamera.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        public Pose GetWorldPose(BattleCameraPose pose)
        {
            Pose localPose = definition.GetLocalPose(pose);
            return new Pose(transform.TransformPoint(localPose.position), transform.rotation * localPose.rotation);
        }

        public IEnumerator MoveTo(BattleCameraPose pose)
        {
            Pose target = GetWorldPose(pose);
            float transitionDuration = definition.TransitionDuration;
            DOTween.Kill(this);
            transitioning = true;
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
            DOTween.Kill(this);
            transitioning = false;
            if (initialized && sceneCamera != null)
                sceneCamera.transform.SetPositionAndRotation(basePosition, baseRotation);
        }

        private void OnDestroy() { DOTween.Kill(this); }
    }
}
