using UnityEngine;
using System.Collections;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // Geometry and text are authored in the prefab; runtime changes display data only.
    [ExecuteAlways, RequireComponent(typeof(BoxCollider))]
    public sealed class CardView : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private Renderer face;
        [SerializeField] private GameObject ghost;
        [SerializeField] private TMP_Text faceText;
        [SerializeField] private TMP_Text ghostText;
        [SerializeField] private TMP_Text effectText;
        [SerializeField] private TMP_Text ghostEffectText;
        [SerializeField] private Renderer back;
        [SerializeField] private BoxCollider hitbox;
        [SerializeField] private CardFeedbackView feedback;
        [SerializeField] private Material playerBack;
        [SerializeField] private Material enemyBack;
        [SerializeField, Tooltip("关闭卡背分色时，双方共用此基础卡背材质。")]
        private Material baseBack;
        [Tooltip("图片已包含文字时不额外叠加说明。")]
        [SerializeField] private bool textureOnly;
        [Header("编辑器卡面预览")]
        [Tooltip("选择 Game/art/card 下的美术 SO。运行时由实际卡牌定义覆盖正面；此 SO 同时提供统一卡背。")]
        [SerializeField] private CardAppearanceDefinition previewAppearance;
        [SerializeField] private Side previewSide;
        [SerializeField] private bool previewFaceDown;
        [SerializeField] private CardTagCatalog tagCatalog;
        [SerializeField] private CardTag previewTag;
        private Vector4 tagData;
        private int visualSeed;
        private Color tagColor = Color.white;
        private Vector4 tagRect = new Vector4(0.07f, 0.19f, 0.93f, 0.78f);
        private string baseDescription;
        public string PreviewName { get; private set; }
        public string PreviewRulesDescription { get; private set; }
        public CardTag Tag { get; private set; }
        public string TagDescription { get; private set; }
        private MaterialPropertyBlock faceProperties;
        private MaterialPropertyBlock backProperties;
        private MaterialPropertyBlock ghostProperties;
        private Renderer ghostRenderer;
        private Color faceColor;
        private Color backColor;
        private Vector3 visualScale;
        private Quaternion visualRotation;
        [SerializeField, Range(0, 1), Tooltip("开启半透明预览时，己方已知暗牌的牌面透明度。")]
        private float privatePreviewOpacity = 0.35f;
        [SerializeField, Min(0.05f), Tooltip("落场盖牌与正式揭牌的翻面时长。")]
        private float flipDuration = 0.22f;
        private Tween flipTween;
        private bool hasDisplay;
        private bool displayedFaceUp;
        private bool displayedShowText;
        private bool privatePreviewEligible;
        private Side originalSide;
        private bool highlighted;
        public bool Known { get; private set; }
        public Texture PreviewArtwork { get; private set; }
        public string PreviewDescription { get; private set; }


        public int CardId { get; private set; }
        public bool Revealed { get; private set; }
        public Side Owner { get; private set; }
        public bool IsOnBoard { get; private set; }
        public bool IsPlacementAnimating => DOTween.IsTweening(transform) || (flipTween != null && flipTween.IsActive() && flipTween.IsPlaying());
        public bool CanDrag { get; private set; }

        private void OnEnable()
        {
            GameDisplaySettings.Changed += RefreshDisplaySettings;
            RefreshEditorPreview();
            if (Application.IsPlaying(gameObject)) RefreshDisplaySettings();
        }

        private void OnDisable() => GameDisplaySettings.Changed -= RefreshDisplaySettings;

        private void RefreshDisplaySettings()
        {
            if (!Application.IsPlaying(gameObject)) { RefreshEditorPreview(); return; }
            if (!hasDisplay) return;
            ApplyBack(originalSide);
            ApplyColors();
            // Preserve the current side during an in-progress flip.
            SetVisibility(face.gameObject.activeSelf, displayedShowText);
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject)) RefreshEditorPreview();
        }

        public void RefreshEditorPreview()
        {
            if (Application.IsPlaying(gameObject) || previewAppearance == null || face == null || back == null) return;
            highlighted = false;
            ApplyArtwork(previewAppearance.FrontArtwork, previewAppearance.FaceTint,
                previewAppearance.DisplayName, previewAppearance.Description);
            SetTag(previewTag, previewAppearance);
            ApplyBack(previewSide);
            SetVisibility(!previewFaceDown, previewAppearance.ShowText);
            ApplyColors();
        }

        private void EnsureProperties()
        {
            if (faceProperties == null) faceProperties = new MaterialPropertyBlock();
            if (backProperties == null) backProperties = new MaterialPropertyBlock();
            if (ghostProperties == null) ghostProperties = new MaterialPropertyBlock();
            if (ghostRenderer == null && ghost != null) ghostRenderer = ghost.GetComponent<Renderer>();
        }

        public void Initialize()
        {
            if (hitbox == null) hitbox = GetComponent<BoxCollider>();
            if (visual == null || face == null || ghost == null || faceText == null || ghostText == null
                || back == null || hitbox == null || playerBack == null || enemyBack == null)
                throw new System.InvalidOperationException("请完整配置卡牌预制体的外观、文字、碰撞体与双方卡背材质。");
            EnsureProperties();
            visualScale = visual.localScale;
            visualRotation = visual.localRotation;
        }

        public void Display(ObservedCard card, CardDefinition definition, Side owner, bool onBoard, bool draggable)
        {
            Known = card.Gesture.HasValue && definition != null;
            PreviewArtwork = Known ? definition.Artwork : null;
            PreviewDescription = Known ? definition.DisplayName + "\n" + definition.Description : "";
            CardId = card.Id;
            visualSeed = card.AppearanceSeed;
            Owner = owner;
            IsOnBoard = onBoard;
            CanDrag = draggable;
            Revealed = card.IsRevealed;
            privatePreviewEligible = Known && onBoard && owner == Side.Player && !card.IsRevealed;
            ApplyBack(card.OriginalSide);
            ApplyArtwork(definition == null ? null : definition.Artwork,
                definition == null ? Color.white : definition.FaceColor,
                definition == null ? "" : definition.DisplayName,
                definition == null ? "" : definition.Description);
            SetTag(Known ? card.Tag : CardTag.None, definition == null ? null : definition.Appearance);
            ApplyColors();
            bool known = card.Gesture.HasValue;
            bool showText = definition == null || definition.ShowText;
            displayedShowText = showText;
            bool faceUp = known && (card.IsRevealed || (owner == Side.Player && !onBoard));
            // The optional overlay uses only the player's already-known card information.
            // Formal reveal already has its own tilt animation in TabletopView.
            if (card.IsRevealed && flipTween != null && flipTween.IsActive())
            {
                flipTween.Kill();
                visual.localScale = visualScale;
            }
            if (hasDisplay && displayedFaceUp != faceUp && !card.IsRevealed)
            {
                flipTween?.Kill();
                visual.localScale = visualScale;
                flipTween = DOTween.Sequence()
                    .Append(visual.DOScaleX(0.001f, flipDuration * 0.5f))
                    .AppendCallback(() => SetVisibility(faceUp, showText))
                    .Append(visual.DOScaleX(visualScale.x, flipDuration * 0.5f));
            }
            else if (flipTween == null || !flipTween.IsActive()) SetVisibility(faceUp, showText);
            displayedFaceUp = faceUp;
            hasDisplay = true;
        }

        public void SetHitbox(bool enabled) { if (hitbox != null) hitbox.enabled = enabled; }

        public void DisplayShop(CardDefinition definition, bool clickable, CardTag tag = CardTag.None, string recordId = null)
        {
            if (feedback != null) feedback.ClearImmediately();
            Known = true;
            PreviewArtwork = definition.Artwork;
            PreviewDescription = definition.DisplayName + "\n" + definition.Description;
            transform.DOKill();
            visual.DOKill();
            visual.localRotation = visualRotation;
            visual.localScale = visualScale;
            CardId = 0;
            visualSeed = CardTags.VisualSeed(recordId);
            Owner = Side.Player;
            IsOnBoard = false;
            CanDrag = false;
            Revealed = true;
            privatePreviewEligible = false;
            displayedFaceUp = true;
            displayedShowText = definition.ShowText;
            hasDisplay = true;
            ApplyBack(Side.Player);
            ApplyArtwork(definition.Artwork, definition.FaceColor, definition.DisplayName, definition.Description);
            SetTag(tag, definition.Appearance);
            SetVisibility(true, definition.ShowText);
            highlighted = false;
            ApplyColors();
            SetHitbox(clickable);
        }

        private void ApplyArtwork(Texture texture, Color color, string label, string description)
        {
            EnsureProperties();
            faceColor = color;
            PreviewName = label;
            PreviewRulesDescription = description;
            baseDescription = label + "\n" + description;
            // Clear the previous pooled card's artwork, including art-less transformations.
            bool hasArtwork = texture != null;
            if (!hasArtwork) texture = Texture2D.whiteTexture;
            faceProperties.SetTexture("_BaseMap", texture);
            faceProperties.SetTexture("_MainTex", texture);
            faceProperties.SetFloat("_ArtworkStrength", hasArtwork ? 1f : 0f);
            ghostProperties.SetTexture("_BaseMap", texture);
            ghostProperties.SetTexture("_MainTex", texture);
            if (faceText != null) faceText.text = label;
            if (ghostText != null) ghostText.text = label;
            if (effectText != null) effectText.text = description;
            if (ghostEffectText != null) ghostEffectText.text = description;
        }

        private void ApplyBack(Side originalSide)
        {
            EnsureProperties();
            this.originalSide = originalSide;
            ResolveBackStyle(originalSide, out Material material, out Texture texture, out backColor);
            if (material != null && back.sharedMaterial != material) back.sharedMaterial = material;
            backProperties.SetTexture("_BaseMap", texture);
            backProperties.SetTexture("_MainTex", texture);
        }

        // The deck piles use the same authored back style as individual cards.
        public void ResolveBackStyle(Side side, out Material material, out Texture texture, out Color color)
        {
            bool distinct = GameDisplaySettings.DistinctCardBacks;
            material = distinct ? (side == Side.Player ? playerBack : enemyBack)
                : baseBack != null ? baseBack : playerBack;
            // The back style never depends on the hidden card's definition.
            texture = previewAppearance != null ? previewAppearance.BackArtwork : null;
            if (texture == null && material != null) texture = material.mainTexture;
            if (texture == null) texture = Texture2D.whiteTexture;
            color = distinct && previewAppearance != null ? previewAppearance.BackTint(side == Side.Player)
                : material != null ? material.color : Color.white;
        }

        private void SetVisibility(bool faceUp, bool showText)
        {
            face.gameObject.SetActive(faceUp);
            if (faceText != null) faceText.gameObject.SetActive(faceUp && showText);
            if (effectText != null) effectText.gameObject.SetActive(faceUp && showText);
            bool privatePreview = GameDisplaySettings.TransparentPlayerCards && privatePreviewEligible && !faceUp;
            if (ghost != null) ghost.SetActive(privatePreview);
            if (ghostText != null) ghostText.gameObject.SetActive(privatePreview && showText);
            if (ghostEffectText != null) ghostEffectText.gameObject.SetActive(privatePreview && showText);
        }

        private void ApplyColors()
        {
            Color tint = new Color(0.45f, 0.9f, 0.55f);
            Color f = highlighted ? Color.Lerp(faceColor, tint, 0.5f) : faceColor;
            Color b = highlighted ? Color.Lerp(backColor, tint, 0.5f) : backColor;
            faceProperties.SetColor("_BaseColor", f);
            faceProperties.SetColor("_Color", f);
            backProperties.SetColor("_BaseColor", b);
            backProperties.SetColor("_Color", b);
            faceProperties.SetVector("_TagData", tagData);
            faceProperties.SetColor("_TagColor", tagColor);
            faceProperties.SetVector("_TagArtworkRect", tagRect);
            ghostProperties.SetVector("_TagData", tagData);
            ghostProperties.SetColor("_TagColor", tagColor);
            ghostProperties.SetVector("_TagArtworkRect", tagRect);
            ghostProperties.SetFloat("_ArtworkStrength", PreviewArtwork != null ? 1f : 0f);
            face.SetPropertyBlock(faceProperties);
            back.SetPropertyBlock(backProperties);
            Color g = faceColor;
            g.a = privatePreviewOpacity;
            ghostProperties.SetColor("_BaseColor", g);
            ghostProperties.SetColor("_Color", g);
            if (ghostRenderer != null) ghostRenderer.SetPropertyBlock(ghostProperties);
            if (ghostText != null) ghostText.alpha = privatePreviewOpacity;
            if (ghostEffectText != null) ghostEffectText.alpha = privatePreviewOpacity;
        }

        public void SetTag(CardTag tag, CardAppearanceDefinition appearance = null)
        {
            EnsureProperties();
            Tag = tag;
            if (appearance != null) tagRect = appearance.EngravingRect;
            CardTagAppearance entry = tagCatalog != null ? tagCatalog.Find(tag) : null;
            tagData = entry == null ? Vector4.zero : new Vector4((int)tag, entry.strength, (visualSeed % 997) * 0.0137f, entry.patternScale);
            tagColor = entry == null ? Color.white : entry.color;
            TagDescription = entry == null ? "" : entry.displayName + "刻印：" + entry.description;
            PreviewDescription = baseDescription + (string.IsNullOrEmpty(TagDescription) ? "" : "\n\n" + TagDescription);
            ApplyColors();
        }

        public void CopyTagTo(Material material)
        {
            material.SetVector("_TagData", tagData);
            material.SetColor("_TagColor", tagColor);
            material.SetVector("_TagArtworkRect", tagRect);
        }

        // Only a visual rotation; never changes public/reveal state in the rules.
        public IEnumerator Engrave(CardTag tag, float seconds)
        {
            bool applied = false;
            SetHighlight(false);
            if (feedback != null) feedback.ClearImmediately();
            yield return DOTween.To(() => 0f, angle =>
            {
                visual.localRotation = visualRotation * Quaternion.AngleAxis(angle, Vector3.forward);
                SetVisibility(angle < 90f || angle > 270f, displayedShowText);
                if (!applied && angle >= 180f) { applied = true; SetTag(tag); }
            }, 360f, seconds).SetTarget(visual).SetEase(Ease.InOutSine).WaitForCompletion();
            visual.localRotation = visualRotation;
            if (!applied) SetTag(tag);
            SetVisibility(true, displayedShowText);
        }

        public void SetHighlight(bool value)
        {
            highlighted = value;
            if (faceProperties != null) ApplyColors();
        }

        public void SetOutcomeHint(int result) { if (feedback != null) feedback.SetOutcome(result, CardId); }
        public void SuppressFeedback(bool value) { if (feedback != null) feedback.SetSuppressed(value); }
        public void FlashAbility() { if (feedback != null) feedback.FlashAbility(); }
        public void ClearFeedback() { if (feedback != null) feedback.ClearImmediately(); }

        public void PresentScore(int points, Camera camera)
        {
            if (points == 0) return;
            visual.DOKill();
            visual.localScale = visualScale;
            float scale = feedback != null ? feedback.ScoreScale : 1.09f;
            float rise = feedback != null ? feedback.RiseSeconds : 0.12f;
            float fall = feedback != null ? feedback.ReturnSeconds : 0.2f;
            DOTween.Sequence()
                .SetTarget(visual)
                .Append(visual.DOScale(visualScale * scale, rise).SetEase(Ease.OutCubic))
                .Append(visual.DOScale(visualScale, fall).SetEase(Ease.InOutSine));
            if (feedback != null) feedback.ShowScore(points, camera);
        }

        public void MoveTo(Vector3 position, Quaternion rotation, float duration)
        {
            transform.DOKill();
            transform.DOMove(position, duration).SetEase(Ease.OutCubic);
            transform.DORotateQuaternion(rotation, duration).SetEase(Ease.OutCubic);
        }

        public void Tilt(float angle, float duration)
        {
            visual.DOKill();
            visual.DOLocalRotateQuaternion(visualRotation * Quaternion.Euler(0, 0, angle), duration).SetEase(Ease.InOutSine);
        }

        public void Pulse()
        {
            visual.DOKill();
            visual.localScale = visualScale;
            visual.DOPunchScale(visualScale * 0.12f, 0.35f, 1, 0.3f);
        }

        public void ResetForPool()
        {
            if (feedback != null) feedback.ClearImmediately();
            flipTween?.Kill();
            hasDisplay = false;
            privatePreviewEligible = false;
            Known = false;
            PreviewArtwork = null;
            PreviewDescription = "";
            baseDescription = "";
            PreviewName = "";
            PreviewRulesDescription = "";
            SetTag(CardTag.None);
            transform.DOKill();
            visual.DOKill();
            visual.localRotation = visualRotation;
            visual.localScale = visualScale;
            CanDrag = false;
            SetHighlight(false);
            SetHitbox(true);
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (!Application.IsPlaying(gameObject)) return;
            flipTween?.Kill();
            transform.DOKill();
            if (visual != null) visual.DOKill();
        }
    }
}
