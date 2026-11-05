using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // Page layouts and illustrations are saved UI objects; only their content changes here.
    [DisallowMultipleComponent]
    public sealed class TutorialManualView : MonoBehaviour
    {
        [Serializable]
        public sealed class PageLayout
        {
            public ManualPage page;
            public GameObject root;
            public TMP_Text[] paragraphs = Array.Empty<TMP_Text>();
        }

        [SerializeField] private PageLayout[] pages = Array.Empty<PageLayout>();
        [SerializeField] private TMP_Text[] tabLabels = Array.Empty<TMP_Text>();
        [SerializeField] private Graphic[] tabUnderlines = Array.Empty<Graphic>();

        private static readonly Color ActiveTabColor = new Color(0.79f, 0.65f, 0.4f);
        private static readonly Color InactiveTabColor = new Color(0.86f, 0.84f, 0.77f);
        // Match existing tags as complete units so their attributes are never recolored.
        private static readonly Regex Emphasis = new Regex(@"<[^>]*>|\d+|中弹两次");

        public void Display(ManualPage page, string body)
        {
            string[] lines = (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (PageLayout layout in pages)
            {
                if (layout == null) continue;
                bool selected = layout.page == page;
                if (layout.root != null) layout.root.SetActive(selected);
                if (!selected || layout.paragraphs == null) continue;
                for (int i = 0; i < layout.paragraphs.Length; i++)
                {
                    TMP_Text paragraph = layout.paragraphs[i];
                    if (paragraph == null) continue;
                    string text = i < lines.Length ? lines[i] : string.Empty;
                    // Retain new authoring lines if a page gains more text than its saved slots.
                    if (i == layout.paragraphs.Length - 1 && lines.Length > layout.paragraphs.Length)
                        text = string.Join("\n", lines, i, lines.Length - i);
                    paragraph.text = Emphasis.Replace(text, Highlight);
                }
            }

            for (int i = 0; i < tabLabels.Length; i++)
                if (tabLabels[i] != null)
                    tabLabels[i].color = i == (int)page ? ActiveTabColor : InactiveTabColor;
            for (int i = 0; i < tabUnderlines.Length; i++)
                if (tabUnderlines[i] != null)
                    tabUnderlines[i].gameObject.SetActive(i == (int)page);
        }

        private static string Highlight(Match match)
        {
            if (match.Value[0] == '<') return match.Value;
            string color = match.Value == "中弹两次" ? "#CC7460" : "#C9A666";
            return "<color=" + color + ">" + match.Value + "</color>";
        }
    }
}
