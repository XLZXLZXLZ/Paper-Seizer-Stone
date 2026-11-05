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

        private static float Value(ObservedCard ownCard, ObservedCard opponentCard,
            FighterObservation ownFighter, FighterObservation opponentFighter, bool afterSwap = false)
        {
            CardSpec own = afterSwap ? AfterSwap(ownCard?.Spec) : ownCard?.Spec;
            CardSpec opponent = opponentCard?.Spec;
            // Tags, like identities, come only from the filtered observation.
            bool ownMoved = afterSwap || (ownCard != null && ownCard.MovedByCheatThisRound);
            bool opponentMoved = opponentCard != null && opponentCard.MovedByCheatThisRound;
            if (own == null || opponent == null)
            {
                // A known lead weight loses its fixed bonus even when its opposing card is hidden.
                return (own != null && own.Ability == CardAbility.LeadWeight && !ownMoved ? 1 : 0)
                    - (opponent != null && opponent.Ability == CardAbility.LeadWeight && !opponentMoved ? 1 : 0);
            }
            int result = CardRules.Compare(own, opponent);
            return CardRules.Points(own, result, ownCard.Tag, ownMoved)
                - CardRules.Points(opponent, -result, opponentCard.Tag, opponentMoved)
                + OutcomeValue(own, result, ownFighter, opponentFighter)
                - OutcomeValue(opponent, -result, opponentFighter, ownFighter);
        }

        private static float OutcomeValue(CardSpec card, int result, FighterObservation own, FighterObservation opponent)
        {
            if (card.Ability == CardAbility.Sickle && result > 0 && own.DrawCount + own.DiscardCount > 0) return 0.5f;
            if (card.Ability == CardAbility.Oilcloth && result == 0 && opponent.Bullets > 1) return 0.5f;
            if (card.Ability == CardAbility.Pyrite && result > 0) return 0.3f;
            if (card.Ability == CardAbility.TicketPunch && result < 0) return 0.15f;
            return 0;
        }

        private static float SwapRisk(ObservedCard first, ObservedCard second)
        {
            return first.SwapDiscountAvailable || second.SwapDiscountAvailable ? 0 : 0.15f;
        }

        private static float ContractValue(FighterObservation own)
        {
            if (own.HasCheatedThisRound) return 0;
            float value = 0;
            foreach (ObservedCard card in own.Board)
                if (card != null && card.Spec != null && card.Spec.Ability == CardAbility.ContractPaper) value += 0.3f;
            return value;
        }

        private static CardSpec AfterSwap(CardSpec card)
        {
            return card != null && card.Ability == CardAbility.Glass
                ? new CardSpec(card.TransformId, Gesture.Scissors, CardAbility.ShatteredGlass) : card;
        }

        public CheatAction ChooseCheat(BattleObservation observation)
        {
            FighterObservation own = observation.Get(observation.Observer);
            FighterObservation opponent = observation.Get(BasicRules.Other(observation.Observer));
            var forbidden = new HashSet<int>(observation.ForbiddenCards);
            var best = new CheatAction(CheatKind.Pass);
            float bestGain = 0.25f;
            if (opponent.Bullets >= BasicRules.Chambers) return best;
            float contractCost = ContractValue(own);
            for (int a = 0; a < BasicRules.Slots; a++)
            {
                ObservedCard first = own.Board[a];
                if (first == null || forbidden.Contains(first.Id)) continue;
                for (int b = a + 1; b < BasicRules.Slots; b++)
                {
                    ObservedCard second = own.Board[b];
                    if (second == null || forbidden.Contains(second.Id)) continue;
                    float gain = Value(second, opponent.Board[a], own, opponent, true)
                        + Value(first, opponent.Board[b], own, opponent, true)
                        - Value(first, opponent.Board[a], own, opponent)
                        - Value(second, opponent.Board[b], own, opponent)
                        - SwapRisk(first, second) - contractCost;
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = new CheatAction(CheatKind.SwapBoard, first.Id, second.Id);
                    }
                }
                foreach (ObservedCard hand in own.Hand)
                {
                    if (forbidden.Contains(hand.Id)) continue;
                    float gain = Value(hand, opponent.Board[a], own, opponent, true)
                        - Value(first, opponent.Board[a], own, opponent) - SwapRisk(first, hand) - contractCost;
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = new CheatAction(CheatKind.SwapHand, first.Id, hand.Id);
                    }
                }
            }
            if (best.Kind == CheatKind.Pass && opponent.Bullets <= 4 && own.DrawCount + own.DiscardCount > 0
                && own.Hand.Count < 4 && contractCost == 0 && random.NextDouble() < 0.18)
                return new CheatAction(CheatKind.Draw);
            return best;
        }
    }
}

