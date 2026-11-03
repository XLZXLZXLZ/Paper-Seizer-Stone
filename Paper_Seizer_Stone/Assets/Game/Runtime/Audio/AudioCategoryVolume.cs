using UnityEngine;

namespace PaperSeizerStone.Audio
{
    // Add to music/effect sources authored in a scene or prefab. Master uses AudioListener.
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioCategoryVolume : MonoBehaviour
    {
        [SerializeField] private AudioCategory category = AudioCategory.Music;
        [SerializeField, Range(0, 1)] private float baseVolume = 1;
        private AudioSource source;

        private void OnEnable()
        {
            source = GetComponent<AudioSource>();
            GameAudioSettings.Changed += Apply;
            Apply();
        }

        private void OnDisable() => GameAudioSettings.Changed -= Apply;
        private void Apply() => source.volume = baseVolume *
            (category == AudioCategory.Music ? GameAudioSettings.Music : GameAudioSettings.Effects);
    }
}
