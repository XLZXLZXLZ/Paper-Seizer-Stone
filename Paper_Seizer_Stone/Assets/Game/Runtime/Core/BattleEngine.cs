using System;
using System.Collections.Generic;

namespace PaperSeizerStone.Battle
{
    // Authoritative rules. No transforms, coroutines, Unity objects or animation callbacks.
    public sealed class BattleEngine
    {
        private readonly Random random;
        public BattleState State { get; }

        public BattleEngine(IReadOnlyList<CardSpec> definitions, IReadOnlyList<DeckCardRecord> playerDeck,
            IReadOnlyList<DeckCardRecord> enemyDeck, int playerLife, int enemyLife, int chips, int seed)
        {
            if (playerLife < 1 || enemyLife < 1 || chips < 0) throw new ArgumentException("战斗的初始生命或筹码配置无效。");
            var catalog = new Dictionary<string, CardSpec>();
            foreach (CardSpec definition in definitions) catalog.Add(definition.Id, definition);
            State = new BattleState(playerLife, enemyLife, chips);
            random = new Random(seed);
            AddDeck(playerDeck, catalog, Side.Player);
            AddDeck(enemyDeck, catalog, Side.Enemy);
            Shuffle(State.Get(Side.Player).DrawCards);
            Shuffle(State.Get(Side.Enemy).DrawCards);
        }

        public BattleObservation Observe(Side observer) => new BattleObservation(State, observer);

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
                    card.RevealedThisRound = true;
                    Emit(events, BattleEventKind.CardRevealed, SideName(color) + "揭示了「" + GestureName(card.Definition.Gesture) + "」。", color, id);
                }
            }
            State.Phase = State.RevealStep == 4 ? BattlePhase.Scoring : BattlePhase.PlayerCheat;
            Emit(events, BattleEventKind.PhaseChanged, State.Phase == BattlePhase.Scoring
                ? "所有底牌已揭示，开始计分。" : "要冒险改变局面，还是保持原样？");
            return ActionResult.Ok(events);
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
                Emit(events, BattleEventKind.CardsSwapped, SideName(side) + "交换了两张牌。", side, action.FirstCard);
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
                State.Phase = BattlePhase.Revealing;
            }
            Emit(events, BattleEventKind.PhaseChanged, side == Side.Player ? "对手正在考虑下一步。" : "继续揭牌。");
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
                Gesture a = State.Cards[player.Slots[lane]].Definition.Gesture;
                Gesture b = State.Cards[enemy.Slots[lane]].Definition.Gesture;
                int p = BasicRules.Points(a, b);
                int e = BasicRules.Points(b, a);
                player.Score += p;
                enemy.Score += e;
                Emit(events, BattleEventKind.LaneScored, "第 " + (lane + 1) + " 组对位    你 +" + p + " 分    ·    对手 +" + e + " 分", lane: lane);
            }
            State.Phase = BattlePhase.Shooting;
            Emit(events, BattleEventKind.PhaseChanged, player.Score == enemy.Score ? "总分相同，双方同时开枪。" : player.Score > enemy.Score ? "你赢下了总分，举枪吧。" : "对手赢下了总分，即将开枪。");
            return ActionResult.Ok(events);
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
