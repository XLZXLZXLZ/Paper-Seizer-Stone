using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PaperSeizerStone.Battle
{
    public sealed class FighterState
    {
        internal readonly List<int> DrawCards = new List<int>();
        internal readonly List<int> HandCards = new List<int>();
        internal readonly List<int> DiscardCards = new List<int>();
        internal readonly int[] Slots = new int[BasicRules.Slots];

        public IReadOnlyList<int> DrawPile { get; }
        public IReadOnlyList<int> Hand { get; }
        public IReadOnlyList<int> DiscardPile { get; }
        public IReadOnlyList<int> Board { get; }
        public int Life { get; internal set; }
        public int Bullets { get; internal set; }
        public int Score { get; internal set; }

        internal FighterState(int life)
        {
            Life = life;
            DrawPile = DrawCards.AsReadOnly();
            Hand = HandCards.AsReadOnly();
            DiscardPile = DiscardCards.AsReadOnly();
            Board = Array.AsReadOnly(Slots);
        }
    }

    public sealed class BattleState
    {
        internal readonly Dictionary<int, CardRuntime> CardMap = new Dictionary<int, CardRuntime>();
        internal readonly HashSet<int> ForbiddenCards = new HashSet<int>();
        private readonly FighterState[] fighters;

        public IReadOnlyDictionary<int, CardRuntime> Cards { get; }
        public BattlePhase Phase { get; internal set; } = BattlePhase.Created;
        public int Round { get; internal set; }
        public int RevealStep { get; internal set; }
        public int Chips { get; internal set; }
        public int RewardChips { get; internal set; }
        public bool PlayerWon => Phase == BattlePhase.BattleEnd && Get(Side.Player).Life > 0 && Get(Side.Enemy).Life == 0;

        internal BattleState(int playerLife, int enemyLife, int chips)
        {
            fighters = new[] { new FighterState(playerLife), new FighterState(enemyLife) };
            Chips = chips;
            Cards = new ReadOnlyDictionary<int, CardRuntime>(CardMap);
        }

        public FighterState Get(Side side) => fighters[(int)side];
    }

    // Snapshots contain only information the observer may know, never Runtime references.
    public sealed class ObservedCard
    {
        public int Id { get; }
        public Side OriginalSide { get; }
        public string DefinitionId { get; }
        public Gesture? Gesture { get; }
        public CardSpec Spec { get; }
        public bool IsRevealed { get; }

        internal ObservedCard(CardRuntime card, bool visible)
        {
            Id = card.InstanceId;
            OriginalSide = card.OriginalSide;
            IsRevealed = card.IsRevealed;
            DefinitionId = visible ? card.Definition.Id : null;
            Gesture = visible ? (Gesture?)card.Definition.Gesture : null;
            Spec = visible ? card.Definition : null;
        }
    }

    public sealed class FighterObservation
    {
        public IReadOnlyList<ObservedCard> Hand { get; }
        public IReadOnlyList<ObservedCard> Board { get; }
        public int DrawCount { get; }
        public int DiscardCount { get; }
        public int Life { get; }
        public int Bullets { get; }
        public int Score { get; }

        internal FighterObservation(BattleState state, Side owner, Side observer)
        {
            FighterState fighter = state.Get(owner);
            var hand = new List<ObservedCard>();
            foreach (int id in fighter.Hand)
            {
                CardRuntime card = state.Cards[id];
                hand.Add(new ObservedCard(card, owner == observer || card.IsRevealed));
            }
            var board = new ObservedCard[BasicRules.Slots];
            for (int i = 0; i < board.Length; i++)
            {
                int id = fighter.Board[i];
                if (id != 0)
                {
                    CardRuntime card = state.Cards[id];
                    board[i] = new ObservedCard(card, owner == observer || card.IsRevealed);
                }
            }
            Hand = hand.AsReadOnly();
            Board = Array.AsReadOnly(board);
            DrawCount = fighter.DrawPile.Count;
            DiscardCount = fighter.DiscardPile.Count;
            Life = fighter.Life;
            Bullets = fighter.Bullets;
            Score = fighter.Score;
        }
    }

    public sealed class BattleObservation
    {
        public Side Observer { get; }
        public BattlePhase Phase { get; }
        public int Round { get; }
        public int RevealStep { get; }
        public int Chips { get; }
        public int RewardChips { get; }
        public bool PlayerWon { get; }
        public FighterObservation Player { get; }
        public FighterObservation Enemy { get; }
        public IReadOnlyCollection<int> ForbiddenCards { get; }

        internal BattleObservation(BattleState state, Side observer)
        {
            Observer = observer;
            Phase = state.Phase;
            Round = state.Round;
            RevealStep = state.RevealStep;
            Chips = state.Chips;
            RewardChips = state.RewardChips;
            PlayerWon = state.PlayerWon;
            Player = new FighterObservation(state, Side.Player, observer);
            Enemy = new FighterObservation(state, Side.Enemy, observer);
            ForbiddenCards = new List<int>(state.ForbiddenCards).AsReadOnly();
        }

        private BattleObservation(BattleObservation source, IEnumerable<int> forbiddenCards)
        {
            Observer = source.Observer;
            Phase = source.Phase;
            Round = source.Round;
            RevealStep = source.RevealStep;
            Chips = source.Chips;
            RewardChips = source.RewardChips;
            PlayerWon = source.PlayerWon;
            Player = source.Player;
            Enemy = source.Enemy;
            ForbiddenCards = new List<int>(forbiddenCards).AsReadOnly();
        }

        // Reuse the immutable observations; only the action restrictions may change.
        internal BattleObservation WithForbiddenCards(IEnumerable<int> forbiddenCards)
            => new BattleObservation(this, forbiddenCards);

        public FighterObservation Get(Side side) => side == Side.Player ? Player : Enemy;
    }
}
