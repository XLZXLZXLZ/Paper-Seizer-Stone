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
        [SerializeField] private GameObject[] contentRoots;
        public bool IsVisible { get; private set; }

        public void Initialize()
        {
            if (world == null || hud == null) throw new InvalidOperationException(name + "未配置 Panel 的桌面与界面转场。");
            world.Initialize();
            hud.Initialize();
            HideImmediately();
        }

        private void SetContent(bool visible)
        {
            if (contentRoots != null) foreach (GameObject root in contentRoots) if (root != null) root.SetActive(visible);
        }

        public void HideImmediately()
        {
            world.HideImmediately();
            hud.HideImmediately();
            SetContent(false);
            IsVisible = false;
        }

        public IEnumerator Show()
        {
            SetContent(true);
            Coroutine worldMotion = StartCoroutine(world.Play(true));
            Coroutine hudMotion = StartCoroutine(hud.Play(true));
            yield return worldMotion;
            yield return hudMotion;
            IsVisible = true;
        }

        public IEnumerator Hide()
        {
            IsVisible = false;
            Coroutine worldMotion = StartCoroutine(world.Play(false));
            Coroutine hudMotion = StartCoroutine(hud.Play(false));
            yield return worldMotion;
            yield return hudMotion;
            SetContent(false);
        }
    }
}
