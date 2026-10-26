using System;
using System.Collections;
using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    // A saved tabletop layout template: content, world motion and screen motion share one lifecycle.
    public sealed class TablePanel : MonoBehaviour
    {
        [SerializeField] private TableFlyGroup world;
        [SerializeField] private TableFlyGroup hud;
        [SerializeField] private TableFadeGroup hudFade;
        [SerializeField] private GameObject[] contentRoots;
        public bool IsVisible { get; private set; }

        public void Initialize()
        {
            if (world == null || (hud == null && hudFade == null)) throw new InvalidOperationException(name + "未配置 Panel 的桌面与界面转场。");
            world.Initialize();
            if (hudFade != null) hudFade.Initialize();
            else hud.Initialize();
            HideImmediately();
        }

        private void SetContent(bool visible)
        {
            if (contentRoots != null) foreach (GameObject root in contentRoots) if (root != null) root.SetActive(visible);
        }

        public void HideImmediately()
        {
            world.HideImmediately();
            if (hudFade != null) hudFade.HideImmediately();
            else hud.HideImmediately();
            SetContent(false);
            IsVisible = false;
        }

        public IEnumerator Show()
        {
            SetContent(true);
            Coroutine worldMotion = StartCoroutine(world.Play(true));
            Coroutine hudMotion = StartCoroutine(hudFade != null ? hudFade.Play(true) : hud.Play(true));
            yield return worldMotion;
            yield return hudMotion;
            IsVisible = true;
            if (hudFade != null) hudFade.SetInteractable(true);
        }

        public IEnumerator Hide()
        {
            IsVisible = false;
            if (hudFade != null) hudFade.SetInteractable(false);
            Coroutine worldMotion = StartCoroutine(world.Play(false));
            Coroutine hudMotion = StartCoroutine(hudFade != null ? hudFade.Play(false) : hud.Play(false));
            yield return worldMotion;
            yield return hudMotion;
            SetContent(false);
        }
    }
}
