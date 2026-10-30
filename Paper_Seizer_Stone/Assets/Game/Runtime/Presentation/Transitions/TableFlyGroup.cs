using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    // World props travel in world space away from the table, UI in its own anchored space.
    public sealed class TableFlyGroup : MonoBehaviour
    {
        [SerializeField] private Transform[] parts;
        [Tooltip("共享桌体等固定物件及其子层级始终不参与转场。")]
        [SerializeField] private Transform[] stationaryRoots;
        [Tooltip("Direct active children are animated individually: live cards and physics tokens.")]
        [SerializeField] private Transform[] runtimeRoots;
        [SerializeField] private bool radialWorld = true;
        [SerializeField] private Transform tableCenter;
        [SerializeField] private Vector3 centerOffset;
        [SerializeField, Min(0)] private float outwardDistance = 8;
        [SerializeField, Min(0)] private float rise = 4;
        [SerializeField] private Vector3 randomOffset = new Vector3(0.7f, 0.8f, 0.7f);
        [SerializeField] private Vector3 travelOffset = new Vector3(0, -1100, 0);
        [SerializeField] private Vector3 travelRotation = new Vector3(25, 30, 12);
        [SerializeField, Min(0.05f)] private float duration = 0.55f;
        [SerializeField, Min(0)] private float stagger = 0.025f;
        private readonly List<Pose> authored = new List<Pose>();
        private Sequence sequence;
        private bool initialized;
        private sealed class Pose
        {
            public Transform Part;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private Vector3 PositionOf(Transform part) => !radialWorld && part is RectTransform rect ? rect.anchoredPosition3D : part.localPosition;
        private void SetPosition(Transform part, Vector3 position)
        {
            if (!radialWorld && part is RectTransform rect) rect.anchoredPosition3D = position;
            else part.localPosition = position;
        }

        public void Initialize()
        {
            if (initialized) return;
            if (parts == null || parts.Length == 0) throw new InvalidOperationException(name + "未配置转场物件。");
            foreach (Transform part in parts)
            {
                if (part == null) throw new InvalidOperationException(name + "有未绑定的转场物件。");
                if (IsStationary(part)) continue;
                authored.Add(new Pose { Part = part, Position = PositionOf(part), Rotation = part.localRotation });
            }
            initialized = true;
        }

        private bool IsStationary(Transform part)
        {
            if (stationaryRoots != null)
                foreach (Transform root in stationaryRoots)
                    if (root != null && (part == root || part.IsChildOf(root))) return true;
            return false;
        }

        public void HideImmediately()
        {
            Initialize();
            if (sequence != null) sequence.Kill();
            foreach (Pose pose in authored) pose.Part.gameObject.SetActive(false);
        }

        private List<Pose> Collect()
        {
            var result = new List<Pose>(authored);
            if (runtimeRoots != null)
                foreach (Transform root in runtimeRoots)
                    if (root != null)
                        foreach (Transform child in root)
                            if (!IsStationary(child) && child.gameObject.activeInHierarchy && (child.GetComponent<CardView>() != null || child.GetComponent<Rigidbody>() != null))
                                result.Add(new Pose { Part = child, Position = PositionOf(child), Rotation = child.localRotation });
            return result;
        }

        private Vector3 AwayPosition(Pose pose, bool enter)
        {
            if (!radialWorld) return pose.Position + travelOffset;
            Transform part = pose.Part;
            Vector3 position = enter ? pose.Position : PositionOf(part);
            Vector3 home = part.parent == null ? position : part.parent.TransformPoint(position);
            Vector3 center = tableCenter == null ? transform.TransformPoint(centerOffset) : tableCenter.TransformPoint(centerOffset);
            Vector3 outward = Vector3.ProjectOnPlane(home - center, Vector3.up);
            if (outward.sqrMagnitude < 0.001f) outward = UnityEngine.Random.onUnitSphere;
            outward.y = 0;
            if (outward.sqrMagnitude < 0.001f) outward = Vector3.right;
            Vector3 jitter = new Vector3(UnityEngine.Random.Range(-randomOffset.x, randomOffset.x),
                UnityEngine.Random.Range(0, randomOffset.y), UnityEngine.Random.Range(-randomOffset.z, randomOffset.z));
            Vector3 target = home + outward.normalized * outwardDistance + Vector3.up * rise + jitter;
            return part.parent == null ? target : part.parent.InverseTransformPoint(target);
        }

        public IEnumerator Play(bool enter)
        {
            Initialize();
            if (sequence != null) sequence.Kill();
            List<Pose> moving = Collect();
            sequence = DOTween.Sequence();
            for (int i = 0; i < moving.Count; i++)
            {
                Pose pose = moving[i];
                Transform part = pose.Part;
                part.DOKill();
                part.gameObject.SetActive(true);
                Vector3 away = AwayPosition(pose, enter);
                Quaternion awayRotation = pose.Rotation * Quaternion.Euler(Vector3.Scale(travelRotation,
                    new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), i % 2 == 0 ? 1 : -1)));
                if (enter) { SetPosition(part, away); part.localRotation = awayRotation; }
                Vector3 target = enter ? pose.Position : away;
                Tween movement = !radialWorld && part is RectTransform ui ? ui.DOAnchorPos3D(target, duration) : part.DOLocalMove(target, duration);
                // Large decks still finish the transition within a bounded interval.
                float delay = Mathf.Min(i * stagger, 0.35f);
                sequence.Insert(delay, movement.SetEase(enter ? Ease.OutCubic : Ease.InCubic));
                sequence.Insert(delay, part.DOLocalRotateQuaternion(enter ? pose.Rotation : awayRotation, duration));
            }
            yield return sequence.WaitForCompletion();
            if (!enter)
                foreach (Pose pose in moving)
                {
                    pose.Part.gameObject.SetActive(false);
                    SetPosition(pose.Part, pose.Position);
                    pose.Part.localRotation = pose.Rotation;
                }
        }

        private void OnDestroy() { if (sequence != null) sequence.Kill(); }
    }
}
