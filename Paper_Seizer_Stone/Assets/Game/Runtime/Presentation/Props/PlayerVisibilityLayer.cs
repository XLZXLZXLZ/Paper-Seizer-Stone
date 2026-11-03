using UnityEngine;

namespace PaperSeizerStone.Presentation.Props
{
    // Keep the complete authored character alive; visibility belongs to each camera.
    [ExecuteAlways]
    public sealed class PlayerVisibilityLayer : MonoBehaviour
    {
        public const int Layer = 8;
        public const int HandsLayer = 9;
        [SerializeField, Tooltip("仅包含第一人称手臂外观的容器；骨骼与碰撞体保留在完整角色层。")] private Transform firstPersonArms;

        private void OnEnable() { AssignLayer(); }
        private void OnValidate() { AssignLayer(); }
        private void OnTransformChildrenChanged() { AssignLayer(); }

        private void AssignLayer()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                bool hands = firstPersonArms != null && (child == firstPersonArms || child.IsChildOf(firstPersonArms));
                int target = hands ? HandsLayer : Layer;
                // Preserve the final layer instead of flipping hands 9 -> 8 -> 9
                // on every prefab validation or child change.
                if (child.gameObject.layer != target) child.gameObject.layer = target;
            }
        }
    }
}
