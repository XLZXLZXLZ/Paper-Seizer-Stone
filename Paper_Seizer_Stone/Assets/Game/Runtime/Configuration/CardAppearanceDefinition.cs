using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    // Art-only data can also describe cards which are not enabled in the rules yet.
    public sealed class CardAppearanceDefinition : ScriptableObject
    {
        [Header("卡面信息（以素材为准）")]
        [SerializeField] private string displayName;
        [SerializeField, TextArea(2, 6)] private string description;
        [SerializeField] private Texture2D frontArtwork;
        [Tooltip("素材已经包含名称和说明时关闭额外 TMP 文字。缺少正面图片时自动显示文字。")]
        [SerializeField] private bool artworkIncludesText = true;
        [SerializeField] private Color faceTint = Color.white;

        [Header("卡背（同一牌组请使用统一配置）")]
        [SerializeField] private Texture2D backArtwork;
        [SerializeField] private Color playerBackTint = new Color(0.5f, 0.72f, 1f);
        [SerializeField] private Color enemyBackTint = new Color(1f, 0.55f, 0.42f);

        public string DisplayName => displayName ?? string.Empty;
        public string Description => description ?? string.Empty;
        public Texture2D FrontArtwork => frontArtwork;
        public Texture2D BackArtwork => backArtwork;
        public Color FaceTint => faceTint;
        public bool ShowText => frontArtwork == null || !artworkIncludesText;
        public Color BackTint(bool player) => player ? playerBackTint : enemyBackTint;
    }
}
