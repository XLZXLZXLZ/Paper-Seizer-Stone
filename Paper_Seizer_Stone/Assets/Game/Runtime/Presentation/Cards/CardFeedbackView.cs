using UnityEngine;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // The border is saved under the card's visual and follows its tilt/score animation.
    public sealed class CardFeedbackView : MonoBehaviour
    {
        [SerializeField] private CardFeedbackDefinition definition;
        [SerializeField] private Renderer border;
        [SerializeField] private CardScorePopup scorePopup;
        private MaterialPropertyBlock properties;
        private Color currentColor;
        private Color targetColor;
        private float flashStart = float.NegativeInfinity;
        private int identity;
        private bool suppressed;

        public float ScoreScale => definition != null ? definition.scoreScale : 1.09f;
        public float RiseSeconds => definition != null ? Mathf.Max(0.01f, definition.scoreRiseSeconds) : 0.12f;
        public float ReturnSeconds => definition != null ? Mathf.Max(0.01f, definition.scoreReturnSeconds) : 0.2f;

        public void SetOutcome(int result, int cardId)
        {
            if (identity != cardId)
            {
                ClearImmediately();
                identity = cardId;
            }
            targetColor = definition == null || result == 0 ? Color.clear
                : result > 0 ? definition.winColor : definition.lossColor;
        }

        public void SetSuppressed(bool value)
        {
            suppressed = value;
            if (value && border != null) border.enabled = false;
        }

        public void FlashAbility() { flashStart = Time.unscaledTime; }
        public void ShowScore(int points, Camera camera)
        {
            if (points != 0 && scorePopup != null && definition != null)
                scorePopup.Show(points, camera, definition);
        }

        private void LateUpdate()
        {
            if (border == null || definition == null || suppressed) return;
            float blend = Mathf.Clamp01(Time.unscaledDeltaTime / Mathf.Max(0.01f, definition.colorTransitionSeconds));
            currentColor = Color.Lerp(currentColor, targetColor, blend);
            float flash = 1f - Mathf.Clamp01((Time.unscaledTime - flashStart) / Mathf.Max(0.05f, definition.abilityFlashSeconds));
            if (currentColor.a < 0.002f && targetColor.a <= 0f && flash <= 0f)
            {
                currentColor = Color.clear;
                border.enabled = false;
                return;
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetColor("_BorderColor", Color.Lerp(currentColor, definition.abilityColor, flash));
            properties.SetVector("_EdgeData", new Vector4(definition.borderWidth, definition.noiseAmplitude,
                definition.noiseFrequency, definition.noiseDrift));
            properties.SetFloat("_BorderSeed", (identity * 0.618034f) % 101f);
            border.SetPropertyBlock(properties);
            border.enabled = true;
        }

        public void ClearImmediately()
        {
            currentColor = targetColor = Color.clear;
            flashStart = float.NegativeInfinity;
            suppressed = false;
            if (border != null) border.enabled = false;
            if (scorePopup != null) scorePopup.Hide();
        }

        private void OnDisable() { ClearImmediately(); }
    }
}
