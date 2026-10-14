using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡存活时间到点时的"溶解 / 消散"特效。
    ///
    /// 【三种类型各有各的散法】——这是这套特效的重点，不然三个泡泡化掉长得一样，
    /// 玩家就分不出刚才消失的是哪种：
    ///
    ///   浮粘（Sticky）——「往下沉、慢慢化」
    ///       泡泡壳向下坠一点，边缘像颜料洇开，碎成小水珠再浮上去。
    ///       浮粘是"黏住、留下"的性格，所以散得最慢、最有分量。
    ///
    ///   弹力（Bouncy）——「弹开式地散」
    ///       先被压扁一下再弹开，速度最快、扩散最广，碎屑往外崩。
    ///       弹力是"弹跳"的性格，所以散得最干脆。
    ///
    ///   爆炸（Bomb）——「收拢后骤灭」
    ///       向内一收、顿一下，然后很快消失，残留一点橙色余温。
    ///       炸弹是"引信烧完"的性格，所以散得最突然。
    ///
    /// 【为什么用代码建，不做预制体】
    /// 与 BubbleBurstVFX 同理：场景/预制体是带自动分配 ID 的 YAML，
    /// 手改或脚本改都容易踩坑；结构固定、参数全在代码里时运行时建更可靠。
    ///
    /// 【配色】
    /// 优先取泡泡自己的材质颜色（这样美术改了泡泡颜色，溶解会自动跟着变）；
    /// 取不到才退回按类型给的默认色，且这些默认色都与 PAO/BubbleBall 同色系。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Bubble Dissolve VFX")]
    public sealed class BubbleDissolveVFX : MonoBehaviour
    {
        // ---- 与 BubbleBall / 策划案 §10.2 "粉紫基调" 对齐的色板 ----
        /// <summary>浮粘：冰蓝（BubbleBall._BaseColor 同一支）</summary>
        private static readonly Color kStickyTint = new Color(0.72f, 0.90f, 1.00f, 1f);

        /// <summary>弹力：青绿（BubbleBall._IridColorA）</summary>
        private static readonly Color kBouncyTint = new Color(0.45f, 0.95f, 0.85f, 1f);

        /// <summary>爆炸：暖橙（和 Bubble.AbsorbBomb 里把宿主染橙的那套一致）</summary>
        private static readonly Color kBombTint   = new Color(1.00f, 0.66f, 0.35f, 1f);

        /// <summary>水痕亮边（三种共用）</summary>
        private static readonly Color kWetEdge    = new Color(0.88f, 0.98f, 1.00f, 1f);

        private const string kDissolveShader = "PAO/BubbleDissolvePuff";

        /// <summary>
        /// 本实例创建出来的所有材质，销毁时统一回收。
        ///
        /// 【为什么不做静态缓存】
        /// 之前按颜色缓存过一份 static，但三个类型颜色不同会互相覆盖：
        /// 新类型建材质时把 static 换掉，旧材质就不在"要保留"的名单里了，
        /// 于是被销毁 —— 而它的粒子还在用。每实例各建各的就没这个问题，
        /// 溶解本身是低频操作，这点开销可以接受。
        /// </summary>
        private readonly System.Collections.Generic.List<Material> m_OwnedMaterials =
            new System.Collections.Generic.List<Material>();

        /// <summary>
        /// 在指定位置播一次溶解特效。
        /// </summary>
        /// <param name="position">泡泡中心</param>
        /// <param name="type">泡泡类型，决定消散姿态与颜色</param>
        /// <param name="diameter">泡泡直径（米）</param>
        /// <param name="sourceColor">泡泡身上的实际颜色。给 null 则用按类型的默认色</param>
        public static BubbleDissolveVFX Spawn(Vector3 position, BubbleType type,
                                              float diameter, Color? sourceColor = null)
        {
            if (diameter <= 0.001f)
            {
                diameter = 0.6f;
            }

            var root = new GameObject("Bubble Dissolve VFX");
            root.transform.position = position;

            var fx = root.AddComponent<BubbleDissolveVFX>();
            fx.Build(type, diameter, sourceColor ?? TintForType(type));

            return fx;
        }

        /// <summary>
        /// 从泡泡身上取实际颜色。
        /// 直接读实例材质的 _BaseColor / _Color / color —— 三种写法都试，
        /// 因为不同 shader 用的属性名不一样（URP/Lit 用 _BaseColor，Standard 用 _Color）。
        /// </summary>
        public static Color? ReadBubbleColor(GameObject bubble)
        {
            if (bubble == null)
            {
                return null;
            }

            Renderer renderer = bubble.GetComponentInChildren<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null)
            {
                return null;
            }

            Material mat = renderer.sharedMaterial;

            if (mat.HasProperty("_BaseColor"))
            {
                return mat.GetColor("_BaseColor");
            }

            if (mat.HasProperty("_Color"))
            {
                return mat.GetColor("_Color");
            }

            return mat.color;
        }

        /// <summary>按类型给默认色。</summary>
        private static Color TintForType(BubbleType type)
        {
            switch (type)
            {
                case BubbleType.Bouncy: return kBouncyTint;
                case BubbleType.Bomb:   return kBombTint;
                default:                return kStickyTint;
            }
        }

        // ==================== 构建 ====================

        private float m_LifeTime;
        private float m_Elapsed;

        private void Build(BubbleType type, float diameter, Color tint)
        {
            Material shellMat = GetShellMaterial(tint);

            switch (type)
            {
                case BubbleType.Bouncy:
                    // 弹开式：最短、最快、扩散最广
                    BuildShell(shellMat, diameter, life: 0.45f, expand: 2.3f,
                               rise: 0.10f, count: 12, startScale: 1.00f);
                    BuildMist(shellMat, diameter, life: 0.40f, speed: 2.6f,
                              rise: 0.25f, count: 20, sizeScale: 0.16f);
                    BuildDroplets(tint, diameter, life: 0.42f, speed: 2.2f,
                                  rise: 0.05f, count: 16);
                    m_LifeTime = 0.85f;
                    break;

                case BubbleType.Bomb:
                    // 收拢后骤灭：先微缩（startScale < 1 表现"收紧"），再三段内消失
                    BuildShell(shellMat, diameter, life: 0.32f, expand: 0.55f,
                               rise: 0.18f, count: 10, startScale: 0.72f);
                    BuildMist(shellMat, diameter, life: 0.28f, speed: 1.2f,
                              rise: 0.42f, count: 12, sizeScale: 0.13f);
                    BuildDroplets(tint, diameter, life: 0.30f, speed: 1.4f,
                                  rise: 0.30f, count: 10);
                    m_LifeTime = 0.70f;
                    break;

                default:
                    // 浮粘：往下沉、散得最慢、最"有分量"
                    BuildShell(shellMat, diameter, life: 0.80f, expand: 1.25f,
                               rise: -0.38f, count: 18, startScale: 1.00f);
                    BuildMist(shellMat, diameter, life: 0.95f, speed: 0.75f,
                              rise: 0.30f, count: 16, sizeScale: 0.18f);
                    BuildDroplets(tint, diameter, life: 0.75f, speed: 0.9f,
                                  rise: 0.22f, count: 14);
                    m_LifeTime = 1.35f;
                    break;
            }
        }

        /// <summary>
        /// 泡泡壳本身化掉。
        /// 这是主视觉：一块和泡泡差不多大的彩块，向内收 + 边缘侵蚀 + 淡出。
        /// </summary>
        private void BuildShell(Material material, float diameter, float life,
                                float expand, float rise, int count, float startScale)
        {
            ParticleSystem ps = NewSystem("Shell", life);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = 0f;                              // 靠 Size 曲线扩张，不靠速度
            main.startSize = diameter * startScale;
            main.startColor = Color.white;                     // 颜色走材质 _Tint
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // 负值 = 往上（水里的"洇上去"），正值 = 往下沉
            main.gravityModifier = -rise;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = diameter * 0.5f;                    // 沿球壳分布 = 泡泡的"皮"
            shape.radiusThickness = 1f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            // 尺寸：从泡泡大小 → 略微扩张（或收紧）→ 收掉
            ApplySizeCurve(ps, 1f, startScale, expand);

            // 透明度：前段保持，后段淡出
            ApplyAlphaCurve(ps, new[] { (0f, 0.95f), (0.45f, 0.75f), (1f, 0f) });

            // 顶点内缩交给 Size 曲线就够了：溶解彩块是 2D billboard，
            // 缩放粒子在视觉上就等价于"泡泡收缩"，不需要额外的顶点流。
            // （早先试过用 Custom1 传内缩量再在顶点着色器里压，但那要同时配置
            //   SetVector 和 renderer 的活动顶点流，多一层能出错的地方，收益为零。）

            // 缓慢自转，让边缘噪声一直在动
            ParticleSystem.RotationOverLifetimeModule rotate = ps.rotationOverLifetime;
            rotate.enabled = true;
            rotate.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        }

        /// <summary>细小的水雾颗粒，从泡泡位置渗出来飘散。</summary>
        private void BuildMist(Material material, float diameter, float life,
                               float speed, float rise, int count, float sizeScale)
        {
            ParticleSystem ps = NewSystem("Mist", life);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * diameter * 0.5f, speed * diameter);
            main.startSize = new ParticleSystem.MinMaxCurve(diameter * sizeScale * 0.6f,
                                                            diameter * sizeScale * 1.4f);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -rise;                      // 水里往上飘
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            // 分两波，第二波稍晚 —— 看起来像"陆续渗出来"
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, Mathf.Max(1, count / 2)),
                new ParticleSystem.Burst(life * 0.30f, Mathf.Max(1, count - count / 2)),
            });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = diameter * 0.35f;

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.45f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;

            ApplySizeCurve(ps, 0.5f, 1.15f, 1f);
            ApplyAlphaCurve(ps, new[] { (0f, 0f), (0.2f, 0.85f), (1f, 0f) });
        }

        /// <summary>很小的实心水珠，给溶解收尾一点颗粒感。</summary>
        private void BuildDroplets(Color tint, float diameter, float life,
                                   float speed, float rise, int count)
        {
            ParticleSystem ps = NewSystem("Droplets", life);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * diameter * 0.8f, speed * diameter * 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(diameter * 0.05f, diameter * 0.12f);
            main.startColor = tint;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -rise;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = diameter * 0.42f;
            shape.radiusThickness = 1f;

            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = MakeDropletMaterial(tint);

            ApplySizeCurve(ps, 1f, 0.9f, 0.35f);
            ApplyAlphaCurve(ps, new[] { (0f, 0.9f), (0.5f, 0.6f), (1f, 0f) });
        }

        /// <summary>水珠用独立材质（实心一点、不吃侵蚀），避免和壳抢同一份材质。</summary>
        private Material MakeDropletMaterial(Color tint)
        {
            return MakeMaterial(tint, softness: 0.10f, corePower: 0.25f, rimStrength: 0.7f, opacity: 1f);
        }

        // ==================== 粒子系统基础 ====================

        private ParticleSystem NewSystem(string name, float duration)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.duration = Mathf.Max(0.1f, duration);
            main.loop = false;
            main.playOnAwake = false;
            main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;   // 统一由本组件销毁

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortingFudge = -1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
            return ps;
        }

        /// <summary>尺寸曲线：起始 → 峰值 → 收尾，三个关键帧。</summary>
        private static void ApplySizeCurve(ParticleSystem ps, float from, float peak, float end)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;

            var curve = new AnimationCurve(
                new Keyframe(0f, from),
                new Keyframe(0.35f, peak),
                new Keyframe(1f, end));

            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>透明度曲线。传入 (时间比例, 透明度) 序列。</summary>
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

        /// <summary>壳的材质。每个 VFX 实例各建一份并登记回收。</summary>
        private Material GetShellMaterial(Color tint)
        {
            return MakeMaterial(tint, 0.24f, 0.50f, 1.2f, 1f);
        }

        /// <summary>
        /// 造一个 PAO/BubbleDissolvePuff 材质，并登记到本实例的回收列表。
        /// 找不到着色器时返回 null —— 粒子会退回默认材质，不崩。
        /// </summary>
        private Material MakeMaterial(Color tint, float softness, float corePower,
                                      float rimStrength, float opacity)
        {
            Shader shader = Shader.Find(kDissolveShader);

            if (shader == null)
            {
                Debug.LogWarning("[BubbleDissolveVFX] 找不到着色器 " + kDissolveShader +
                                 "，溶解会退回默认粒子材质。请确认 " +
                                 "Assets/GameMain/Art/Shaders/BubbleDissolvePuff.shader 存在且编译通过。");
                return null;
            }

            var mat = new Material(shader);
            mat.name = "BubbleDissolve (Runtime)";
            mat.hideFlags = HideFlags.DontSave;

            SetColor(mat, "_Tint", tint);
            SetColor(mat, "_RimColor", kWetEdge);
            SetFloat(mat, "_Opacity", opacity);
            SetFloat(mat, "_Softness", softness);
            SetFloat(mat, "_CorePower", corePower);
            SetFloat(mat, "_RimStrength", rimStrength);
            SetFloat(mat, "_RimWidth", 0.18f);
            SetFloat(mat, "_Erode", 0.55f);       // 溶解特有的边缘侵蚀
            SetFloat(mat, "_WobbleAmp", 3.4f);
            SetFloat(mat, "_WobbleSpeed", 1.8f);

            m_OwnedMaterials.Add(mat);
            return mat;
        }
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
            // 回收本实例造出来的所有材质。
            // 材质是运行时 new 的，不回收会一直泄漏到退出 Play。
            for (int i = 0; i < m_OwnedMaterials.Count; i++)
            {
                if (m_OwnedMaterials[i] != null)
                {
                    Destroy(m_OwnedMaterials[i]);
                }
            }

            m_OwnedMaterials.Clear();
        }
    }
}
