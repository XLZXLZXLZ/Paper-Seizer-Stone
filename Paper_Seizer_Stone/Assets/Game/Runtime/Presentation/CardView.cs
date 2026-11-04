using UnityEngine;
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

        public void DisplayShop(CardDefinition definition, bool clickable)
        {
            Known = true;
            PreviewArtwork = definition.Artwork;
            PreviewDescription = definition.DisplayName + "\n" + definition.Description;
            transform.DOKill();
            visual.DOKill();
            visual.localRotation = visualRotation;
            visual.localScale = visualScale;
            CardId = 0;
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
            SetVisibility(true, definition.ShowText);
            highlighted = false;
            ApplyColors();
            SetHitbox(clickable);
        }

        private void ApplyArtwork(Texture texture, Color color, string label, string description)
        {
            EnsureProperties();
            faceColor = color;
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

        public void SetHighlight(bool value)
        {
            highlighted = value;
            if (faceProperties != null) ApplyColors();
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
            flipTween?.Kill();
            hasDisplay = false;
            privatePreviewEligible = false;
            Known = false;
            PreviewArtwork = null;
            PreviewDescription = "";
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
