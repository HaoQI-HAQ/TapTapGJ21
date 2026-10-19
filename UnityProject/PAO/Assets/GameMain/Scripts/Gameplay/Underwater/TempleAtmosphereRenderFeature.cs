using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.Underwater
{
    /// <summary>A single blend pass, shared by game and Scene view cameras. No colour-buffer copy.</summary>
    [DisallowMultipleRendererFeature("Temple Atmosphere")]
    public sealed class TempleAtmosphereRenderFeature : ScriptableRendererFeature
    {
        AtmospherePass pass;

        public override void Create() { pass = new AtmospherePass(); }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            if (!TempleAtmosphereVolume.TryGet(camera, out Material material)) return;
            pass.material = material;
            renderer.EnqueuePass(pass);
        }

        sealed class AtmospherePass : ScriptableRenderPass
        {
            public Material material;
            RTHandle target;

            public AtmospherePass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                target = renderingData.cameraData.renderer.cameraColorTargetHandle;
                ConfigureTarget(target);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (material == null || target == null) return;
                CommandBuffer cmd = CommandBufferPool.Get("Temple water haze");
                try
                {
                    CoreUtils.SetRenderTarget(cmd, target);
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    context.ExecuteCommandBuffer(cmd);
                }
                finally { CommandBufferPool.Release(cmd); }
            }
        }
    }
}
