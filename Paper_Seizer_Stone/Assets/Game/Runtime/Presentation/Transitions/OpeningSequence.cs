using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // The saved prefab owns its eyelids; the renderer reads this instance's temporary defocus.
    public sealed class OpeningSequence : MonoBehaviour
    {
        [SerializeField] private OpeningSequenceDefinition definition;
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private CanvasGroup inputBlock;
        [SerializeField] private Image eyelids;
        [SerializeField] private bool startClosed = true;
        private Material authoredMaterial;
        private Material animatedMaterial;
        private int revision;
        private static bool awaitingMenuArrival;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetArrival() { awaitingMenuArrival = false; }

        public bool IsPending { get; private set; }
        public float BlurPixels { get; private set; }

        private void Awake()
        {
            if (definition == null || overlayCanvas == null || inputBlock == null || eyelids == null)
            {
                Debug.LogError("请完整绑定苏醒开场的配置、画布、遮罩与眼睑。", this);
                Cancel();
                enabled = false;
                return;
            }
            bool menuArrival = startClosed && awaitingMenuArrival;
            if (startClosed) awaitingMenuArrival = false;

            authoredMaterial = eyelids.material;
            animatedMaterial = new Material(authoredMaterial) { hideFlags = HideFlags.HideAndDontSave };
            eyelids.material = animatedMaterial;
            animatedMaterial.SetFloat("_EyeOpening", 0f);
            animatedMaterial.SetFloat("_UpperShare", definition.upperLidShare);
            animatedMaterial.SetFloat("_Curvature", definition.curvature);
            animatedMaterial.SetFloat("_EdgePixels", definition.edgeSoftnessPixels);
            animatedMaterial.SetColor("_LidColor", definition.lidColor);

            if (!startClosed || (!definition.playOpening && !menuArrival)) { Cancel(); return; }

            IsPending = true;
            BlurPixels = 0f;
            // Cover the very first rendered frame while the controller prepares the table.
            inputBlock.alpha = 1f;
            inputBlock.interactable = false;
            inputBlock.blocksRaycasts = true;
            overlayCanvas.enabled = true;
        }

        public void SetTargetDisplay(int display)
        {
            if (overlayCanvas != null) overlayCanvas.targetDisplay = display;
        }

        // Reuse the same eyelids in the menu, then leave them closed until scene unload.
        public IEnumerator Close()
        {
            if (animatedMaterial == null || !isActiveAndEnabled) yield break;
            int run = ++revision;
            inputBlock.alpha = 1f;
            inputBlock.blocksRaycasts = true;
            overlayCanvas.enabled = true;
            float start = Time.unscaledTime;
            float seconds = Mathf.Max(0.1f, definition.closeDuration);
            while (run == revision && isActiveAndEnabled)
            {
                float progress = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                animatedMaterial.SetFloat("_EyeOpening", 1f - Mathf.SmoothStep(0f, 1f, progress));
                if (progress >= 1f) break;
                yield return null;
            }
            if (run != revision || !isActiveAndEnabled) yield break;
            awaitingMenuArrival = true;
            // Render one fully closed frame before starting the scene load.
            yield return null;
        }

        // One call plays every stage and releases the mask/blur even if the coroutine is disposed.
        public IEnumerator Play()
        {
            if (!IsPending || !isActiveAndEnabled) yield break;
            int run = ++revision;
            float start = Time.unscaledTime;
            try
            {
                while (run == revision && isActiveAndEnabled)
                {
                    float progress = Mathf.Clamp01((Time.unscaledTime - start) / definition.Duration);
                    if (progress >= 1f) break;
                    float opening = definition.eyelidOpening == null ? progress : definition.eyelidOpening.Evaluate(progress);
                    float blur = definition.defocus == null ? 1f - progress : definition.defocus.Evaluate(progress);
                    animatedMaterial.SetFloat("_EyeOpening", Mathf.Clamp01(opening));
                    BlurPixels = Mathf.Max(0f, definition.maximumBlurPixels) * Mathf.Clamp01(blur);
                    yield return null;
                }
            }
            finally
            {
                if (run == revision) Cancel();
            }
        }

        public void Cancel()
        {
            revision++;
            IsPending = false;
            BlurPixels = 0f;
            if (animatedMaterial != null) animatedMaterial.SetFloat("_EyeOpening", 1f);
            if (inputBlock != null)
            {
                inputBlock.alpha = 0f;
                inputBlock.interactable = false;
                inputBlock.blocksRaycasts = false;
            }
            if (overlayCanvas != null) overlayCanvas.enabled = false;
        }

        private void OnDisable() { Cancel(); }
        private void OnDestroy()
        {
            Cancel();
            if (eyelids != null && animatedMaterial != null) eyelids.material = authoredMaterial;
            if (animatedMaterial != null) Destroy(animatedMaterial);
        }
    }
}
