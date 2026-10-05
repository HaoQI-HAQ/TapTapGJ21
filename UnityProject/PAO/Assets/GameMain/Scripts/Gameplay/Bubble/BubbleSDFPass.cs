using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 泡泡 SDF 的渲染 Pass。做四件事：
    ///
    ///   1. 把可见泡泡数据上传到 ComputeBuffer
    ///   2. 备份「渲染前」的相机画面 -> _SourceTexture
    ///   3. 全屏 Blit 到一张（可降采样的）HDR 中间图，用材质 pass 0 做 SDF 光线步进
    ///   4. 再 Blit 回相机颜色，用材质 pass 1 把泡泡合成上去
    ///
    /// 生命周期由 BubbleSDFRenderFeature 管理。
    /// </summary>
    public sealed class BubbleSDFPass : ScriptableRenderPass
    {
        private const string k_ProfilerTag = "BubbleSDF";

        private static readonly int s_ShapeDataBuffer     = Shader.PropertyToID("_ShapeDataBuffer");
        private static readonly int s_ShapeDataCount      = Shader.PropertyToID("_ShapeDataCount");
        private static readonly int s_SourceTexture       = Shader.PropertyToID("_SourceTexture");
        private static readonly int s_MainTex             = Shader.PropertyToID("_MainTex");
        private static readonly int s_IVP                 = Shader.PropertyToID("_IVP");
        private static readonly int s_CameraDepthTexture  = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraOpaqueTexture = Shader.PropertyToID("_CameraOpaqueTexture");
        private static readonly int s_GlobalFuse          = Shader.PropertyToID("_BubbleGlobalFuse");
        private static readonly int s_DistortionSpeed     = Shader.PropertyToID("_BubbleDistortionSpeed");
        private static readonly int s_Iridescence         = Shader.PropertyToID("_BubbleIridescence");
        private static readonly int s_IridescenceScale    = Shader.PropertyToID("_BubbleIridescenceScale");
        private static readonly int s_SpecularIntensity   = Shader.PropertyToID("_BubbleSpecularIntensity");
        private static readonly int s_SpecularPower       = Shader.PropertyToID("_BubbleSpecularPower");
        private static readonly int s_Opacity             = Shader.PropertyToID("_BubbleOpacity");
        private static readonly int s_RimPower            = Shader.PropertyToID("_BubbleRimPower");
        private static readonly int s_LightDir            = Shader.PropertyToID("_BubbleLightDir");
        private static readonly int s_Tint                = Shader.PropertyToID("_BubbleTint");
        private static readonly int s_Enabled             = Shader.PropertyToID("_BubbleEnabled");
        private static readonly int s_Occlusion           = Shader.PropertyToID("_BubbleOcclusion");
        private static readonly int s_Debug               = Shader.PropertyToID("_BubbleDebug");

        private readonly BubbleSDFRenderFeature.BubbleSDFSettings m_Settings;
        private readonly Material m_Material;

        private RTHandle m_SdfTexture;      // SDF 结果（可降采样）
        private RTHandle m_SourceTexture;   // 渲染前的画面备份

        private ComputeBuffer m_ShapeBuffer;
        private int m_ShapeBufferCapacity;

        public BubbleSDFPass(Material material, BubbleSDFRenderFeature.BubbleSDFSettings settings)
        {
            m_Material       = material;
            m_Settings       = settings;
            profilingSampler = new ProfilingSampler(k_ProfilerTag);
            renderPassEvent  = settings.passEvent;
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
            int divisor = Mathf.Clamp(m_Settings.resolutionDivisor, 1, 8);

            RenderTextureDescriptor sdfDesc = cameraTextureDescriptor;
            sdfDesc.width           = Mathf.Max(1, cameraTextureDescriptor.width  / divisor);
            sdfDesc.height          = Mathf.Max(1, cameraTextureDescriptor.height / divisor);
            sdfDesc.depthBufferBits = 0;
            sdfDesc.msaaSamples     = 1;
            sdfDesc.colorFormat     = RenderTextureFormat.DefaultHDR;   // 给 Bloom 留余量

            RenderingUtils.ReAllocateIfNeeded(ref m_SdfTexture, sdfDesc, FilterMode.Bilinear,
                                              TextureWrapMode.Clamp, name: "_BubbleSDFTexture");

            RenderTextureDescriptor srcDesc = cameraTextureDescriptor;
            srcDesc.depthBufferBits = 0;
            srcDesc.msaaSamples     = 1;

            RenderingUtils.ReAllocateIfNeeded(ref m_SourceTexture, srcDesc, FilterMode.Bilinear,
                                              TextureWrapMode.Clamp, name: "_BubbleSourceTexture");
        }

        // ------------------------------------------------------------------
        // 每帧执行
        // ------------------------------------------------------------------

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

            // 视锥剔除能明显减少 shader 里那个 O(N) 循环的 N。
            // 注意：没打 MainCamera tag 的相机会让 Camera.main 为 null，
            // 此时 CollectVisible 退化成「全部提交」，只是少了优化，不影响正确性。
            Camera cullCamera = m_Settings.enableFrustumCulling ? Camera.main : null;

            BubblePayload[] shapes = BubbleSDFManager.CollectVisible(cullCamera);
            int count = BubbleSDFManager.VisibleCount;
            bool hasShapes = shapes != null && count > 0;

            RTHandle cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (cameraTarget == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(k_ProfilerTag);

            // ---- 上传形状数据 ----
            if (hasShapes)
            {
                EnsureShapeBuffer(count);
                if (m_ShapeBuffer != null)
                    m_ShapeBuffer.SetData(shapes, 0, 0, count);
            }

            SetGlobalProperties(cmd, camera, (hasShapes && m_ShapeBuffer != null) ? count : 0);

            // ---- 备份渲染前的画面 ----
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_SourceTexture);
            cmd.SetGlobalTexture(s_SourceTexture, m_SourceTexture);

            // ---- Pass 0：SDF 光线步进 -> 中间图 ----
            Blitter.BlitCameraTexture(cmd, cameraTarget, m_SdfTexture, m_Material, 0);

            // Blitter 不会自动把「它自己刚写入的那张图」绑给下一趟要用的 _MainTex，
            // 这里手动绑上。
            cmd.SetGlobalTexture(s_MainTex, m_SdfTexture);

            // 注意：这里【不能】把 _CameraDepthTexture 解绑。
            // 之前为了防止"同一张纹理既作输入又作输出"而解绑它，结果连深度遮挡
            // 一起废掉了。Pass 1 的输出目标是相机颜色，和深度纹理不冲突，
            // 所以保持绑定是安全的。
            cmd.SetGlobalTexture(s_CameraOpaqueTexture, Texture2D.whiteTexture);

            // ---- Pass 1：合成回相机颜色 ----
            Blitter.BlitCameraTexture(cmd, m_SdfTexture, cameraTarget, m_Material, 1);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void EnsureShapeBuffer(int count)
        {
            if (m_ShapeBuffer != null && m_ShapeBufferCapacity >= count)
                return;

            if (m_ShapeBuffer != null)
            {
                m_ShapeBuffer.Dispose();
                m_ShapeBuffer = null;
            }

            // 建缓冲区之前先确认托管布局没漂 —— 漂了就在这里报出来，
            // 而不是等 SetData 抛一个看不懂的 stride 异常。
            if (!BubblePayload.ValidateLayout(out int actualSize, out string layoutError))
            {
                Debug.LogError("[BubbleSDF] " + layoutError);
                return;
            }

            // 留一倍余量，避免泡泡逐个加入时反复重建
            m_ShapeBufferCapacity = Mathf.NextPowerOfTwo(Mathf.Max(count, 64));
            m_ShapeBuffer = new ComputeBuffer(m_ShapeBufferCapacity,
                                              BubblePayload.Stride,
                                              ComputeBufferType.Structured);
        }

        private void SetGlobalProperties(CommandBuffer cmd, Camera camera, int count)
        {
            cmd.SetGlobalInt(s_ShapeDataCount, count);

            if (count > 0 && m_ShapeBuffer != null)
                cmd.SetGlobalBuffer(s_ShapeDataBuffer, m_ShapeBuffer);

            cmd.SetGlobalFloat(s_Enabled, m_Settings.enableRendering ? 1f : 0f);
            cmd.SetGlobalFloat(s_Occlusion, m_Settings.enableOcclusion ? 1f : 0f);
            cmd.SetGlobalInt(s_Debug, (int)m_Settings.debugMode);

            // 逆 View-Projection：shader 用它把「屏幕像素 + 深度」反投影成世界坐标
            Matrix4x4 proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 vp   = proj * camera.worldToCameraMatrix;
            cmd.SetGlobalMatrix(s_IVP, vp.inverse);

            // ---- 外观参数 ----
            cmd.SetGlobalFloat(s_GlobalFuse,        m_Settings.globalFuse);
            cmd.SetGlobalFloat(s_DistortionSpeed,   m_Settings.distortionSpeed);
            cmd.SetGlobalFloat(s_Iridescence,       m_Settings.iridescenceIntensity);
            cmd.SetGlobalFloat(s_IridescenceScale,  m_Settings.iridescenceScale);
            cmd.SetGlobalFloat(s_SpecularIntensity, m_Settings.specularIntensity);
            cmd.SetGlobalFloat(s_SpecularPower,     m_Settings.specularPower);
            cmd.SetGlobalFloat(s_Opacity,           m_Settings.opacity);
            cmd.SetGlobalFloat(s_RimPower,          m_Settings.rimPower);

            Vector3 ld = m_Settings.lightDirection;
            if (ld.sqrMagnitude < 1e-6f)
                ld = new Vector3(-0.4f, 0.8f, -0.45f);
            ld.Normalize();

            cmd.SetGlobalVector(s_LightDir, new Vector4(ld.x, ld.y, ld.z,
                                                        m_Settings.useLightDirection ? 1f : 0f));
            cmd.SetGlobalVector(s_Tint, (Vector4)m_Settings.globalTint);
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
            if (m_ShapeBuffer != null)
            {
                m_ShapeBuffer.Dispose();
                m_ShapeBuffer = null;
            }

            m_ShapeBufferCapacity = 0;

            if (m_SdfTexture != null)
            {
                m_SdfTexture.Release();
                m_SdfTexture = null;
            }

            if (m_SourceTexture != null)
            {
                m_SourceTexture.Release();
                m_SourceTexture = null;
            }

            CoreUtils.Destroy(m_Material);
        }
    }
}
