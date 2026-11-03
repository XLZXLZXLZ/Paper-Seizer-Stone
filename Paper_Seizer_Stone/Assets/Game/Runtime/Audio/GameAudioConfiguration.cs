using System;
using UnityEngine;

namespace PaperSeizerStone.Audio
{
    [Serializable]
    public sealed class SoundCueConfiguration
    {
        public AudioCue cue;
        public string description;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Range(0, 1)] public float volume = 0.7f;
        public Vector2 pitchRange = Vector2.one;
        [Min(0)] public float minimumInterval = 0.06f;
        [Range(1, 12)] public int maximumVoices = 2;
        [Range(0, 256), Tooltip("数值越小优先级越高；枪声可抢占普通牌声。")] public int priority = 128;
    }

    [Serializable]
    public sealed class MusicConfiguration
    {
        public MusicContext context;
        public AudioClip clip;
        [Range(0, 1)] public float volume = 0.3f;
    }

    // Authored asset; playback never changes this shared configuration.
    public sealed class GameAudioConfiguration : ScriptableObject
    {
        [Header("背景音乐")]
        public MusicConfiguration[] music = Array.Empty<MusicConfiguration>();
        [Min(0.01f)] public float musicFadeSeconds = 1.2f;
        [Range(0, 1), Tooltip("射击演出时保留的音乐比例。")] public float shootingMusicMultiplier = 0.25f;
        [Min(0.01f)] public float duckFadeSeconds = 0.2f;
        [Header("关键音效")]
        public SoundCueConfiguration[] sounds = Array.Empty<SoundCueConfiguration>();

        public MusicConfiguration FindMusic(MusicContext context)
        {
            foreach (MusicConfiguration entry in music)
                if (entry != null && entry.context == context) return entry;
            return null;
        }
    }
}
