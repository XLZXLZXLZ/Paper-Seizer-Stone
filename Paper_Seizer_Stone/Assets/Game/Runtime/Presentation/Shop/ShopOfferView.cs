using System.Collections;
using UnityEngine;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    public sealed class ShopOfferView : MonoBehaviour
    {
        [SerializeField] private CardView card;
        [SerializeField] private TMP_Text priceLabel;
        private Vector3 position;
        private Quaternion rotation;
        public int Index { get; private set; }
        public string Details { get; private set; }

        public void Initialize(int index)
        {
            if (card == null || priceLabel == null) throw new System.InvalidOperationException("商品缺少卡牌或价格文字。");
            Index = index;
            card.Initialize();
            position = card.transform.localPosition;
            rotation = card.transform.localRotation;
        }

        public void Display(ShopOffer offer, CardDefinition definition, int wallet)
        {
            card.transform.DOKill();
            card.transform.localPosition = position;
            card.transform.localRotation = rotation;
            card.gameObject.SetActive(!offer.Sold);
            if (!offer.Sold) card.DisplayShop(definition, true);
            priceLabel.text = offer.Sold ? "已售罄" : definition.DisplayName + "   ·   " + offer.Price + " 筹码" + (wallet < offer.Price ? "\n筹码不足" : "\n点击卡牌购买");
            Details = offer.Sold ? "这张商品已经售罄。" : "「" + definition.DisplayName + "」  " + offer.Price + " 筹码   ·   " + definition.Description.Replace("\n", " ");
        }

        public IEnumerator Purchase(Transform destination, float duration)
        {
            card.SetHitbox(false);
            card.transform.DOMove(destination.position, duration).SetEase(Ease.InCubic);
            card.transform.DORotateQuaternion(destination.rotation, duration);
            yield return new WaitForSeconds(duration);
            card.gameObject.SetActive(false);
        }

        private void OnDestroy() { if (card != null) card.transform.DOKill(); }
    }
}
