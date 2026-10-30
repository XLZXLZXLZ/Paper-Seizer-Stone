using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Flow;

namespace PaperSeizerStone.Presentation
{
    [Serializable]
    public sealed class OwnedCardSlot
    {
        public Button Button;
        public TMP_Text Label;
        [NonSerialized] public string RecordId;
    }

    public sealed class ShopTableView : MonoBehaviour
    {
        [SerializeField] private TablePanel panel;
        [SerializeField] private PhysicsTokenArea walletArea;
        [SerializeField] private ShopOfferView[] offers;
        [SerializeField] private CardView selectedCard;
        [SerializeField] private Transform paymentExit;
        [SerializeField] private Transform deckAnchor;
        [SerializeField] private TMP_Text walletLabel;
        [SerializeField] private TMP_Text lifeLabel;
        [SerializeField] private TMP_Text nextEnemyLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private TMP_Text deckLabel;
        [SerializeField] private TMP_Text pageLabel;
        [SerializeField] private TMP_Text selectedLabel;
        [SerializeField] private TMP_Text removePriceLabel;
        [SerializeField] private OwnedCardSlot[] ownedSlots;
        [SerializeField] private Button previousPage;
        [SerializeField] private Button nextPage;
        [SerializeField] private Button removeButton;
        [SerializeField] private Button continueButton;
        [SerializeField, Min(0.05f)] private float transactionDuration = 0.45f;
        [SerializeField, Min(0)] private float paymentStagger = 0.05f;
        private BattleController controller;
        private AdventureState adventure;
        private ShopDefinition shop;
        private Dictionary<string, CardDefinition> catalog;
        private Vector3 selectedPosition;
        private Quaternion selectedRotation;
        private string selectedRecord;
        private int page;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> cards)
        {
            if (panel == null || walletArea == null || selectedCard == null || paymentExit == null
                || deckAnchor == null || offers == null || offers.Length < 3
                || ownedSlots == null || ownedSlots.Length == 0 || previousPage == null || nextPage == null
                || removeButton == null || continueButton == null || walletLabel == null || lifeLabel == null
                || nextEnemyLabel == null || messageLabel == null || deckLabel == null || pageLabel == null
                || selectedLabel == null || removePriceLabel == null)
                throw new InvalidOperationException("请完整绑定商店的物件、界面与按钮。");
            controller = owner;
            catalog = cards;
            walletArea.Initialize();
            selectedCard.Initialize();
            selectedPosition = selectedCard.transform.localPosition;
            selectedRotation = selectedCard.transform.localRotation;
            for (int i = 0; i < offers.Length; i++) offers[i].Initialize(i);
            for (int i = 0; i < ownedSlots.Length; i++)
            {
                int slot = i;
                if (ownedSlots[i].Button == null || ownedSlots[i].Label == null)
                    throw new InvalidOperationException("持有牌列表缺少按钮或文字。");
                ownedSlots[i].Button.onClick.AddListener(() => Select(slot));
            }
            previousPage.onClick.AddListener(() => ChangePage(-1));
            nextPage.onClick.AddListener(() => ChangePage(1));
            removeButton.onClick.AddListener(() => controller.OpenBurnTable());
            continueButton.onClick.AddListener(() => controller.LeaveShop());
            panel.Initialize();
        }

        public void Prepare(AdventureState state, ShopDefinition definition, EnemyDefinition nextEnemy)
        {
            adventure = state;
            shop = definition;
            page = 0;
            selectedRecord = null;
            selectedCard.gameObject.SetActive(false);
            nextEnemyLabel.text = "下一位对手  " + nextEnemy.DisplayName + "   ·   " + nextEnemy.Life + " 生命";
            Notify("点击桌面商品购买；点击「烧牌整备」将持有牌组摊到桌上。");
            Refresh();
        }

        public IEnumerator Show()
        {
            yield return panel.Show();
            float delay = walletArea.SetCount(adventure.Wallet);
            if (delay > 0) yield return new WaitForSeconds(delay);
        }

        public IEnumerator Hide()
        {
            SetInteractable(false);
            walletArea.Freeze();
            yield return panel.Hide();
            walletArea.SetCount(0, true);
        }

        public void Notify(string message) { messageLabel.text = message; }

        public void Refresh()
        {
            if (adventure == null) return;
            walletLabel.text = "筹码  " + adventure.Wallet;
            lifeLabel.text = "剩余生命  " + adventure.Life;
            deckLabel.text = "我的牌组   " + adventure.Deck.Count + " 张";
            removePriceLabel.text = "烧牌整备   ·   " + shop.RemovePrice + " 筹码";
            page = Mathf.Clamp(page, 0, Mathf.Max(0, (adventure.Deck.Count - 1) / ownedSlots.Length));
            pageLabel.text = (page + 1) + " / " + Mathf.Max(1, (adventure.Deck.Count + ownedSlots.Length - 1) / ownedSlots.Length);
            for (int i = 0; i < offers.Length; i++)
            {
                offers[i].gameObject.SetActive(i < adventure.Offers.Count);
                if (i < adventure.Offers.Count)
                {
                    ShopOffer offer = adventure.Offers[i];
                    offers[i].Display(offer, catalog[offer.DefinitionId], adventure.Wallet);
                }
            }
            for (int i = 0; i < ownedSlots.Length; i++)
            {
                int index = page * ownedSlots.Length + i;
                OwnedCardSlot slot = ownedSlots[i];
                slot.Button.gameObject.SetActive(index < adventure.Deck.Count);
                slot.RecordId = index < adventure.Deck.Count ? adventure.Deck[index].RecordId : null;
                if (slot.RecordId != null)
                    slot.Label.text = (slot.RecordId == selectedRecord ? "● " : "") + (index + 1).ToString("00") + "   " + catalog[adventure.Deck[index].DefinitionId].DisplayName;
            }
            DeckCardRecord selected = FindSelected();
            selectedLabel.text = selected == null ? "点击列表查看牌面与能力\n烧牌最少保留 " + shop.MinimumDeckSize + " 张"
                : "已选择「" + catalog[selected.DefinitionId].DisplayName + "」\n"
                    + (adventure.Deck.Count <= shop.MinimumDeckSize ? "已达牌组下限，最少保留 " + shop.MinimumDeckSize + " 张"
                    : adventure.Wallet < shop.RemovePrice ? "筹码不足，无法支付删牌费用" : "可前往烧牌桌面精简这副牌组");
            selectedCard.gameObject.SetActive(selected != null);
            if (selected != null)
            {
                selectedCard.transform.localPosition = selectedPosition;
                selectedCard.transform.localRotation = selectedRotation;
                selectedCard.DisplayShop(catalog[selected.DefinitionId], false);
            }
            SetInteractable(controller.CanShopInteract);
        }

        public void SetInteractable(bool enabled)
        {
            foreach (OwnedCardSlot slot in ownedSlots) slot.Button.interactable = enabled;
            previousPage.interactable = enabled && page > 0;
            nextPage.interactable = enabled && adventure != null && (page + 1) * ownedSlots.Length < adventure.Deck.Count;
            removeButton.interactable = enabled && adventure != null;
            continueButton.interactable = enabled;
        }

        private DeckCardRecord FindSelected()
        {
            if (adventure != null)
                foreach (DeckCardRecord card in adventure.Deck) if (card.RecordId == selectedRecord) return card;
            return null;
        }

        private void Select(int slot)
        {
            if (!controller.CanShopInteract || ownedSlots[slot].RecordId == null) return;
            selectedRecord = ownedSlots[slot].RecordId;
            Refresh();
        }

        private void ChangePage(int delta)
        {
            if (!controller.CanShopInteract) return;
            page += delta;
            Refresh();
        }

        private void Update()
        {
            if (controller == null || !controller.CanShopInteract) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            int mask = Physics.DefaultRaycastLayers & ~(1 << PaperSeizerStone.Presentation.Props.PlayerVisibilityLayer.Layer);
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100, mask)) return;
            ShopOfferView offer = hit.collider.GetComponentInParent<ShopOfferView>();
            if (offer == null) return;
            Notify(offer.Details);
            if (Input.GetMouseButtonDown(0)) controller.BuyCard(offer.Index);
        }

        public IEnumerator Purchase(int index, int price)
        {
            Notify("正在收下新牌并支付筹码……");
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, transactionDuration, paymentStagger));
            yield return offers[index].Purchase(deckAnchor, transactionDuration);
            yield return payment;
        }

        private void OnDestroy() { if (selectedCard != null) selectedCard.transform.DOKill(); }
    }
}

