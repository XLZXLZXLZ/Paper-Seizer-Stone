using UnityEngine;
using UnityEngine.UI;

namespace PaperSeizerStone.Presentation
{
    // Simple manual diagrams stay editable UI geometry rather than baked image labels.
    public sealed class TutorialDiagramGraphic : MaskableGraphic
    {
        public enum Shape { Ring = 0, Disc = 1, Cartridge = 2, ChipStack = 3 }

        [SerializeField] private Shape shape;
        [SerializeField, Min(0.5f)] private float lineWidth = 2;
        [SerializeField, Range(1, 8)] private int chipCount = 8;

        private const int EllipseSegments = 48;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float inset = Mathf.Max(0.5f, lineWidth * 0.5f);
            rect.xMin += inset;
            rect.xMax -= inset;
            rect.yMin += inset;
            rect.yMax -= inset;
            if (rect.width <= 0 || rect.height <= 0) return;

            switch (shape)
            {
                case Shape.Ring:
                    DrawEllipse(mesh, rect.center, rect.width * 0.5f, rect.height * 0.5f, lineWidth);
                    break;
                case Shape.Disc:
                    DrawDisc(mesh, rect.center, rect.width * 0.5f, rect.height * 0.5f);
                    break;
                case Shape.Cartridge:
                    DrawCartridge(mesh, rect);
                    break;
                case Shape.ChipStack:
                    DrawChipStack(mesh, rect);
                    break;
            }
        }

        private void DrawEllipse(VertexHelper mesh, Vector2 center, float radiusX, float radiusY, float width)
        {
            if (radiusX <= 0 || radiusY <= 0) return;
            float innerX = Mathf.Max(0, radiusX - width);
            float innerY = Mathf.Max(0, radiusY - width);
            int start = mesh.currentVertCount;
            for (int i = 0; i < EllipseSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / EllipseSegments;
                Vector2 unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                AddVertex(mesh, center + Vector2.Scale(unit, new Vector2(radiusX, radiusY)));
                AddVertex(mesh, center + Vector2.Scale(unit, new Vector2(innerX, innerY)));
            }
            for (int i = 0; i < EllipseSegments; i++)
            {
                int a = start + i * 2;
                int b = start + ((i + 1) % EllipseSegments) * 2;
                mesh.AddTriangle(a, b, a + 1);
                mesh.AddTriangle(a + 1, b, b + 1);
            }
        }

        private void DrawDisc(VertexHelper mesh, Vector2 center, float radiusX, float radiusY)
        {
            int start = mesh.currentVertCount;
            AddVertex(mesh, center);
            for (int i = 0; i < EllipseSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / EllipseSegments;
                AddVertex(mesh, center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY));
            }
            for (int i = 0; i < EllipseSegments; i++)
                mesh.AddTriangle(start, start + i + 1, start + (i + 1) % EllipseSegments + 1);
        }

        private void DrawCartridge(VertexHelper mesh, Rect rect)
        {
            float centerX = rect.center.x;
            float baseY = rect.yMin;
            float flangeTop = baseY + rect.height * 0.08f;
            float shoulderY = baseY + rect.height * 0.63f;
            float caseRadius = rect.width * 0.38f;
            float tipRadius = rect.width * 0.31f;
            float flangeRadius = rect.width * 0.47f;

            DrawArc(mesh, new Vector2(centerX, shoulderY), tipRadius, rect.yMax - shoulderY, 0, Mathf.PI, lineWidth);
            Stroke(mesh, new[]
            {
                new Vector2(centerX - tipRadius, shoulderY),
                new Vector2(centerX - caseRadius, shoulderY),
                new Vector2(centerX - caseRadius, flangeTop),
                new Vector2(centerX - flangeRadius, flangeTop),
                new Vector2(centerX - flangeRadius, baseY),
                new Vector2(centerX + flangeRadius, baseY),
                new Vector2(centerX + flangeRadius, flangeTop),
                new Vector2(centerX + caseRadius, flangeTop),
                new Vector2(centerX + caseRadius, shoulderY),
                new Vector2(centerX + tipRadius, shoulderY)
            }, lineWidth);
            Line(mesh, new Vector2(centerX - caseRadius, shoulderY), new Vector2(centerX + caseRadius, shoulderY), lineWidth);
            Line(mesh, new Vector2(centerX - caseRadius, flangeTop), new Vector2(centerX + caseRadius, flangeTop), lineWidth);
            // A short engraved highlight gives the casing a restrained printed appearance.
            Line(mesh, new Vector2(centerX - caseRadius * 0.52f, flangeTop + rect.height * 0.1f),
                new Vector2(centerX - caseRadius * 0.52f, shoulderY - rect.height * 0.11f), lineWidth * 0.65f);
        }

        private void DrawChipStack(VertexHelper mesh, Rect rect)
        {
            int count = Mathf.Clamp(chipCount, 1, 8);
            float radiusX = rect.width * 0.5f;
            float radiusY = Mathf.Min(rect.width * 0.17f, rect.height * 0.18f);
            // Use eight-chip capacity so 8, 7 and 6 chip stacks share a baseline and decrease in height.
            float step = (rect.height - radiusY * 2) / 8;
            Vector2 bottom = new Vector2(rect.center.x, rect.yMin + radiusY);
            DrawArc(mesh, bottom, radiusX, radiusY, Mathf.PI, Mathf.PI * 2, lineWidth);
            for (int i = 0; i < count; i++)
            {
                float lowerY = bottom.y + i * step;
                float upperY = lowerY + step;
                Line(mesh, new Vector2(bottom.x - radiusX, lowerY), new Vector2(bottom.x - radiusX, upperY), lineWidth);
                Line(mesh, new Vector2(bottom.x + radiusX, lowerY), new Vector2(bottom.x + radiusX, upperY), lineWidth);
                DrawArc(mesh, new Vector2(bottom.x, upperY), radiusX, radiusY, Mathf.PI, Mathf.PI * 2, lineWidth);
                float sideX = bottom.x + radiusX * 0.63f;
                float frontOffset = radiusY * 0.77f;
                Line(mesh, new Vector2(sideX, lowerY - frontOffset + step * 0.25f),
                    new Vector2(sideX, upperY - frontOffset - step * 0.25f), lineWidth * 0.7f);
            }
            Vector2 top = bottom + Vector2.up * (count * step);
            DrawArc(mesh, top, radiusX, radiusY, 0, Mathf.PI, lineWidth);
            DrawEllipse(mesh, top, radiusX * 0.73f, radiusY * 0.67f, lineWidth * 0.7f);
        }

        private void DrawArc(VertexHelper mesh, Vector2 center, float radiusX, float radiusY,
            float startAngle, float endAngle, float width)
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(endAngle - startAngle) / (Mathf.PI * 2) * EllipseSegments));
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(startAngle, endAngle, i / (float)segments);
                points[i] = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            }
            Stroke(mesh, points, width);
        }

        private void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width)
        {
            Stroke(mesh, new[] { from, to }, width);
        }

        private void Stroke(VertexHelper mesh, Vector2[] points, float width)
        {
            if (points.Length < 2) return;
            int start = mesh.currentVertCount;
            float halfWidth = Mathf.Max(0.25f, width * 0.5f);
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 before = i > 0 ? (points[i] - points[i - 1]).normalized : (points[1] - points[0]).normalized;
                Vector2 after = i < points.Length - 1 ? (points[i + 1] - points[i]).normalized : before;
                Vector2 normalBefore = new Vector2(-before.y, before.x);
                Vector2 normalAfter = new Vector2(-after.y, after.x);
                Vector2 normal = (normalBefore + normalAfter).normalized;
                if (normal.sqrMagnitude < 0.01f) normal = normalAfter;
                float scale = halfWidth / Mathf.Max(0.5f, Vector2.Dot(normal, normalAfter));
                Vector2 offset = normal * scale;
                AddVertex(mesh, points[i] - offset);
                AddVertex(mesh, points[i] + offset);
            }
            for (int i = 0; i < points.Length - 1; i++)
            {
                int a = start + i * 2;
                mesh.AddTriangle(a, a + 2, a + 1);
                mesh.AddTriangle(a + 1, a + 2, a + 3);
            }
        }

        private void AddVertex(VertexHelper mesh, Vector2 position)
        {
            mesh.AddVert(new Vector3(position.x, position.y, 0), color, Vector2.zero);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
