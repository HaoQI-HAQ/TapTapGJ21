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
        // ---- 项目风格色板（与 BubbleBall 的 _BaseColor / _IridColorA / _IridColorB 对应）----
        /// <summary>冰蓝底（泡泡主体色）</summary>
        private static readonly Color kIceBlue   = new Color(0.72f, 0.90f, 1.00f, 1f);

        /// <summary>彩虹色相 A：青（BubbleBall 的 _IridColorA）</summary>
        private static readonly Color kIridTeal  = new Color(0.45f, 0.95f, 0.85f, 1f);

        /// <summary>彩虹色相 B：粉（BubbleBall 的 _IridColorB，对应粉紫基调）</summary>
        private static readonly Color kIridPink  = new Color(1.00f, 0.62f, 0.92f, 1f);

        /// <summary>水痕亮边</summary>
        private static readonly Color kWetEdge   = new Color(0.88f, 0.98f, 1.00f, 1f);

        /// <summary>爆炸的暖色核心（一点点对比，免得整团都是冷色没有爆发感）</summary>
        private static readonly Color kFlashWarm = new Color(1.00f, 0.86f, 0.72f, 1f);

        private const string kSplashShader = "PAO/BubbleBurstSplash";

        private static Material s_SplashMaterial;

        /// <summary>
        /// 在指定位置播一次爆炸特效。
        /// </summary>
        /// <param name="position">爆炸中心（世界坐标）</param>
        /// <param name="radius">爆炸半径（米），由炸弹参数传进来，特效按它缩放</param>
        /// <param name="bubbleDiameter">原泡泡直径（米）。0 表示未知，按半径推算</param>
        /// <param name="tint">主色调。传 null 用默认冰蓝</param>
        public static BubbleBurstVFX Spawn(Vector3 position, float radius,
                                           float bubbleDiameter = 0f,
                                           Color? tint = null)
        {
            if (radius <= 0.001f)
            {
                radius = 1f;
            }

            if (bubbleDiameter <= 0.001f)
            {
                // 泡泡一般是爆炸半径的三分之一左右，没传就按这个估
                bubbleDiameter = radius * 0.66f;
            }

            var root = new GameObject("Bubble Burst VFX");
            root.transform.position = position;

            var fx = root.AddComponent<BubbleBurstVFX>();
            fx.Build(radius, bubbleDiameter, tint ?? kIceBlue);

            return fx;
        }

        // ==================== 构建 ====================

        private float m_LifeTime;
        private float m_Elapsed;

        private void Build(float radius, float bubbleDiameter, Color tint)
        {
            Material splash = GetSplashMaterial();

            float splashLife = 0.85f;
            float shellLife = 0.55f;
            float sprayLife = 0.45f;
            float flashLife = 0.16f;

            // 整个特效的存活时间 = 最长的那个子系统 + 一点余量
            m_LifeTime = Mathf.Max(splashLife, Mathf.Max(shellLife, sprayLife)) + 0.35f;

            // ---------- 1. 冲击闪白 ----------
            BuildFlash(radius, flashLife);

            // ---------- 2. 爆开外壳（原泡泡那一层皮）----------
            BuildShell(bubbleDiameter, shellLife);

            // ---------- 3. 水彩彩块（主视觉）----------
            BuildSplash(radius, splashLife, tint, splash);

            // ---------- 4. 飞溅液滴 ----------
            BuildSpray(radius, sprayLife, tint);
        }

        /// <summary>极短的一下亮光，给爆发定节奏。</summary>
        private void BuildFlash(float radius, float life)
        {
            ParticleSystem ps = NewSystem("Flash", 0.35f);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = radius * 1.9f;
            main.startColor = new ParticleSystem.MinMaxGradient(kFlashWarm, Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            // 用同一个彩块材质，但把核心撑满、边缘不化开 —— 看起来就是一团实心光
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeMaterial(
                Color.white, softness: 0.02f, corePower: 0.05f, rimStrength: 0.4f, opacity: 1f);

            ApplySizeCurve(ps, 1f, 2.6f);
            ApplyAlphaCurve(ps, new[] { (0f, 1f), (0.45f, 0.85f), (1f, 0f) });
        }

        /// <summary>原泡泡尺寸的球壳向外撑开 —— 表现"这颗泡泡破了"。</summary>
        private void BuildShell(float bubbleDiameter, float life)
        {
            ParticleSystem ps = NewSystem("Shell", 0.7f);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = bubbleDiameter * 0.55f;
            main.startSize = bubbleDiameter * 2.6f;     // 出生时几乎就是泡泡本身
            main.startColor = new ParticleSystem.MinMaxGradient(kIceBlue, kIridTeal);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;              // 轻微上浮，和水下感一致

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = bubbleDiameter * 0.5f;
            shape.radiusThickness = 1f;                 // 只在球壳上生成，不填满内部

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeMaterial(
                kIceBlue, softness: 0.32f, corePower: 0.30f, rimStrength: 1.5f, opacity: 0.85f);

            // 从泡泡尺寸放大到约 1.6 倍，然后收住
            ApplySizeCurve(ps, 1f, 1.6f);
            ApplyAlphaCurve(ps, new[] { (0f, 0.9f), (0.55f, 0.5f), (1f, 0f) });
        }

        /// <summary>主视觉：一团水彩彩块向外飞散，边一直在蠕动。</summary>
        private void BuildSplash(float radius, float life, Color tint, Material material)
        {
            ParticleSystem ps = NewSystem("Splash", 1.1f);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = radius * 1.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.55f, radius * 1.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(tint, kIridPink);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.02f;               // 水里几乎不下沉
            main.maxParticles = 220;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;                       // 从中心喷出

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
            limit.limit = radius * 1.6f;

            ParticleSystem.RotationOverLifetimeModule rotate = ps.rotationOverLifetime;
            rotate.enabled = true;
            rotate.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ApplySizeCurve(ps, 0.55f, 1.9f);
            ApplyAlphaCurve(ps, new[] { (0f, 1f), (0.35f, 0.85f), (1f, 0f) });
        }

        /// <summary>快速往外甩的小点，拉出方向感。</summary>
        private void BuildSpray(float radius, float life, Color tint)
        {
            ParticleSystem ps = NewSystem("Spray", 0.9f);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 2.2f, radius * 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.08f, radius * 0.20f);
            main.startColor = new ParticleSystem.MinMaxGradient(kWetEdge, tint);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.12f;               // 比彩块重一点，会往下坠

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.5f;

            // 小点用实心一点的材质，飞出去才看得见
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeMaterial(
                kWetEdge, softness: 0.12f, corePower: 0.22f, rimStrength: 0.6f, opacity: 1f);

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

            s_SplashMaterial = MakeMaterial(kIceBlue, 0.20f, 0.52f, 1.15f, 1f);
            return s_SplashMaterial;
        }

        /// <summary>
        /// 按参数造一个 PAO/BubbleBurstSplash 材质。
        ///
        /// 找不到着色器时返回 null —— 粒子系统会退回默认材质，
        /// 效果不理想但不会崩，也不会把游戏卡死。
        /// </summary>
        private static Material MakeMaterial(Color tint, float softness, float corePower,
                                             float rimStrength, float opacity)
        {
            Shader shader = Shader.Find(kSplashShader);

            if (shader == null)
            {
                Debug.LogWarning("[BubbleBurstVFX] 找不到着色器 " + kSplashShader +
                                 "，爆炸会退回默认粒子材质。请确认 " +
                                 "Assets/GameMain/Art/Shaders/BubbleBurstSplash.shader 存在且编译通过。");
                return null;
            }

            var mat = new Material(shader);
            mat.name = "BubbleBurst Splash (Runtime)";
            mat.hideFlags = HideFlags.DontSave;

            SetColor(mat, "_Tint", tint);
            SetColor(mat, "_RimColor", kWetEdge);
            SetFloat(mat, "_Opacity", opacity);
            SetFloat(mat, "_Softness", softness);
            SetFloat(mat, "_CorePower", corePower);
            SetFloat(mat, "_RimStrength", rimStrength);
            SetFloat(mat, "_RimWidth", 0.16f);
            SetFloat(mat, "_WobbleAmp", 4.2f);
            SetFloat(mat, "_WobbleSpeed", 2.4f);

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
