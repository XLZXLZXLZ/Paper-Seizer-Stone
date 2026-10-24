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
        [SerializeField] private ShopTableView shopView;
        [SerializeField] private BurnTableView burnView;
        [SerializeField] private TablePanel battlePanel;
        [Header("动画之间的留白")]
        [SerializeField, Min(0)] private float actionPause = 0.15f;
        [SerializeField, Min(0)] private float phasePause = 0.3f;
        [SerializeField, Min(0)] private float victoryPause = 0.8f;

        private BattleEngine engine;
        private BasicEnemyPolicy enemy;
        private AdventureState adventure;
        private EnemyDefinition currentEnemy;
        private readonly List<int> draftHand = new List<int>();
        private readonly int[] draftSlots = new int[BasicRules.Slots];
        private bool busy = true;
        private bool burningDeck;
        private int actualSeed;
        private string initializationError;
        public string InitializationError => initializationError;

        public event Action<BattleObservation> BattleFinished;
        public TabletopView View => tableView;
        public Camera Camera => cameraRig != null ? cameraRig.Camera : null;
        public string EnemyName => currentEnemy == null ? "对手" : currentEnemy.DisplayName;
        public int EncounterNumber => adventure == null ? 1 : adventure.EncounterIndex + 1;
        public bool CanInteract => !busy && engine != null && adventure != null && adventure.Phase != AdventurePhase.Shop;
        public bool CanShopInteract => !busy && !burningDeck && adventure != null && adventure.Phase == AdventurePhase.Shop;
        public bool CanBurnInteract => !busy && burningDeck && adventure != null && adventure.Phase == AdventurePhase.Shop;
        public bool CanDrag => CanInteract && (engine.State.Phase == BattlePhase.Planning
            || (engine.State.Phase == BattlePhase.PlayerCheat && engine.State.Get(Side.Enemy).Bullets < BasicRules.Chambers));

        private void Start()
        {
            try
            {
                if (definition == null) throw new InvalidOperationException("请在战斗控制器中配置战斗资源。");
                adventure = definition.CreateAdventure();
                if (cameraRig == null || tableView == null || cardPrefab == null || battleInput == null
                    || shopView == null || burnView == null || battlePanel == null)
                    throw new InvalidOperationException("请在场景中绑定战斗、商店、烧牌 Panel、相机与输入组件。");
                cameraRig.Initialize();
                tableView.Initialize(definition, cardPrefab);
                battleInput.Initialize(this);
                shopView.gameObject.SetActive(true);
                shopView.Initialize(this, definition.GetCardAssets());
                burnView.gameObject.SetActive(true);
                burnView.Initialize(this, definition.GetCardAssets());
                battlePanel.Initialize();
                ConfigureEncounter();
            }
            catch (Exception exception)
            {
                initializationError = exception.Message;
                Debug.LogException(exception, this);
                return;
            }
            StartCoroutine(EnterEncounter());
        }

        private void ConfigureEncounter()
        {
            currentEnemy = definition.GetEnemy(adventure.EncounterIndex);
            engine = definition.CreateEngine(adventure, out actualSeed);
            enemy = new BasicEnemyPolicy(unchecked(actualSeed + 7919));
            tableView.ResetEncounter(currentEnemy);
        }

        private IEnumerator MoveBattle(bool enter)
        {
            if (!enter) tableView.PrepareExit();
            yield return enter ? battlePanel.Show() : battlePanel.Hide();
            yield return new WaitForSeconds(phasePause);
        }

        private IEnumerator MoveCamera(BattleCameraPose pose)
        {
            yield return cameraRig.MoveTo(pose);
            yield return new WaitForSeconds(phasePause);
        }

        private IEnumerator EnterEncounter()
        {
            busy = true;
            yield return MoveBattle(true);
            yield return BeginRound();
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
                    if (battleEvent.Kind == BattleEventKind.RoundEnded) yield return MoveCamera(BattleCameraPose.Table);
                    yield return tableView.Present(battleEvent);
                }
                yield return new WaitForSeconds(battleEvent.Kind == BattleEventKind.PhaseChanged ? phasePause : actionPause);
            }
        }

        public void ConfirmOrPass()
        {
            if (!CanInteract) return;
            if (engine.State.Phase == BattlePhase.BattleEnd)
            {
                // Defeat starts a fresh adventure; victories enter the shop automatically.
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
            yield return MoveCamera(BattleCameraPose.Scoring);
            yield return Play(engine.ScoreRound());
            yield return MoveCamera(BattleCameraPose.Shooting);
            yield return tableView.PrepareShooting();
            yield return Play(engine.ExecuteShots());
            if (engine.State.Phase == BattlePhase.BattleEnd)
            {
                yield return FinishEncounter();
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
            CheatAction enemyAction = enemy.ChooseCheat(engine.ObserveEnemyCheat());
            ActionResult enemyResult = engine.Cheat(Side.Enemy, enemyAction);
            if (!enemyResult.Success) throw new InvalidOperationException(enemyResult.Error);
            yield return tableView.PresentCheats(playerResult.Events, enemyResult.Events);
            yield return new WaitForSeconds(actionPause);
            yield return RevealAndContinue();
        }

        private IEnumerator FinishEncounter()
        {
            BattleObservation outcome = engine.Observe(Side.Player);
            adventure.FinishBattle(outcome, outcome.PlayerWon
                ? definition.Shop.CreateStock(unchecked(actualSeed ^ 15485863)) : new List<ShopOffer>());
            BattleFinished?.Invoke(outcome);
            if (!outcome.PlayerWon)
            {
                yield return MoveCamera(BattleCameraPose.Table);
                busy = false;
                yield break;
            }
            yield return new WaitForSeconds(victoryPause);
            yield return MoveCamera(BattleCameraPose.Table);
            yield return MoveBattle(false);
            shopView.Prepare(adventure, definition.Shop, definition.GetEnemy(adventure.EncounterIndex + 1));
            yield return shopView.Show();
            yield return new WaitForSeconds(phasePause);
            busy = false;
            shopView.Refresh();
        }

        public void BuyCard(int index)
        {
            if (!CanShopInteract) return;
            if (!adventure.TryBuy(index, out string error)) { shopView.Notify(error); return; }
            busy = true;
            shopView.SetInteractable(false);
            StartCoroutine(CompletePurchase(index, adventure.Offers[index].Price));
        }

        private IEnumerator CompletePurchase(int index, int price)
        {
            yield return shopView.Purchase(index, price);
            yield return new WaitForSeconds(actionPause);
            busy = false;
            shopView.Refresh();
            shopView.Notify("新牌已加入牌组。可以继续整备，或前往下一场战斗。");
        }

        public void RemoveCard(string recordId)
        {
            if (!CanBurnInteract) return;
            int price = definition.Shop.RemovePrice;
            if (!adventure.TryRemove(recordId, price, definition.Shop.MinimumDeckSize, out string error))
            { burnView.Notify(error); return; }
            busy = true;
            burnView.SetInteractable(false);
            StartCoroutine(CompleteRemoval(price));
        }

        private IEnumerator CompleteRemoval(int price)
        {
            yield return burnView.Burn(price);
            yield return new WaitForSeconds(actionPause);
            yield return ReturnFromBurnTable();
            shopView.Notify("选中的牌已烧毁。可以继续整备，或前往下一场战斗。");
        }

        public void OpenBurnTable()
        {
            if (!CanShopInteract) return;
            busy = true;
            shopView.SetInteractable(false);
            StartCoroutine(EnterBurnTable());
        }

        private IEnumerator EnterBurnTable()
        {
            yield return shopView.Hide();
            yield return new WaitForSeconds(phasePause);
            burningDeck = true;
            burnView.Prepare(adventure, definition.Shop);
            yield return burnView.Show();
            yield return new WaitForSeconds(phasePause);
            busy = false;
            burnView.Refresh();
        }

        public void CloseBurnTable()
        {
            if (!CanBurnInteract) return;
            busy = true;
            burnView.SetInteractable(false);
            StartCoroutine(ReturnFromBurnTable());
        }

        private IEnumerator ReturnFromBurnTable()
        {
            yield return burnView.Hide();
            yield return new WaitForSeconds(phasePause);
            burningDeck = false;
            shopView.Prepare(adventure, definition.Shop, definition.GetEnemy(adventure.EncounterIndex + 1));
            yield return shopView.Show();
            yield return new WaitForSeconds(phasePause);
            busy = false;
            shopView.Refresh();
        }

        public void LeaveShop()
        {
            if (!CanShopInteract) return;
            busy = true;
            shopView.SetInteractable(false);
            StartCoroutine(ContinueAdventure());
        }

        private IEnumerator ContinueAdventure()
        {
            yield return shopView.Hide();
            yield return new WaitForSeconds(phasePause);
            adventure.BeginNextBattle();
            bool configured = true;
            try { ConfigureEncounter(); }
            catch (Exception exception)
            {
                configured = false;
                initializationError = exception.Message;
                Debug.LogException(exception, this);
            }
            if (configured) yield return EnterEncounter();
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
