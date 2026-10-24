using System;
using System.Collections.Generic;

namespace PaperSeizerStone.Battle
{
    // Authoritative rules. No transforms, coroutines, Unity objects or animation callbacks.
    public sealed class BattleEngine
    {
        private readonly Random random;
        private readonly Dictionary<string, CardSpec> catalog = new Dictionary<string, CardSpec>();
        private BattleObservation enemyCheatStart;
        public BattleState State { get; }

        public BattleEngine(IReadOnlyList<CardSpec> definitions, IReadOnlyList<DeckCardRecord> playerDeck,
            IReadOnlyList<DeckCardRecord> enemyDeck, int playerLife, int enemyLife, int chips, int seed)
        {
            if (playerLife < 1 || enemyLife < 1 || chips < 0) throw new ArgumentException("战斗的初始生命或筹码配置无效。");
            foreach (CardSpec definition in definitions) catalog.Add(definition.Id, definition);
            foreach (CardSpec definition in definitions)
                if (definition.Ability == CardAbility.Glass && (string.IsNullOrEmpty(definition.TransformId) || !catalog.ContainsKey(definition.TransformId)))
                    throw new ArgumentException("玻璃必须配置碎玻璃变形目标。");
            State = new BattleState(playerLife, enemyLife, chips);
            random = new Random(seed);
            AddDeck(playerDeck, catalog, Side.Player);
            AddDeck(enemyDeck, catalog, Side.Enemy);
            Shuffle(State.Get(Side.Player).DrawCards);
            Shuffle(State.Get(Side.Enemy).DrawCards);
        }

        public BattleObservation Observe(Side observer) => new BattleObservation(State, observer);

        public BattleObservation ObserveEnemyCheat()
        {
            if (State.Phase != BattlePhase.EnemyCheat || enemyCheatStart == null)
                throw new InvalidOperationException("当前没有可用的作弊开始快照。");
            return enemyCheatStart.WithForbiddenCards(State.ForbiddenCards);
        }

        private static string SideName(Side side) => side == Side.Player ? "你" : "对手";
        private static string GestureName(Gesture gesture) => gesture == Gesture.Rock ? "石头" : gesture == Gesture.Paper ? "布" : "剪刀";

        private void AddDeck(IReadOnlyList<DeckCardRecord> records, Dictionary<string, CardSpec> catalog, Side side)
        {
            if (records == null || records.Count < 5) throw new ArgumentException("每副牌至少需要五张卡牌。");
            var recordIds = new HashSet<string>();
            foreach (DeckCardRecord record in records)
            {
                if (record == null || string.IsNullOrEmpty(record.RecordId) || !recordIds.Add(record.RecordId))
                    throw new ArgumentException("牌组记录的标识不能为空或重复。");
                if (!catalog.TryGetValue(record.DefinitionId, out CardSpec spec))
                    throw new ArgumentException("找不到卡牌配置：" + record.DefinitionId);
                int id = State.CardMap.Count + 1;
                State.CardMap.Add(id, new CardRuntime(id, record, spec, side));
                State.Get(side).DrawCards.Add(id);
            }
        }

        private void Shuffle(List<int> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int value = cards[i];
                cards[i] = cards[j];
                cards[j] = value;
            }
        }

        private void Emit(List<BattleEvent> events, BattleEventKind kind, string message,
            Side side = Side.Player, int cardId = 0, int lane = -1, bool hit = false)
        {
            events.Add(new BattleEvent(State, kind, side, message, cardId, lane, hit));
        }

        public ActionResult BeginRound()
        {
            if (State.Phase != BattlePhase.Created && State.Phase != BattlePhase.RoundEnd)
                return ActionResult.Fail("当前阶段不能开始新回合。");
            var events = new List<BattleEvent>();
            State.Phase = BattlePhase.Preparing;
            State.Round++;
            State.RevealStep = 0;
            State.ForbiddenCards.Clear();
            enemyCheatStart = null;
            foreach (CardRuntime card in State.CardMap.Values)
            {
                card.IsRevealed = false;
                card.RevealedThisRound = false;
            }
            foreach (Side side in new[] { Side.Player, Side.Enemy })
            {
                State.Get(side).Bullets = 2;
                State.Get(side).Score = 0;
            }
            Emit(events, BattleEventKind.RoundStarted, "新回合开始，双方左轮重新装入两发子弹。");
            int count = State.Round == 1 ? 5 : 4;
            foreach (Side side in new[] { Side.Player, Side.Enemy })
                for (int i = 0; i < count; i++) Draw(side, events);
            State.Phase = BattlePhase.Planning;
            Emit(events, BattleEventKind.PhaseChanged, "选好四张牌，安排它们的位置。");
            return ActionResult.Ok(events);
        }

        private int Draw(Side side, List<BattleEvent> events)
        {
            FighterState fighter = State.Get(side);
            if (fighter.DrawCards.Count == 0 && fighter.DiscardCards.Count > 0)
            {
                fighter.DrawCards.AddRange(fighter.DiscardCards);
                fighter.DiscardCards.Clear();
                Shuffle(fighter.DrawCards);
                Emit(events, BattleEventKind.DeckShuffled, SideName(side) + "的弃牌已洗入牌库。", side);
            }
            if (fighter.DrawCards.Count == 0) return 0;
            int last = fighter.DrawCards.Count - 1;
            int id = fighter.DrawCards[last];
            fighter.DrawCards.RemoveAt(last);
            fighter.HandCards.Add(id);
            Emit(events, BattleEventKind.CardDrawn, SideName(side) + "抽取了一张牌。", side, id);
            return id;
        }

        private bool ValidLayout(Side side, IReadOnlyList<int> slots)
        {
            if (slots == null || slots.Count != BasicRules.Slots) return false;
            var unique = new HashSet<int>();
            for (int i = 0; i < slots.Count; i++)
                if (!State.Get(side).HandCards.Contains(slots[i]) || !unique.Add(slots[i])) return false;
            return true;
        }

        public ActionResult CommitLayouts(IReadOnlyList<int> playerSlots, IReadOnlyList<int> enemySlots)
        {
            if (State.Phase != BattlePhase.Planning) return ActionResult.Fail("当前不是布阵阶段。");
            if (!ValidLayout(Side.Player, playerSlots) || !ValidLayout(Side.Enemy, enemySlots))
                return ActionResult.Fail("请从手牌中选出四张不同的牌，填满场上槽位。");
            var events = new List<BattleEvent>();
            PutLayout(Side.Player, playerSlots);
            PutLayout(Side.Enemy, enemySlots);
            State.Phase = BattlePhase.Revealing;
            Emit(events, BattleEventKind.LayoutCommitted, "双方出牌已确认，准备揭牌。");
            return ActionResult.Ok(events);
        }

        private void PutLayout(Side side, IReadOnlyList<int> layout)
        {
            FighterState fighter = State.Get(side);
            for (int i = 0; i < BasicRules.Slots; i++)
            {
                fighter.Slots[i] = layout[i];
                fighter.HandCards.Remove(layout[i]);
            }
        }

        public ActionResult RevealNext()
        {
            if (State.Phase != BattlePhase.Revealing || State.RevealStep >= 4)
                return ActionResult.Fail("当前阶段不能揭牌。");
            var events = new List<BattleEvent>();
            State.RevealStep++;
            foreach (Side color in new[] { Side.Player, Side.Enemy })
            {
                var candidates = new List<int>();
                foreach (Side owner in new[] { Side.Player, Side.Enemy })
                    foreach (int id in State.Get(owner).Board)
                        if (id != 0 && State.Cards[id].OriginalSide == color && !State.Cards[id].IsRevealed)
                            candidates.Add(id);
                if (State.RevealStep < 4 && candidates.Count > 0)
                {
                    int chosen = candidates[random.Next(candidates.Count)];
                    candidates.Clear();
                    candidates.Add(chosen);
                }
                foreach (int id in candidates)
                {
                    CardRuntime card = State.CardMap[id];
                    card.IsRevealed = true;
                    bool firstReveal = !card.RevealedThisRound;
                    card.RevealedThisRound = true;
                    Emit(events, BattleEventKind.CardRevealed, SideName(color) + "揭示了「" + card.Definition.Name + "」。", color, id);
                    if (firstReveal) RevealAbility(card, events);
                }
            }
            State.Phase = State.RevealStep == 4 ? BattlePhase.Scoring : BattlePhase.PlayerCheat;
            enemyCheatStart = State.Phase == BattlePhase.PlayerCheat ? Observe(Side.Enemy) : null;
            Emit(events, BattleEventKind.PhaseChanged, State.Phase == BattlePhase.Scoring
                ? "所有底牌已揭示，开始计分。" : "要冒险改变局面，还是保持原样？");
            return ActionResult.Ok(events);
        }

        private Side BoardSide(int id)
        {
            return Array.IndexOf(State.Get(Side.Player).Slots, id) >= 0 ? Side.Player : Side.Enemy;
        }

        private void RevealAbility(CardRuntime card, List<BattleEvent> events)
        {
            Side side = BoardSide(card.InstanceId);
            if (card.Definition.Ability == CardAbility.Pliers)
            {
                FighterState opponent = State.Get(BasicRules.Other(side));
                int removed = opponent.Bullets > 1 ? 1 : 0;
                opponent.Bullets -= removed;
                Emit(events, BattleEventKind.AbilityTriggered, removed > 0 ? SideName(side) + "的钳子移除了对方一发子弹。" : "钳子无法继续减弹：至少保留一发。", side, card.InstanceId);
            }
            else if (card.Definition.Ability == CardAbility.Fork)
            {
                Emit(events, BattleEventKind.AbilityTriggered, SideName(side) + "的叉子尝试抽取一张牌，不增加作弊子弹。", side, card.InstanceId);
                if (Draw(side, events) == 0) Emit(events, BattleEventKind.AbilityTriggered, "牌库与弃牌堆已空，叉子没有抽到牌。", side, card.InstanceId);
            }
        }

        private void TransformAfterSwap(int id, Side side, List<BattleEvent> events)
        {
            CardRuntime card = State.CardMap[id];
            if (card.Definition.Ability != CardAbility.Glass) return;
            card.Definition = catalog[card.Definition.TransformId];
            // Private enemy transformations must not disclose the hidden card's identity.
            string message = side == Side.Player || card.IsRevealed ? SideName(side) + "的玻璃碎裂，变为碎玻璃。" : "对手的暗牌发生了变化。";
            Emit(events, BattleEventKind.CardTransformed, message, side, id);
        }

        public ActionResult Cheat(Side side, CheatAction action)
        {
            BattlePhase expected = side == Side.Player ? BattlePhase.PlayerCheat : BattlePhase.EnemyCheat;
            if (State.Phase != expected || action == null) return ActionResult.Fail("当前不能执行这次作弊。");
            FighterState fighter = State.Get(side);
            FighterState opponent = State.Get(BasicRules.Other(side));
            int cost = action.Kind == CheatKind.Pass ? 0 : action.Kind == CheatKind.Draw ? 2 : 1;
            if (cost > 0 && opponent.Bullets + cost > BasicRules.Chambers)
                return ActionResult.Fail("对方弹巢的空位不足，无法进行这次作弊。");
            if (side == Side.Enemy && (State.ForbiddenCards.Contains(action.FirstCard)
                || State.ForbiddenCards.Contains(action.SecondCard)))
                return ActionResult.Fail("这张牌已被玩家本次行动涉及，电脑不能操作。");

            int firstSlot = Array.IndexOf(fighter.Slots, action.FirstCard);
            int secondSlot = Array.IndexOf(fighter.Slots, action.SecondCard);
            int handIndex = fighter.HandCards.IndexOf(action.SecondCard);
            switch (action.Kind)
            {
                case CheatKind.SwapBoard:
                    if (firstSlot < 0 || secondSlot < 0 || firstSlot == secondSlot)
                        return ActionResult.Fail("请选择己方场上的两张不同卡牌。");
                    break;
                case CheatKind.SwapHand:
                    if (firstSlot < 0 || handIndex < 0) return ActionResult.Fail("请选择一张场上卡牌与一张手牌交换。");
                    break;
                case CheatKind.Draw:
                    if (fighter.DrawCards.Count + fighter.DiscardCards.Count == 0)
                        return ActionResult.Fail("牌库和弃牌堆都已为空，无法继续抽牌。");
                    break;
                case CheatKind.Pass: break;
                default: return ActionResult.Fail("无法识别这次作弊操作。");
            }

            var events = new List<BattleEvent>();
            if (action.Kind == CheatKind.SwapBoard)
            {
                fighter.Slots[firstSlot] = action.SecondCard;
                fighter.Slots[secondSlot] = action.FirstCard;
            }
            else if (action.Kind == CheatKind.SwapHand)
            {
                fighter.Slots[firstSlot] = action.SecondCard;
                fighter.HandCards[handIndex] = action.FirstCard;
            }
            if (side == Side.Player && (action.Kind == CheatKind.SwapBoard || action.Kind == CheatKind.SwapHand))
            {
                State.ForbiddenCards.Add(action.FirstCard);
                State.ForbiddenCards.Add(action.SecondCard);
            }
            if (action.Kind == CheatKind.SwapBoard || action.Kind == CheatKind.SwapHand)
            {
                Emit(events, BattleEventKind.CardsSwapped, SideName(side) + "交换了两张牌。", side, action.FirstCard);
                TransformAfterSwap(action.FirstCard, side, events);
                TransformAfterSwap(action.SecondCard, side, events);
            }
            else if (action.Kind == CheatKind.Draw)
            {
                int drawnId = Draw(side, events);
                if (side == Side.Player) State.ForbiddenCards.Add(drawnId);
            }
            else Emit(events, BattleEventKind.DecisionPassed, SideName(side) + "选择不作弊。", side);

            if (cost > 0)
            {
                opponent.Bullets += cost;
                Emit(events, BattleEventKind.AmmunitionChanged, SideName(side) + "为对方的左轮增加了子弹。", BasicRules.Other(side));
            }
            if (side == Side.Player) State.Phase = BattlePhase.EnemyCheat;
            else
            {
                State.ForbiddenCards.Clear();
                enemyCheatStart = null;
                State.Phase = BattlePhase.Revealing;
            }
            Emit(events, BattleEventKind.PhaseChanged, side == Side.Player ? "双方正在执行本次行动。" : "继续揭牌。");
            return ActionResult.Ok(events);
        }

        public ActionResult ScoreRound()
        {
            if (State.Phase != BattlePhase.Scoring) return ActionResult.Fail("当前阶段不能计分。");
            var events = new List<BattleEvent>();
            FighterState player = State.Get(Side.Player);
            FighterState enemy = State.Get(Side.Enemy);
            for (int lane = 0; lane < BasicRules.Slots; lane++)
            {
                CardRuntime a = State.CardMap[player.Slots[lane]];
                CardRuntime b = State.CardMap[enemy.Slots[lane]];
                int result = CardRules.Compare(a.Definition, b.Definition);
                int p = CardRules.Points(a.Definition, result);
                int e = CardRules.Points(b.Definition, -result);
                player.Score += p;
                enemy.Score += e;
                Emit(events, BattleEventKind.LaneScored, "第 " + (lane + 1) + " 组对位    你 +" + p + " 分    ·    对手 +" + e + " 分", lane: lane);
                if (result != 0) WinAbility(result > 0 ? a : b, result > 0 ? Side.Player : Side.Enemy, events);
            }
            State.Phase = BattlePhase.Shooting;
            Emit(events, BattleEventKind.PhaseChanged, player.Score == enemy.Score ? "总分相同，双方同时开枪。" : player.Score > enemy.Score ? "你赢下了总分，举枪吧。" : "对手赢下了总分，即将开枪。");
            return ActionResult.Ok(events);
        }

        private void WinAbility(CardRuntime card, Side side, List<BattleEvent> events)
        {
            if (card.Definition.Ability == CardAbility.Silk)
            {
                State.Chips += 2;
                Emit(events, BattleEventKind.AbilityTriggered, SideName(side) + "的绸缎赢下对位，奖池增加两枚筹码。", side, card.InstanceId);
            }
            else if (card.Definition.Ability == CardAbility.GunpowderPaper)
            {
                FighterState fighter = State.Get(side);
                bool full = fighter.Bullets >= BasicRules.Chambers;
                fighter.Bullets = Math.Min(BasicRules.Chambers, fighter.Bullets + 1);
                Emit(events, BattleEventKind.AbilityTriggered, full ? "火药纸获胜，但己方弹巢已满。" : SideName(side) + "的火药纸获胜，己方装入一发子弹。", side, card.InstanceId);
            }
        }

        public ActionResult ExecuteShots()
        {
            if (State.Phase != BattlePhase.Shooting) return ActionResult.Fail("当前阶段不能开枪。");
            var events = new List<BattleEvent>();
            FighterState player = State.Get(Side.Player);
            FighterState enemy = State.Get(Side.Enemy);
            bool playerFires = player.Score >= enemy.Score;
            bool enemyFires = enemy.Score >= player.Score;
            // Both outcomes are sampled before applying any damage, including mutual death.
            bool playerHit = playerFires && random.Next(BasicRules.Chambers) < player.Bullets;
            bool enemyHit = enemyFires && random.Next(BasicRules.Chambers) < enemy.Bullets;
            if (playerHit) enemy.Life = Math.Max(0, enemy.Life - 1);
            if (enemyHit) player.Life = Math.Max(0, player.Life - 1);
            if (playerFires) Emit(events, BattleEventKind.ShotResolved, playerHit ? "你的子弹击中了对手。" : "你的左轮传来一声空响。", Side.Player, hit: playerHit);
            if (enemyFires) Emit(events, BattleEventKind.ShotResolved, enemyHit ? "对手的子弹击中了你。" : "对手的左轮传来一声空响。", Side.Enemy, hit: enemyHit);

            if (player.Life == 0 || enemy.Life == 0)
            {
                State.Phase = BattlePhase.BattleEnd;
                State.RewardChips = player.Life > 0 ? State.Chips : 0;
                Emit(events, BattleEventKind.BattleEnded, player.Life > 0 ? "你赢下了这场对局。" : "这场冒险到此为止。");
            }
            else
            {
                State.Phase = BattlePhase.RoundEnd;
                foreach (Side side in new[] { Side.Player, Side.Enemy })
                {
                    FighterState fighter = State.Get(side);
                    foreach (int id in fighter.Slots) if (id != 0) fighter.DiscardCards.Add(id);
                    Array.Clear(fighter.Slots, 0, fighter.Slots.Length);
                }
                State.Chips = Math.Max(0, State.Chips - 1);
                Emit(events, BattleEventKind.RoundEnded, "场上卡牌已弃置，手牌保留到下一回合。");
            }
            return ActionResult.Ok(events);
        }
    }
}
