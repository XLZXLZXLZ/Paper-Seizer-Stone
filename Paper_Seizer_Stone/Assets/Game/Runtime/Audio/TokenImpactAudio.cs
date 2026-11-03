using UnityEngine;

namespace PaperSeizerStone.Audio
{
    [DisallowMultipleComponent]
    public sealed class TokenImpactAudio : MonoBehaviour
    {
        [SerializeField] private AudioCue cue = AudioCue.ChipDrop;
        [SerializeField, Min(0)] private float minimumSpeed = 0.35f;
        private bool played;

        private void OnEnable() => played = false;

        private void OnCollisionEnter(Collision collision)
        {
            // One sound per physical deal, not on every bounce or resting contact.
            if (played || collision.relativeVelocity.sqrMagnitude < minimumSpeed * minimumSpeed) return;
            played = true;
            AudioManager.Play(cue);
        }
    }
}
