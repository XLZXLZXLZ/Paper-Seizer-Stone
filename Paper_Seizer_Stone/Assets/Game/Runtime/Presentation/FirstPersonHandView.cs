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
        [SerializeField, Range(0, 0.5f)] private float pullOutThreshold = 0.22f;
        [SerializeField, Range(0, 0.6f)] private float retractThreshold = 0.36f;
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
        public bool WantsExpanded(float cursorHeight, bool current)
        {
            return cursorHeight < (current ? Mathf.Max(retractThreshold, pullOutThreshold) : pullOutThreshold);
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
