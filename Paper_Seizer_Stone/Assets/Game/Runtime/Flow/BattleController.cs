using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using PaperSeizerStone.InputControl;

namespace PaperSeizerStone.Flow
{
    // Coordinates one action batch at a time. Presentation completion only unlocks the next step.
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private BattleDefinition definition;
        [SerializeField] private CardView cardPrefab;
        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private TabletopView tableView;
        [SerializeField] private BattleInput battleInput;

        private BattleEngine engine;
        private BasicEnemyPolicy enemy;
        private readonly List<int> draftHand = new List<int>();
        private readonly int[] draftSlots = new int[BasicRules.Slots];
        private bool busy = true;
        private int actualSeed;
        private string initializationError;
        public string InitializationError => initializationError;

        public event Action<BattleObservation> BattleFinished;
        public TabletopView View => tableView;
        public Camera Camera => cameraRig != null ? cameraRig.Camera : null;
        public bool CanInteract => !busy && engine != null;
        public bool CanDrag => CanInteract && (engine.State.Phase == BattlePhase.Planning
            || (engine.State.Phase == BattlePhase.PlayerCheat && engine.State.Get(Side.Enemy).Bullets < BasicRules.Chambers));

        private void Start()
        {
            try
            {
                if (definition == null) throw new InvalidOperationException("请在战斗控制器中配置战斗资源。");
                engine = definition.CreateEngine(out actualSeed);
                enemy = new BasicEnemyPolicy(unchecked(actualSeed + 7919));
                if (cameraRig == null || tableView == null || cardPrefab == null || battleInput == null)
                    throw new InvalidOperationException("请在场景中绑定桌面、相机、卡牌预制体与输入组件。");
                cameraRig.Initialize();
                tableView.Initialize(definition, cardPrefab);
                battleInput.Initialize(this);
            }
            catch (Exception exception)
            {
                initializationError = exception.Message;
                Debug.LogException(exception, this);
                return;
            }
            StartCoroutine(BeginRound());
        }

        private IEnumerator BeginRound()
        {
            busy = true;
            yield return Play(engine.BeginRound());
            draftHand.Clear();
            draftHand.AddRange(engine.State.Get(Side.Player).Hand);
            Array.Clear(draftSlots, 0, draftSlots.Length);
            tableView.ShowDraft(draftHand, draftSlots);
            busy = false;
        }

        private IEnumerator Play(ActionResult result)
        {
            if (!result.Success) throw new InvalidOperationException(result.Error);
            for (int i = 0; i < result.Events.Count; i++)
            {
                BattleEvent battleEvent = result.Events[i];
                if (battleEvent.Kind == BattleEventKind.ShotResolved)
                {
                    var shots = new List<BattleEvent> { battleEvent };
                    while (i + 1 < result.Events.Count && result.Events[i + 1].Kind == BattleEventKind.ShotResolved)
                        shots.Add(result.Events[++i]);
                    yield return tableView.PresentShots(shots);
                }
                else
                {
                    if (battleEvent.Kind == BattleEventKind.RoundEnded) yield return cameraRig.MoveTo(BattleCameraPose.Table);
                    yield return tableView.Present(battleEvent);
                }
            }
        }

        public void ConfirmOrPass()
        {
            if (!CanInteract) return;
            if (engine.State.Phase == BattlePhase.BattleEnd)
            {
                // A restart is a fresh encounter, not continuation of an adventure.
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
                return;
            }
            if (engine.State.Phase == BattlePhase.Planning)
            {
                for (int i = 0; i < draftSlots.Length; i++)
                    if (draftSlots[i] == 0) { tableView.Notify("还有空槽位，请先放好四张牌。"); return; }
                ActionResult result = engine.CommitLayouts(draftSlots, enemy.ChooseLayout(engine.Observe(Side.Enemy)));
                if (!result.Success) { tableView.Notify(result.Error); return; }
                busy = true;
                StartCoroutine(CommitAndReveal(result));
            }
            else if (engine.State.Phase == BattlePhase.PlayerCheat) SubmitCheat(new CheatAction(CheatKind.Pass));
        }

        private IEnumerator CommitAndReveal(ActionResult committed)
        {
            yield return Play(committed);
            yield return RevealAndContinue();
        }

        private IEnumerator RevealAndContinue()
        {
            yield return Play(engine.RevealNext());
            if (engine.State.Phase == BattlePhase.PlayerCheat)
            {
                busy = false;
                yield break;
            }
            yield return cameraRig.MoveTo(BattleCameraPose.Scoring);
            yield return Play(engine.ScoreRound());
            yield return cameraRig.MoveTo(BattleCameraPose.Shooting);
            yield return tableView.PrepareShooting();
            yield return Play(engine.ExecuteShots());
            if (engine.State.Phase == BattlePhase.BattleEnd)
            {
                yield return cameraRig.MoveTo(BattleCameraPose.Table);
                busy = false;
                BattleFinished?.Invoke(engine.Observe(Side.Player));
            }
            else yield return BeginRound();
        }

        private void SubmitCheat(CheatAction action)
        {
            ActionResult result = engine.Cheat(Side.Player, action);
            if (!result.Success)
            {
                tableView.Notify(result.Error);
                tableView.RestoreLayout();
                return;
            }
            busy = true;
            StartCoroutine(ResolveCheats(result));
        }

        private IEnumerator ResolveCheats(ActionResult playerResult)
        {
            yield return Play(playerResult);
            yield return new WaitForSeconds(0.35f);
            CheatAction enemyAction = enemy.ChooseCheat(engine.Observe(Side.Enemy));
            yield return Play(engine.Cheat(Side.Enemy, enemyAction));
            yield return RevealAndContinue();
        }

        public void DrawExtra()
        {
            if (CanInteract && engine.State.Phase == BattlePhase.PlayerCheat) SubmitCheat(new CheatAction(CheatKind.Draw));
        }

        public bool IsLegalDrop(int cardId, CardView targetCard, TableTarget target)
        {
            if (!CanDrag || (targetCard != null && (targetCard.Owner != Side.Player || targetCard.CardId == cardId))) return false;
            if (engine.State.Phase == BattlePhase.Planning)
            {
                int slot = targetCard != null ? Array.IndexOf(draftSlots, targetCard.CardId)
                    : target != null && target.Kind == TableTargetKind.PlayerSlot ? target.Slot : -1;
                if (slot >= 0) return true;
                return Array.IndexOf(draftSlots, cardId) >= 0 && ((target != null && target.Kind == TableTargetKind.PlayerHand)
                    || (targetCard != null && draftHand.Contains(targetCard.CardId)));
            }
            FighterState own = engine.State.Get(Side.Player);
            int from = IndexOf(own.Board, cardId);
            int to = targetCard != null ? IndexOf(own.Board, targetCard.CardId)
                : target != null && target.Kind == TableTargetKind.PlayerSlot ? target.Slot : -1;
            if (from >= 0 && to >= 0) return from != to;
            if (from >= 0 && targetCard != null) return IndexOf(own.Hand, targetCard.CardId) >= 0;
            return to >= 0 && IndexOf(own.Hand, cardId) >= 0;
        }

        public void Drop(int cardId, CardView targetCard, TableTarget target)
        {
            if (!CanDrag) return;
            if (targetCard != null && (targetCard.Owner != Side.Player || targetCard.CardId == cardId))
            {
                tableView.RestoreLayout();
                return;
            }
            if (engine.State.Phase == BattlePhase.Planning)
            {
                EditDraft(cardId, targetCard, target);
                tableView.ShowDraft(draftHand, draftSlots);
                return;
            }
            IReadOnlyList<int> board = engine.State.Get(Side.Player).Board;
            IReadOnlyList<int> hand = engine.State.Get(Side.Player).Hand;
            int firstSlot = IndexOf(board, cardId);
            int secondSlot = targetCard != null ? IndexOf(board, targetCard.CardId)
                : target != null && target.Kind == TableTargetKind.PlayerSlot ? target.Slot : -1;
            int secondId = targetCard != null ? targetCard.CardId : secondSlot >= 0 ? board[secondSlot] : 0;
            if (firstSlot >= 0 && secondSlot >= 0)
                SubmitCheat(new CheatAction(CheatKind.SwapBoard, cardId, secondId));
            else if (firstSlot >= 0 && IndexOf(hand, secondId) >= 0)
                SubmitCheat(new CheatAction(CheatKind.SwapHand, cardId, secondId));
            else if (IndexOf(hand, cardId) >= 0 && secondSlot >= 0)
                SubmitCheat(new CheatAction(CheatKind.SwapHand, secondId, cardId));
            else
            {
                tableView.Notify("请将卡牌拖到己方槽位，或拖到要交换的那张手牌上。");
                tableView.RestoreLayout();
            }
        }

        private void EditDraft(int id, CardView targetCard, TableTarget target)
        {
            int fromSlot = Array.IndexOf(draftSlots, id);
            int fromHand = draftHand.IndexOf(id);
            int toSlot = targetCard != null ? Array.IndexOf(draftSlots, targetCard.CardId)
                : target != null && target.Kind == TableTargetKind.PlayerSlot ? target.Slot : -1;
            if (toSlot >= 0)
            {
                int replaced = draftSlots[toSlot];
                if (fromSlot >= 0) { draftSlots[toSlot] = id; draftSlots[fromSlot] = replaced; }
                else if (fromHand >= 0)
                {
                    draftSlots[toSlot] = id;
                    if (replaced == 0) draftHand.RemoveAt(fromHand); else draftHand[fromHand] = replaced;
                }
            }
            else if (fromSlot >= 0 && ((target != null && target.Kind == TableTargetKind.PlayerHand)
                || (targetCard != null && draftHand.Contains(targetCard.CardId))))
            {
                int handIndex = targetCard == null ? -1 : draftHand.IndexOf(targetCard.CardId);
                if (handIndex >= 0) { draftSlots[fromSlot] = draftHand[handIndex]; draftHand[handIndex] = id; }
                else { draftSlots[fromSlot] = 0; draftHand.Add(id); }
            }
        }

        private static int IndexOf(IReadOnlyList<int> values, int value)
        {
            for (int i = 0; i < values.Count; i++) if (values[i] == value) return i;
            return -1;
        }

    }
}
