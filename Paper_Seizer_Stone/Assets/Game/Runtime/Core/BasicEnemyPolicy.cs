using System;
using System.Collections.Generic;

namespace PaperSeizerStone.Battle
{
    // Deliberately accepts only a filtered observation; cannot access BattleState or hidden definitions.
    public sealed class BasicEnemyPolicy
    {
        private readonly Random random;
        public BasicEnemyPolicy(int seed) { random = new Random(seed); }

        public int[] ChooseLayout(BattleObservation observation)
        {
            var ids = new List<int>();
            foreach (ObservedCard card in observation.Get(observation.Observer).Hand) ids.Add(card.Id);
            for (int i = ids.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int value = ids[i]; ids[i] = ids[j]; ids[j] = value;
            }
            return ids.GetRange(0, BasicRules.Slots).ToArray();
        }

        private static float Value(Gesture? own, Gesture? opponent)
        {
            // Unknown cards contribute neutral expected score difference, never their real gesture.
            return own.HasValue && opponent.HasValue ? BasicRules.Compare(own.Value, opponent.Value) * 2 : 0;
        }

        public CheatAction ChooseCheat(BattleObservation observation)
        {
            FighterObservation own = observation.Get(observation.Observer);
            FighterObservation opponent = observation.Get(BasicRules.Other(observation.Observer));
            var forbidden = new HashSet<int>(observation.ForbiddenCards);
            var best = new CheatAction(CheatKind.Pass);
            float bestGain = 0.25f;
            if (opponent.Bullets >= BasicRules.Chambers) return best;
            for (int a = 0; a < BasicRules.Slots; a++)
            {
                ObservedCard first = own.Board[a];
                if (first == null || forbidden.Contains(first.Id)) continue;
                for (int b = a + 1; b < BasicRules.Slots; b++)
                {
                    ObservedCard second = own.Board[b];
                    if (second == null || forbidden.Contains(second.Id)) continue;
                    float gain = Value(second.Gesture, opponent.Board[a]?.Gesture) + Value(first.Gesture, opponent.Board[b]?.Gesture)
                        - Value(first.Gesture, opponent.Board[a]?.Gesture) - Value(second.Gesture, opponent.Board[b]?.Gesture);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = new CheatAction(CheatKind.SwapBoard, first.Id, second.Id);
                    }
                }
                foreach (ObservedCard hand in own.Hand)
                {
                    if (forbidden.Contains(hand.Id)) continue;
                    float gain = Value(hand.Gesture, opponent.Board[a]?.Gesture) - Value(first.Gesture, opponent.Board[a]?.Gesture);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = new CheatAction(CheatKind.SwapHand, first.Id, hand.Id);
                    }
                }
            }
            if (best.Kind == CheatKind.Pass && opponent.Bullets <= 4 && own.DrawCount + own.DiscardCount > 0
                && own.Hand.Count < 4 && random.NextDouble() < 0.18)
                return new CheatAction(CheatKind.Draw);
            return best;
        }
    }
}
