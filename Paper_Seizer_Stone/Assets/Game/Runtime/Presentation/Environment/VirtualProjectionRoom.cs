using System.Collections.Generic;
using UnityEngine;

namespace PaperSeizerStone.Presentation
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VirtualProjectionRoom : MonoBehaviour
    {
        [Header("投影参考")]
        [Tooltip("固定的参考机位，沿其本地 +Z 方向投射。移动或旋转此定位点可校准构图，不需要 Camera 组件。")]
        [SerializeField] private Transform projectionOrigin;
        [Tooltip("仅绑定房间模型。不要加入桌面、卡牌或对手的 Renderer。")]
        [SerializeField] private MeshRenderer[] surfaces = new MeshRenderer[0];
        [Tooltip("房间贴图，可使用原图或扩展图；配合原画面区域保持投影构图。")]
        [SerializeField] private Texture2D projectionTexture;
        [Tooltip("原画面在扩展贴图中的归一化区域（X、Y、宽、高，原点左下）。原图使用 (0,0,1,1)；长宽各扩为两倍并居中时使用 (0.25,0.25,0.5,0.5)。")]
        [SerializeField] private Rect sourceImageRect = new Rect(0f, 0f, 1f, 1f);
        [Tooltip("原画面参考机位的垂直视野角度。使用扩展贴图时仍保持原画面的视野，无需增大此值。")]
        [SerializeField, Range(1f, 179f)] private float verticalFieldOfView = 53f;

        [Header("图像表现")]
        [SerializeField, Min(0f)] private float brightness = 1f;
        [SerializeField] private Color tint = Color.white;
        [Tooltip("原图没有覆盖到的模型区域使用此颜色，不重复或拉长图像边缘。")]
        [SerializeField] private Color outsideColor = new Color(0.018f, 0.014f, 0.011f, 1f);
        [Tooltip("图像四边向填充颜色柔化的宽度，占图像宽高的比例。值越大暗边覆盖越广；超过 0.5 时也会影响画面中心。")]
        [SerializeField, Range(0f, 1f)] private float edgeFeather = 0.025f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int OutsideColorId = Shader.PropertyToID("_OutsideColor");
        private static readonly int EdgeFeatherId = Shader.PropertyToID("_EdgeFeather");
        private static readonly int RoomProjectionParamsId = Shader.PropertyToID("_RoomProjectionParams");
        private static readonly int RoomImageRectId = Shader.PropertyToID("_RoomImageRect");
        private static readonly int ProjectionWorldToLocalId = Shader.PropertyToID("_ProjectionWorldToLocal");

        private readonly Dictionary<MeshRenderer, MaterialPropertyBlock> originalProperties =
            new Dictionary<MeshRenderer, MaterialPropertyBlock>();
        private readonly List<MeshRenderer> detachedSurfaces = new List<MeshRenderer>();
        private MaterialPropertyBlock properties;

        private void OnEnable() { ApplyProjection(); }

        // ExecuteAlways also updates the projection after editing the reference Transform.
        private void LateUpdate() { ApplyProjection(); }

        private void OnValidate()
        {
            // OnValidate can run during import. Keep Renderer calls in OnEnable/LateUpdate.
            verticalFieldOfView = Mathf.Clamp(verticalFieldOfView, 1f, 179f);
            brightness = Mathf.Max(0f, brightness);
            edgeFeather = Mathf.Clamp(edgeFeather, 0f, 1f);
            sourceImageRect = ClampSourceImageRect(sourceImageRect);
        }

        private void ApplyProjection()
        {
            if (projectionOrigin == null || projectionTexture == null || surfaces == null)
            {
                RestoreProperties();
                return;
            }

            RestoreDetachedSurfaces();
            if (properties == null) properties = new MaterialPropertyBlock();

            Rect imageRect = ClampSourceImageRect(sourceImageRect);
            float aspect = projectionTexture.width * imageRect.width
                / (Mathf.Max(1, projectionTexture.height) * imageRect.height);
            float halfHeight = Mathf.Tan(Mathf.Clamp(verticalFieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            Vector4 projectionParams = new Vector4(0.5f / (halfHeight * aspect), 0.5f / halfHeight, 0.05f, 0f);
            Matrix4x4 worldToProjection = projectionOrigin.worldToLocalMatrix;

            foreach (MeshRenderer surface in surfaces)
            {
                if (surface == null) continue;
                if (!originalProperties.ContainsKey(surface))
                {
                    var original = new MaterialPropertyBlock();
                    surface.GetPropertyBlock(original);
                    originalProperties.Add(surface, original);
                }

                surface.GetPropertyBlock(properties);
                properties.SetTexture(BaseMapId, projectionTexture);
                properties.SetMatrix(ProjectionWorldToLocalId, worldToProjection);
                properties.SetVector(RoomProjectionParamsId, projectionParams);
                properties.SetVector(RoomImageRectId, new Vector4(imageRect.x, imageRect.y, imageRect.width, imageRect.height));
                properties.SetFloat(BrightnessId, Mathf.Max(0f, brightness));
                properties.SetColor(TintId, tint);
                properties.SetColor(OutsideColorId, outsideColor);
                properties.SetFloat(EdgeFeatherId, Mathf.Clamp(edgeFeather, 0f, 1f));
                surface.SetPropertyBlock(properties);
            }
        }

        private void RestoreDetachedSurfaces()
        {
            detachedSurfaces.Clear();
            foreach (var entry in originalProperties)
            {
                if (entry.Key != null && System.Array.IndexOf(surfaces, entry.Key) >= 0) continue;
                if (entry.Key != null) entry.Key.SetPropertyBlock(entry.Value);
                detachedSurfaces.Add(entry.Key);
            }
            foreach (MeshRenderer surface in detachedSurfaces) originalProperties.Remove(surface);
        }

        private void RestoreProperties()
        {
            foreach (var entry in originalProperties)
                if (entry.Key != null) entry.Key.SetPropertyBlock(entry.Value);
            originalProperties.Clear();
        }

        private void OnDisable() { RestoreProperties(); }

        private static Rect ClampSourceImageRect(Rect imageRect)
        {
            float width = Mathf.Clamp(imageRect.width, 0.001f, 1f);
            float height = Mathf.Clamp(imageRect.height, 0.001f, 1f);
            float x = Mathf.Clamp(imageRect.x, 0f, 1f - width);
            float y = Mathf.Clamp(imageRect.y, 0f, 1f - height);
            return new Rect(x, y, width, height);
        }

        private void OnDrawGizmosSelected()
        {
            if (projectionOrigin == null) return;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Rect imageRect = ClampSourceImageRect(sourceImageRect);
            float aspect = projectionTexture != null
                ? projectionTexture.width * imageRect.width / (Mathf.Max(1, projectionTexture.height) * imageRect.height)
                : 16f / 9f;
            Gizmos.matrix = projectionOrigin.localToWorldMatrix;
            Gizmos.color = new Color(0.85f, 0.66f, 0.3f, 1f);
            Gizmos.DrawWireSphere(Vector3.zero, 0.12f);
            Gizmos.DrawFrustum(Vector3.zero, Mathf.Clamp(verticalFieldOfView, 1f, 179f), 4f, 0.05f, aspect);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
