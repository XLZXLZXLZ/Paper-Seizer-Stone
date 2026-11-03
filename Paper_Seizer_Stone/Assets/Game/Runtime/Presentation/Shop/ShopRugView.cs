using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    // The saved rug prefab is centered using its fully unrolled mesh, not its edge pivot.
    public sealed class ShopRugView : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float placementDuration = 0.4f;
        [SerializeField, Min(0)] private float placementHeight = 1.2f;
        private Animator animator;
        private Vector3 homePosition;
        private Tween movement;
        private bool initialized;
        private static readonly int RolledIdle = Animator.StringToHash("Base Layer.RolledIdle");
        private static readonly int OpenIdle = Animator.StringToHash("Base Layer.OpenIdle");
        private static readonly int Unroll = Animator.StringToHash("Base Layer.Unroll");
        private static readonly int RollUp = Animator.StringToHash("Base Layer.RollUp");

        public void Initialize()
        {
            if (initialized) return;
            animator = GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException(name + "缺少毯子动画控制器。");
            if (!animator.HasState(0, RolledIdle) || !animator.HasState(0, OpenIdle)
                || !animator.HasState(0, Unroll) || !animator.HasState(0, RollUp))
                throw new InvalidOperationException(name + "缺少毯子铺开、卷起及定格状态。");
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            homePosition = transform.localPosition;
            initialized = true;
            HideImmediately();
        }

        public void HideImmediately()
        {
            if (movement != null) movement.Kill();
            if (initialized) transform.localPosition = homePosition;
            gameObject.SetActive(false);
        }

        public IEnumerator Show()
        {
            Initialize();
            transform.localPosition = homePosition + Vector3.up * placementHeight;
            gameObject.SetActive(true);
            animator.ResetTrigger("Open");
            animator.ResetTrigger("Close");
            animator.Play(RolledIdle, 0, 0);
            animator.Update(0);
            movement = transform.DOLocalMove(homePosition, placementDuration).SetEase(Ease.OutCubic);
            yield return movement.WaitForCompletion();
            animator.Play(Unroll, 0, 0);
            animator.Update(0);
            // Wait for the real terminal pose, including the controller's exit transition.
            while (animator.GetCurrentAnimatorStateInfo(0).fullPathHash != OpenIdle)
                yield return null;
        }

        public IEnumerator Hide()
        {
            animator.Play(RollUp, 0, 0);
            animator.Update(0);
            while (animator.GetCurrentAnimatorStateInfo(0).fullPathHash != RolledIdle)
                yield return null;
            movement = transform.DOLocalMove(homePosition + Vector3.up * placementHeight,
                placementDuration).SetEase(Ease.InCubic);
            yield return movement.WaitForCompletion();
            HideImmediately();
        }

        private void OnDisable() { if (movement != null) movement.Kill(); }
        private void OnDestroy() { if (movement != null) movement.Kill(); }
    }
}
