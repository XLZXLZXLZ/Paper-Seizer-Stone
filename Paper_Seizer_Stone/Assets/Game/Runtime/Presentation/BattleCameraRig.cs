using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    public enum BattleCameraPose { Table, Scoring, Shooting }

    public sealed class BattleCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Transform tablePose;
        [SerializeField] private Transform scoringPose;
        [SerializeField] private Transform shootingPose;
        [SerializeField, Min(0.05f)] private float transitionDuration = 0.3f;
        public Camera Camera => sceneCamera;

        public void Initialize()
        {
            if (sceneCamera == null || tablePose == null || scoringPose == null || shootingPose == null)
                throw new System.InvalidOperationException("请配置相机预制体中的相机与三个机位定位点。");
            sceneCamera.transform.SetPositionAndRotation(tablePose.position, tablePose.rotation);
        }

        public IEnumerator MoveTo(BattleCameraPose pose)
        {
            Transform target = pose == BattleCameraPose.Table ? tablePose : pose == BattleCameraPose.Scoring ? scoringPose : shootingPose;
            Transform cameraTransform = sceneCamera.transform;
            cameraTransform.DOKill();
            cameraTransform.DOMove(target.position, transitionDuration).SetEase(Ease.InOutSine);
            cameraTransform.DORotateQuaternion(target.rotation, transitionDuration).SetEase(Ease.InOutSine);
            yield return new WaitForSeconds(transitionDuration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.65f, 0.35f);
            foreach (Transform pose in new[] { tablePose, scoringPose, shootingPose })
            {
                if (pose == null) continue;
                Gizmos.DrawWireSphere(pose.position, 0.12f);
                Gizmos.DrawRay(pose.position, pose.forward * 0.65f);
            }
        }

        private void OnDestroy() { if (sceneCamera != null) sceneCamera.transform.DOKill(); }
    }
}
