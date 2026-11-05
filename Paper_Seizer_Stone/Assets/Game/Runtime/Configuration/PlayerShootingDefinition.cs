using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public sealed class PlayerShootingDefinition : ScriptableObject
    {
        [SerializeField] private AnimationClip characterClip;
        [SerializeField] private AnimationClip gunClip;
        [SerializeField] private AnimationClip loadingBulletClip;
        [SerializeField, Min(0)] private float gunStart = 1f / 3f;
        [SerializeField, Min(0)] private float bulletStart = 3f;
        [SerializeField, Tooltip("击发时刻，单位秒；负数使用人物动作末帧。")] private float fireMoment = -1f;
        [SerializeField, Min(0)] private float enterBlend = 0.12f;
        [SerializeField, Min(0)] private float returnBlend = 0.18f;
        [SerializeField, Min(0)] private float postShotHold = 0f;
        [Header("机械音效时间（人物动画秒数，负数自动跟随动作轨）")]
        [SerializeField] private float openSoundTime = -1f;
        [SerializeField] private float loadSoundTime = -1f;
        [SerializeField] private float closeSoundTime = -1f;
        [SerializeField] private float cockSoundTime = -1f;

        public bool IsConfigured => HasDuration(characterClip) && HasDuration(gunClip) && HasDuration(loadingBulletClip)
            && IsFinite(gunStart) && IsFinite(bulletStart) && IsFinite(fireMoment)
            && IsFinite(enterBlend) && IsFinite(returnBlend) && IsFinite(postShotHold);
        public AnimationClip CharacterClip => characterClip;
        public AnimationClip GunClip => gunClip;
        public AnimationClip LoadingBulletClip => loadingBulletClip;
        public float GunStart => Mathf.Max(0, gunStart);
        public float BulletStart => Mathf.Max(0, bulletStart);
        public float Duration => Mathf.Max(Length(characterClip), Mathf.Max(GunStart + Length(gunClip), BulletStart + Length(loadingBulletClip)));
        public float FireTime => Mathf.Clamp(fireMoment < 0 ? Length(characterClip) : fireMoment, 0, Duration);
        public float EnterBlend => Mathf.Max(0, enterBlend);
        public float ReturnBlend => Mathf.Max(0, returnBlend);
        public float PostShotHold => Mathf.Max(0, postShotHold);
        public float OpenSoundTime => Mathf.Clamp(openSoundTime < 0 ? GunStart : openSoundTime, 0, FireTime);
        public float LoadSoundTime => Mathf.Clamp(loadSoundTime < 0 ? BulletStart : loadSoundTime, OpenSoundTime, FireTime);
        public float CloseSoundTime => Mathf.Clamp(closeSoundTime < 0 ? BulletStart + Length(loadingBulletClip) : closeSoundTime, LoadSoundTime, FireTime);
        public float CockSoundTime => Mathf.Clamp(cockSoundTime < 0 ? FireTime - 0.2f : cockSoundTime, CloseSoundTime, FireTime);

        private static float Length(AnimationClip clip) => clip != null ? clip.length : 0;
        private static bool HasDuration(AnimationClip clip) => clip != null && clip.length > 0 && IsFinite(clip.length);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
