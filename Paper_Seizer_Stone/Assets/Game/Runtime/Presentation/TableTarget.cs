using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    public enum TableTargetKind { PlayerSlot, PlayerHand, PlayerDeck, Confirm }

    public sealed class TableTarget : MonoBehaviour
    {
        public TableTargetKind Kind;
        public int Slot;
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color highlightColor = new Color(0.4f, 0.85f, 0.45f);
        [SerializeField, Min(0)] private float highlightEmission;

        public void SetHighlight(bool highlighted)
        {
            Renderer targetRenderer = highlightRenderer != null ? highlightRenderer : GetComponent<Renderer>();
            if (targetRenderer == null) return;
            if (!highlighted) { targetRenderer.SetPropertyBlock(null); return; }
            var block = new MaterialPropertyBlock();
            Color color = Color.Lerp(targetRenderer.sharedMaterial.color, highlightColor, 0.65f);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            if (highlightEmission > 0)
            {
                block.SetColor("_EmissionColor", highlightColor);
                block.SetFloat("_EmissionStrength", highlightEmission);
            }
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
