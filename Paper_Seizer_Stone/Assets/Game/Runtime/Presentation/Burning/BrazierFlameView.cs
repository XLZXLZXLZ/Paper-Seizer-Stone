using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BrazierFlameView : MonoBehaviour
    {
        [SerializeField] private MeshRenderer flames;
        [SerializeField] private MeshRenderer sparks;
        [SerializeField] private Light fireLight;
        [Header("持续燃烧")]
        [SerializeField, Min(0.01f)] private float flameWidth = 0.65f;
        [SerializeField, Min(0.01f)] private float flameHeight = 0.52f;
        [SerializeField, Min(0.01f)] private float flowSpeed = 1.8f;
        [SerializeField, Min(0)] private float brightness = 2.8f;
        [SerializeField, Min(0)] private float lightIntensity = 2.4f;
        [SerializeField, Min(0.1f)] private float lightRange = 2f;
        [SerializeField, Range(0, 1)] private float lightFlicker = 0.25f;
        [Header("烧牌时火势增强")]
        [SerializeField, Min(0)] private float burnBoost = 0.8f;
        private MaterialPropertyBlock flameProperties, sparkProperties;
        private float pulseStart = float.NegativeInfinity;
        private float pulseSeconds = 0.5f;
        private float pulseStrength;

        public void Pulse(float strength, float seconds)
        {
            pulseStrength = Mathf.Max(0, strength);
            pulseSeconds = Mathf.Max(0.05f, seconds);
            pulseStart = Time.unscaledTime;
        }

        public void ResetPulse()
        {
            pulseStart = float.NegativeInfinity;
            pulseStrength = 0;
        }

        private void OnEnable()
        {
            ResetPulse();
            // The shader moves vertices outside the small authored spark quads.
            if (flames != null) flames.localBounds = new Bounds(new Vector3(0, 0.3f, 0), Vector3.one);
            if (sparks != null) sparks.localBounds = new Bounds(new Vector3(0, 0.4f, 0), new Vector3(1, 1.2f, 1));
            Update();
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - pulseStart) / pulseSeconds);
            float boost = burnBoost * pulseStrength * (1f - Mathf.SmoothStep(0, 1, t));
            if (flames != null)
            {
                flames.transform.localScale = new Vector3(flameWidth / 0.65f,
                    flameHeight / 0.52f, flameWidth / 0.65f);
                if (flameProperties == null) flameProperties = new MaterialPropertyBlock();
                flameProperties.SetFloat("_Boost", boost);
                flameProperties.SetFloat("_Speed", flowSpeed);
                flameProperties.SetFloat("_Intensity", brightness);
                flames.SetPropertyBlock(flameProperties);
            }
            if (sparks != null)
            {
                sparks.transform.localScale = new Vector3(flameWidth / 0.65f, flameHeight / 0.52f, flameWidth / 0.65f);
                if (sparkProperties == null) sparkProperties = new MaterialPropertyBlock();
                sparkProperties.SetFloat("_Boost", boost);
                sparkProperties.SetFloat("_Speed", flowSpeed * 0.55f);
                sparkProperties.SetFloat("_Intensity", brightness * 0.7f);
                sparks.SetPropertyBlock(sparkProperties);
            }
            if (fireLight != null)
            {
                float flicker = Application.isPlaying
                    ? Mathf.PerlinNoise(Time.realtimeSinceStartup * 8f, 0.73f) * 2f - 1f : 0f;
                fireLight.intensity = lightIntensity * (1f + lightFlicker * flicker) * (1f + boost);
                fireLight.range = lightRange;
            }
        }

        private void OnDisable() { ResetPulse(); }
    }
}
