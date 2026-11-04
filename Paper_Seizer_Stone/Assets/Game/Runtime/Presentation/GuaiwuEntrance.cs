using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    // Runs once per instantiated opponent. The runtime prefab is saved in A Pose
    // so native Cloth also starts there, before the first simulated frame.
    public sealed class GuaiwuEntrance : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [Header("入场布料准备")]
        [SerializeField, Min(0)] private float aPoseHoldSeconds = 0.5f;
        [SerializeField, Min(0.05f)] private float transitionSeconds = 2f;
        private int phase;
        private float elapsed;
        public bool IsReady => phase == 2;

        public void Configure(Animator target) { animator = target; }

        private void Awake()
        {
            if (animator == null) { phase = 2; return; }
            animator.enabled = true;
            animator.speed = 1;
            animator.Play("Base Layer.A_Pose", 0, 0);
            animator.Update(0);
        }

        private void Update()
        {
            if (IsReady) return;
            if (animator == null || !animator.isActiveAndEnabled) { phase = 2; return; }
            elapsed += Time.deltaTime;
            if (phase == 0 && elapsed >= aPoseHoldSeconds)
            {
                elapsed = 0;
                phase = 1;
                animator.speed = 1;
                animator.Play("Base Layer.A_To_Idle", 0, 0);
                animator.Update(0);
                float clipSeconds = animator.GetCurrentAnimatorStateInfo(0).length;
                animator.speed = Mathf.Max(.001f, clipSeconds) / Mathf.Max(0.05f, transitionSeconds);
            }
            else if (phase == 1 && elapsed >= Mathf.Max(0.05f, transitionSeconds))
            {
                animator.speed = 1;
                animator.Play("Base Layer.Idle", 0, 0);
                phase = 2;
            }
        }

        private void OnDestroy() { if (animator != null) animator.speed = 1; }
    }
}
