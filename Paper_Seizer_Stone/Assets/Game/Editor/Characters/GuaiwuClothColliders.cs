using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PaperSeizerStone.EditorTools
{
    /// <summary>将怪物的布料碰撞形状保存到骨骼层级，由角色生成器显式绑定给 Cloth。</summary>
    public static class GuaiwuClothColliders
    {
        private const string ColliderPrefix = "ClothCollision_";
        private const int IgnoreRaycastLayer = 2;
        private const float MinimumScale = 0.000001f;
        private const float SourceUpperLegLength = 0.2158f;
        private const float SegmentLengthRatio = 0.92f;

        /// <summary>
        /// 创建或更新 13 个骨骼子级胶囊。只返回这些碰撞体，不创建刚体或运行时脚本。
        /// </summary>
        public static CapsuleCollider[] Build(Transform characterRoot)
        {
            if (characterRoot == null) throw new ArgumentNullException(nameof(characterRoot));

            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform candidate in characterRoot.GetComponentsInChildren<Transform>(true))
            {
                string key = NormalizeBoneName(candidate.name);
                if (!bones.ContainsKey(key)) bones.Add(key, candidate);
            }

            // 在新增节点前解析完整关节，缺骨骼时由生成器报告明确的资源配置错误。
            Transform hips = RequireBone(bones, "Hips");
            Transform spine = RequireBone(bones, "Spine");
            Transform chest = RequireBone(bones, "Chest");
            Transform upperChest = RequireBone(bones, "UpperChest");
            Transform neck = RequireBone(bones, "Neck");
            Transform head = RequireBone(bones, "Head");
            Transform leftEye = RequireBone(bones, "Left_Eye");
            Transform rightEye = RequireBone(bones, "Right_Eye");
            Transform leftUpperArm = RequireBone(bones, "Left_UpperArm");
            Transform leftLowerArm = RequireBone(bones, "Left_LowerArm");
            Transform leftHand = RequireBone(bones, "Left_Hand");
            Transform rightUpperArm = RequireBone(bones, "Right_UpperArm");
            Transform rightLowerArm = RequireBone(bones, "Right_LowerArm");
            Transform rightHand = RequireBone(bones, "Right_Hand");
            Transform leftUpperLeg = RequireBone(bones, "Left_UpperLeg");
            Transform leftLowerLeg = RequireBone(bones, "Left_LowerLeg");
            Transform leftFoot = RequireBone(bones, "Left_Foot", "Left_Feet");
            Transform rightUpperLeg = RequireBone(bones, "Right_UpperLeg");
            Transform rightLowerLeg = RequireBone(bones, "Right_LowerLeg");
            Transform rightFoot = RequireBone(bones, "Right_Foot", "Right_Feet");

            // 源模型约一米高，以实际关节距离换算导入单位，避免依赖 FBX 骨轴或厘米缩放。
            float meanUpperLegLength = (Vector3.Distance(leftUpperLeg.position, leftLowerLeg.position)
                + Vector3.Distance(rightUpperLeg.position, rightLowerLeg.position)) * 0.5f;
            if (meanUpperLegLength < MinimumScale)
                throw new InvalidOperationException("怪物大腿关节重合，无法配置布料碰撞体。");
            float characterScale = meanUpperLegLength / SourceUpperLegLength;

            var segments = new[]
            {
                new Segment(hips, spine.position, 0.09f),
                new Segment(spine, chest.position, 0.095f),
                new Segment(chest, upperChest.position, 0.10f),
                new Segment(upperChest, neck.position, 0.085f),
                new Segment(head, (leftEye.position + rightEye.position) * 0.5f, 0.06f),
                new Segment(leftUpperArm, leftLowerArm.position, 0.048f),
                new Segment(leftLowerArm, leftHand.position, 0.04f),
                new Segment(rightUpperArm, rightLowerArm.position, 0.048f),
                new Segment(rightLowerArm, rightHand.position, 0.04f),
                new Segment(leftUpperLeg, leftLowerLeg.position, 0.058f),
                new Segment(leftLowerLeg, leftFoot.position, 0.043f),
                new Segment(rightUpperLeg, rightLowerLeg.position, 0.058f),
                new Segment(rightLowerLeg, rightFoot.position, 0.043f),
            };

            var result = new CapsuleCollider[segments.Length];
            for (int i = 0; i < segments.Length; i++)
                result[i] = BuildCapsule(segments[i], characterScale);
            return result;
        }

        private static CapsuleCollider BuildCapsule(Segment segment, float characterScale)
        {
            Vector3 start = segment.Bone.position;
            Vector3 delta = segment.End - start;
            float length = delta.magnitude;
            if (length < MinimumScale)
                throw new InvalidOperationException("怪物骨骼端点重合：" + segment.Bone.name);

            string childName = ColliderPrefix + segment.Bone.name;
            Transform child = segment.Bone.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(segment.Bone, false);
            }

            child.gameObject.layer = IgnoreRaycastLayer;
            child.localScale = Vector3.one;
            child.position = start + delta * 0.5f;
            child.rotation = Quaternion.FromToRotation(Vector3.up, delta / length);

            float radialScale = Mathf.Max(child.TransformVector(Vector3.right).magnitude,
                child.TransformVector(Vector3.forward).magnitude);
            float axialScale = child.TransformVector(Vector3.up).magnitude;
            if (radialScale < MinimumScale || axialScale < MinimumScale)
                throw new InvalidOperationException("怪物骨骼包含零缩放：" + segment.Bone.name);

            CapsuleCollider capsule = child.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = child.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.center = Vector3.zero;
            capsule.radius = segment.Radius * characterScale / radialScale;
            // height 是包含两端半球的总高，不再额外加两倍半径；短躯干段自然收为球形。
            capsule.height = Mathf.Max(length * SegmentLengthRatio / axialScale, capsule.radius * 2f);
            capsule.isTrigger = true;
            capsule.enabled = true;
            return capsule;
        }

        private static Transform RequireBone(Dictionary<string, Transform> bones, params string[] names)
        {
            foreach (string name in names)
                if (bones.TryGetValue(NormalizeBoneName(name), out Transform bone)) return bone;
            throw new InvalidOperationException("怪物缺少布料碰撞关节：" + string.Join(" / ", names));
        }

        private static string NormalizeBoneName(string name)
        {
            // 兼容 FBX 命名空间、下划线及常见分隔符，仍按完整骨名匹配。
            int prefixEnd = Mathf.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            var normalized = new StringBuilder(name.Length);
            for (int i = prefixEnd + 1; i < name.Length; i++)
                if (char.IsLetterOrDigit(name[i])) normalized.Append(char.ToLowerInvariant(name[i]));
            return normalized.ToString();
        }

        private readonly struct Segment
        {
            public readonly Transform Bone;
            public readonly Vector3 End;
            public readonly float Radius;

            public Segment(Transform bone, Vector3 end, float radius)
            {
                Bone = bone;
                End = end;
                Radius = radius;
            }
        }
    }
}
