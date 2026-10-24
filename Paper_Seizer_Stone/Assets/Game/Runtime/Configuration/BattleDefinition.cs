using System;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    public sealed class BattleDefinition : ScriptableObject
    {
        [SerializeField] private DeckDefinition playerDeck;
        [SerializeField] private EnemyDefinition[] enemies;
        [SerializeField] private ShopDefinition shop;
        [SerializeField, Min(1)] private int playerLife = 2;
        [SerializeField, Min(0)] private int chips = 8;
        [Tooltip("零使用新种子，非零按场次派生种子。")]
        [SerializeField] private int seed;
        public ShopDefinition Shop => shop;

        public AdventureState CreateAdventure()
        {
            if (playerDeck == null || shop == null) throw new InvalidOperationException("请配置初始牌组与商店。");
            GetEnemy(0);
            return new AdventureState(playerDeck.CreateRecords("player"), playerLife);
        }

        public EnemyDefinition GetEnemy(int encounterIndex)
        {
            if (enemies == null || enemies.Length == 0) throw new InvalidOperationException("请配置至少一名敌人。");
            EnemyDefinition enemy = enemies[encounterIndex % enemies.Length];
            if (enemy == null || enemy.Deck == null || enemy.Life < 1)
                throw new InvalidOperationException("敌人缺少有效生命或牌组配置。");
            return enemy;
        }

        public BattleEngine CreateEngine(AdventureState adventure, out int actualSeed)
        {
            EnemyDefinition enemy = GetEnemy(adventure.EncounterIndex);
            var specs = new List<CardSpec>();
            foreach (CardDefinition card in GetCardAssets().Values) specs.Add(card.ToSpec());
            actualSeed = seed == 0 ? Environment.TickCount : unchecked(seed + adventure.EncounterIndex * 7919);
            return new BattleEngine(specs, adventure.Deck, enemy.Deck.CreateRecords("enemy"),
                adventure.Life, enemy.Life, chips, actualSeed);
        }

        public Dictionary<string, CardDefinition> GetCardAssets()
        {
            var result = new Dictionary<string, CardDefinition>();
            AddDeck(playerDeck, result);
            if (enemies != null)
                foreach (EnemyDefinition enemy in enemies)
                    if (enemy != null) AddDeck(enemy.Deck, result);
            if (shop != null && shop.CardPool != null)
                foreach (ShopEntry entry in shop.CardPool)
                    if (entry != null) AddCard(entry.Card, result);
            return result;
        }

        private static void AddDeck(DeckDefinition deck, Dictionary<string, CardDefinition> result)
        {
            if (deck == null || deck.Cards == null) return;
            foreach (DeckEntry entry in deck.Cards) if (entry != null) AddCard(entry.Card, result);
        }

        private static void AddCard(CardDefinition card, Dictionary<string, CardDefinition> result)
        {
            if (card == null) return;
            if (result.TryGetValue(card.Id, out CardDefinition existing))
            {
                if (existing != card) throw new InvalidOperationException("不同卡牌资源使用了相同标识：" + card.Id);
                return;
            }
            result[card.Id] = card;
            AddCard(card.Transformation, result);
        }
    }
}
