using System.Collections;
using PaperSeizerStone.Audio;
using PaperSeizerStone.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PaperSeizerStone.Presentation.MainMenu
{
    public sealed class MenuSettingsPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup panel;
        [SerializeField] private Slider master;
        [SerializeField] private Slider music;
        [SerializeField] private Slider effects;
        [SerializeField] private Toggle transparentCards;
        [SerializeField] private Toggle distinctCardBacks;
        [SerializeField] private TMP_Text masterValue;
        [SerializeField] private TMP_Text musicValue;
        [SerializeField] private TMP_Text effectsValue;
        [SerializeField] private Button close;
        [SerializeField] private Button returnToMenu;
        [SerializeField] private string mainMenuScene = "Assets/Scenes/MainMenu.unity";
        [SerializeField] private TMP_Text status;
        [SerializeField, Min(0.05f)] private float fadeDuration = 0.22f;
        [SerializeField] private bool handleEscape = true;
        private Coroutine fade;
        private bool open;
        private bool initialized;
        public bool IsReady => initialized;
        public bool IsOpen => open;
        public int LastClosedFrame { get; private set; } = -1;
        public bool BlocksMenu => panel != null && (open || panel.gameObject.activeSelf);

        private void Awake()
        {
            if (panel == null || master == null || music == null || effects == null
                || transparentCards == null || distinctCardBacks == null || masterValue == null
                || musicValue == null || effectsValue == null || close == null)
            {
                Debug.LogError("设置面板的控件引用不完整，请检查设置预制体是否正确导入及绑定。", this);
                enabled = false;
                return;
            }
            panel.alpha = 0;
            panel.interactable = panel.blocksRaycasts = false;
            panel.gameObject.SetActive(false);
            master.SetValueWithoutNotify(GameAudioSettings.Master);
            music.SetValueWithoutNotify(GameAudioSettings.Music);
            effects.SetValueWithoutNotify(GameAudioSettings.Effects);
            RefreshDisplayToggles();
            master.onValueChanged.AddListener(ChangeMaster);
            music.onValueChanged.AddListener(ChangeMusic);
            effects.onValueChanged.AddListener(ChangeEffects);
            transparentCards.onValueChanged.AddListener(ChangeTransparency);
            distinctCardBacks.onValueChanged.AddListener(ChangeCardBacks);
            GameDisplaySettings.Changed += RefreshDisplayToggles;
            close.onClick.AddListener(Hide);
            if (returnToMenu != null) returnToMenu.onClick.AddListener(ReturnToMainMenu);
            RefreshValues();
            initialized = true;
        }

        private void ChangeMaster(float value) { GameAudioSettings.SetMaster(value); RefreshValues(); }
        private void ChangeMusic(float value) { GameAudioSettings.SetMusic(value); RefreshValues(); }
        private void ChangeEffects(float value) { GameAudioSettings.SetEffects(value); RefreshValues(); }
        private void ChangeTransparency(bool value) => GameDisplaySettings.SetTransparentPlayerCards(value);
        private void ChangeCardBacks(bool value) => GameDisplaySettings.SetDistinctCardBacks(value);

        private void RefreshDisplayToggles()
        {
            transparentCards.SetIsOnWithoutNotify(GameDisplaySettings.TransparentPlayerCards);
            distinctCardBacks.SetIsOnWithoutNotify(GameDisplaySettings.DistinctCardBacks);
        }
        private void RefreshValues()
        {
            masterValue.text = Mathf.RoundToInt(master.value * 100) + "%";
            musicValue.text = Mathf.RoundToInt(music.value * 100) + "%";
            effectsValue.text = Mathf.RoundToInt(effects.value * 100) + "%";
        }

        public void Show() { if (initialized && isActiveAndEnabled) SetOpen(true); }
        public void Hide() { if (initialized && isActiveAndEnabled) SetOpen(false); }
        public void ShowStatus(string message) { if (status != null) status.text = message; }

        private void ReturnToMainMenu()
        {
            if (!initialized || !open || !panel.interactable) return;
            if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
            {
                const string message = "主菜单场景未加入构建列表，请配置 MainMenu。";
                ShowStatus(message);
                Debug.LogError(message, this);
                return;
            }
            GameAudioSettings.Save();
            if (fade != null) StopCoroutine(fade);
            fade = null;
            panel.interactable = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            SceneManager.LoadScene(mainMenuScene);
        }

        private void Update()
        {
            if (handleEscape && Application.isFocused && open && Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (fade != null) StopCoroutine(fade);
            panel.gameObject.SetActive(true);
            panel.blocksRaycasts = true;
            panel.interactable = value;
            if (!value)
            {
                GameAudioSettings.Save();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            fade = StartCoroutine(Fade(value));
        }

        private IEnumerator Fade(bool visible)
        {
            float from = panel.alpha;
            float elapsed = 0;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                panel.alpha = Mathf.Lerp(from, visible ? 1 : 0,
                    Mathf.SmoothStep(0, 1, elapsed / fadeDuration));
                yield return null;
            }
            panel.alpha = visible ? 1 : 0;
            panel.blocksRaycasts = visible;
            if (!visible)
            {
                LastClosedFrame = Time.frameCount;
                panel.gameObject.SetActive(false);
            }
            else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(master.gameObject);
            fade = null;
        }

        private void OnApplicationPause(bool paused) { if (paused) GameAudioSettings.Save(); }
        private void OnApplicationQuit() => GameAudioSettings.Save();

        public void CloseImmediately()
        {
            if (fade != null) StopCoroutine(fade);
            fade = null;
            open = false;
            LastClosedFrame = Time.frameCount;
            if (panel != null)
            {
                panel.alpha = 0;
                panel.interactable = panel.blocksRaycasts = false;
                panel.gameObject.SetActive(false);
            }
            GameAudioSettings.Save();
        }

        private void OnDisable() => CloseImmediately();

        private void OnDestroy()
        {
            if (!initialized) return;
            master.onValueChanged.RemoveListener(ChangeMaster);
            music.onValueChanged.RemoveListener(ChangeMusic);
            effects.onValueChanged.RemoveListener(ChangeEffects);
            transparentCards.onValueChanged.RemoveListener(ChangeTransparency);
            distinctCardBacks.onValueChanged.RemoveListener(ChangeCardBacks);
            GameDisplaySettings.Changed -= RefreshDisplayToggles;
            close.onClick.RemoveListener(Hide);
            if (returnToMenu != null) returnToMenu.onClick.RemoveListener(ReturnToMainMenu);
        }
    }
}
