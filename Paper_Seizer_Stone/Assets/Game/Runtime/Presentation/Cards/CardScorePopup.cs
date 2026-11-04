using TMPro;
using UnityEngine;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    [DefaultExecutionOrder(200)]
    public sealed class CardScorePopup : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        private Camera viewCamera;
        private CardFeedbackDefinition definition;
        private float started;

        public void Show(int points, Camera camera, CardFeedbackDefinition settings)
        {
            if (points == 0 || label == null || camera == null || settings == null) return;
            definition = settings;
            viewCamera = camera;
            started = Time.unscaledTime;
            label.text = points.ToString("+0;-0;0");
            label.color = points > 0 ? definition.scoreColor : definition.negativeScoreColor;
            label.fontSize = definition.popupFontSize;
            label.alpha = 0f;
            gameObject.SetActive(true);
            Draw(0f);
        }

        private void LateUpdate()
        {
            if (definition == null || viewCamera == null) { Hide(); return; }
            float t = (Time.unscaledTime - started) / Mathf.Max(0.1f, definition.popupSeconds);
            if (t >= 1f) { Hide(); return; }
            Draw(t);
        }

        private void Draw(float t)
        {
            Vector3 anchor = transform.parent != null
                ? transform.parent.TransformPoint(definition.popupOffset) : definition.popupOffset;
            transform.position = anchor + viewCamera.transform.up * (definition.popupRise * Mathf.SmoothStep(0, 1, t));
            transform.rotation = viewCamera.transform.rotation;
            float scale = t < 0.2f ? Mathf.Lerp(0.75f, 1.08f, t / 0.2f)
                : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((t - 0.2f) / 0.2f));
            transform.localScale = Vector3.one * scale;
            label.alpha = Mathf.Clamp01(t / 0.06f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1f, t)));
        }

        public void Hide() { gameObject.SetActive(false); }
        private void OnDisable() { if (label != null) label.alpha = 0f; }
    }
}
