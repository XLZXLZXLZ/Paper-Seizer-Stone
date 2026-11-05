using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Flow;
using PaperSeizerStone.Presentation.Props;

namespace PaperSeizerStone.Presentation
{
    public sealed class ShopTableView : MonoBehaviour
    {
        [SerializeField] private TablePanel panel;
        [SerializeField] private ShopRugView rug;
        [SerializeField] private PhysicsTokenArea walletArea;
        [SerializeField] private ShopOfferView[] offers;
        [SerializeField] private Transform paymentExit;
        [SerializeField] private Transform deckAnchor;
        [SerializeField] private DeckPileView deckPile;
        [SerializeField] private TableBellView continueBell;
        [SerializeField] private CardView cardStyle;
        [SerializeField] private CardHoverPreview previewPrefab;
        [SerializeField, Min(0.05f)] private float transactionDuration = 0.45f;
        [SerializeField, Min(0)] private float paymentStagger = 0.05f;
        [SerializeField] private EngravingStationView engravingStation;
        private BattleController controller;
        private AdventureState adventure;
        private Dictionary<string, CardDefinition> catalog;
        private CardHoverPreview preview;
        private bool interactable;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> cards)
        {
            if (engravingStation == null || owner.Engravings == null || panel == null || rug == null || walletArea == null || paymentExit == null
                || deckAnchor == null || deckPile == null || continueBell == null || cardStyle == null
                || offers == null || offers.Length < 4 || previewPrefab == null)
                throw new InvalidOperationException("请完整绑定商店的四个商品位、价格、火盆、卡组与铜铃。");
            controller = owner;
            catalog = cards;
            walletArea.Initialize();
            deckPile.SetCount(1);
            deckPile.ConfigureBacks(cardStyle, Side.Player);
            for (int i = 0; i < offers.Length; i++) offers[i].Initialize(i);
            preview = Instantiate(previewPrefab);
            preview.Hide();
            panel.Initialize();
            rug.Initialize();
        }

        public void Prepare(AdventureState state)
        {
            adventure = state;
            Refresh();
            // Refresh configures active offers; keep them hidden while the rug is entering.
            panel.HideImmediately();
        }

        public IEnumerator Show(bool showRug = true)
        {
            if (showRug) yield return rug.Show();
            yield return panel.Show(adventure.EngravingOffer == CardTag.None ? engravingStation.transform : null);
            float delay = walletArea.SetCount(adventure.Wallet);
            if (delay > 0) yield return new WaitForSeconds(delay);
        }

        public IEnumerator Hide(bool hideRug = true)
        {
            SetInteractable(false);
            walletArea.Freeze();
            yield return panel.Hide();
            walletArea.SetCount(0, true);
            if (hideRug) yield return rug.Hide();
        }

        public void Notify(string message)
        {
            controller.ShowShopWarning(this, message);
            deckPile.PulseOutline();
        }

        public void Refresh()
        {
            if (adventure == null) return;
            engravingStation.Entrance(controller.Engravings, adventure.EngravingOffer, adventure.Wallet);
            deckPile.SetCount(adventure.Deck.Count);
            for (int i = 0; i < offers.Length; i++)
            {
                offers[i].gameObject.SetActive(i < adventure.Offers.Count);
                if (i < adventure.Offers.Count)
                {
                    ShopOffer offer = adventure.Offers[i];
                    offers[i].Display(offer, catalog[offer.DefinitionId], adventure.Wallet);
                }
            }
            SetInteractable(controller.CanShopInteract);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!value) engravingStation.SetHover(false);
            continueBell.SetAvailable(value);
            if (!value)
            {
                controller.ClearShopHint(this);
                deckPile.SetHoverOutline(false);
                if (preview != null) preview.Hide();
            }
        }

        private void Update()
        {
            if (controller == null) return;
            engravingStation.SetHover(false);
            if (preview != null) preview.Hide();
            deckPile.SetHoverOutline(false);
            if (!interactable || !controller.CanShopInteract)
            {
                controller.ClearShopHint(this);
                return;
            }
            if (!Application.isFocused || !new Rect(0, 0, Screen.width, Screen.height).Contains(Input.mousePosition)
                || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                controller.SetShopHint(this, null);
                return;
            }
            int mask = Physics.DefaultRaycastLayers & ~(1 << PlayerVisibilityLayer.Layer);
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100, mask))
            {
                controller.SetShopHint(this, null);
                return;
            }
            ShopTableAction action = hit.collider.GetComponentInParent<ShopTableAction>();
            if (action != null)
            {
                if (action.Kind == ShopTableActionKind.Engrave) engravingStation.SetHover(true);
                if (action.Kind == ShopTableActionKind.InspectDeck) deckPile.SetHoverOutline(true);
                controller.SetShopHint(this, HintFor(action.Kind));
                if (!Input.GetMouseButtonDown(0)) return;
                switch (action.Kind)
                {
                    case ShopTableActionKind.Engrave: controller.OpenEngravingTable(); break;
                    case ShopTableActionKind.Burn: controller.OpenBurnTable(); break;
                    case ShopTableActionKind.InspectDeck: controller.OpenDeckInspection(); break;
                    case ShopTableActionKind.Continue:
                        continueBell.Press();
                        controller.LeaveShop();
                        break;
                }
                return;
            }
            ShopOfferView offer = hit.collider.GetComponentInParent<ShopOfferView>();
            controller.SetShopHint(this, offer == null ? null : offer.Details);
            if (offer == null) return;
            if (preview != null) preview.Show(hit.collider.GetComponentInParent<CardView>(), Input.mousePosition, controller.Camera);
            if (Input.GetMouseButtonDown(0)) controller.BuyCard(offer.Index);
        }

        private string HintFor(ShopTableActionKind kind)
        {
            switch (kind)
            {
                case ShopTableActionKind.Burn:
                    return "删除一张牌 " + controller.CardRemovalPrice + "筹码";
                case ShopTableActionKind.InspectDeck:
                    return "你有 " + adventure.Deck.Count + " 张牌";
                case ShopTableActionKind.Engrave:
                    CardTagAppearance entry = controller.Engravings.Find(adventure.EngravingOffer);
                    return entry == null ? null : "为卡牌刻印 \"" + entry.displayName + "\" " + entry.price + " 筹码";
                case ShopTableActionKind.Continue:
                    return "进入下一场战斗";
                default:
                    return null;
            }
        }

        public IEnumerator Purchase(int index, int price)
        {
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, transactionDuration, paymentStagger));
            yield return offers[index].Purchase(deckAnchor, transactionDuration);
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPlace);
            yield return payment;
        }

        private void OnDisable()
        {
            if (controller != null) controller.ClearShopHint(this);
            if (preview != null) preview.Hide();
        }
        private void OnDestroy() { if (preview != null) Destroy(preview.gameObject); }
    }
}
