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
            if (material == null || settings == null || !settings.filterEnabled || settings.parameters == null) return;
            var camera = renderingData.cameraData;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if (camera.renderType != CameraRenderType.Base) return;
#if UNITY_EDITOR
            if (!StyleFilterSettings.PreviewEnabled || StyleFilterSettings.CompareOriginal) return;
            if (camera.isSceneViewCamera && !StyleFilterSettings.PreviewSceneView) return;
#endif
            if (settings.parameters.strength <= 0) return;
            pass.Setup(renderer, material, settings.parameters);
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
#if UNITY_2022_2_OR_NEWER
            private RTHandle copy;
#endif
            private RenderTextureDescriptor descriptor;
            private ScriptableRenderer renderer;
            private Material material;
            private StyleFilterParameters parameters;

            public void Setup(ScriptableRenderer target, Material effect, StyleFilterParameters values)
            { renderer = target; material = effect; parameters = values; }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
#if UNITY_2022_2_OR_NEWER
                RenderingUtils.ReAllocateIfNeeded(ref copy, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_StyleFilterCopy");
#else
                cmd.GetTemporaryRT(temporary, descriptor, FilterMode.Bilinear);
#endif
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var p = parameters;
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
                cmd.SetGlobalTexture("_BlitTexture", temporary);
                cmd.SetRenderTarget(source, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1);
#endif
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
#if !UNITY_2022_2_OR_NEWER
                cmd.ReleaseTemporaryRT(temporary);
#endif
            }

            public void Dispose()
            {
#if UNITY_2022_2_OR_NEWER
                copy?.Release();
                copy = null;
#endif
            }
        }
    }
}
