using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Audio;

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
        [SerializeField] private FirstPersonHandView firstPersonHand;
        [SerializeField] private PlayerShootingSequence playerShooting;
        [SerializeField] private Transform opponentAnchor;
        [SerializeField] private Transform scoreCounter;
        [SerializeField] private GameObject defaultOpponent;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField, Tooltip("可选：未绑定时不显示桌面回合文字。")] private TMP_Text roundLabel;
        [SerializeField] private Props.TableBellView confirmBell;
        [SerializeField, Min(0.05f)] private float moveDuration = 0.24f;
        [Header("手牌布局（相对于手牌定位点）")]
        [SerializeField] private float handSpacing = 0.58f;
        [SerializeField] private float maximumHandWidth = 4.7f;
        [SerializeField] private float handFanDepth = 0.2f;
        [SerializeField] private float handFanAngle = 24;
        [Header("弃牌散落（相对于弃牌定位点）")]
        [SerializeField] private Vector2 discardSpread = new Vector2(0.25f, 0.2f);
        [SerializeField, Range(0, 90)] private float discardAngle = 38;
        [SerializeField, Min(0.03f)] private float discardLayerHeight = 0.032f;
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
        private Transform fallenOpponent;
        private Vector3 opponentStandingPosition;
        private Quaternion opponentStandingRotation;
        private string shootingFallbackReason;

        public string Message { get; private set; }
        public Transform ScoreCounter => scoreCounter;
        private ScoreCounterView scoreCounterView;
        public BattleObservation Displayed => displayed;
        public Transform DragAnchor => dragAnchor;
        public bool HandExpanded => expandedHand;
        public CardView FindCard(int id) { cards.TryGetValue(id, out CardView view); return view; }
        public void Notify(string message) { Message = message; }
        private SideTableBindings Get(Side side) => side == Side.Player ? player : enemy;

        private BattleCameraRig shotCameraRig;
        private bool postShotPresentation;
        private bool handsWereVisible;

        public void Initialize(BattleDefinition definition, CardView prefab, Camera camera)
        {
            if (player == null || enemy == null) throw new InvalidOperationException("请配置双方桌面节点。");
            player.RequireAssigned("玩家");
            enemy.RequireAssigned("对手");
            if (prefab == null || camera == null || firstPersonHand == null || cardRoot == null || dragAnchor == null || chipArea == null || opponentAnchor == null || defaultOpponent == null
                || confirmLabel == null || confirmBell == null)
                throw new InvalidOperationException("桌面预制体缺少卡牌、筹码、定位点或按钮节点。");
            chipArea.Initialize();
            definitions = definition.GetCardAssets();
            cardPrefab = prefab;
            player.DrawPile.ConfigureBacks(prefab, Side.Player);
            enemy.DrawPile.ConfigureBacks(prefab, Side.Enemy);
            shotCameraRig = camera != null ? camera.GetComponentInParent<BattleCameraRig>() : null;
            scoreCounterView = scoreCounter != null ? scoreCounter.GetComponent<ScoreCounterView>() : null;
            firstPersonHand.Initialize(camera);
            PlayerShootingSequence gripShooting = firstPersonHand.Grip != null
                ? firstPersonHand.Grip.GetComponent<PlayerShootingSequence>() : null;
            if (gripShooting != null) playerShooting = gripShooting;
            else if (playerShooting == null) playerShooting = FindObjectOfType<PlayerShootingSequence>(true);
            // Capture the authored gun pose before the first transition; scene overrides are intentional.
            foreach (SideTableBindings side in new[] { player, enemy })
                side.GunRestPose.SetPositionAndRotation(side.Gun.position, side.Gun.rotation);
            if (editorPreview != null) editorPreview.SetActive(false);
            player.MuzzleFlash.SetActive(false);
            enemy.MuzzleFlash.SetActive(false);
        }

        public void ResetEncounter(EnemyDefinition opponent)
        {
            RestoreOpponentPose();
            if (scoreCounterView != null) scoreCounterView.ResetDisplay();
            EndPostShotPresentation();
            if (playerShooting != null) playerShooting.Cancel();
            firstPersonHand.ResetHand();
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
            if (playerShooting != null) playerShooting.Cancel();
            firstPersonHand.Clear();
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
            public bool Discard;
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
                for (int i = 0; i < fighter.Discard.Count; i++)
                    result.Add(new Placement { Card = fighter.Discard[i], Owner = side, Discard = true, Index = i });
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
            if (placement.Discard)
                return side.DiscardAnchor.position + side.DiscardAnchor.rotation * new Vector3(
                    Scatter(placement.Card.Id, 1) * discardSpread.x,
                    placement.Index * discardLayerHeight,
                    Scatter(placement.Card.Id, 2) * discardSpread.y);
            if (placement.Board) return side.Slots[placement.Index].position;
            float normalized = placement.Count < 2 ? 0 : (float)placement.Index / (placement.Count - 1) - 0.5f;
            float spacing = handSpacing;
            float spread = Mathf.Min(placement.Count * spacing, maximumHandWidth);
            return side.HandAnchor.TransformPoint(new Vector3(normalized * spread, placement.Index * 0.008f, -Mathf.Abs(normalized) * handFanDepth));
        }

        private Quaternion Rotation(Placement placement)
        {
            SideTableBindings side = Get(placement.Owner);
            if (placement.Discard)
                return side.DiscardAnchor.rotation * Quaternion.Euler(0, Scatter(placement.Card.Id, 3) * discardAngle, 0);
            if (placement.Board) return side.Slots[placement.Index].rotation;
            float normalized = placement.Count < 2 ? 0 : (float)placement.Index / (placement.Count - 1) - 0.5f;
            return side.HandAnchor.rotation * Quaternion.Euler(0, normalized * -handFanAngle, 0);
        }

        // Stable scatter must not consume the rule engine's random sequence or change on every layout.
        private static float Scatter(int id, int salt)
        {
            unchecked
            {
                uint hash = (uint)(id * 73856093 ^ salt * 19349663);
                hash ^= hash >> 16;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return (hash & 65535) / 32767.5f - 1;
            }
        }

        public void SetConfirmInteraction(bool enabled)
        {
            if (confirmBell != null) confirmBell.SetAvailable(enabled && displayed != null &&
                (displayed.Phase == BattlePhase.Planning || displayed.Phase == BattlePhase.PlayerCheat ||
                 (displayed.Phase == BattlePhase.BattleEnd && !displayed.PlayerWon)));
        }

        public void PressConfirm() { confirmBell.Press(); }

        private void Layout(float duration)
        {
            if (displayed == null) return;
            firstPersonHand.Clear();
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
                view.Display(placement.Card, definition, placement.Owner, placement.Board, !placement.Discard && interactive && placement.Owner == Side.Player);
                view.SetHitbox(!placement.Discard);
                if (placement.Owner == Side.Player && !placement.Board && !placement.Discard)
                    firstPersonHand.Register(view, placement.Index, placement.Count);
                else
                {
                    view.MoveTo(Position(placement), Rotation(placement), duration);
                    view.transform.DOScale(Vector3.one, duration);
                }
            }
            var removed = new List<int>();
            foreach (var pair in cards)
            {
                if (active.Contains(pair.Key)) continue;
                // A card missing from all visible areas was shuffled into the draw pile.
                Transform discard = Get(pair.Value.Owner).DrawAnchor;
                pair.Value.SetHitbox(false);
                pair.Value.MoveTo(discard.position, discard.rotation, duration);
                pair.Value.transform.DOScale(Vector3.one, duration);
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
            if (displayed != null && !resetBullets &&
                (snapshot.Player.Bullets < displayed.Player.Bullets || snapshot.Enemy.Bullets < displayed.Enemy.Bullets))
                AudioManager.Play(AudioCue.AmmunitionRemove);
            displayed = snapshot;
            if (scoreCounterView != null) scoreCounterView.Observe(snapshot);
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
            SetConfirmInteraction(false);
            return dropDuration;
        }

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
            if (flipping != null) AudioManager.Play(AudioCue.CardReveal);
            else if (battleEvent.Kind == BattleEventKind.CardDrawn) AudioManager.Play(AudioCue.CardDraw);
            else if (battleEvent.Kind == BattleEventKind.CardsSwapped || battleEvent.Kind == BattleEventKind.LayoutCommitted)
                AudioManager.Play(AudioCue.CardPlace);
            else if (battleEvent.Kind == BattleEventKind.DeckShuffled) AudioManager.Play(AudioCue.CardShuffle);
            else if (battleEvent.Kind == BattleEventKind.RoundEnded) AudioManager.Play(AudioCue.CardPlace);
            else if (battleEvent.Kind == BattleEventKind.BattleEnded)
                AudioManager.Play(displayed.PlayerWon ? AudioCue.Victory : AudioCue.Defeat);
            if (battleEvent.Kind == BattleEventKind.RoundStarted)
                foreach (SideTableBindings side in new[] { player, enemy })
                {
                    side.Gun.DOKill();
                    side.Gun.DOMove(side.GunRestPose.position, moveDuration);
                    side.Gun.DORotateQuaternion(side.GunRestPose.rotation, moveDuration);
                }
            if (battleEvent.Kind == BattleEventKind.LaneScored)
            {
                AudioManager.Play(AudioCue.ScoreTick);
                foreach (FighterObservation fighter in new[] { displayed.Player, displayed.Enemy })
                {
                    CardView view = FindCard(fighter.Board[battleEvent.Lane].Id);
                    if (view != null) view.Pulse();
                }
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
            foreach (BattleEvent action in playerEvents)
                if (action.Kind == BattleEventKind.CardDrawn && action.Side == Side.Player)
                {
                    player.DrawPile.PulseOutline();
                    break;
                }
            float dropDuration = Apply(actionSnapshot);
            Layout(moveDuration);
            PulseTransforms(playerEvents);
            PulseTransforms(enemyEvents);
            PlayCheatSound(playerEvents, enemyEvents);
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

        private static void PlayCheatSound(IReadOnlyList<BattleEvent> playerEvents, IReadOnlyList<BattleEvent> enemyEvents)
        {
            bool draw = false;
            bool swap = false;
            foreach (IReadOnlyList<BattleEvent> events in new[] { playerEvents, enemyEvents })
                foreach (BattleEvent action in events)
                {
                    draw |= action.Kind == BattleEventKind.CardDrawn;
                    swap |= action.Kind == BattleEventKind.CardsSwapped;
                    if (action.Kind == BattleEventKind.DeckShuffled) AudioManager.Play(AudioCue.CardShuffle);
                }
            if (draw) AudioManager.Play(AudioCue.CardDraw);
            if (swap) AudioManager.Play(AudioCue.CardPlace);
        }

        public IEnumerator PrepareShooting()
        {
            AudioManager.SetShootingFocus(true);
            bool playerFires = displayed.Player.Score >= displayed.Enemy.Score;
            bool enemyFires = displayed.Enemy.Score >= displayed.Player.Score;
            bool animatedPlayer = playerFires && playerShooting != null && playerShooting.isActiveAndEnabled && playerShooting.IsConfigured;
            if (playerFires)
            {
                string problem = playerShooting == null ? "未找到玩家 PlayerShootingSequence。"
                    : !playerShooting.isActiveAndEnabled ? "玩家 PlayerShootingSequence 或其对象未启用。"
                    : playerShooting.ConfigurationProblem;
                if (!string.IsNullOrEmpty(problem) && problem != shootingFallbackReason)
                    Debug.LogWarning("玩家开枪动画未接管：" + problem + "本次使用原枪械表现。", this);
                shootingFallbackReason = problem;
            }
            Coroutine playerPreparation = animatedPlayer ? StartCoroutine(playerShooting.Prepare(firstPersonHand, player.Gun)) : null;
            if (enemyFires || (playerFires && !animatedPlayer)) AudioManager.Play(AudioCue.GunMechanism);
            foreach (SideTableBindings side in new[] { player, enemy })
            {
                bool fires = side == enemy ? enemyFires : playerFires;
                side.Gun.DOKill();
                if (!fires || (side == player && animatedPlayer)) continue;
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
            if (playerPreparation != null) yield return playerPreparation;
        }

        private static float Tremor(float time, float offset) => Mathf.PerlinNoise(time, offset) * 2 - 1;

        public IEnumerator RevealCounterScores()
        {
            if (scoreCounterView == null || displayed == null) yield break;
            scoreCounterView.RevealScores(displayed);
            yield return new WaitForSecondsRealtime(scoreCounterView.FillSeconds);
            AudioManager.Play(AudioCue.ScoreReveal);
            yield return new WaitForSecondsRealtime(scoreCounterView.RevealSeconds - scoreCounterView.FillSeconds);
        }

        public void RestoreOpponentForNextRound() => RestoreOpponentPose();

        private void BeginPostShotPresentation()
        {
            if (postShotPresentation) return;
            postShotPresentation = true;
            firstPersonHand.SetPostShotSuspended(true);
            if (shotCameraRig == null) return;
            handsWereVisible = shotCameraRig.ShowPlayerHands;
            shotCameraRig.ShowPlayerHands = false;
        }

        public void EndPostShotPresentation()
        {
            AudioManager.SetShootingFocus(false);
            if (!postShotPresentation) return;
            postShotPresentation = false;
            firstPersonHand.SetPostShotSuspended(false);
            if (shotCameraRig != null) shotCameraRig.ShowPlayerHands = handsWereVisible;
        }

        public IEnumerator PresentShots(IReadOnlyList<BattleEvent> shots)
        {
            Apply(shots[0].Snapshot);
            Message = shots.Count == 2 ? shots[0].Message + "  " + shots[1].Message : shots[0].Message;
            bool animatedPlayer = playerShooting != null && playerShooting.IsActive;
            bool hit = false;
            float firstHitTime = 0f;
            Coroutine knockdown = null;
            bool playerHit = false;
            foreach (BattleEvent shot in shots) playerHit |= shot.Side == Side.Enemy && shot.Hit;
            bool playerKilled = shots[shots.Count - 1].Snapshot.Player.Life <= 0;
            foreach (BattleEvent shot in shots)
            {
                if (!shot.Hit)
                {
                    AudioManager.Play(AudioCue.GunEmpty);
                    continue;
                }
                SideTableBindings side = Get(shot.Side);
                bool usesAnimatedGun = shot.Side == Side.Player && animatedPlayer;
                GameObject muzzle = usesAnimatedGun ? playerShooting.MuzzleFlash : side.MuzzleFlash;
                muzzle.SetActive(false);
                if (!usesAnimatedGun)
                    side.Gun.DOPunchPosition(side.Gun.up * 0.22f, 0.3f, 2, 0.3f);
                Vector3 tracerTarget = shot.Side == Side.Player
                    ? opponentAnchor.position + opponentAnchor.up * 0.55f
                    : player.HandAnchor.position + player.HandAnchor.up * 0.35f;
                StartCoroutine(ShowDischarge(muzzle, tracerTarget, shot.Side == Side.Enemy));
                if (!hit)
                {
                    firstHitTime = Time.unscaledTime;
                    if (!playerHit && shotCameraRig != null) shotCameraRig.ShakeForShot();
                }
                if (shot.Side == Side.Enemy && shotCameraRig != null)
                {
                    BeginPostShotPresentation();
                    StartCoroutine(shotCameraRig.PlayPlayerFall(playerKilled));
                }
                hit = true;
                if (shot.Side == Side.Player && knockdown == null)
                {
                    Message += "  对手中枪倒地。";
                    knockdown = StartCoroutine(KnockDownOpponent());
                    if (!playerHit && shotCameraRig != null) StartCoroutine(shotCameraRig.FollowOpponentFall());
                }
                AudioManager.Play(AudioCue.GunFire);
            }
            yield return new WaitForSecondsRealtime(0.08f);
            if (hit) AudioManager.Play(AudioCue.Hit);
            player.MuzzleFlash.SetActive(false);
            enemy.MuzzleFlash.SetActive(false);
            if (animatedPlayer) playerShooting.SetMuzzleFlash(false);
            BeginPostShotPresentation();

            if (animatedPlayer)
            {
                yield return playerShooting.Finish();
            }
            else yield return new WaitForSeconds(0.92f);

            if (knockdown != null && fallenOpponent != null && fallenOpponent.gameObject.activeSelf)
                yield return knockdown;

            if (hit && shotCameraRig != null)
            {
                float remaining = BattleCameraRig.HitBuildUpSeconds - (Time.unscaledTime - firstHitTime);
                if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
                shotCameraRig.FlashHit();
                while (shotCameraRig.IsHitEffectPlaying) yield return null;
            }
        }

        private IEnumerator ShowDischarge(GameObject muzzle, Vector3 target, bool towardPlayer)
        {
            const float trailDuration = 0.033f;
            const float flashDuration = 0.14f;
            var effect = new GameObject("Muzzle Discharge");
            effect.layer = gameObject.layer;
            effect.transform.SetParent(muzzle.transform.parent, false);
            effect.transform.SetPositionAndRotation(muzzle.transform.position, muzzle.transform.rotation);

            var light = effect.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.69f, 0.32f);
            light.range = 3.8f;
            light.shadows = LightShadows.None;

            LineRenderer trail = null;
            LineRenderer burst = null;
            var flash = muzzle.GetComponentInChildren<Renderer>(true);
            Vector3 origin = muzzle.transform.position;
            if (flash != null && flash.sharedMaterial != null && (target - origin).sqrMagnitude > 0.001f)
            {
                trail = effect.AddComponent<LineRenderer>();
                trail.useWorldSpace = true;
                trail.sharedMaterial = flash.sharedMaterial;
                trail.positionCount = 2;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.28f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.SetPosition(0, origin);
                // Stop the streak short of the victim so it cannot bloom across a near camera.
                Vector3 direction = (target - origin).normalized;
                trail.SetPosition(1, towardPlayer
                    ? origin + direction * Mathf.Max(0.1f, Vector3.Distance(origin, target) - 0.65f)
                    : target);

                var flame = new GameObject("Muzzle Flame");
                flame.layer = effect.layer;
                flame.transform.SetParent(effect.transform, false);
                burst = flame.AddComponent<LineRenderer>();
                burst.useWorldSpace = true;
                burst.sharedMaterial = flash.sharedMaterial;
                burst.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.55f);
                burst.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                burst.receiveShadows = false;
                Vector3 forward = direction;
                Vector3 spread = Vector3.Cross(forward, Vector3.up).normalized;
                if (spread.sqrMagnitude < 0.01f) spread = Vector3.right;
                Vector3 rise = Vector3.Cross(spread, forward).normalized;
                burst.positionCount = 9;
                burst.SetPositions(new[]
                {
                    origin + forward * 0.12f + spread * 0.11f, origin, origin + forward * 0.31f,
                    origin, origin + forward * 0.11f - spread * 0.10f,
                    origin, origin + forward * 0.10f + rise * 0.095f,
                    origin, origin + forward * 0.09f - rise * 0.08f
                });
            }

            float start = Time.unscaledTime;
            try
            {
                while (effect != null)
                {
                    float elapsed = Time.unscaledTime - start;
                    if (elapsed >= flashDuration) break;
                    float fade = 1f - elapsed / flashDuration;
                    light.intensity = 7.5f * fade;
                    if (burst != null) burst.widthMultiplier = 0.055f * fade;
                    if (trail != null)
                    {
                        float progress = Mathf.Clamp01(elapsed / trailDuration);
                        trail.SetPosition(0, Vector3.Lerp(origin, trail.GetPosition(1), progress));
                        trail.widthMultiplier = 0.005f * (1f - progress);
                        trail.enabled = progress < 1f;
                    }
                    yield return null;
                }
            }
            finally
            {
                if (effect != null) Destroy(effect);
            }
        }

        private IEnumerator KnockDownOpponent()
        {
            GameObject actor = opponentInstance != null ? opponentInstance : defaultOpponent;
            fallenOpponent = actor.transform;
            opponentStandingPosition = fallenOpponent.localPosition;
            opponentStandingRotation = fallenOpponent.localRotation;

            // Tip away from the player's side of the table and sink as the body lands.
            const float duration = BattleCameraRig.OpponentFallSeconds;
            Vector3 away = fallenOpponent.parent.InverseTransformDirection(
                fallenOpponent.position - player.GunRestPose.position);
            Vector3 backward = Vector3.ProjectOnPlane(away, Vector3.up).normalized;
            if (backward.sqrMagnitude < 0.001f) backward = Vector3.back;
            Vector3 fallAxis = Vector3.Cross(Vector3.up, backward);
            float start = Time.unscaledTime;
            while (fallenOpponent != null)
            {
                float progress = Mathf.Clamp01((Time.unscaledTime - start) / duration);
                float recoil = Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress / 0.25f));
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.88f, progress));
                float settle = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(0.82f, 1f, progress));
                fallenOpponent.localPosition = opponentStandingPosition
                    + backward * (0.3f * fall)
                    - Vector3.up * (0.6f * fall - 0.035f * settle);
                fallenOpponent.localRotation = Quaternion.AngleAxis(76f * fall + 9f * recoil - 3f * settle, fallAxis) * opponentStandingRotation;
                if (progress >= 1f)
                {
                    fallenOpponent.gameObject.SetActive(false);
                    yield break;
                }
                yield return null;
            }
        }

        private void RestoreOpponentPose()
        {
            if (fallenOpponent == null) return;
            fallenOpponent.localPosition = opponentStandingPosition;
            fallenOpponent.localRotation = opponentStandingRotation;
            fallenOpponent.gameObject.SetActive(true);
            fallenOpponent = null;
        }

        public void ShowDraft(IReadOnlyList<int> hand, IReadOnlyList<int> slots)
        {
            draftHand = hand;
            draftSlots = slots;
            Layout(moveDuration);
            ReleaseRetired();
        }

        public void RestoreLayout() { Layout(moveDuration); }
        public IEnumerable<TableTarget> PlayerDropSlots()
        {
            foreach (Transform slot in player.Slots)
            {
                var target = slot.GetComponentInParent<TableTarget>();
                if (target != null && target.gameObject.activeInHierarchy) yield return target;
            }
        }

        public bool WantsHandExpanded(Vector2 screenPoint) => firstPersonHand.WantsExpanded(screenPoint, expandedHand);
        public void SetDraggingCard(CardView view)
        {
            firstPersonHand.SetDragging(view);
            if (view != null) AudioManager.Play(AudioCue.CardPickUp);
        }
        public void SetHandExpanded(bool expanded)
        {
            if (expandedHand == expanded || displayed == null) return;
            expandedHand = expanded;
            firstPersonHand.SetExpanded(expanded);
        }

        private void OnDisable()
        {
            if (playerShooting != null) playerShooting.Cancel();
            EndPostShotPresentation();
        }

        private void OnDestroy()
        {
            if (playerShooting != null) playerShooting.Cancel();
            if (player != null && player.Gun != null) player.Gun.DOKill();
            if (enemy != null && enemy.Gun != null) enemy.Gun.DOKill();
        }
    }
}
