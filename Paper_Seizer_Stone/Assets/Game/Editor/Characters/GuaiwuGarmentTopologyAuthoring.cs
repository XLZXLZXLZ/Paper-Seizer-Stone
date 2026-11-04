using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Saved-asset construction only. Starts from the unmodified FBX, never from a
    // previously masked/cropped candidate. The caller owns backup and persistence.
    public static class GuaiwuGarmentTopologyAuthoring
    {
        public sealed class Result
        {
            public Mesh Mesh;
            public int[] SourceVertexOfNewVertex;
            public Vector3[] ModelPoints;
            // Multiplier for the original full garment's per-particle freedom.
            // Structural upper garment is skinned; only the lower hem simulates.
            public float[] ClothFreedom;
            public int RemovedTriangles, LeftSleeveVertices, WeldedVertices;
        }

        sealed class Edge { public int A, B, Count; public float Length; }

        public static Result Build(SkinnedMeshRenderer source, Transform modelRoot)
        {
            Mesh original = source.sharedMesh;
            if (original == null || original.vertexCount != 10471)
                throw new InvalidOperationException("需要原始 Guaiwu_Cloak FBX 网格，不能在裁剪候选上重复制作。");
            Vector3[] vertices = original.vertices;
            var points = new List<Vector3>();
            var groups = new List<List<int>>();
            var lookup = new Dictionary<Vector3Int, int>();
            var groupOf = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 p = modelRoot.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000000), Mathf.RoundToInt(p.y * 1000000), Mathf.RoundToInt(p.z * 1000000));
                if (!lookup.TryGetValue(key, out int g))
                { g = groups.Count; lookup.Add(key, g); groups.Add(new List<int>()); points.Add(p); }
                groupOf[i] = g; groups[g].Add(i);
            }
            var graph = Enumerable.Range(0, groups.Count).Select(_ => new List<Edge>()).ToArray();
            var edges = new Dictionary<long, Edge>();
            int[] triangles = original.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; ++j)
                {
                    int a = groupOf[triangles[i + j]], b = groupOf[triangles[i + (j + 1) % 3]];
                    if (a == b) continue;
                    long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (!edges.TryGetValue(key, out Edge edge))
                    {
                        edge = new Edge { A = a, B = b, Length = Vector3.Distance(points[a], points[b]) };
                        edges.Add(key, edge); graph[a].Add(edge); graph[b].Add(edge);
                    }
                    ++edge.Count;
                }

            // These source boundary components identify actual sleeve/front/hem
            // surfaces, including UV seam duplicates after positional welding.
            var border = Enumerable.Range(0, groups.Count).Select(_ => new List<int>()).ToArray();
            foreach (Edge edge in edges.Values.Where(e => e.Count == 1))
            { border[edge.A].Add(edge.B); border[edge.B].Add(edge.A); }
            var boundaries = new List<List<int>>();
            var seen = new HashSet<int>();
            for (int i = 0; i < border.Length; ++i)
            {
                if (border[i].Count == 0 || !seen.Add(i)) continue;
                var queue = new Queue<int>(); var component = new List<int>(); queue.Enqueue(i);
                while (queue.Count > 0)
                { int g = queue.Dequeue(); component.Add(g); foreach (int n in border[g]) if (seen.Add(n)) queue.Enqueue(n); }
                boundaries.Add(component);
            }
            List<int> Boundary(int count) => boundaries.Single(b => b.Count == count);
            List<int> leftCuff = Boundary(266), rightCuff = Boundary(268), front = Boundary(272), hem = Boundary(746);
            if (leftCuff.Average(g => points[g].x) >= rightCuff.Average(g => points[g].x))
                throw new InvalidOperationException("原始左右袖口边界不符合模型朝向，停止裁剪。");
            float[] left = Distance(leftCuff, graph), right = Distance(rightCuff, graph), opening = Distance(front, graph), bottom = Distance(hem, graph);
            var margin = points.Select((p, g) => Mathf.Min(right[g], opening[g], bottom[g]) - left[g]).ToArray();
            bool[] sleeve = margin.Select(m => m > 0).ToArray();
            int Bone(string name)
            {
                int index = Array.FindIndex(source.bones, b => b.name == name);
                if (index < 0) throw new InvalidOperationException("原衣服缺少骨骼 " + name);
                return index;
            }
            int upper = Bone("Left_UpperArm"), lower = Bone("Left_LowerArm"), shoulder = Bone("Left_Shoulder");
            Vector3 BindPoint(int b) => modelRoot.InverseTransformPoint(source.transform.TransformPoint(original.bindposes[b].inverse.MultiplyPoint3x4(Vector3.zero)));
            Vector3 aPoint = BindPoint(upper), bPoint = BindPoint(lower);
            Vector3 axis = (bPoint - aPoint).normalized, cut = Vector3.Lerp(aPoint, bPoint, .5f);
            bool[] remove = points.Select((p, g) => sleeve[g] && Vector3.Dot(p - cut, axis) > 0).ToArray();
            var used = new HashSet<int>();
            var keptSubMeshes = new List<int[]>();
            int removed = 0;
            for (int subMesh = 0; subMesh < original.subMeshCount; ++subMesh)
            {
                var kept = new List<int>(); int[] sub = original.GetTriangles(subMesh);
                for (int i = 0; i < sub.Length; i += 3)
                {
                    // A triangle touching a competing semantic region belongs
                    // to the attachment/lapel side and must never be cut away.
                    bool onlySleeve = sleeve[groupOf[sub[i]]] && sleeve[groupOf[sub[i + 1]]] && sleeve[groupOf[sub[i + 2]]];
                    if (onlySleeve && (remove[groupOf[sub[i]]] || remove[groupOf[sub[i + 1]]] || remove[groupOf[sub[i + 2]]]))
                    { ++removed; continue; }
                    for (int k = 0; k < 3; ++k) { kept.Add(sub[i + k]); used.Add(sub[i + k]); }
                }
                keptSubMeshes.Add(kept.ToArray());
            }
            if (removed < 500 || removed > 2500)
                throw new InvalidOperationException("袖片裁剪范围异常，未生成网格：" + removed);
            int[] sourceIds = used.OrderBy(i => i).ToArray();
            var newOfSource = Enumerable.Repeat(-1, vertices.Length).ToArray();
            for (int i = 0; i < sourceIds.Length; ++i) newOfSource[sourceIds[i]] = i;
            var mesh = new Mesh { name = "Guaiwu_Cloak_TopologyShortSleeve", indexFormat = original.indexFormat };
            mesh.vertices = sourceIds.Select(i => vertices[i]).ToArray();
            if (original.normals.Length == vertices.Length) mesh.normals = sourceIds.Select(i => original.normals[i]).ToArray();
            if (original.tangents.Length == vertices.Length) mesh.tangents = sourceIds.Select(i => original.tangents[i]).ToArray();
            if (original.colors.Length == vertices.Length) mesh.colors = sourceIds.Select(i => original.colors[i]).ToArray();
            for (int channel = 0; channel < 8; ++channel)
            {
                var uv = new List<Vector4>(); original.GetUVs(channel, uv);
                if (uv.Count == vertices.Length) mesh.SetUVs(channel, sourceIds.Select(i => uv[i]).ToList());
            }
            mesh.bindposes = original.bindposes;
            mesh.subMeshCount = original.subMeshCount;
            for (int sub = 0; sub < keptSubMeshes.Count; ++sub)
                mesh.SetTriangles(keptSubMeshes[sub].Select(i => newOfSource[i]).ToArray(), sub);

            var groupWeights = groups.Select(_ => new Dictionary<int, float>()).ToArray();
            using (var counts = original.GetBonesPerVertex())
            using (var weights = original.GetAllBoneWeights())
            {
                int cursor = 0;
                for (int vertex = 0; vertex < vertices.Length; ++vertex)
                {
                    int g = groupOf[vertex]; var map = groupWeights[g];
                    for (int j = 0; j < counts[vertex]; ++j)
                    {
                        BoneWeight1 influence = weights[cursor++];
                        map.TryGetValue(influence.boneIndex, out float previous);
                        map[influence.boneIndex] = previous + influence.weight / groups[g].Count;
                    }
                }
            }
            // Only the true sleeve-side region gets arm support. Hood and lapel
            // keep original FBX weights, even where they overlap it in XY.
            for (int g = 0; g < groups.Count; ++g)
            {
                if (!sleeve[g]) continue;
                float alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(margin[g] / .045f));
                var map = groupWeights[g];
                foreach (int key in map.Keys.ToArray()) map[key] *= 1 - alpha;
                map.TryGetValue(upper, out float u); map[upper] = u + .85f * alpha;
                map.TryGetValue(shoulder, out float s); map[shoulder] = s + .15f * alpha;
            }
            var newCounts = new NativeArray<byte>(sourceIds.Length, Allocator.Temp);
            var all = new List<BoneWeight1>();
            try
            {
                for (int i = 0; i < sourceIds.Length; ++i)
                {
                    var sorted = groupWeights[groupOf[sourceIds[i]]].Where(w => w.Value > .0000001f).OrderByDescending(w => w.Value).ToArray();
                    float sum = sorted.Sum(w => w.Value); newCounts[i] = (byte)sorted.Length;
                    foreach (var w in sorted) all.Add(new BoneWeight1 { boneIndex = w.Key, weight = w.Value / sum });
                }
                using (var nativeWeights = new NativeArray<BoneWeight1>(all.ToArray(), Allocator.Temp)) mesh.SetBoneWeights(newCounts, nativeWeights);
            }
            finally { newCounts.Dispose(); }
            mesh.RecalculateBounds();
            Vector3[] newPoints = sourceIds.Select(i => points[groupOf[i]]).ToArray();
            return new Result
            {
                Mesh = mesh, SourceVertexOfNewVertex = sourceIds, ModelPoints = newPoints,
                ClothFreedom = newPoints.Select(p => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .22f, p.y))).ToArray(),
                RemovedTriangles = removed, LeftSleeveVertices = sourceIds.Count(i => sleeve[groupOf[i]]), WeldedVertices = points.Count
            };
        }

        static float[] Distance(IEnumerable<int> seeds, List<Edge>[] graph)
        {
            var distance = Enumerable.Repeat(float.PositiveInfinity, graph.Length).ToArray();
            var pending = new SortedSet<Tuple<float, int>>();
            foreach (int seed in seeds) { distance[seed] = 0; pending.Add(Tuple.Create(0f, seed)); }
            while (pending.Count > 0)
            {
                var current = pending.Min; pending.Remove(current);
                int g = current.Item2; if (current.Item1 != distance[g]) continue;
                foreach (Edge edge in graph[g])
                {
                    int next = edge.A == g ? edge.B : edge.A; float value = current.Item1 + edge.Length;
                    if (value >= distance[next]) continue;
                    if (!float.IsPositiveInfinity(distance[next])) pending.Remove(Tuple.Create(distance[next], next));
                    distance[next] = value; pending.Add(Tuple.Create(value, next));
                }
            }
            return distance;
        }
    }
}
