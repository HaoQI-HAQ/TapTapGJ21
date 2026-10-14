using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 爆炸泡泡的特效：一次"水彩洇开"式的爆开。
    ///
    /// 【表现设计】
    ///   1. 冲击闪白   —— 极短的一下亮光，给爆发感定节奏
    ///   2. 爆开外壳   —— 原泡泡尺寸的球壳向外撑开并化开（泡泡破了的那一层）
    ///   3. 水彩彩块   —— 主视觉：一团圆块向外飞散，边缘带水痕、一直蠕动
    ///   4. 飞溅液滴   —— 快速往外甩的小点，拉出方向感
    ///
    /// 【为什么用代码建，不做预制体】
    /// 场景文件（.unity）和预制体都是带自动分配 ID 的 YAML，改起来容易踩坑
    /// （引用错位、合并冲突）。这个特效结构固定、参数都在代码里，
    /// 运行时建出来更可靠，也不用往每个关卡场景里塞东西。
    ///
    /// 【风格对齐】
    /// 配色与 PAO/BubbleBall 同一套：冰蓝底 + 青/粉彩虹，符合策划案 §10.2
    /// 的"粉紫基调 + 手绘厚涂"。彩块着色器在
    /// Assets/GameMain/Art/Shaders/BubbleBurstSplash.shader。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Bubble Burst VFX")]
    public sealed class BubbleBurstVFX : MonoBehaviour
    {
        // ---- 爆炸配色：草莓色系 ----
        //
        // 早先用的是冰蓝底 + 青/粉（照搬 BubbleBall 的 _BaseColor / _IridColorA），
        // 但那让爆炸和泡泡本身一个颜色，炸开时看不出"这里发生了变化"。
        // 现在改成草莓色系：更饱和、更偏红，和青蓝的水下环境形成对比，爆炸才有力度。
        // 色相仍落在策划案 §10.2 的"粉紫基调"里，不破坏整体色板。

        /// <summary>
        /// 默认爆炸主色：草莓色。
        /// 对应世界观里"记忆泡泡"的粉色系，也和 BubbleBall._IridColorB 同色相，但更偏红。
        /// </summary>
        private static readonly Color kStrawberry     = new Color(1.00f, 0.45f, 0.60f, 1f);

        /// <summary>草莓色的亮光（偏白粉，用来做高光边）</summary>
        private static readonly Color kStrawberryGlow = new Color(1.00f, 0.86f, 0.90f, 1f);

        /// <summary>
        /// 爆炸粒子用的着色器。
        ///
        /// 【为什么从 BubbleBurstSplash 换成 BubbleCircle】
        /// 前者用噪声去"咬"圆形的边，但没有保证基础圆形 ——
        /// 粒子方块的四角会露出来，看起来就是一片片粉紫色的三角形。
        /// BubbleCircle 用径向衰减打底（圆外直接 clip），
        /// 无论参数怎么调都一定是个圆。
        /// </summary>
        private const string kSplashShader = "PAO/BubbleCircle";

        [Header("子系统开关（调试用）")]
        [Tooltip("爆开外壳：原泡泡尺寸的球壳向外撑开")]
        [SerializeField] private bool m_UseShell = true;

        [Tooltip("水彩彩块：主视觉")]
        [SerializeField] private bool m_UseSplash = true;

        [Tooltip("飞溅液滴：往外甩的小点")]
        [SerializeField] private bool m_UseSpray = true;

        private static Material s_SplashMaterial;

        /// <summary>
        /// 在指定位置播一次爆炸特效。
        ///
        /// 【尺寸由谁决定 —— 这里是关键】
        /// 特效大小**只看泡泡直径**，不看爆炸半径。
        /// 之前是按 blastRadius（固定 3 米）推尺寸，结果无论泡泡吹多小，
        /// 爆炸画面都一样大 —— 玩家感觉不到"我把泡泡吹大了"。
        ///
        /// 现在：吹得小的泡泡 → 紧凑的一小簇；吹满的大泡泡 → 铺开的整团。
        /// 爆炸半径仍然只用于**物理推力**，两者不再混在一起。
        /// </summary>
        /// <param name="position">爆炸中心（世界坐标）</param>
        /// <param name="bubbleDiameter">原泡泡直径（米）。这是决定特效大小的唯一依据</param>
        /// <param name="tint">主色调。传 null 用默认草莓色</param>
        /// <param name="sizeReference">"满蓄力"的直径参考值，用来把泡泡大小归一化。0 则内部估一个</param>
        public static BubbleBurstVFX Spawn(Vector3 position, float bubbleDiameter,
                                           Color? tint = null,
                                           float sizeReference = 0f)
        {
            if (bubbleDiameter <= 0.001f)
            {
                bubbleDiameter = 0.6f;
            }

            if (sizeReference <= 0.001f)
            {
                // 没给参考值就按默认满蓄力直径估（BubbleSettings.maxSize 的默认值）
                sizeReference = 1.5f;
            }

            // 视觉缩放 = 泡泡直径 / 满蓄力直径。
            //
            // 直接相除，不做"归一化再插值"那一圈 —— 那样会绕回去，
            // 结果 1.5 米的满蓄力泡泡反而不是 1.0 倍，比例对不上。
            // 现在：满蓄力 = 1.0 倍（基准），最小泡泡（0.2 米）≈ 0.13 倍，吹得越大炸得越大。
            // 下限 0.25 是为了别小到看不见；上限 2 是为了别炸到满屏。
            float visualScale = Mathf.Clamp(bubbleDiameter / sizeReference, 0.25f, 2f);

            var root = new GameObject("Bubble Burst VFX");
            root.transform.position = position;

            var fx = root.AddComponent<BubbleBurstVFX>();
            fx.Build(bubbleDiameter, visualScale, tint ?? kStrawberry);

            return fx;
        }

        // ==================== 构建 ====================

        private float m_LifeTime;
        private float m_Elapsed;

        /// <param name="bubbleDiameter">泡泡直径，决定所有子系统的基准尺寸</param>
        /// <param name="visualScale">整体缩放，= 泡泡直径 / 满蓄力直径</param>
        /// <param name="tint">主色调</param>
        private void Build(float bubbleDiameter, float visualScale, Color tint)
        {
            Material splash = GetSplashMaterial();

            float splashLife = 0.85f;
            float shellLife = 0.55f;
            float sprayLife = 0.45f;

            // 整个特效的存活时间 = 最长的那个子系统 + 一点余量
            m_LifeTime = Mathf.Max(splashLife, Mathf.Max(shellLife, sprayLife)) + 0.35f;

            // ---------- 1. 爆开外壳（原泡泡那一层皮）----------
            if (m_UseShell)
            {
                BuildShell(bubbleDiameter, visualScale, shellLife);
            }

            // ---------- 2. 水彩彩块（主视觉）----------
            if (m_UseSplash)
            {
                BuildSplash(bubbleDiameter, visualScale, splashLife, tint, splash);
            }

            // ---------- 3. 飞溅液滴 ----------
            if (m_UseSpray)
            {
                BuildSpray(bubbleDiameter, visualScale, sprayLife, tint);
            }
        }

        /// <summary>原泡泡尺寸的球壳向外撑开 —— 表现"这颗泡泡破了"。</summary>
        private void BuildShell(float bubbleDiameter, float visualScale, float life)
        {
            ParticleSystem ps = NewSystem("Shell", 0.7f);

            float d = bubbleDiameter * visualScale;

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = d * 0.50f;
            main.startSize = d * 1.7f;                  // 出生时差不多就是泡泡本身大小
            main.startColor = new ParticleSystem.MinMaxGradient(kStrawberry, kStrawberryGlow);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;              // 轻微上浮，和水下感一致

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.5f;
            shape.radiusThickness = 1f;                 // 只在球壳上生成，不填满内部

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

            // 球壳用"空心圆"（coreSolid 越大中心越空，看得出来是"壳"）
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeMaterial(
                tint: kStrawberry,
                glowColor: kStrawberryGlow,
                radius: 1.0f,
                edgeSoftness: 0.55f,
                coreSolid: 0.55f,
                glowStrength: 1.5f,
                glowWidth: 0.34f,
                opacity: 0.85f);

            // 从泡泡尺寸放大到约 1.6 倍，然后收住
            ApplySizeCurve(ps, 1f, 1.6f);
            ApplyAlphaCurve(ps, new[] { (0f, 0.9f), (0.55f, 0.5f), (1f, 0f) });
        }

        /// <summary>主视觉：一团水彩彩块向外飞散，边一直在蠕动。</summary>
        private void BuildSplash(float bubbleDiameter, float visualScale,
                                 float life, Color tint, Material material)
        {
            ParticleSystem ps = NewSystem("Splash", 1.1f);

            // 所有尺寸/速度都以泡泡直径为基准，再乘整体缩放 ——
            // 这样"吹得小 = 炸得紧凑，吹得大 = 炸得铺开"
            float d = bubbleDiameter * visualScale;

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = d * 1.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(d * 0.42f, d * 0.80f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(tint, kStrawberryGlow);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.02f;               // 水里几乎不下沉
            main.maxParticles = 220;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.06f;                   // 从中心喷出

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            // 两波：一波瞬间爆开，一波稍后跟上，形成层次
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, 22),
                new ParticleSystem.Burst(0.07f, 14),
            });

            // 阻力让它们飞出去后迅速慢下来 —— 水里不该一直冲
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.32f;
            limit.limit = d * 1.6f;

            ParticleSystem.RotationOverLifetimeModule rotate = ps.rotationOverLifetime;
            rotate.enabled = true;
            rotate.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ApplySizeCurve(ps, 0.55f, 1.9f);
            ApplyAlphaCurve(ps, new[] { (0f, 1f), (0.35f, 0.85f), (1f, 0f) });
        }

        /// <summary>快速往外甩的小点，拉出方向感。</summary>
        private void BuildSpray(float bubbleDiameter, float visualScale, float life, Color tint)
        {
            ParticleSystem ps = NewSystem("Spray", 0.9f);

            float d = bubbleDiameter * visualScale;

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(d * 1.9f, d * 3.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(d * 0.07f, d * 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(kStrawberryGlow, tint);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.12f;               // 比彩块重一点，会往下坠

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.04f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.5f;

            // 小液滴用接近实心的圆，飞出去才看得见
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeMaterial(
                tint: kStrawberryGlow,
                glowColor: Color.white,
                radius: 1.0f,
                edgeSoftness: 0.30f,
                coreSolid: 0.10f,          // 几乎是实心圆
                glowStrength: 0.7f,
                glowWidth: 0.22f,
                opacity: 1f);

            ApplySizeCurve(ps, 1f, 0.35f);   // 飞出去越来越小
            ApplyAlphaCurve(ps, new[] { (0f, 1f), (0.6f, 0.7f), (1f, 0f) });
        }

        // ==================== 粒子系统基础设置 ====================

        /// <summary>
        /// 建一个配好公共项的粒子系统。
        ///
        /// 公共项：世界空间模拟、不循环、播完销毁、按 Z 轴面向摄像机
        /// （2D 彩块要一直正对镜头，不然侧着看会变成一条线）。
        /// </summary>
        private ParticleSystem NewSystem(string name, float duration)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;   // 统一由本组件销毁

            // 彩块是 2D 的，必须一直正对镜头
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortingFudge = -1f;      // 稍微往前一点，避免被场景挡住
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // 关掉用不到的模块，省一点开销
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;   // 下面统一设曲线

            ps.Play();
            return ps;
        }

        /// <summary>尺寸随时间变化：出生 → 峰值 → 收尾。</summary>
        private static void ApplySizeCurve(ParticleSystem ps, float startScale, float peakScale)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;

            var curve = new AnimationCurve(
                new Keyframe(0f, startScale),
                new Keyframe(0.28f, peakScale),
                new Keyframe(1f, peakScale * 0.85f));

            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>透明度随时间变化。传入 (时间比例, 透明度) 序列。</summary>
        private static void ApplyAlphaCurve(ParticleSystem ps, (float t, float a)[] keys)
        {
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;

            var gradient = new Gradient();
            var colorKeys = new GradientColorKey[keys.Length];
            var alphaKeys = new GradientAlphaKey[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                colorKeys[i] = new GradientColorKey(Color.white, keys[i].t);
                alphaKeys[i] = new GradientAlphaKey(keys[i].a, keys[i].t);
            }

            gradient.SetKeys(colorKeys, alphaKeys);
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        // ==================== 材质 ====================

        /// <summary>取共用的主材质（所有爆炸共享一份，省内存）。</summary>
        private static Material GetSplashMaterial()
        {
            if (s_SplashMaterial != null)
            {
                return s_SplashMaterial;
            }

            s_SplashMaterial = MakeMaterial(
                tint: kStrawberry,
                glowColor: kStrawberryGlow,
                radius: 1.0f,
                edgeSoftness: 0.42f,
                coreSolid: 0.35f,
                glowStrength: 1.2f,
                glowWidth: 0.30f,
                opacity: 1f);

            return s_SplashMaterial;
        }

        /// <summary>
        /// 造一个 PAO/BubbleCircle 材质。
        ///
        /// 找不到着色器时返回 null —— 粒子系统会退回默认材质，
        /// 效果不理想但不会崩，也不会把游戏卡死。
        /// </summary>
        private static Material MakeMaterial(Color tint, Color glowColor,
                                             float radius, float edgeSoftness, float coreSolid,
                                             float glowStrength, float glowWidth, float opacity)
        {
            Shader shader = Shader.Find(kSplashShader);

            if (shader == null)
            {
                Debug.LogWarning("[BubbleBurstVFX] 找不到着色器 " + kSplashShader +
                                 "，爆炸会退回默认粒子材质。请确认 " +
                                 "Assets/GameMain/Art/Shaders/BubbleCircle.shader 存在且编译通过。");
                return null;
            }

            var mat = new Material(shader);
            mat.name = "BubbleBurst Circle (Runtime)";
            mat.hideFlags = HideFlags.DontSave;

            SetColor(mat, "_Tint", tint);
            SetColor(mat, "_GlowColor", glowColor);
            SetFloat(mat, "_Opacity", opacity);
            SetFloat(mat, "_Radius", radius);
            SetFloat(mat, "_EdgeSoftness", edgeSoftness);
            SetFloat(mat, "_CoreSolid", coreSolid);
            SetFloat(mat, "_GlowWidth", glowWidth);
            SetFloat(mat, "_WobbleAmp", 0.06f);    // 只做轻微扰动，保证还是圆
            SetFloat(mat, "_WobbleSpeed", 2.0f);

            // ---- 对齐角色的清透 PBR 画风 ----
            //
            // 角色的 ModelPBR.shader 走 URP 标准 PBR，立体感是**被场景照出来的**：
            // 环境光来自 Light Probe / 天空盒，主光来自场景方向光。
            //
            // 所以这里不再自己写死光方向（那样只能在特定光照下碰巧像，
            // 换关卡或进水下就会和角色脱节），改成：
            //   着色器里 SampleSH(n) 采样环境、GetMainLight() 取主光，
            //   这里只调"强度"和"通透感"。
            //
            // 四条一起用才像：
            //   1. 环境光给足 —— 角色在晴空下背光面也不黑
            //   2. 主光压低 —— 环境光主导才是那套画风（不是硬阴影）
            //   3. 次表面散射感 —— 半透明软体，光会透过去，边缘发亮
            //   4. 大片柔光高光 —— 低粗糙度的光滑表面
            SetFloat(mat, "_GlowStrength", glowStrength * 0.35f);
            SetFloat(mat, "_SphereAmount", 1.0f);      // 厚度方向用满，明暗才连续
            SetFloat(mat, "_ToonSteps", 1.0f);         // 1 = 完全平滑，不做色阶
            SetFloat(mat, "_ShadowSoft", 0.62f);       // 明暗过渡拉宽 = 化开
            SetFloat(mat, "_AmbientBoost", 1.35f);     // 环境光主导
            SetFloat(mat, "_DirectBoost", 0.50f);      // 主光只做辅助
            SetFloat(mat, "_Transmission", 0.60f);     // 半透明软体的透光感
            SetFloat(mat, "_GrainStrength", 0.20f);    // 笔触强度
            SetFloat(mat, "_GrainScale", 20.0f);       // 笔触粗细
            SetFloat(mat, "_GrainStretch", 1.45f);     // 沿一个方向拉伸 = 运笔方向

            // 光照方向和暗部颜色**不再由这里指定**。
            //
            // 着色器直接采样场景的环境光（SampleSH）和主光（GetMainLight），
            // 和角色拿到的是同一份数据 —— 场景光照变了，爆炸会跟着一起变。
            // 早先这里写死过一个光方向，那样只能在特定光照下"碰巧像"，
            // 换个关卡或进水下就会和角色脱节。

            // 镜面高光：角色表面是光滑的（低粗糙度），
            // 所以高光是一片渐隐的大柔光，不是小块实心白
            SetColor(mat, "_SpecColor", Color.white);
            SetFloat(mat, "_SpecStrength", 1.25f);
            SetFloat(mat, "_SpecPower", 24f);      // 值越小光斑越宽
            SetVector(mat, "_SpecOffset", new Vector4(-0.18f, 0.20f, 0f, 0f));

            // 边缘透光：角色边缘有一圈柔和的亮，这里对应上
            SetColor(mat, "_RimColor", new Color(1f, 0.94f, 0.95f, 1f));
            SetFloat(mat, "_RimStrength", 0.65f);
            SetFloat(mat, "_RimPower", 2.6f);

            return mat;
        }

        /// <summary>安全设值：着色器里没这个属性时跳过，不报错。</summary>
        private static void SetColor(Material mat, string name, Color value)
        {
            if (mat != null && mat.HasProperty(name))
            {
                mat.SetColor(name, value);
            }
        }

        private static void SetFloat(Material mat, string name, float value)
        {
            if (mat != null && mat.HasProperty(name))
            {
                mat.SetFloat(name, value);
            }
        }

        private static void SetVector(Material mat, string name, Vector4 value)
        {
            if (mat != null && mat.HasProperty(name))
            {
                mat.SetVector(name, value);
            }
        }

        // ==================== 生命周期 ====================

        private void Update()
        {
            m_Elapsed += Time.deltaTime;

            if (m_Elapsed >= m_LifeTime)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // 运行时造的材质不会自动回收，这里自己清掉。
            // 注意共用材质 s_SplashMaterial 不清 —— 那是静态缓存，下次还要用。
            ParticleSystemRenderer[] renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Material mat = renderers[i].sharedMaterial;

                if (mat != null && mat != s_SplashMaterial)
                {
                    Destroy(mat);
                }
            }
        }
    }
}
