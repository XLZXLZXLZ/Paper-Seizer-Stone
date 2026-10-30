using UnityEngine;

namespace PaperSeizerStone.Presentation.Props
{
    // Keep the complete authored character alive; visibility belongs to each camera.
    [ExecuteAlways]
    public sealed class PlayerVisibilityLayer : MonoBehaviour
    {
        public const int Layer = 8;
        private void OnEnable() { AssignLayer(); }
        private void OnTransformChildrenChanged() { AssignLayer(); }

        private void AssignLayer()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = Layer;
        }
    }
}
