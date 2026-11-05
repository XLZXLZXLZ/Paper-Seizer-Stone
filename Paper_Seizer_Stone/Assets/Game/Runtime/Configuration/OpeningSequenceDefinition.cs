using UnityEngine;

namespace PaperSeizerStone.Configuration
{
    public sealed class OpeningSequenceDefinition : ScriptableObject
    {
        [Tooltip("每次开始冒险时，在首场战斗播放苏醒开场。")]
        public bool playOpening = true;
        [Min(0.1f), Tooltip("睁眼与对焦动画时长，不含场景布景等待，单位秒。")]
        public float duration = 5f;
        [Min(0.1f), Tooltip("主菜单开始游戏时的闭眼时长，单位秒。")]
        public float closeDuration = 0.8f;
        [Tooltip("横轴为0～1的播放进度，纵轴为0～1的睁眼程度。")]
        public AnimationCurve eyelidOpening = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 0f), new Keyframe(0.3f, 0.18f),
            new Keyframe(0.3375f, 0f), new Keyframe(0.375f, 0f),
            new Keyframe(0.7f, 1f), new Keyframe(1f, 1f));
        [Tooltip("横轴为播放进度，纵轴为模糊比例；与眼睑共用时间轴。")]
        public AnimationCurve defocus = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.3f, 0.9f), new Keyframe(0.375f, 1f),
            new Keyframe(0.7f, 0.35f), new Keyframe(0.86f, 0.12f), new Keyframe(1f, 0f));
        [Range(0, 40), Tooltip("1080p参考分辨率下的最大模糊半径。")]
        public float maximumBlurPixels = 18f;
        [Range(0.5f, 0.8f), Tooltip("上眼睑承担的开合比例。")]
        public float upperLidShare = 0.65f;
        [Range(0, 0.35f), Tooltip("眼睑在画面左右两侧的弧度。")]
        public float curvature = 0.2f;
        [Range(1, 80), Tooltip("1080p参考分辨率下眼睑边缘的柔化宽度。")]
        public float edgeSoftnessPixels = 24f;
        public Color lidColor = new Color(0.008f, 0.006f, 0.005f, 1f);

        public float Duration => Mathf.Max(0.1f, duration);
    }
}
