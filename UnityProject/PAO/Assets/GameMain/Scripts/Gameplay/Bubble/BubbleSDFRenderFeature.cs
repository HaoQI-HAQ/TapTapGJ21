using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 把泡泡 SDF 的全屏 Pass 注入 URP。
    ///
    /// 安装方式（二选一）：
    ///   · 菜单 Tools/PAO/泡泡 SDF/安装到 URP Renderer —— 一键装好
    ///   · 或手动：选中 URP Renderer 资产 -> Add Renderer Feature -> Bubble SDF Render Feature
    ///
    /// 只需要装一个。场景里没有 SDFBubble 时 Pass 会自己跳过，不会白跑。
    /// </summary>
    [DisallowMultipleRendererFeature("Bubble SDF Render Feature")]
    public sealed class BubbleSDFRenderFeature : ScriptableRendererFeature
    {
        /// <summary>调试可视化模式。排查问题用，正常渲染时保持 Normal。</summary>
        public enum BubbleDebugMode
        {
            Normal       = 0,  // 正常渲染
            HitMask      = 1,  // 白 = 光线步进收敛到了壳面；黑 = 没收敛
            SceneDist    = 2,  // 场景距离场（应看到泡泡处有一圈暗环）
            WorldPos     = 3,  // 命中点世界坐标（条纹应随相机移动而"流动"）
            WorldNormal  = 4,  // 世界空间法线（泡泡应是平滑渐变的球面）
            RayDir       = 5,  // 光线方向（应是一个平滑的渐变，不是纯色）
            DepthPos     = 6,  // 深度反投影结果
            SurfaceLevel = 7,  // 壳面收敛程度（泡泡应为亮色实心）
        }

        /// <summary>所有泡泡共用的一组参数。改这里 = 全局改观感。</summary>
        [Serializable]
        public class BubbleSDFSettings
        {
            [Tooltip("Pass 在 URP 流程里的注入时机。泡泡是透明物体，放在 AfterRenderingTransparents 之后比较合适。")]
            public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

            [Header("性能")]
            [Tooltip("SDF 渲染的分辨率除数。2 = 半分辨率。泡泡边缘很软，降到 2 几乎看不出差别，但省一半以上算力。")]
            [Range(1, 8)] public int resolutionDivisor = 2;

            [Tooltip("只把相机视野内的泡泡送进 GPU，减少 shader 里的循环次数。")]
            public bool enableFrustumCulling = true;

            [Header("外观")]
            [Tooltip("总开关。关掉后泡泡完全不渲染。")]
            public bool enableRendering = true;

            [Tooltip("调试可视化。排查问题时逐个试；正常使用保持 Normal。")]
            public BubbleDebugMode debugMode = BubbleDebugMode.Normal;

            [Tooltip("用场景深度遮挡泡泡：实体挡在前面时泡泡会被正确挡住。\n" +
                     "需要 URP 资产里勾上 Depth Texture，否则自动失效（不会报错）。")]
            public bool enableOcclusion = true;

            [Tooltip("所有泡泡的额外融合强度（米）。单颗泡泡自己的 FuseStrength 仍然生效，取较大值。")]
            [Range(0f, 2f)] public float globalFuse = 0.05f;

            [Tooltip("表面抖动速度。")]
            [Range(0f, 10f)] public float distortionSpeed = 1.2f;

            [Tooltip("薄膜干涉彩虹的强度。")]
            [Range(0f, 4f)] public float iridescenceIntensity = 1.2f;

            [Tooltip("彩虹的密集程度。越大，从泡泡中心到边缘扫过的色环越多。")]
            [Range(0.1f, 6f)] public float iridescenceScale = 1.6f;

            [Tooltip("高光强度。")]
            [Range(0f, 4f)] public float specularIntensity = 1.1f;

            [Tooltip("高光锐度。越大高光点越小越亮。")]
            [Range(1f, 512f)] public float specularPower = 96f;

            [Tooltip("泡泡不透明度。0.6 左右最像肥皂泡，调高会变成实心玻璃球。")]
            [Range(0f, 3f)] public float opacity = 0.65f;

            [Tooltip("边缘聚拢程度（菲涅尔指数）。越大，亮边越窄越贴边。")]
            [Range(0.5f, 8f)] public float rimPower = 2.2f;

            [Tooltip("全局色调，乘在每颗泡泡的颜色上。")]
            public Color globalTint = Color.white;

            [Header("高光方向")]
            [Tooltip("使用下面这个固定方向作为主光，而不是跟随场景里的平行光。")]
            public bool useLightDirection = true;

            [Tooltip("指向光源的方向（世界空间）。")]
            public Vector3 lightDirection = new Vector3(-0.4f, 0.8f, -0.45f);
        }

        public BubbleSDFSettings settings = new BubbleSDFSettings();

        [Tooltip("留空则运行时自动创建（Shader 路径：PAO/BubbleSDF）。")]
        public Material material;

        private BubbleSDFPass m_Pass;
        private Material m_RuntimeMaterial;

        public override void Create()
        {
            Shader shader = Shader.Find("PAO/BubbleSDF");
            if (shader == null)
            {
                Debug.LogError("[BubbleSDF] 找不到 Shader \"PAO/BubbleSDF\"，" +
                               "确认 Assets/GameMain/Art/Shaders/BubbleSDF.shader 存在且能编译。");
                return;
            }

            if (material != null && material.shader == shader)
            {
                m_RuntimeMaterial = material;
            }
            else
            {
                if (material != null)
                    Debug.LogWarning("[BubbleSDF] 指定的材质用的不是 PAO/BubbleSDF，已改用自动创建的实例。");

                m_RuntimeMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_RuntimeMaterial.name = "BubbleSDF (Runtime)";
            }

            m_Pass = new BubbleSDFPass(m_RuntimeMaterial, settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || m_RuntimeMaterial == null)
                return;

            // 预览相机不参与，免得在材质球预览 / 反射探针里也跑一遍 SDF
            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
                return;

            m_Pass.SyncSettings();
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_Pass != null)
            {
                m_Pass.Dispose();
                m_Pass = null;
            }

            // 只有自动创建的才销毁；用户自己指定的材质归用户管
            if (m_RuntimeMaterial != null && m_RuntimeMaterial != material)
                CoreUtils.Destroy(m_RuntimeMaterial);

            m_RuntimeMaterial = null;
        }
    }
}
