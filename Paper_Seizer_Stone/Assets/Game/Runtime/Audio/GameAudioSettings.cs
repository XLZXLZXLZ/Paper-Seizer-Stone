using System;
using UnityEngine;

namespace PaperSeizerStone.Audio
{
    public enum AudioCategory { Music, Effects }

    public static class GameAudioSettings
    {
        private const string Prefix = "PaperSeizerStone.Audio.";
        public static event Action Changed;
        public static float Master { get; private set; }
        public static float Music { get; private set; }
        public static float Effects { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            Changed = null;
            Master = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Master", 1));
            Music = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Music", 0.8f));
            Effects = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Effects", 1));
            AudioListener.volume = Master;
        }

        public static void SetMaster(float value)
        {
            Master = Mathf.Clamp01(value);
            AudioListener.volume = Master;
            PlayerPrefs.SetFloat(Prefix + "Master", Master);
            Changed?.Invoke();
        }

        public static void SetMusic(float value)
        {
            Music = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Prefix + "Music", Music);
            Changed?.Invoke();
        }

        public static void SetEffects(float value)
        {
            Effects = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(Prefix + "Effects", Effects);
            Changed?.Invoke();
        }

        public static void Save() => PlayerPrefs.Save();
    }
}
