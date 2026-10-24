using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace PaperSeizerStone.Presentation
{
    // The tray, walls and physics bodies are authored assets. Counts come from battle snapshots.
    public sealed class PhysicsTokenArea : MonoBehaviour
    {
        [SerializeField] private Rigidbody tokenPrefab;
        [SerializeField] private Transform tokenRoot;
        [SerializeField] private Transform dropPoint;
        [SerializeField] private List<Rigidbody> tokens = new List<Rigidbody>();
        [Header("落桌表现")]
        [SerializeField] private Vector2 spawnSpread = new Vector2(0.25f, 0.25f);
        [SerializeField, Min(0.01f)] private float dropInterval = 0.08f;
        [SerializeField, Min(0)] private float settleTime = 0.55f;
        [SerializeField, Min(0)] private float tumbleSpeed = 2;

        private System.Random random;
        private Coroutine dropping;
        private int currentCount;
        private bool initialized;

        public void Initialize()
        {
            if (initialized) return;
            if (tokenPrefab == null || tokenRoot == null || dropPoint == null)
                throw new InvalidOperationException(name + "缺少实体预制体、对象容器或落点。");
            random = new System.Random(unchecked(Environment.TickCount + GetInstanceID()));
            foreach (Rigidbody body in tokens)
            {
                if (body == null) throw new InvalidOperationException(name + "有未绑定的物理实体。");
                body.gameObject.SetActive(false);
            }
            initialized = true;
        }

        // Existing bodies retain their settled pose. A round reset explicitly redeals ammunition.
        public float SetCount(int count, bool redeal = false)
        {
            Initialize();
            count = Mathf.Max(0, count);
            if (redeal)
            {
                if (dropping != null) StopCoroutine(dropping);
                dropping = null;
                foreach (Rigidbody body in tokens) Return(body);
                currentCount = 0;
            }
            int previousCount = currentCount;
            while (tokens.Count < count)
            {
                Rigidbody body = Instantiate(tokenPrefab, tokenRoot);
                body.gameObject.SetActive(false);
                tokens.Add(body);
            }
            for (int i = count; i < currentCount; i++) Return(tokens[i]);
            currentCount = count;
            if (count > previousCount && dropping == null) dropping = StartCoroutine(DropPending());
            return count > previousCount ? (count - previousCount - 1) * dropInterval + settleTime : 0;
        }

        private IEnumerator DropPending()
        {
            while (true)
            {
                Rigidbody body = null;
                for (int i = 0; i < currentCount; i++)
                    if (!tokens[i].gameObject.activeSelf) { body = tokens[i]; break; }
                if (body == null) break;
                Vector3 offset = new Vector3(Signed() * spawnSpread.x, 0, Signed() * spawnSpread.y);
                Vector3 position = dropPoint.position + dropPoint.TransformDirection(offset);
                Quaternion rotation = dropPoint.rotation * Quaternion.Euler(Signed() * 65, Signed() * 180, Signed() * 65);
                body.transform.SetPositionAndRotation(position, rotation);
                body.position = position;
                body.rotation = rotation;
                body.gameObject.SetActive(true);
                body.isKinematic = false;
                body.detectCollisions = true;
                body.velocity = Vector3.zero;
                body.angularVelocity = new Vector3(Signed(), Signed(), Signed()) * tumbleSpeed;
                body.WakeUp();
                yield return new WaitForSeconds(dropInterval);
            }
            dropping = null;
        }

        private float Signed() => (float)(random.NextDouble() * 2 - 1);

        private static void Return(Rigidbody body)
        {
            if (!body.gameObject.activeSelf) return;
            body.transform.DOKill();
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.Sleep();
            body.gameObject.SetActive(false);
        }

        public void Freeze()
        {
            if (dropping != null) StopCoroutine(dropping);
            dropping = null;
            foreach (Rigidbody body in tokens)
            {
                if (!body.gameObject.activeSelf || body.isKinematic) continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        public IEnumerator Pay(int amount, Transform destination, float duration, float stagger)
        {
            if (amount < 0 || amount > currentCount) throw new InvalidOperationException("支付实体筹码的数量无效。");
            if (amount == 0) yield break;
            if (dropping != null) StopCoroutine(dropping);
            dropping = null;
            var payment = new List<Rigidbody>();
            for (int i = 0; i < amount; i++)
            {
                Rigidbody body = tokens[currentCount - 1 - i];
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.detectCollisions = false;
                body.transform.DOMove(destination.position, duration).SetDelay(i * stagger).SetEase(Ease.InCubic);
                body.transform.DORotate(new Vector3(0, 0, 180), duration, RotateMode.LocalAxisAdd).SetDelay(i * stagger);
                payment.Add(body);
            }
            yield return new WaitForSeconds(duration + (amount - 1) * stagger);
            foreach (Rigidbody body in payment) Return(body);
            currentCount -= amount;
        }

        private void OnDisable()
        {
            if (dropping != null) StopCoroutine(dropping);
            dropping = null;
        }

        private void OnDestroy()
        {
            foreach (Rigidbody body in tokens) if (body != null) body.transform.DOKill();
        }
    }
}
