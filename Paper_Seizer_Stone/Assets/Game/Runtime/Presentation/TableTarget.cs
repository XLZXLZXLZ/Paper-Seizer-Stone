using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    public enum TableTargetKind { PlayerSlot, PlayerHand, PlayerDeck, Confirm }

    public sealed class TableTarget : MonoBehaviour
    {
        public TableTargetKind Kind;
        public int Slot;

        public void SetHighlight(bool highlighted)
        {
            Renderer targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) return;
            if (!highlighted) { targetRenderer.SetPropertyBlock(null); return; }
            var block = new MaterialPropertyBlock();
            Color color = Color.Lerp(targetRenderer.sharedMaterial.color, new Color(0.4f, 0.85f, 0.45f), 0.65f);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            targetRenderer.SetPropertyBlock(block);
        }
    }
}
