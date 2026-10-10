using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡潮的渲染 Pass。做四件事：
    ///
    ///   1. 把 URP 资产上的观感参数写进全局 shader 变量
    ///   2. Blit 相机颜色 → 一张（可降采样的）HDR 中间图，用材质 pass 0 做光线步进
    ///   3. 再 Blit 回相机颜色，用材质 pass 1 合成
    ///
    /// 场数据（3D 纹理）不由这里上传 —— 它是 BubbleTideDirector 在烤制时
    /// 用 Shader.SetGlobalTexture 设好的，运行期不会变。这样这个 Pass
    /// 每帧只写几个 float，CPU 开销可以忽略。
    ///
    /// 生命周期由 BubbleTideRenderFeature 管理。
    /// </summary>
    public sealed class BubbleTidePass : ScriptableRenderPass
    {
        private const string k_ProfilerTag = "BubbleTide";

        // ---- 场数据（由 Director 设置，这里只读）----
        private static readonly int s_Field          = Shader.PropertyToID("_BubbleTideField");
        private static readonly int s_Origin         = Shader.PropertyToID("_BubbleTideOrigin");
        private static readonly int s_Size           = Shader.PropertyToID("_BubbleTideSize");
        private static readonly int s_NormalizeScale = Shader.PropertyToID("_BubbleTideNormalizeScale");

        // ---- 本 Pass 每帧写的 ----
        private static readonly int s_Texture       = Shader.PropertyToID("_BubbleTideTexture");
        private static readonly int s_MaxSteps      = Shader.PropertyToID("_TideMaxSteps");
        private static readonly int s_StepScale     = Shader.PropertyToID("_TideStepScale");
        private static readonly int s_Opacity       = Shader.PropertyToID("_TideOpacity");
        private static readonly int s_RimPower      = Shader.PropertyToID("_TideRimPower");
        private static readonly int s_RimIntensity  = Shader.PropertyToID("_TideRimIntensity");
        private static readonly int s_Iridescence   = Shader.PropertyToID("_TideIridescence");
        private static readonly int s_IridScale     = Shader.PropertyToID("_TideIridescenceScale");
        private static readonly int s_NoiseScale    = Shader.PropertyToID("_TideNoiseScale");
        private static readonly int s_NoiseStrength = Shader.PropertyToID("_TideNoiseStrength");
        private static readonly int s_UseDepth      = Shader.PropertyToID("_TideUseDepthOcclusion");
        private static readonly int s_Debug         = Shader.PropertyToID("_TideDebug");
        private static readonly int s_ShallowColor  = Shader.PropertyToID("_TideShallowColor");
        private static readonly int s_DeepColor     = Shader.PropertyToID("_TideDeepColor");
        private static readonly int s_LightDir      = Shader.PropertyToID("_TideLightDir");

        private readonly BubbleTideRenderFeature.BubbleTideSettings m_Settings;
        private readonly Material m_Material;

        private RTHandle m_TideTexture;

        public BubbleTidePass(Material material, BubbleTideRenderFeature.BubbleTideSettings settings)
        {
            m_Material      = material;
            m_Settings      = settings;
            profilingSampler = new ProfilingSampler(k_ProfilerTag);
            renderPassEvent = settings.passEvent;
        }

        /// <summary>
        /// 场数据准备好了吗。渲染 Feature 用它决定要不要 Enqueue 这一趟。
        /// 没准备好的时候直接不排队，比排进去再 early-return 更省。
        /// </summary>
        public static bool HasTideData()
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            return director != null
                && director.Field != null
                && director.Field.IsBaked
                && BubbleTideDirector.FieldTexture != null;
        }

        /// <summary>Inspector 里改过参数之后，重新同步注入时机。</summary>
        public void SyncSettings()
        {
            renderPassEvent = m_Settings.passEvent;
        }

        // ------------------------------------------------------------------
        // 资源分配
        // ------------------------------------------------------------------

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            int divisor = Mathf.Clamp(m_Settings.resolutionDivisor, 1, 4);

            RenderTextureDescriptor desc = cameraTextureDescriptor;
            desc.width           = Mathf.Max(1, cameraTextureDescriptor.width  / divisor);
            desc.height          = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            desc.depthBufferBits = 0;
            desc.msaaSamples     = 1;
            desc.colorFormat     = RenderTextureFormat.DefaultHDR;   // 给 Bloom 留余量

            RenderingUtils.ReAllocateIfNeeded(ref m_TideTexture, desc, FilterMode.Bilinear,
                                              TextureWrapMode.Clamp, name: "_BubbleTideTextureRT");
        }

        // ------------------------------------------------------------------
        // 每帧执行
        // ------------------------------------------------------------------

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!HasTideData())
            {
                return;
            }

            RTHandle cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (cameraTarget == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get(k_ProfilerTag);

            ApplySettings(cmd);

            // ---- Pass 0：光线步进 -> 中间图 ----
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_TideTexture, m_Material, 0);

            // Blitter 不会把「它自己刚写入的那张图」绑给下一趟要用的 _MainTex，手动绑上
            cmd.SetGlobalTexture(s_Texture, m_TideTexture);

            // ---- Pass 1：合成回相机颜色 ----
            Blitter.BlitCameraTexture(cmd, m_TideTexture, cameraTarget, m_Material, 1);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

#if UNITY_2022_2_OR_NEWER && PAO_BUBBLETIDE_RENDERGRAPH
        /// <summary>
        /// RenderGraph 路径（**当前默认不编译**）。
        ///
        /// 项目的 URP 是 14.0.11，Renderer 默认走兼容模式，也就是上面那个 Execute。
        /// 而 RenderGraph 的 Blit 辅助 API 在 14.0 里还不可用，硬写会编译不过 ——
        /// 所以这里用 PAO_BUBBLETIDE_RENDERGRAPH 这个自定义宏挡住。
        ///
        /// 什么时候需要打开它：在 Project Settings → Graphics → URP 里
        /// 勾了 Render Graph，那时兼容模式的 Execute 会不再被调用。
        /// 打开之前请先确认所用的 URP 版本里有 AddBlitPass 这个 API，
        /// 没有的话就按该版本的写法改成 renderGraph.AddRenderPass + 手动 SetRenderTarget。
        /// </summary>
        public override void RecordRenderGraph(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            if (!HasTideData())
            {
                return;
            }

            RTHandle cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (cameraTarget == null)
            {
                return;
            }

            RenderGraphUtils.BlitMaterialParameters pass0 =
                new RenderGraphUtils.BlitMaterialParameters(cameraTarget, m_TideTexture, m_Material, 0);
            renderGraph.AddBlitPass(pass0, k_ProfilerTag + " Raymarch");

            RenderGraphUtils.BlitMaterialParameters pass1 =
                new RenderGraphUtils.BlitMaterialParameters(m_TideTexture, cameraTarget, m_Material, 1);
            renderGraph.AddBlitPass(pass1, k_ProfilerTag + " Composite");
        }
#endif

        /// <summary>
        /// 把 Inspector 上的观感参数写进全局 shader 变量。
        /// </summary>
        private void ApplySettings(CommandBuffer cmd)
        {
            cmd.SetGlobalFloat(s_MaxSteps,      Mathf.Clamp(m_Settings.maxSteps, 16, 512));
            cmd.SetGlobalFloat(s_StepScale,     Mathf.Clamp(m_Settings.stepScale, 0.25f, 2f));
            cmd.SetGlobalFloat(s_Opacity,       Mathf.Clamp01(m_Settings.opacity));
            cmd.SetGlobalFloat(s_RimPower,      m_Settings.rimPower);
            cmd.SetGlobalFloat(s_RimIntensity,  m_Settings.rimIntensity);
            cmd.SetGlobalFloat(s_Iridescence,   m_Settings.iridescence);
            cmd.SetGlobalFloat(s_IridScale,     m_Settings.iridescenceScale);
            cmd.SetGlobalFloat(s_NoiseScale,    m_Settings.noiseScale);
            cmd.SetGlobalFloat(s_NoiseStrength, Mathf.Clamp01(m_Settings.noiseStrength));
            cmd.SetGlobalFloat(s_UseDepth,      m_Settings.useDepthOcclusion ? 1f : 0f);
            cmd.SetGlobalFloat(s_Debug,         (float)(int)m_Settings.debugMode);

            cmd.SetGlobalVector(s_ShallowColor, (Vector4)m_Settings.shallowColor);
            cmd.SetGlobalVector(s_DeepColor,    (Vector4)m_Settings.deepColor);

            Vector3 ld = m_Settings.lightDirection;
            if (ld.sqrMagnitude < 1e-6f)
            {
                ld = new Vector3(-0.4f, 0.8f, -0.45f);
            }

            ld.Normalize();
            cmd.SetGlobalVector(s_LightDir, new Vector4(ld.x, ld.y, ld.z,
                                                        m_Settings.useLightDirection ? 1f : 0f));

            // 场数据虽然由 Director 设置过，但 URP 的 RenderGraph 路径会重置全局纹理绑定，
            // 所以每帧重新绑一次，代价可以忽略。
            Texture3D field = BubbleTideDirector.FieldTexture;
            if (field != null)
            {
                cmd.SetGlobalTexture(s_Field, field);
            }
        }

        // ------------------------------------------------------------------
        // 清理
        // ------------------------------------------------------------------

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // RT 跨帧复用，这里不释放；真正释放在 Dispose 里。
        }

        public void Dispose()
        {
            if (m_TideTexture != null)
            {
                m_TideTexture.Release();
                m_TideTexture = null;
            }

            CoreUtils.Destroy(m_Material);
        }
    }
}
