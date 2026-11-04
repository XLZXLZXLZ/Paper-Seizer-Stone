using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PaperSeizerStone.Presentation
{
    // Saved character/prop references; combat rules remain in BattleController.
    [DefaultExecutionOrder(300), DisallowMultipleComponent]
    public sealed class GuaiwuCombatSequence : MonoBehaviour
    {
        [Header("怪物动作")]
        [SerializeField] private AnimationClip fireClip;
        [SerializeField] private AnimationClip hitClip;
        [SerializeField] private AnimationClip gunClip;
        [SerializeField] private Animator characterAnimator;
        [SerializeField] private Animator gunAnimator;
        [SerializeField] private Transform gunRoot;
        [SerializeField] private GameObject muzzleFlash;
        [Header("同步时间（秒）")]
        [SerializeField, Min(0)] private float gunStart;
        [SerializeField, Tooltip("负数表示人物动作末帧击发。")] private float fireMoment = -1;
        [SerializeField, Min(0)] private float enterBlend = .15f;
        [SerializeField, Min(0)] private float returnBlend = .25f;
        [Header("手动对位（相对预制体原位置）")]
        [SerializeField] private ShootingPlacement characterPlacement = new ShootingPlacement();
        [SerializeField] private ShootingPlacement gunPlacement = new ShootingPlacement();

        private PlayableGraph graph;
        private float lastClock; private bool returning; private AnimatorStateInfo resumeState;
        private AnimationClipPlayable gun;
        
        private RuntimeAnimatorController controller;
        private Transform restingGun;
        private bool restingGunVisible, gunRootVisible, animatorEnabled, gunAnimatorEnabled;
        private bool rootMotion;
        private float animatorSpeed;
        private AnimatorCullingMode culling;
        private int revision, queued, evaluated;
        private float clock, weight;
        private bool shooting, active;
        private Pose characterBase, gunBase;
        private struct Pose { public Vector3 position, scale; public Quaternion rotation; }
        public bool IsShooting => active && shooting;
        public bool IsConfigured => fireClip != null && hitClip != null && gunClip != null
            && characterAnimator != null && gunAnimator != null && gunRoot != null && muzzleFlash != null
            && (characterAnimator.runtimeAnimatorController != null || active) && fireClip.length > 0;
        public GameObject MuzzleFlash => muzzleFlash;
        public float FireTime => fireClip == null ? 0 : fireMoment < 0 ? fireClip.length : Mathf.Clamp(fireMoment, 0, fireClip.length);
        private static Pose Read(Transform t) => new Pose { position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
        private static void Restore(Transform t, Pose p) { if (t == null) return; t.localPosition = p.position; t.localRotation = p.rotation; t.localScale = p.scale; }

        public void Configure(Animator actor, Animator prop, Transform wrapper, GameObject flash,
            AnimationClip fire, AnimationClip hit, AnimationClip gunAnimation, float start)
        {
            characterAnimator = actor; gunAnimator = prop; gunRoot = wrapper; muzzleFlash = flash;
            fireClip = fire; hitClip = hit; gunClip = gunAnimation; gunStart = start;
        }

        public IEnumerator Prepare(Transform tableGun)
        {
            if (!IsConfigured || !isActiveAndEnabled || tableGun == null) yield break;
            Cancel();
            int run = ++revision;
            restingGun = tableGun; restingGunVisible = tableGun.gameObject.activeSelf;
            bool ready = false;
            try
            {
                StartAction(fireClip, true);
                tableGun.gameObject.SetActive(false);
                while (active && run == revision && clock < FireTime)
                {
                    yield return null;
                    if (!active || run != revision) yield break;
                    float next = Mathf.Min(FireTime, clock + Time.deltaTime);
                    Queue(next, enterBlend <= 0 ? 1 : Mathf.SmoothStep(0, 1, next / enterBlend));
                }
                int frame = queued;
                while (active && run == revision && evaluated < frame) yield return null;
                ready = active && run == revision;
            }
            finally { if (!ready && run == revision) Cancel(); }
        }

        public IEnumerator FinishShooting()
        {
            if (!IsShooting) yield break;
            int run = revision;
            try
            {
                while (IsShooting && run == revision && clock < fireClip.length)
                {
                    yield return null;
                    if (run != revision || !IsShooting) yield break;
                    Queue(Mathf.Min(fireClip.length, clock + Time.deltaTime), 1);
                }
                yield return ReturnToIdle(run);
            }
            finally { if (run == revision) Cancel(); }
        }

        public IEnumerator PlayHit(bool terminal)
        {
            if (!IsConfigured || !isActiveAndEnabled) yield break;
            Cancel();
            int run = ++revision;
            bool held = false;
            try
            {
                StartAction(hitClip, false);
                while (active && run == revision && clock < hitClip.length)
                {
                    yield return null;
                    if (run != revision || !active) yield break;
                    float next = Mathf.Min(hitClip.length, clock + Time.unscaledDeltaTime);
                    Queue(next, Mathf.SmoothStep(0, 1, next / Mathf.Max(.001f, Mathf.Min(enterBlend, hitClip.length * .3f))));
                }
                int frame = queued;
                while (active && run == revision && evaluated < frame) yield return null;
                if (run != revision || !active) yield break;
                if (terminal) held = true;
                else yield return ReturnToIdle(run);
            }
            finally { if (!held && run == revision) Cancel(); }
        }

        private IEnumerator ReturnToIdle(int run)
        {
            if (!active || run != revision) yield break;
            returning = true;
            characterAnimator.CrossFadeInFixedTime("Base Layer.Idle", returnBlend, 0, 0);
            float elapsed = 0;
            while (active && run == revision && elapsed < returnBlend)
            {
                yield return null;
                if (run != revision || !active) yield break;
                elapsed += Time.unscaledDeltaTime;
                Queue(clock, 1 - Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.001f, returnBlend)));
            }
            if (!active || run != revision) yield break;
            Queue(clock, 0);
            int frame = queued;
            while (active && run == revision && evaluated < frame) yield return null;
            while (active && run == revision && characterAnimator.IsInTransition(0)) yield return null;
        }

        private void StartAction(AnimationClip clip, bool withGun)
        {
            controller = characterAnimator.runtimeAnimatorController;
            resumeState = characterAnimator.GetCurrentAnimatorStateInfo(0);
            animatorEnabled = characterAnimator.enabled; rootMotion = characterAnimator.applyRootMotion; culling = characterAnimator.cullingMode;
            animatorSpeed = characterAnimator.speed;
            characterAnimator.speed = 1;
            gunAnimatorEnabled = gunAnimator.enabled;
            characterBase = Read(characterAnimator.transform); gunBase = Read(gunRoot);
            gunRootVisible = gunRoot.gameObject.activeSelf;
            active = true; shooting = withGun; clock = weight = 0; queued = evaluated = 0;
            lastClock = 0; returning = false;
            characterAnimator.enabled = false; characterAnimator.applyRootMotion = false; characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            gunRoot.gameObject.SetActive(withGun); muzzleFlash.SetActive(false);
            graph = PlayableGraph.Create("Guaiwu combat " + GetInstanceID()); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            characterAnimator.CrossFadeInFixedTime(withGun ? "Base Layer.Fire" : "Base Layer.Hit", enterBlend, 0, 0);
            if (withGun)
            {
                gunAnimator.enabled = true;
                gun = AnimationClipPlayable.Create(graph, gunClip); gun.SetSpeed(0); gun.SetApplyFootIK(false); gun.SetApplyPlayableIK(false);
                var gunOutput = AnimationPlayableOutput.Create(graph, "Monster gun", gunAnimator); gunOutput.SetSourcePlayable(gun); gunOutput.SetWeight(1);
            }
            graph.Play(); Queue(0, 0);
        }

        private void Queue(float time, float blend) { clock = time; weight = Mathf.Clamp01(blend); ++queued; }
        private void LateUpdate()
        {
            if (!active || !graph.IsValid()) return;
            Restore(characterAnimator.transform, characterBase); Restore(gunRoot, gunBase);
            characterAnimator.Update(returning ? Time.unscaledDeltaTime : Mathf.Max(0, clock - lastClock));
            lastClock = clock;
            if (shooting && gun.IsValid()) gun.SetTime(Mathf.Clamp(clock - gunStart, 0, gunClip.length));
            graph.Evaluate(0);
            PlayerShootingSequence.ApplyPlacement(characterAnimator.transform, characterBase.position, characterBase.rotation, characterBase.scale, characterPlacement, weight, Vector3.zero);
            if (shooting) PlayerShootingSequence.ApplyPlacement(gunRoot, gunBase.position, gunBase.rotation, gunBase.scale, gunPlacement, 1, gunRoot.InverseTransformPoint(gunAnimator.transform.position));
            evaluated = queued;
        }

        public void Cancel()
        {
            if (!active && !graph.IsValid()) return;
            ++revision; active = shooting = false;
            AnimatorStateInfo current = characterAnimator != null ? characterAnimator.GetCurrentAnimatorStateInfo(0) : resumeState;
            float normalized = returning ? current.normalizedTime : 0;
            if (graph.IsValid()) graph.Destroy();
            if (characterAnimator != null)
            {
                Restore(characterAnimator.transform, characterBase);
                characterAnimator.runtimeAnimatorController = controller;
                characterAnimator.applyRootMotion = rootMotion; characterAnimator.cullingMode = culling; characterAnimator.enabled = animatorEnabled;
                characterAnimator.speed = animatorSpeed;
                if (characterAnimator.isActiveAndEnabled && characterAnimator.HasState(0, Animator.StringToHash("Base Layer.Idle")))
                { characterAnimator.Play("Base Layer.Idle", 0, normalized); characterAnimator.Update(0); }
            }
            Restore(gunRoot, gunBase);
            if (gunRoot != null) gunRoot.gameObject.SetActive(gunRootVisible);
            if (gunAnimator != null) gunAnimator.enabled = gunAnimatorEnabled;
            if (muzzleFlash != null) muzzleFlash.SetActive(false);
            if (restingGun != null) restingGun.gameObject.SetActive(restingGunVisible);
            restingGun = null;
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
