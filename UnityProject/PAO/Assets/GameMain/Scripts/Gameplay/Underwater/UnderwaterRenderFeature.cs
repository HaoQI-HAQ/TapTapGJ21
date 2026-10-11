using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PAO.Underwater
{
    /// <summary>
    /// 把水下效果注入 URP。
    ///
    /// 安装方式（二选一）：
    ///   · 菜单「工具 → PAO → 水下 → 安装到 URP Renderer」—— 一键装好
    ///   · 或手动：选中 URP Renderer 资产 → Add Renderer Feature → Underwater Render Feature
    ///
    /// 只需要装一个。场景里没有 WaterVolume 时 Pass 会自己跳过，不会白跑一次全屏。
    ///
    /// -----------------------------------------------------------------------
    /// 【这个 Feature 只负责「画」】
    ///
    /// 水面高度、水色、焦散强度、光柱方向……全在场景里的 **WaterVolume** 上。
    /// 这里只有「注入到哪、用多高分辨率、哪几段要开」这些和渲染管线有关的参数。
    ///
    /// 所以：
    ///   · 调观感 → 改场景里 WaterVolume 的 Inspector
    ///   · 调性能 → 改这里（以及 WaterVolume 上的步数）
    ///
    /// 两边打架的情况只有一种：**这里关掉了某一段**，那 WaterVolume 上对应的
    /// 参数就完全没作用了。所以关之前先想清楚是在调性能还是在调观感。
    /// -----------------------------------------------------------------------
    /// </summary>
    [DisallowMultipleRendererFeature("Underwater Render Feature")]
    public sealed class UnderwaterRenderFeature : ScriptableRendererFeature
    {
        /// <summary>和渲染管线有关的参数。观感参数不在这里，在场景的 WaterVolume 上。</summary>
        [Serializable]
        public class UnderwaterSettings
        {
            /// <summary>
            /// 注入时机的**硬上限**。
            ///
            /// 【为什么有这条线】URP 里 RenderPassEvent 的实际数值是：
            ///   AfterRenderingOpaques            = 300
            ///   BeforeRenderingSkybox            = 350
            ///   BeforeRenderingTransparents      = 450
            ///   AfterRenderingTransparents       = 500
            ///   AfterRenderingPostProcessing     = **600**
            ///
            /// 超过 600 之后，相机的颜色目标已经交还给渲染管线，
            /// cameraColorTargetHandle 会开始返回 null —— 那一段就画不出来了。
            ///
            /// 这个值不是估计的，是从 URP 源码的 ScriptableRenderPass.cs 里
            /// 读出来的。之前把体积光设在 560，看着离 600 还有余量，
            /// 实际已经踩线，表现是 Console 报「拿不到相机颜色目标」。
            /// 所以现在由 ClampEvent 强制夹住，不让它有机会配错。
            /// </summary>
            public const RenderPassEvent k_MaxEvent = RenderPassEvent.AfterRenderingTransparents + 90;   // 590

            [Header("注入时机")]
            [Tooltip("焦散那一趟的注入时机。\n" +
                     "默认 BeforeRenderingSkybox(350) —— 不透明几何体已经画完、深度也就绪，\n" +
                     "但天空盒和透明物体还没画。焦散正好该长在这时候的几何体上。\n" +
                     "⚠️ 改成 AfterRenderingTransparents(500) 会让焦散穿透泡泡和玻璃。")]
            public RenderPassEvent causticsEvent = RenderPassEvent.BeforeRenderingSkybox;

            [Tooltip("焦散合成那一趟。必须紧跟焦散，中间不能插进别的东西。")]
            public RenderPassEvent causticsCompositeEvent = RenderPassEvent.BeforeRenderingSkybox;

            [Tooltip("雾 + 焦散补色。默认 AfterRenderingTransparents + 50 = 550。\n" +
                     "必须排在泡泡（500）之后，否则泡泡会是「泡在干净空气里」，\n" +
                     "而画面其他地方都是雾，一眼穿帮。\n" +
                     "这一趟跑**全分辨率**，因为它很便宜（一次深度采样 + 一个 lerp）。")]
            public RenderPassEvent compositeEvent = RenderPassEvent.AfterRenderingTransparents + 50;

            [Tooltip("体积光。默认 AfterRenderingTransparents + 30 = 530。\n" +
                     "排在雾（550）之前，所以光柱会被雾淡化一点点 —— 这更真实，\n" +
                     "光在浑水里本来就该衰减。\n" +
                     "它跑在**半分辨率**上：光柱是十几米厚的体积，半分辨率看不出差别，\n" +
                     "但能省掉一半的步进算力。\n" +
                     "⚠️ 不要设到 590 以上，超过之后相机颜色目标会失效（见 k_MaxEvent）。")]
            public RenderPassEvent godRayEvent = RenderPassEvent.AfterRenderingTransparents + 30;

            [Header("性能")]
            [Tooltip("焦散中间纹理的分辨率除数。2 = 半分辨率。\n" +
                     "焦散花纹本身是低频的，半分辨率几乎看不出差别。")]
            [Range(1, 4)] public int causticsResolutionDivisor = 2;

            [Tooltip("体积光的分辨率除数。2 = 半分辨率，**建议不要改成 1**：\n" +
                     "算力翻倍，而观感几乎没有区别。真嫌卡应该先去\n" +
                     "WaterVolume 上把「体积光步数」调小。")]
            [Range(1, 4)] public int godRayResolutionDivisor = 2;

            [Tooltip("关掉后完全不算焦散，也不做那两趟 Blit。\n" +
                     "注意：关掉这里之后，场景 WaterVolume 上的焦散参数就全失效了。")]
            public bool causticsEnabled = true;

            [Tooltip("关掉后不做深度雾和焦散补色。\n" +
                     "关掉它画面会立刻变得「干」—— 远处不再发蓝，失去了在水里的感觉。\n" +
                     "要压帧率请优先关体积光，不要关这个。")]
            public bool fogEnabled = true;

            [Tooltip("关掉后不做体积光（斜着照下来的光柱）。\n" +
                     "⚠️ 这是整套里最贵的一项，要压帧率第一个关它 —— \n" +
                     "但更推荐先去 WaterVolume 上把「体积光步数」调小（14 → 8），\n" +
                     "那样还能保住光柱的形态。")]
            public bool godRaysEnabled = true;

            [Header("调试")]
            [Tooltip("可视化中间结果。排查「效果没出来」用，正常保持「关」。\n" +
                     "\n" +
                     "焦散看不见有五种完全不同的原因，画面上表现却一模一样：\n" +
                     "  焦散量        → 应该看到一张会动的亮网。全黑 = 图案没算出来\n" +
                     "  表面匹配度    → 白 = 这个像素还是几何表面。全黑 = 被透明物体盖住\n" +
                     "  视距          → 应该是随距离变亮的灰。不对 = 深度读错了\n" +
                     "  世界坐标格子  → 应该看到规则格子。糊掉/条纹 = 世界坐标重建错了\n" +
                     "  水面光柱强度  → 体积光那趟的原始值。应该看到水面上的亮网\n" +
                     "\n" +
                     "背景用深灰而不是纯黑 —— 这样「没算出来」和「算出来是 0」能区分开。")]
            public UnderwaterDebugMode debugMode = UnderwaterDebugMode.Off;

            [Tooltip("在 Console 里打印每帧的水下参数（水面高度、相机是否在水下）。\n" +
                     "只在排查「效果没出来」时开，正常关掉 —— 它会每帧刷屏。")]
            public bool debugLogState = false;

            /// <summary>某一段该注入到哪。四段各自独立，改一个不影响另三个。</summary>
            public RenderPassEvent GetPassEvent(UnderwaterPass.Stage stage)
            {
                switch (stage)
                {
                    case UnderwaterPass.Stage.Caustics:
                        return ClampEvent(causticsEvent, "焦散");

                    case UnderwaterPass.Stage.CausticsComposite:
                        return ClampEvent(causticsCompositeEvent, "焦散合成");

                    case UnderwaterPass.Stage.GodRays:
                        return ClampEvent(godRayEvent, "体积光");

                    default:
                        return ClampEvent(compositeEvent, "深度雾");
                }
            }

            /// <summary>
            /// 把注入时机夹进安全范围。
            ///
            /// 【为什么要夹而不是只在文档里写】
            ///   设错这个值的后果是「那一段静默地不画」，不崩溃、不报错
            ///   （只会有一条我加的诊断，而且它本身也可能被忽略）。
            ///   与其指望下一个人读过注释，不如让错值根本传不进去。
            ///
            ///   夹的时候会在 Console 里说清楚是被夹了，不会让人以为设置生效了。
            /// </summary>
            public static RenderPassEvent ClampEvent(RenderPassEvent value, string label)
            {
                if (value <= k_MaxEvent)
                {
                    return value;
                }

                Debug.LogWarning(string.Format(
                    "[水下] {0} 的注入时机设成了 {1}（数值 {2}），超过了安全上限 " +
                    "{3}（数值 {4}）。\n" +
                    "超过这个点之后相机的颜色目标已经交还给渲染管线，这一段画不出来。\n" +
                    "已自动夹到上限。",
                    label, value, (int)value, k_MaxEvent, (int)k_MaxEvent));

                return k_MaxEvent;
            }
        }

        public UnderwaterSettings settings = new UnderwaterSettings();

        [Tooltip("留空则运行时自动创建（Shader 路径：PAO/UnderwaterFullscreen）。\n" +
                 "只影响雾和焦散那三段 —— 体积光用的是另一个 shader。")]
        public Material material;

        [Tooltip("留空则运行时自动创建（Shader 路径：PAO/UnderwaterGodRays）。")]
        public Material godRayMaterial;

        private UnderwaterPass m_CausticsPass;
        private UnderwaterPass m_CausticsCompositePass;
        private UnderwaterPass m_CompositePass;
        private UnderwaterPass m_GodRayPass;

        private Material m_RuntimeMaterial;
        private Material m_RuntimeGodRayMaterial;

        // 「这个材质是我建的」—— 决定 Dispose 时该不该销毁它。
        private bool m_OwnsRuntimeMaterial;
        private bool m_OwnsGodRayMaterial;

        // 场景里那个 WaterVolume。每帧 FindObjectOfType 在编辑器里是几千次
        // 场景遍历，所以缓存下来；场景换掉 / 被删掉时靠 null 检查重建。
        private WaterVolume m_Water;

        private int m_LastLookupFrame = -1;

        public override void Create()
        {
            m_RuntimeMaterial = ResolveMaterial(material,
                                                "PAO/UnderwaterFullscreen",
                                                "UnderwaterFullscreen (Runtime)",
                                                out m_OwnsRuntimeMaterial);

            m_RuntimeGodRayMaterial = ResolveMaterial(godRayMaterial,
                                                      "PAO/UnderwaterGodRays",
                                                      "UnderwaterGodRays (Runtime)",
                                                      out m_OwnsGodRayMaterial);

            if (m_RuntimeMaterial == null)
            {
                return;
            }

            m_CausticsPass          = new UnderwaterPass(m_RuntimeMaterial, m_RuntimeGodRayMaterial,
                                                         settings, UnderwaterPass.Stage.Caustics);

            m_CausticsCompositePass = new UnderwaterPass(m_RuntimeMaterial, m_RuntimeGodRayMaterial,
                                                         settings, UnderwaterPass.Stage.CausticsComposite);

            m_CompositePass         = new UnderwaterPass(m_RuntimeMaterial, m_RuntimeGodRayMaterial,
                                                         settings, UnderwaterPass.Stage.Composite);

            m_GodRayPass            = new UnderwaterPass(m_RuntimeMaterial, m_RuntimeGodRayMaterial,
                                                         settings, UnderwaterPass.Stage.GodRays);
        }

        /// <summary>
        /// 找 shader 建一个运行时材质。用户自己指定了、且 shader 对得上就用他的，
        /// 否则建一个自己的实例 —— 和泡泡 SDF / 泡泡潮两套是同一个套路。
        ///
        /// ownsInstance 用来记住「这个材质是我建的」，Dispose 时才知道该不该销毁。
        /// 不记的话要么泄漏，要么把用户指定的材质销毁掉（后者更糟：
        /// 资产被删掉，而且只在退出 Play 时才发生，很容易漏掉）。
        /// </summary>
        private static Material ResolveMaterial(Material userMaterial,
                                                string shaderName,
                                                string instanceName,
                                                out bool ownsInstance)
        {
            ownsInstance = false;

            Shader shader = Shader.Find(shaderName);

            if (shader == null)
            {
                Debug.LogError("[水下] 找不到 Shader \"" + shaderName + "\"。" +
                               "确认 Assets/GameMain/Art/Shaders/ 下对应的 .shader " +
                               "存在且能编译。");
                return null;
            }

            if (userMaterial != null && userMaterial.shader == shader)
            {
                return userMaterial;
            }

            if (userMaterial != null)
            {
                Debug.LogWarning("[水下] 为 \"" + shaderName + "\" 指定的材质用的不是这个 shader，" +
                                 "已改用自动创建的实例。");
            }

            Material runtime = CoreUtils.CreateEngineMaterial(shader);
            runtime.name = instanceName;

            ownsInstance = true;
            return runtime;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_RuntimeMaterial == null)
            {
                return;
            }

            // 预览相机不参与 —— 免得在材质预览球和反射探针里也跑一遍全屏
            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
            {
                return;
            }

            // 场景里没有 WaterVolume —— 直接跳过，省掉三趟全屏。
            // 这跟泡泡潮那边「没潮水就不跑」是同一个约定：
            // 装了 Feature 但没有对应场景物体时，画面完全不受影响。
            if (!TryGetWater(out WaterVolume water))
            {
                return;
            }

            // 参数是 WaterVolume 每帧推的全局变量。这里再推一次，
            // 是为了保证「渲染用的值」和「这一帧 Inspector 里的值」严格一致 ——
            // 编辑器里暂停、拖动时间轴的时候，Update 不一定跑。
            water.PushGlobals();

            // 调试模式跟 WaterVolume 无关（它是渲染管线的排查开关，不是观感参数），
            // 所以不放进 PushGlobals，在这里单独推。
            Shader.SetGlobalFloat(UnderwaterShaderIDs.DebugMode, (float)settings.debugMode);

            if (settings.debugLogState)
            {
                Camera cam = renderingData.cameraData.camera;
                float above = cam.transform.position.y - water.waterLevel;

                // 和 shader 里 PAO_UnderwaterAmount、WaterVolume 上的公式是同一个：
                //   amount = 1 - smoothstep(-fadeInDepth, +fadeOutAbove, above)
                // 写成一样不是偷懒 —— 两边不一致的话这个调试输出就没意义了。
                float t = Mathf.InverseLerp(-water.fadeInDepth, water.fadeOutAbove, above);
                float amount = (1f - Mathf.SmoothStep(0f, 1f, t)) * water.underwaterStrength;

                Debug.Log(string.Format(
                    "[水下] 相机 {0}  y={1:F2}  水面 y={2:F2}  高度差 {3:F2}  " +
                    "水下程度 {4:F2}  {5}",
                    cam.name,
                    cam.transform.position.y,
                    water.waterLevel,
                    above,
                    amount,
                    water.IsCameraUnderwater(cam) ? "水下" : "水上"));
            }

            m_CausticsPass.SyncSettings();
            m_CausticsCompositePass.SyncSettings();
            m_CompositePass.SyncSettings();
            m_GodRayPass.SyncSettings();

            // 【顺序很重要】
            //   焦散必须先算完，合成才能读到中间纹理。
            //   雾必须排在焦散合成之后 —— 焦散合成会重绑相机目标，
            //   顺序错了雾就作用在中间纹理上了。
            //   EnqueuePass 按调用顺序执行，所以这里就是执行顺序。
            //
            // 【开关放在这里而不是 Pass 内部】
            //   关掉的 Pass 干脆不入队，比让它进队列再自己 return 干净 ——
            //   少一次 Configure + Execute 的空转，Profiler 里也不会多出
            //   一条永远不干活的记录。Pass 内部仍然各有一份同样的判断，
            //   那是兜底：万一以后有人从别处 Enqueue，行为依然正确。
            if (settings.causticsEnabled)
            {
                renderer.EnqueuePass(m_CausticsPass);
                renderer.EnqueuePass(m_CausticsCompositePass);
            }

            if (settings.fogEnabled)
            {
                renderer.EnqueuePass(m_CompositePass);
            }

            if (settings.godRaysEnabled)
            {
                renderer.EnqueuePass(m_GodRayPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            m_CausticsPass?.Dispose();
            m_CausticsCompositePass?.Dispose();
            m_CompositePass?.Dispose();
            m_GodRayPass?.Dispose();

            m_CausticsPass = null;
            m_CausticsCompositePass = null;
            m_CompositePass = null;
            m_GodRayPass = null;

            // 只有自己建的才销毁；用户指定的材质归用户管。
            // 【这里必须分开记】早期的写法是「m_RuntimeMaterial != material」，
            // 那样在「用户给了材质但 shader 不对 → 我们建了替代品」这条路径上
            // 会漏掉销毁，泄漏一个材质。用 owns 标志就没有这个边角。
            if (m_OwnsRuntimeMaterial && m_RuntimeMaterial != null)
            {
                CoreUtils.Destroy(m_RuntimeMaterial);
            }

            if (m_OwnsGodRayMaterial && m_RuntimeGodRayMaterial != null)
            {
                CoreUtils.Destroy(m_RuntimeGodRayMaterial);
            }

            m_RuntimeMaterial = null;
            m_RuntimeGodRayMaterial = null;
            m_OwnsRuntimeMaterial = false;
            m_OwnsGodRayMaterial = false;

            m_Water = null;
        }

        /// <summary>取场景里的 WaterVolume，带缓存。</summary>
        private bool TryGetWater(out WaterVolume water)
        {
            // 缓存里的对象被销毁了（换场景、手动删掉）时重建。
            // Unity 的「假 null」会在这里正确地判定为 null，不用额外操心。
            if (m_Water != null)
            {
                water = m_Water;
                return true;
            }

            // 每帧最多找一次。多个相机时后面的相机直接用前一帧的结果 —— 
            // 这一帧内场景不会变，所以是对的。
            if (m_LastLookupFrame == Time.frameCount)
            {
                water = null;
                return false;
            }

            m_LastLookupFrame = Time.frameCount;
            m_Water = UnityEngine.Object.FindObjectOfType<WaterVolume>();

            water = m_Water;
            return water != null;
        }
    }
}
