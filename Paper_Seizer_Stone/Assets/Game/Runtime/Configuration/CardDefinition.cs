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
        [SerializeField] private Texture2D artwork;
        [SerializeField] private Color faceColor = new Color(0.84f, 0.77f, 0.6f);

        public string Id => definitionId;
        public string DisplayName => displayName;
        public Gesture Gesture => gesture;
        public Texture2D Artwork => artwork;
        public Color FaceColor => faceColor;
        public CardSpec ToSpec() => new CardSpec(definitionId, gesture);
    }
}
