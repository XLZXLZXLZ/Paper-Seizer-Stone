using System;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    [Serializable]
    public sealed class CardTagAppearance
    {
        public CardTag tag;
        public string displayName;
        [TextArea] public string description;
        [Min(0)] public int price = 3;
        public Color color = Color.white;
        [Range(0, 1)] public float strength = 0.85f;
        [Range(0.3f, 3)] public float patternScale = 1;
    }

    // One authoring source for the shop, world cards and UI previews.
    public sealed class CardTagCatalog : ScriptableObject
    {
        [SerializeField] private CardTagAppearance[] tags;
        public CardTagAppearance Find(CardTag tag)
        {
            if (tags != null) foreach (CardTagAppearance entry in tags)
                if (entry != null && entry.tag == tag) return entry;
            return null;
        }
    }
}
