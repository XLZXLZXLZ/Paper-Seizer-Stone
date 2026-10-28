using System;
using UnityEngine;
using TMPro;
using PaperSeizerStone.Battle;

namespace PaperSeizerStone.Presentation
{
    [Serializable]
    public sealed class SideTableBindings
    {
        public Transform[] Slots;
        public Transform HandAnchor;
        public Transform DrawAnchor;
        public Transform DiscardAnchor;
        [Tooltip("可选：未绑定时不显示牌库计数。")]
        public TMP_Text DrawLabel;
        [Tooltip("可选：未绑定时不显示弃牌计数。")]
        public TMP_Text DiscardLabel;
        [Tooltip("可选：未绑定时不显示桌面比分与生命。")]
        public TMP_Text ScoreLabel;
        public Transform Gun;
        public Transform GunRestPose;
        public Transform GunAimPose;
        public GameObject MuzzleFlash;
        public PhysicsTokenArea BulletArea;
        public DeckPileView DrawPile;

        public void RequireAssigned(string name)
        {
            if (Slots == null || Slots.Length != BasicRules.Slots || HandAnchor == null || DrawAnchor == null
                || DiscardAnchor == null
                || Gun == null || GunRestPose == null || GunAimPose == null || MuzzleFlash == null
                || BulletArea == null || DrawPile == null)
                throw new InvalidOperationException(name + "的桌面节点未完整配置。");
            foreach (Transform slot in Slots) if (slot == null) throw new InvalidOperationException(name + "缺少卡槽定位点。");
            BulletArea.Initialize();
        }
    }
}
