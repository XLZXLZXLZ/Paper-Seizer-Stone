using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaperSeizerStone.Presentation.MainMenu
{
    [Serializable]
    public sealed class MenuHoverLabelSettings
    {
        [Min(0.05f), Tooltip("从隐藏到完整显示的时间。")]
        public float revealSeconds = 0.4f;
        [Min(0.05f)] public float fadeSeconds = 0.25f;
        [Range(0, 0.8f), Tooltip("逐字错开比例，0表示所有字同时显示。")]
        public float letterStagger = 0.45f;
        public Color particleColor = new Color(1f, 0.76f, 0.36f, 0.85f);
        [Range(0, 24)] public int particlesPerCharacter = 8;
        [Range(0.01f, 0.2f), Tooltip("相对字高的粒子大小。")]
        public float particleSize = 0.055f;
        [Range(0.1f, 1.5f), Tooltip("相对字高的聚拢/散开距离。")]
        public float scatterDistance = 0.65f;
    }

    // Local presentation only: no scene saving, font material replacement or input handling.
    [DisallowMultipleComponent]
    public sealed class MenuHoverLabelView : MonoBehaviour
    {
        private const int MaxSparks = 256;
        private TMP_Text label;
        private MenuHoverLabelSettings settings;
        private Color32[][] originalColors;
        private Glyph[] glyphs = Array.Empty<Glyph>();
        private int glyphCount;
        private bool hovered;
        private bool subscribed;
        private float progress;
        private bool colorsDirty;
        private ParticleSystem particles;
        private ParticleSystemRenderer particleRenderer;
        private readonly Spark[] sparks = new Spark[MaxSparks];
        private readonly ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[MaxSparks];
        private int sparkCount;
        private uint randomState = 71453;

        private struct Glyph
        {
            public int material;
            public int vertex;
            public Vector3 bottomLeft;
            public Vector3 topRight;
        }

        private struct Spark
        {
            public Vector3 from;
            public Vector3 to;
            public float start;
            public float duration;
            public float size;
        }

        public void Initialize(TMP_Text text, MenuHoverLabelSettings configuration)
        {
            Unsubscribe();
            label = text;
            settings = configuration;
            hovered = false;
            progress = 0;
            randomState = unchecked((uint)GetInstanceID()) ^ 71453u;
            Subscribe();
            // Rebuild once so the first rendered frame is already hidden. Later updates only
            // change vertex alpha and leave layout, font assets and generated submeshes to TMP.
            label.ForceMeshUpdate();
            colorsDirty = true;
        }

        private void OnEnable()
        {
            Subscribe();
            if (label != null) label.SetVerticesDirty();
        }

        private void Subscribe()
        {
            if (label == null || subscribed) return;
            label.OnPreRenderText += TextRebuilt;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (label != null && subscribed) label.OnPreRenderText -= TextRebuilt;
            subscribed = false;
        }

        private void TextRebuilt(TMP_TextInfo info)
        {
            if (settings == null) return;
            if (originalColors == null || originalColors.Length != info.meshInfo.Length)
                originalColors = new Color32[info.meshInfo.Length][];
            for (int i = 0; i < info.meshInfo.Length; i++)
            {
                Color32[] source = info.meshInfo[i].colors32;
                if (source == null) { originalColors[i] = null; continue; }
                if (originalColors[i] == null || originalColors[i].Length != source.Length)
                    originalColors[i] = new Color32[source.Length];
                Array.Copy(source, originalColors[i], source.Length);
            }

            if (glyphs.Length < info.characterCount) glyphs = new Glyph[info.characterCount];
            glyphCount = 0;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];
                if (!character.isVisible) continue;
                int material = character.materialReferenceIndex;
                int vertex = character.vertexIndex;
                Vector3[] vertices = info.meshInfo[material].vertices;
                glyphs[glyphCount++] = new Glyph
                {
                    material = material, vertex = vertex,
                    bottomLeft = vertices[vertex], topRight = vertices[vertex + 2]
                };
            }
            ApplyAlpha(info);
            colorsDirty = false;
        }

        public void SetHovered(bool value)
        {
            if (hovered == value || label == null || settings == null) return;
            hovered = value;
            SpawnSparks(value);
        }

        public void HideImmediately()
        {
            hovered = false;
            progress = 0;
            sparkCount = 0;
            colorsDirty = true;
            if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (label != null && label.isActiveAndEnabled && originalColors != null)
            {
                ApplyAlpha(label.textInfo);
                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                colorsDirty = false;
            }
        }

        private void LateUpdate()
        {
            if (label == null || settings == null || !label.isActiveAndEnabled) return;
            float duration = hovered ? settings.revealSeconds : settings.fadeSeconds;
            float next = Mathf.MoveTowards(progress, hovered ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(0.05f, duration));
            if (next != progress || colorsDirty)
            {
                progress = next;
                ApplyAlpha(label.textInfo);
                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
                colorsDirty = false;
            }
            if (sparkCount > 0) DrawSparks();
        }

        private void ApplyAlpha(TMP_TextInfo info)
        {
            if (originalColors == null) return;
            for (int i = 0; i < glyphCount; i++)
            {
                Glyph glyph = glyphs[i];
                if (glyph.material >= info.meshInfo.Length || glyph.material >= originalColors.Length) continue;
                Color32[] source = originalColors[glyph.material];
                Color32[] destination = info.meshInfo[glyph.material].colors32;
                if (source == null || destination == null || glyph.vertex + 3 >= source.Length
                    || glyph.vertex + 3 >= destination.Length) continue;
                float stagger = Mathf.Clamp(settings.letterStagger, 0, 0.8f);
                float offset = glyphCount > 1 ? i / (float)(glyphCount - 1) * stagger : 0;
                float alpha = Mathf.SmoothStep(0, 1,
                    Mathf.Clamp01((progress - offset) / (1f - stagger)));
                for (int corner = 0; corner < 4; corner++)
                {
                    Color32 color = source[glyph.vertex + corner];
                    color.a = (byte)Mathf.RoundToInt(color.a * alpha);
                    destination[glyph.vertex + corner] = color;
                }
            }
        }

        private bool PrepareParticles()
        {
            if (particles != null) return true;
            Material material = Resources.Load<Material>("UI/MenuHoverParticles");
            if (material == null) return false;
            var localEffect = new GameObject("Hover Letter Particles");
            // Keep the label's camera visibility. No Collider or UI Graphic is added,
            // so these particles cannot intercept menu pointer events on any layer.
            localEffect.layer = label.gameObject.layer;
            localEffect.transform.SetParent(label.transform, false);
            particles = localEffect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0;
            main.startLifetime = 1;
            main.maxParticles = MaxSparks;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.useUnscaledTime = true;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            particleRenderer = localEffect.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = material;
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (label is TextMeshPro worldText && worldText.renderer != null)
            {
                particleRenderer.sortingLayerID = worldText.renderer.sortingLayerID;
                particleRenderer.sortingOrder = worldText.renderer.sortingOrder + 1;
            }
            return true;
        }

        private void SpawnSparks(bool entering)
        {
            // Reverse the text from its current alpha, replacing the small particle burst
            // rather than accumulating objects or coroutines on repeated pointer movement.
            sparkCount = 0;
            if (settings.particlesPerCharacter <= 0 || glyphCount == 0 || !PrepareParticles()) return;
            float now = Time.unscaledTime;
            float duration = Mathf.Max(0.05f, entering ? settings.revealSeconds : settings.fadeSeconds);
            int count = Mathf.Clamp(settings.particlesPerCharacter, 0, 24);
            for (int i = 0; i < glyphCount; i++)
            {
                Glyph glyph = glyphs[i];
                float height = Mathf.Max(0.001f, glyph.topRight.y - glyph.bottomLeft.y);
                for (int n = 0; n < count && sparkCount < MaxSparks; n++)
                {
                    Vector3 anchor = new Vector3(
                        Mathf.Lerp(glyph.bottomLeft.x, glyph.topRight.x, RandomValue()),
                        Mathf.Lerp(glyph.bottomLeft.y, glyph.topRight.y, RandomValue()),
                        glyph.bottomLeft.z - height * 0.025f);
                    Vector3 scatter = new Vector3((RandomValue() - 0.5f) * 2,
                        0.2f + RandomValue() * 0.8f, 0) * height * settings.scatterDistance;
                    sparks[sparkCount++] = new Spark
                    {
                        from = entering ? anchor + scatter : anchor,
                        to = entering ? anchor : anchor + scatter,
                        start = now + duration * 0.3f * i / Mathf.Max(1, glyphCount - 1)
                            + RandomValue() * duration * 0.08f,
                        duration = duration * (0.65f + RandomValue() * 0.2f),
                        size = height * settings.particleSize * (0.6f + RandomValue() * 0.8f)
                    };
                }
            }
            particles.Play();
        }

        private float RandomValue()
        {
            randomState = unchecked(randomState * 1664525u + 1013904223u);
            return (randomState & 0x00ffffffu) / 16777216f;
        }

        private void DrawSparks()
        {
            if (particles == null) { sparkCount = 0; return; }
            int visible = 0;
            bool pending = false;
            float now = Time.unscaledTime;
            for (int i = 0; i < sparkCount; i++)
            {
                Spark spark = sparks[i];
                float t = (now - spark.start) / spark.duration;
                if (t < 0) { pending = true; continue; }
                if (t >= 1) continue;
                Color color = settings.particleColor;
                color.a *= Mathf.Sin(t * Mathf.PI);
                particleBuffer[visible++] = new ParticleSystem.Particle
                {
                    position = Vector3.Lerp(spark.from, spark.to, Mathf.SmoothStep(0, 1, t)),
                    startColor = color, startSize = spark.size,
                    startLifetime = 1, remainingLifetime = 1
                };
            }
            particles.SetParticles(particleBuffer, visible);
            if (visible == 0 && !pending)
            {
                sparkCount = 0;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDisable()
        {
            HideImmediately();
            Unsubscribe();
        }

        private void OnDestroy() => Unsubscribe();
    }
}
