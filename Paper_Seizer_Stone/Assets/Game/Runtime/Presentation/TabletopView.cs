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
        [SerializeField] private PhysicsTokenArea chipArea;
        [SerializeField] private GameObject editorPreview;
        [SerializeField] private Transform opponentAnchor;
        [SerializeField] private GameObject defaultOpponent;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField, Tooltip("可选：未绑定时不显示桌面回合文字。")] private TMP_Text roundLabel;
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
        [Header("举枪与敌方紧张表现")]
        [SerializeField, Min(0.05f)] private float gunRaiseDuration = 0.3f;
        [SerializeField, Min(0)] private float aimHoldDuration = 0.65f;
        [SerializeField, Min(0)] private float enemyTremorPosition = 0.008f;
        [SerializeField, Min(0)] private float enemyTremorAngle = 0.65f;
        [SerializeField, Min(0.1f)] private float enemyTremorFrequency = 13;

        private readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        private readonly Stack<CardView> pool = new Stack<CardView>();
        private readonly List<CardView> retiring = new List<CardView>();
        private Dictionary<string, CardDefinition> definitions;
        private CardView cardPrefab;
        private BattleObservation displayed;
        private IReadOnlyList<int> draftHand;
        private IReadOnlyList<int> draftSlots;
        private bool expandedHand;
        private GameObject opponentInstance;

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
            if (prefab == null || cardRoot == null || dragAnchor == null || chipArea == null || opponentAnchor == null || defaultOpponent == null
                || confirmLabel == null || confirmButtonRenderer == null
                || activeButtonMaterial == null || inactiveButtonMaterial == null || audioSource == null)
                throw new InvalidOperationException("桌面预制体缺少卡牌、筹码、定位点、按钮或声音节点。");
            chipArea.Initialize();
            definitions = definition.GetCardAssets();
            cardPrefab = prefab;
            if (editorPreview != null) editorPreview.SetActive(false);
            player.MuzzleFlash.SetActive(false);
            enemy.MuzzleFlash.SetActive(false);
        }

        public void ResetEncounter(EnemyDefinition opponent)
        {
            foreach (CardView view in cards.Values) { view.ResetForPool(); pool.Push(view); }
            cards.Clear();
            ReleaseRetired();
            displayed = null;
            draftHand = null;
            draftSlots = null;
            expandedHand = false;
            Message = "下一位对手：" + opponent.DisplayName;
            chipArea.SetCount(0, true);
            foreach (SideTableBindings side in new[] { player, enemy })
            {
                side.BulletArea.SetCount(0, true);
                side.MuzzleFlash.SetActive(false);
                side.Gun.DOKill();
                side.Gun.SetPositionAndRotation(side.GunRestPose.position, side.GunRestPose.rotation);
            }
            if (opponentInstance != null)
            {
                opponentInstance.SetActive(false);
                Destroy(opponentInstance);
                opponentInstance = null;
            }
            defaultOpponent.SetActive(opponent.Appearance == null);
            if (opponent.Appearance != null) opponentInstance = Instantiate(opponent.Appearance, opponentAnchor);
        }

        public void PrepareExit()
        {
            foreach (CardView view in cards.Values) view.transform.DOKill();
            chipArea.Freeze();
            player.BulletArea.Freeze();
            enemy.BulletArea.Freeze();
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

        private float Apply(BattleObservation snapshot, bool resetBullets = false)
        {
            displayed = snapshot;
            float dropDuration = 0;
            for (int i = 0; i < 2; i++)
            {
                SideTableBindings nodes = Get((Side)i);
                FighterObservation fighter = snapshot.Get((Side)i);
                if (nodes.DrawLabel != null) nodes.DrawLabel.text = "牌库\n" + fighter.DrawCount.ToString("00");
                if (nodes.DiscardLabel != null) nodes.DiscardLabel.text = "弃牌\n" + fighter.DiscardCount.ToString("00");
                if (nodes.ScoreLabel != null) nodes.ScoreLabel.text = (i == 0 ? "你" : "对手") + "    " + fighter.Score + " 分    ·    生命 " + fighter.Life;
                nodes.DrawPile.SetCount(fighter.DrawCount);
                dropDuration = Mathf.Max(dropDuration, nodes.BulletArea.SetCount(fighter.Bullets, resetBullets));
            }
            dropDuration = Mathf.Max(dropDuration, chipArea.SetCount(snapshot.Chips));
            if (roundLabel != null) roundLabel.text = "第 " + snapshot.Round + " 回合   ·   揭牌 " + snapshot.RevealStep;
            confirmLabel.text = snapshot.Phase == BattlePhase.Planning ? "确认出牌"
                : snapshot.Phase == BattlePhase.PlayerCheat ? "跳过作弊" : snapshot.Phase == BattlePhase.BattleEnd ? snapshot.PlayerWon ? "胜利" : "重新开始" : "请稍候";
            bool active = snapshot.Phase == BattlePhase.Planning || snapshot.Phase == BattlePhase.PlayerCheat || snapshot.Phase == BattlePhase.BattleEnd;
            confirmButtonRenderer.sharedMaterial = active ? activeButtonMaterial : inactiveButtonMaterial;
            return dropDuration;
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
            float dropDuration = Apply(battleEvent.Snapshot, battleEvent.Kind == BattleEventKind.RoundStarted);
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
            if (battleEvent.Kind == BattleEventKind.AbilityTriggered || battleEvent.Kind == BattleEventKind.CardTransformed)
            {
                CardView effectCard = FindCard(battleEvent.CardId);
                if (effectCard != null) effectCard.Pulse();
            }
            float duration = battleEvent.Kind == BattleEventKind.LaneScored ? 0.55f
                : battleEvent.Kind == BattleEventKind.RoundEnded ? 0.6f : moveDuration;
            yield return new WaitForSeconds(Mathf.Max(dropDuration, Mathf.Max(moveDuration, duration)));
            ReleaseRetired();
        }

        public IEnumerator PresentCheats(IReadOnlyList<BattleEvent> playerEvents, IReadOnlyList<BattleEvent> enemyEvents)
        {
            // Both rule actions have resolved. Display one combined board instead of replaying intermediate boards.
            BattleObservation finalSnapshot = enemyEvents[enemyEvents.Count - 1].Snapshot;
            BattleObservation actionSnapshot = finalSnapshot;
            for (int i = enemyEvents.Count - 1; i >= 0; i--)
                if (enemyEvents[i].Kind != BattleEventKind.PhaseChanged)
                {
                    actionSnapshot = enemyEvents[i].Snapshot;
                    break;
                }
            Message = CheatMessage(playerEvents) + "  " + CheatMessage(enemyEvents);
            draftHand = null;
            draftSlots = null;
            float dropDuration = Apply(actionSnapshot);
            Layout(moveDuration);
            PulseTransforms(playerEvents);
            PulseTransforms(enemyEvents);
            if (HasCardMotion(playerEvents) || HasCardMotion(enemyEvents)) PlaySound(cardSound);
            yield return new WaitForSeconds(Mathf.Max(moveDuration, dropDuration));
            Apply(finalSnapshot);
            ReleaseRetired();
        }

        private static string CheatMessage(IReadOnlyList<BattleEvent> events)
        {
            string message = "";
            foreach (BattleEvent action in events)
                if (action.Kind == BattleEventKind.CardsSwapped || action.Kind == BattleEventKind.CardDrawn
                    || action.Kind == BattleEventKind.DecisionPassed || action.Kind == BattleEventKind.CardTransformed) message += action.Message + " ";
            return message;
        }

        private void PulseTransforms(IReadOnlyList<BattleEvent> events)
        {
            foreach (BattleEvent action in events)
                if (action.Kind == BattleEventKind.CardTransformed)
                {
                    CardView view = FindCard(action.CardId);
                    if (view != null) view.Pulse();
                }
        }

        private static bool HasCardMotion(IReadOnlyList<BattleEvent> events)
        {
            foreach (BattleEvent action in events)
                if (action.Kind == BattleEventKind.CardsSwapped || action.Kind == BattleEventKind.CardDrawn) return true;
            return false;
        }

        public IEnumerator PrepareShooting()
        {
            bool enemyFires = displayed.Enemy.Score >= displayed.Player.Score;
            foreach (SideTableBindings side in new[] { player, enemy })
            {
                bool fires = side == enemy ? enemyFires : displayed.Player.Score >= displayed.Enemy.Score;
                side.Gun.DOKill();
                if (!fires) continue;
                side.Gun.DOMove(side.GunAimPose.position, gunRaiseDuration);
                side.Gun.DORotateQuaternion(side.GunAimPose.rotation, gunRaiseDuration);
            }
            yield return new WaitForSeconds(gunRaiseDuration);
            if (enemyFires) enemy.Gun.DOKill();
            for (float elapsed = 0; elapsed < aimHoldDuration; elapsed += Time.deltaTime)
            {
                if (enemyFires)
                {
                    float time = elapsed * enemyTremorFrequency;
                    Vector3 noise = new Vector3(Tremor(time, 1), Tremor(time, 19), Tremor(time, 47));
                    enemy.Gun.position = enemy.GunAimPose.position + enemy.GunAimPose.TransformDirection(noise * enemyTremorPosition);
                    enemy.Gun.rotation = enemy.GunAimPose.rotation * Quaternion.Euler(noise * enemyTremorAngle);
                }
                yield return null;
            }
            if (enemyFires) enemy.Gun.SetPositionAndRotation(enemy.GunAimPose.position, enemy.GunAimPose.rotation);
        }

        private static float Tremor(float time, float offset) => Mathf.PerlinNoise(time, offset) * 2 - 1;

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
