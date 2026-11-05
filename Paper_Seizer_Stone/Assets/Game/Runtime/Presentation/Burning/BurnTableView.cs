using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        [SerializeField] private Vector2 spreadSize = new Vector2(4.1f, 3.4f);
        [SerializeField, Min(1)] private int columns = 4;
        [SerializeField] private Vector2 cardSpacing = new Vector2(0.82f, 1.23f);
        [SerializeField, Range(0.3f, 1)] private float maximumCardScale = 0.85f;
        [SerializeField] private Button returnButton;
        [SerializeField] private Transform burnAnchor;
        [SerializeField] private Transform emberGlow;
        [SerializeField] private PhysicsTokenArea walletArea;
        [SerializeField] private Transform paymentExit;
        [SerializeField] private Transform brazierRoot;
        [SerializeField] private CardHoverPreview previewPrefab;
        private CardHoverPreview preview;
        [SerializeField] private EngravingStationView engravingStation;
        [SerializeField, Min(0.3f)] private float engravingFlipSeconds = 0.8f;
        [SerializeField, Min(0.4f)] private float engravingLift = 0.55f;
        private bool engraving;
        private CardTag selectedTag = CardTag.Violence;
        private bool inspectOnly;
        private bool interactable;
        [SerializeField, Min(0.05f)] private float burnDuration = 0.8f;
        private readonly Dictionary<CardView, DeckCardRecord> records = new Dictionary<CardView, DeckCardRecord>();
        private readonly Dictionary<CardView, Vector3> positions = new Dictionary<CardView, Vector3>();
        private readonly List<CardView> cards = new List<CardView>();
        private Dictionary<string, CardDefinition> catalog;
        private AdventureState adventure;
        private BattleController controller;
        private CardView selected;
        private CardTag selectedOriginalTag;
        private Vector3 glowScale;
        private BrazierFlameView brazierFlame;

        public void Initialize(BattleController owner, Dictionary<string, CardDefinition> definitions)
        {
            if (panel == null || cardPrefab == null || cardRoot == null || spreadAnchor == null || burnAnchor == null
                || returnButton == null || engravingStation == null || emberGlow == null || walletArea == null || paymentExit == null || brazierRoot == null || previewPrefab == null)
                throw new InvalidOperationException("请完整绑定卡组桌面的摊牌范围、火盆、钱包与悬停预览。");
            controller = owner;
            returnButton.onClick.AddListener(ReturnToShop);
            catalog = definitions;
            if (editorPreview != null) editorPreview.SetActive(false);
            walletArea.Initialize();
            glowScale = emberGlow.localScale;
            brazierFlame = emberGlow.GetComponent<BrazierFlameView>();
            preview = Instantiate(previewPrefab);
            preview.Hide();
            panel.Initialize();
        }

        public void Prepare(AdventureState state, bool inspection = false, bool engrave = false)
        {
            inspectOnly = inspection;
            engraving = engrave;
            selectedTag = engrave ? state.EngravingOffer : CardTag.None;
            adventure = state;
            foreach (CardView card in cards) { card.gameObject.SetActive(false); Destroy(card.gameObject); }
            cards.Clear();
            records.Clear();
            positions.Clear();
            selected = null;
            int columnCount = Mathf.Min(columns, adventure.Deck.Count);
            int rows = Mathf.CeilToInt(adventure.Deck.Count / (float)columnCount);
            // Fit the occupied card bounds, rather than reserving a wide empty cell per card.
            float occupiedWidth = (columnCount - 1) * cardSpacing.x + 0.74f;
            float occupiedDepth = (rows - 1) * cardSpacing.y + 1.11f;
            float scale = Mathf.Min(maximumCardScale, spreadSize.x / occupiedWidth, spreadSize.y / occupiedDepth);
            for (int i = 0; i < adventure.Deck.Count; i++)
            {
                DeckCardRecord record = adventure.Deck[i];
                CardView card = Instantiate(cardPrefab, cardRoot);
                card.Initialize();
                card.DisplayShop(catalog[record.DefinitionId], true, record.Tag, record.RecordId);
                card.name = "Owned_" + record.RecordId;
                card.transform.localScale = Vector3.one * scale;
                int inRow = Mathf.Min(columnCount, adventure.Deck.Count - (i / columnCount) * columnCount);
                Vector3 local = new Vector3((i % columnCount - (inRow - 1) * 0.5f) * cardSpacing.x * scale,
                    0.005f * (i % 3), ((rows - 1) * 0.5f - i / columnCount) * cardSpacing.y * scale);
                card.transform.SetPositionAndRotation(spreadAnchor.TransformPoint(local), spreadAnchor.rotation);
                positions.Add(card, card.transform.position);
                records.Add(card, record);
                cards.Add(card);
            }
            emberGlow.localScale = glowScale;
            if (brazierFlame != null) brazierFlame.ResetPulse();
            Refresh();
        }

        public IEnumerator Show()
        {
            yield return panel.Show(inspectOnly || engraving ? brazierRoot : null, secondaryExcludedWorldRoot: engraving ? null : engravingStation.transform);
            float delay = walletArea.SetCount(adventure.Wallet);
            if (delay > 0) yield return new WaitForSeconds(delay);
        }

        public IEnumerator Hide()
        {
            SetInteractable(false);
            controller.ClearShopHint(this);
            if (selected != null && engraving) selected.SetTag(records[selected].Tag);
            foreach (CardView card in cards) card.transform.DOKill();
            walletArea.Freeze();
            yield return panel.Hide();
            walletArea.SetCount(0, true);
        }

        public void Refresh()
        {
            SetInteractable(controller.CanDeckTableInteract);
            if (engraving) engravingStation.Display(controller.Engravings, selectedTag,
                selected != null ? records[selected] : null, adventure.Wallet);
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            returnButton.interactable = value;
            if (value) controller.SetShopHint(this, PersistentHint);
            if (!value) engravingStation.SetHover(false);
            if (!value && preview != null) preview.Hide();
        }

        private string PersistentHint
        {
            get
            {
                if (inspectOnly) return null;
                if (!engraving) return "点击卡牌立即烧毁 " + controller.CardRemovalPrice + "筹码";
                CardTagAppearance entry = controller.Engravings.Find(selectedTag);
                return entry == null ? null : "刻印 \"" + entry.displayName + "\" " + entry.price + "筹码";
            }
        }

        private bool IsReturnHovered()
        {
            if (!returnButton.isActiveAndEnabled || !returnButton.interactable) return false;
            Canvas canvas = returnButton.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)returnButton.transform, Input.mousePosition, uiCamera);
        }

        private void ReturnToShop()
        {
            if (interactable && controller != null) controller.CloseBurnTable();
        }

        public void Notify(string message)
        {
            controller.ShowShopWarning(this, message);
            if (engraving) { engravingStation.Notify(message); return; }
            if (!inspectOnly && emberGlow != null)
            {
                emberGlow.DOKill();
                emberGlow.localScale = glowScale;
                if (brazierFlame != null) brazierFlame.Pulse(0.25f, 0.3f);
                else emberGlow.DOPunchScale(glowScale * 0.2f, 0.3f, 2, 0.4f);
            }
        }

        private void Update()
        {
            if (controller == null || !interactable || !controller.CanDeckTableInteract) return;
            engravingStation.SetHover(false);
            if (preview != null) preview.Hide();
            bool cursorInside = Application.isFocused
                && new Rect(0, 0, Screen.width, Screen.height).Contains(Input.mousePosition);
            controller.SetShopHint(this, cursorInside && IsReturnHovered() ? "返回商店" : PersistentHint);
            if (Input.GetKeyDown(KeyCode.Escape)) { controller.CloseBurnTable(); return; }
            if (!cursorInside) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            int mask = Physics.DefaultRaycastLayers & ~(1 << PaperSeizerStone.Presentation.Props.PlayerVisibilityLayer.Layer);
            if (!Physics.Raycast(controller.Camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 100, mask)) return;
            CardView card = hit.collider.GetComponentInParent<CardView>();
            if (card != null && records.ContainsKey(card) && preview != null) preview.Show(card, Input.mousePosition, controller.Camera);
            var action = hit.collider.GetComponentInParent<PaperSeizerStone.Presentation.Props.ShopTableAction>();
            if (engraving && action != null && action.Kind == PaperSeizerStone.Presentation.Props.ShopTableActionKind.Engrave)
                engravingStation.SetHover(true);
            if (!Input.GetMouseButtonDown(0)) return;
            if (engraving && action != null && action.Kind == PaperSeizerStone.Presentation.Props.ShopTableActionKind.Engrave)
            {
                CardTagAppearance entry = controller.Engravings.Find(selectedTag);
                if (entry != null && adventure.Wallet < entry.price) Notify("筹码不足");
                else if (selected != null) controller.EngraveCard(records[selected].RecordId, selectedTag);
                else Notify("请先选择一张牌。");
                return;
            }
            if (!inspectOnly && !engraving && action != null && action.Kind == PaperSeizerStone.Presentation.Props.ShopTableActionKind.Burn)
            {
                Notify("点击要烧毁的卡牌即可。");
                return;
            }
            if (card == null || !records.ContainsKey(card)) return;
            if (!inspectOnly && !engraving)
            {
                selected = card;
                controller.RemoveCard(records[card].RecordId);
                return;
            }
            if (selected != null)
            {
                selected.SetHighlight(false);
                if (engraving) selected.SetTag(records[selected].Tag);
                selected.transform.DOKill();
                selected.transform.DOMove(positions[selected], 0.15f);
            }
            selected = card;
            selectedOriginalTag = records[card].Tag;
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPickUp);
            selected.SetHighlight(!engraving);
            if (engraving) selected.SetTag(selectedTag);
            selected.transform.DOKill();
            selected.transform.DOMove(positions[selected] + Vector3.up * 0.14f, 0.15f);
            Refresh();
        }

        public IEnumerator Engrave(CardTag tag, int price)
        {
            CardView card = selected;
            // The authoritative purchase is already committed; show the old finish until the back faces up.
            // The original finish was saved when the card was selected, independently of the mutable record.
            card.SetTag(selectedOriginalTag);
            card.SetHitbox(false);
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, 0.45f, 0.05f));
            card.transform.DOKill();
            Quaternion home = card.transform.rotation;
            Vector3 homeScale = card.transform.localScale;
            card.transform.DORotateQuaternion(engravingStation.CardAnchor.rotation, 0.2f);
            card.transform.DOScale(Vector3.one * 0.95f, 0.2f);
            yield return card.transform.DOMove(engravingStation.CardAnchor.position, 0.2f).SetEase(Ease.InOutSine).WaitForCompletion();
            yield return card.transform.DOMove(engravingStation.CardAnchor.position + Vector3.up * engravingLift, 0.12f).WaitForCompletion();
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPickUp);
            yield return card.Engrave(tag, engravingFlipSeconds);
            yield return card.transform.DOMove(engravingStation.CardAnchor.position, 0.12f).WaitForCompletion();
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardPlace);
            card.transform.DORotateQuaternion(home, 0.2f);
            card.transform.DOScale(homeScale, 0.2f);
            yield return card.transform.DOMove(positions[card], 0.2f).SetEase(Ease.InOutSine).WaitForCompletion();
            card.SetHitbox(true);
            selectedOriginalTag = tag;
            yield return payment;
        }

        public IEnumerator Burn(int price)
        {
            CardView burning = selected;
            burning.SetHitbox(false);
            burning.SetHighlight(false);
            burning.transform.DOKill();
            Coroutine payment = StartCoroutine(walletArea.Pay(price, paymentExit, burnDuration * 0.65f, 0.05f));
            yield return burning.transform.DOMove(burnAnchor.position, burnDuration * 0.4f).SetEase(Ease.InOutSine).WaitForCompletion();
            PaperSeizerStone.Audio.AudioManager.Play(PaperSeizerStone.Audio.AudioCue.CardBurn);
            if (brazierFlame != null) brazierFlame.Pulse(1f, burnDuration * 0.6f);
            else emberGlow.DOPunchScale(glowScale * 0.65f, burnDuration * 0.6f, 2, 0.4f);
            burning.transform.DORotate(new Vector3(0, 120, 0), burnDuration * 0.6f, RotateMode.LocalAxisAdd);
            yield return burning.transform.DOScale(Vector3.zero, burnDuration * 0.6f).SetEase(Ease.InCubic).WaitForCompletion();
            burning.gameObject.SetActive(false);
            selected = null;
            yield return payment;
        }

        private void OnDisable()
        {
            if (controller != null) controller.ClearShopHint(this);
            if (preview != null) preview.Hide();
        }

        private void OnDestroy()
        {
            if (returnButton != null) returnButton.onClick.RemoveListener(ReturnToShop);
            if (preview != null) Destroy(preview.gameObject);
            foreach (CardView card in cards) if (card != null) card.transform.DOKill();
            if (emberGlow != null) emberGlow.DOKill();
        }
    }
}
