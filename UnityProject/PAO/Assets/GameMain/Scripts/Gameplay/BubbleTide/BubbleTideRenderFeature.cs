using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 把泡泡潮的全屏光线步进 Pass 注入 URP。
    ///
    /// 安装方式（二选一）：
    ///   · 菜单「工具 → PAO → 泡泡潮 → 安装到 URP Renderer」—— 一键装好
    ///   · 或手动：选中 URP Renderer 资产 → Add Renderer Feature → Bubble Tide Render Feature
    ///
    /// 只需要装一个。场景里没有 BubbleTideDirector（或者潮水场还没烤好）时，
    /// Pass 会自己跳过，不会白跑一次全屏。
    ///
    /// 【重要】这个 Feature 只负责「画」。潮水的数据、计时、胜负判定全在
    /// BubbleTideDirector / BubbleTideField 里，两边通过一个 3D 纹理和几个
    /// 全局 shader 变量通信。所以在 URP 资产里改参数只能改观感，
    /// 不会影响玩法数值 —— 玩法数值在场景里的 BubbleTideDirector 上。
    /// </summary>
    [DisallowMultipleRendererFeature("泡泡潮")]
    public sealed class BubbleTideRenderFeature : ScriptableRendererFeature
    {
        /// <summary>调试可视化模式。排查问题时逐个试，正常保持 Normal。</summary>
        public enum TideDebugMode
        {
            Normal      = 0,   // 正常渲染
            HitDistance = 1,   // 命中距离（近 = 蓝，远 = 红）。用来确认步进确实打到了东西
        }

        /// <summary>潮水的观感参数。改这里 = 全局改潮水长什么样。</summary>
        [Serializable]
        public class BubbleTideSettings
        {
            [Tooltip("Pass 在 URP 流程里的注入时机。潮水是半透明物体，放在不透明物之后比较合适。")]
            public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

            [Header("性能")]
            [Tooltip("总开关。关掉后潮水完全不渲染，但计时与判定照常 —— 用来单独确认玩法手感。")]
            public bool enableRendering = true;

            [Tooltip("渲染的分辨率除数。2 = 半分辨率。潮水边缘很软，\n" +
                     "半分辨率几乎看不出差别，但在移动端能省一半以上算力。")]
            [Range(1, 4)] public int resolutionDivisor = 2;

            [Tooltip("每条光线最多走多少步。这是性能的**主要旋钮**。\n" +
                     "96 步在大步长下足够走很远；\n" +
                     "出现「泡泡在某些角度看是断的」就往上加。")]
            [Range(16, 512)] public int maxSteps = 96;

            [Tooltip("步长倍率。距离场给的步长会乘上它再走。\n" +
                     "小于 1 更保守（更精细、更慢）；大于 1 走得更快但可能蹭过薄处。")]
            [Range(0.15f, 2f)] public float stepScale = 0.8f;

            [Header("遮挡")]
            [Tooltip("用场景深度遮挡潮水：实心墙挡在前面时潮水会被正确遮住。\n" +
                     "【注意】需要 URP 资产里勾上 Depth Texture。没勾时会自动失效，\n" +
                     "画面照常，只是泡泡会透过墙被看到。")]
            public bool useDepthOcclusion = true;

            // ---- 以下参数与 BubbleSDF 的同名参数一一对应 ----
            // 想让潮水和真泡泡看起来是同一种东西，两边的这几个数就保持一致。
            // 默认值直接照抄 URP-Balanced-Renderer 上 BubbleSDFRenderFeature 的现值。

            [Header("外观（与泡泡 SDF 对应）")]
            [Tooltip("泡泡固有色。中心几乎透明，主要影响掠射边缘的着色。")]
            public Color shallowColor = new Color(0.72f, 0.88f, 1f, 1f);

            [Tooltip("泡泡内部颜色（备用：将来做「越深越暗」）。")]
            public Color deepColor = new Color(0.42f, 0.72f, 1f, 1f);

            [Tooltip("不透明度。0.65 左右最像肥皂泡，调高会变成实心玻璃球。")]
            [Range(0f, 3f)] public float opacity = 0.65f;

            [Tooltip("边缘聚拢程度（菲涅尔指数）。越大，亮边越窄越贴边。")]
            [Range(0.5f, 8f)] public float rimPower = 2.2f;

            [Tooltip("高光强度。")]
            [Range(0f, 4f)] public float specularIntensity = 1.1f;

            [Tooltip("高光锐度。越大高光点越小越亮。")]
            [Range(1f, 512f)] public float specularPower = 96f;

            [Tooltip("薄膜干涉彩虹的强度。")]
            [Range(0f, 4f)] public float iridescence = 1.2f;

            [Tooltip("彩虹的密集程度。越大，从泡泡中心到边缘扫过的色环越多。")]
            [Range(0.1f, 6f)] public float iridescenceScale = 1.6f;

            [Header("高光方向")]
            [Tooltip("用固定方向作为主光，而不是跟随场景里的平行光。")]
            public bool useLightDirection = true;

            [Tooltip("指向光源的方向（世界空间）。")]
            public Vector3 lightDirection = new Vector3(-0.4f, 0.8f, -0.45f);

            [Header("调试")]
            public TideDebugMode debugMode = TideDebugMode.Normal;
        }

        public BubbleTideSettings settings = new BubbleTideSettings();

        [Tooltip("留空则运行时自动创建（Shader 路径：PAO/BubbleTide）。")]
        public Material material;

        private BubbleTidePass m_Pass;
        private Material m_RuntimeMaterial;

        public override void Create()
        {
            Shader shader = Shader.Find("PAO/BubbleTide");
            if (shader == null)
            {
                Debug.LogError("[泡泡潮] 找不到 Shader \"PAO/BubbleTide\"，" +
                               "确认 Assets/GameMain/Art/Shaders/BubbleTide.shader 存在且能编译。");
                return;
            }

            if (material != null && material.shader == shader)
            {
                m_RuntimeMaterial = material;
            }
            else
            {
                if (material != null)
                {
                    Debug.LogWarning("[泡泡潮] 指定的材质用的不是 PAO/BubbleTide，已改用自动创建的实例。");
                }

                m_RuntimeMaterial = CoreUtils.CreateEngineMaterial(shader);
                m_RuntimeMaterial.name = "BubbleTide (Runtime)";
            }

            m_Pass = new BubbleTidePass(m_RuntimeMaterial, settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_Pass == null || m_RuntimeMaterial == null)
            {
                return;
            }

            // 预览相机不参与，免得在材质预览 / 反射探针里也跑一遍全屏步进
            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
            {
                return;
            }

            // 场景里没有潮水总控，或者场还没烤好 —— 直接跳过，省掉一次全屏
            if (!BubbleTidePass.HasTideData())
            {
                return;
            }

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
            {
                CoreUtils.Destroy(m_RuntimeMaterial);
            }

            m_RuntimeMaterial = null;
        }
    }
}
