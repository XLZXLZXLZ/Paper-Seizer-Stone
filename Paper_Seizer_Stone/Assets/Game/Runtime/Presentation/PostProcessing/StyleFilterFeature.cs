using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaperSeizerStone.Presentation.PostProcessing
{
    public sealed class StyleFilterFeature : ScriptableRendererFeature
    {
        public StyleFilterSettings settings;
        public Shader filterShader;
        private Material material;
        private FilterPass pass;
        private readonly StyleFilterParameters neutralStyle = new StyleFilterParameters { strength = 0 };

        public override void Create()
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            if (filterShader != null) material = CoreUtils.CreateEngineMaterial(filterShader);
            pass = new FilterPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            pass.ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null) return;
            var camera = renderingData.cameraData;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if (camera.renderType != CameraRenderType.Base) return;
            bool styled = settings != null && settings.filterEnabled && settings.parameters != null;
#if UNITY_EDITOR
            styled &= StyleFilterSettings.PreviewEnabled && !StyleFilterSettings.CompareOriginal
                && (!camera.isSceneViewCamera || StyleFilterSettings.PreviewSceneView);
#endif
            var rig = camera.cameraType == CameraType.Game ? camera.camera.GetComponentInParent<BattleCameraRig>() : null;
            Vector4 impact = rig != null && rig.Camera == camera.camera ? rig.HitScreenEffect : Vector4.zero;
            float openingBlur = rig != null && rig.Camera == camera.camera && rig.Opening != null
                ? rig.Opening.BlurPixels * renderingData.cameraData.cameraTargetDescriptor.height / 1080f : 0f;
            var parameters = styled ? settings.parameters : neutralStyle;
            if (parameters.strength <= 0 && impact.x <= 0f && impact.w <= 0f && openingBlur <= 0.01f)
            {
                pass.ReleaseOpeningBlur();
                return;
            }
            pass.Setup(renderer, material, parameters, impact, openingBlur);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        private sealed class FilterPass : ScriptableRenderPass
        {
            private readonly int temporary = Shader.PropertyToID("_StyleFilterCopy");
            private readonly int blurHorizontal = Shader.PropertyToID("_OpeningBlurHorizontal");
            private readonly int blurVertical = Shader.PropertyToID("_OpeningBlurVertical");
            private readonly int openingBlurTexture = Shader.PropertyToID("_OpeningBlurTexture");
#if UNITY_2022_2_OR_NEWER
            private RTHandle copy;
            private RTHandle blurA;
            private RTHandle blurB;
#endif
            private RenderTextureDescriptor descriptor;
            private RenderTextureDescriptor blurDescriptor;
            private ScriptableRenderer renderer;
            private Material material;
            private StyleFilterParameters parameters;
            private Vector4 impact;
            private float blurPixels;
            private bool HasOpeningBlur => blurPixels > 0.01f;

            public void Setup(ScriptableRenderer target, Material effect, StyleFilterParameters values, Vector4 screenImpact, float openingBlur)
            { renderer = target; material = effect; parameters = values; impact = screenImpact; blurPixels = openingBlur; }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                blurDescriptor = descriptor;
                blurDescriptor.width = Mathf.Max(1, (descriptor.width + 1) / 2);
                blurDescriptor.height = Mathf.Max(1, (descriptor.height + 1) / 2);
#if UNITY_2022_2_OR_NEWER
                RenderingUtils.ReAllocateIfNeeded(ref copy, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_StyleFilterCopy");
                if (HasOpeningBlur)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref blurA, blurDescriptor, FilterMode.Bilinear,
                        TextureWrapMode.Clamp, name: "_OpeningBlurHorizontal");
                    RenderingUtils.ReAllocateIfNeeded(ref blurB, blurDescriptor, FilterMode.Bilinear,
                        TextureWrapMode.Clamp, name: "_OpeningBlurVertical");
                }
                else ReleaseOpeningBlur();
#else
                cmd.GetTemporaryRT(temporary, descriptor, FilterMode.Bilinear);
                if (HasOpeningBlur)
                {
                    cmd.GetTemporaryRT(blurHorizontal, blurDescriptor, FilterMode.Bilinear);
                    cmd.GetTemporaryRT(blurVertical, blurDescriptor, FilterMode.Bilinear);
                }
#endif
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var p = parameters;
                material.SetVector("_ShotImpact", impact);
                material.SetVector("_OpeningBlur", HasOpeningBlur
                    ? new Vector4(blurPixels / (3.230769f * descriptor.width),
                        blurPixels / (3.230769f * descriptor.height), Mathf.Clamp01(blurPixels / 2f), 0f)
                    : Vector4.zero);
                material.SetVector("_Style", new Vector4(p.strength, p.posterize, Mathf.Max(3, p.levels), p.dither));
                material.SetVector("_Print", new Vector4(p.halftone, p.dotSize, p.dotAngle * Mathf.Deg2Rad, p.ditherSize));
                material.SetVector("_Surface", new Vector4(p.oil, Mathf.Clamp(p.oilRadius, 1, 4), p.paper, p.paperScale));
                material.SetVector("_Grade", new Vector4(p.exposure, p.contrast / 100f, p.saturation / 100f, p.temperature / 100f));
                material.SetVector("_Vignette", new Vector4(p.vignette, p.vignetteSoftness, p.vignetteCenter.x, p.vignetteCenter.y));
                material.SetColor("_InkColor", p.inkColor);
                material.SetColor("_ShadowColor", p.shadows);
                material.SetColor("_HighlightColor", p.highlights);
                material.SetColor("_VignetteColor", p.vignetteColor);
                material.SetFloat("_SplitTone", p.splitTone);
                material.SetVector("_StyleSourceTexelSize", new Vector4(
                    1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));
                var cmd = CommandBufferPool.Get("全局风格滤镜");
#if UNITY_2022_2_OR_NEWER
                // URP's blitter binds _BlitTexture and supplies fullscreen geometry / RTHandle scaling.
                var source = renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, source, copy, material, 1);
                if (HasOpeningBlur)
                {
                    Blitter.BlitCameraTexture(cmd, copy, blurA, material, 2);
                    Blitter.BlitCameraTexture(cmd, blurA, blurB, material, 3);
                    cmd.SetGlobalTexture(openingBlurTexture, blurB.nameID);
                }
                Blitter.BlitCameraTexture(cmd, copy, source, material, 0);
#else
                RenderTargetIdentifier source = renderer.cameraColorTarget;
                // URP 12: explicitly bind the texture and draw the same procedural fullscreen triangle.
                // Avoid CommandBuffer.Blit's implicit _MainTex and legacy vertex conventions.
                cmd.SetGlobalVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                cmd.SetGlobalTexture("_BlitTexture", source);
                cmd.SetRenderTarget(temporary, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, descriptor.width, descriptor.height));
                cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1);
                if (HasOpeningBlur)
                {
                    cmd.SetGlobalTexture("_BlitTexture", temporary);
                    cmd.SetRenderTarget(blurHorizontal, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                    cmd.SetViewport(new Rect(0, 0, blurDescriptor.width, blurDescriptor.height));
                    cmd.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, 3, 1);
                    cmd.SetGlobalTexture("_BlitTexture", blurHorizontal);
                    cmd.SetRenderTarget(blurVertical, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                    cmd.DrawProcedural(Matrix4x4.identity, material, 3, MeshTopology.Triangles, 3, 1);
                    cmd.SetGlobalTexture(openingBlurTexture, blurVertical);
                }
                cmd.SetGlobalTexture("_BlitTexture", temporary);
                cmd.SetRenderTarget(source, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, descriptor.width, descriptor.height));
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
#endif
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
#if !UNITY_2022_2_OR_NEWER
                cmd.ReleaseTemporaryRT(temporary);
                if (HasOpeningBlur)
                {
                    cmd.ReleaseTemporaryRT(blurHorizontal);
                    cmd.ReleaseTemporaryRT(blurVertical);
                }
#endif
            }

            public void ReleaseOpeningBlur()
            {
#if UNITY_2022_2_OR_NEWER
                blurA?.Release();
                blurB?.Release();
                blurA = blurB = null;
#endif
            }

            public void Dispose()
            {
                ReleaseOpeningBlur();
#if UNITY_2022_2_OR_NEWER
                copy?.Release();
                copy = null;
#endif
            }
        }
    }
}
