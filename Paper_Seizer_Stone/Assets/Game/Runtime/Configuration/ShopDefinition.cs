using System;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Configuration
{
    [Serializable]
    public sealed class ShopEntry
    {
        public CardDefinition Card;
        [Min(0)] public int Price = 2;
    }

    public sealed class ShopDefinition : ScriptableObject
    {
        [SerializeField] private ShopEntry[] cardPool;
        [SerializeField, Range(1, 3)] private int offerCount = 3;
        [SerializeField] private bool guaranteeSpecialCard = true;
        [SerializeField, Min(0)] private int removePrice = 3;
        [SerializeField, Min(5)] private int minimumDeckSize = 5;
        public IReadOnlyList<ShopEntry> CardPool => cardPool;
        public int RemovePrice => removePrice;
        public int MinimumDeckSize => Math.Max(5, minimumDeckSize);

        public List<ShopOffer> CreateStock(int seed)
        {
            if (cardPool == null || cardPool.Length == 0) throw new InvalidOperationException("商店未配置商品池。");
            var candidates = new List<ShopEntry>();
            var ids = new HashSet<string>();
            foreach (ShopEntry entry in cardPool)
            {
                if (entry == null || entry.Card == null || entry.Price < 0)
                    throw new InvalidOperationException("商店商品配置无效。");
                if (entry.Card.Ability != CardAbility.ShatteredGlass && ids.Add(entry.Card.Id)) candidates.Add(entry);
            }
            var random = new System.Random(seed);
            var result = new List<ShopOffer>();
            if (guaranteeSpecialCard)
            {
                var special = candidates.FindAll(entry => entry.Card.Ability != CardAbility.None);
                if (special.Count > 0)
                {
                    ShopEntry entry = special[random.Next(special.Count)];
                    candidates.Remove(entry);
                    result.Add(new ShopOffer(entry.Card.Id, entry.Price));
                }
            }
            while (result.Count < Mathf.Clamp(offerCount, 1, 3) && candidates.Count > 0)
            {
                int index = random.Next(candidates.Count);
                ShopEntry entry = candidates[index];
                candidates.RemoveAt(index);
                if (entry == null || entry.Card == null || entry.Price < 0)
                    throw new InvalidOperationException("商店商品配置无效。");
                result.Add(new ShopOffer(entry.Card.Id, entry.Price));
            }
            return result;
        }
    }
}
