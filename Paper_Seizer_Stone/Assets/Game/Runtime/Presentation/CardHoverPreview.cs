using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PaperSeizerStone.Presentation
{
    public sealed class CardHoverPreview : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private RawImage artwork;
        [SerializeField] private TMP_Text fallback;
        [UnityEngine.Serialization.FormerlySerializedAs("tagLabel")]
        [SerializeField] private TMP_Text descriptionLabel;
        [SerializeField] private Material engravingMaterial;
        private Material previewMaterial;
        private CardView describedCard;
        private string describedRules;
        private string describedTag;
        private float footerHeight;
        [SerializeField, Min(0)] private float descriptionPadding = 16;

        private void Awake()
        {
            if (engravingMaterial != null)
            {
                previewMaterial = new Material(engravingMaterial);
                artwork.material = previewMaterial;
            }
        }

        private void OnDestroy() { if (previewMaterial != null) Destroy(previewMaterial); }
        [SerializeField] private Vector2 size = new Vector2(220, 330);
        [SerializeField] private Vector2 cursorOffset = new Vector2(24, 20);
        [SerializeField, Min(0)] private float screenMargin = 12;

        public void Hide()
        {
            if (panel != null) panel.gameObject.SetActive(false);
        }

        private void Describe(CardView card)
        {
            if (descriptionLabel == null) { footerHeight = 0; return; }
            if (describedCard == card && describedRules == card.PreviewRulesDescription
                && describedTag == card.TagDescription) return;
            describedCard = card;
            describedRules = card.PreviewRulesDescription;
            describedTag = card.TagDescription;
            string text = describedRules ?? "";
            if (!string.IsNullOrEmpty(describedTag))
                text += (string.IsNullOrEmpty(text) ? "" : "\n") + "<size=80%>" + describedTag + "</size>";
            descriptionLabel.text = text;
            descriptionLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
            footerHeight = string.IsNullOrEmpty(text) ? 0 : descriptionPadding
                + descriptionLabel.GetPreferredValues(text, Mathf.Max(20, size.x - descriptionPadding), Mathf.Infinity).y;
        }

        public void Show(CardView card, Vector2 cursor)
        {
            if (card == null || !card.Known) { Hide(); return; }
            var canvas = GetComponent<Canvas>();
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            Describe(card);
            float footer = footerHeight;
            Vector2 desiredSize = new Vector2(size.x, size.y + footer);
            Vector2 pixels = desiredSize * scale;
            float fit = Mathf.Min(1, Mathf.Min((Screen.width - 2 * screenMargin) / pixels.x,
                (Screen.height - 2 * screenMargin) / pixels.y));
            fit = Mathf.Max(0.01f, fit);
            pixels *= fit;
            panel.sizeDelta = desiredSize;
            panel.localScale = Vector3.one * fit;
            float x = cursor.x + cursorOffset.x;
            float y = cursor.y + cursorOffset.y;
            if (x + pixels.x > Screen.width - screenMargin) x = cursor.x - cursorOffset.x - pixels.x;
            if (y + pixels.y > Screen.height - screenMargin) y = cursor.y - cursorOffset.y - pixels.y;
            panel.position = new Vector3(Mathf.Clamp(x, screenMargin, Mathf.Max(screenMargin, Screen.width - pixels.x - screenMargin)),
                Mathf.Clamp(y, screenMargin, Mathf.Max(screenMargin, Screen.height - pixels.y - screenMargin)), 0);
            artwork.rectTransform.anchorMin = new Vector2(0, footer / (size.y + footer));
            artwork.rectTransform.offsetMin = Vector2.zero;
            fallback.rectTransform.anchorMin = artwork.rectTransform.anchorMin;
            fallback.rectTransform.offsetMin = Vector2.zero;
            artwork.texture = card.PreviewArtwork;
            if (previewMaterial != null) card.CopyTagTo(previewMaterial);
            if (descriptionLabel != null)
            {
                descriptionLabel.rectTransform.anchorMax = new Vector2(1, footer / (size.y + footer));
            }
            artwork.gameObject.SetActive(card.PreviewArtwork != null);
            fallback.text = card.PreviewName;
            fallback.gameObject.SetActive(card.PreviewArtwork == null);
            panel.gameObject.SetActive(true);
        }
    }
}
