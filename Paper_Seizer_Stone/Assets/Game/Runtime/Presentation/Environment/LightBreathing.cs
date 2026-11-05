using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Light))]
    public sealed class LightBreathing : MonoBehaviour
    {
        [Tooltip("相对灯光原始强度的最大变化比例")]
        [SerializeField, Range(0f, 1f)] private float amplitude = 0.05f;
        [Tooltip("每次随机强度变化的过渡时长范围（秒）")]
        [SerializeField] private Vector2 transitionSeconds = new Vector2(1.8f, 3.8f);

        private Light targetLight;
        // Keep ambient animation independent of gameplay's UnityEngine.Random state.
        private System.Random random;
        private float baseIntensity;
        private float startIntensity;
        private float targetIntensity;
        private float elapsed;
        private float duration;

        private void OnEnable()
        {
            targetLight = GetComponent<Light>();
            random = random ?? new System.Random();
            baseIntensity = targetLight.intensity;
            SelectTarget();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            targetLight.intensity = Mathf.Lerp(startIntensity, targetIntensity, progress);
            if (elapsed >= duration) SelectTarget();
        }

        private void SelectTarget()
        {
            startIntensity = targetLight.intensity;
            targetIntensity = Mathf.Max(0f, baseIntensity *
                (1f + ((float)random.NextDouble() * 2f - 1f) * amplitude));
            float minimum = Mathf.Max(0.1f, transitionSeconds.x);
            float maximum = Mathf.Max(minimum, transitionSeconds.y);
            duration = Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
            elapsed = 0f;
        }

        private void OnDisable()
        {
            if (targetLight != null) targetLight.intensity = baseIntensity;
        }
    }
}
