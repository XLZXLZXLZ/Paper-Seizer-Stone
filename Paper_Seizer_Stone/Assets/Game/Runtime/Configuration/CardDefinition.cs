using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    // Read-only authoring data. Runtime never writes to a ScriptableObject.
    public sealed class CardDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId;
        [SerializeField] private string displayName;
        [SerializeField] private Gesture gesture;
        [SerializeField] private CardAbility ability;
        [SerializeField, TextArea(2, 4)] private string description;
        [SerializeField] private CardDefinition transformation;
        [Tooltip("正式卡面配置，名称、说明和图片优先读取该 SO。美术资源存放在 Game/art/card。")]
        [SerializeField] private CardAppearanceDefinition appearance;
        [SerializeField] private Texture2D artwork;
        [SerializeField] private Color faceColor = new Color(0.84f, 0.77f, 0.6f);

        public string Id => definitionId;
        public string DisplayName => appearance != null ? appearance.DisplayName : displayName;
        public Gesture Gesture => gesture;
        public CardAbility Ability => ability;
        public string Description => appearance != null ? appearance.Description : description ?? string.Empty;
        public CardDefinition Transformation => transformation;
        public CardAppearanceDefinition Appearance => appearance;
        public Texture2D Artwork => appearance != null ? appearance.FrontArtwork : artwork;
        public Color FaceColor => appearance != null ? appearance.FaceTint : faceColor;
        public bool ShowText => appearance == null || appearance.ShowText;
        public CardSpec ToSpec() => new CardSpec(definitionId, gesture, ability, transformation == null ? null : transformation.Id, DisplayName);
    }
}
