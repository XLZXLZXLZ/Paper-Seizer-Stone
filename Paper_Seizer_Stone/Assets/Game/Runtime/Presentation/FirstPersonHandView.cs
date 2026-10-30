using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    // Existing card instances remain under LiveCards for pooling and panel transitions.
    [DefaultExecutionOrder(200)]
    public sealed class FirstPersonHandView : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float distance = 1.8f;
        [SerializeField, Range(0.1f, 1)] private float cardScale = 0.55f;
        [SerializeField, Tooltip("相对于视口底边的卡牌中心位置。")] private float collapsedHeight = -0.12f;
        [SerializeField] private float expandedHeight = 0.14f;
        [SerializeField, Range(0, 90)] private float fanAngle = 36;
        [SerializeField, Min(0)] private float fanDrop = 0.08f;
        [SerializeField, Min(0.1f)] private float followSpeed = 16;
        [Header("手牌响应扇区（相机视口坐标）")]
        [SerializeField, Tooltip("扇区圆心，(0.5, 0) 为画面底部中央，可放到画面下方。")]
        private Vector2 hoverCenter = new Vector2(0.5f, -0.08f);
        [SerializeField, Tooltip("扇区水平与垂直半径，占相机视口宽高的比例。")]
        private Vector2 hoverRadius = new Vector2(0.26f, 0.3f);
        [SerializeField, Range(5, 90), Tooltip("相对向上方向的半张角，总张角为此值的两倍。")]
        private float hoverHalfAngle = 70;
        [SerializeField, Range(0, 0.1f), Tooltip("展开后在扇区边缘增加的视口余量，避免边界反复收放。")]
        private float hoverExitPadding = 0.035f;
        private readonly List<Entry> entries = new List<Entry>();
        private Camera viewCamera;
        private CardView dragging;
        private bool expanded;
        private float expansion;
        private struct Entry { public CardView View; public int Index; public int Count; }

        public void Initialize(Camera camera) { viewCamera = camera; }
        public void Clear() { entries.Clear(); }
        public void ResetHand() { Clear(); dragging = null; expanded = false; expansion = 0; }
        public void SetExpanded(bool value) { expanded = value; }
        public bool WantsExpanded(Vector2 screenPoint, bool current)
        {
            if (viewCamera == null || entries.Count == 0) return false;
            if (!viewCamera.pixelRect.Contains(screenPoint)) return false;
            Vector3 viewport = viewCamera.ScreenToViewportPoint(screenPoint);
            Vector2 delta = (Vector2)viewport - hoverCenter;
            // Normalize an elliptical sector around the configured viewport center.
            float rx = Mathf.Max(0.001f, hoverRadius.x);
            float ry = Mathf.Max(0.001f, hoverRadius.y);
            Vector2 normalized = new Vector2(delta.x / rx, delta.y / ry);
            float angle = Mathf.Atan2(normalized.x, normalized.y) * Mathf.Rad2Deg;
            float limit = Mathf.Clamp(hoverHalfAngle, 5, 90);
            if (normalized.sqrMagnitude <= 1 && Mathf.Abs(angle) <= limit) return true;
            if (!current || hoverExitPadding <= 0) return false;
            // Expand the same sector slightly while the hand is open.
            Vector2 expandedPoint = new Vector2(delta.x / (rx + hoverExitPadding), delta.y / (ry + hoverExitPadding));
            float expandedAngle = Mathf.Abs(Mathf.Atan2(expandedPoint.x, expandedPoint.y) * Mathf.Rad2Deg);
            float anglePadding = Mathf.Atan2(hoverExitPadding, Mathf.Min(rx, ry)) * Mathf.Rad2Deg;
            return expandedPoint.sqrMagnitude <= 1 && expandedAngle <= Mathf.Min(90, limit + anglePadding);
        }
        public void SetDragging(CardView view) { dragging = view; }
        public void Register(CardView view, int index, int count)
        {
            if (view != dragging) view.transform.DOKill();
            entries.Add(new Entry { View = view, Index = index, Count = count });
        }

        private void LateUpdate()
        {
            if (viewCamera == null || entries.Count == 0) return;
            float blend = 1 - Mathf.Exp(-followSpeed * Time.deltaTime);
            expansion = Mathf.Lerp(expansion, expanded ? 1 : 0, blend);
            float depth = Mathf.Max(distance, viewCamera.nearClipPlane + 0.5f);
            float height = 2 * depth * Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float available = Mathf.Max(0, height * viewCamera.aspect * 0.84f - 0.9f * cardScale);
            Transform camera = viewCamera.transform;
            foreach (Entry entry in entries)
            {
                if (entry.View == null || entry.View == dragging || !entry.View.gameObject.activeInHierarchy) continue;
                float normalized = entry.Count < 2 ? 0 : (float)entry.Index / (entry.Count - 1) - 0.5f;
                float span = Mathf.Min(available, Mathf.Max(0, entry.Count - 1) * cardScale * Mathf.Lerp(0.32f, 0.48f, expansion));
                Vector3 local = new Vector3(normalized * span,
                    (Mathf.Lerp(collapsedHeight, expandedHeight, expansion) - 0.5f) * height -
                    normalized * normalized * 4 * fanDrop,
                    depth - entry.Index * 0.003f);
                Quaternion rotation = camera.rotation * Quaternion.Euler(0, 0, -normalized * fanAngle) * Quaternion.Euler(-90, 0, 0);
                Transform card = entry.View.transform;
                card.position = Vector3.Lerp(card.position, camera.TransformPoint(local), blend);
                card.rotation = Quaternion.Slerp(card.rotation, rotation, blend);
                card.localScale = Vector3.Lerp(card.localScale, Vector3.one * cardScale, blend);
            }
        }
    }
}
