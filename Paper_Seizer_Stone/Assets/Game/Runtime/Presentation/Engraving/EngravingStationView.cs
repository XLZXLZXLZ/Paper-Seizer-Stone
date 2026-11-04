using UnityEngine;
using TMPro;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // A shallow saved card slot, using the same tray geometry as the battle table.
    public sealed class EngravingStationView : MonoBehaviour
    {
        [SerializeField] private Transform cardAnchor;
        [SerializeField] private Renderer inset;
        [SerializeField] private Renderer rim;
        [SerializeField] private TMP_Text status;
        private MaterialPropertyBlock properties;
        private Color rimColor = new Color(0.43f, 0.35f, 0.21f);
        public Transform CardAnchor => cardAnchor;

        private void ApplyFinish(CardTagCatalog catalog, CardTag tag)
        {
            CardTagAppearance entry = catalog.Find(tag);
            if (properties == null) properties = new MaterialPropertyBlock();
            inset.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", entry == null ? new Color(0.16f, 0.14f, 0.11f)
                : Color.Lerp(new Color(0.16f, 0.14f, 0.11f), entry.color, 0.2f));
            inset.SetPropertyBlock(properties);
            SetHover(false);
        }

        public void SetHover(bool hover)
        {
            if (rim == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            rim.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", hover ? new Color(0.69f, 0.55f, 0.31f) : rimColor);
            rim.SetPropertyBlock(properties);
        }

        public void Entrance(CardTagCatalog catalog, CardTag tag, int wallet)
        {
            CardTagAppearance entry = catalog.Find(tag);
            ApplyFinish(catalog, tag);
            status.text = entry == null ? "本次无刻印" : "刻印 · " + entry.displayName + "\n" + entry.price + "筹码";
        }

        public void Display(CardTagCatalog catalog, CardTag tag, DeckCardRecord card, int wallet)
        {
            ApplyFinish(catalog, tag);
            CardTagAppearance entry = catalog.Find(tag);
            if (entry == null) { status.text = "请选择刻印"; return; }
            string action = card == null ? "先选一张牌" : card.Tag == tag ? "已拥有此刻印" : wallet < entry.price ? "筹码不足"
                : card.Tag == CardTag.None ? "点击槽位刻印" : "点击槽位替换";
            status.text = "刻印 · " + entry.displayName + "\n" + entry.price + "筹码 · " + action;
        }

        public void Notify(string message) { status.text = message; }
    }
}
