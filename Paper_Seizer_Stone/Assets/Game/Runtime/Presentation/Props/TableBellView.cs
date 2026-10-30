using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation.Props
{
    // The authored bell retains its own brass material and texture in every phase.
    public sealed class TableBellView : MonoBehaviour
    {
        [SerializeField] private Transform bellVisual;
        [SerializeField] private Collider hitbox;
        [SerializeField, Range(0, 1)] private float inactiveBrightness = 0.55f;
        [SerializeField, Range(0, 0.2f)] private float pressCompression = 0.06f;
        private Renderer[] renderers;
        private MaterialPropertyBlock properties;
        private Vector3 restScale;
        private bool available;

        private void Awake()
        {
            renderers = bellVisual.GetComponentsInChildren<Renderer>(true);
            properties = new MaterialPropertyBlock();
            restScale = bellVisual.localScale;
            Apply(false);
        }

        public void SetAvailable(bool value)
        {
            if (renderers == null || available == value) return;
            Apply(value);
        }

        private void Apply(bool value)
        {
            available = value;
            hitbox.enabled = value;
            foreach (Renderer target in renderers)
            {
                Material material = target.sharedMaterial;
                if (material == null || !material.HasProperty("_Exposure")) continue;
                target.GetPropertyBlock(properties);
                properties.SetFloat("_Exposure", material.GetFloat("_Exposure") * (value ? 1 : inactiveBrightness));
                target.SetPropertyBlock(properties);
            }
        }

        public void Press()
        {
            if (!available) return;
            bellVisual.DOKill();
            bellVisual.localScale = restScale;
            // The single-mesh bell compresses above its base instead of sinking into the table.
            // The imported bell's local Z becomes world up after its authored -90 degree rotation.
            bellVisual.DOScale(new Vector3(restScale.x, restScale.y, restScale.z * (1 - pressCompression)), 0.07f)
                .SetLoops(2, LoopType.Yoyo).SetEase(Ease.OutSine);
            Apply(false);
        }

        private void OnDisable()
        {
            if (bellVisual == null || renderers == null) return;
            bellVisual.DOKill();
            bellVisual.localScale = restScale;
            Apply(false);
        }
    }
}
