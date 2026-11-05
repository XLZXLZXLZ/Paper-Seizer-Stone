using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PaperSeizerStone.Presentation.MainMenu
{
    // Scene, disc, UI and table are saved assets; this only handles interaction and flow.
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Camera menuCamera;
        [SerializeField] private Transform disc;
        [SerializeField] private MenuSettingsPanel settings;
        [SerializeField] private string gameplayScene = "Assets/Scenes/BasicBattle.unity";
        [SerializeField] private LayerMask interactionMask = 1;
        [Header("转盘缓慢循环旋转")]
        [SerializeField] private bool rotateDisc = true;
        [SerializeField, Range(-20, 20), Tooltip("每秒旋转角度；2度约3分钟一圈，负数反转，0停止。")] private float discRotationSpeed = 2f;
        private MenuSector hovered;
        private MenuSector pressed;
        private bool busy;

        private void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1;
        }

        private void Update()
        {
            if (busy || settings.BlocksMenu || !Application.isFocused)
            {
                SetHovered(null);
                pressed = null;
                return;
            }
            Vector3 cursor = Input.mousePosition;
            bool inside = cursor.x >= 0 && cursor.y >= 0 && cursor.x < Screen.width && cursor.y < Screen.height;
            MenuSector target = null;
            if (inside && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) &&
                Physics.Raycast(menuCamera.ScreenPointToRay(cursor), out RaycastHit hit, 100, interactionMask))
            {
                MenuSector candidate = hit.collider.GetComponentInParent<MenuSector>();
                if (candidate != null && candidate.transform.IsChildOf(disc)) target = candidate;
            }
            SetHovered(target);
            if (Input.GetMouseButtonDown(0)) pressed = target;
            if (Input.GetMouseButtonUp(0))
            {
                MenuSector clicked = pressed;
                pressed = null;
                if (clicked != null && clicked == target) StartCoroutine(Activate(clicked));
            }
            if (rotateDisc && disc != null && hovered == null && pressed == null
                && !busy && !settings.BlocksMenu && !Mathf.Approximately(discRotationSpeed, 0f))
            {
                // Rotate the complete disc around its local normal, preserving the authored tilt.
                // Colliders and visuals turn together; only the existing Visual hover motion is separate.
                disc.Rotate(Vector3.up, discRotationSpeed * Time.unscaledDeltaTime, Space.Self);
                Physics.SyncTransforms();
            }
        }

        private void SetHovered(MenuSector target)
        {
            if (hovered == target) return;
            if (hovered != null) hovered.SetHovered(false);
            hovered = target;
            if (hovered != null) hovered.SetHovered(true);
        }

        private IEnumerator Activate(MenuSector sector)
        {
            busy = true;
            yield return sector.ClickFeedback();
            switch (sector.Action)
            {
                case MenuAction.StartGame:
                    if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
                    {
                        settings.ShowStatus("游玩场景未加入构建列表，请配置 BasicBattle。");
                        break;
                    }
                    PaperSeizerStone.Audio.GameAudioSettings.Save();
                    settings.ShowStatus("正在进入游戏…");
                    yield return SceneManager.LoadSceneAsync(gameplayScene);
                    break;
                case MenuAction.Settings:
                    settings.Show();
                    break;
                case MenuAction.Quit:
                    PaperSeizerStone.Audio.GameAudioSettings.Save();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
            busy = false;
        }

        private void OnDisable() { SetHovered(null); pressed = null; }
    }
}
