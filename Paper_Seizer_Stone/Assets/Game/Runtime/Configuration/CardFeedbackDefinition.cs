using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public sealed class CardFeedbackDefinition : ScriptableObject
    {
        [Header("对位边框")]
        public Color winColor = new Color(0.5f, 0.78f, 0.59f, 0.9f);
        public Color lossColor = new Color(0.53f, 0.12f, 0.14f, 0.95f);
        public Color abilityColor = new Color(1f, 0.8f, 0.4f, 0.95f);
        [Min(0.01f)] public float colorTransitionSeconds = 0.15f;
        [Min(0.001f)] public float borderWidth = 0.024f;
        [Range(0, 0.01f)] public float noiseAmplitude = 0.003f;
        [Min(1)] public float noiseFrequency = 55f;
        [Range(0, 0.5f)] public float noiseDrift = 0.08f;
        [Min(0.05f)] public float abilityFlashSeconds = 0.28f;

        [Header("非零得分放大与回落")]
        [Range(1, 1.3f)] public float scoreScale = 1.09f;
        [Min(0.01f)] public float scoreRiseSeconds = 0.12f;
        [Min(0.01f)] public float scoreReturnSeconds = 0.2f;

        [Header("加分数字")]
        public Color scoreColor = new Color(1f, 0.9f, 0.65f, 1f);
        public Color negativeScoreColor = new Color(0.76f, 0.3f, 0.23f, 1f);
        [Min(0.1f)] public float popupSeconds = 0.6f;
        [Min(0)] public float popupRise = 0.22f;
        [Min(0.1f)] public float popupFontSize = 4f;
        public Vector3 popupOffset = new Vector3(0, 0.12f, 0);
    }
}
