using System;
using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public static class GameDisplaySettings
    {
        private const string Prefix = "PaperSeizerStone.Display.";
        public static event Action Changed;
        public static bool TransparentPlayerCards { get; private set; }
        public static bool DistinctCardBacks { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Changed = null;
            TransparentPlayerCards = PlayerPrefs.GetInt(Prefix + "TransparentPlayerCards", 0) != 0;
            DistinctCardBacks = PlayerPrefs.GetInt(Prefix + "DistinctCardBacks", 0) != 0;
        }

        public static void SetTransparentPlayerCards(bool value)
        {
            if (TransparentPlayerCards == value) return;
            TransparentPlayerCards = value;
            Save("TransparentPlayerCards", value);
        }

        public static void SetDistinctCardBacks(bool value)
        {
            if (DistinctCardBacks == value) return;
            DistinctCardBacks = value;
            Save("DistinctCardBacks", value);
        }

        private static void Save(string key, bool value)
        {
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
