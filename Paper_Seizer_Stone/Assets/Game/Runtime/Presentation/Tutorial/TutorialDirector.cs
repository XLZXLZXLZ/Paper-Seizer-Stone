using System;
using System.Collections;
using System.Collections.Generic;
using Febucci.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PaperSeizerStone.Audio;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // All visuals and the dedicated speech source are authored in TutorialOverlay.prefab.
    // No PlayerPrefs: a new adventure receives a fresh set of completed nodes.
    public sealed class TutorialDirector : MonoBehaviour
    {
        [SerializeField] private TutorialDefinition definition;
        [SerializeField] private GameObject inputShield;
        [SerializeField] private GameObject dialoguePanel;
        [SerializeField] private TextAnimator_TMP dialogueText;
        [SerializeField, Range(0, 1)] private float subtitleOutlineWidth = 0.18f;
        [SerializeField] private Color subtitleOutlineColor = new Color(0.03f, 0.025f, 0.02f, 1);
        [SerializeField] private AudioSource speechSource;
        [SerializeField] private GameObject manualPanel;
        [SerializeField] private TMP_Text manualTitle;
        [SerializeField] private TMP_Text manualBody;
        [SerializeField] private TMP_Text pageNumber;
        [SerializeField] private Button manualButton;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button[] pageButtons;

        private readonly HashSet<TutorialNode> completed = new HashSet<TutorialNode>();
        private readonly System.Random voiceRandom = new System.Random();
        private ShopDefinition shop;
        private bool running;
        private bool manualOpen;
        private bool inputGuard;
        private int releaseFrame;
        private float nextBlipTime;
        private ManualPage currentPage;
        private int revision;
        public bool ManualRequested { get; private set; }
        public bool IsPresenting => running || manualOpen;
        public bool BlocksInput => IsPresenting || ManualRequested || inputGuard;

        private void Awake()
        {
            inputShield.SetActive(false);
            dialoguePanel.SetActive(false);
            manualPanel.SetActive(false);
            manualButton.onClick.AddListener(RequestManual);
            previousButton.onClick.AddListener(() => TurnPage(-1));
            nextButton.onClick.AddListener(() => TurnPage(1));
            closeButton.onClick.AddListener(CloseManual);
            for (int i = 0; i < pageButtons.Length; i++)
            {
                ManualPage page = (ManualPage)i;
                pageButtons[i].onClick.AddListener(() => ShowPage(page));
            }
        }

        public void Initialize(ShopDefinition shopDefinition)
        {
            if (definition == null) throw new InvalidOperationException("请绑定初期演出与玩法手册配置。");
            shop = shopDefinition;
            completed.Clear();
        }

        public bool HasPlayed(TutorialNode node) => completed.Contains(node);
        public void RequestManual()
        {
            if (manualOpen) CloseManual();
            else ManualRequested = true;
        }

        private bool IsCurrent(int run) => run == revision && isActiveAndEnabled;

        private static bool HeldInput() => Input.GetMouseButton(0) || Input.GetKey(KeyCode.Return)
            || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape);

        private void GuardInput()
        {
            inputGuard = true;
            releaseFrame = Time.frameCount + 1;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void Update()
        {
            if (inputGuard && Time.frameCount > releaseFrame && !HeldInput()) inputGuard = false;
            if (!manualOpen || inputGuard || !Application.isFocused) return;
            if (Input.GetKeyDown(KeyCode.Escape)) CloseManual();
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) TurnPage(-1);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) TurnPage(1);
        }

        public IEnumerator Play(TutorialNode node)
        {
            if (HasPlayed(node)) yield break;
            TutorialSequence sequence = definition.Find(node);
            if (sequence == null) yield break;
            int run = revision;
            running = true;
            // Speech uses a bottom subtitle only. The dimmed backdrop belongs to the manual.
            inputShield.SetActive(false);
            try
            {
                foreach (TutorialLine line in sequence.lines)
                {
                    if (!IsCurrent(run)) yield break;
                    if (line != null && !string.IsNullOrEmpty(line.text)) yield return Say(line, run);
                }
                if (!IsCurrent(run)) yield break;
                dialoguePanel.SetActive(false);
                if (sequence.openManual) yield return OpenManual(sequence.page);
                // An interrupted sequence must remain eligible for a later attempt.
                if (IsCurrent(run)) completed.Add(node);
            }
            finally
            {
                speechSource.Stop();
                dialoguePanel.SetActive(false);
                running = false;
                inputShield.SetActive(manualOpen);
                GuardInput();
            }
        }

        private IEnumerator Say(TutorialLine line, int run)
        {
            dialoguePanel.SetActive(true);
            // The saved subtitle starts inactive. Activate and initialize TMP before its
            // outline setters access the CanvasRenderer and create a local material instance.
            TMP_Text subtitle = dialogueText.TMProComponent;
            subtitle.ForceMeshUpdate();
            subtitle.outlineWidth = subtitleOutlineWidth;
            subtitle.outlineColor = subtitleOutlineColor;
            dialogueText.timeScale = TimeScale.Unscaled;
            dialogueText.SetText(line.text, true);
            speechSource.Stop();
            speechSource.pitch = 1;
            speechSource.volume = definition.speechVolume * GameAudioSettings.Effects;
            if (line.voice != null)
            {
                speechSource.clip = line.voice;
                speechSource.Play();
            }
            nextBlipTime = 0;
            float nextCharacter = Time.unscaledTime;
            int index = 0;
            while (IsCurrent(run) && index < dialogueText.CharactersCount)
            {
                if (Time.unscaledTime >= nextCharacter)
                {
                    char character = dialogueText.Characters[index].info.character;
                    dialogueText.SetVisibilityChar(index++, true);
                    bool punctuation = char.IsPunctuation(character);
                    if (line.voice == null && !punctuation && !char.IsWhiteSpace(character)) PlayBlip();
                    nextCharacter = Time.unscaledTime + Mathf.Max(0.01f, line.characterSeconds) * (punctuation ? 4 : 1);
                }
                yield return null;
            }
            if (line.voice == null) speechSource.Stop();
            float holdUntil = Time.unscaledTime + Mathf.Max(0, line.holdSeconds);
            while (IsCurrent(run) && (Time.unscaledTime < holdUntil || (line.voice != null && speechSource.isPlaying))) yield return null;
            speechSource.Stop();
        }

        private void PlayBlip()
        {
            if (Time.unscaledTime < nextBlipTime || definition.speechBlips.Length == 0) return;
            AudioClip clip = definition.speechBlips[voiceRandom.Next(definition.speechBlips.Length)];
            if (clip == null) return;
            speechSource.volume = definition.speechVolume * GameAudioSettings.Effects;
            speechSource.pitch = Mathf.Lerp(definition.blipPitch.x, definition.blipPitch.y, (float)voiceRandom.NextDouble());
            speechSource.PlayOneShot(clip);
            nextBlipTime = Time.unscaledTime + Mathf.Max(0.03f, definition.blipInterval);
        }

        public IEnumerator OpenRequestedManual()
        {
            if (ManualRequested) yield return OpenManual(currentPage);
        }

        private IEnumerator OpenManual(ManualPage page)
        {
            int run = revision;
            ManualRequested = false;
            manualOpen = true;
            inputShield.SetActive(true);
            manualPanel.SetActive(true);
            GuardInput();
            ShowPage(page);
            try
            {
                while (IsCurrent(run) && manualOpen) yield return null;
            }
            finally
            {
                manualOpen = false;
                manualPanel.SetActive(false);
                inputShield.SetActive(false);
                GuardInput();
            }
        }

        private void ShowPage(ManualPage page)
        {
            if (!manualOpen) return;
            ManualEntry entry = definition.Find(page);
            if (entry == null) return;
            currentPage = page;
            manualTitle.text = entry.title;
            manualBody.text = entry.body.Replace("{burnPrice}", shop.RemovePrice.ToString())
                .Replace("{minimumDeck}", shop.MinimumDeckSize.ToString());
            pageNumber.text = ((int)page + 1) + " / " + pageButtons.Length;
            previousButton.interactable = (int)page > 0;
            nextButton.interactable = (int)page < pageButtons.Length - 1;
            for (int i = 0; i < pageButtons.Length; i++) pageButtons[i].interactable = i != (int)page;
        }

        private void TurnPage(int direction) => ShowPage((ManualPage)Mathf.Clamp((int)currentPage + direction, 0, pageButtons.Length - 1));

        private void CloseManual()
        {
            if (!manualOpen || inputGuard) return;
            manualOpen = false;
            manualPanel.SetActive(false);
            GuardInput();
        }

        public void Cancel()
        {
            revision++;
            StopAllCoroutines();
            running = false;
            manualOpen = false;
            ManualRequested = false;
            if (speechSource != null) speechSource.Stop();
            dialoguePanel.SetActive(false);
            manualPanel.SetActive(false);
            inputShield.SetActive(false);
            GuardInput();
        }

        private void OnDisable() => Cancel();
    }
}
