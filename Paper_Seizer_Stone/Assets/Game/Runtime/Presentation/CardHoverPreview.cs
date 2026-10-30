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
        [SerializeField] private Vector2 size = new Vector2(220, 330);
        [SerializeField] private Vector2 cursorOffset = new Vector2(24, 20);
        [SerializeField, Min(0)] private float screenMargin = 12;

        public void Hide() => panel.gameObject.SetActive(false);

        public void Show(CardView card, Vector2 cursor)
        {
            if (card == null || !card.Known) { Hide(); return; }
            var canvas = GetComponent<Canvas>();
            float scale = Mathf.Max(0.01f, canvas.scaleFactor);
            Vector2 pixels = size * scale;
            float fit = Mathf.Min(1, Mathf.Min((Screen.width - 2 * screenMargin) / pixels.x,
                (Screen.height - 2 * screenMargin) / pixels.y));
            pixels *= Mathf.Max(0.01f, fit);
            panel.sizeDelta = pixels / scale;
            float x = cursor.x + cursorOffset.x;
            float y = cursor.y + cursorOffset.y;
            if (x + pixels.x > Screen.width - screenMargin) x = cursor.x - cursorOffset.x - pixels.x;
            if (y + pixels.y > Screen.height - screenMargin) y = cursor.y - cursorOffset.y - pixels.y;
            panel.position = new Vector3(Mathf.Clamp(x, screenMargin, Mathf.Max(screenMargin, Screen.width - pixels.x - screenMargin)),
                Mathf.Clamp(y, screenMargin, Mathf.Max(screenMargin, Screen.height - pixels.y - screenMargin)), 0);
            artwork.texture = card.PreviewArtwork;
            artwork.gameObject.SetActive(card.PreviewArtwork != null);
            fallback.text = card.PreviewDescription;
            fallback.gameObject.SetActive(card.PreviewArtwork == null);
            panel.gameObject.SetActive(true);
        }
    }
}
