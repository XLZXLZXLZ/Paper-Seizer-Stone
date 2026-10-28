using System;

namespace PaperSeizerStone.Battle
{
    public enum Side { Player, Enemy }
    public enum Gesture { Rock, Paper, Scissors }
    // Keep serialized values and legacy identifiers: WoodenCrate = 柏木棒, Pliers = 刀子,
    // Fork = 三叉戟. Fork and Silk now arm the card-face trailing-score effect on reveal.
    public enum CardAbility { None, Hammer, Glass, ShatteredGlass, WoodenCrate, FlowerScissors, Pliers, Fork, Silk, GunpowderPaper }
    public enum BattlePhase { Created, Preparing, Planning, Revealing, PlayerCheat, EnemyCheat, Scoring, Shooting, RoundEnd, BattleEnd }

    // Immutable rule data, detached from Unity assets.
    public sealed class CardSpec
    {
        public string Id { get; }
        public Gesture Gesture { get; }
        public CardAbility Ability { get; }
        public string TransformId { get; }
        public string Name { get; }

        public CardSpec(string id, Gesture gesture, CardAbility ability = CardAbility.None, string transformId = null, string name = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("卡牌必须配置非空的稳定标识。");
            if (!Enum.IsDefined(typeof(Gesture), gesture)) throw new ArgumentOutOfRangeException(nameof(gesture));
            Id = id;
            Gesture = gesture;
            Ability = ability;
            TransformId = transformId;
            Name = name ?? id;
        }
    }

    // One owned card in an adventure. Two copies of Rock have different record IDs.
    [Serializable]
    public sealed class DeckCardRecord
    {
        public string RecordId;
        public string DefinitionId;

        public DeckCardRecord(string recordId, string definitionId)
        {
            RecordId = recordId;
            DefinitionId = definitionId;
        }
    }

    // A single instance survives draws, swaps, discards and reshuffles for the whole battle.
    // Position belongs exclusively to the zone containers in BattleState.
    public sealed class CardRuntime
    {
        public int InstanceId { get; }
        public string SourceRecordId { get; }
        public Side OriginalSide { get; }
        public CardSpec Definition { get; internal set; }
        public bool IsRevealed { get; internal set; }
        public bool RevealedThisRound { get; internal set; }

        internal CardRuntime(int id, DeckCardRecord record, CardSpec definition, Side side)
        {
            InstanceId = id;
            SourceRecordId = record.RecordId;
            Definition = definition;
            OriginalSide = side;
        }
    }

    public static class BasicRules
    {
        public const int Slots = 4;
        public const int Chambers = 6;
        public static Side Other(Side side) => side == Side.Player ? Side.Enemy : Side.Player;

        public static int Compare(Gesture a, Gesture b)
        {
            if (a == b) return 0;
            return (a == Gesture.Rock && b == Gesture.Scissors)
                || (a == Gesture.Scissors && b == Gesture.Paper)
                || (a == Gesture.Paper && b == Gesture.Rock) ? 1 : -1;
        }

        public static int Points(Gesture a, Gesture b)
        {
            int result = Compare(a, b);
            return result > 0 ? 2 : result == 0 ? 1 : 0;
        }
    }
}
