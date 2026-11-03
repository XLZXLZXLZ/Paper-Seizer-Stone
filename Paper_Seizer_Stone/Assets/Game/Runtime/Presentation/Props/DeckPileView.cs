using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    // Each saved layer has its own slight offset and yaw; refreshing counts never rerolls it.
    public sealed class DeckPileView : MonoBehaviour
    {
        [SerializeField] private Renderer[] layers;
        [SerializeField, Tooltip("仅在卡背层未配置材质时作为回退，不覆盖已编辑的卡背外观。")]
        private Material backMaterial;
        [SerializeField] private Material drawOutlineMaterial;
        private bool initialized;
        private LineRenderer drawOutline;
        private float outlineUntil;
        private bool hoverOutline;
        private const float OutlineDuration = 0.42f;
        private const float OutlineWidth = 0.011f;
        private const float HoverWidth = 0.007f;

        public void PulseOutline()
        {
            if (!PrepareOutline()) return;
            outlineUntil = Time.unscaledTime + OutlineDuration;
            RefreshOutline();
        }

        public void SetHoverOutline(bool value)
        {
            hoverOutline = value && PrepareOutline();
            RefreshOutline();
        }

        private bool PrepareOutline()
        {
            if (!initialized || drawOutlineMaterial == null) return false;

            // The last visible layer is the top card. Copy its footprint before
            // SetCount removes that card so the outline stays on the deck.
            Renderer top = null;
            for (int i = layers.Length - 1; i >= 0; i--)
            {
                if (!layers[i].gameObject.activeInHierarchy) continue;
                top = layers[i];
                break;
            }
            if (top == null) top = layers[0];

            if (drawOutline == null)
            {
                var visual = new GameObject("Cheat Draw Outline");
                visual.layer = gameObject.layer;
                visual.transform.SetParent(transform, false);
                drawOutline = visual.AddComponent<LineRenderer>();
                drawOutline.useWorldSpace = false;
                drawOutline.loop = true;
                drawOutline.positionCount = 4;
                drawOutline.numCornerVertices = 3;
                drawOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                drawOutline.receiveShadows = false;
                drawOutline.sharedMaterial = drawOutlineMaterial;
            }

            Bounds bounds = top.localBounds;
            float y = bounds.max.y;
            SetOutlineCorner(0, top.transform, new Vector3(bounds.min.x, y, bounds.min.z));
            SetOutlineCorner(1, top.transform, new Vector3(bounds.max.x, y, bounds.min.z));
            SetOutlineCorner(2, top.transform, new Vector3(bounds.max.x, y, bounds.max.z));
            SetOutlineCorner(3, top.transform, new Vector3(bounds.min.x, y, bounds.max.z));
            return true;
        }

        private void SetOutlineCorner(int index, Transform card, Vector3 localCorner)
        {
            Vector3 worldCorner = card.TransformPoint(localCorner) + card.up * 0.02f;
            drawOutline.SetPosition(index, transform.InverseTransformPoint(worldCorner));
        }

        private void Update() => RefreshOutline();

        private void RefreshOutline()
        {
            if (drawOutline == null) return;
            float remaining = outlineUntil - Time.unscaledTime;
            float pulseWidth = OutlineWidth * Mathf.Clamp01(remaining / 0.2f);
            drawOutline.enabled = hoverOutline || remaining > 0f;
            drawOutline.widthMultiplier = Mathf.Max(hoverOutline ? HoverWidth : 0f, pulseWidth);
        }

        private void OnDisable()
        {
            hoverOutline = false;
            outlineUntil = 0f;
            if (drawOutline != null) drawOutline.enabled = false;
        }

        public void SetCount(int count)
        {
            if (!initialized)
            {
                if (layers == null || layers.Length == 0)
                    throw new System.InvalidOperationException(name + "缺少牌堆卡背层。");
                foreach (Renderer layer in layers)
                {
                    if (layer == null) throw new System.InvalidOperationException(name + "有未绑定的卡背层。");
                    // Count updates must not replace the appearance authored on each layer.
                    if (layer.sharedMaterial == null)
                    {
                        if (backMaterial == null) throw new System.InvalidOperationException(name + "缺少卡背材质。");
                        layer.sharedMaterial = backMaterial;
                    }
                }
                initialized = true;
            }
            for (int i = 0; i < layers.Length; i++) layers[i].gameObject.SetActive(i < count);
        }
    }
}
