using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    public static class GuaiwuSleeveClothAuthoring
    {
        public sealed class Snapshot
        {
            public ClothSkinningCoefficient[] Coefficients;
            public int[] ParticleOfVertex;
            public CapsuleCollider[] Colliders;
        }
        [Serializable]
        public sealed class Result
        {
            public int particles, tightenedParticles, untouchedParticles;
        }
        [Serializable]
        public sealed class NaturalDrapeResult
        {
            public int particles, releasedSleeveParticles, untouchedParticles, capsuleColliders, elbowSpheres;
            public float cuffMaximumDistance, bendingStiffness, damping;
        }

        public static int EnableTriangleContacts(SkinnedMeshRenderer renderer, Cloth cloth)
        {
            int[] particle = Map(renderer, cloth);
            int[] triangles = renderer.sharedMesh.triangles;
            var indices = new List<uint>();
            var seen = new HashSet<string>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = particle[triangles[i]], b = particle[triangles[i + 1]], c = particle[triangles[i + 2]];
                if (a == b || b == c || c == a) continue;
                int min = Mathf.Min(a, b, c), max = Mathf.Max(a, b, c), middle = a + b + c - min - max;
                if (!seen.Add(min + ":" + middle + ":" + max)) continue;
                indices.Add((uint)a); indices.Add((uint)b); indices.Add((uint)c); indices.Add(0);
            }
            cloth.SetVirtualParticleWeights(new List<Vector3> { new Vector3(1f / 3f, 1f / 3f, 1f / 3f) });
            cloth.SetVirtualParticleIndices(indices);
            cloth.useVirtualParticles = 1f;
            return indices.Count / 4;
        }

        public static NaturalDrapeResult ApplyNaturalDrape(SkinnedMeshRenderer renderer, Cloth cloth, float[] strength)
        {
            Snapshot saved = Capture(renderer, cloth);
            if (strength.Length != saved.ParticleOfVertex.Length)
                throw new InvalidOperationException("袖口选择与当前网格顶点数不对应。");
            var sleeve = new float[saved.Coefficients.Length];
            for (int vertex = 0; vertex < strength.Length; vertex++)
            {
                int particle = saved.ParticleOfVertex[vertex];
                sleeve[particle] = Mathf.Max(sleeve[particle], strength[vertex]);
            }
            var coefficients = saved.Coefficients;
            var result = new NaturalDrapeResult
            {
                particles = coefficients.Length, cuffMaximumDistance = 0.06f,
                bendingStiffness = 0.15f, damping = 0.45f,
                capsuleColliders = saved.Colliders.Length
            };
            for (int i = 0; i < coefficients.Length; i++)
            {
                if (sleeve[i] > 0)
                {
                    // The cuff is the free end, not an attachment ring. Release old zero constraints.
                    coefficients[i].maxDistance = Mathf.Lerp(coefficients[i].maxDistance,
                        result.cuffMaximumDistance, Mathf.SmoothStep(0, 1, sleeve[i]));
                    result.releasedSleeveParticles++;
                }
                else result.untouchedParticles++;
            }
            cloth.coefficients = coefficients;
            cloth.enabled = true;
            cloth.useGravity = true;
            cloth.useTethers = true;
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = result.bendingStiffness;
            cloth.damping = result.damping;
            cloth.friction = 0.5f;
            cloth.externalAcceleration = Vector3.zero;
            cloth.randomAcceleration = Vector3.zero;
            cloth.worldVelocityScale = 0.2f;
            cloth.worldAccelerationScale = 0.1f;
            cloth.enableContinuousCollision = true;

            var spherePairs = cloth.sphereColliders.ToList();
            foreach (string side in new[] { "Left", "Right" })
            {
                var upper = saved.Colliders.Single(c => c.name == "ClothCollision_" + side + "_UpperArm");
                var lower = saved.Colliders.Single(c => c.name == "ClothCollision_" + side + "_LowerArm");
                Transform elbow = lower.transform.parent;
                string name = "ClothCollision_" + side + "_Elbow";
                Transform node = elbow.Find(name);
                if (node == null)
                {
                    node = new GameObject(name).transform;
                    node.SetParent(elbow, false);
                }
                node.localPosition = Vector3.zero;
                node.localRotation = Quaternion.identity;
                node.localScale = Vector3.one;
                node.gameObject.layer = 2;
                var sphere = node.GetComponent<SphereCollider>();
                if (sphere == null) sphere = node.gameObject.AddComponent<SphereCollider>();
                sphere.center = Vector3.zero;
                sphere.radius = Mathf.Min(CapsuleRadius(upper), CapsuleRadius(lower)) * 0.9f
                    / Mathf.Max(0.000001f, Mathf.Abs(node.lossyScale.x), Mathf.Abs(node.lossyScale.y), Mathf.Abs(node.lossyScale.z));
                sphere.isTrigger = true;
                sphere.enabled = true;
                spherePairs.RemoveAll(pair => pair.first == sphere || pair.second == sphere);
                spherePairs.Add(new ClothSphereColliderPair(sphere));
                result.elbowSpheres++;
            }
            cloth.sphereColliders = spherePairs.ToArray();
            return result;
        }

        private static float CapsuleRadius(CapsuleCollider capsule)
        {
            int axis = capsule.direction;
            Vector3 a = axis == 0 ? Vector3.up : Vector3.right;
            Vector3 b = axis == 2 ? Vector3.up : Vector3.forward;
            return capsule.radius * Mathf.Max(capsule.transform.TransformVector(a).magnitude,
                capsule.transform.TransformVector(b).magnitude);
        }
        public static Snapshot Capture(SkinnedMeshRenderer renderer, Cloth cloth)
        {
            return new Snapshot
            {
                Coefficients = cloth.coefficients,
                ParticleOfVertex = Map(renderer, cloth),
                Colliders = cloth.capsuleColliders
            };
        }
        public static Result Apply(Snapshot saved, SkinnedMeshRenderer renderer, Cloth cloth, float[] strength)
        {
            int[] next = Map(renderer, cloth);
            int count = cloth.vertices.Length;
            if (count != saved.Coefficients.Length || next.Length != saved.ParticleOfVertex.Length || next.Length != strength.Length)
                throw new InvalidOperationException("新旧布料焊接组数量改变，未迁移约束。");
            int[] previous = Enumerable.Repeat(-1, count).ToArray();
            float[] sleeve = new float[count];
            for (int vertex = 0; vertex < next.Length; vertex++)
            {
                int particle = next[vertex], old = saved.ParticleOfVertex[vertex];
                if (particle < 0 || old < 0) throw new InvalidOperationException("布料顶点没有对应粒子。");
                if (previous[particle] >= 0 && previous[particle] != old)
                    throw new InvalidOperationException("新网格合并了不同原粒子，未迁移约束。");
                previous[particle] = old;
                sleeve[particle] = Mathf.Max(sleeve[particle], strength[vertex]);
            }
            if (previous.Any(p => p < 0) || previous.Distinct().Count() != count)
                throw new InvalidOperationException("无法建立旧新布料粒子的一一对应。");
            var coefficients = new ClothSkinningCoefficient[count];
            var result = new Result { particles = count };
            for (int i = 0; i < count; i++)
            {
                coefficients[i] = saved.Coefficients[previous[i]];
                if (sleeve[i] > 0)
                {
                    float limit = sleeve[i] > 0.95f ? 0 : Mathf.Lerp(coefficients[i].maxDistance, 0.003f, sleeve[i]);
                    coefficients[i].maxDistance = Mathf.Min(coefficients[i].maxDistance, limit);
                    result.tightenedParticles++;
                }
                else result.untouchedParticles++;
            }
            cloth.coefficients = coefficients;
            cloth.capsuleColliders = saved.Colliders;
            return result;
        }

        private static int[] Map(SkinnedMeshRenderer renderer, Cloth cloth)
        {
            PropertyInfo actual = typeof(SkinnedMeshRenderer).GetProperty("actualRootBone", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Transform root = actual?.GetValue(renderer, null) as Transform;
            if (root == null) root = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            Vector3[] particles = cloth.vertices.Select(v => root.rotation * v + root.position).ToArray();
            Mesh baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                Vector3[] skin = baked.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray();
                Vector3[] rest = renderer.sharedMesh.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray();
                Vector3[] linear = LinearSkin(renderer);
                var particleSpaces = new List<Vector3[]> { particles,
                    cloth.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray(),
                    cloth.vertices.Select(v => root.TransformPoint(v)).ToArray(), cloth.vertices };
                int rootIndex = Array.FindIndex(renderer.bones, bone => bone == root);
                if (rootIndex >= 0)
                {
                    Matrix4x4 bindRoot = renderer.transform.localToWorldMatrix * renderer.sharedMesh.bindposes[rootIndex].inverse;
                    particleSpaces.Add(cloth.vertices.Select(v => bindRoot.MultiplyPoint3x4(v)).ToArray());
                    particleSpaces.Add(cloth.vertices.Select(v => bindRoot.rotation * v + (Vector3)bindRoot.GetColumn(3)).ToArray());
                }
                int[] result = null;
                foreach (Vector3[] particleSpace in particleSpaces)
                {
                    result = Match(linear, particleSpace) ?? Match(skin, particleSpace) ?? Match(rest, particleSpace);
                    if (result != null) break;
                }
                if (result == null) throw new InvalidOperationException("Cloth 粒子与渲染顶点无法按位置匹配，停止保存以保护原约束。root="
                    + root.name + "; particles=" + Describe(particles) + "; skin=" + Describe(skin) + "; linear=" + Describe(linear) + "; rest=" + Describe(rest));
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }

        private static Vector3[] LinearSkin(SkinnedMeshRenderer renderer)
        {
            Mesh mesh = renderer.sharedMesh;
            var matrices = renderer.bones.Select((bone, i) => bone.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            var vertices = mesh.vertices;
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var points = new Vector3[vertices.Length];
            int cursor = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = Vector3.zero;
                for (int j = 0; j < counts[i]; j++)
                {
                    BoneWeight1 weight = weights[cursor++];
                    point += matrices[weight.boneIndex].MultiplyPoint3x4(vertices[i]) * weight.weight;
                }
                points[i] = point;
            }
            return points;
        }

        private static int[] Match(Vector3[] vertices, Vector3[] particles)
        {
            const float size = 0.00002f;
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < particles.Length; i++)
            {
                Vector3Int key = Key(particles[i], size);
                if (!grid.TryGetValue(key, out var values)) grid[key] = values = new List<int>();
                values.Add(i);
            }
            int[] result = new int[vertices.Length];
            var used = new HashSet<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3Int key = Key(vertices[i], size); int found = -1;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (!grid.TryGetValue(key + new Vector3Int(x, y, z), out var options)) continue;
                    foreach (int candidate in options)
                    {
                        if ((particles[candidate] - vertices[i]).sqrMagnitude > size * size) continue;
                        if (found >= 0 && found != candidate) return null;
                        found = candidate;
                    }
                }
                if (found < 0) return null;
                result[i] = found; used.Add(found);
            }
            return used.Count == particles.Length ? result : null;
        }
        private static Vector3Int Key(Vector3 p, float size) => new Vector3Int(
            Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.y / size), Mathf.FloorToInt(p.z / size));
        private static string Describe(Vector3[] points)
        {
            if (points.Length == 0) return "empty";
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            return points.Length + ":" + bounds.ToString("F5");
        }
    }
}
