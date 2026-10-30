using System;
using UnityEngine;

namespace PaperSeizerStone.Presentation.PostProcessing
{
    [Serializable]
    public sealed class StyleFilterParameters
    {
        [Range(0, 1)] public float strength = 1;
        [Range(0, 1)] public float posterize = 0.35f;
        [Range(3, 32)] public int levels = 12;
        [Range(0, 1)] public float dither = 0.15f;
        [Range(1, 4)] public float ditherSize = 1;
        [Range(0, 1)] public float halftone;
        [Range(3, 16)] public float dotSize = 5;
        [Range(0, 90)] public float dotAngle = 35;
        public Color inkColor = new Color(0.16f, 0.18f, 0.2f, 1);
        [Range(0, 1)] public float oil;
        [Range(1, 4)] public int oilRadius = 2;
        [Range(0, 0.25f)] public float paper = 0.025f;
        [Range(1, 6)] public float paperScale = 2;
        [Range(-2, 2)] public float exposure;
        [Range(-50, 50)] public float contrast = 5;
        [Range(-100, 100)] public float saturation = -6;
        [Range(-100, 100)] public float temperature = 4;
        public Color shadows = new Color(0.88f, 0.94f, 1, 1);
        public Color highlights = new Color(1, 0.97f, 0.90f, 1);
        [Range(0, 1)] public float splitTone = 0.12f;
        [Range(0, 1)] public float vignette = 0.13f;
        [Range(0.05f, 1)] public float vignetteSoftness = 0.6f;
        public Color vignetteColor = new Color(0.035f, 0.04f, 0.055f, 1);
        public Vector2 vignetteCenter = new Vector2(0.5f, 0.5f);

        public StyleFilterParameters Copy() => JsonUtility.FromJson<StyleFilterParameters>(JsonUtility.ToJson(this));
    }

    public sealed class StyleFilterPreset : ScriptableObject
    {
        [TextArea] public string description;
        public StyleFilterParameters parameters = new StyleFilterParameters();
    }
}
