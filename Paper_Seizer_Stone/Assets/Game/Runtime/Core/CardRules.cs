namespace PaperSeizerStone.Battle
{
    // Used by both the authoritative engine and the enemy's filtered snapshot evaluation.
    public static class CardRules
    {
        public static int Compare(CardSpec a, CardSpec b)
        {
            if (a.Id == b.Id) return 0;
            if (a.Ability == CardAbility.Hammer && b.Ability == CardAbility.Hammer) return 0;
            if (a.Ability == CardAbility.Hammer && (b.Gesture == Gesture.Rock || b.Gesture == Gesture.Scissors)) return 1;
            if (b.Ability == CardAbility.Hammer && (a.Gesture == Gesture.Rock || a.Gesture == Gesture.Scissors)) return -1;
            if (a.Ability == CardAbility.WoodenCrate && b.Gesture == Gesture.Paper && b.Ability != CardAbility.None) return 0;
            if (b.Ability == CardAbility.WoodenCrate && a.Gesture == Gesture.Paper && a.Ability != CardAbility.None) return 0;
            return BasicRules.Compare(a.Gesture, b.Gesture);
        }

        public static int Points(CardSpec card, int result)
        {
            return result > 0 ? 2 : result == 0 ? 1 : card.Ability == CardAbility.FlowerScissors ? 1 : 0;
        }
    }
}
