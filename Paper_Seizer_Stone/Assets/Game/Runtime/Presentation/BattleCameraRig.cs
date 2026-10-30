using System.Collections;
using UnityEngine;
using DG.Tweening;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    public enum BattleCameraPose { Table, Scoring, Shooting }

    public sealed class BattleCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private BattleCameraDefinition definition;
        [SerializeField, Tooltip("主相机是否显示完整玩家角色；第一人称默认关闭。")] private bool showPlayerModel;
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
            Transform cameraTransform = sceneCamera.transform;
            cameraTransform.DOKill();
            cameraTransform.DOMove(target.position, transitionDuration).SetEase(Ease.InOutSine);
            cameraTransform.DORotateQuaternion(target.rotation, transitionDuration).SetEase(Ease.InOutSine);
            yield return new WaitForSeconds(transitionDuration);
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

        private void OnDestroy() { if (sceneCamera != null) sceneCamera.transform.DOKill(); }
    }
}
