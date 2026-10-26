using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class TableFadeGroup : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Min(0.01f)] private float duration = 0.3f;
        private Tween fade;

        public void Initialize()
        {
            if (canvasGroup == null) throw new InvalidOperationException(name + "未绑定 UI 淡入淡出的 CanvasGroup。");
        }

        public void SetInteractable(bool enabled)
        {
            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        public void HideImmediately()
        {
            Initialize();
            if (fade != null) fade.Kill();
            canvasGroup.alpha = 0;
            SetInteractable(false);
        }

        public IEnumerator Play(bool enter)
        {
            Initialize();
            if (fade != null) fade.Kill();
            SetInteractable(false);
            if (enter) canvasGroup.alpha = 0;
            fade = canvasGroup.DOFade(enter ? 1 : 0, duration).SetEase(Ease.InOutSine);
            yield return fade.WaitForCompletion();
            // The owning Panel enables input only after its world transition has also finished.
        }

        private void OnDisable()
        {
            if (fade != null) fade.Kill();
            if (canvasGroup != null) SetInteractable(false);
        }

        private void OnDestroy() { if (fade != null) fade.Kill(); }
    }
}
