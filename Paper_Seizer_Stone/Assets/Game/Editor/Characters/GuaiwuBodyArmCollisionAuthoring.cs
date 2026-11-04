using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Explicit Editor authoring only. The caller owns the prefab transaction and saving.
    // Input is JSON text supplied by the caller; this helper reads no files.
    // Collider centres and radii remain in source bone-local metres: the bone
    // hierarchy supplies the actor scale exactly once.
    public static class GuaiwuBodyArmCollisionAuthoring
    {
        const string ManagedPrefix = "ClothCollision_ActualBody_";
        const float PalmSkin = 0.003f;
        const float FingerRootReach = 0.015f;
        const int NativeSphereLimit = 32;

        static readonly string[] ArmNames =
        {
            "Left_UpperArm", "Left_LowerArm", "Right_UpperArm", "Right_LowerArm"
        };

        [Serializable]
        public sealed class Result
        {
            public string source;
            public string units = "source bone-local metres; actor scale is carried by collider parents";
            public int retainedCapsules, removedArmCapsules, removedElbowPairs;
            public int conicSpherePairs, handSpherePairs, newSpherePairs, nativeSpheres;
            public int handSphereOverrides, handSphereFallbacks;
            public RadiusRecord[] radii;
        }

        [Serializable]
        public sealed class RadiusRecord
        {
            public string bone, node, role, fitSource;
            public Vector3 centerBoneLocal;
            public float radius;
            // Core points used by FitPalm. A JSON fit may cover additional Body
            // contact samples prepared externally; those are not counted here.
            public int selectedBodyVertices;
            public bool handOverride;
            public Vector3 coreCenterBoneLocal;
            public float coreRadius, coreContainmentMargin;
        }

        [Serializable] sealed class FitDocument
        {
            public int schemaVersion;
            public BoneFit[] bones;
            public HandFit[] handSpheres;
        }
        [Serializable] sealed class BoneFit { public string bone; public PairFit[] pairs; }
        [Serializable] sealed class PairFit
        {
            public Vector3 firstCenterBoneLocal, secondCenterBoneLocal;
            public float firstRadius, secondRadius;
        }
        [Serializable] sealed class HandFit
        {
            public string bone;
            public JsonPoint centerBoneLocal;
            public float radius;
        }
        [Serializable] sealed class JsonPoint
        {
            public float x, y, z;
            public Vector3 Value => new Vector3(x, y, z);
        }

        sealed class SpherePlan
        {
            public Transform Bone;
            public string Node, Role, FitSource;
            public Vector3 Center;
            public float Radius;
            public int Samples;
            public bool HandOverride;
            public Vector3 CoreCenter;
            public float CoreRadius, CoreContainmentMargin;
        }

        public static Result Apply(GameObject root, string fitJson)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(fitJson)) throw new ArgumentException("Arm fit JSON is empty.", nameof(fitJson));
            const string source = "caller-provided schema 1 JSON";
            FitDocument fit = JsonUtility.FromJson<FitDocument>(fitJson);
            if (fit == null || fit.schemaVersion != 1 || fit.bones == null || fit.bones.Length != ArmNames.Length)
                throw new InvalidOperationException("Arm fit JSON must contain schema 1 and exactly four arm bones: " + source);
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Guaiwu_Body");
            var cloak = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Guaiwu_Cloak");
            Cloth cloth = cloak.GetComponent<Cloth>();
            if (cloth == null) throw new InvalidOperationException("Guaiwu_Cloak has no Cloth component.");
            if (body.sharedMesh == null || !body.sharedMesh.isReadable)
                throw new InvalidOperationException("Guaiwu_Body requires its readable original skinned mesh.");

            var armCapsuleNames = new HashSet<string>(ArmNames.Select(name => "ClothCollision_" + name), StringComparer.Ordinal);
            CapsuleCollider[] oldCapsules = cloth.capsuleColliders;
            if (oldCapsules.Any(c => c == null) || oldCapsules.Distinct().Count() != oldCapsules.Length)
                throw new InvalidOperationException("Existing Cloth capsule references contain nulls or duplicates.");
            CapsuleCollider[] retained = oldCapsules.Where(c => !armCapsuleNames.Contains(c.name)).ToArray();
            if (retained.Length != 9)
                throw new InvalidOperationException("Expected exactly nine non-arm capsules; found " + retained.Length + ".");

            var preservedPairs = new List<ClothSphereColliderPair>();
            int removedElbows = 0;
            foreach (ClothSphereColliderPair pair in cloth.sphereColliders)
            {
                if (IsOldElbow(pair.first) || IsOldElbow(pair.second)) { removedElbows++; continue; }
                if (IsManaged(pair.first) || IsManaged(pair.second)) continue; // Idempotent re-application.
                if (pair.first == null) throw new InvalidOperationException("Existing Cloth sphere pair has no first sphere.");
                preservedPairs.Add(pair);
            }

            // Prepare all data and check the budget before adding any nodes.
            var conics = new List<SpherePlan[]>();
            var plans = new List<SpherePlan>();
            foreach (string name in ArmNames)
            {
                BoneFit boneFit = fit.bones.SingleOrDefault(b => b != null && b.bone == name);
                int requiredPairs = name.EndsWith("UpperArm", StringComparison.Ordinal) ? 1 : 2;
                if (boneFit == null || boneFit.pairs == null || boneFit.pairs.Length != requiredPairs)
                    throw new InvalidOperationException("Unexpected conic pair count for " + name + ".");
                Transform bone = RequireBone(body, name);
                for (int i = 0; i < boneFit.pairs.Length; i++)
                {
                    PairFit pair = boneFit.pairs[i];
                    if (pair == null) throw new InvalidOperationException("Null conic pair for " + name + ".");
                    var first = Plan(bone, name + "_" + i + "_A", pair.firstCenterBoneLocal, pair.firstRadius, "arm conic endpoint");
                    var second = Plan(bone, name + "_" + i + "_B", pair.secondCenterBoneLocal, pair.secondRadius, "arm conic endpoint");
                    conics.Add(new[] { first, second });
                    plans.Add(first); plans.Add(second);
                }
            }
            var palms = new[] { ResolvePalm(body, "Left", fit.handSpheres), ResolvePalm(body, "Right", fit.handSpheres) };
            plans.AddRange(palms);
            int nativeSpheres = retained.Length * 2 + conics.Count * 2 + palms.Length
                + preservedPairs.Sum(pair => pair.second != null ? 2 : 1);
            if (conics.Count != 6 || nativeSpheres > NativeSphereLimit)
                throw new InvalidOperationException("Native Cloth sphere budget exceeded: " + nativeSpheres + " / " + NativeSphereLimit + ".");

            foreach (SpherePlan plan in plans)
            {
                Transform existing = plan.Bone.Find(plan.Node);
                if (existing != null && existing.GetComponent<SphereCollider>() == null)
                    throw new InvalidOperationException("Managed collider name is occupied by another object: " + plan.Node);
            }
            var activePairs = new List<ClothSphereColliderPair>(preservedPairs);
            foreach (SpherePlan[] pair in conics)
                activePairs.Add(new ClothSphereColliderPair(EnsureSphere(pair[0]), EnsureSphere(pair[1])));
            foreach (SpherePlan palm in palms) activePairs.Add(new ClothSphereColliderPair(EnsureSphere(palm)));
            // Old capsules and elbow sphere objects are preserved. Only the active
            // Cloth arrays change; unrelated body capsule references stay intact.
            cloth.capsuleColliders = retained;
            cloth.sphereColliders = activePairs.ToArray();
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(cloth))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(cloth);
            UnityEditor.EditorUtility.SetDirty(cloth);
            return new Result
            {
                source = source, retainedCapsules = retained.Length,
                removedArmCapsules = oldCapsules.Length - retained.Length, removedElbowPairs = removedElbows,
                conicSpherePairs = conics.Count, handSpherePairs = palms.Length,
                newSpherePairs = conics.Count + palms.Length, nativeSpheres = nativeSpheres,
                handSphereOverrides = palms.Count(p => p.HandOverride), handSphereFallbacks = palms.Count(p => !p.HandOverride),
                radii = plans.Select(plan => new RadiusRecord
                {
                    bone = plan.Bone.name, node = plan.Node, role = plan.Role, fitSource = plan.FitSource,
                    centerBoneLocal = plan.Center, radius = plan.Radius, selectedBodyVertices = plan.Samples,
                    handOverride = plan.HandOverride, coreCenterBoneLocal = plan.CoreCenter,
                    coreRadius = plan.CoreRadius, coreContainmentMargin = plan.CoreContainmentMargin
                }).ToArray()
            };
        }

        static SpherePlan Plan(Transform bone, string suffix, Vector3 center, float radius, string role)
        {
            if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z) || !Finite(radius) || radius <= 0f)
                throw new InvalidOperationException("Invalid collider fit for " + suffix + ".");
            return new SpherePlan
            {
                Bone = bone, Node = ManagedPrefix + suffix, Center = center, Radius = radius,
                Role = role, FitSource = "JSON bones/pairs"
            };
        }

        static SpherePlan ResolvePalm(SkinnedMeshRenderer body, string side, HandFit[] overrides)
        {
            SpherePlan core = FitPalm(body, side);
            core.FitSource = "Body bind-space FitPalm; handSpheres entry absent";
            core.CoreCenter = core.Center;
            core.CoreRadius = core.Radius;
            core.CoreContainmentMargin = 0f;
            if (overrides == null) return core;
            HandFit[] matching = overrides.Where(p => p != null && p.bone == core.Bone.name).ToArray();
            if (matching.Length == 0) return core;
            if (matching.Length > 1)
                throw new InvalidOperationException("Duplicate handSpheres entries for " + core.Bone.name + ".");
            HandFit fit = matching[0];
            if (fit.centerBoneLocal == null || !Finite(fit.radius) || fit.radius <= 0f)
            {
                core.FitSource = "Body bind-space FitPalm; handSpheres centre/radius incomplete or invalid";
                return core;
            }
            Vector3 center = fit.centerBoneLocal.Value;
            if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z))
            {
                core.FitSource = "Body bind-space FitPalm; handSpheres centre is non-finite";
                return core;
            }
            float margin = fit.radius - Vector3.Distance(center, core.Center) - core.Radius;
            if (!Finite(margin) || margin < -0.00001f)
                throw new InvalidOperationException("JSON hand sphere does not enclose the original Hand core sphere: "
                    + core.Bone.name + "; containment margin=" + margin + ".");
            // Keep the external fit exactly as authored: it already includes skin
            // and extra contact coverage. Do not inflate it or apply actor scale.
            core.Center = center;
            core.Radius = fit.radius;
            core.Role = "JSON Hand contact sphere enclosing original core";
            core.FitSource = "JSON handSpheres";
            core.HandOverride = true;
            core.CoreContainmentMargin = margin;
            return core;
        }

        static SpherePlan FitPalm(SkinnedMeshRenderer body, string side)
        {
            Mesh mesh = body.sharedMesh;
            Transform[] bones = body.bones;
            Matrix4x4[] binds = mesh.bindposes;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3[] vertices = mesh.vertices;
            if (binds.Length != bones.Length || weights.Length != vertices.Length)
                throw new InvalidOperationException("Body bindposes, bones and vertex weights do not match.");
            Transform hand = RequireBone(body, side + "_Hand");
            int handIndex = Array.IndexOf(bones, hand);
            Matrix4x4 toHand = binds[handIndex];
            var proximal = Enumerable.Range(0, bones.Length)
                .Where(i => bones[i] != null && bones[i].IsChildOf(hand)
                    && bones[i].name.StartsWith(side + "_", StringComparison.Ordinal)
                    && bones[i].name.EndsWith("Proximal", StringComparison.Ordinal))
                .ToDictionary(i => i, i => toHand.MultiplyPoint3x4(binds[i].inverse.MultiplyPoint3x4(Vector3.zero)));
            Vector3[] nonThumbRoots = proximal.Where(p => !bones[p.Key].name.Contains("Thumb")).Select(p => p.Value).ToArray();
            if (nonThumbRoots.Length == 0) throw new InvalidOperationException("No bind-space finger roots for " + side + " palm.");
            Vector3 axis = nonThumbRoots.Aggregate(Vector3.zero, (sum, p) => sum + p).normalized;
            if (axis.sqrMagnitude < 0.9f) throw new InvalidOperationException("Degenerate bind-space palm axis for " + side + ".");
            float farLimit = nonThumbRoots.Max(p => Vector3.Dot(p, axis)) + 0.006f;
            var points = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int dominant = Dominant(weights[i]);
                if (dominant != handIndex && !proximal.ContainsKey(dominant)) continue;
                Vector3 point = toHand.MultiplyPoint3x4(vertices[i]);
                float along = Vector3.Dot(point, axis);
                if (along < -0.008f || along > farLimit) continue;
                // Only the proximal root neighbourhood may extend the palm;
                // intermediate/distal finger bones and fingertip geometry never do.
                if (dominant != handIndex && (point - proximal[dominant]).sqrMagnitude > FingerRootReach * FingerRootReach) continue;
                if (!Finite(point.x) || !Finite(point.y) || !Finite(point.z))
                    throw new InvalidOperationException("Non-finite Body palm vertex for " + side + ".");
                points.Add(point);
            }
            if (points.Count < 4) throw new InvalidOperationException("Insufficient dominant Hand/core proximal vertices for " + side + " palm: " + points.Count);
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            Vector3 mean = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Count;
            Vector3 extremeA = points.OrderByDescending(p => (p - points[0]).sqrMagnitude).First();
            Vector3 extremeB = points.OrderByDescending(p => (p - extremeA).sqrMagnitude).First();
            Vector3 bestCenter = bounds.center;
            float bestRadius = float.PositiveInfinity;
            foreach (Vector3 center in new[] { bounds.center, mean, (extremeA + extremeB) * 0.5f })
            {
                float radius = Mathf.Sqrt(points.Max(p => (p - center).sqrMagnitude));
                if (radius < bestRadius) { bestRadius = radius; bestCenter = center; }
            }
            // The JSON arm fits already include skin; only this newly fitted palm
            // receives one .003 m margin. No actor-scale multiplication is applied.
            SpherePlan plan = Plan(hand, side + "_Palm", bestCenter, bestRadius + PalmSkin, "Hand core enclosing sphere + .003m skin");
            plan.Samples = points.Count;
            return plan;
        }

        static int Dominant(BoneWeight weight)
        {
            int bone = weight.boneIndex0; float value = weight.weight0;
            if (weight.weight1 > value) { value = weight.weight1; bone = weight.boneIndex1; }
            if (weight.weight2 > value) { value = weight.weight2; bone = weight.boneIndex2; }
            if (weight.weight3 > value) bone = weight.boneIndex3;
            return bone;
        }

        static Transform RequireBone(SkinnedMeshRenderer body, string name)
        {
            Transform[] matches = body.bones.Where(b => b != null && b.name == name).Distinct().ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one Body bone named " + name + ".");
            return matches[0];
        }

        static SphereCollider EnsureSphere(SpherePlan plan)
        {
            Transform node = plan.Bone.Find(plan.Node);
            if (node == null)
            {
                node = new GameObject(plan.Node).transform;
                node.SetParent(plan.Bone, false);
            }
            node.localPosition = plan.Center;
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one;
            node.gameObject.layer = 2;
            node.gameObject.SetActive(true);
            SphereCollider sphere = node.GetComponent<SphereCollider>();
            if (sphere == null) sphere = node.gameObject.AddComponent<SphereCollider>();
            sphere.center = Vector3.zero;
            sphere.radius = plan.Radius;
            sphere.isTrigger = true;
            sphere.enabled = true;
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(sphere))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject);
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(sphere);
            }
            return sphere;
        }

        static bool IsOldElbow(SphereCollider sphere) => sphere != null
            && (sphere.name == "ClothCollision_Left_Elbow" || sphere.name == "ClothCollision_Right_Elbow");
        static bool IsManaged(SphereCollider sphere) => sphere != null && sphere.name.StartsWith(ManagedPrefix, StringComparison.Ordinal);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
