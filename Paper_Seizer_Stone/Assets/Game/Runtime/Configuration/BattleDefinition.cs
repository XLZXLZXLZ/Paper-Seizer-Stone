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

    public sealed class BattleDefinition : ScriptableObject
    {
        [SerializeField] private DeckEntry[] playerDeck;
        [SerializeField] private DeckEntry[] enemyDeck;
        [SerializeField, Min(1)] private int playerLife = 2;
        [SerializeField, Min(1)] private int enemyLife = 1;
        [SerializeField, Min(0)] private int chips = 8;
        [Tooltip("Zero uses a new seed on each battle.")]
        [SerializeField] private int seed;

        public BattleEngine CreateEngine(out int actualSeed)
        {
            var specs = new Dictionary<string, CardSpec>();
            var assets = new Dictionary<string, CardDefinition>();
            var player = BuildRecords(playerDeck, Side.Player, specs, assets);
            var enemy = BuildRecords(enemyDeck, Side.Enemy, specs, assets);
            actualSeed = seed == 0 ? Environment.TickCount : seed;
            return new BattleEngine(new List<CardSpec>(specs.Values), player, enemy, playerLife, enemyLife, chips, actualSeed);
        }

        private static List<DeckCardRecord> BuildRecords(DeckEntry[] entries, Side side,
            Dictionary<string, CardSpec> specs, Dictionary<string, CardDefinition> assets)
        {
            if (entries == null) throw new InvalidOperationException("请在战斗资源中配置双方牌组。");
            var records = new List<DeckCardRecord>();
            foreach (DeckEntry entry in entries)
            {
                if (entry == null || entry.Card == null || entry.Copies < 1)
                    throw new InvalidOperationException("每个牌组条目都需要卡牌资源，且张数必须大于零。");
                CardDefinition card = entry.Card;
                CardSpec spec = card.ToSpec();
                if (assets.TryGetValue(card.Id, out CardDefinition existing) && existing != card)
                    throw new InvalidOperationException("不同卡牌资源使用了相同标识：" + card.Id);
                assets[card.Id] = card;
                specs[card.Id] = spec;
                for (int i = 0; i < entry.Copies; i++)
                    records.Add(new DeckCardRecord(side + "-" + records.Count, card.Id));
            }
            return records;
        }

        public Dictionary<string, CardDefinition> GetCardAssets()
        {
            var result = new Dictionary<string, CardDefinition>();
            foreach (DeckEntry[] deck in new[] { playerDeck, enemyDeck })
                if (deck != null)
                    foreach (DeckEntry entry in deck)
                        if (entry?.Card != null) result[entry.Card.Id] = entry.Card;
            return result;
        }
    }
}
