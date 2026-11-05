using System;
using UnityEngine;
using TMPro;
using PaperSeizerStone.Battle;
using UnityEngine.Animations;
using UnityEngine.Playables;

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
        [Tooltip("对齐动画拿枪起点后，相对桌面枪定位点的位置微调。")]
        public Vector3 RestGunPositionOffset;
        public Vector3 RestGunEulerOffset;
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

        // Sample only an isolated copy of the authored gun, never the live character or Cloth.
        public void AlignRestGunToAnimation(Transform gunRoot, Animator gunAnimator, AnimationClip clip,
            ShootingPlacement placement)
        {
            if (gunRoot == null || gunAnimator == null || clip == null) return;
            string path = "";
            for (Transform node = gunAnimator.transform; node != gunRoot; node = node.parent)
            {
                if (node == null) return;
                path = string.IsNullOrEmpty(path) ? node.name : node.name + "/" + path;
            }
            GameObject sample = UnityEngine.Object.Instantiate(gunRoot.gameObject, gunRoot.parent, false);
            sample.name = "Gun rest pose sample";
            PlayableGraph graph = default;
            try
            {
                foreach (Renderer renderer in sample.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                foreach (Collider collider in sample.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                sample.SetActive(true);
                Transform animatorRoot = string.IsNullOrEmpty(path) ? sample.transform : sample.transform.Find(path);
                Animator animator = animatorRoot.GetComponent<Animator>();
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Gun rest pose");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetSpeed(0);
                playable.SetTime(0);
                AnimationPlayableOutput.Create(graph, "Gun", animator).SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0);
                Transform root = sample.transform;
                PlayerShootingSequence.ApplyPlacement(root, root.localPosition, root.localRotation, root.localScale,
                    placement, 1f, root.InverseTransformPoint(animator.transform.position));
                MatchGunSockets(root);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                sample.SetActive(false);
                UnityEngine.Object.Destroy(sample);
            }
        }

        private static Transform Socket(Transform root, string name)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node.name == name) return node;
            return null;
        }

        private void MatchGunSockets(Transform source)
        {
            Transform sourceGrip = Socket(source, "Socket_Grip"), sourceMuzzle = Socket(source, "Socket_Muzzle");
            Transform sourceCylinder = Socket(source, "Socket_Cylinder");
            Transform grip = Socket(Gun, "Socket_Grip"), muzzle = Socket(Gun, "Socket_Muzzle");
            Transform cylinder = Socket(Gun, "Socket_Cylinder");
            if (sourceGrip == null || sourceMuzzle == null || sourceCylinder == null || grip == null || muzzle == null || cylinder == null) return;
            Transform model = grip;
            while (model.parent != null && model.parent != Gun) model = model.parent;
            if (model.parent != Gun) return;
            Vector3 forward = muzzle.position - grip.position;
            Vector3 targetForward = sourceMuzzle.position - sourceGrip.position;
            Vector3 up = Vector3.ProjectOnPlane(cylinder.position - grip.position, forward);
            Vector3 targetUp = Vector3.ProjectOnPlane(sourceCylinder.position - sourceGrip.position, targetForward);
            if (forward.sqrMagnitude < 0.000001f || targetForward.sqrMagnitude < 0.000001f || up.sqrMagnitude < 0.000001f || targetUp.sqrMagnitude < 0.000001f) return;
            model.localScale *= targetForward.magnitude / forward.magnitude;
            model.rotation = Quaternion.LookRotation(targetForward, targetUp)
                * Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.rotation;
            Quaternion adjustment = GunRestPose.rotation * Quaternion.Euler(RestGunEulerOffset) * Quaternion.Inverse(GunRestPose.rotation);
            Vector3 targetGrip = sourceGrip.position + GunRestPose.TransformVector(RestGunPositionOffset);
            model.rotation = adjustment * model.rotation;
            model.position += targetGrip - grip.position;
            MuzzleFlash.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
        }
    }
}
