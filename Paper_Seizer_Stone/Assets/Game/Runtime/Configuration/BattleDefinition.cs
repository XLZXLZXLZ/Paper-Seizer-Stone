using System;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    public sealed class BattleDefinition : ScriptableObject
    {
        public const int EncountersPerCycle = 4;

        [SerializeField] private DeckDefinition playerDeck;
        [Tooltip("每轮按三个普通敌人、一个头目的顺序配置。超过已配置轮次后重复最后一轮。")]
        [SerializeField] private EnemyDefinition[] enemies;
        [SerializeField] private ShopDefinition shop;
        [SerializeField, Min(1)] private int playerLife = 2;
        [SerializeField, Min(0)] private int chips = 10;
        [Tooltip("零使用新种子，非零按场次派生种子。")]
        [SerializeField] private int seed;
        [SerializeField, Min(1)] private int engravingStartCycle = 3;
        [SerializeField, Range(0, 1)] private float initialEnemyEngravingChance = 0.2f;
        [SerializeField, Range(0, 1)] private float enemyEngravingChancePerCycle = 0.1f;
        [SerializeField, Range(0, 1)] private float maximumEnemyEngravingChance = 1f;
        public ShopDefinition Shop => shop;

        public int GetCycleNumber(int encounterIndex)
        {
            if (encounterIndex < 0) throw new ArgumentOutOfRangeException(nameof(encounterIndex));
            return encounterIndex / EncountersPerCycle + 1;
        }

        public float GetEnemyEngravingChance(int encounterIndex)
        {
            int cycle = GetCycleNumber(encounterIndex);
            int firstCycle = Mathf.Max(1, engravingStartCycle);
            if (cycle < firstCycle) return 0f;
            float chance = Mathf.Max(0f, initialEnemyEngravingChance)
                + (cycle - firstCycle) * Mathf.Max(0f, enemyEngravingChancePerCycle);
            return Mathf.Min(Mathf.Clamp01(maximumEnemyEngravingChance), Mathf.Clamp01(chance));
        }

        public AdventureState CreateAdventure()
        {
            if (playerDeck == null || shop == null) throw new InvalidOperationException("请配置初始牌组与商店。");
            GetEnemy(0);
            return new AdventureState(playerDeck.CreateRecords("player"), playerLife);
        }

        public EnemyDefinition GetEnemy(int encounterIndex)
        {
            int cycleIndex = GetCycleNumber(encounterIndex) - 1;
            if (enemies == null || enemies.Length == 0 || enemies.Length % EncountersPerCycle != 0)
                throw new InvalidOperationException("请按每轮三个普通敌人、一个头目的顺序完整配置敌人。");
            int configuredCycle = Mathf.Min(cycleIndex, enemies.Length / EncountersPerCycle - 1);
            int slot = encounterIndex % EncountersPerCycle;
            EnemyDefinition enemy = enemies[configuredCycle * EncountersPerCycle + slot];
            if (enemy == null || enemy.Deck == null || enemy.Life < 1)
                throw new InvalidOperationException("敌人缺少有效生命或牌组配置。");
            if (enemy.IsBoss != (slot == EncountersPerCycle - 1))
                throw new InvalidOperationException("每轮前三场必须配置普通敌人，第四场必须配置头目。");
            return enemy;
        }

        public BattleEngine CreateEngine(AdventureState adventure, out int actualSeed)
        {
            EnemyDefinition enemy = GetEnemy(adventure.EncounterIndex);
            var specs = new List<CardSpec>();
            foreach (CardDefinition card in GetCardAssets().Values) specs.Add(card.ToSpec());
            actualSeed = seed == 0 ? Environment.TickCount : unchecked(seed + adventure.EncounterIndex * 7919);
            List<DeckCardRecord> enemyDeck = enemy.Deck.CreateRecords("enemy");
            ApplyEnemyEngravings(enemyDeck, adventure.EncounterIndex, actualSeed);
            return new BattleEngine(specs, adventure.Deck, enemyDeck,
                adventure.Life, enemy.Life, chips, actualSeed);
        }

        private void ApplyEnemyEngravings(List<DeckCardRecord> deck, int encounterIndex, int actualSeed)
        {
            float chance = GetEnemyEngravingChance(encounterIndex);
            if (chance <= 0f) return;
            // A separate stream leaves battle shuffles, reveals and shots unchanged.
            var engravingRandom = new System.Random(unchecked(actualSeed ^ 0x4F1BBCDC ^ (encounterIndex * 104729)));
            foreach (DeckCardRecord card in deck)
                if (engravingRandom.NextDouble() < chance)
                    card.Tag = (CardTag)engravingRandom.Next((int)CardTag.Violence, (int)CardTag.Balance + 1);
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
