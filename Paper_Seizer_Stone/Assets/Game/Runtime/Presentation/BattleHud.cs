using UnityEngine;
using TMPro;
using PaperSeizerStone.Battle;
using PaperSeizerStone.Flow;

namespace PaperSeizerStone.Presentation
{
    // Layout, colors and fonts live in the Canvas prefab. This component updates content only.
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] private BattleController controller;
        [SerializeField] private TabletopView tableView;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text playerScore;
        [SerializeField] private TMP_Text enemyScore;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text instructionText;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text resultTitle;
        [SerializeField] private TMP_Text resultDetails;
        [SerializeField] private GameObject errorPanel;
        [SerializeField] private TMP_Text errorText;

        private BattleObservation previous;
        private string previousMessage;
        private string previousError;
        private bool previousInteractive;

        private static string PhaseName(BattlePhase phase)
        {
            switch (phase)
            {
                case BattlePhase.Planning: return "布阵";
                case BattlePhase.Revealing: return "揭牌";
                case BattlePhase.PlayerCheat: return "你的抉择";
                case BattlePhase.EnemyCheat: return "双方行动";
                case BattlePhase.Scoring: return "对位计分";
                case BattlePhase.Shooting: return "执行";
                case BattlePhase.RoundEnd: return "回合结束";
                case BattlePhase.BattleEnd: return "对局结束";
                default: return "准备";
            }
        }

        private static string Instruction(BattleObservation snapshot)
        {
            switch (snapshot.Phase)
            {
                case BattlePhase.Planning:
                    return "拖拽手牌填满四个槽位  ·  回车确认出牌  ·  右键 / Esc 取消拖拽";
                case BattlePhase.PlayerCheat:
                    return snapshot.Enemy.Bullets >= BasicRules.Chambers
                        ? "对方弹巢已满，本次只能跳过作弊。按回车或点击桌面按钮继续。"
                        : "换位 / 换牌：对方增加 1 发子弹  ·  点击己方牌库抽牌：增加 2 发  ·  回车跳过";
                case BattlePhase.EnemyCheat: return "双方同时执行本次操作，完成后继续揭牌。";
                case BattlePhase.Revealing: return "先揭示我方颜色的牌，再揭示对手颜色的牌。";
                case BattlePhase.Scoring: return "基础得分：胜利 +2  ·  平局 +1  ·  失败 +0；特殊牌按牌面结算。";
                case BattlePhase.Shooting: return "总分高的一方开枪；总分相同，双方同时开枪。";
                case BattlePhase.BattleEnd: return snapshot.PlayerWon ? "胜利，正在收拾桌面，准备进入商店。" : "按回车或点击「重新开始」，开始一次新的冒险。";
                case BattlePhase.RoundEnd: return "手牌保留到下一回合，筹码归零后对局仍会继续。";
                default: return "正在准备牌库与桌面，请稍候。";
            }
        }

        private void LateUpdate()
        {
            if (controller == null || tableView == null) return;
            string error = controller.InitializationError;
            BattleObservation snapshot = tableView.Displayed;
            string message = tableView.Message;
            bool interactive = controller.CanInteract;
            if (previous == snapshot && previousMessage == message && previousError == error && previousInteractive == interactive) return;
            previous = snapshot;
            previousMessage = message;
            previousError = error;
            previousInteractive = interactive;
            errorPanel.SetActive(!string.IsNullOrEmpty(error));
            if (!string.IsNullOrEmpty(error))
            {
                errorText.text = error;
                resultPanel.SetActive(false);
                return;
            }
            roundText.text = snapshot == null ? "准备第 " + controller.EncounterNumber + " 场战斗" : "第 " + controller.EncounterNumber + " 场  ·  第 " + snapshot.Round.ToString("00") + " 回合  /  " + PhaseName(snapshot.Phase);
            if (playerScore != null) playerScore.text = "你  " + (snapshot == null ? "00" : snapshot.Player.Score.ToString("00"));
            if (enemyScore != null) enemyScore.text = controller.EnemyName + "  " + (snapshot == null ? "00" : snapshot.Enemy.Score.ToString("00"));
            messageText.text = string.IsNullOrEmpty(message) ? "正在准备对局。" : message;
            if (instructionText != null) instructionText.text = snapshot == null ? "正在发牌，请稍候。" : Instruction(snapshot);
            bool finished = snapshot != null && snapshot.Phase == BattlePhase.BattleEnd && interactive;
            resultPanel.SetActive(finished);
            if (finished)
            {
                resultTitle.text = "冒险结束";
                resultDetails.text = "抵达第 " + controller.EncounterNumber + " 场战斗\n按回车或点击桌面按钮重新开始";
            }
        }
    }
}
