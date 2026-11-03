using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PaperSeizerStone.Audio
{
    [DefaultExecutionOrder(-500), DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        [SerializeField] private GameAudioConfiguration configuration;
        [SerializeField] private AudioSource musicA;
        [SerializeField] private AudioSource musicB;
        [SerializeField] private AudioSource[] effectSources;
        public static AudioManager Instance { get; private set; }
        public GameAudioConfiguration Configuration => configuration;

        private sealed class CueState
        {
            public SoundCueConfiguration Configuration;
            public double LastPlayed = double.NegativeInfinity;
            public int LastClip = -1;
        }

        private readonly Dictionary<AudioCue, CueState> cues = new Dictionary<AudioCue, CueState>();
        // Audio variation must never consume the rule engine or Unity's shared random sequence.
        private System.Random random;
        private AudioSource[] musicSources;
        private AudioCue[] voiceCues;
        private float[] voiceVolumes;
        private readonly float[] levels = new float[2];
        private readonly float[] fadeFrom = new float[2];
        private readonly float[] fadeTo = new float[2];
        private int activeMusic;
        private float fadeProgress = 1;
        private float duck = 1;
        private bool shootingFocus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            if (configuration == null || musicA == null || musicB == null || effectSources == null
                || effectSources.Length == 0 || Array.Exists(effectSources, source => source == null))
            {
                Debug.LogError("AudioManager 预制体缺少配置、音乐音源或音效音源。", this);
                enabled = false;
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            random = new System.Random(unchecked(Environment.TickCount + GetInstanceID()));
            musicSources = new[] { musicA, musicB };
            voiceCues = new AudioCue[effectSources.Length];
            voiceVolumes = new float[effectSources.Length];
            foreach (SoundCueConfiguration entry in configuration.sounds)
                if (entry != null) cues[entry.cue] = new CueState { Configuration = entry };
            foreach (AudioSource source in musicSources) { source.Stop(); source.volume = 0; }
            foreach (AudioSource source in effectSources) source.Stop();
            GameAudioSettings.Changed += ApplyVolumes;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private void Start()
        {
            if (Instance == this) ChangeMusic(ContextFor(SceneManager.GetActiveScene()));
        }

        private static MusicContext ContextFor(Scene scene) =>
            scene.name == "MainMenu" ? MusicContext.Menu : MusicContext.Battle;

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            shootingFocus = false;
            foreach (AudioSource source in effectSources) source.Stop();
            ChangeMusic(ContextFor(scene));
        }

        public static void Play(AudioCue cue)
        {
            if (Instance != null && Instance.isActiveAndEnabled) Instance.PlayCue(cue);
        }

        public static void SetMusic(MusicContext context)
        {
            if (Instance != null && Instance.isActiveAndEnabled) Instance.ChangeMusic(context);
        }

        public static void SetShootingFocus(bool value)
        {
            if (Instance != null) Instance.shootingFocus = value;
        }

        private void PlayCue(AudioCue cue)
        {
            if (!cues.TryGetValue(cue, out CueState state)) return;
            SoundCueConfiguration entry = state.Configuration;
            if (entry.clips == null || entry.clips.Length == 0
                || AudioSettings.dspTime - state.LastPlayed < entry.minimumInterval) return;
            int count = 0;
            int free = -1;
            int replace = -1;
            for (int i = 0; i < effectSources.Length; i++)
            {
                AudioSource source = effectSources[i];
                if (!source.isPlaying) { if (free < 0) free = i; continue; }
                if (voiceCues[i] == cue) count++;
                if (replace < 0 || source.priority > effectSources[replace].priority) replace = i;
            }
            if (count >= Mathf.Max(1, entry.maximumVoices)) return;
            if (free < 0)
            {
                if (replace < 0 || entry.priority > effectSources[replace].priority) return;
                free = replace;
            }
            int clipIndex = random.Next(entry.clips.Length);
            if (entry.clips.Length > 1 && clipIndex == state.LastClip)
                clipIndex = (clipIndex + 1 + random.Next(entry.clips.Length - 1)) % entry.clips.Length;
            AudioClip clip = entry.clips[clipIndex];
            if (clip == null) return;
            AudioSource voice = effectSources[free];
            voice.Stop();
            voice.clip = clip;
            voice.loop = false;
            voice.priority = entry.priority;
            float low = Mathf.Max(0.1f, Mathf.Min(entry.pitchRange.x, entry.pitchRange.y));
            float high = Mathf.Max(low, Mathf.Max(entry.pitchRange.x, entry.pitchRange.y));
            voice.pitch = Mathf.Lerp(low, high, (float)random.NextDouble());
            voiceCues[free] = cue;
            voiceVolumes[free] = Mathf.Clamp01(entry.volume);
            voice.volume = voiceVolumes[free] * GameAudioSettings.Effects;
            voice.Play();
            state.LastPlayed = AudioSettings.dspTime;
            state.LastClip = clipIndex;
        }

        private void ChangeMusic(MusicContext context)
        {
            MusicConfiguration entry = configuration.FindMusic(context);
            if (entry == null) return;
            int next = activeMusic;
            if (entry.clip != null && (musicSources[next].clip != entry.clip || !musicSources[next].isPlaying))
            {
                next = 1 - activeMusic;
                AudioSource source = musicSources[next];
                source.Stop();
                source.clip = entry.clip;
                source.loop = true;
                levels[next] = 0;
                source.volume = 0;
                source.Play();
            }
            activeMusic = next;
            for (int i = 0; i < 2; i++)
            {
                fadeFrom[i] = levels[i];
                fadeTo[i] = i == next && entry.clip != null ? Mathf.Clamp01(entry.volume) : 0;
            }
            fadeProgress = 0;
        }

        private void Update()
        {
            if (Instance != this) return;
            fadeProgress = Mathf.Min(1, fadeProgress + Time.unscaledDeltaTime / Mathf.Max(0.01f, configuration.musicFadeSeconds));
            float weight = Mathf.SmoothStep(0, 1, fadeProgress);
            for (int i = 0; i < 2; i++)
            {
                levels[i] = Mathf.Lerp(fadeFrom[i], fadeTo[i], weight);
                if (fadeProgress >= 1 && fadeTo[i] == 0 && musicSources[i].isPlaying) musicSources[i].Stop();
            }
            duck = Mathf.MoveTowards(duck, shootingFocus ? configuration.shootingMusicMultiplier : 1,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, configuration.duckFadeSeconds));
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            for (int i = 0; i < 2; i++) musicSources[i].volume = levels[i] * duck * GameAudioSettings.Music;
            for (int i = 0; i < effectSources.Length; i++)
                if (effectSources[i].isPlaying) effectSources[i].volume = voiceVolumes[i] * GameAudioSettings.Effects;
        }

        private void OnApplicationPause(bool paused) { if (paused && Instance == this) GameAudioSettings.Save(); }
        private void OnApplicationQuit() { if (Instance == this) GameAudioSettings.Save(); }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= SceneLoaded;
            GameAudioSettings.Changed -= ApplyVolumes;
            Instance = null;
        }
    }
}
