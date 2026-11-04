using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Editor asset authoring only. No automatic execution or menu entry; preserve the source FBX.
    public static class GuaiwuDrapeMeshAuthoring
    {
        public const string MeshPath = "Assets/Game/art/act/Guaiwu/Meshes/Guaiwu_Cloak_Draped.asset";
        public const string ReportPath = "Assets/Game/art/act/Guaiwu/Guaiwu_DrapeMeshRecord.json";
        const float OuterTransition = 0.18f;
        const int Iterations = 900;

        [Serializable] public sealed class Report
        {
            public string source, mesh, method, limitation;
            public int renderVertices, weldedVertices, leftRegion, rightRegion, leftTransition, rightTransition;
            public int changedVertices, maximumInfluences;
            public bool requiresUnlimitedSkinWeights = true;
            public float[] sleeveStrength;
        }
        struct Influence
        {
            public int bone; public float weight;
            public Influence(int bone, float weight) { this.bone = bone; this.weight = weight; }
        }
        sealed class Edge { public int a, b, count; public float length; }

        public static Mesh Build()
        {
            string source = AssetDatabase.FindAssets("Guaiwu_Rigged t:Model")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Single(p => Path.GetFileName(p).Equals("Guaiwu_Rigged.fbx", StringComparison.OrdinalIgnoreCase));
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject model = null;
            Mesh candidate = null;
            try
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source), previewScene);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                var cloak = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Guaiwu_Cloak");
                Mesh original = cloak.sharedMesh;
                candidate = UnityEngine.Object.Instantiate(original);
                candidate.name = "Guaiwu_Cloak_Draped";
                Vector3[] vertices = original.vertices;
                var groups = new List<List<int>>();
                var points = new List<Vector3>();
                var lookup = new Dictionary<Vector3Int, int>();
                var groupOf = new int[vertices.Length];
                for (int i = 0; i < vertices.Length; ++i)
                {
                    Vector3 p = model.transform.InverseTransformPoint(cloak.transform.TransformPoint(vertices[i]));
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000000), Mathf.RoundToInt(p.y * 1000000), Mathf.RoundToInt(p.z * 1000000));
                    if (!lookup.TryGetValue(key, out int g))
                    { g = groups.Count; lookup.Add(key, g); groups.Add(new List<int>()); points.Add(p); }
                    groups[g].Add(i); groupOf[i] = g;
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
                        if (!edges.TryGetValue(key, out Edge e))
                        {
                            e = new Edge { a = Math.Min(a, b), b = Math.Max(a, b), length = Vector3.Distance(points[a], points[b]) };
                            edges.Add(key, e); graph[a].Add(e); graph[b].Add(e);
                        }
                        e.count++;
                    }
                var border = Enumerable.Range(0, groups.Count).Select(_ => new List<int>()).ToArray();
                foreach (Edge e in edges.Values.Where(e => e.count == 1)) { border[e.a].Add(e.b); border[e.b].Add(e.a); }
                var loops = new List<List<int>>(); var seen = new HashSet<int>();
                for (int i = 0; i < border.Length; ++i)
                {
                    if (border[i].Count == 0 || !seen.Add(i)) continue;
                    var queue = new Queue<int>(); var component = new List<int>(); queue.Enqueue(i);
                    while (queue.Count > 0)
                    { int g = queue.Dequeue(); component.Add(g); foreach (int n in border[g]) if (seen.Add(n)) queue.Enqueue(n); }
                    if (component.Count == 266 || component.Count == 268) loops.Add(component);
                }
                if (loops.Count != 2) throw new InvalidOperationException("Expected the source 266/268-point cuff boundaries.");
                BoneWeight[] sourceWeights = original.boneWeights;
                var old = groups.Select(g => Read(sourceWeights[g[0]])).ToArray();
                var weights = old.Select(w => new List<Influence>(w)).ToArray();
                var strength = new float[groups.Count];
                Bounds bounds = new Bounds(points[0], Vector3.zero); foreach (Vector3 p in points) bounds.Encapsulate(p);
                float centerX = bounds.center.x;
                var report = new Report
                {
                    source = source, mesh = MeshPath, renderVertices = vertices.Length, weldedVertices = groups.Count,
                    method = "Original FBX geometry/bindposes/UV. Loose sleeve reference follows Chest .85 + same-side Shoulder .15 throughout each whole sleeve panel; no cuff/forearm pin in the skin reference. Cloth, gravity and arm collisions must supply the secondary motion. A 0.18 m surface-distance attachment blend returns continuously to the source weights and retains all influences (no Top4 truncation). A separate four-component harmonic field (900 Jacobi iterations, inverse-edge-length conductance) supplies per-render-vertex sleeveStrength only; zero at the proximal attachment and one at the cuff.",
                    limitation = "Source sleeves share topology with the lower coat. The attachment transition can change upper coat skinning, but the lower hem below bind height .18 m is untouched. Runtime must use QualitySettings.skinWeights=SkinWeights.Unlimited and renderer.quality=SkinQuality.Auto, because attachment blends retain up to six influences. First-frame offline comparison on the old sleeve edge region: edges >2x 199 to 0, >5x 14 to 0, max ratio about 1.327; this does not establish simulated behavior.",
                    sleeveStrength = new float[vertices.Length]
                };
                foreach (string side in new[] { "Right", "Left" })
                {
                    int sign = side == "Right" ? 1 : -1;
                    List<int> cuff = loops.OrderByDescending(l => sign * (l.Average(g => points[g].x) - centerX)).First();
                    var allowed = points.Select(p => p.y >= 0.34f && p.y <= 0.84f
                        && sign * (p.x - centerX) >= 0.155f - 0.09f * Mathf.Clamp01((p.y - 0.68f) / 0.14f)).ToArray();
                    var region = new HashSet<int>(cuff); var queue = new Queue<int>(cuff);
                    while (queue.Count > 0)
                    { int g = queue.Dequeue(); foreach (Edge e in graph[g]) { int n = Other(e, g); if (allowed[n] && region.Add(n)) queue.Enqueue(n); } }
                    float[] cuffDistance = Distance(cuff, graph, Enumerable.Repeat(true, points.Count).ToArray(), 0.16f);
                    var boundary = new HashSet<int>(region.Where(g => graph[g].Any(e => !region.Contains(Other(e, g)))));
                    var field = Enumerable.Range(0, points.Count).Select(_ => new Vector4(0, 0, 0, 1)).ToArray();
                    var fixedPoints = new HashSet<int>();
                    foreach (int g in region)
                    {
                        Vector3 p = points[g]; float u = sign * (p.x - centerX);
                        if (cuffDistance[g] < 0.025f) { field[g] = new Vector4(1, 0, 0, 0); fixedPoints.Add(g); }
                        else if (boundary.Contains(g)) fixedPoints.Add(g);
                        else if (p.y > 0.70f && p.y < 0.748f && u > 0.19f)
                        { field[g] = new Vector4(0, 1, 0, 0); fixedPoints.Add(g); }
                        else if (p.y > 0.77f && u > 0.105f)
                        { field[g] = new Vector4(0, 0, 0.7f, 0.3f); fixedPoints.Add(g); }
                    }
                    for (int iteration = 0; iteration < Iterations; ++iteration)
                    {
                        var next = (Vector4[])field.Clone();
                        foreach (int g in region)
                        {
                            if (fixedPoints.Contains(g)) continue;
                            Vector4 sum = Vector4.zero; float total = 0;
                            foreach (Edge e in graph[g])
                            {
                                int n = Other(e, g); if (!region.Contains(n)) continue;
                                float conductance = 1 / (e.length + 0.005f); sum += field[n] * conductance; total += conductance;
                            }
                            if (total > 0) next[g] = sum / total;
                        }
                        field = next;
                    }
                    int[] bones = { Bone(cloak, side + "_LowerArm"), Bone(cloak, side + "_UpperArm"), Bone(cloak, side + "_Shoulder"), Bone(cloak, "Chest") };
                    foreach (int g in region)
                    {
                        // Keep a coherent, loose proximal skin reference. The solved arm field is
                        // used only as a Cloth freedom mask, never as a wrist pin in the mesh.
                        weights[g] = new List<Influence> { new Influence(bones[3], 0.85f), new Influence(bones[2], 0.15f) };
                        strength[g] = Mathf.Clamp01(field[g].x + 0.45f * field[g].y);
                    }
                    bool[] transitionAllowed = points.Select(p => p.y >= 0.18f && p.y <= 0.95f && sign * (p.x - centerX) > 0.005f).ToArray();
                    float[] outsideDistance = Distance(region, graph, transitionAllowed, OuterTransition);
                    int transition = 0;
                    for (int g = 0; g < groups.Count; ++g)
                    {
                        if (region.Contains(g) || outsideDistance[g] >= OuterTransition) continue;
                        float alpha = 1 - Smooth(outsideDistance[g] / OuterTransition);
                        var blended = new Dictionary<int, float>();
                        foreach (Influence w in old[g]) blended[w.bone] = w.weight * (1 - alpha);
                        blended.TryGetValue(bones[3], out float previous); blended[bones[3]] = previous + alpha * 0.85f;
                        blended.TryGetValue(bones[2], out previous); blended[bones[2]] = previous + alpha * 0.15f;
                        weights[g] = blended.Select(p => new Influence(p.Key, p.Value)).Where(w => w.weight > 0.00000001f).ToList();
                        transition++;
                    }
                    if (side == "Right") { report.rightRegion = region.Count; report.rightTransition = transition; }
                    else { report.leftRegion = region.Count; report.leftTransition = transition; }
                }
                var counts = new NativeArray<byte>(vertices.Length, Allocator.Temp);
                var allWeights = new List<BoneWeight1>();
                try
                {
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        int g = groupOf[i]; var ws = weights[g].OrderByDescending(w => w.weight).ToArray();
                        float sum = ws.Sum(w => w.weight); if (sum <= 0) throw new InvalidOperationException("Empty candidate weights.");
                        counts[i] = (byte)ws.Length;
                        foreach (Influence w in ws) allWeights.Add(new BoneWeight1 { boneIndex = w.bone, weight = w.weight / sum });
                        report.sleeveStrength[i] = strength[g];
                        report.maximumInfluences = Math.Max(report.maximumInfluences, ws.Length);
                        if (!Same(old[g], weights[g])) report.changedVertices++;
                    }
                    using (var nativeWeights = new NativeArray<BoneWeight1>(allWeights.ToArray(), Allocator.Temp)) candidate.SetBoneWeights(counts, nativeWeights);
                }
                finally { counts.Dispose(); }
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (saved == null) { AssetDatabase.CreateAsset(candidate, MeshPath); saved = candidate; candidate = null; }
                else { EditorUtility.CopySerialized(candidate, saved); EditorUtility.SetDirty(saved); }
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
                AssetDatabase.ImportAsset(ReportPath); AssetDatabase.SaveAssets();
                Debug.Log("GuaiwuDrapeMeshAuthoring saved " + MeshPath + "; R=" + report.rightRegion + "; L=" + report.leftRegion + "; max influences=" + report.maximumInfluences);
                return saved;
            }
            finally
            {
                try
                {
                    if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
                    if (model != null) UnityEngine.Object.DestroyImmediate(model);
                }
                finally { EditorSceneManager.ClosePreviewScene(previewScene); }
            }
        }

        static int Bone(SkinnedMeshRenderer r, string name)
        { int i = Array.FindIndex(r.bones, b => b.name == name); if (i < 0) throw new InvalidOperationException("Missing " + name); return i; }
        static int Other(Edge e, int g) => e.a == g ? e.b : e.a;
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
        static List<Influence> Read(BoneWeight w)
        {
            return new[] { new Influence(w.boneIndex0, w.weight0), new Influence(w.boneIndex1, w.weight1),
                new Influence(w.boneIndex2, w.weight2), new Influence(w.boneIndex3, w.weight3) }.Where(p => p.weight > 0).ToList();
        }
        static bool Same(List<Influence> a, List<Influence> b) => a.Count == b.Count
            && a.All(w => b.Any(v => v.bone == w.bone && Mathf.Abs(v.weight - w.weight) < 0.000001f));
        static float[] Distance(IEnumerable<int> starts, List<Edge>[] graph, bool[] allowed, float maximum)
        {
            float[] d = Enumerable.Repeat(float.PositiveInfinity, graph.Length).ToArray();
            var queue = new SortedSet<Tuple<float, int>>();
            foreach (int g in starts) { d[g] = 0; queue.Add(Tuple.Create(0f, g)); }
            while (queue.Count > 0)
            {
                var item = queue.Min; queue.Remove(item); float v = item.Item1; int g = item.Item2;
                if (v != d[g]) continue;
                foreach (Edge e in graph[g])
                {
                    int n = Other(e, g); float next = v + e.length;
                    if (!allowed[n] || next >= d[n] || next >= maximum) continue;
                    d[n] = next; queue.Add(Tuple.Create(next, n));
                }
            }
            return d;
        }
    }
}
