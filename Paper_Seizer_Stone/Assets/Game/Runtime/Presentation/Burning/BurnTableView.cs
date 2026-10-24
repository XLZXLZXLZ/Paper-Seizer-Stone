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
    public sealed class BurnTableView : MonoBehaviour
    {
        [SerializeField] private TablePanel panel;
        [SerializeField] private GameObject editorPreview;
        [SerializeField] private CardView cardPrefab;
        [SerializeField] private Transform cardRoot;
        [SerializeField] private Transform spreadAnchor;
        [SerializeField] private Vector2 spreadSize = new Vector2(6.4f, 4.2f);
        [SerializeField, Min(1)] private int columns = 6;
        [SerializeField] private Transform burnAnchor;
        [SerializeField] private Transform emberGlow;
        [SerializeField] private PhysicsTokenArea walletArea;
        [SerializeField] private Transform paymentExit;
        [SerializeField] private TMP_Text walletLabel;
        [SerializeField] private TMP_Text selectionLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private TMP_Text priceLabel;
        [SerializeField] private Button burnButton;
        [SerializeField] private Button backButton;
        [SerializeField, Min(0.05f)] private float burnDuration = 0.8f;
        private readonly Dictionary<CardView, string> records = new Dictionary<CardView, string>();
        private readonly Dictionary<CardView, Vector3> positions = new Dictionary<CardView, Vector3>();
        private readonly List<CardView> cards = new List<CardView>();
        private Dictionary<string, CardDefinition> catalog;
        private AdventureState adventure;
        private ShopDefinition shop;
        private BattleController controller;
        private CardView selected;
        private Vector3 glowScale;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> definitions)
        {
            if (panel == null || cardPrefab == null || cardRoot == null || spreadAnchor == null || burnAnchor == null
                || emberGlow == null || walletArea == null || paymentExit == null || walletLabel == null
                || selectionLabel == null || messageLabel == null || priceLabel == null || burnButton == null || backButton == null)
                throw new InvalidOperationException("请完整绑定烧牌 Panel 的卡牌、摊牌范围、火盆、钱包与按钮。");
            controller = owner;
            catalog = definitions;
            if (editorPreview != null) editorPreview.SetActive(false);
            walletArea.Initialize();
            glowScale = emberGlow.localScale;
            burnButton.onClick.AddListener(() => { if (selected != null) controller.RemoveCard(records[selected]); });
            backButton.onClick.AddListener(() => controller.CloseBurnTable());
            panel.Initialize();
        }

        public void Prepare(AdventureState state, ShopDefinition definition)
        {
            adventure = state;
            shop = definition;
            foreach (CardView card in cards) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
            cards.Clear();
            records.Clear();
            positions.Clear();
            selected = null;
            int columnCount = Mathf.Min(columns, adventure.Deck.Count);
            int rows = Mathf.CeilToInt(adventure.Deck.Count / (float)columnCount);
            float scale = Mathf.Min(0.9f, spreadSize.x / (columnCount * 1.12f), spreadSize.y / (rows * 1.65f));
            for (int i = 0; i < adventure.Deck.Count; i++)
            {
                DeckCardRecord record = adventure.Deck[i];
                CardView card = Instantiate(cardPrefab, cardRoot);
                card.Initialize();
                card.DisplayShop(catalog[record.DefinitionId], true);
                card.name = "Owned_" + record.RecordId;
                card.transform.localScale = Vector3.one * scale;
                int inRow = Mathf.Min(columnCount, adventure.Deck.Count - (i / columnCount) * columnCount);
                Vector3 local = new Vector3((i % columnCount - (inRow - 1) * 0.5f) * 1.12f * scale,
                    0.01f * (i % 3), ((rows - 1) * 0.5f - i / columnCount) * 1.65f * scale);
                card.transform.SetPositionAndRotation(spreadAnchor.TransformPoint(local), spreadAnchor.rotation * Quaternion.Euler(0, UnityEngine.Random.Range(-2f, 2f), 0));
                positions.Add(card, card.transform.position);
                records.Add(card, record.RecordId);
                cards.Add(card);
            }
            emberGlow.localScale = glowScale;
            Notify("点选桌上的一张牌，再确认烧牌；返回商店可以继续买牌。");
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
            foreach (CardView card in cards) card.transform.DOKill();
            walletArea.Freeze();
            yield return panel.Hide();
            walletArea.SetCount(0, true);
        }

        public void Refresh()
        {
            walletLabel.text = "筹码  " + adventure.Wallet + "    ·    持有牌 " + adventure.Deck.Count + " 张";
            priceLabel.text = "确认烧牌   ·   " + shop.RemovePrice + " 筹码";
            selectionLabel.text = selected == null ? "点选一张牌查看能力\n至少保留 " + shop.MinimumDeckSize + " 张牌" : SelectionText();
            if (adventure.Deck.Count <= shop.MinimumDeckSize) Notify("牌组已达到下限，至少保留 " + shop.MinimumDeckSize + " 张。可以返回商店。");
            else if (adventure.Wallet < shop.RemovePrice) Notify("筹码不足，烧牌需要 " + shop.RemovePrice + " 筹码。可以查看牌组或返回商店。");
            SetInteractable(controller.CanBurnInteract);
        }

        private string SelectionText()
        {
            string recordId = records[selected];
            foreach (DeckCardRecord record in adventure.Deck)
                if (record.RecordId == recordId)
                {
                    CardDefinition definition = catalog[record.DefinitionId];
                    return "「" + definition.DisplayName + "」\n" + definition.Description + "\n烧毁后永久移出本次冒险牌组";
                }
            return "卡牌已烧毁";
        }

        public void SetInteractable(bool enabled)
        {
            burnButton.interactable = enabled && selected != null && adventure.Deck.Count > shop.MinimumDeckSize && adventure.Wallet >= shop.RemovePrice;
            backButton.interactable = enabled;
        }

        public void Notify(string message) { messageLabel.text = message; }

        private void Update()
        {
            if (controller == null || !controller.CanBurnInteract) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { controller.CloseBurnTable(); return; }
            if (!Input.GetMouseButtonDown(0) || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100)) return;
            CardView card = hit.collider.GetComponentInParent<CardView>();
            if (card == null || !records.ContainsKey(card)) return;
            if (selected != null)
            {
                selected.SetHighlight(false);
                selected.transform.DOMove(positions[selected], 0.15f);
            }
            selected = card;
            selected.SetHighlight(true);
            selected.transform.DOKill();
            selected.transform.DOMove(positions[selected] + Vector3.up * 0.14f, 0.15f);
            Refresh();
        }

        public IEnumerator Burn(int price)
        {
            Notify("正在烧掉选中的牌并支付筹码……");
            CardView burning = selected;
            burning.SetHitbox(false);
            burning.SetHighlight(false);
            burning.transform.DOKill();
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, burnDuration * 0.65f, 0.05f));
            yield return burning.transform.DOMove(burnAnchor.position, burnDuration * 0.4f).SetEase(Ease.InOutSine).WaitForCompletion();
            emberGlow.DOPunchScale(glowScale * 0.65f, burnDuration * 0.6f, 2, 0.4f);
            burning.transform.DORotate(new Vector3(0, 120, 0), burnDuration * 0.6f, RotateMode.LocalAxisAdd);
            yield return burning.transform.DOScale(Vector3.zero, burnDuration * 0.6f).SetEase(Ease.InCubic).WaitForCompletion();
            burning.gameObject.SetActive(false);
            selected = null;
            yield return payment;
        }

        private void OnDestroy()
        {
            foreach (CardView card in cards) if (card != null) card.transform.DOKill();
            if (emberGlow != null) emberGlow.DOKill();
        }
    }
}
