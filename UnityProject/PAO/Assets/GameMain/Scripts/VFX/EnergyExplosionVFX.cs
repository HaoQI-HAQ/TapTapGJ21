using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 炸弹泡泡的**能量爆炸** —— 塞尔达那种"一大团发光能量炸开"的观感。
    ///
    /// ============================================================
    /// 【为什么换掉了粒子方案】
    /// ============================================================
    /// 前面几版都用 ParticleSystem 堆粒子，无论怎么调都不对，原因是粒子这个
    /// 载体做不出"能量"：
    ///
    ///   · 粒子是很多**小图元**，凑不出一整片连续的发光体
    ///   · 粒子没有"从中心射出、越远越淡"的**连续光束**形状
    ///     （用 Stretch 去伪造会得到硬边直线 —— 之前"线条感太重"就是这个）
    ///   · 没有后处理 Bloom 时，粒子再亮也不会"溢出来"，所以"没有光感"
    ///
    /// 现在改成 **PAO/EnergyBurst 着色器 + 正对相机的四边形**：
    /// 能量结构（白热核心 / 放射光束 / 扩张环 / 外晕）全部在片元里画出来，
    /// 形状连续、光束渐变、核心亮度远超 1.0 从而过曝成白。
    ///
    /// ============================================================
    /// 【为什么要分 4 层叠】
    /// ============================================================
    /// 工程里没有任何 Volume / Bloom 后处理（10 个场景 0 处引用），
    /// 所以"泛光"只能自己叠：
    ///
    ///   Flash  极亮、极短、很大      —— 炸开瞬间那一下白屏
    ///   Core   白热核，中等时长      —— 能量的"芯"
    ///   Body   光束 + 扩张环，主体   —— 观感主体
    ///   Halo   很大很柔、很暗        —— 外圈柔光（假 Bloom）
    ///
    /// 四层用同一个材质，靠 MaterialPropertyBlock 各自设参数和尺寸，
    /// 叠起来就近似 Bloom 的柔光溢出。
    ///
    /// ============================================================
    /// 【向外消散，不是向内收缩】
    /// ============================================================
    ///   · 每层尺寸只涨不缩（sizeFrom → sizeTo 单调递增）
    ///   · 亮度包络是"快进 → 保持 → 后段淡出"，淡出发生在**已经涨到最大之后**
    ///   · 扩张环半径 = 进度，一路往外推
    ///
    /// ============================================================
    /// 【配色】
    /// 能量色取炸弹泡泡的类型色 BubbleTypeSwitcher.ColorOf(BubbleType.Bomb)，
    /// 即 (1.00, 0.62, 0.42)。核心用近白的热色，保证过曝时是白的。
    /// ============================================================
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Energy Explosion VFX")]
    public sealed class EnergyExplosionVFX : MonoBehaviour
    {
        // 粒子碎屑用的材质（可选，取不到就只显示能量层）
        private const string kMatDebris = "M_EnergySpark";

        private static readonly Color kFallbackTint = new Color(1.00f, 0.62f, 0.42f, 1f);

        private static Color kMainColor = kFallbackTint;
        private static Color kHotColor  = Color.Lerp(kFallbackTint, Color.white, 0.85f);

        // ---- 着色器属性 ID（每帧用字符串找属性很贵）----
        private static readonly int ID_Tint        = Shader.PropertyToID("_Tint");
        private static readonly int ID_HotTint     = Shader.PropertyToID("_HotTint");
        private static readonly int ID_Intensity   = Shader.PropertyToID("_Intensity");
        private static readonly int ID_Progress    = Shader.PropertyToID("_Progress");
        private static readonly int ID_RayCount    = Shader.PropertyToID("_RayCount");
        private static readonly int ID_RaySharp    = Shader.PropertyToID("_RaySharp");
        private static readonly int ID_RayBoost    = Shader.PropertyToID("_RayBoost");
        private static readonly int ID_RingBoost   = Shader.PropertyToID("_RingBoost");
        private static readonly int ID_GlowBoost   = Shader.PropertyToID("_GlowBoost");
        private static readonly int ID_GlowWidth   = Shader.PropertyToID("_GlowWidth");
        private static readonly int ID_CoreBoost   = Shader.PropertyToID("_CoreBoost");
        private static readonly int ID_CoreRadius  = Shader.PropertyToID("_CoreRadius");
        private static readonly int ID_NoiseAmount = Shader.PropertyToID("_NoiseAmount");

        /// <summary>一层能量图。</summary>
        private sealed class Layer
        {
            public Transform trans;
            public MeshRenderer renderer;
            public MaterialPropertyBlock block;

            public float sizeFrom;    // 起始直径
            public float sizeTo;      // 结束直径（必须 >= sizeFrom）
            public float delay;       // 相对爆炸起点的延迟
            public float life;        // 这一层自己的持续时间
            public float peak;        // 峰值 _Intensity

            public float rayCount;
            public float raySharp;
            public float rayBoost;
            public float ringBoost;
            public float glowBoost;
            public float glowWidth;
            public float coreBoost;
            public float coreRadius;
            public float noiseAmount;
        }

        private readonly System.Collections.Generic.List<Layer> m_Layers =
            new System.Collections.Generic.List<Layer>(4);

        private float m_Elapsed;
        private float m_Lifetime;
        private bool m_Running;
        private float m_Seed;

        private Camera m_Camera;

        // ==================== 共享资源 ====================

        private static Mesh s_Quad;
        private static Material s_Material;
        private static bool s_ReportedMissingShader;

        /// <summary>中心在原点、边长 1 的正方形。所有层共用。</summary>
        private static Mesh GetQuad()
        {
            if (s_Quad != null)
            {
                return s_Quad;
            }

            var mesh = new Mesh();
            mesh.name = "EnergyBurstQuad";
            mesh.hideFlags = HideFlags.DontSave;

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };

            mesh.normals = new[]
            {
                new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f),
                new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f),
            };

            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();

            s_Quad = mesh;
            return s_Quad;
        }

        private static Material GetMaterial()
        {
            if (s_Material != null)
            {
                return s_Material;
            }

            Shader shader = Shader.Find("PAO/EnergyBurst");

            if (shader == null)
            {
                if (!s_ReportedMissingShader)
                {
                    s_ReportedMissingShader = true;
                    Debug.LogWarning("[EnergyExplosionVFX] 找不到着色器 PAO/EnergyBurst，" +
                                     "炸弹爆炸不会有视觉效果。请确认 " +
                                     "Assets/GameMain/Art/Shaders/EnergyBurst.shader 编译通过。");
                }

                return null;
            }

            s_Material = new Material(shader);
            s_Material.name = "EnergyBurst (Runtime Shared)";
            s_Material.hideFlags = HideFlags.DontSave;

            return s_Material;
        }

        // ==================== 对外接口 ====================

        public static EnergyExplosionVFX Spawn(Vector3 position, float bubbleDiameter,
                                               float sizeReference = 0f,
                                               Color? tint = null)
        {
            if (sizeReference <= 0.001f)
            {
                sizeReference = Mathf.Max(bubbleDiameter, 0.5f);
            }

            Color main = tint.HasValue ? tint.Value : ColorOfBomb();
            DerivePalette(main);

            float scale = Mathf.Clamp(bubbleDiameter / sizeReference, 0.35f, 2.0f);

            var root = new GameObject("Energy Explosion");
            root.transform.position = position;

            var fx = root.AddComponent<EnergyExplosionVFX>();
            fx.Build(bubbleDiameter * scale);
            return fx;
        }

        private static Color ColorOfBomb()
        {
            try
            {
                return BubbleTypeSwitcher.ColorOf(BubbleType.Bomb);
            }
            catch (System.Exception)
            {
                return kFallbackTint;
            }
        }

        private static void DerivePalette(Color main)
        {
            kMainColor = main;
            // 核心几乎白 —— 过曝才像能量，而不是一块橙色的纸片
            kHotColor = Color.Lerp(main, Color.white, 0.85f);
        }

        // ==================== 构建 ====================

        private void Build(float size)
        {
            float d = Mathf.Max(size, 0.6f);
            m_Seed = Random.Range(0f, 100f);

            Material mat = GetMaterial();

            if (mat == null)
            {
                Destroy(gameObject);
                return;
            }

            // ---- 四层，从"极亮极短"到"很大很柔" ----
            //
            // 尺寸从小到大、时长为从短到长，叠起来就有一点"能量层层扩散"的感觉。
            // 全部单调整大（sizeTo >= sizeFrom），不会有任何收缩。

            AddLayer(mat, d, 0.55f, 2.4f, 0f,    0.16f, 5.0f,
                     rayCount: 7f,  raySharp: 2.0f, rayBoost: 0.35f,
                     ringBoost: 0.15f, glowBoost: 1.1f, glowWidth: 0.55f,
                     coreBoost: 5.0f, coreRadius: 0.30f, noiseAmount: 0.35f);

            AddLayer(mat, d, 0.90f, 3.2f, 0.02f, 0.42f, 3.2f,
                     rayCount: 9f,  raySharp: 2.6f, rayBoost: 1.0f,
                     ringBoost: 0.8f,  glowBoost: 0.9f, glowWidth: 0.65f,
                     coreBoost: 3.5f, coreRadius: 0.20f, noiseAmount: 0.5f);

            AddLayer(mat, d, 1.30f, 4.6f, 0.04f, 0.95f, 2.2f,
                     rayCount: 13f, raySharp: 3.2f, rayBoost: 1.9f,
                     ringBoost: 1.6f,  glowBoost: 0.7f, glowWidth: 0.80f,
                     coreBoost: 1.6f, coreRadius: 0.14f, noiseAmount: 0.6f);

            AddLayer(mat, d, 2.10f, 7.0f, 0.06f, 1.25f, 0.85f,
                     rayCount: 17f, raySharp: 4.0f, rayBoost: 0.35f,
                     ringBoost: 0.5f,  glowBoost: 1.5f, glowWidth: 1.0f,
                     coreBoost: 0.25f, coreRadius: 0.10f, noiseAmount: 0.85f);

            BuildDebris(d);

            m_Lifetime = 1.35f;
            m_Running = true;
        }

        private void AddLayer(Material mat, float d,
                              float sizeFromMul, float sizeToMul,
                              float delay, float life, float peak,
                              float rayCount, float raySharp, float rayBoost,
                              float ringBoost, float glowBoost, float glowWidth,
                              float coreBoost, float coreRadius, float noiseAmount)
        {
            var go = new GameObject("Burst Layer");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = GetQuad();

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            var layer = new Layer
            {
                trans = go.transform,
                renderer = renderer,
                block = new MaterialPropertyBlock(),
                sizeFrom = d * sizeFromMul,
                sizeTo = d * sizeToMul,
                delay = delay,
                life = life,
                peak = peak,
                rayCount = rayCount,
                raySharp = raySharp,
                rayBoost = rayBoost,
                ringBoost = ringBoost,
                glowBoost = glowBoost,
                glowWidth = glowWidth,
                coreBoost = coreBoost,
                coreRadius = coreRadius,
                noiseAmount = noiseAmount,
            };

            renderer.enabled = false;
            m_Layers.Add(layer);
        }

        /// <summary>
        /// 碎屑：被能量顶出去的小块。
        ///
        /// 【为什么这里可以用粒子】
        /// 因为碎屑本来就该是"离散的小块"，正好是粒子擅长的。
        /// 而爆炸主体那种"连续的一团能量"必须是着色器画的。
        /// 用 glow 类贴图（柔和衰减），不用 Stretch —— 避免又出现硬直线。
        /// </summary>
        private void BuildDebris(float d)
        {
            Material mat = Resources.Load<Material>("PAO/EnergyFX/" + kMatDebris);

            if (mat == null)
            {
                return;   // 没材质就不放碎屑，不影响主体
            }

            var go = new GameObject("Debris");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(d * 2.2f, d * 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(d * 0.05f, d * 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(kHotColor, kMainColor);
            main.gravityModifier = 0.25f;
            main.maxParticles = 90;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = d * 0.12f;
            shape.radiusThickness = 0.7f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f,    40),
                new ParticleSystem.Burst(0.06f, 26),
            });

            // 不要速度限制（limit 默认 0 会把粒子刹停）
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.3f, 1.0f),
                new Keyframe(1f, 1.3f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(kHotColor, 0f),
                    new GradientColorKey(kMainColor, 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = mat;

            ps.Play();
        }

        // ==================== 每帧驱动 ====================

        private void LateUpdate()
        {
            if (!m_Running)
            {
                return;
            }

            m_Elapsed += Time.deltaTime;

            // 所有层一起正对相机（爆炸是一团光，不该有固定朝向）
            Camera cam = GetCamera();

            if (cam != null)
            {
                transform.rotation = cam.transform.rotation;
            }

            for (int i = 0; i < m_Layers.Count; i++)
            {
                UpdateLayer(m_Layers[i]);
            }

            if (m_Elapsed >= m_Lifetime + 0.5f)
            {
                Destroy(gameObject);
            }
        }

        private Camera GetCamera()
        {
            if (m_Camera != null)
            {
                return m_Camera;
            }

            m_Camera = Camera.main;

            if (m_Camera == null)
            {
                m_Camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
            }

            return m_Camera;
        }

        private void UpdateLayer(Layer layer)
        {
            float local = m_Elapsed - layer.delay;

            if (local < 0f || local >= layer.life)
            {
                if (layer.renderer.enabled)
                {
                    layer.renderer.enabled = false;
                }
                return;
            }

            float t = local / layer.life;
            float envelope = Envelope(t);

            if (envelope <= 0.001f)
            {
                layer.renderer.enabled = false;
                return;
            }

            layer.renderer.enabled = true;

            // 尺寸：用 sqrt 让开头涨得快（炸开是瞬间的），但全程只涨不缩
            float grow = Mathf.Sqrt(Mathf.Clamp01(t));
            float size = Mathf.Lerp(layer.sizeFrom, layer.sizeTo, grow);
            layer.trans.localScale = new Vector3(size, size, 1f);

            MaterialPropertyBlock block = layer.block;
            block.SetColor(ID_Tint, kMainColor);
            block.SetColor(ID_HotTint, kHotColor);
            block.SetFloat(ID_Intensity, layer.peak * envelope);

            // 扩张环半径跟着进度走 —— 一路往外推
            block.SetFloat(ID_Progress, Mathf.Clamp01(t));

            block.SetFloat(ID_RayCount, layer.rayCount);
            block.SetFloat(ID_RaySharp, layer.raySharp);
            block.SetFloat(ID_RayBoost, layer.rayBoost);
            block.SetFloat(ID_RingBoost, layer.ringBoost);
            block.SetFloat(ID_GlowBoost, layer.glowBoost);
            block.SetFloat(ID_GlowWidth, layer.glowWidth);
            block.SetFloat(ID_CoreBoost, layer.coreBoost);
            block.SetFloat(ID_CoreRadius, layer.coreRadius);
            block.SetFloat(ID_NoiseAmount, layer.noiseAmount);

            layer.renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 亮度包络：快进（10%）→ 保持 → 后段淡出。
        ///
        /// 关键是**淡出发生在后 45%**，此时尺寸已经涨到接近最大 ——
        /// 所以看到的是"炸开到最大、然后在边缘淡掉"，
        /// 而不是"在原地涨一下就暗下去"。
        /// </summary>
        private static float Envelope(float t)
        {
            float fadeIn = Mathf.Clamp01(t / 0.10f);
            float fadeOut = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);

            // 末尾再乘一个好走的曲线，避免最后几帧突然消失
            fadeOut = fadeOut * fadeOut * (3f - 2f * fadeOut);

            return fadeIn * fadeOut;
        }
    }
}
