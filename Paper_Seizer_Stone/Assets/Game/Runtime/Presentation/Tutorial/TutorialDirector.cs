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
        [SerializeField] private CanvasGroup subtitleGroup;
        [SerializeField, Min(0.01f)] private float subtitleFadeInSeconds = 0.1f;
        [SerializeField, Min(0.01f)] private float subtitleFadeOutSeconds = 0.12f;
        [SerializeField, Range(0.3f, 0.95f)] private float subtitleMaximumWidth = 0.7f;
        [SerializeField, Range(0, 1)] private float subtitleOutlineWidth = 0.18f;
        [SerializeField] private Color subtitleOutlineColor = new Color(0.03f, 0.025f, 0.02f, 1);
        [SerializeField, Min(0.01f)] private float hintCharacterSeconds = 0.045f;
        [SerializeField, Min(0)] private float hintWarningHoldSeconds = 1.2f;
        [SerializeField] private AudioSource speechSource;
        [SerializeField] private GameObject manualPanel;
        [SerializeField] private CanvasGroup manualGroup;
        [SerializeField] private CanvasGroup[] pageContentGroups;
        [SerializeField, Min(0.01f)] private float manualOpenSeconds = 0.22f;
        [SerializeField, Min(0.01f)] private float manualCloseSeconds = 0.16f;
        [SerializeField, Range(0.01f, 1)] private float manualClosedScale = 0.12f;
        [SerializeField, Min(0.01f)] private float pageFadeSeconds = 0.14f;
        [SerializeField] private TMP_Text manualTitle;
        [SerializeField] private TMP_Text manualBody;
        [SerializeField] private TutorialManualView manualView;
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
        private bool manualTransitioning;
        private bool manualCloseRequested;
        private RectTransform manualRect;
        private Vector2 manualRestPosition;
        private Vector3 manualRestScale;
        private Coroutine pageTransition;
        private bool inputGuard;
        private int releaseFrame;
        private float nextBlipTime;
        private ManualPage currentPage;
        private int revision;
        private UnityEngine.Object hintOwner;
        private string hintText;
        private string hintWarning;
        private string displayedHint;
        private bool displayingHint;
        private bool displayingWarning;
        private int hintCharacter;
        private float nextHintCharacter;
        private float warningHoldUntil = -1;
        private float subtitleTargetAlpha;
        public bool ManualRequested { get; private set; }
        public bool IsPresenting => running || manualOpen;
        public bool BlocksInput => IsPresenting || ManualRequested || inputGuard;

        private void Awake()
        {
            // Fallback only covers untagged characters, preserving authored shake modifiers.
            dialogueText.DefaultBehaviorsTags = new[] { "shake" };
            dialogueText.defaultTagsMode = Febucci.UI.Core.TAnimCore.DefaultTagsMode.Fallback;
            inputShield.SetActive(false);
            HideSubtitle();
            manualRect = (RectTransform)manualPanel.transform;
            manualRestPosition = manualRect.anchoredPosition;
            manualRestScale = manualRect.localScale;
            manualPanel.SetActive(false);
            ResetManualVisuals();
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

        // Hints borrow the subtitle visuals without taking the tutorial's input lock.
        // Views may refresh the same hint every frame without restarting its typewriter.
        public void SetHint(UnityEngine.Object owner, string text)
        {
            if (owner == null) return;
            if (hintOwner != owner)
            {
                StopHintDisplay();
                hintOwner = owner;
                hintWarning = null;
            }
            hintText = text;
        }

        public void ClearHint(UnityEngine.Object owner)
        {
            if (hintOwner != owner) return;
            hintOwner = null;
            hintText = null;
            hintWarning = null;
            StopHintDisplay();
        }

        public void ShowHintWarning(UnityEngine.Object owner, string message)
        {
            if (owner == null || string.IsNullOrEmpty(message)) return;
            if (hintOwner != owner) SetHint(owner, null);
            hintWarning = message;
            // A new click can repeat the warning, even when its wording is unchanged.
            StopHintDisplay();
        }

        private void StopHintDisplay()
        {
            warningHoldUntil = -1;
            if (!displayingHint) return;
            displayingHint = false;
            displayedHint = null;
            FadeOutSubtitle();
            speechSource.Stop();
        }

        private void FadeOutSubtitle()
        {
            subtitleTargetAlpha = 0;
            if (subtitleGroup == null) HideSubtitle();
        }

        private void HideSubtitle()
        {
            subtitleTargetAlpha = 0;
            if (subtitleGroup != null) subtitleGroup.alpha = 0;
            dialoguePanel.SetActive(false);
        }

        private void UpdateSubtitleFade()
        {
            if (subtitleGroup == null || !dialoguePanel.activeSelf) return;
            float duration = subtitleTargetAlpha > subtitleGroup.alpha
                ? subtitleFadeInSeconds : subtitleFadeOutSeconds;
            subtitleGroup.alpha = Mathf.MoveTowards(subtitleGroup.alpha, subtitleTargetAlpha,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
            if (subtitleTargetAlpha == 0 && subtitleGroup.alpha == 0) dialoguePanel.SetActive(false);
        }

        private void LateUpdate()
        {
            if (BlocksInput || !Application.isFocused)
            {
                StopHintDisplay();
                return;
            }
            if (hintOwner == null)
            {
                hintText = null;
                hintWarning = null;
                StopHintDisplay();
                return;
            }
            if (!string.IsNullOrEmpty(hintWarning) && displayingHint && displayingWarning
                && warningHoldUntil >= 0 && Time.unscaledTime >= warningHoldUntil)
            {
                hintWarning = null;
                StopHintDisplay();
            }
            bool warning = !string.IsNullOrEmpty(hintWarning);
            string text = warning ? hintWarning : hintText;
            if (string.IsNullOrEmpty(text))
            {
                StopHintDisplay();
                return;
            }
            if (!displayingHint || displayedHint != text || displayingWarning != warning)
            {
                StopHintDisplay();
                PrepareSubtitle(text);
                displayedHint = text;
                displayingHint = true;
                displayingWarning = warning;
                hintCharacter = 0;
                nextHintCharacter = Time.unscaledTime;
                nextBlipTime = 0;
            }
            if (hintCharacter < dialogueText.CharactersCount && Time.unscaledTime >= nextHintCharacter)
            {
                char character = dialogueText.Characters[hintCharacter].info.character;
                dialogueText.SetVisibilityChar(hintCharacter++, true);
                bool punctuation = char.IsPunctuation(character);
                if (!punctuation && !char.IsWhiteSpace(character)) PlayBlip();
                nextHintCharacter = Time.unscaledTime + Mathf.Max(0.01f, hintCharacterSeconds) * (punctuation ? 4 : 1);
            }
            if (warning && hintCharacter >= dialogueText.CharactersCount && warningHoldUntil < 0)
                warningHoldUntil = Time.unscaledTime + Mathf.Max(0, hintWarningHoldSeconds);
        }

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
            UpdateSubtitleFade();
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
            StopHintDisplay();
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
                HideSubtitle();
                if (sequence.openManual) yield return OpenManual(sequence.page);
                // An interrupted sequence must remain eligible for a later attempt.
                if (IsCurrent(run)) completed.Add(node);
            }
            finally
            {
                speechSource.Stop();
                HideSubtitle();
                running = false;
                inputShield.SetActive(manualOpen);
                GuardInput();
            }
        }

        private void PrepareSubtitle(string text)
        {
            if (subtitleGroup != null) subtitleGroup.alpha = 0;
            subtitleTargetAlpha = 1;
            dialoguePanel.SetActive(true);
            // The saved subtitle starts inactive. Activate and initialize TMP before its
            // outline setters access the CanvasRenderer and create a local material instance.
            TMP_Text subtitle = dialogueText.TMProComponent;
            subtitle.ForceMeshUpdate();
            subtitle.outlineWidth = subtitleOutlineWidth;
            subtitle.outlineColor = subtitleOutlineColor;
            // Fit the complete sentence once, so the backing does not grow with each letter.
            GlobalFontService.Apply(subtitle);
            RectTransform backing = (RectTransform)dialoguePanel.transform;
            RectTransform parent = (RectTransform)backing.parent;
            float maximumWidth = Mathf.Max(128, parent.rect.width * subtitleMaximumWidth);
            Vector2 preferred = subtitle.GetPreferredValues(text, maximumWidth - 96, Mathf.Infinity);
            backing.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Clamp(preferred.x + 96, 128, maximumWidth));
            backing.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(56, preferred.y + 16));
            dialogueText.timeScale = TimeScale.Unscaled;
            dialogueText.SetText(text, true);
        }

        private IEnumerator Say(TutorialLine line, int run)
        {
            PrepareSubtitle(line.text);
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
            if (IsCurrent(run))
            {
                FadeOutSubtitle();
                while (IsCurrent(run) && dialoguePanel.activeSelf) yield return null;
            }
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
            StopHintDisplay();
            ManualRequested = false;
            manualOpen = true;
            manualCloseRequested = false;
            manualTransitioning = true;
            inputShield.SetActive(true);
            GuardInput();
            try
            {
                ResetManualVisuals();
                ApplyPage(page);
                RefreshPageNavigation();
                Vector2 closedPosition = ManualButtonPosition();
                SetManualPose(closedPosition, manualClosedScale, 0);
                manualPanel.SetActive(true);
                yield return AnimateManual(closedPosition, true, run);
                if (!IsCurrent(run)) yield break;
                manualTransitioning = false;
                manualGroup.interactable = true;
                while (IsCurrent(run) && !manualCloseRequested) yield return null;
                if (!IsCurrent(run)) yield break;
                // Keep manualOpen and the full-screen shield until the fold finishes.
                yield return AnimateManual(ManualButtonPosition(), false, run);
            }
            finally
            {
                StopPageTransition();
                manualOpen = false;
                manualTransitioning = false;
                manualCloseRequested = false;
                manualPanel.SetActive(false);
                ResetManualVisuals();
                inputShield.SetActive(false);
                GuardInput();
            }
        }

        private Vector2 ManualButtonPosition()
        {
            RectTransform buttonRect = (RectTransform)manualButton.transform;
            Vector3 buttonCenter = manualRect.parent.InverseTransformPoint(buttonRect.TransformPoint(buttonRect.rect.center));
            // Convert the live button center into the panel's anchored coordinates.
            // Account for a non-centered panel pivot without changing the authored layout.
            Vector3 foldedCenterOffset = manualRect.localRotation
                * Vector3.Scale(manualRect.rect.center, manualRestScale * manualClosedScale);
            return manualRect.anchoredPosition + (Vector2)(buttonCenter - manualRect.localPosition - foldedCenterOffset);
        }

        private void SetManualPose(Vector2 position, float scale, float alpha)
        {
            manualRect.anchoredPosition = position;
            manualRect.localScale = manualRestScale * scale;
            manualGroup.alpha = alpha;
        }

        private IEnumerator AnimateManual(Vector2 closedPosition, bool opening, int run)
        {
            float duration = Mathf.Max(0.01f, opening ? manualOpenSeconds : manualCloseSeconds);
            float elapsed = 0;
            while (IsCurrent(run) && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Ease out on expansion; reverse that motion when folding away.
                float progress = opening ? 1 - Mathf.Pow(1 - t, 3) : 1 - t * t * t;
                SetManualPose(Vector2.Lerp(closedPosition, manualRestPosition, progress),
                    Mathf.Lerp(manualClosedScale, 1, progress), progress);
                yield return null;
            }
            if (IsCurrent(run))
                SetManualPose(opening ? manualRestPosition : closedPosition, opening ? 1 : manualClosedScale, opening ? 1 : 0);
        }

        private void ResetManualVisuals()
        {
            if (manualRect == null) return;
            SetManualPose(manualRestPosition, 1, 0);
            manualGroup.interactable = false;
            manualGroup.blocksRaycasts = true;
            SetPageAlpha(1);
        }

        private void ShowPage(ManualPage page)
        {
            if (!manualOpen || manualTransitioning || pageTransition != null || inputGuard || page == currentPage) return;
            if (definition.Find(page) == null) return;
            pageTransition = StartCoroutine(ChangePage(page, revision));
        }

        private void ApplyPage(ManualPage page)
        {
            ManualEntry entry = definition.Find(page);
            if (entry == null) return;
            currentPage = page;
            manualTitle.text = entry.title;
            string body = entry.body.Replace("{burnPrice}", shop.RemovePrice.ToString())
                .Replace("{minimumDeck}", shop.MinimumDeckSize.ToString());
            if (manualView != null) manualView.Display(page, body);
            else if (manualBody != null) manualBody.text = body;
            pageNumber.text = ((int)page + 1) + " / " + pageButtons.Length;
        }

        private void RefreshPageNavigation(bool interactable = true)
        {
            previousButton.interactable = interactable && (int)currentPage > 0;
            nextButton.interactable = interactable && (int)currentPage < pageButtons.Length - 1;
            for (int i = 0; i < pageButtons.Length; i++) pageButtons[i].interactable = interactable && i != (int)currentPage;
        }

        private void SetPageAlpha(float alpha)
        {
            foreach (CanvasGroup content in pageContentGroups) content.alpha = alpha;
        }

        private IEnumerator ChangePage(ManualPage page, int run)
        {
            RefreshPageNavigation(false);
            yield return FadePage(1, 0, run);
            if (!IsCurrent(run)) yield break;
            ApplyPage(page);
            yield return FadePage(0, 1, run);
            if (!IsCurrent(run)) yield break;
            pageTransition = null;
            RefreshPageNavigation();
        }

        private IEnumerator FadePage(float from, float to, int run)
        {
            float duration = Mathf.Max(0.01f, pageFadeSeconds * 0.5f);
            float elapsed = 0;
            while (IsCurrent(run) && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetPageAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / duration)));
                yield return null;
            }
            if (IsCurrent(run)) SetPageAlpha(to);
        }

        private void StopPageTransition()
        {
            if (pageTransition == null) return;
            StopCoroutine(pageTransition);
            pageTransition = null;
        }

        private void TurnPage(int direction) => ShowPage((ManualPage)Mathf.Clamp((int)currentPage + direction, 0, pageButtons.Length - 1));

        private void CloseManual()
        {
            if (!manualOpen || manualTransitioning || inputGuard) return;
            manualCloseRequested = true;
            manualTransitioning = true;
            manualGroup.interactable = false;
            StopPageTransition();
            GuardInput();
        }

        public void Cancel()
        {
            revision++;
            StopAllCoroutines();
            pageTransition = null;
            hintOwner = null;
            hintText = null;
            hintWarning = null;
            StopHintDisplay();
            running = false;
            manualOpen = false;
            manualTransitioning = false;
            manualCloseRequested = false;
            ManualRequested = false;
            if (speechSource != null) speechSource.Stop();
            HideSubtitle();
            manualPanel.SetActive(false);
            ResetManualVisuals();
            inputShield.SetActive(false);
            GuardInput();
        }

        private void OnDisable() => Cancel();
    }
}
