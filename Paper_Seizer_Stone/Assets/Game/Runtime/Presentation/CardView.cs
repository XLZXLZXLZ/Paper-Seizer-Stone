using UnityEngine;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // Geometry and text are authored in the prefab; runtime changes display data only.
    [RequireComponent(typeof(BoxCollider))]
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
        private MaterialPropertyBlock faceProperties;
        private MaterialPropertyBlock backProperties;
        private Color faceColor;
        private Color backColor;
        private Vector3 visualScale;
        private Quaternion visualRotation;
        private bool highlighted;

        public int CardId { get; private set; }
        public bool Revealed { get; private set; }
        public Side Owner { get; private set; }
        public bool IsOnBoard { get; private set; }
        public bool CanDrag { get; private set; }

        public void Initialize()
        {
            if (hitbox == null) hitbox = GetComponent<BoxCollider>();
            if (visual == null || face == null || ghost == null || faceText == null || ghostText == null
                || back == null || hitbox == null || playerBack == null || enemyBack == null)
                throw new System.InvalidOperationException("请完整配置卡牌预制体的外观、文字、碰撞体与双方卡背材质。");
            faceProperties = new MaterialPropertyBlock();
            backProperties = new MaterialPropertyBlock();
            visualScale = visual.localScale;
            visualRotation = visual.localRotation;
        }

        public void Display(ObservedCard card, CardDefinition definition, Side owner, bool onBoard, bool draggable)
        {
            CardId = card.Id;
            Owner = owner;
            IsOnBoard = onBoard;
            CanDrag = draggable;
            Revealed = card.IsRevealed;
            back.sharedMaterial = card.OriginalSide == Side.Player ? playerBack : enemyBack;
            backColor = back.sharedMaterial.color;
            faceColor = definition != null ? definition.FaceColor : face.sharedMaterial.color;
            Texture texture = definition != null ? definition.Artwork : null;
            if (texture == null) texture = face.sharedMaterial.mainTexture;
            if (texture == null) texture = Texture2D.whiteTexture;
            faceProperties.SetTexture("_BaseMap", texture);
            faceProperties.SetTexture("_MainTex", texture);
            ApplyColors();
            string label = definition == null ? "" : definition.DisplayName;
            faceText.text = label;
            ghostText.text = label;
            if (effectText != null) effectText.text = definition == null ? "" : definition.Description;
            if (ghostEffectText != null) ghostEffectText.text = definition == null ? "" : definition.Description;
            bool known = card.Gesture.HasValue;
            bool faceUp = known && (card.IsRevealed || (!onBoard && owner == Side.Player));
            face.gameObject.SetActive(faceUp);
            faceText.gameObject.SetActive(faceUp);
            if (effectText != null) effectText.gameObject.SetActive(faceUp);
            bool privatePreview = known && onBoard && owner == Side.Player && !card.IsRevealed;
            ghost.SetActive(privatePreview);
            ghostText.gameObject.SetActive(privatePreview);
            if (ghostEffectText != null) ghostEffectText.gameObject.SetActive(privatePreview);
        }

        public void SetHitbox(bool enabled) { if (hitbox != null) hitbox.enabled = enabled; }

        public void DisplayShop(CardDefinition definition, bool clickable)
        {
            transform.DOKill();
            visual.DOKill();
            visual.localRotation = visualRotation;
            visual.localScale = visualScale;
            CardId = 0;
            Owner = Side.Player;
            IsOnBoard = false;
            CanDrag = false;
            Revealed = true;
            back.sharedMaterial = playerBack;
            backColor = playerBack.color;
            faceColor = definition.FaceColor;
            Texture texture = definition.Artwork == null ? Texture2D.whiteTexture : definition.Artwork;
            faceProperties.SetTexture("_BaseMap", texture);
            faceProperties.SetTexture("_MainTex", texture);
            faceText.text = definition.DisplayName;
            if (effectText != null) { effectText.text = definition.Description; effectText.gameObject.SetActive(true); }
            face.gameObject.SetActive(true);
            faceText.gameObject.SetActive(true);
            ghost.SetActive(false);
            ghostText.gameObject.SetActive(false);
            if (ghostEffectText != null) ghostEffectText.gameObject.SetActive(false);
            highlighted = false;
            ApplyColors();
            SetHitbox(clickable);
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
            transform.DOKill();
            if (visual != null) visual.DOKill();
        }
    }
}
