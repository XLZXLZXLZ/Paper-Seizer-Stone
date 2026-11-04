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
        private BattleController controller;
        private AdventureState adventure;
        private Dictionary<string, CardDefinition> catalog;
        private CardHoverPreview preview;
        private bool interactable;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> cards)
        {
            if (panel == null || rug == null || walletArea == null || paymentExit == null
                || deckAnchor == null || deckPile == null || continueBell == null || cardStyle == null
                || offers == null || offers.Length < 3 || previewPrefab == null)
                throw new InvalidOperationException("请完整绑定商店的商品、价格、火盆、卡组与铜铃。");
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
        }

        public IEnumerator Show(bool showRug = true)
        {
            if (showRug) yield return rug.Show();
            yield return panel.Show();
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

        // Errors use the prop's feedback; there is no persistent shop message panel.
        public void Notify(string message) { deckPile.PulseOutline(); }

        public void Refresh()
        {
            if (adventure == null) return;
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
            continueBell.SetAvailable(value);
            if (!value)
            {
                deckPile.SetHoverOutline(false);
                if (preview != null) preview.Hide();
            }
        }

        private void Update()
        {
            if (controller == null || !interactable || !controller.CanShopInteract) return;
            if (preview != null) preview.Hide();
            deckPile.SetHoverOutline(false);
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            int mask = Physics.DefaultRaycastLayers & ~(1 << PlayerVisibilityLayer.Layer);
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100, mask)) return;
            ShopTableAction action = hit.collider.GetComponentInParent<ShopTableAction>();
            if (action != null)
            {
                if (action.Kind == ShopTableActionKind.InspectDeck) deckPile.SetHoverOutline(true);
                if (!Input.GetMouseButtonDown(0)) return;
                switch (action.Kind)
                {
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
            if (offer == null) return;
            if (preview != null) preview.Show(hit.collider.GetComponentInParent<CardView>(), Input.mousePosition);
            if (Input.GetMouseButtonDown(0)) controller.BuyCard(offer.Index);
        }

        public IEnumerator Purchase(int index, int price)
        {
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, transactionDuration, paymentStagger));
            yield return offers[index].Purchase(deckAnchor, transactionDuration);
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPlace);
            yield return payment;
        }

        private void OnDisable() { if (preview != null) preview.Hide(); }
        private void OnDestroy() { if (preview != null) Destroy(preview.gameObject); }
    }
}
