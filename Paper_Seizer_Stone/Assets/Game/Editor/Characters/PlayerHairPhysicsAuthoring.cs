using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PaperSeizerStone.EditorTools
{
    [Serializable]
    public sealed class PlayerHairAuthoringRecord
    {
        public int particleCount;
        public int fixedParticleCount;
        public int movableParticleCount;
        public int colliderCount;
        public float hairHeight;
        public float maximumDistance;
        public float fixedRootFraction;
        public float fixedParticleFraction;
        public float stretchingStiffness;
        public float bendingStiffness;
        public float damping;
        public float solverFrequency;
        public float worldVelocityScale;
        public float worldAccelerationScale;
        public float headCapsuleRadius;
        public float headCapsuleHeight;
        public float neckCapsuleRadius;
        public Vector3 externalAcceleration;
        public Vector3 randomAcceleration;
        public string constraints;
    }

    /// <summary>只为玩家 Hair 保存原生 Cloth、实际粒子约束和头颈碰撞体。</summary>
    public static class PlayerHairPhysicsAuthoring
    {
        private const float SourceHairHeight = 0.176311f;
        private const float RootHeightFraction = 0.45f;
        private const float TipMaximumDistance = 0.026f;
        private const float Epsilon = 0.000001f;

        public static PlayerHairAuthoringRecord Configure(Transform root, SkinnedMeshRenderer hair,
            SkinnedMeshRenderer headRenderer)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (hair == null) throw new ArgumentNullException(nameof(hair));
            if (headRenderer == null) throw new ArgumentNullException(nameof(headRenderer));
            if (hair == headRenderer || hair.sharedMesh == null || headRenderer.sharedMesh == null)
                throw new InvalidOperationException("玩家头发和头部必须是两个独立、已绑定网格的 SkinnedMeshRenderer。");
            if (!hair.transform.IsChildOf(root) || !headRenderer.transform.IsChildOf(root))
                throw new InvalidOperationException("玩家头发和头部必须属于指定角色根节点。");

            Transform head = FindBone(root, "Head");
            Transform neck = FindBone(root, "Neck");
            Bounds headBounds = BakedBoundsInRoot(root, headRenderer);
            float upScale = root.TransformVector(Vector3.up).magnitude;
            float rightScale = root.TransformVector(Vector3.right).magnitude;
            float forwardScale = root.TransformVector(Vector3.forward).magnitude;
            if (Mathf.Min(upScale, Mathf.Min(rightScale, forwardScale)) < Epsilon)
                throw new InvalidOperationException("玩家根节点包含零缩放，无法制作头发布料。");

            Cloth cloth = hair.GetComponent<Cloth>();
            if (cloth == null) cloth = hair.gameObject.AddComponent<Cloth>();
            cloth.enabled = true;

            // 必须使用 native Cloth 粒子顺序；不能按 Blender 或 sharedMesh 顶点编号分配约束。
            Vector3[] particles = cloth.vertices;
            ClothSkinningCoefficient[] coefficients = cloth.coefficients;
            if (particles.Length == 0 || particles.Length != coefficients.Length)
                throw new InvalidOperationException("玩家 Hair 的原生 Cloth 粒子尚未创建或约束数量不匹配。");

            Transform particleRoot = ActualParticleRoot(hair);
            var positions = new Vector3[particles.Length];
            Vector3 first = root.InverseTransformPoint(particleRoot.rotation * particles[0] + particleRoot.position);
            var hairBounds = new Bounds(first, Vector3.zero);
            for (int i = 0; i < particles.Length; i++)
            {
                // 与 Unity Cloth Inspector 一致：这里只应用实际 rootBone 的旋转和平移。
                positions[i] = root.InverseTransformPoint(particleRoot.rotation * particles[i] + particleRoot.position);
                hairBounds.Encapsulate(positions[i]);
            }
            float localHeight = hairBounds.size.y;
            float worldHeight = localHeight * upScale;
            if (worldHeight < 0.01f)
                throw new InvalidOperationException("玩家 Hair 的实际粒子高度过小，无法固定发根并配置发梢。");

            float characterScale = worldHeight / SourceHairHeight;
            float maximumDistance = TipMaximumDistance * characterScale;
            var record = new PlayerHairAuthoringRecord
            {
                particleCount = particles.Length,
                hairHeight = worldHeight,
                fixedRootFraction = RootHeightFraction,
                constraints = "Native Cloth particle indices; upper 45% fixed, smooth lower-tip gradient. "
                    + "Backstop, self/inter collision and virtual particles disabled; head/neck capsules only."
            };

            float movingHeightFraction = 1f - RootHeightFraction;
            for (int i = 0; i < particles.Length; i++)
            {
                float normalizedHeight = (positions[i].y - hairBounds.min.y) / localHeight;
                float tip = 1f - Mathf.InverseLerp(0f, movingHeightFraction, normalizedHeight);
                float weight = Mathf.Pow(Mathf.SmoothStep(0f, 1f, tip), 1.5f);
                float distance = maximumDistance * weight;
                if (normalizedHeight >= movingHeightFraction || distance < 0.00025f * characterScale)
                    distance = 0f;
                coefficients[i].maxDistance = distance;
                coefficients[i].collisionSphereDistance = float.MaxValue;
                if (distance == 0f) record.fixedParticleCount++;
                else record.movableParticleCount++;
                record.maximumDistance = Mathf.Max(record.maximumDistance, distance);
            }
            // 每个数组元素对应同一编号的 Cloth.vertices，原生组件负责持久化该索引关系。
            cloth.coefficients = coefficients;
            record.fixedParticleFraction = (float)record.fixedParticleCount / record.particleCount;

            cloth.useGravity = true;
            cloth.useTethers = true;
            cloth.stretchingStiffness = 0.98f;
            cloth.bendingStiffness = 0.8f;
            cloth.damping = 0.43f;
            cloth.friction = 0.25f;
            cloth.clothSolverFrequency = 60f;
            cloth.stiffnessFrequency = 120f;
            cloth.enableContinuousCollision = true;
            cloth.worldVelocityScale = 0.35f;
            cloth.worldAccelerationScale = 0.15f;
            cloth.externalAcceleration = new Vector3(0.12f, 0f, 0.06f) * characterScale;
            cloth.randomAcceleration = new Vector3(0.28f, 0.04f, 0.22f) * characterScale;
            cloth.selfCollisionDistance = 0f;
            cloth.selfCollisionStiffness = 0f;
            cloth.SetSelfAndInterCollisionIndices(new List<uint>());
            cloth.useVirtualParticles = 0;
            cloth.SetVirtualParticleIndices(new List<uint>());
            cloth.sphereColliders = new ClothSphereColliderPair[0];

            // 在角色坐标系取实际蒙皮头部范围，避免旋转后的世界 AABB 放大头碰撞半径。
            float headHeight = headBounds.size.y * upScale;
            float headWidth = Mathf.Min(headBounds.size.x * rightScale, headBounds.size.z * forwardScale);
            float headRadius = Mathf.Min(headWidth * 0.4f, headHeight * 0.4f);
            if (headRadius < Epsilon || headHeight < Epsilon)
                throw new InvalidOperationException("玩家头部实际蒙皮范围为空，无法配置头发碰撞体。");
            float headCapsuleHeight = Mathf.Max(headHeight * 0.85f, 2f * headRadius);
            Vector3 headCenter = root.TransformPoint(headBounds.center);
            CapsuleCollider headCapsule = Capsule(head, "HairCollision_Head", headCenter, root.up,
                headRadius, headCapsuleHeight);

            Vector3 neckToHead = head.position - neck.position;
            if (neckToHead.magnitude < Epsilon)
                throw new InvalidOperationException("玩家 Neck 和 Head 关节重合，无法配置颈部碰撞体。");
            float neckRadius = headRadius * 0.48f;
            CapsuleCollider neckCapsule = Capsule(neck, "HairCollision_Neck",
                (neck.position + head.position) * 0.5f, neckToHead.normalized, neckRadius,
                Mathf.Max(neckToHead.magnitude * 0.9f, neckRadius * 2f));
            cloth.capsuleColliders = new[] { headCapsule, neckCapsule };

            hair.updateWhenOffscreen = true;
            // Renderer 的显示包围盒使用自身局部空间，与上面的 Cloth 粒子空间分开处理。
            float rendererScale = Mathf.Min(hair.transform.TransformVector(Vector3.right).magnitude,
                Mathf.Min(hair.transform.TransformVector(Vector3.up).magnitude,
                    hair.transform.TransformVector(Vector3.forward).magnitude));
            if (rendererScale > Epsilon)
            {
                Bounds displayBounds = hair.localBounds;
                displayBounds.Expand(2f * maximumDistance / rendererScale);
                hair.localBounds = displayBounds;
            }

            record.colliderCount = cloth.capsuleColliders.Length;
            record.stretchingStiffness = cloth.stretchingStiffness;
            record.bendingStiffness = cloth.bendingStiffness;
            record.damping = cloth.damping;
            record.solverFrequency = cloth.clothSolverFrequency;
            record.worldVelocityScale = cloth.worldVelocityScale;
            record.worldAccelerationScale = cloth.worldAccelerationScale;
            record.headCapsuleRadius = headRadius;
            record.headCapsuleHeight = headCapsuleHeight;
            record.neckCapsuleRadius = neckRadius;
            record.externalAcceleration = cloth.externalAcceleration;
            record.randomAcceleration = cloth.randomAcceleration;
            return record;
        }

        private static Transform ActualParticleRoot(SkinnedMeshRenderer hair)
        {
            PropertyInfo property = typeof(SkinnedMeshRenderer).GetProperty("actualRootBone",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Transform particleRoot = property == null ? null : property.GetValue(hair, null) as Transform;
            if (particleRoot == null) particleRoot = hair.rootBone != null ? hair.rootBone : hair.transform;
            return particleRoot;
        }

        private static Bounds BakedBoundsInRoot(Transform root, SkinnedMeshRenderer renderer)
        {
            var baked = new Mesh { name = "PlayerHair_HeadBounds_Temporary" };
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] vertices = baked.vertices;
                if (vertices.Length == 0) throw new InvalidOperationException("玩家头部没有可用的蒙皮顶点。");
                Vector3 first = root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[0]));
                var bounds = new Bounds(first, Vector3.zero);
                for (int i = 1; i < vertices.Length; i++)
                    bounds.Encapsulate(root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i])));
                return bounds;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        private static CapsuleCollider Capsule(Transform bone, string name, Vector3 worldCenter,
            Vector3 worldAxis, float worldRadius, float worldHeight)
        {
            Transform child = bone.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(bone, false);
            }
            child.gameObject.layer = 2;
            child.localScale = Vector3.one;
            child.position = worldCenter;
            child.rotation = Quaternion.FromToRotation(Vector3.up, worldAxis);
            float radialScale = Mathf.Max(child.TransformVector(Vector3.right).magnitude,
                child.TransformVector(Vector3.forward).magnitude);
            float axialScale = child.TransformVector(Vector3.up).magnitude;
            if (radialScale < Epsilon || axialScale < Epsilon)
                throw new InvalidOperationException("玩家头颈骨骼包含零缩放。");
            CapsuleCollider collider = child.GetComponent<CapsuleCollider>();
            if (collider == null) collider = child.gameObject.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = Vector3.zero;
            collider.radius = worldRadius / radialScale;
            collider.height = Mathf.Max(worldHeight / axialScale, collider.radius * 2f);
            collider.enabled = true;
            collider.isTrigger = true;
            return collider;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                int prefix = Mathf.Max(bone.name.LastIndexOf(':'), bone.name.LastIndexOf('|'));
                var key = new StringBuilder();
                for (int i = prefix + 1; i < bone.name.Length; i++)
                    if (char.IsLetterOrDigit(bone.name[i])) key.Append(bone.name[i]);
                if (string.Equals(key.ToString(), boneName, StringComparison.OrdinalIgnoreCase)) return bone;
            }
            throw new InvalidOperationException("玩家缺少头发布料需要的骨骼：" + boneName);
        }
    }
}
