namespace PaperSeizerStone.Battle
{
    public enum CardTag { None, Violence, Risk, Conservative, Cathode, Balance }

    public static class CardTags
    {
        public static int VisualSeed(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (value != null) foreach (char c in value) hash = (hash ^ c) * 16777619;
                return (int)(hash % 100003);
            }
        }

        public static bool IsEngraving(CardTag tag) => tag >= CardTag.Violence && tag <= CardTag.Balance;

        public static int ScoreModifier(CardTag tag, int result)
        {
            switch (tag)
            {
                case CardTag.Risk: return result > 0 ? 2 : result < 0 ? -1 : 0;
                case CardTag.Conservative: return result > 0 ? -1 : result < 0 ? 2 : 0;
                case CardTag.Cathode: return result > 0 ? -4 : result < 0 ? 4 : 0;
                case CardTag.Balance: return result > 0 ? -1 : result == 0 ? 2 : 0;
                default: return 0;
            }
        }
    }
}
