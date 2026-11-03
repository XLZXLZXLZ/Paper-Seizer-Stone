using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaperSeizerStone.Editor
{
    // The caller owns loading and saving the prefab contents. This only authors
    // one renderer in that instance and its reusable mesh asset.
    public static class PlayerFirstPersonArmAuthoring
    {
        private const string MeshFolder = "Assets/Game/art/act/Player/Meshes";
        private const string MeshPath = MeshFolder + "/Player_RightForearm_Hand.asset";

        public static SkinnedMeshRenderer EnsureRightArm(GameObject playerPrefabRoot)
        {
            if (playerPrefabRoot == null || EditorUtility.IsPersistent(playerPrefabRoot))
                throw new ArgumentException("请传入已加载的玩家 PrefabContents 实例。", nameof(playerPrefabRoot));
            Transform model = playerPrefabRoot.transform.Find("Player_Rigged");
            if (model == null) throw new InvalidOperationException("玩家预制体缺少 Player_Rigged 模型根。");
            var body = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SingleOrDefault(candidate => candidate.name == "Player_Body");
            if (body == null || body.sharedMesh == null)
                throw new InvalidOperationException("玩家预制体缺少 Player_Body 蒙皮网格。");
            var lowerArm = body.bones.Where(bone => bone != null && bone.name == "Right_LowerArm").ToArray();
            var hand = body.bones.Where(bone => bone != null && bone.name == "Right_Hand").ToArray();
            if (lowerArm.Length != 1 || hand.Length != 1 || !hand[0].IsChildOf(lowerArm[0]))
                throw new InvalidOperationException("玩家模型需要唯一且层级完整的 Right_LowerArm / Right_Hand。");

            Mesh extracted = ExtractArm(body.sharedMesh, body.bones, lowerArm[0]);
            Mesh savedMesh;
            try
            {
                EnsureFolder(MeshFolder);
                savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (savedMesh == null)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(MeshPath) != null)
                        throw new InvalidOperationException("右手网格目标路径已被其它资产占用：" + MeshPath);
                    AssetDatabase.CreateAsset(extracted, MeshPath);
                    savedMesh = extracted;
                    extracted = null;
                }
                else
                {
                    // Keep the existing asset object and GUID on repeated authoring.
                    EditorUtility.CopySerialized(extracted, savedMesh);
                    EditorUtility.SetDirty(savedMesh);
                }
                AssetDatabase.SaveAssetIfDirty(savedMesh);
            }
            finally
            {
                if (extracted != null) UnityEngine.Object.DestroyImmediate(extracted);
            }

            Transform container = model.Find("FirstPersonArms");
            if (container == null)
            {
                container = new GameObject("FirstPersonArms").transform;
                container.SetParent(model, false);
                container.gameObject.layer = 9;
            }
            Transform arm = container.Find("FirstPerson_RightArm");
            if (arm == null)
            {
                arm = new GameObject("FirstPerson_RightArm").transform;
                arm.SetParent(container, false);
            }
            // Mesh vertices and bindposes stay in the original renderer space.
            Matrix4x4 local = container.worldToLocalMatrix * body.transform.localToWorldMatrix;
            arm.localPosition = local.GetColumn(3);
            arm.localRotation = local.rotation;
            arm.localScale = local.lossyScale;
            arm.gameObject.layer = 9;
            var renderer = arm.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null) renderer = arm.gameObject.AddComponent<SkinnedMeshRenderer>();
            var left = container.Find("FirstPerson_LeftArm")?.GetComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = savedMesh;
            renderer.bones = body.bones;
            renderer.rootBone = body.rootBone;
            renderer.sharedMaterials = body.sharedMaterials;
            renderer.quality = body.quality;
            renderer.updateWhenOffscreen = true;
            renderer.skinnedMotionVectors = left != null ? left.skinnedMotionVectors : body.skinnedMotionVectors;
            renderer.localBounds = savedMesh.bounds;
            renderer.shadowCastingMode = left != null ? left.shadowCastingMode : ShadowCastingMode.Off;
            renderer.receiveShadows = left != null ? left.receiveShadows : body.receiveShadows;
            renderer.motionVectorGenerationMode = left != null ? left.motionVectorGenerationMode : body.motionVectorGenerationMode;
            renderer.lightProbeUsage = left != null ? left.lightProbeUsage : body.lightProbeUsage;
            renderer.reflectionProbeUsage = left != null ? left.reflectionProbeUsage : body.reflectionProbeUsage;
            renderer.probeAnchor = body.probeAnchor;
            renderer.lightProbeProxyVolumeOverride = body.lightProbeProxyVolumeOverride;
            renderer.allowOcclusionWhenDynamic = left != null ? left.allowOcclusionWhenDynamic : body.allowOcclusionWhenDynamic;
            renderer.renderingLayerMask = left != null ? left.renderingLayerMask : body.renderingLayerMask;
            renderer.sortingLayerID = left != null ? left.sortingLayerID : body.sortingLayerID;
            renderer.sortingOrder = left != null ? left.sortingOrder : body.sortingOrder;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            arm.gameObject.SetActive(false);
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        private static Mesh ExtractArm(Mesh source, Transform[] bones, Transform lowerArm)
        {
            if (!source.isReadable) throw new InvalidOperationException("Player_Body 网格必须启用 Read/Write。");
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != source.vertexCount)
                throw new InvalidOperationException("Player_Body 缺少完整的顶点骨骼权重。");
            var includedBones = new HashSet<int>();
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null && (bones[b] == lowerArm || bones[b].IsChildOf(lowerArm))) includedBones.Add(b);
            var selected = new bool[source.vertexCount];
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight weight = weights[v];
                float armWeight = (includedBones.Contains(weight.boneIndex0) ? weight.weight0 : 0f)
                    + (includedBones.Contains(weight.boneIndex1) ? weight.weight1 : 0f)
                    + (includedBones.Contains(weight.boneIndex2) ? weight.weight2 : 0f)
                    + (includedBones.Contains(weight.boneIndex3) ? weight.weight3 : 0f);
                selected[v] = armWeight >= 0.5f;
            }
            var remap = Enumerable.Repeat(-1, source.vertexCount).ToArray();
            var retained = new List<int>();
            var triangles = new List<int>[source.subMeshCount];
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                if (source.GetTopology(sub) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Player_Body 的子网格必须使用三角形拓扑。");
                int[] original = source.GetTriangles(sub);
                triangles[sub] = new List<int>();
                for (int t = 0; t < original.Length; t += 3)
                {
                    if (!selected[original[t]] || !selected[original[t + 1]] || !selected[original[t + 2]]) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int index = original[t + corner];
                        if (remap[index] < 0)
                        {
                            remap[index] = retained.Count;
                            retained.Add(index);
                        }
                        triangles[sub].Add(remap[index]);
                    }
                }
            }
            if (retained.Count == 0) throw new InvalidOperationException("未能从 Player_Body 提取右前臂和手部三角形。");
            var mesh = new Mesh { name = "Player_RightForearm_Hand", indexFormat = retained.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.vertices = Remap(source.vertices, retained);
                if (source.normals.Length == source.vertexCount) mesh.normals = Remap(source.normals, retained);
                if (source.tangents.Length == source.vertexCount) mesh.tangents = Remap(source.tangents, retained);
                if (source.colors.Length == source.vertexCount) mesh.colors = Remap(source.colors, retained);
                for (int channel = 0; channel < 8; channel++) CopyUV(source, mesh, channel, retained);
                mesh.boneWeights = Remap(weights, retained);
                mesh.bindposes = source.bindposes;
                mesh.subMeshCount = source.subMeshCount;
                for (int sub = 0; sub < triangles.Length; sub++) mesh.SetTriangles(triangles[sub], sub, false);
                CopyBlendShapes(source, mesh, retained);
                mesh.RecalculateBounds();
                return mesh;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        private static T[] Remap<T>(T[] source, List<int> retained)
        {
            var result = new T[retained.Count];
            for (int i = 0; i < result.Length; i++) result[i] = source[retained[i]];
            return result;
        }

        private static void CopyUV(Mesh source, Mesh destination, int channel, List<int> retained)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
            if (!source.HasVertexAttribute(attribute)) return;
            int dimension = source.GetVertexAttributeDimension(attribute);
            if (dimension == 2)
            {
                var uv = new List<Vector2>();
                source.GetUVs(channel, uv);
                destination.SetUVs(channel, retained.Select(index => uv[index]).ToList());
            }
            else if (dimension == 3)
            {
                var uv = new List<Vector3>();
                source.GetUVs(channel, uv);
                destination.SetUVs(channel, retained.Select(index => uv[index]).ToList());
            }
            else
            {
                var uv = new List<Vector4>();
                source.GetUVs(channel, uv);
                destination.SetUVs(channel, retained.Select(index => uv[index]).ToList());
            }
        }

        private static void CopyBlendShapes(Mesh source, Mesh destination, List<int> retained)
        {
            if (source.blendShapeCount == 0) return;
            var positions = new Vector3[source.vertexCount];
            var normals = new Vector3[source.vertexCount];
            var tangents = new Vector3[source.vertexCount];
            for (int shape = 0; shape < source.blendShapeCount; shape++)
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); frame++)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, positions, normals, tangents);
                    destination.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame),
                        Remap(positions, retained), Remap(normals, retained), Remap(tangents, retained));
                }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
