using System.Collections.Generic;
using UnityEngine;

namespace PaperSeizerStone.Presentation.MainMenu
{
    // Projects the saved disc meshes onto the illustrated tabletop; it never changes the menu layout.
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MenuContactShadowView : MonoBehaviour
    {
        [SerializeField] private Transform disc;
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private MeshRenderer shadowRenderer;
        [SerializeField] private Vector2 footprintScale = new Vector2(1.14f, 1.12f);
        [SerializeField] private Vector2 shadowOffset = new Vector2(0.07f, -0.075f);
        [SerializeField, Range(0, 1)] private float opacity = 0.38f;
        [SerializeField, Range(0.05f, 0.7f)] private float softness = 0.28f;
        [SerializeField, Range(0, 1)] private float contactStrength = 0.45f;
        [SerializeField, Min(0.0001f)] private float surfaceOffset = 0.003f;
        private readonly List<Footprint> sources = new List<Footprint>();
        private sealed class Footprint
        {
            public MeshRenderer Renderer;
            public Vector3[] Vertices;
            public Matrix4x4 Matrix;
            public bool Visible;
        }
        private Matrix4x4 receiverMatrix;
        private Vector3 projectionDirection;
        private bool dirty = true;
        private bool backgroundVisible;
        private MaterialPropertyBlock properties;

        private void OnEnable() { sources.Clear(); dirty = true; }
        private void OnValidate() { sources.Clear(); outlines = null; dirty = true; }
        private void OnDisable() { if (shadowRenderer != null) shadowRenderer.enabled = false; }

        private void LateUpdate()
        {
            if (disc == null || background == null || viewCamera == null || shadowRenderer == null)
            {
                if (shadowRenderer != null) shadowRenderer.enabled = false;
                return;
            }
            if (sources.Count == 0)
            {
                foreach (MeshRenderer renderer in disc.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.GetComponentInParent<MenuSector>() == null) continue;
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    // Cache a compact silhouette generated with the imported mesh, not a full per-frame mesh read.
                    Vector3[] points = OutlineFor(renderer);
                    if (points != null) sources.Add(new Footprint { Renderer = renderer, Vertices = points });
                }
                dirty = true;
            }
            Matrix4x4 receiver = background.transform.worldToLocalMatrix;
            Vector3 direction = receiver.MultiplyVector(viewCamera.transform.forward);
            if (Mathf.Abs(direction.z) < 0.0001f) return;
            dirty |= receiver != receiverMatrix || direction != projectionDirection;
            bool receiverVisible = background.enabled && background.gameObject.activeInHierarchy;
            dirty |= receiverVisible != backgroundVisible;
            backgroundVisible = receiverVisible;
            foreach (Footprint source in sources)
            {
                if (source.Renderer == null) continue;
                Matrix4x4 matrix = receiver * source.Renderer.transform.localToWorldMatrix;
                dirty |= matrix != source.Matrix;
                bool visible = source.Renderer.enabled && source.Renderer.gameObject.activeInHierarchy;
                dirty |= visible != source.Visible;
                source.Visible = visible;
                source.Matrix = matrix;
            }
            if (dirty)
            {
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (Footprint source in sources)
                {
                    if (source.Renderer == null || !source.Visible) continue;
                    foreach (Vector3 vertex in source.Vertices)
                    {
                        Vector3 point = source.Matrix.MultiplyPoint3x4(vertex);
                        point -= direction * (point.z / direction.z);
                        min = Vector2.Min(min, point);
                        max = Vector2.Max(max, point);
                    }
                }
                bool visible = background.enabled && background.gameObject.activeInHierarchy && !float.IsInfinity(min.x);
                shadowRenderer.enabled = visible;
                if (visible)
                {
                    Vector2 center = (min + max) * 0.5f;
                    Vector2 size = max - min;
                    float side = Mathf.Sign(Vector3.Dot(viewCamera.transform.position - background.transform.position, background.transform.forward));
                    transform.SetPositionAndRotation(background.transform.TransformPoint(new Vector3(center.x, center.y, surfaceOffset * side)), background.transform.rotation);
                    Vector3 scale = background.transform.lossyScale;
                    transform.localScale = new Vector3(Mathf.Max(0.01f, size.x * Mathf.Abs(scale.x) * footprintScale.x),
                        Mathf.Max(0.01f, size.y * Mathf.Abs(scale.y) * footprintScale.y), 1f);
                }
                receiverMatrix = receiver;
                projectionDirection = direction;
                dirty = false;
            }
            if (properties == null) properties = new MaterialPropertyBlock();
            properties.SetFloat("_Opacity", opacity);
            properties.SetFloat("_Softness", softness);
            properties.SetFloat("_ContactStrength", contactStrength);
            properties.SetVector("_CastOffset", new Vector4(shadowOffset.x, shadowOffset.y, 0, 0));
            shadowRenderer.SetPropertyBlock(properties);
        }

        [SerializeField] private TextAsset silhouetteData;
        [System.Serializable] private sealed class Outline { public string meshName; public Vector3[] points; }
        [System.Serializable] private sealed class Outlines { public Outline[] meshes; }
        private Outlines outlines;
        private Vector3[] OutlineFor(MeshRenderer renderer)
        {
            if (outlines == null && silhouetteData != null) outlines = JsonUtility.FromJson<Outlines>(silhouetteData.text);
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (outlines != null && outlines.meshes != null)
                foreach (Outline outline in outlines.meshes)
                    if (outline.meshName == filter.sharedMesh.name) return outline.points;
            // Mesh.bounds remains accessible when Read/Write is disabled.
            Bounds bounds = filter.sharedMesh.bounds;
            var points = new Vector3[8];
            for (int i = 0; i < 8; i++) points[i] = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            return points;
        }
    }
}
