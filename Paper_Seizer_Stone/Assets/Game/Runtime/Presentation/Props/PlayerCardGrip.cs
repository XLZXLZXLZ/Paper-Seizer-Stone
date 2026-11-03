using UnityEngine;

namespace PaperSeizerStone.Presentation.Props
{
    // Animation owns the bones; this component exposes the authored grip and its arm visibility.
    public sealed class PlayerCardGrip : MonoBehaviour
    {
        [SerializeField, Tooltip("随左手动画运动的握牌锚点，局部 XY 为牌面横竖方向。")]
        private Transform gripAnchor;
        [SerializeField] private SkinnedMeshRenderer firstPersonArms;

        private Camera viewCamera;
        private bool presentationActive;
        private bool shootingPresentation;

        public Transform Anchor => gripAnchor;

        public void Initialize(Camera camera)
        {
            viewCamera = camera;
            ApplyVisibility();
        }

        public void SetPresentationActive(bool value)
        {
            presentationActive = value;
            ApplyVisibility();
        }

        public void SetShootingPresentation(bool value)
        {
            shootingPresentation = value;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (firstPersonArms != null)
                firstPersonArms.forceRenderingOff = !(presentationActive || shootingPresentation)
                    || !isActiveAndEnabled || viewCamera == null || gripAnchor == null;
        }

        private void OnEnable() { ApplyVisibility(); }

        private void OnDisable()
        {
            if (firstPersonArms != null) firstPersonArms.forceRenderingOff = true;
        }

    }
}
