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
            AlignGunModel();
            BulletArea.Initialize();
        }

        private void AlignGunModel()
        {
            Transform muzzle = null;
            Transform grip = null;
            Transform cylinder = null;
            foreach (Transform node in Gun.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == "Socket_Muzzle") muzzle = node;
                else if (node.name == "Socket_Grip") grip = node;
                else if (node.name == "Socket_Cylinder") cylinder = node;
            }
            if (muzzle == null || grip == null || cylinder == null) return;
            Transform model = muzzle;
            while (model.parent != null && model.parent != Gun) model = model.parent;
            if (model.parent != Gun) return;
            // Lay the side of the gun on the table; socket geometry avoids assumptions about FBX axes.
            Vector3 forward = muzzle.position - cylinder.position;
            Vector3 top = Vector3.ProjectOnPlane(cylinder.position - grip.position, forward);
            Vector3 side = Vector3.Cross(forward, top);
            if (forward.sqrMagnitude > 0.0001f && side.sqrMagnitude > 0.0001f)
                model.rotation = Gun.rotation * Quaternion.Inverse(Quaternion.LookRotation(forward, side)) * model.rotation;
            MuzzleFlash.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
        }
    }
}
