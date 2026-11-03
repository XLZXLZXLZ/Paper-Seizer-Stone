using TMPro;
using UnityEngine;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ScoreCounterView : MonoBehaviour
    {
        [SerializeField] private Renderer screen;
        [SerializeField, Min(0)] private int screenMaterialIndex = 1;
        [SerializeField] private Material screenEffectsMaterial;
        [SerializeField, Min(1)] private int fullScore = 8;
        [SerializeField, Min(0.05f)] private float fillSeconds = 0.95f;
        [SerializeField, Min(0)] private float scoreHoldSeconds = 0.55f;
        [SerializeField, Tooltip("优先尝试已安装的苹方字体；清空后直接使用下方字体资源。")]
        private string installedFontFamily = "PingFang SC";
        [SerializeField] private TMP_FontAsset numberFont;
        [SerializeField] private Vector3 enemyNumberPosition;
        [SerializeField] private Vector3 playerNumberPosition;
        [SerializeField] private Vector3 numberEulerAngles;
        [SerializeField, Min(0.01f)] private float numberSize = 2f;
        [SerializeField] private Color numberColor = new Color(1f, 0.88f, 0.62f);

        private static readonly int EnemyFill = Shader.PropertyToID("_EnemyFill");
        private static readonly int PlayerFill = Shader.PropertyToID("_PlayerFill");
        private static readonly int EnemyDamaged = Shader.PropertyToID("_EnemyDamaged");
        private static readonly int PlayerDamaged = Shader.PropertyToID("_PlayerDamaged");

        private Font installedFont;
        private TMP_FontAsset generatedFont;
        private bool installedDigitsAvailable;
        private MaterialPropertyBlock properties;
        private TextMeshPro enemyNumber;
        private TextMeshPro playerNumber;
        private int startingEnemyLife = -1;
        private int startingPlayerLife = -1;
        private int round = -1;
        private int enemyScore;
        private int playerScore;
        private bool enemyDamaged;
        private bool playerDamaged;
        private float revealStart = float.NegativeInfinity;
        private bool revealing;
        private Mesh effectsMesh;
        private MeshRenderer effectsRenderer;

        public float RevealSeconds => fillSeconds + scoreHoldSeconds;

        private void Awake()
        {
            properties = new MaterialPropertyBlock();
            CreateScreenEffects();
            ResolveNumberFont();
            enemyNumber = CreateNumber("Enemy Score", enemyNumberPosition);
            playerNumber = CreateNumber("Player Score", playerNumberPosition);
            Draw(0f);
        }

        private void CreateScreenEffects()
        {
            if (screen == null || screenEffectsMaterial == null) return;
            MeshFilter filter = screen.GetComponent<MeshFilter>();
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable || screenMaterialIndex < 0
                || screenMaterialIndex >= source.subMeshCount) return;

            int[] triangles = source.GetTriangles(screenMaterialIndex);
            effectsMesh = Instantiate(source);
            effectsMesh.name = "Score Counter Screen Effects";
            effectsMesh.subMeshCount = 1;
            effectsMesh.SetTriangles(triangles, 0);

            var overlay = new GameObject("Screen Effects", typeof(MeshFilter), typeof(MeshRenderer));
            overlay.layer = screen.gameObject.layer;
            overlay.transform.SetParent(screen.transform, false);
            overlay.GetComponent<MeshFilter>().sharedMesh = effectsMesh;
            effectsRenderer = overlay.GetComponent<MeshRenderer>();
            effectsRenderer.sharedMaterial = screenEffectsMaterial;
            effectsRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            effectsRenderer.receiveShadows = false;
        }

        public void ResetDisplay()
        {
            startingEnemyLife = startingPlayerLife = -1;
            enemyDamaged = playerDamaged = false;
            round = -1;
            ClearScores();
        }

        public void Observe(BattleObservation snapshot)
        {
            if (startingEnemyLife < 0) startingEnemyLife = snapshot.Enemy.Life;
            if (startingPlayerLife < 0) startingPlayerLife = snapshot.Player.Life;
            enemyDamaged = snapshot.Enemy.Life < startingEnemyLife;
            playerDamaged = snapshot.Player.Life < startingPlayerLife;
            if (round != snapshot.Round)
            {
                round = snapshot.Round;
                ClearScores();
            }
            else Draw(CurrentFillProgress());
        }

        public void RevealScores(BattleObservation snapshot)
        {
            enemyScore = snapshot.Enemy.Score;
            playerScore = snapshot.Player.Score;
            revealStart = Time.unscaledTime;
            revealing = true;
            Draw(0f);
        }

        private void Update()
        {
            if (!revealing) return;
            float progress = CurrentFillProgress();
            Draw(progress);
            revealing = progress < 1f;
        }

        private float CurrentFillProgress() =>
            Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - revealStart) / fillSeconds));

        private void ClearScores()
        {
            enemyScore = playerScore = 0;
            revealStart = float.NegativeInfinity;
            revealing = false;
            Draw(0f);
        }

        private void Draw(float progress)
        {
            if (effectsRenderer != null && properties != null)
            {
                effectsRenderer.GetPropertyBlock(properties);
                properties.SetFloat(EnemyFill, Mathf.Clamp01(enemyScore * progress / Mathf.Max(1, fullScore)));
                properties.SetFloat(PlayerFill, Mathf.Clamp01(playerScore * progress / Mathf.Max(1, fullScore)));
                properties.SetFloat(EnemyDamaged, enemyDamaged ? 1f : 0f);
                properties.SetFloat(PlayerDamaged, playerDamaged ? 1f : 0f);
                effectsRenderer.SetPropertyBlock(properties);
            }
            if (enemyNumber != null) enemyNumber.text = Mathf.RoundToInt(enemyScore * progress).ToString();
            if (playerNumber != null) playerNumber.text = Mathf.RoundToInt(playerScore * progress).ToString();
        }

        private void ResolveNumberFont()
        {
            if (string.IsNullOrWhiteSpace(installedFontFamily)) return;
            string[] names = Font.GetOSInstalledFontNames();
            string family = System.Array.Find(names, name =>
                string.Equals(name, installedFontFamily, System.StringComparison.OrdinalIgnoreCase));
            if (family == null && installedFontFamily == "PingFang SC")
                family = System.Array.Find(names, name => name.StartsWith("PingFang", System.StringComparison.OrdinalIgnoreCase));
            if (family == null) return;
            installedFont = Font.CreateDynamicFontFromOSFont(family, 48);
            if (installedFont != null)
                generatedFont = TMP_FontAsset.CreateFontAsset(installedFont, 48, 5,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 256, 256);
            installedDigitsAvailable = generatedFont != null && generatedFont.TryAddCharacters("0123456789");
        }

        private void OnDestroy()
        {
            if (effectsMesh != null) Destroy(effectsMesh);
            if (generatedFont != null)
            {
                foreach (Texture2D texture in generatedFont.atlasTextures)
                    if (texture != null) Destroy(texture);
                if (generatedFont.material != null) Destroy(generatedFont.material);
                Destroy(generatedFont);
            }
            if (installedFont != null) Destroy(installedFont);
        }

        private TextMeshPro CreateNumber(string label, Vector3 position)
        {
            TMP_FontAsset font = installedDigitsAvailable ? generatedFont : numberFont;
            if (font == null) return null;
            var node = new GameObject(label, typeof(RectTransform));
            node.layer = gameObject.layer;
            node.transform.SetParent(transform, false);
            node.transform.localPosition = position;
            node.transform.localRotation = Quaternion.Euler(numberEulerAngles);
            var number = node.AddComponent<TextMeshPro>();
            number.font = font;
            number.fontSize = numberSize;
            number.color = numberColor;
            number.alignment = TextAlignmentOptions.Center;
            number.enableWordWrapping = false;
            number.rectTransform.sizeDelta = Vector2.one;
            return number;
        }
    }
}
