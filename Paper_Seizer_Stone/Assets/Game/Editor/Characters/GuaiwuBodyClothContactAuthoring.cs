using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PaperSeizerStone.Editor
{
    // Explicit Editor authoring. Pass JSON text loaded by the caller, normally
    // from TextAssets. No paths, menus, startup hooks, runtime work, or saving.
    // Use LoadPrefabContents; save only after Apply succeeds, otherwise unload
    // without saving. Existing mesh, particle order and virtual data are retained.
    public static class GuaiwuBodyClothContactAuthoring
    {
        const float FlatReleaseRadius = 0.015f;
        const float CollarCenterX = -0.005f;
        const float CollarHalfWidth = 0.025f;
        const float CollarMinimumY = 0.77f;

        [Serializable]
        public sealed class Result
        {
            public string fitDocumentSha256, releaseDocumentSha256;
            public ReleaseResult panels;
            public GuaiwuBodyArmCollisionAuthoring.Result contacts;
            public float savedVirtualParticleSetting;
            public int savedVirtualParticleCount;
        }

        [Serializable]
        public sealed class ReleaseResult
        {
            public int seedVertices, renderVertices, particles;
            public int affectedParticles, changedParticles, protectedAnchorGroups;
            public float maxDistance, expansion;
            public bool mergedWithExistingCoefficients = true;
        }

        [Serializable]
        sealed class ReleaseDocument
        {
            // Older Lab seed JSON has no schemaVersion; zero denotes that schema.
            public int schemaVersion;
            public int renderVertexCount;
            public int particleCount;
            public string sourceMeshGuid;
            public int[] allRenderVertexIds;
        }

        sealed class ReleasePlan
        {
            public Cloth Cloth;
            public ClothSkinningCoefficient[] Coefficients;
            public ReleaseResult Report;
        }

        // Same JSON and parameters are idempotent. This intentionally merges
        // release with existing freedom (including the hem): it never tightens
        // old coefficients. For a narrower mask or lower distance, reapply to
        // the saved pre-contact baseline, rather than the previously released
        // prefab. Caller-owned baseline/backup avoids hidden generated assets.
        public static Result Apply(GameObject root, string fitJson, string releaseJson,
            float releaseDistance = 0.12f, float releaseExpansion = 0.09f)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (string.IsNullOrWhiteSpace(fitJson))
                throw new ArgumentException("Arm fit JSON is empty.", nameof(fitJson));
            if (string.IsNullOrWhiteSpace(releaseJson))
                throw new ArgumentException("Panel release JSON is empty.", nameof(releaseJson));

            // Build and validate the complete release before changing contacts.
            ReleasePlan release = PrepareRelease(root, releaseJson, releaseDistance, releaseExpansion);
            var savedVirtualIndices = new List<uint>();
            release.Cloth.GetVirtualParticleIndices(savedVirtualIndices);
            float savedVirtualSetting = release.Cloth.useVirtualParticles;
            var contacts = GuaiwuBodyArmCollisionAuthoring.Apply(root, fitJson);

            // Only maxDistance changes; collisionSphereDistance and every other
            // Cloth setting remain as saved. No mesh assignment or recooking is
            // performed, so existing particle/virtual-particle indices stay valid.
            release.Cloth.coefficients = release.Coefficients;
            if (PrefabUtility.IsPartOfPrefabInstance(release.Cloth))
                PrefabUtility.RecordPrefabInstancePropertyModifications(release.Cloth);
            EditorUtility.SetDirty(release.Cloth);
            return new Result
            {
                fitDocumentSha256 = Hash(fitJson), releaseDocumentSha256 = Hash(releaseJson),
                panels = release.Report, contacts = contacts,
                savedVirtualParticleSetting = savedVirtualSetting,
                savedVirtualParticleCount = savedVirtualIndices.Count / 4
            };
        }

        static ReleasePlan PrepareRelease(GameObject root, string json, float maximum, float expansion)
        {
            if (!Finite(maximum) || maximum < 0f || !Finite(expansion) || expansion <= FlatReleaseRadius)
                throw new ArgumentOutOfRangeException(nameof(maximum),
                    "Release distance must be finite and nonnegative; expansion must exceed .015 source metres.");
            ReleaseDocument seeds = JsonUtility.FromJson<ReleaseDocument>(json);
            if (seeds == null || (seeds.schemaVersion != 0 && seeds.schemaVersion != 1)
                || seeds.allRenderVertexIds == null || seeds.allRenderVertexIds.Length == 0)
                throw new InvalidOperationException("Panel JSON requires schema 0/1 and nonempty allRenderVertexIds.");

            SkinnedMeshRenderer renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Single(r => r.name == "Guaiwu_Cloak");
            Mesh mesh = renderer.sharedMesh;
            Cloth cloth = renderer.GetComponent<Cloth>();
            if (mesh == null || !mesh.isReadable || cloth == null)
                throw new InvalidOperationException("Guaiwu_Cloak requires its readable skinned mesh and Cloth.");
            Vector3[] vertices = mesh.vertices;
            if (seeds.renderVertexCount != vertices.Length)
                throw new InvalidOperationException("Panel seed renderVertexCount differs from the current Cloak mesh.");
            if (!string.IsNullOrEmpty(seeds.sourceMeshGuid))
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh));
                if (!string.Equals(guid, seeds.sourceMeshGuid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Panel seed mesh GUID differs from the current Cloak mesh.");
            }
            int[] seedVertices = seeds.allRenderVertexIds.Distinct().ToArray();
            if (seedVertices.Any(v => v < 0 || v >= vertices.Length))
                throw new InvalidOperationException("Panel seed vertex ID is outside the current Cloak mesh.");

            GuaiwuSleeveClothAuthoring.Snapshot snapshot = GuaiwuSleeveClothAuthoring.Capture(renderer, cloth);
            if (snapshot.Coefficients == null || snapshot.ParticleOfVertex == null
                || snapshot.ParticleOfVertex.Length != vertices.Length
                || snapshot.Coefficients.Length == 0
                || snapshot.ParticleOfVertex.Any(p => p < 0 || p >= snapshot.Coefficients.Length)
                || (seeds.particleCount > 0 && seeds.particleCount != snapshot.Coefficients.Length))
                throw new InvalidOperationException("Panel seed, Cloth particle map and saved coefficients do not match.");

            // Weld UV/material splits in immutable rest coordinates. Actor world
            // scale cancels here; distances stay in source actor-local metres.
            var points = new List<Vector3>();
            var groups = new Dictionary<Vector3Int, int>();
            var groupOf = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = root.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i]));
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z))
                    throw new InvalidOperationException("Cloak rest vertex is non-finite.");
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000000f),
                    Mathf.RoundToInt(p.y * 1000000f), Mathf.RoundToInt(p.z * 1000000f));
                if (!groups.TryGetValue(key, out int g))
                {
                    g = points.Count;
                    groups.Add(key, g);
                    points.Add(p);
                }
                groupOf[i] = g;
            }
            var graph = Enumerable.Range(0, points.Count).Select(_ => new HashSet<int>()).ToArray();
            int[] triangles = mesh.triangles;
            if (triangles.Length % 3 != 0 || triangles.Any(v => v < 0 || v >= vertices.Length))
                throw new InvalidOperationException("Cloak triangle indices are invalid.");
            for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    int a = groupOf[triangles[i + j]], b = groupOf[triangles[i + (j + 1) % 3]];
                    if (a != b) { graph[a].Add(b); graph[b].Add(a); }
                }

            // V2 support mask: central collar/crown stay as saved; lateral panels
            // under raised arms may move. This is rest-space, never world-space.
            bool[] anchor = points.Select(p => Mathf.Abs(p.x - CollarCenterX) < CollarHalfWidth
                && p.y > CollarMinimumY).ToArray();
            float[] distance = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray();
            var queue = new SortedSet<Tuple<float, int>>();
            foreach (int v in seedVertices)
            {
                int g = groupOf[v];
                if (anchor[g] || distance[g] == 0f) continue;
                distance[g] = 0f;
                queue.Add(Tuple.Create(0f, g));
            }
            while (queue.Count > 0)
            {
                Tuple<float, int> next = queue.Min;
                queue.Remove(next);
                if (next.Item1 != distance[next.Item2]) continue;
                foreach (int n in graph[next.Item2])
                {
                    if (anchor[n]) continue;
                    float d = next.Item1 + Vector3.Distance(points[n], points[next.Item2]);
                    if (d >= expansion || d >= distance[n]) continue;
                    distance[n] = d;
                    queue.Add(Tuple.Create(d, n));
                }
            }
            var amount = new float[snapshot.Coefficients.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                float d = distance[groupOf[v]];
                float strength = float.IsInfinity(d) ? 0f
                    : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatReleaseRadius, expansion, d));
                int particle = snapshot.ParticleOfVertex[v];
                amount[particle] = Mathf.Max(amount[particle], strength);
            }
            var coefficients = (ClothSkinningCoefficient[])snapshot.Coefficients.Clone();
            int changed = 0;
            for (int p = 0; p < coefficients.Length; p++)
            {
                float old = coefficients[p].maxDistance;
                if (float.IsNaN(old) || old < 0f)
                    throw new InvalidOperationException("Saved Cloth maxDistance is invalid.");
                float value = Mathf.Max(old, maximum * amount[p]);
                if (value != old) changed++;
                coefficients[p].maxDistance = value;
            }
            return new ReleasePlan
            {
                Cloth = cloth, Coefficients = coefficients,
                Report = new ReleaseResult
                {
                    seedVertices = seedVertices.Length, renderVertices = vertices.Length,
                    particles = coefficients.Length, affectedParticles = amount.Count(v => v > 0f),
                    changedParticles = changed, protectedAnchorGroups = anchor.Count(v => v),
                    maxDistance = maximum, expansion = expansion
                }
            };
        }

        static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
