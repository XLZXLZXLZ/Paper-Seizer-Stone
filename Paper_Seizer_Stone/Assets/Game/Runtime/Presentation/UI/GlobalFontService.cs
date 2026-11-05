using System.Collections.Generic;
using PaperSeizerStone.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaperSeizerStone.Presentation
{
    // Event driven: no per-frame scene search and no edits to TMP's generated submeshes.
    public static class GlobalFontService
    {
        public const string ResourcePath = "UI/GlobalFonts";
        public static GlobalFontConfiguration Configuration => configuration != null ? configuration
            : configuration = Resources.Load<GlobalFontConfiguration>(ResourcePath);
        private static GlobalFontConfiguration configuration;
        private static readonly HashSet<TMP_Text> pending = new HashSet<TMP_Text>();
        private static List<TMP_FontAsset> originalFallbacks;
        private static bool refresh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Shutdown();
            configuration = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(TextChanged);
            SceneManager.sceneLoaded += SceneLoaded;
            Canvas.preWillRenderCanvases += Flush;
            Application.quitting += Shutdown;
            RequestRefresh();
        }

        public static void RequestRefresh() => refresh = true;
        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => RequestRefresh();
        private static void TextChanged(Object changed)
        {
            if (changed is TMP_Text text && PrepareTextRenderer(text) && Configuration != null)
            {
                TMP_FontAsset font = Configuration.Resolve(text);
                if (font != null && (text.font != font || text.fontSharedMaterial != font.material)) pending.Add(text);
            }
        }

        public static bool Apply(TMP_Text text, GlobalFontConfiguration settings = null)
        {
            if (!PrepareTextRenderer(text)) return false;
            settings = settings != null ? settings : Configuration;
            TMP_FontAsset font = settings != null ? settings.Resolve(text) : null;
            if (font == null || text == null) return false;
            if (text.font == font && text.fontSharedMaterial == font.material) return false;
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.SetAllDirty();
            return true;
        }

        public static bool PrepareTextRenderer(TMP_Text text)
        {
            if (text == null) return false;
            // Inactive 3D text may not have run Awake. Its public renderer getter populates
            // TMP's internal m_renderer before the font setter's LoadFontAsset uses it.
            if (text is TextMeshPro worldText) return worldText.renderer != null;
            return true;
        }

        private static void Flush()
        {
            if (refresh)
            {
                refresh = false;
                if (Configuration != null && Configuration.applyGlobally)
                {
                    if (originalFallbacks == null) originalFallbacks = new List<TMP_FontAsset>(TMP_Settings.fallbackFontAssets);
                    TMP_Settings.fallbackFontAssets.Clear();
                    foreach (TMP_FontAsset font in Configuration.fallbackFonts)
                        if (font != null && !TMP_Settings.fallbackFontAssets.Contains(font)) TMP_Settings.fallbackFontAssets.Add(font);
                }
                else if (originalFallbacks != null)
                {
                    TMP_Settings.fallbackFontAssets.Clear();
                    TMP_Settings.fallbackFontAssets.AddRange(originalFallbacks);
                    originalFallbacks = null;
                }
                foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                    if (text != null && text.gameObject.scene.IsValid() && text.gameObject.scene.isLoaded) pending.Add(text);
            }
            if (pending.Count == 0) return;
            var work = new List<TMP_Text>(pending);
            pending.Clear();
            foreach (TMP_Text text in work) if (text != null) Apply(text);
        }

        private static void Shutdown()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(TextChanged);
            SceneManager.sceneLoaded -= SceneLoaded;
            Canvas.preWillRenderCanvases -= Flush;
            Application.quitting -= Shutdown;
            if (originalFallbacks != null)
            {
                TMP_Settings.fallbackFontAssets.Clear();
                TMP_Settings.fallbackFontAssets.AddRange(originalFallbacks);
                originalFallbacks = null;
            }
            pending.Clear();
            refresh = false;
        }
    }
}
