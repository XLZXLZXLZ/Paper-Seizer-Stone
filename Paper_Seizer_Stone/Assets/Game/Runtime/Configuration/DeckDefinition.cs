using System;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    [Serializable]
    public sealed class DeckEntry
    {
        public CardDefinition Card;
        [Min(1)] public int Copies = 4;
    }

    public sealed class DeckDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private DeckEntry[] cards;
        public string DisplayName => displayName;
        public IReadOnlyList<DeckEntry> Cards => cards;

        public List<DeckCardRecord> CreateRecords(string prefix)
        {
            if (cards == null) throw new InvalidOperationException(name + "未配置牌组。");
            var result = new List<DeckCardRecord>();
            foreach (DeckEntry entry in cards)
            {
                if (entry == null || entry.Card == null || entry.Copies < 1)
                    throw new InvalidOperationException(name + "的牌组条目无效。");
                for (int i = 0; i < entry.Copies; i++)
                    result.Add(new DeckCardRecord(prefix + "-" + result.Count, entry.Card.Id));
            }
            if (result.Count < 5) throw new InvalidOperationException(name + "至少需要五张牌。");
            return result;
        }
    }
}
