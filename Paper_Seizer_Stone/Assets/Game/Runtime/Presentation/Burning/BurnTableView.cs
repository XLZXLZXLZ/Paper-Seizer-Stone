using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
        [SerializeField] private Transform brazierRoot;
        [SerializeField] private CardHoverPreview previewPrefab;
        private CardHoverPreview preview;
        private bool inspectOnly;
        private bool interactable;
        [SerializeField, Min(0.05f)] private float burnDuration = 0.8f;
        private readonly Dictionary<CardView, string> records = new Dictionary<CardView, string>();
        private readonly Dictionary<CardView, Vector3> positions = new Dictionary<CardView, Vector3>();
        private readonly List<CardView> cards = new List<CardView>();
        private Dictionary<string, CardDefinition> catalog;
        private AdventureState adventure;
        private BattleController controller;
        private CardView selected;
        private Vector3 glowScale;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> definitions)
        {
            if (panel == null || cardPrefab == null || cardRoot == null || spreadAnchor == null || burnAnchor == null
                || emberGlow == null || walletArea == null || paymentExit == null || brazierRoot == null || previewPrefab == null)
                throw new InvalidOperationException("请完整绑定卡组桌面的摊牌范围、火盆、钱包与悬停预览。");
            controller = owner;
            catalog = definitions;
            if (editorPreview != null) editorPreview.SetActive(false);
            walletArea.Initialize();
            glowScale = emberGlow.localScale;
            preview = Instantiate(previewPrefab);
            preview.Hide();
            panel.Initialize();
        }

        public void Prepare(AdventureState state, bool inspection = false)
        {
            inspectOnly = inspection;
            adventure = state;
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
            Refresh();
        }

        public IEnumerator Show()
        {
            yield return panel.Show(inspectOnly ? brazierRoot : null);
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
            SetInteractable(controller.CanDeckTableInteract);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            if (!value && preview != null) preview.Hide();
        }

        public void Notify(string message)
        {
            if (!inspectOnly && emberGlow != null)
            {
                emberGlow.DOKill();
                emberGlow.localScale = glowScale;
                emberGlow.DOPunchScale(glowScale * 0.2f, 0.3f, 2, 0.4f);
            }
        }

        private void Update()
        {
            if (controller == null || !interactable || !controller.CanDeckTableInteract) return;
            if (preview != null) preview.Hide();
            if (Input.GetKeyDown(KeyCode.Escape)) { controller.CloseBurnTable(); return; }
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            int mask = Physics.DefaultRaycastLayers & ~(1 << PaperSeizerStone.Presentation.Props.PlayerVisibilityLayer.Layer);
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100, mask)) return;
            CardView card = hit.collider.GetComponentInParent<CardView>();
            if (card != null && records.ContainsKey(card) && preview != null) preview.Show(card, Input.mousePosition);
            if (!Input.GetMouseButtonDown(0)) return;
            var action = hit.collider.GetComponentInParent<PaperSeizerStone.Presentation.Props.ShopTableAction>();
            if (!inspectOnly && action != null && action.Kind == PaperSeizerStone.Presentation.Props.ShopTableActionKind.Burn)
            {
                if (selected != null) controller.RemoveCard(records[selected]);
                else Notify("请先选择卡牌。");
                return;
            }
            if (card == null || !records.ContainsKey(card)) return;
            if (selected != null)
            {
                selected.SetHighlight(false);
                selected.transform.DOMove(positions[selected], 0.15f);
            }
            selected = card;
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPickUp);
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
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardBurn);
            emberGlow.DOPunchScale(glowScale * 0.65f, burnDuration * 0.6f, 2, 0.4f);
            burning.transform.DORotate(new Vector3(0, 120, 0), burnDuration * 0.6f, RotateMode.LocalAxisAdd);
            yield return burning.transform.DOScale(Vector3.zero, burnDuration * 0.6f).SetEase(Ease.InCubic).WaitForCompletion();
            burning.gameObject.SetActive(false);
            selected = null;
            yield return payment;
        }

        private void OnDisable() { if (preview != null) preview.Hide(); }

        private void OnDestroy()
        {
            if (preview != null) Destroy(preview.gameObject);
            foreach (CardView card in cards) if (card != null) card.transform.DOKill();
            if (emberGlow != null) emberGlow.DOKill();
        }
    }
}
