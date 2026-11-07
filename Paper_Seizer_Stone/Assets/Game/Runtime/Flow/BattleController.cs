using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Configuration;
using PaperSeizerStone.Presentation;
using PaperSeizerStone.InputControl;
using PaperSeizerStone.Audio;
using PaperSeizerStone.Presentation.MainMenu;

namespace PaperSeizerStone.Flow
{
    // Coordinates one action batch at a time. Presentation completion only unlocks the next step.
    [DefaultExecutionOrder(-100)]
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private BattleDefinition definition;
        [SerializeField] private CardView cardPrefab;
        [SerializeField] private BattleCameraRig cameraRig;
        [SerializeField] private TabletopView tableView;
        [SerializeField] private BattleHud battleHud;
        [SerializeField] private BattleInput battleInput;
        [SerializeField] private ShopTableView shopView;
        [SerializeField] private BurnTableView burnView;
        [SerializeField] private TablePanel battlePanel;
        [SerializeField] private TutorialDirector tutorial;
        [SerializeField] private BossBattleLighting bossLighting;
        [SerializeField] private MenuSettingsPanel settings;
        [Header("动画之间的留白")]
        [SerializeField, Min(0)] private float actionPause = 0.15f;
        [SerializeField, Min(0)] private float phasePause = 0.3f;
        [SerializeField, Min(0)] private float victoryPause = 0.8f;
        [SerializeField, Min(0), Tooltip("中枪结算后积分器箭头机位的停留秒数。")] private float postShotScoreHold = 1.5f;

        private BattleEngine engine;
        private BasicEnemyPolicy enemy;
        private AdventureState adventure;
        private EnemyDefinition currentEnemy;
        private readonly List<int> draftHand = new List<int>();
        private readonly int[] draftSlots = new int[BasicRules.Slots];
        private bool busy = true;
        private bool burningDeck;
        private bool inspectingDeck;
        private bool engravingDeck;
        private bool settingsRequested;
        private bool previousSettingsBlocked;
        public CardTagCatalog Engravings => definition.Shop.Engravings;
        public int CardRemovalPrice => definition.Shop.RemovePrice;
        public CardTag AvailableEngraving => adventure == null ? CardTag.None : adventure.EngravingOffer;
        private int actualSeed;
        private string initializationError;
        public string InitializationError => initializationError;
        public bool TutorialVisible => tutorial != null && tutorial.IsPresenting;
        public bool DeathPresentationComplete { get; private set; }
        public bool DeathChoicesReady { get; private set; }

        public void SetDeathChoicesReady(bool ready) => DeathChoicesReady = DeathPresentationComplete && ready;

        private void CompletePlayerDeath()
        {
            if (bossLighting != null) bossLighting.Restore();
            DeathPresentationComplete = true;
            // The battle HUD may be disabled in the scene; it cannot poll for death until activated.
            if (battleHud != null) battleHud.ShowPlayerDeath();
            battleInput.SuspendInteraction();
            if (tutorial != null)
            {
                tutorial.Cancel();
                tutorial.gameObject.SetActive(false);
            }
        }
        private bool TutorialBlocked => tutorial != null && tutorial.BlocksInput;
        public bool SettingsBlockInput => settings != null
            && (settings.BlocksMenu || Time.frameCount <= settings.LastClosedFrame);
        private bool previousTutorialBlocked;

        public event Action<BattleObservation> BattleFinished;
        public TabletopView View => tableView;
        public Camera Camera => cameraRig != null ? cameraRig.Camera : null;
        public BattleCameraRig CameraRig => cameraRig;
        public string EnemyName => currentEnemy == null ? "对手" : currentEnemy.DisplayName;
        public int EncounterNumber => adventure == null ? 1 : adventure.EncounterIndex + 1;
        public int CycleNumber => definition == null ? 1 : definition.GetCycleNumber(EncounterNumber - 1);
        public int EncounterInCycle => (EncounterNumber - 1) % BattleDefinition.EncountersPerCycle + 1;
        public bool IsBossEncounter => currentEnemy != null && currentEnemy.IsBoss;
        public bool CanInteract => !SettingsBlockInput && !DeathPresentationComplete && !busy && !TutorialBlocked && engine != null && adventure != null && adventure.Phase != AdventurePhase.Shop;
        public bool CanCheatDraw => CanInteract && engine.State.Phase == BattlePhase.PlayerCheat
            && engine.State.Get(Side.Enemy).Bullets + 2 <= BasicRules.Chambers
            && engine.State.Get(Side.Player).DrawPile.Count + engine.State.Get(Side.Player).DiscardPile.Count > 0;
        public bool CanShopInteract => !SettingsBlockInput && !busy && !TutorialBlocked && !burningDeck && adventure != null && adventure.Phase == AdventurePhase.Shop;
        public bool CanDeckTableInteract => !SettingsBlockInput && !busy && !TutorialBlocked && burningDeck && adventure != null && adventure.Phase == AdventurePhase.Shop;
        public bool CanBurnInteract => CanDeckTableInteract && !inspectingDeck && !engravingDeck;
        public bool CanEngraveInteract => CanDeckTableInteract && engravingDeck;
        public bool CanDrag => CanInteract && (engine.State.Phase == BattlePhase.Planning
            || (engine.State.Phase == BattlePhase.PlayerCheat && engine.State.Get(Side.Enemy).Bullets < BasicRules.Chambers));

        private void Start()
        {
            try
            {
                if (definition == null) throw new InvalidOperationException("请在战斗控制器中配置战斗资源。");
                adventure = definition.CreateAdventure();
                if (cameraRig == null || tableView == null || cardPrefab == null || battleInput == null
                    || shopView == null || burnView == null || battlePanel == null || tutorial == null)
                    throw new InvalidOperationException("请在场景中绑定战斗、商店、烧牌 Panel、相机与输入组件。");
                cameraRig.Initialize();
                tableView.Initialize(definition, cardPrefab, cameraRig.Camera);
                battleInput.Initialize(this);
                tutorial.Initialize(definition.Shop);
                shopView.gameObject.SetActive(true);
                shopView.Initialize(this, definition.GetCardAssets());
                burnView.gameObject.SetActive(true);
                burnView.Initialize(this, definition.GetCardAssets());
                battlePanel.Initialize();
                ConfigureEncounter();
            }
            catch (Exception exception)
            {
                if (cameraRig != null && cameraRig.Opening != null) cameraRig.Opening.Cancel();
                initializationError = exception.Message;
                Debug.LogException(exception, this);
                return;
            }
            StartCoroutine(EnterEncounter());
        }

        private void Update()
        {
            if (adventure == null || !string.IsNullOrEmpty(initializationError)) return;
            UpdateSettings();
            if (SettingsBlockInput) return;
            if (previousTutorialBlocked != TutorialBlocked)
            {
                previousTutorialBlocked = TutorialBlocked;
                if (adventure.Phase == AdventurePhase.Shop)
                {
                    if (burningDeck) burnView.Refresh();
                    else shopView.Refresh();
                }
            }
            if (!busy && tutorial.ManualRequested)
            {
                busy = true;
                battleInput.SuspendInteraction();
                StartCoroutine(BrowseManual());
            }
        }

        private void UpdateSettings()
        {
            if (settings == null || !settings.IsReady || !settings.isActiveAndEnabled) return;
            if (Application.isFocused && Input.GetKeyDown(KeyCode.Escape))
            {
                if (SettingsBlockInput)
                {
                    if (settings.IsOpen) settings.Hide();
                }
                // Let the manual and deck-table views consume their own Escape first.
                else if ((tutorial == null || !tutorial.IsManualOpen)
                    && (!burningDeck || busy || TutorialBlocked))
                    settingsRequested = true;
            }

            if (settingsRequested && !SettingsBlockInput && (!busy || DeathChoicesReady) && !TutorialBlocked
                && (!DeathPresentationComplete || DeathChoicesReady))
            {
                settingsRequested = false;
                battleInput.SuspendInteraction();
                shopView.SetInteractable(false);
                burnView.SetInteractable(false);
                tutorial.ClearHint(shopView);
                tutorial.ClearHint(burnView);
                settings.Show();
            }

            bool blocked = SettingsBlockInput;
            if (previousSettingsBlocked && !blocked && adventure.Phase == AdventurePhase.Shop)
            {
                if (burningDeck) burnView.Refresh();
                else shopView.Refresh();
            }
            previousSettingsBlocked = blocked;
        }

        private IEnumerator BrowseManual()
        {
            try { yield return tutorial.OpenRequestedManual(); }
            finally
            {
                busy = false;
                if (adventure.Phase == AdventurePhase.Shop)
                {
                    if (burningDeck) burnView.Refresh();
                    else shopView.Refresh();
                }
            }
        }

        private void ConfigureEncounter()
        {
            // The saved table owns the lights, so an unset scene override can use its component.
            if (bossLighting == null) bossLighting = tableView.GetComponentInParent<BossBattleLighting>();
            if (bossLighting == null)
                throw new InvalidOperationException("请在 BattleTable 预制体上配置头目战灯光组件与目标灯。");
            bossLighting.Restore();
            currentEnemy = definition.GetEnemy(adventure.EncounterIndex);
            engine = definition.CreateEngine(adventure, out actualSeed);
            enemy = definition.CreateEnemyPolicy(adventure, actualSeed);
            tableView.ResetEncounter(currentEnemy);
        }

        private IEnumerator MoveBattle(bool enter, bool showHud = true)
        {
            if (!enter) tableView.PrepareExit();
            yield return enter ? battlePanel.Show(showHud: showHud) : battlePanel.Hide();
            yield return new WaitForSeconds(phasePause);
        }

        private IEnumerator MoveCamera(BattleCameraPose pose)
        {
            yield return cameraRig.MoveTo(pose);
            yield return new WaitForSeconds(phasePause);
        }

        private IEnumerator EnterEncounter()
        {
            AudioManager.SetMusic(MusicContext.Battle);
            busy = true;
            OpeningSequence opening = cameraRig.Opening;
            bool playOpening = opening != null && opening.IsPending;
            try
            {
                yield return MoveBattle(true, showHud: !playOpening);
                while (!tableView.OpponentEntranceReady) yield return null;
                if (playOpening)
                {
                    yield return opening.Play();
                    yield return battlePanel.ShowHud();
                }
            }
            finally
            {
                if (playOpening && opening != null) opening.Cancel();
            }
            yield return tutorial.Play(TutorialNode.Greeting);
            if (IsBossEncounter)
            {
                // The tutorial director remembers this node for the current adventure only.
                yield return tutorial.Play(TutorialNode.FirstBoss);
                if (bossLighting != null) bossLighting.EnterBossBattle();
            }
            yield return BeginRound();
        }

        private void OnDisable()
        {
            settingsRequested = false;
            if (settings != null) settings.CloseImmediately();
            StopAllCoroutines();
            if (bossLighting != null) bossLighting.Restore();
            if (tutorial != null) tutorial.Cancel();
            if (cameraRig != null && cameraRig.Opening != null) cameraRig.Opening.Cancel();
        }

        private IEnumerator BeginRound()
        {
            busy = true;
            tableView.RestoreOpponentForNextRound();
            bool firstDraw = !tutorial.HasPlayed(TutorialNode.PlaceCards);
            yield return tutorial.Play(TutorialNode.DrawFour);
            yield return Play(engine.BeginRound(), firstDraw);
            if (firstDraw)
            {
                yield return tutorial.Play(TutorialNode.Basics);
                yield return tutorial.Play(TutorialNode.PlaceCards);
            }
            draftHand.Clear();
            draftHand.AddRange(engine.State.Get(Side.Player).Hand);
            Array.Clear(draftSlots, 0, draftSlots.Length);
            tableView.ShowDraft(draftHand, draftSlots);
            busy = false;
        }

        private IEnumerator Play(ActionResult result, bool introduceDraw = false)
        {
            if (!result.Success) throw new InvalidOperationException(result.Error);
            int playerDraws = 0;
            for (int i = 0; i < result.Events.Count; i++)
            {
                BattleEvent battleEvent = result.Events[i];
                if (introduceDraw && battleEvent.Kind == BattleEventKind.CardDrawn && battleEvent.Side == Side.Player)
                {
                    if (playerDraws == 4) yield return tutorial.Play(TutorialNode.ExtraCard);
                    playerDraws++;
                }
                if (battleEvent.Kind == BattleEventKind.ShotResolved)
                {
                    var shots = new List<BattleEvent> { battleEvent };
                    while (i + 1 < result.Events.Count && result.Events[i + 1].Kind == BattleEventKind.ShotResolved)
                        shots.Add(result.Events[++i]);
                    bool playerKilled = shots[shots.Count - 1].Snapshot.Player.Life <= 0;
                    bool enemyKilled = shots[shots.Count - 1].Snapshot.Enemy.Life <= 0;
                    bool playerHurt = shots.Exists(shot => shot.Side == Side.Enemy && shot.Hit);
                    try
                    {
                        yield return tableView.PresentShots(shots, () => tutorial.Play(TutorialNode.EnemyDeath), CompletePlayerDeath);
                        if (!playerKilled && shots.Exists(shot => shot.Hit))
                        {
                            yield return cameraRig.FocusScoreCounter(tableView.ScoreCounter);
                            yield return new WaitForSecondsRealtime(postShotScoreHold);
                        }
                        if (!playerKilled) yield return cameraRig.MoveTo(BattleCameraPose.Table);
                    }
                    finally
                    {
                        tableView.EndPostShotPresentation(playerKilled);
                    }
                    // A simultaneous kill defers the entire hurt introduction, including its completion flag.
                    if (playerHurt && !playerKilled && !enemyKilled)
                        yield return tutorial.Play(TutorialNode.FirstHurt);
                }
                else
                {
                    yield return tableView.Present(battleEvent);
                }
                yield return new WaitForSeconds(battleEvent.Kind == BattleEventKind.PhaseChanged ? phasePause : actionPause);
                yield return tutorial.OpenRequestedManual();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugBoostNextScore()
        {
            if (!CanInteract || !engine.QueueDebugScoreBonus()) return;
            tableView.Notify("调试已启用：下一次计分额外 +1000（仅一次，重复按下不叠加）。");
        }
#endif

        public void ConfirmOrPass()
        {
            if (DeathPresentationComplete)
            {
                if (!DeathChoicesReady) return;
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
                return;
            }
            if (!CanInteract) return;
            if (engine.State.Phase == BattlePhase.BattleEnd)
            {
                // Death choices own restarting; victories enter the shop automatically.
                return;
            }
            if (engine.State.Phase == BattlePhase.Planning)
            {
                for (int i = 0; i < draftSlots.Length; i++)
                    if (draftSlots[i] == 0) { tableView.Notify("还有空槽位，请先放好四张牌。"); return; }
                ActionResult result = engine.CommitLayouts(draftSlots, enemy.ChooseLayout(engine.Observe(Side.Enemy)));
                if (!result.Success) { tableView.Notify(result.Error); return; }
                tableView.PressConfirm();
                busy = true;
                StartCoroutine(CommitAndReveal(result));
            }
            else if (engine.State.Phase == BattlePhase.PlayerCheat)
            {
                tableView.PressConfirm();
                SubmitCheat(new CheatAction(CheatKind.Pass));
            }
        }

        private IEnumerator CommitAndReveal(ActionResult committed)
        {
            yield return Play(committed);
            yield return tutorial.Play(TutorialNode.FirstBell);
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
            yield return cameraRig.FocusScoreCounter(tableView.ScoreCounter);
            yield return tableView.RevealCounterScores();
            bool tied = engine.State.Get(Side.Player).Score == engine.State.Get(Side.Enemy).Score;
            yield return MoveCamera(tied ? BattleCameraPose.Table : BattleCameraPose.Shooting);
            try
            {
                if (!tied) yield return tableView.PrepareShooting();
                yield return Play(engine.ExecuteShots());
            }
            finally
            {
                AudioManager.SetShootingFocus(false);
            }
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
            if (bossLighting != null) bossLighting.Restore();
            BattleObservation outcome = engine.Observe(Side.Player);
            adventure.FinishBattle(outcome, outcome.PlayerWon
                ? definition.Shop.CreateStock(unchecked(actualSeed ^ 15485863)) : new List<ShopOffer>(),
                outcome.PlayerWon ? definition.Shop.CreateEngraving(adventure.EncounterIndex, unchecked(actualSeed ^ 32452843)) : CardTag.None);
            BattleFinished?.Invoke(outcome);
            if (!outcome.PlayerWon)
            {
                busy = false;
                yield break;
            }
            yield return new WaitForSeconds(victoryPause);
            yield return MoveBattle(false);
            yield return MoveCamera(BattleCameraPose.Shop);
            AudioManager.SetMusic(MusicContext.Shop);
            shopView.Prepare(adventure);
            yield return shopView.Show();
            yield return new WaitForSeconds(phasePause);
            yield return tutorial.Play(TutorialNode.Shop);
            if (adventure.EngravingOffer != CardTag.None) yield return tutorial.Play(TutorialNode.Engraving);
            yield return tutorial.OpenRequestedManual();
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

        public void SetShopHint(UnityEngine.Object owner, string message) => tutorial.SetHint(owner, message);
        public void ClearShopHint(UnityEngine.Object owner) => tutorial.ClearHint(owner);
        public void ShowShopWarning(UnityEngine.Object owner, string message)
        {
            tutorial.ShowHintWarning(owner, message.StartsWith("筹码不足", StringComparison.Ordinal) ? "筹码不足" : message);
        }

        private IEnumerator CompletePurchase(int index, int price)
        {
            yield return shopView.Purchase(index, price);
            yield return new WaitForSeconds(actionPause);
            busy = false;
            shopView.Refresh();
        }

        public void RemoveCard(string recordId)
        {
            if (!CanBurnInteract) return;
            int price = definition.Shop.RemovePrice;
            if (adventure.Wallet < price) { burnView.Notify("筹码不足"); return; }
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
            burnView.Prepare(adventure);
            busy = false;
            burnView.Refresh();
        }

        public void OpenEngravingTable()
        {
            if (!CanShopInteract || AvailableEngraving == CardTag.None) return;
            CardTagAppearance entry = Engravings.Find(AvailableEngraving);
            if (entry == null) return;
            if (adventure.Wallet < entry.price) { shopView.Notify("筹码不足"); return; }
            OpenDeckTable(false, true);
        }

        public void EngraveCard(string recordId, CardTag tag)
        {
            if (!CanEngraveInteract) return;
            CardTagAppearance entry = Engravings.Find(tag);
            if (entry == null) { burnView.Notify("请选择有效刻印。"); return; }
            if (adventure.Wallet < entry.price) { burnView.Notify("筹码不足"); return; }
            if (!adventure.TryEngrave(recordId, tag, entry.price, out string error))
            { burnView.Notify(error); return; }
            busy = true;
            burnView.SetInteractable(false);
            StartCoroutine(CompleteEngraving(tag, entry.price));
        }

        private IEnumerator CompleteEngraving(CardTag tag, int price)
        {
            yield return burnView.Engrave(tag, price);
            yield return new WaitForSeconds(actionPause);
            busy = false;
            burnView.Refresh();
        }

        public void OpenBurnTable()
        {
            if (!CanShopInteract) return;
            if (adventure.Wallet < CardRemovalPrice) { shopView.Notify("筹码不足"); return; }
            OpenDeckTable(false);
        }

        public void OpenDeckInspection()
        {
            OpenDeckTable(true);
        }

        private void OpenDeckTable(bool inspectOnly, bool engrave = false)
        {
            if (!CanShopInteract) return;
            busy = true;
            shopView.SetInteractable(false);
            StartCoroutine(EnterBurnTable(inspectOnly, engrave));
        }

        private IEnumerator EnterBurnTable(bool inspectOnly, bool engrave)
        {
            yield return shopView.Hide(hideRug: false);
            yield return new WaitForSeconds(phasePause);
            burningDeck = true;
            inspectingDeck = inspectOnly;
            engravingDeck = engrave;
            burnView.Prepare(adventure, inspectOnly, engrave);
            yield return burnView.Show();
            yield return new WaitForSeconds(phasePause);
            busy = false;
            burnView.Refresh();
        }

        public void CloseBurnTable()
        {
            if (!CanDeckTableInteract) return;
            busy = true;
            burnView.SetInteractable(false);
            StartCoroutine(ReturnFromBurnTable());
        }

        private IEnumerator ReturnFromBurnTable()
        {
            yield return burnView.Hide();
            yield return new WaitForSeconds(phasePause);
            burningDeck = false;
            inspectingDeck = false;
            engravingDeck = false;
            shopView.Prepare(adventure);
            yield return shopView.Show(showRug: false);
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
            yield return MoveCamera(BattleCameraPose.Table);
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
                if (IsLegalDrop(cardId, targetCard, target)) AudioManager.Play(AudioCue.CardPlace);
                EditDraft(cardId, targetCard, target);
                tableView.ShowDraft(draftHand, draftSlots);
                if (!tutorial.HasPlayed(TutorialNode.BellPrompt) && Array.TrueForAll(draftSlots, id => id != 0))
                {
                    busy = true;
                    StartCoroutine(PromptForBell());
                }
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

        private IEnumerator PromptForBell()
        {
            yield return tableView.WaitForDraftPlacement();
            yield return tutorial.Play(TutorialNode.BellPrompt);
            busy = false;
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
