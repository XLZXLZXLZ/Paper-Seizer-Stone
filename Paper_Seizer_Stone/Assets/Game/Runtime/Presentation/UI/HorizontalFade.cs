using UnityEngine;
using UnityEngine.UI;

namespace PaperSeizerStone.Presentation
{
    // Stored on simple Image backgrounds; child text and pointer hit areas stay intact.
    [DisallowMultipleComponent, RequireComponent(typeof(Image))]
    public sealed class HorizontalFade : BaseMeshEffect
    {
        [SerializeField, Range(0.01f, 0.5f), Tooltip("左右渐隐区域各占底板宽度的比例。")]
        private float edgeWidth = 0.18f;
        private readonly UIVertex[] strip = new UIVertex[4];

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount != 4) return;
            UIVertex bottomLeft = default, topLeft = default, topRight = default, bottomRight = default;
            mesh.PopulateUIVertex(ref bottomLeft, 0);
            mesh.PopulateUIVertex(ref topLeft, 1);
            mesh.PopulateUIVertex(ref topRight, 2);
            mesh.PopulateUIVertex(ref bottomRight, 3);
            mesh.Clear();
            const int segments = 24;
            for (int i = 0; i < segments; i++)
            {
                float left = i / (float)segments;
                float right = (i + 1) / (float)segments;
                strip[0] = Sample(bottomLeft, bottomRight, left);
                strip[1] = Sample(topLeft, topRight, left);
                strip[2] = Sample(topLeft, topRight, right);
                strip[3] = Sample(bottomLeft, bottomRight, right);
                mesh.AddUIVertexQuad(strip);
            }
        }

        private UIVertex Sample(UIVertex from, UIVertex to, float t)
        {
            UIVertex vertex = from;
            vertex.position = Vector3.Lerp(from.position, to.position, t);
            vertex.uv0 = Vector4.Lerp(from.uv0, to.uv0, t);
            vertex.uv1 = Vector4.Lerp(from.uv1, to.uv1, t);
            vertex.uv2 = Vector4.Lerp(from.uv2, to.uv2, t);
            vertex.uv3 = Vector4.Lerp(from.uv3, to.uv3, t);
            Color32 color = Color32.Lerp(from.color, to.color, t);
            float distance = Mathf.Min(t, 1 - t);
            color.a = (byte)Mathf.RoundToInt(color.a * Mathf.SmoothStep(0, 1,
                distance / Mathf.Max(0.01f, edgeWidth)));
            vertex.color = color;
            return vertex;
        }
    }
}
