using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;

namespace PaperSeizerStone.Presentation
{
    // References an authored tabletop prefab. Never constructs the scene or its materials.
    public sealed class TabletopView : MonoBehaviour
    {
        [SerializeField] private SideTableBindings player;
        [SerializeField] private SideTableBindings enemy;
        [SerializeField] private Transform cardRoot;
        [SerializeField] private Transform dragAnchor;
        [SerializeField] private Transform chipRoot;
        [SerializeField] private GameObject editorPreview;
        [SerializeField] private GameObject chipPrefab;
        [SerializeField] private List<GameObject> chipModels = new List<GameObject>();
        [SerializeField] private Vector3 chipSpacing = new Vector3(0.5f, 0.07f, 0);
        [SerializeField, Min(1)] private int chipColumns = 2;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private TMP_Text roundLabel;
        [SerializeField] private Renderer confirmButtonRenderer;
        [SerializeField] private Material activeButtonMaterial;
        [SerializeField] private Material inactiveButtonMaterial;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip cardSound;
        [SerializeField] private AudioClip shotSound;
        [SerializeField] private AudioClip emptySound;
        [SerializeField, Min(0.05f)] private float moveDuration = 0.24f;
        [Header("手牌布局（相对于手牌定位点）")]
        [SerializeField] private float handSpacing = 0.58f;
        [SerializeField] private float expandedSpacing = 0.85f;
        [SerializeField] private float maximumHandWidth = 4.7f;
        [SerializeField] private float handFanDepth = 0.2f;
        [SerializeField] private float handFanAngle = 24;

        private readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        private readonly Stack<CardView> pool = new Stack<CardView>();
        private readonly List<CardView> retiring = new List<CardView>();
        private Dictionary<string, CardDefinition> definitions;
        private CardView cardPrefab;
        private BattleObservation displayed;
        private IReadOnlyList<int> draftHand;
        private IReadOnlyList<int> draftSlots;
        private bool expandedHand;

        public string Message { get; private set; }
        public BattleObservation Displayed => displayed;
        public Transform DragAnchor => dragAnchor;
        public bool HandExpanded => expandedHand;
        public CardView FindCard(int id) { cards.TryGetValue(id, out CardView view); return view; }
        public void Notify(string message) { Message = message; }
        private SideTableBindings Get(Side side) => side == Side.Player ? player : enemy;

        public void Initialize(BattleDefinition definition, CardView prefab)
        {
            if (player == null || enemy == null) throw new InvalidOperationException("请配置双方桌面节点。");
            player.RequireAssigned("玩家");
            enemy.RequireAssigned("对手");
            if (prefab == null || cardRoot == null || dragAnchor == null || chipRoot == null || chipPrefab == null
                || confirmLabel == null || roundLabel == null || confirmButtonRenderer == null
                || activeButtonMaterial == null || inactiveButtonMaterial == null || audioSource == null)
                throw new InvalidOperationException("桌面预制体缺少卡牌、筹码、定位点、按钮或声音节点。");
            foreach (GameObject chip in chipModels)
                if (chip == null) throw new InvalidOperationException("筹码列表中有未绑定的物体。");
            definitions = definition.GetCardAssets();
            cardPrefab = prefab;
            if (editorPreview != null) editorPreview.SetActive(false);
            player.MuzzleFlash.SetActive(false);
            enemy.MuzzleFlash.SetActive(false);
        }

        private sealed class Placement
        {
            public ObservedCard Card;
            public Side Owner;
            public bool Board;
            public int Index;
            public int Count;
        }

        private List<Placement> Placements()
        {
            var result = new List<Placement>();
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                Side side = (Side)sideIndex;
                FighterObservation fighter = displayed.Get(side);
                if (side == Side.Player && draftHand != null)
                {
                    var available = new Dictionary<int, ObservedCard>();
                    foreach (ObservedCard card in fighter.Hand) available.Add(card.Id, card);
                    for (int i = 0; i < draftHand.Count; i++)
                        result.Add(new Placement { Card = available[draftHand[i]], Owner = side, Index = i, Count = draftHand.Count });
                    for (int i = 0; i < draftSlots.Count; i++)
                        if (draftSlots[i] != 0)
                            result.Add(new Placement { Card = available[draftSlots[i]], Owner = side, Board = true, Index = i });
                }
                else
                {
                    for (int i = 0; i < fighter.Hand.Count; i++)
                        result.Add(new Placement { Card = fighter.Hand[i], Owner = side, Index = i, Count = fighter.Hand.Count });
                    for (int i = 0; i < fighter.Board.Count; i++)
                        if (fighter.Board[i] != null)
                            result.Add(new Placement { Card = fighter.Board[i], Owner = side, Board = true, Index = i });
                }
            }
            return result;
        }

        private Vector3 Position(Placement placement)
        {
            SideTableBindings side = Get(placement.Owner);
            if (placement.Board) return side.Slots[placement.Index].position;
            float normalized = placement.Count < 2 ? 0 : (float)placement.Index / (placement.Count - 1) - 0.5f;
            float spacing = expandedHand && placement.Owner == Side.Player ? expandedSpacing : handSpacing;
            float spread = Mathf.Min(placement.Count * spacing, maximumHandWidth);
            return side.HandAnchor.TransformPoint(new Vector3(normalized * spread, placement.Index * 0.008f, -Mathf.Abs(normalized) * handFanDepth));
        }

        private Quaternion Rotation(Placement placement)
        {
            SideTableBindings side = Get(placement.Owner);
            if (placement.Board) return side.Slots[placement.Index].rotation;
            float normalized = placement.Count < 2 ? 0 : (float)placement.Index / (placement.Count - 1) - 0.5f;
            return side.HandAnchor.rotation * Quaternion.Euler(0, normalized * -handFanAngle, 0);
        }

        private void Layout(float duration)
        {
            if (displayed == null) return;
            var active = new HashSet<int>();
            bool interactive = displayed.Phase == BattlePhase.Planning
                || (displayed.Phase == BattlePhase.PlayerCheat && displayed.Enemy.Bullets < BasicRules.Chambers);
            foreach (Placement placement in Placements())
            {
                active.Add(placement.Card.Id);
                if (!cards.TryGetValue(placement.Card.Id, out CardView view))
                {
                    if (pool.Count > 0) view = pool.Pop();
                    else
                    {
                        view = Instantiate(cardPrefab, cardRoot);
                        view.Initialize();
                    }
                    view.gameObject.SetActive(true);
                    view.transform.SetPositionAndRotation(Get(placement.Owner).DrawAnchor.position, Get(placement.Owner).DrawAnchor.rotation);
                    cards.Add(placement.Card.Id, view);
                }
                CardDefinition definition = null;
                if (placement.Card.DefinitionId != null) definitions.TryGetValue(placement.Card.DefinitionId, out definition);
                view.name = "Card_" + placement.Card.Id;
                view.Display(placement.Card, definition, placement.Owner, placement.Board, interactive && placement.Owner == Side.Player);
                view.MoveTo(Position(placement), Rotation(placement), duration);
            }
            var removed = new List<int>();
            foreach (var pair in cards)
            {
                if (active.Contains(pair.Key)) continue;
                Transform discard = Get(pair.Value.Owner).DiscardAnchor;
                pair.Value.SetHitbox(false);
                pair.Value.MoveTo(discard.position, discard.rotation, duration);
                retiring.Add(pair.Value);
                removed.Add(pair.Key);
            }
            foreach (int id in removed) cards.Remove(id);
        }

        private void ReleaseRetired()
        {
            foreach (CardView view in retiring) { view.ResetForPool(); pool.Push(view); }
            retiring.Clear();
        }

        private void Apply(BattleObservation snapshot)
        {
            displayed = snapshot;
            for (int i = 0; i < 2; i++)
            {
                SideTableBindings nodes = Get((Side)i);
                FighterObservation fighter = snapshot.Get((Side)i);
                nodes.DrawLabel.text = "牌库\n" + fighter.DrawCount.ToString("00");
                nodes.DiscardLabel.text = "弃牌\n" + fighter.DiscardCount.ToString("00");
                nodes.ScoreLabel.text = (i == 0 ? "你" : "对手") + "    " + fighter.Score + " 分    ·    生命 " + fighter.Life;
                for (int bullet = 0; bullet < BasicRules.Chambers; bullet++) nodes.Bullets[bullet].SetActive(bullet < fighter.Bullets);
            }
            // Only additional gameplay tokens are pooled from a saved prefab; the table itself is static.
            while (chipModels.Count < snapshot.Chips)
            {
                int i = chipModels.Count;
                GameObject chip = Instantiate(chipPrefab, chipRoot);
                chip.transform.localPosition = new Vector3(-(i % chipColumns) * chipSpacing.x, (i / chipColumns) * chipSpacing.y, (i / chipColumns) * chipSpacing.z);
                chipModels.Add(chip);
            }
            for (int i = 0; i < chipModels.Count; i++) chipModels[i].SetActive(i < snapshot.Chips);
            roundLabel.text = "第 " + snapshot.Round + " 回合   ·   揭牌 " + snapshot.RevealStep;
            confirmLabel.text = snapshot.Phase == BattlePhase.Planning ? "确认出牌"
                : snapshot.Phase == BattlePhase.PlayerCheat ? "跳过作弊" : snapshot.Phase == BattlePhase.BattleEnd ? "再来一局" : "请稍候";
            bool active = snapshot.Phase == BattlePhase.Planning || snapshot.Phase == BattlePhase.PlayerCheat || snapshot.Phase == BattlePhase.BattleEnd;
            confirmButtonRenderer.sharedMaterial = active ? activeButtonMaterial : inactiveButtonMaterial;
        }

        private void PlaySound(AudioClip clip) { if (clip != null) audioSource.PlayOneShot(clip); }

        public IEnumerator Present(BattleEvent battleEvent)
        {
            Message = battleEvent.Message;
            CardView flipping = battleEvent.Kind == BattleEventKind.CardRevealed ? FindCard(battleEvent.CardId) : null;
            if (flipping != null)
            {
                flipping.Tilt(88, 0.12f);
                yield return new WaitForSeconds(0.12f);
            }
            draftHand = null;
            draftSlots = null;
            Apply(battleEvent.Snapshot);
            Layout(moveDuration);
            if (flipping != null) flipping.Tilt(0, 0.14f);
            if (battleEvent.Kind == BattleEventKind.CardDrawn || flipping != null || battleEvent.Kind == BattleEventKind.CardsSwapped)
                PlaySound(cardSound);
            if (battleEvent.Kind == BattleEventKind.RoundStarted)
                foreach (SideTableBindings side in new[] { player, enemy })
                {
                    side.Gun.DOKill();
                    side.Gun.DOMove(side.GunRestPose.position, moveDuration);
                    side.Gun.DORotateQuaternion(side.GunRestPose.rotation, moveDuration);
                }
            if (battleEvent.Kind == BattleEventKind.LaneScored)
                foreach (FighterObservation fighter in new[] { displayed.Player, displayed.Enemy })
                {
                    CardView view = FindCard(fighter.Board[battleEvent.Lane].Id);
                    if (view != null) view.Pulse();
                }
            float duration = battleEvent.Kind == BattleEventKind.LaneScored ? 0.55f
                : battleEvent.Kind == BattleEventKind.RoundEnded ? 0.6f : moveDuration;
            yield return new WaitForSeconds(Mathf.Max(moveDuration, duration));
            ReleaseRetired();
        }

        public IEnumerator PrepareShooting()
        {
            foreach (SideTableBindings side in new[] { player, enemy })
            {
                side.Gun.DOMove(side.GunAimPose.position, 0.3f);
                side.Gun.DORotateQuaternion(side.GunAimPose.rotation, 0.3f);
            }
            yield return new WaitForSeconds(0.3f);
        }

        public IEnumerator PresentShots(IReadOnlyList<BattleEvent> shots)
        {
            Apply(shots[0].Snapshot);
            Message = shots.Count == 2 ? shots[0].Message + "  " + shots[1].Message : shots[0].Message;
            foreach (BattleEvent shot in shots)
            {
                SideTableBindings side = Get(shot.Side);
                side.Gun.DOPunchPosition(side.Gun.up * (shot.Hit ? 0.22f : 0.045f), 0.3f, 2, 0.3f);
                PlaySound(shot.Hit ? shotSound : emptySound);
                side.MuzzleFlash.SetActive(shot.Hit);
            }
            yield return new WaitForSeconds(0.08f);
            player.MuzzleFlash.SetActive(false);
            enemy.MuzzleFlash.SetActive(false);
            yield return new WaitForSeconds(0.92f);
        }

        public void ShowDraft(IReadOnlyList<int> hand, IReadOnlyList<int> slots)
        {
            draftHand = hand;
            draftSlots = slots;
            Layout(moveDuration);
            ReleaseRetired();
        }

        public void RestoreLayout() { Layout(moveDuration); }
        public void SetHandExpanded(bool expanded)
        {
            if (expandedHand == expanded || displayed == null) return;
            expandedHand = expanded;
            Layout(0.15f);
        }

        private void OnDestroy()
        {
            if (player != null && player.Gun != null) player.Gun.DOKill();
            if (enemy != null && enemy.Gun != null) enemy.Gun.DOKill();
        }
    }
}
