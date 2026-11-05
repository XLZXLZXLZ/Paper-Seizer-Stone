using System.Collections.Generic;
using TMPro;
using UnityEngine;
using PaperSeizerStone.Presentation;

namespace PaperSeizerStone.Configuration
{
    public sealed class GlobalFontConfiguration : ScriptableObject
    {
        [Tooltip("关闭后不统一覆盖文字组件的字体。")]
        public bool applyGlobally = true;
        public TMP_FontAsset defaultFont;
        [Header("分类覆盖（留空时使用默认字体）")]
        public TMP_FontAsset uiFont;
        public TMP_FontAsset cardFont;
        public TMP_FontAsset scoreFont;
        [Tooltip("统一补字列表；不替代各字体自身的备用字库。")]
        public List<TMP_FontAsset> fallbackFonts = new List<TMP_FontAsset>();

        public TMP_FontAsset Resolve(TMP_Text text)
        {
            if (!applyGlobally || text == null) return null;
            if (text.GetComponentInParent<CardView>(true) != null) return cardFont != null ? cardFont : defaultFont;
            if (text.GetComponentInParent<ScoreCounterView>(true) != null) return scoreFont != null ? scoreFont : defaultFont;
            return uiFont != null ? uiFont : defaultFont;
        }

        private void OnValidate()
        {
            if (Application.isPlaying) GlobalFontService.RequestRefresh();
        }
    }
}
