using UnityEngine;

namespace PaperSeizerStone.Presentation.PostProcessing
{
    public sealed class StyleFilterSettings : ScriptableObject
    {
        public bool filterEnabled = true;
        public StyleFilterPreset selectedPreset;
        public StyleFilterParameters parameters = new StyleFilterParameters();
        // Editor preview state must never change the shipped configuration.
        public static bool PreviewEnabled = true;
        public static bool CompareOriginal;
        public static bool PreviewSceneView;
    }
}
