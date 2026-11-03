using System.Collections;
using PaperSeizerStone.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PaperSeizerStone.Presentation.MainMenu
{
    public sealed class MenuSettingsPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup panel;
        [SerializeField] private Slider master;
        [SerializeField] private Slider music;
        [SerializeField] private Slider effects;
        [SerializeField] private TMP_Text masterValue;
        [SerializeField] private TMP_Text musicValue;
        [SerializeField] private TMP_Text effectsValue;
        [SerializeField] private Button close;
        [SerializeField] private TMP_Text status;
        [SerializeField, Min(0.05f)] private float fadeDuration = 0.22f;
        private Coroutine fade;
        private bool open;
        public bool BlocksMenu => open || panel.gameObject.activeSelf;

        private void Awake()
        {
            panel.alpha = 0;
            panel.interactable = panel.blocksRaycasts = false;
            panel.gameObject.SetActive(false);
            master.SetValueWithoutNotify(GameAudioSettings.Master);
            music.SetValueWithoutNotify(GameAudioSettings.Music);
            effects.SetValueWithoutNotify(GameAudioSettings.Effects);
            master.onValueChanged.AddListener(ChangeMaster);
            music.onValueChanged.AddListener(ChangeMusic);
            effects.onValueChanged.AddListener(ChangeEffects);
            close.onClick.AddListener(Hide);
            RefreshValues();
        }

        private void ChangeMaster(float value) { GameAudioSettings.SetMaster(value); RefreshValues(); }
        private void ChangeMusic(float value) { GameAudioSettings.SetMusic(value); RefreshValues(); }
        private void ChangeEffects(float value) { GameAudioSettings.SetEffects(value); RefreshValues(); }
        private void RefreshValues()
        {
            masterValue.text = Mathf.RoundToInt(master.value * 100) + "%";
            musicValue.text = Mathf.RoundToInt(music.value * 100) + "%";
            effectsValue.text = Mathf.RoundToInt(effects.value * 100) + "%";
        }

        public void Show() => SetOpen(true);
        public void Hide() => SetOpen(false);
        public void ShowStatus(string message) => status.text = message;

        private void Update()
        {
            if (open && Input.GetKeyDown(KeyCode.Escape)) Hide();
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
            if (!visible) panel.gameObject.SetActive(false);
            else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(master.gameObject);
            fade = null;
        }

        private void OnApplicationPause(bool paused) { if (paused) GameAudioSettings.Save(); }
        private void OnApplicationQuit() => GameAudioSettings.Save();

        private void OnDestroy()
        {
            master.onValueChanged.RemoveListener(ChangeMaster);
            music.onValueChanged.RemoveListener(ChangeMusic);
            effects.onValueChanged.RemoveListener(ChangeEffects);
            close.onClick.RemoveListener(Hide);
        }
    }
}
