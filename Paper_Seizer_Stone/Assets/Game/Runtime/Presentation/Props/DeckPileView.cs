using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    // Each saved layer has its own slight offset and yaw; refreshing counts never rerolls it.
    public sealed class DeckPileView : MonoBehaviour
    {
        [SerializeField] private Renderer[] layers;
        [SerializeField, Tooltip("仅在卡背层未配置材质时作为回退，不覆盖已编辑的卡背外观。")]
        private Material backMaterial;
        private bool initialized;

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
