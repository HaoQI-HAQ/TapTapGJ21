using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.Underwater
{
    /// <summary>
    /// 水下效果的渲染 Pass。一个类管四段，用 Stage 区分。
    ///
    /// =======================================================================
    /// 【★ 这个类最重要的一条规则：相机颜色永远只写不读】
    ///
    ///   四段里有三段要「把画面改一改再写回去」。每一步都必须先把画面
    ///   拷进一张**自己的**纹理，再从那张纹理读。
    ///
    ///   为什么这条规则不能破：
    ///
    ///   1. **UV 的 Y 翻转。** 渲染到 RT 和渲染到相机颜色，UV 的 Y 方向是
    ///      相反的（Unity 的 RT 约定），由 Unity 的
    ///      GetFullScreenTriangleTexCoord 通过 _ProjectionParams.x 自动补偿。
    ///      「从纹理读 + 写相机颜色」这个组合天然成立；
    ///      「从相机颜色读 + 写相机颜色」就不成立 —— 同一套 uv 拿去采样
    ///      会上下颠倒，焦散会跑到天花板上去。
    ///
    ///   2. **同一张 RT 既读又写**在部分平台上是未定义行为。
    ///      在编辑器里九成时候看着是对的，剩下那一成在打包后表现为
    ///      画面偶尔整块花掉，且只在特定分辨率复现。
    ///
    ///   3. **Blitter 会直接炸。** 实测过：在这种用法下
    ///      Blitter.BlitCameraTexture 抛
    ///        「Assertion failed」
    ///        「ArgumentNullException: Value cannot be null. Parameter name: value」
    ///      而且每帧刷屏几百条。项目里泡泡 SDF / 泡泡潮那两套能用 Blitter，
    ///      是因为它们**只把相机颜色当源**，输出永远写进自己的中间纹理。
    ///
    ///   4. **源和目标的尺寸必须对得上。** Blitter 的 viewport 是按源的
    ///      rtHandleScale 算的，尺寸不一致会只画到一块区域。
    ///      所以下面每一段都自己分配纹理，除数跟着那段的分辨率走。
    ///
    ///   代价是每帧多几次全屏拷贝。拷一次是「一次采样 + 一次写入」，
    ///   比一趟带循环的 Pass 便宜得多 —— 换来的是不会莫名其妙炸。
    /// =======================================================================
    ///
    /// 【生命周期】
    ///   由 UnderwaterRenderFeature 创建 / 释放。RT 跨帧复用，
    ///   不在 OnCameraCleanup 里释放（那会导致每帧重新分配一张全屏 RT，
    ///   在编辑器里表现为 GC 曲线一直有尖峰）。
    /// </summary>
    public sealed class UnderwaterPass : ScriptableRenderPass
    {
        public enum Stage
        {
            /// <summary>算焦散量，写中间纹理。半分辨率。</summary>
            Caustics = 0,

            /// <summary>把焦散量加法混回画面。全分辨率。</summary>
            CausticsComposite = 1,

            /// <summary>深度雾。全分辨率 —— 它便宜。</summary>
            Composite = 2,

            /// <summary>体积光。半分辨率 —— 它贵。</summary>
            GodRays = 3,
        }

        // 在 UnderwaterFullscreen.shader 里的下标
        private static readonly int k_PassCaustics = 0;
        private static readonly int k_PassCausticsComposite = 1;
        private static readonly int k_PassFog = 2;

        // 在 UnderwaterGodRays.shader 里是唯一的 Pass
        private static readonly int k_PassGodRays = 0;

        private readonly Material m_Material;
        private readonly Material m_GodRayMaterial;
        private readonly UnderwaterRenderFeature.UnderwaterSettings m_Settings;
        private readonly Stage m_Stage;
        private readonly string m_ProfilerTag;

        /// <summary>焦散量。（半分辨率，RGHalf）</summary>
        private RTHandle m_CausticsTexture;

        /// <summary>画面备份。每段自己的，分辨率跟着那段走。</summary>
        private RTHandle m_ColorTexture;

        // 相机颜色目标拿不到时只报一次错。渲染问题一旦发生就是每帧一次，
        // 刷屏会把别的日志全冲掉 —— 那是「报错太多反而查不出问题」的典型。
        private bool m_ReportedMissingCameraTarget;

        public UnderwaterPass(Material material,
                              Material godRayMaterial,
                              UnderwaterRenderFeature.UnderwaterSettings settings,
                              Stage stage)
        {
            m_Material = material;
            m_GodRayMaterial = godRayMaterial;
            m_Settings = settings;
            m_Stage    = stage;

            m_ProfilerTag = "Underwater." + stage;

            profilingSampler = new ProfilingSampler(m_ProfilerTag);
            renderPassEvent = settings.GetPassEvent(stage);

            // 四段都要深度：焦散靠它重建世界坐标和法线，
            // 雾和体积光靠它算「这条光线最多走多远」。
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
        }

        /// <summary>Inspector 里改过参数之后，重新同步注入时机。</summary>
        public void SyncSettings()
        {
            renderPassEvent = m_Settings.GetPassEvent(m_Stage);
        }

        // ------------------------------------------------------------------
        // 资源分配
        // ------------------------------------------------------------------

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // 描述符必须有效，否则分配出来的是 0×0 的 RT：
            // 设成渲染目标也「什么都不画」，而且不会有任何报错。
            if (cameraTextureDescriptor.width <= 0 || cameraTextureDescriptor.height <= 0)
            {
                return;
            }

            switch (m_Stage)
            {
                // 焦散：只要一张半分辨率的量。
                case Stage.Caustics:
                {
                    int divisor = Mathf.Clamp(m_Settings.causticsResolutionDivisor, 1, 4);

                    RenderTextureDescriptor desc = Scaled(cameraTextureDescriptor, divisor);

                    // RHalf 而不是 R8：G 通道要存归一化后的视距（0~1，除以 20 米），
                    // 8 位只有 256 级 → 每级 8 厘米，表面匹配会一直在临界值上抖。
                    // half 是这个用途最便宜的合适格式。
                    desc.colorFormat = RenderTextureFormat.RGHalf;

                    RenderingUtils.ReAllocateIfNeeded(ref m_CausticsTexture, desc,
                                                      FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                      name: "_PAO_CausticsTexture");
                    break;
                }

                // 焦散合成 + 深度雾：全分辨率。
                case Stage.CausticsComposite:
                case Stage.Composite:
                {
                    RenderTextureDescriptor desc = Scaled(cameraTextureDescriptor, 1);

                    // DefaultHDR：水下颜色会写进超过 1 的值，交给 Bloom 咬。
                    desc.colorFormat = RenderTextureFormat.DefaultHDR;

                    RenderingUtils.ReAllocateIfNeeded(ref m_ColorTexture, desc,
                                                      FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                      name: "_PAO_ColorTexture");
                    break;
                }

                // 体积光：半分辨率。这也是它单独成一趟的全部意义 ——
                // 步进算力直接省一半，而光柱是十几米厚的体积，看不出来。
                case Stage.GodRays:
                {
                    int divisor = Mathf.Clamp(m_Settings.godRayResolutionDivisor, 1, 4);

                    RenderTextureDescriptor desc = Scaled(cameraTextureDescriptor, divisor);
                    desc.colorFormat = RenderTextureFormat.DefaultHDR;

                    RenderingUtils.ReAllocateIfNeeded(ref m_ColorTexture, desc,
                                                      FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                      name: "_PAO_ColorTextureHalf");
                    break;
                }
            }
        }

        /// <summary>按除数缩小描述符，并清掉深度和 MSAA —— 这是张纯颜色图。</summary>
        private static RenderTextureDescriptor Scaled(RenderTextureDescriptor src, int divisor)
        {
            divisor = Mathf.Max(1, divisor);

            RenderTextureDescriptor desc = src;
            desc.width           = Mathf.Max(1, src.width  / divisor);
            desc.height          = Mathf.Max(1, src.height / divisor);
            desc.depthBufferBits = 0;
            desc.msaaSamples     = 1;

            return desc;
        }

        // ------------------------------------------------------------------
        // 每帧执行
        // ------------------------------------------------------------------

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Material == null)
            {
                return;
            }

            RTHandle cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            // 【为什么只判断 == null，不判断 cameraTarget.rt == null】
            //   URP 的 cameraColorTargetHandle 在很多路径下走的是 RTHandle 的
            //   **fallback**（见 ScriptableRenderer.RTHandleRenderTargetIdentifierCompat）：
            //   这时 handle 有效，但 .rt 可以是 null —— 因为相机颜色挂在 builtin 的
            //   CameraTarget 上，而不是一张 URP 自己的 RenderTexture。
            //
            //   加上 .rt 判断之后，这个本该正常工作的情况会被误报成
            //   「拿不到相机颜色目标」。之前那条误导性的报错就是这么来的。
            if (cameraTarget == null)
            {
                ReportMissingCameraTarget(renderingData.cameraData.cameraType);
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get(m_ProfilerTag);

            // 相机矩阵和缓冲区尺寸。必须在最前面 ——
            // 后面所有重建世界坐标的代码都依赖它。
            UnderwaterCameraUtil.SetCameraMatrices(cmd, renderingData);

            switch (m_Stage)
            {
                case Stage.Caustics:
                    RecordCaustics(cmd, cameraTarget);
                    break;

                case Stage.CausticsComposite:
                    RecordCausticsComposite(cmd, cameraTarget);
                    break;

                case Stage.Composite:
                    RecordFog(cmd, cameraTarget);
                    break;

                case Stage.GodRays:
                    RecordGodRays(cmd, cameraTarget);
                    break;
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // ------------------------------------------------------------------
        // 第 0 段：焦散量 → 中间纹理
        // ------------------------------------------------------------------

        private void RecordCaustics(CommandBuffer cmd, RTHandle cameraTarget)
        {
            if (m_CausticsTexture == null)
            {
                return;
            }

            // 这一趟不读画面，所以不需要拷贝 —— 直接设目标画进去。
            // 焦散是「凭空算出来的光」，shader 里也只采深度。
            UnderwaterCameraUtil.Draw(cmd, m_CausticsTexture,
                                      m_Material, k_PassCaustics, "焦散");

            cmd.SetGlobalTexture(UnderwaterShaderIDs.CausticsTexture, m_CausticsTexture);
        }

        // ------------------------------------------------------------------
        // 第 1 段：焦散合成 → 相机颜色
        // ------------------------------------------------------------------

        private void RecordCausticsComposite(CommandBuffer cmd, RTHandle cameraTarget)
        {
            if (m_ColorTexture == null)
            {
                return;
            }

            // 先备份画面。源（相机颜色）和目标（自己的纹理）永远是两张，
            // 这是 Blitter 的标准用法，没有风险。
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_ColorTexture);

            cmd.SetGlobalTexture(UnderwaterShaderIDs.ColorTexture, m_ColorTexture);

            if (m_CausticsTexture != null)
            {
                cmd.SetGlobalTexture(UnderwaterShaderIDs.CausticsTexture, m_CausticsTexture);
            }

            UnderwaterCameraUtil.Draw(cmd, cameraTarget,
                                      m_Material, k_PassCausticsComposite, "焦散·合成");
        }

        // ------------------------------------------------------------------
        // 第 2 段：深度雾 → 相机颜色
        // ------------------------------------------------------------------

        private void RecordFog(CommandBuffer cmd, RTHandle cameraTarget)
        {
            if (m_ColorTexture == null)
            {
                return;
            }

            Blitter.BlitCameraTexture(cmd, cameraTarget, m_ColorTexture);

            cmd.SetGlobalTexture(UnderwaterShaderIDs.ColorTexture, m_ColorTexture);

            // 焦散那两段可能被关掉了，这里兜一下：
            // 没绑的话 shader 会采样到上一帧或者别的 Pass 留下的纹理。
            if (m_CausticsTexture != null)
            {
                cmd.SetGlobalTexture(UnderwaterShaderIDs.CausticsTexture, m_CausticsTexture);
            }

            UnderwaterCameraUtil.Draw(cmd, cameraTarget,
                                      m_Material, k_PassFog, "深度雾");
        }

        // ------------------------------------------------------------------
        // 第 3 段：体积光 → 相机颜色
        // ------------------------------------------------------------------

        private void RecordGodRays(CommandBuffer cmd, RTHandle cameraTarget)
        {
            if (m_GodRayMaterial == null || m_ColorTexture == null)
            {
                return;
            }

            // 备份是**半分辨率**的（Configure 里按 godRayResolutionDivisor 分配）。
            // 所以这一次 Blit 顺手做了降采样，最后画回相机目标时又做升采样 ——
            // 升采样是双线性的，等于免费拿了一层抗锯齿。
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_ColorTexture);

            cmd.SetGlobalTexture(UnderwaterShaderIDs.ColorTexture, m_ColorTexture);

            UnderwaterCameraUtil.Draw(cmd, cameraTarget,
                                      m_GodRayMaterial, k_PassGodRays, "体积光");
        }

        // ------------------------------------------------------------------

        /// <summary>相机颜色目标拿不到时只报一次，并说清楚该怎么查。</summary>
        private void ReportMissingCameraTarget(CameraType cameraType)
        {
            if (m_ReportedMissingCameraTarget)
            {
                return;
            }

            m_ReportedMissingCameraTarget = true;

            Debug.LogError("[水下] " + m_Stage + "：拿不到相机颜色目标" +
                           "（相机类型 " + cameraType + "）。这一段不会被绘制。\n" +
                           "常见原因：\n" +
                           "  1. 注入时机设成了相机渲染流程之外的阶段\n" +
                           "  2. 相机没开 Post Processing\n" +
                           "  3. 有别的 Renderer Feature 在更早的阶段改了渲染目标\n" +
                           "（这条错误只报一次，不会刷屏）");
        }

        // ------------------------------------------------------------------
        // 清理
        // ------------------------------------------------------------------

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // RT 跨帧复用，这里【不能】释放 —— 每帧重新分配一张全屏 RT
            // 会造成持续的内存抖动，在编辑器里表现为 GC 曲线一直有尖峰。
        }

        public void Dispose()
        {
            if (m_CausticsTexture != null)
            {
                m_CausticsTexture.Release();
                m_CausticsTexture = null;
            }

            if (m_ColorTexture != null)
            {
                m_ColorTexture.Release();
                m_ColorTexture = null;
            }

            // 注意：两个材质都不在这里销毁 —— 它们是 ScriptableRendererFeature
            // 创建的，归 Feature 的 Dispose 管。这里再销毁一次会变成
            // 「用了已经销毁的材质」，表现是画面整块变粉。
        }
    }
}
