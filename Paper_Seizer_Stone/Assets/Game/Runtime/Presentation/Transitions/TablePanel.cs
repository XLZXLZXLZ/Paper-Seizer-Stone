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
            if (world == null) throw new InvalidOperationException(name + "未配置 Panel 的桌面转场。");
            world.Initialize();
            if (hudFade != null) hudFade.Initialize();
            else if (hud != null) hud.Initialize();
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
            else if (hud != null) hud.HideImmediately();
            SetContent(false);
            IsVisible = false;
        }

        public IEnumerator Show(Transform excludedWorldRoot = null, bool showHud = true, Transform secondaryExcludedWorldRoot = null)
        {
            if (excludedWorldRoot != null) excludedWorldRoot.gameObject.SetActive(false);
            if (secondaryExcludedWorldRoot != null) secondaryExcludedWorldRoot.gameObject.SetActive(false);
            SetContent(true);
            Coroutine worldMotion = StartCoroutine(world.Play(true, excludedWorldRoot, secondaryExcludedWorldRoot));
            Coroutine hudMotion = !showHud ? null : hudFade != null ? StartCoroutine(hudFade.Play(true))
                : hud != null ? StartCoroutine(hud.Play(true)) : null;
            yield return worldMotion;
            if (hudMotion != null) yield return hudMotion;
            IsVisible = true;
            if (showHud && hudFade != null) hudFade.SetInteractable(true);
        }

        public IEnumerator ShowHud()
        {
            if (hudFade != null)
            {
                yield return hudFade.Play(true);
                hudFade.SetInteractable(true);
            }
            else if (hud != null) yield return hud.Play(true);
        }

        public IEnumerator Hide()
        {
            IsVisible = false;
            if (hudFade != null) hudFade.SetInteractable(false);
            Coroutine worldMotion = StartCoroutine(world.Play(false));
            Coroutine hudMotion = hudFade != null ? StartCoroutine(hudFade.Play(false))
                : hud != null ? StartCoroutine(hud.Play(false)) : null;
            yield return worldMotion;
            if (hudMotion != null) yield return hudMotion;
            SetContent(false);
        }
    }
}
