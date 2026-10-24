using System.Collections.Generic;

namespace PaperSeizerStone.Battle
{
    public enum CheatKind { Pass, SwapBoard, SwapHand, Draw }

    public sealed class CheatAction
    {
        public CheatKind Kind { get; }
        public int FirstCard { get; }
        public int SecondCard { get; }

        public CheatAction(CheatKind kind, int firstCard = 0, int secondCard = 0)
        {
            Kind = kind;
            FirstCard = firstCard;
            SecondCard = secondCard;
        }
    }

    public enum BattleEventKind
    {
        RoundStarted, DeckShuffled, CardDrawn, LayoutCommitted, CardRevealed,
        CardsSwapped, AmmunitionChanged, DecisionPassed, PhaseChanged,
        LaneScored, ShotResolved, RoundEnded, BattleEnded, AbilityTriggered, CardTransformed
    }

    public sealed class BattleEvent
    {
        public BattleEventKind Kind { get; }
        public Side Side { get; }
        public int CardId { get; }
        public int Lane { get; }
        public bool Hit { get; }
        public string Message { get; }
        public BattleObservation Snapshot { get; }

        internal BattleEvent(BattleState state, BattleEventKind kind, Side side, string message,
            int cardId = 0, int lane = -1, bool hit = false)
        {
            Kind = kind;
            Side = side;
            CardId = cardId;
            Lane = lane;
            Hit = hit;
            Message = message;
            Snapshot = new BattleObservation(state, Side.Player);
        }
    }

    public sealed class ActionResult
    {
        public bool Success { get; }
        public string Error { get; }
        public IReadOnlyList<BattleEvent> Events { get; }

        private ActionResult(bool success, string error, List<BattleEvent> events)
        {
            Success = success;
            Error = error;
            Events = events.AsReadOnly();
        }

        internal static ActionResult Ok(List<BattleEvent> events) => new ActionResult(true, null, events);
        internal static ActionResult Fail(string error) => new ActionResult(false, error, new List<BattleEvent>());
    }
}
