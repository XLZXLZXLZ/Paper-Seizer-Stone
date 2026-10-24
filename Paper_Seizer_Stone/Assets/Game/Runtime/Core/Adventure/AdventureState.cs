using System;
using System.Collections.Generic;

namespace PaperSeizerStone.Battle
{
    public enum AdventurePhase { Battle, Shop, Defeat }

    public sealed class ShopOffer
    {
        public string DefinitionId { get; }
        public int Price { get; }
        public bool Sold { get; internal set; }

        public ShopOffer(string definitionId, int price)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || price < 0)
                throw new ArgumentException("商品必须配置卡牌与非负价格。");
            DefinitionId = definitionId;
            Price = price;
        }
    }

    // Owns persistent encounter-to-encounter data; never writes into configuration assets.
    public sealed class AdventureState
    {
        private readonly List<DeckCardRecord> deck = new List<DeckCardRecord>();
        private readonly List<ShopOffer> offers = new List<ShopOffer>();
        private int purchaseSerial;
        public IReadOnlyList<DeckCardRecord> Deck { get; }
        public IReadOnlyList<ShopOffer> Offers { get; }
        public AdventurePhase Phase { get; private set; }
        public int Life { get; private set; }
        public int Wallet { get; private set; }
        public int EncounterIndex { get; private set; }

        public AdventureState(IReadOnlyList<DeckCardRecord> startingDeck, int life)
        {
            if (startingDeck == null || startingDeck.Count < 5 || life < 1)
                throw new ArgumentException("冒险需要至少五张初始牌和有效生命。");
            var ids = new HashSet<string>();
            foreach (DeckCardRecord card in startingDeck)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.RecordId) || !ids.Add(card.RecordId))
                    throw new ArgumentException("初始牌组包含无效或重复的持有记录。");
                deck.Add(new DeckCardRecord(card.RecordId, card.DefinitionId));
            }
            Life = life;
            Deck = deck.AsReadOnly();
            Offers = offers.AsReadOnly();
        }

        public void FinishBattle(BattleObservation result, IReadOnlyList<ShopOffer> stock)
        {
            if (Phase != AdventurePhase.Battle || result.Phase != BattlePhase.BattleEnd)
                throw new InvalidOperationException("当前不能重复结算战斗。");
            Life = result.Player.Life;
            if (!result.PlayerWon) { Phase = AdventurePhase.Defeat; return; }
            Wallet = checked(Wallet + result.RewardChips);
            offers.Clear();
            foreach (ShopOffer item in stock) offers.Add(new ShopOffer(item.DefinitionId, item.Price));
            Phase = AdventurePhase.Shop;
        }

        public bool TryBuy(int index, out string error)
        {
            error = null;
            if (Phase != AdventurePhase.Shop || index < 0 || index >= offers.Count)
                error = "当前无法购买这张牌。";
            else if (offers[index].Sold) error = "这张商品已经售罄。";
            else if (Wallet < offers[index].Price) error = "筹码不足，无法购买。";
            if (error != null) return false;
            ShopOffer offer = offers[index];
            string recordId;
            do { recordId = "purchase-" + (++purchaseSerial); }
            while (deck.Exists(card => card.RecordId == recordId));
            deck.Add(new DeckCardRecord(recordId, offer.DefinitionId));
            Wallet -= offer.Price;
            offer.Sold = true;
            return true;
        }

        public bool TryRemove(string recordId, int price, int minimumCards, out string error)
        {
            error = null;
            int index = deck.FindIndex(card => card.RecordId == recordId);
            if (Phase != AdventurePhase.Shop || index < 0) error = "请先选择牌组中的一张牌。";
            else if (price < 0) error = "删牌价格配置无效。";
            else if (deck.Count <= Math.Max(5, minimumCards)) error = "牌组至少需要保留 " + Math.Max(5, minimumCards) + " 张牌。";
            else if (Wallet < price) error = "筹码不足，无法删牌。";
            if (error != null) return false;
            Wallet -= price;
            deck.RemoveAt(index);
            return true;
        }

        public void BeginNextBattle()
        {
            if (Phase != AdventurePhase.Shop) throw new InvalidOperationException("当前不能离开商店。");
            offers.Clear();
            EncounterIndex++;
            Phase = AdventurePhase.Battle;
        }
    }
}
