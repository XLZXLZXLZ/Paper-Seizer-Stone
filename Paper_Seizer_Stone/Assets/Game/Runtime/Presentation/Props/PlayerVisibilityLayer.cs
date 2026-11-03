using System;
using UnityEngine;

namespace PaperSeizerStone.Presentation.Props
{
    // Keep the complete authored character alive; visibility belongs to each camera.
    [ExecuteAlways]
    public sealed class PlayerVisibilityLayer : MonoBehaviour
    {
        public const int Layer = 8;
        public const int HandsLayer = 9;
        [SerializeField, Tooltip("仅包含第一人称手臂外观的容器；骨骼与碰撞体保留在完整角色层。")] private Transform firstPersonArms;

        [SerializeField] private bool useFullArmMeshes;
        private Mesh leftArmMesh;
        private Mesh rightArmMesh;

        private void OnEnable()
        {
            AssignLayer();
            if (!Application.isPlaying || !useFullArmMeshes || firstPersonArms == null || leftArmMesh != null) return;

            SkinnedMeshRenderer body = Array.Find(GetComponentsInChildren<SkinnedMeshRenderer>(true),
                candidate => candidate.name == "Player_Body");
            if (body == null || body.sharedMesh == null || !body.sharedMesh.isReadable) return;

            leftArmMesh = SetFullArm(body, "Left");
            rightArmMesh = SetFullArm(body, "Right");
        }

        private Mesh SetFullArm(SkinnedMeshRenderer body, string side)
        {
            var target = firstPersonArms.Find("FirstPerson_" + side + "Arm")?.GetComponent<SkinnedMeshRenderer>();
            if (target == null) return null;
            Transform[] bones = body.bones;
            Transform upperArm = Array.Find(bones, bone => bone != null && bone.name == side + "_UpperArm");
            if (upperArm == null) return null;

            bool[] armBones = Array.ConvertAll(bones, bone => bone != null && (bone == upperArm || bone.IsChildOf(upperArm)));
            Mesh source = body.sharedMesh;
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != source.vertexCount) return null;

            bool[] keep = Array.ConvertAll(weights, weight =>
                (armBones[weight.boneIndex0] ? weight.weight0 : 0f)
                + (armBones[weight.boneIndex1] ? weight.weight1 : 0f)
                + (armBones[weight.boneIndex2] ? weight.weight2 : 0f)
                + (armBones[weight.boneIndex3] ? weight.weight3 : 0f) >= 0.5f);

            Mesh mesh = Instantiate(source);
            mesh.name = "FirstPerson_" + side + "_FullArm";
            int retained = 0;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                var triangles = new System.Collections.Generic.List<int>();
                int[] original = source.GetTriangles(sub);
                for (int i = 0; i < original.Length; i += 3)
                {
                    if (!keep[original[i]] || !keep[original[i + 1]] || !keep[original[i + 2]]) continue;
                    triangles.Add(original[i]);
                    triangles.Add(original[i + 1]);
                    triangles.Add(original[i + 2]);
                }
                retained += triangles.Count;
                mesh.SetTriangles(triangles, sub, false);
            }
            if (retained == 0)
            {
                Destroy(mesh);
                return null;
            }

            target.sharedMesh = mesh;
            target.bones = bones;
            target.rootBone = body.rootBone;
            target.localBounds = body.localBounds;
            return mesh;
        }

        private void OnDestroy()
        {
            if (leftArmMesh != null) Destroy(leftArmMesh);
            if (rightArmMesh != null) Destroy(rightArmMesh);
        }

        private void OnValidate() { AssignLayer(); }
        private void OnTransformChildrenChanged() { AssignLayer(); }

        private void AssignLayer()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                bool hands = firstPersonArms != null && (child == firstPersonArms || child.IsChildOf(firstPersonArms));
                int target = hands ? HandsLayer : Layer;
                // Preserve the final layer instead of flipping hands 9 -> 8 -> 9
                // on every prefab validation or child change.
                if (child.gameObject.layer != target) child.gameObject.layer = target;
            }
        }
    }
}
