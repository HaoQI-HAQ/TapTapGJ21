using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 炸弹泡泡的**能量爆炸**：球状爆发 + 冲击环 + 放射光条 + 余烬。
    ///
    /// 【参考效果】
    /// 塞尔达那种"能量爆发"：中心一团过曝的火球迅速撑开，
    /// 外面一圈亮边冲击波向外推，同时有大量细长光条沿射线方向飞出。
    /// 整体是加色发光的，不是实心烟团。
    ///
    /// 【配色跟炸弹泡泡走】
    /// 早先这套是写死的冰蓝过曝色（照着参考图抄的），结果炸开时
    /// 颜色和那颗泡泡对不上 —— 玩家看不出"是我吹的那颗炸了"。
    ///
    /// 现在主色从 **BubbleTypeSwitcher.ColorOf(BubbleType.Bomb)** 取，
    /// 也就是游戏里真正显示的那个橙红 (1.00, 0.62, 0.42)，
    /// 再按同一个色相推出"过曝核心"和"暗部余烬"两个派生色。
    ///
    /// 【为什么运行时构建，而不是做成预制体资产】
    /// 预制体要序列化一堆粒子模块参数，手写 YAML 不可靠（这个工程踩过）。
    /// 运行时用 API 搭出来，参数全在代码里、能直接读能改。
    ///
    /// 【贴图】
    /// Assets/GameMain/Art/Textures/EnergyFX/ 下的 nova / ring / sparkle / glow。
    /// 这些是从 Epic Toon FX 素材里挑的，但**材质换成了本工程的**：
    /// 原素材的材质用 Built-in 的 Standard 着色器，在 URP 里会变成品红。
    /// 材质资产在 Resources/PAO/EnergyFX/，由 EnergyExplosionMaterialBuilder 生成。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Energy Explosion VFX")]
    public sealed class EnergyExplosionVFX : MonoBehaviour
    {
        // ---- 素材路径 ----
        // 材质是资产，放在 Resources 下 —— 运行时只能用 Resources.Load 取到。
        private const string kMatResourcePath = "PAO/EnergyFX/";

        private const string kMatCoreFlash = "M_EnergyCoreFlash";
        private const string kMatRing      = "M_EnergyRing";
        private const string kMatRay       = "M_EnergyRay";
        private const string kMatEmber     = "M_EnergyEmber";

        // ---- 配色 ----
        //
        // 主色在 Spawn 时按炸弹泡泡的类型色决定；下面三个是运行时的当前值。
        // 用静态字段是因为颜色要在 4 个 Build 方法里共享。

        /// <summary>兜底主色：炸弹类型色 (1.00, 0.62, 0.42) 橙红。</summary>
        private static readonly Color kFallbackTint = new Color(1.00f, 0.62f, 0.42f, 1f);

        /// <summary>当前主色（泡泡本身的颜色）。</summary>
        private static Color kMainColor = kFallbackTint;

        /// <summary>过曝核心色：主色往白里提。</summary>
        private static Color kHotColor = new Color(1f, 0.92f, 0.82f, 1f);

        /// <summary>暗部余烬色：主色压暗。</summary>
        private static Color kDeepColor = new Color(0.55f, 0.18f, 0.08f, 1f);

        /// <summary>
        /// 按主色推出"过曝核心"和"暗部余烬"两种派生色。
        ///
        /// 核心往白里提 —— 能量爆发的中心必须过曝才亮得起来；
        /// 暗部只压亮度、保留色相，避免变成灰色。
        /// </summary>
        private static void DerivePalette(Color main)
        {
            kMainColor = main;

            // 核心：往白提 70%。再多就全白、丢掉色相
            kHotColor = Color.Lerp(main, Color.white, 0.70f);

            // 暗部：亮度压到 45%，色相保留
            kDeepColor = new Color(main.r * 0.45f, main.g * 0.30f, main.b * 0.22f, 1f);
        }

        private float m_Elapsed;
        private float m_Lifetime;
        private bool m_Running;

        // ==================== 对外接口 ====================

        /// <summary>
        /// 在世界坐标生成一次能量爆炸。
        /// </summary>
        /// <param name="position">爆炸中心</param>
        /// <param name="bubbleDiameter">泡泡直径 —— 决定整体规模</param>
        /// <param name="sizeReference">"满蓄力"直径，用来归一化。0 则用泡泡直径</param>
        /// <param name="tint">爆炸主色。null 则用炸弹泡泡的类型色</param>
        public static EnergyExplosionVFX Spawn(Vector3 position, float bubbleDiameter,
                                               float sizeReference = 0f,
                                               Color? tint = null)
        {
            if (sizeReference <= 0.001f)
            {
                sizeReference = Mathf.Max(bubbleDiameter, 0.5f);
            }

            // 颜色：没指定就用炸弹泡泡的类型色（HUD 上那个橙红）
            Color main = tint.HasValue ? tint.Value : ColorOfBomb();
            DerivePalette(main);

            // 归一化：吹得小 = 炸得紧凑，吹得大 = 炸得铺开
            float scale = Mathf.Clamp(bubbleDiameter / sizeReference, 0.35f, 2.0f);

            var root = new GameObject("Energy Explosion");
            root.transform.position = position;

            var fx = root.AddComponent<EnergyExplosionVFX>();
            fx.Build(bubbleDiameter * scale);
            return fx;
        }

        /// <summary>
        /// 取炸弹泡泡的类型色。
        ///
        /// BubbleTypeSwitcher.ColorOf 是静态方法，不需要场景里有切换器实例，
        /// 所以在任何关卡都能拿到正确的颜色。
        /// </summary>
        private static Color ColorOfBomb()
        {
            try
            {
                return BubbleTypeSwitcher.ColorOf(BubbleType.Bomb);
            }
            catch (System.Exception)
            {
                // 极少数情况下取不到，退回兜底色
                return kFallbackTint;
            }
        }

        // ==================== 构建 ====================

        private void Build(float size)
        {
            // 基础尺度：按泡泡大小走，但给一个下限免得小泡泡几乎看不见
            float d = Mathf.Max(size, 0.6f);

            // 四层结构，从内到外。顺序不影响渲染（都是加色），
            // 但按"爆发 → 冲击波 → 射线 → 余烬"排读起来更清楚。
            BuildCoreFlash(d);
            BuildShockRing(d);
            BuildRadialRays(d);
            BuildEmbers(d);

            m_Lifetime = 0.95f;
            m_Running = true;
        }

        /// <summary>
        /// 核心光团：球状爆发的主体。用 nova 精灵表的一帧当贴图。
        ///
        /// 【为什么只取一帧】
        /// 精灵表是 6×3 的序列帧，逐帧播放需要每帧改材质属性，
        /// 而粒子材质是共享的 —— 改一次会让场上所有爆炸同步跳帧。
        /// 所以只取**最有爆发感的那一帧**，靠粒子的尺寸曲线做出
        /// "迅速撑开然后消散"，观感上足够。
        /// </summary>
        private void BuildCoreFlash(float d)
        {
            var go = NewLayer("Core Flash", transform);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            // 粒子系统默认会在 Play 时自动播，先停掉，参数设完再手动 Play
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.12f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.4f);   // 几乎不飞，原地撑开
            main.startSize = new ParticleSystem.MinMaxCurve(d * 1.6f, d * 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(kHotColor, kMainColor);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = 8;

            // 从中心一个点喷，靠尺寸曲线撑开成球
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.15f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });

            // 尺寸：先猛地撑大，再继续扩散（配合透明度降到 0）
            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f),
                new Keyframe(0.22f, 1.0f),
                new Keyframe(1f, 1.35f)));

            // 透明度：一闪就没了。能量爆发不该拖很久
            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(kHotColor, 0f),
                    new GradientColorKey(kMainColor, 0.45f),
                    new GradientColorKey(kDeepColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.95f, 0.25f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ApplyRenderer(ps, kMatCoreFlash);
            ps.Play();
        }

        /// <summary>
        /// 冲击环：一圈亮光向外推。参考图里那层明显的球壳边缘就是它。
        /// </summary>
        private void BuildShockRing(float d)
        {
            var go = NewLayer("Shock Ring", transform);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(d * 1.1f, d * 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(d * 2.0f, d * 2.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(kMainColor, kDeepColor);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = 6;

            // 用 Circle 形状往四周平推 —— 形成一个横向扩散的环
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = d * 0.2f;
            shape.radiusThickness = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.65f;      // 飞一小段就减速，像冲击波推出去然后停住

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.30f),
                new Keyframe(0.35f, 1.0f),
                new Keyframe(1f, 1.15f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(kHotColor, 0f),
                    new GradientColorKey(kDeepColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ApplyRenderer(ps, kMatRing);
            ps.Play();
        }

        /// <summary>
        /// 放射光条：参考图里最显眼的那些白色细长线，沿射线方向飞出。
        ///
        /// 【为什么要拉长】
        /// 用 Stretch 渲染 + 较长 lengthScale，看起来就是"光条"。
        /// 圆点粒子完全做不出那种速度感。
        /// </summary>
        private void BuildRadialRays(float d)
        {
            var go = NewLayer("Radial Rays", transform);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.18f;
            main.loop = false;
            main.playOnAwake = false;
            // 寿命拉开差距：有的瞬间消失、有的拖得长一点，看起来才不整齐划一
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(d * 2.4f, d * 7.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(d * 0.10f, d * 0.30f);
            main.startColor = new ParticleSystem.MinMaxGradient(kHotColor, kMainColor);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.maxParticles = 140;

            // 从中心球面往四周喷
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.3f;
            shape.radiusThickness = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 46),
                new ParticleSystem.Burst(0.05f, 30),
            });

            // 阻力：飞得快、很快就慢下来，像被水拦住
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.45f;

            // 尺寸：越飞越细
            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(1f, 0.15f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(kHotColor, 0f),
                    new GradientColorKey(kMainColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ApplyRenderer(ps, kMatRay);
            EnableStretch(ps, 6.5f);
            ps.Play();
        }

        /// <summary>余烬：爆完之后零星飘着的亮点，把收尾补顺。</summary>
        private void BuildEmbers(float d)
        {
            var go = NewLayer("Embers", transform);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(d * 0.4f, d * 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(d * 0.05f, d * 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(kMainColor, kDeepColor);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.06f;   // 轻微上浮（水里的余烬）
            main.maxParticles = 60;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.6f;
            shape.radiusThickness = 1f;      // 只在球壳上生成

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.06f, 22) });

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.7f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.1f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(kMainColor, 0f),
                    new GradientColorKey(kDeepColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ApplyRenderer(ps, kMatEmber);
            ps.Play();
        }

        // ==================== 辅助 ====================

        private static GameObject NewLayer(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>把渲染器设成 Billboard + 加载好的加色材质。</summary>
        private static void ApplyRenderer(ParticleSystem ps, string materialName)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material mat = LoadMaterial(materialName);

            if (mat != null)
            {
                renderer.sharedMaterial = mat;
            }
        }

        /// <summary>
        /// 把粒子拉成光条。参考图里那种放射状细线的关键就是它 ——
        /// 圆点粒子完全做不出速度感。
        /// </summary>
        private static void EnableStretch(ParticleSystem ps, float lengthScale)
        {
            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = lengthScale;
            renderer.lengthScale = 2.2f;
            renderer.cameraVelocityScale = 0f;
        }

        /// <summary>
        /// 从 Resources 加载材质资产。
        ///
        /// 【为什么不在运行时 new Material】
        /// 运行时拿不到贴图（贴图不在 Resources 下，Resources.Load 找不到）。
        /// 所以贴图必须提前烤进材质并放进 Resources。
        ///
        /// 【为什么用共享材质】
        /// 爆炸是高频对象，每次 new Material 会造成大量 GC。
        /// 共享资产不会泄漏，代价是不能在运行时改参数 —— 但这里参数是固定的。
        /// </summary>
        private static Material LoadMaterial(string name)
        {
            Material mat = Resources.Load<Material>(kMatResourcePath + name);

            if (mat == null && !s_ReportedMissingMaterial)
            {
                s_ReportedMissingMaterial = true;
                Debug.LogWarning("[EnergyExplosionVFX] 找不到材质 " + kMatResourcePath + name +
                                 "，爆炸会变成没有贴图的默认粒子。\n" +
                                 "请执行菜单：Tools → PAO → 特效 → 生成能量爆炸材质");
            }

            return mat;
        }

        /// <summary>材质缺失的警告只报一次，别刷屏。</summary>
        private static bool s_ReportedMissingMaterial;

        // ==================== 生命周期 ====================

        private void Update()
        {
            if (!m_Running)
            {
                return;
            }

            m_Elapsed += Time.deltaTime;

            if (m_Elapsed >= m_Lifetime + 0.6f)
            {
                // 【不要在这里 Destroy 材质】
                // 材质是 Resources 里的共享资产，由 Unity 管理生命周期。
                // 销毁它会让下一次爆炸没有材质 —— 直接变成不可见。
                Destroy(gameObject);
            }
        }
    }
}
