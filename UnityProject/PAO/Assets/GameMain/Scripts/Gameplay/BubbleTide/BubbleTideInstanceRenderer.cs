using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 颜色随机的作用范围。
    /// </summary>
    public enum BubbleTintMode
    {
        /// <summary>底色和描边一起换 —— 泡泡才有整体色感，推荐。</summary>
        Both = 0,

        /// <summary>只换底色，描边保持材质上设的那个。</summary>
        BaseOnly = 1,

        /// <summary>只换描边，底色保持材质上设的那个。</summary>
        OutlineOnly = 2,
    }

    /// <summary>
    /// 色板里的一项：一组「底色 + 描边色」。
    ///
    /// 【为什么把两个颜色配成一对，而不是各自随机】
    /// 如果底色和描边各自独立随机，会出现「深绿泡泡配亮黄描边」这种撞色，
    /// 一堆泡泡里就会有几颗特别跳。配成对之后，每颗泡泡是一个协调的整体，
    /// 而你仍然可以逐对去调。
    ///
    /// 【这两个颜色是「乘」上去的，不是覆盖】
    /// 最终颜色 = 材质颜色 × 这里的颜色。所以默认值全给白色（= 不变），
    /// 材质上的设置依然是主体。想要强烈的色彩就把材质底色调淡一些。
    /// </summary>
    [System.Serializable]
    public struct BubbleTintPair
    {
        [Tooltip("底色相乘项。白色 = 不改变材质底色。")]
        public Color baseTint;

        [Tooltip("描边色相乘项。白色 = 不改变材质描边色。")]
        public Color outlineTint;
    }

    /// <summary>
    /// 泡泡潮 · 卡通泡泡渲染器（全新一版）
    ///
    /// 【它做什么】
    ///   1. 从 <see cref="BubbleTideField"/> 那张「第几秒被淹」的表里
    ///      抽出一批泡泡的位置和出现时刻 —— 所以「泡泡慢慢充满地图」的顺序
    ///      和潮水天然一致，不需要另外调一套节奏
    ///   2. 每帧按潮流时间算出每颗泡泡的缩放，喂给实例化绘制
    ///   3. 用 PAO/BubbleTideToon 材质画出来
    ///
    /// 【它不做什么】
    ///   不生成任何 GameObject。几百颗泡泡的位置矩阵直接交给
    ///   Graphics.DrawMeshInstanced，没有 Transform、没有 Renderer、没有 Update。
    ///
    /// 【为什么在这里上传矩阵，而不是像原版 SDF 那样传一个 float】
    ///   原版 BubbleSDF 把所有形状塞进 StructuredBuffer、每帧只更新一个时间参数，
    ///   那个做法在「泡泡数成千上万」时是必须的。
    ///   但这个功能的泡泡数是几十到两百，矩阵总量不到 8 KB，
    ///   每帧重算一次的 CPU 开销远小于「为了省它而引入自定义 RenderFeature +
    ///   计算着色器」的复杂度。**先按简单的来，真不够再说。**
    ///
    /// 【和原组件的关系】
    ///   完全不碰 BubbleTideDirector / BubbleTideCellRenderer / BubbleTide.shader。
    ///   这个组件只是并排挂在同一个物体上，自己读同一个 Field。
    ///   想让原版潮水视觉效果退场，把 BubbleTideCellRenderer 的
    ///   RenderVisuals 关掉即可（那是它自己的开关）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/卡通泡泡渲染（新版）")]
    public sealed class BubbleTideInstanceRenderer : MonoBehaviour
    {
        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("材质")]
        [Tooltip("用 Assets/GameMain/Art/Shaders/BubbleTideToon.shader 建的材质。\n" +
                 "留空的话运行时按下面的 shader 自己建一个，但发布版里 Shader.Find 不可靠，\n" +
                 "所以正式用请用菜单「工具 → PAO → 泡泡潮 → 新建卡通泡泡材质」建好再拖进来。")]
        [SerializeField] private Material m_Material;

        [Tooltip("没指定材质时用来现场创建材质的 shader。")]
        [SerializeField] private Shader m_Shader;

        [Header("泡泡布局")]
        [Tooltip("泡泡的整体大小（米，半径）。蓝噪声分布会在这个基础上下浮动。\n" +
                 "房间 41×13×33 米时：半径 3 米 ≈ 200 颗，4.5 米 ≈ 60~100 颗。")]
        [SerializeField, Range(0.5f, 12f)] private float m_Radius = 4f;

        [Tooltip("每颗泡泡的大小浮动比例。0.35 = 半径在 0.65~1.35 倍之间随机。\n" +
                 "**大球小球混着才有「一堆泡泡」的层次**，全一样大一眼假。")]
        [SerializeField, Range(0f, 0.8f)] private float m_RadiusVariation = 0.35f;

        [Tooltip("两颗泡泡球心之间的最小距离系数（相对两者半径之和）。\n" +
                 "  < 1  允许重叠（挤在一起像一坨）\n" +
                 "  1.0  刚好相切\n" +
                 "  1.25 默认，留出一点缝\n" +
                 "  1.6+ 明显分开，能看见中间的场景")]
        [SerializeField, Range(0.5f, 2.5f)] private float m_MinDistanceFactor = 1.25f;

        [Tooltip("球心离房间边界至少留多少（米）。设小一点会让泡泡稍微穿进墙里，\n" +
                 "对「挤满」的观感有帮助。")]
        [SerializeField, Range(-4f, 6f)] private float m_EdgeMargin = 0.5f;

        [Tooltip("球心整体抬升（米）。潮水从地面涨起来，抬高等于让泡泡更靠天花板，\n" +
                 "地面留出的活动空间更大。")]
        [SerializeField, Range(-3f, 8f)] private float m_HeightOffset = 1f;

        [Tooltip("有多少比例的采样点是实心就放弃这颗泡泡（0.5 = 一半以上是墙就不放）。")]
        [SerializeField, Range(0f, 1f)] private float m_SolidRejectRatio = 0.5f;

        [Tooltip("最多摆几颗（上限保护）。按出现时刻从早到晚保留。")]
        [SerializeField, Range(8, 512)] private int m_MaxBubbles = 100;

        [Tooltip("最多尝试多少个采样点。蓝噪声靠「撒点 + 拒绝」实现，\n" +
                 "这个数决定了它能找到多少颗泡泡。太小会摆不满。")]
        [SerializeField, Range(200, 20000)] private int m_MaxSamples = 6000;

        [Header("颜色随机")]
        [Tooltip("从下面的色板里每颗随机取一个颜色，乘到材质颜色上。\n" +
                 "材质上的「底色」和「描边颜色」仍然是主体，这里只是加一层变化。")]
        [SerializeField] private bool m_RandomizeColor = true;

        [Tooltip("哪些颜色跟着随机：\n" +
                 "  Both         底色和描边一起换（推荐，泡泡才有整体色感）\n" +
                 "  BaseOnly     只换底色，描边统一\n" +
                 "  OutlineOnly  只换描边，底色统一")]
        [SerializeField] private BubbleTintMode m_TintMode = BubbleTintMode.Both;

        [Tooltip("颜色随机强度。0 = 完全按色板；1 = 和材质原色一样（等于不随机）。\n" +
                 "0.7 左右比较自然，全 0 会显得有些跳。")]
        [SerializeField, Range(0f, 1f)] private float m_TintStrength = 0.75f;

        [Tooltip("色板。每颗泡泡从这里随机取一组（底色 + 描边色）。\n" +
                 "留空则用内置的一组浅色。")]
        [SerializeField] private BubbleTintPair[] m_TintPalette = null;

        [Header("出场动画")]
        [Tooltip("单颗泡泡从 0 长到满用多少秒。")]
        [SerializeField, Range(0.2f, 12f)] private float m_GrowDuration = 3f;

        [Tooltip("长满时的过冲量。0.15 = 先胀到 1.15 倍再回弹到 1 —— \n" +
                 "这是「啵」的一下的关键，别设成 0。")]
        [SerializeField, Range(0f, 0.5f)] private float m_GrowOvershoot = 0.16f;

        [Tooltip("整体进度对齐到哪：\n" +
                 "  true  = 对齐到「终点被淹」的时刻（Deadline），推荐 —— 泡泡和倒计时同步\n" +
                 "  false = 对齐到「整个房间淹满」的时刻")]
        [SerializeField] private bool m_AlignToDeadline = true;

        [Tooltip("通关/失败之后，泡泡是否停止继续出现。\n" +
                 "**建议勾上**：不然玩家已经过关了，屏幕上还在长新泡泡。")]
        [SerializeField] private bool m_FreezeAfterFinish = true;

        [Header("网格")]
        [Tooltip("icosphere 细分级别。1 = 80 面（推荐），0 = 20 面（更省但不圆）。\n" +
                 "顶点位移（扰动）是逐顶点的，级别太低会看成多面体。")]
        [SerializeField, Range(0, 3)] private int m_Subdivisions = 1;

        [Header("视锥剔除")]
        [Tooltip("关掉视锥剔除（调试用，能确认泡泡确实在场景里）。")]
        [SerializeField] private bool m_DisableCulling = false;

        [Header("调试")]
        [Tooltip("在 Game 视图左上角显示泡泡数与帧开销。")]
        [SerializeField] private bool m_DrawHud = true;

        [Tooltip("在 Scene 视图画出泡泡球心的位置与半径，方便调布局。")]
        [SerializeField] private bool m_DrawGizmos = true;

        // ==================================================================
        // 运行时
        // ==================================================================

        private struct PlacedBubble
        {
            public Vector3 position;
            public float radius;
            public float appearTime;    // 秒（已按对齐模式归一化到潮流时间轴）
            public float phase;         // 彩虹相位
            public float wobbleSeed;    // 扰动种子
            public float tintShift;     // 彩虹色相微差
            public int tintIndex;       // 取自色板的哪一项
        }

        private BubbleTideDirector m_Director;
        private BubbleTideField m_Field;

        private readonly List<PlacedBubble> m_Bubbles = new List<PlacedBubble>(256);
        private readonly List<int> m_Active = new List<int>(256);

        private Matrix4x4[] m_Matrices;
        private Vector4[] m_Params;      // 相位 / 种子 / 色调偏移 / 缩放
        private Vector4[] m_TintA;       // 底色相乘项
        private Vector4[] m_TintB;       // 描边色相乘项
        private MaterialPropertyBlock m_PropertyBlock;
        private Material m_RuntimeMaterial;
        private Mesh m_Mesh;

        private int m_LastActiveCount = -1;
        private float m_FreezeElapsed = -1f;   // <0 = 未冻结

        /// <summary>实际布下的泡泡数量。</summary>
        public int BubbleCount { get { return m_Bubbles.Count; } }

        /// <summary>本帧参与绘制的泡泡数。</summary>
        public int ActiveCount { get { return m_Active.Count; } }

        /// <summary>布局是否已经完成。</summary>
        public bool IsReady { get { return m_Field != null && m_Field.IsBaked && m_Bubbles.Count > 0; } }

        // ==================================================================
        // 生命周期
        // ==================================================================

        private void Awake()
        {
            m_PropertyBlock = new MaterialPropertyBlock();

            if (m_Shader == null)
            {
                // 只在没指定材质时才找。编辑器里 Shader.Find 永远能找到，
                // 所以打包前请用菜单工具把材质做好拖进来。
                m_Shader = Shader.Find("PAO/BubbleTideToon");
            }
        }

        private void OnDestroy()
        {
            if (m_RuntimeMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(m_RuntimeMaterial);
                }
                else
                {
                    DestroyImmediate(m_RuntimeMaterial);
                }

                m_RuntimeMaterial = null;
            }
        }

        private void Start()
        {
            BuildLayout();
        }

        /// <summary>
        /// 场换了就重新布局。
        ///
        /// 这里用「轮询」而不是订阅事件，是刻意的：
        /// BubbleTideDirector 上的 Baked 是**实例事件**，为了订阅它就得去改
        /// 原来的脚本，而这个组件的原则是「一个字都不动原版」。
        /// 每帧一次引用比较的成本可以忽略，换来的完全解耦更值。
        ///
        /// 两种没就绪的情况都要能自愈：
        ///   · 还没找到 Director（脚本执行顺序不确定）→ 每帧重试一次查找
        ///   · 场还没烤好 → 烤好之后 Field 会变成一个新对象，引用比较就会发现
        /// </summary>
        private void Update()
        {
            if (m_Director == null)
            {
                m_Director = BubbleTideDirector.Instance;
                if (m_Director != null && !ReferenceEquals(m_Field, m_Director.Field))
                {
                    m_Field = m_Director.Field;
                    BuildLayout();
                }

                return;
            }

            if (!ReferenceEquals(m_Field, m_Director.Field))
            {
                m_Field = m_Director.Field;
                BuildLayout();
            }
        }

        // ==================================================================
        // 布局：从体素场里抽出泡泡
        // ==================================================================

        /// <summary>
        /// 重新生成泡泡布局。正常情况下由 Start 自动调用。
        ///
        /// 【算法：蓝噪声（Poisson-disk 拒绝采样）】
        ///
        /// 之前的做法是「把房间切成规则网格，每块正中心放一颗」——
        /// 位置、大小全部一样，所以看起来像排队，一眼就是程序生成的。
        ///
        /// 现在改成：在包围盒里撒**远多于需要数量**的候选点，逐个检查
        /// 「离已接受的所有泡泡是否都足够远」，够远就接受。
        /// 这样得到的就是「均匀但没有周期」的分布 —— 俗称蓝噪声，
        /// 正好是自然界里气泡堆积的样子。**没有任何网格痕迹。**
        ///
        /// 【为什么不用「抖动网格」那种便宜做法】
        /// 抖动只能打散「整齐」，打散不了格子的痕迹 ——
        /// 抖动量必须小于半格，否则会撞在一起，所以周期结构还在。
        /// 而拒绝采样对这个量级（几十~两百颗）只要几毫秒，值得。
        ///
        /// 【出现时刻】
        /// 取该处空气格子里较早的那一批的被淹时间。用「较早的」而不是平均值，
        /// 是为了让泡泡在潮水刚摸到时就出现，视觉上和潮水前沿贴在一起。
        /// </summary>
        public void BuildLayout()
        {
            m_Bubbles.Clear();
            m_Active.Clear();
            m_LastActiveCount = -1;

            if (m_Director == null)
            {
                m_Director = BubbleTideDirector.Instance;
                if (m_Director == null)
                {
                    m_Director = FindObjectOfType<BubbleTideDirector>();
                }
            }

            if (m_Director == null)
            {
                Debug.LogWarning("[泡泡潮·卡通] 场景里没有 BubbleTideDirector，泡泡不会出现。");
                return;
            }

            if (m_Field == null)
            {
                m_Field = m_Director.Field;
            }

            if (m_Field == null || !m_Field.IsBaked)
            {
                // 还没烤。Update 里会一直比较 Field 引用，烤好了就会自己重新进来。
                return;
            }

            m_Mesh = BubbleIcosphere.Get(m_Subdivisions);

            Bounds bounds = m_Field.WorldBounds;

            float solidTolerance = Mathf.Clamp01(m_SolidRejectRatio);
            float timeScale = ResolveTimeScale();

            // 蓝噪声的候选点范围：球心必须留在房间内，否则泡泡会挂在墙外
            Bounds spawn = bounds;
            spawn.Expand(-Mathf.Max(0f, m_EdgeMargin) * 2f);
            if (spawn.size.x <= 0.1f || spawn.size.y <= 0.1f || spawn.size.z <= 0.1f)
            {
                spawn = bounds;   // 房间比边距还小，退回原始范围
            }

            int attempts = 0;
            int solidRejected = 0;
            int crowdedRejected = 0;

            // ---- 撒点 ----
            // 用 Hash01 而不是 Random：布局每次跑出来完全一样。
            // 调试时「改了参数」和「换了个随机种子」不会混在一起，出问题好复现。
            for (int i = 0; i < m_MaxSamples && m_Bubbles.Count < m_MaxBubbles; i++)
            {
                attempts++;

                // ---- 随机半径（尺寸差异）----
                // 大球小球混着才有「一堆泡泡」的层次；全一样大一眼假。
                float radiusScale = 1f + (Hash01(i, 11) * 2f - 1f) * m_RadiusVariation;
                float radius = Mathf.Max(0.15f, m_Radius * radiusScale);

                // ---- 随机位置 ----
                Vector3 candidate = new Vector3(
                    Mathf.Lerp(spawn.min.x, spawn.max.x, Hash01(i, 1)),
                    Mathf.Lerp(spawn.min.y, spawn.max.y, Hash01(i, 2)) + m_HeightOffset,
                    Mathf.Lerp(spawn.min.z, spawn.max.z, Hash01(i, 3)));

                // ---- 这一处是不是墙里 ----
                float appearTime;
                if (!TrySampleArrivalTime(m_Field, candidate, radius,
                                          solidTolerance, timeScale, out appearTime))
                {
                    solidRejected++;
                    continue;
                }

                // ---- 离已接受的泡泡够不够远（蓝噪声的核心）----
                // minDistanceFactor < 1 允许重叠；> 1 会留出缝隙
                bool tooClose = false;
                for (int k = 0; k < m_Bubbles.Count; k++)
                {
                    PlacedBubble other = m_Bubbles[k];
                    float minDistance = (radius + other.radius) * 0.5f * m_MinDistanceFactor;
                    float minDistanceSq = minDistance * minDistance;

                    if ((other.position - candidate).sqrMagnitude < minDistanceSq)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                {
                    crowdedRejected++;
                    continue;
                }

                m_Bubbles.Add(new PlacedBubble
                {
                    position    = candidate,
                    radius      = radius,
                    appearTime  = appearTime,
                    phase       = Hash01(i, 4) * 6.2831853f,      // 彩虹相位拉开整圈
                    wobbleSeed  = Hash01(i, 5) * 10f,             // 扰动种子
                    tintShift   = (Hash01(i, 6) - 0.5f) * 0.6f,
                    tintIndex   = ResolveTintIndex(i),
                });
            }

            // ---- 出现时刻从早到晚排序 ----
            // 排序有两个用处：
            //   1. 可以只用 ActiveCount 前缀来绘制，不必每颗都判断
            //   2. 超过上限时截断，砍掉的永远是最后才出现的那批（而不是随机的）
            m_Bubbles.Sort(CompareByAppearTime);

            if (m_Bubbles.Count > m_MaxBubbles)
            {
                m_Bubbles.RemoveRange(m_MaxBubbles, m_Bubbles.Count - m_MaxBubbles);
            }

            EnsureBuffers();

            float averageRadius = 0f;
            for (int i = 0; i < m_Bubbles.Count; i++)
            {
                averageRadius += m_Bubbles[i].radius;
            }

            if (m_Bubbles.Count > 0)
            {
                averageRadius /= m_Bubbles.Count;
            }

            Debug.Log(string.Format(
                "[泡泡潮·卡通] 布局完成（蓝噪声）：{0} 颗泡泡\n" +
                "  平均半径 {1:F2} 米（基准 {2:F2} ±{3:P0}）\n" +
                "  采样 {4} 次：墙里丢掉 {5}，太挤丢掉 {6}\n" +
                "  最小间距系数 {7:F2}，网格 {8} 面，出现时刻对齐到 {9:F1} 秒",
                m_Bubbles.Count, averageRadius, m_Radius, m_RadiusVariation,
                attempts, solidRejected, crowdedRejected,
                m_MinDistanceFactor,
                BubbleIcosphere.GetTriangleCount(m_Subdivisions),
                timeScale));
        }

        /// <summary>
        /// 采样一个位置：它是不是空腔、会不会被淹、第几秒被淹。
        ///
        /// 【为什么用固定采样数，而不是按半径逐格遍历】
        /// 一颗 5 米的泡泡，按 0.5 米格距逐格是 4000 多个格子，
        /// 而候选点有几千个 —— 那就成了千万级循环，一进 Play 就卡住。
        /// 这里固定每轴最多采 5 个点（一颗最多 125 次），
        /// 对「这里是不是墙」「大概第几秒被淹」完全够用。
        /// </summary>
        private bool TrySampleArrivalTime(
            BubbleTideField field, Vector3 center, float radius,
            float solidTolerance, float timeScale, out float appearTime)
        {
            appearTime = 0f;

            Vector3 half = Vector3.one * Mathf.Max(0.05f, radius * 0.9f);

            const int kSamplesPerAxis = 5;
            Vector3 axisStep = half * 2f / (kSamplesPerAxis - 1);

            int total = 0;
            int solid = 0;
            int flooded = 0;
            float earliest = float.MaxValue;
            float latest = 0f;

            for (int iz = 0; iz < kSamplesPerAxis; iz++)
            {
                float z = center.z - half.z + axisStep.z * iz;

                for (int iy = 0; iy < kSamplesPerAxis; iy++)
                {
                    float y = center.y - half.y + axisStep.y * iy;

                    for (int ix = 0; ix < kSamplesPerAxis; ix++)
                    {
                        float x = center.x - half.x + axisStep.x * ix;

                        Vector3 sample = new Vector3(x, y, z);
                        Vector3Int cell = field.WorldToCell(sample);

                        total++;

                        if (cell.x < 0 || cell.y < 0 || cell.z < 0 ||
                            cell.x >= field.SizeX || cell.y >= field.SizeY || cell.z >= field.SizeZ)
                        {
                            // 出界：当作实心，免得泡泡挂在房间外面
                            solid++;
                            continue;
                        }

                        if (field.IsSolid(cell.x, cell.y, cell.z))
                        {
                            solid++;
                            continue;
                        }

                        float arrival = field.SampleArrivalTime(sample);
                        if (float.IsPositiveInfinity(arrival))
                        {
                            // 封闭空腔：永远不会被淹，不算进出现时刻
                            continue;
                        }

                        flooded++;
                        if (arrival < earliest) earliest = arrival;
                        if (arrival > latest) latest = arrival;
                    }
                }
            }

            if (total == 0 || flooded == 0)
            {
                return false;      // 全实心，或者是个封闭空腔
            }

            if ((float)solid / total >= solidTolerance)
            {
                return false;      // 这个位置大半在墙里
            }

            // 用「较早的那一批」而不是平均值：潮水前沿刚摸到这颗泡泡就冒出来，
            // 视觉上生长和潮水贴在一起
            float blend = Mathf.Lerp(earliest, latest, 0.3f);

            // 归一化到潮流时间轴，再减去一点提前量（让泡泡比潮水面稍早冒头）
            appearTime = Mathf.Max(0f, blend * timeScale - m_GrowDuration * 0.5f);
            return true;
        }

        /// <summary>
        /// 这颗泡泡取色板里的第几项。不启用随机时统一返回 -1（表示「不加色」）。
        /// </summary>
        private int ResolveTintIndex(int sampleIndex)
        {
            if (!m_RandomizeColor)
            {
                return -1;
            }

            BubbleTintPair[] palette = ResolvePalette();
            if (palette == null || palette.Length == 0)
            {
                return -1;
            }

            return Mathf.Clamp((int)(Hash01(sampleIndex, 7) * palette.Length), 0, palette.Length - 1);
        }

        /// <summary>
        /// 取色板。没在 Inspector 上配就用内置的一组浅色。
        ///
        /// 【内置色板为什么都是浅色 / 低饱和】
        /// 这些颜色是**乘**到材质颜色上的。乘法只会让颜色变暗、变偏，
        /// 不会变亮。所以想让泡泡更鲜艳应该去提材质的底色亮度，
        /// 色板负责的是「彼此之间的差异」。
        /// </summary>
        private BubbleTintPair[] ResolvePalette()
        {
            if (m_TintPalette != null && m_TintPalette.Length > 0)
            {
                return m_TintPalette;
            }

            return s_DefaultPalette;
        }

        private static readonly BubbleTintPair[] s_DefaultPalette =
        {
            new BubbleTintPair { baseTint = new Color(1.00f, 1.00f, 1.00f), outlineTint = new Color(1.00f, 1.00f, 1.00f) },
            new BubbleTintPair { baseTint = new Color(0.85f, 0.98f, 1.00f), outlineTint = new Color(0.75f, 0.95f, 1.00f) },
            new BubbleTintPair { baseTint = new Color(1.00f, 0.88f, 0.97f), outlineTint = new Color(1.00f, 0.72f, 0.90f) },
            new BubbleTintPair { baseTint = new Color(0.90f, 1.00f, 0.90f), outlineTint = new Color(0.72f, 1.00f, 0.80f) },
            new BubbleTintPair { baseTint = new Color(1.00f, 0.97f, 0.85f), outlineTint = new Color(1.00f, 0.88f, 0.62f) },
            new BubbleTintPair { baseTint = new Color(0.93f, 0.90f, 1.00f), outlineTint = new Color(0.84f, 0.76f, 1.00f) },
        };

        /// <summary>
        /// 按 _TintMode 把色板里取出的那一对颜色分配到两条通道上。
        ///
        /// 【为什么用「插值到白色」而不是直接赋值】
        /// 这两条通道在 shader 里是**乘**到材质颜色上的：
        ///     最终底色   = 材质底色   × tintA
        ///     最终描边色 = 材质描边色 × tintB
        /// 所以白色 = 不改变材质设置。而 _TintStrength 就是在
        /// 「完全用色板」（0）和「完全不改」（1）之间插值 ——
        /// 不需要在 shader 里再加一个强度参数。
        /// </summary>
        private void ResolveTintArrays(int tintIndex, out Vector4 tintA, out Vector4 tintB)
        {
            Vector4 white = Vector4.one;

            if (tintIndex < 0)
            {
                tintA = white;
                tintB = white;
                return;
            }

            BubbleTintPair[] palette = ResolvePalette();
            if (palette == null || tintIndex >= palette.Length)
            {
                tintA = white;
                tintB = white;
                return;
            }

            BubbleTintPair pair = palette[tintIndex];
            float k = Mathf.Clamp01(m_TintStrength);

            // k = 0 → 完全按色板；k = 1 → 等于白色（材质原色）
            Vector4 a = new Vector4(pair.baseTint.r, pair.baseTint.g, pair.baseTint.b, 1f);
            Vector4 b = new Vector4(pair.outlineTint.r, pair.outlineTint.g, pair.outlineTint.b, 1f);

            a = Vector4.Lerp(a, white, k);
            b = Vector4.Lerp(b, white, k);

            // 按模式决定哪一条保持白色（= 不参与随机）
            tintA = (m_TintMode == BubbleTintMode.OutlineOnly) ? white : a;
            tintB = (m_TintMode == BubbleTintMode.BaseOnly)    ? white : b;
        }

        /// <summary>
        /// 把「潮水秒数」换算到「泡泡出现时间轴」上的系数。
        /// 对齐 Deadline 时，泡泡会在终点被淹的那一刻正好长满。
        /// </summary>
        private float ResolveTimeScale()
        {
            if (m_Field == null || m_Field.LastArrivalTime <= 1e-4f)
            {
                return 1f;
            }

            if (!m_AlignToDeadline || m_Director == null || m_Director.Deadline <= 1e-4f)
            {
                return 1f;
            }

            // 潮水在第 Deadline 秒淹到终点、在第 LastArrivalTime 秒淹满全场。
            // 想让泡泡在第 Deadline 秒长满，就把整条时间轴压到 Deadline 内。
            return Mathf.Clamp(m_Director.Deadline / m_Field.LastArrivalTime, 0.05f, 1f);
        }

        private static int CompareByAppearTime(PlacedBubble a, PlacedBubble b)
        {
            return a.appearTime.CompareTo(b.appearTime);
        }

        private void EnsureBuffers()
        {
            int required = Mathf.Min(m_Bubbles.Count, 1023);
            if (required <= 0)
            {
                return;
            }

            if (m_Matrices == null || m_Matrices.Length < required)
            {
                m_Matrices = new Matrix4x4[required];
                m_Params = new Vector4[required];
                m_TintA = new Vector4[required];
                m_TintB = new Vector4[required];
            }
        }

        // ==================================================================
        // 每帧：推进进度 + 绘制
        // ==================================================================

        private void LateUpdate()
        {
            if (!IsReady || m_Mesh == null)
            {
                return;
            }

            Material material = ResolveMaterial();
            if (material == null)
            {
                return;
            }

            float elapsed = ResolveElapsed();

            // ---- 挑出已经在场上的泡泡 ----
            // 因为按出现时刻排过序，这里就是扫一遍前缀。
            // 用 while 而不是全量遍历，是为了让「已经出现的数量」直接变成绘制数量。
            m_Active.Clear();

            float grow = Mathf.Max(0.05f, m_GrowDuration);

            for (int i = 0; i < m_Bubbles.Count; i++)
            {
                if (m_Bubbles[i].appearTime > elapsed)
                {
                    break;      // 后面的更晚，不用看了
                }

                m_Active.Add(i);
            }

            if (m_Active.Count == 0)
            {
                return;
            }

            EnsureBuffers();

            // ---- 填矩阵 ----
            // 网格是**半径 1 的单位球**，所以缩放值直接就是世界半径。
            Camera camera = Camera.main;

            int count = 0;
            for (int i = 0; i < m_Active.Count && count < m_Matrices.Length; i++)
            {
                PlacedBubble bubble = m_Bubbles[m_Active[i]];

                // 出场进度 0~1
                float t = Mathf.Clamp01((elapsed - bubble.appearTime) / grow);

                // 缓动 + 过冲：先长过头再回弹，就是「啵」的一下的来源
                float eased = 1f - Mathf.Pow(1f - t, 3f);          // easeOutCubic
                float overshoot = Mathf.Sin(t * Mathf.PI) * m_GrowOvershoot;
                float scale = bubble.radius * (eased + overshoot);

                if (scale < 1e-4f)
                {
                    continue;
                }

                if (!m_DisableCulling && camera != null)
                {
                    // 球体剔除：比 Renderer 的包围盒剔除便宜得多，而且这里是手动绘制的
                    Vector3 toCamera = bubble.position - camera.transform.position;
                    float distance = toCamera.magnitude;
                    if (distance > scale + camera.farClipPlane)
                    {
                        continue;
                    }

                    // 在相机背后且离得比半径远 → 看不见
                    if (distance > scale &&
                        Vector3.Dot(camera.transform.forward, toCamera) < -scale)
                    {
                        continue;
                    }
                }

                m_Matrices[count] = Matrix4x4.TRS(
                    bubble.position,
                    Quaternion.identity,
                    new Vector3(scale, scale, scale));

                // 每颗泡泡的差异：相位 / 扰动种子 / 色调偏移 / 尺寸微差。
                //
                // 种子必须用**布局里的稳定下标**（m_Active[i]），不能用这里的 count：
                // count 会被视锥剔除改写，用它的后果是「镜头一动，泡泡的相位就跳一下」。
                // 每颗泡泡的差异：相位 / 扰动种子 / 色调偏移 / 缩放。
                // 这些值在布局阶段就算好了，这里只是打包 —— 每帧零哈希开销。
                m_Params[count] = BuildInstanceParams(bubble);

                // 颜色：把色板取出来的那一对，按 _TintMode 分配到两条通道上。
                // 不启用的那一条填白色 —— 乘上去等于没改，不需要分支。
                ResolveTintArrays(bubble.tintIndex,
                                  out m_TintA[count], out m_TintB[count]);

                count++;
            }

            if (count == 0)
            {
                return;
            }

            // ---- 提交 ----
            // DrawMeshInstanced 单次上限 1023，这里一次就够（泡泡最多两百来颗）。
            m_PropertyBlock.Clear();
            m_PropertyBlock.SetVectorArray(s_ParamsId, m_Params);
            m_PropertyBlock.SetVectorArray(s_TintAId, m_TintA);
            m_PropertyBlock.SetVectorArray(s_TintBId, m_TintB);

            Graphics.DrawMeshInstanced(
                m_Mesh, 0, material, m_Matrices, count, m_PropertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                false,   // 不接收阴影
                0,       // 所有层
                camera,
                UnityEngine.Rendering.LightProbeUsage.Off,
                null);

            m_LastActiveCount = count;
        }

        /// <summary>
        /// 当前用来推进泡泡的「剧情时间」。
        ///
        /// 【为什么要单独抽一个函数】
        /// BubbleTideDirector 在通关/失败之后**仍然会把 m_Elapsed 往上加**
        /// （它自己的注释说明了：为了让玩家继续看潮水淹满）。
        /// 但泡泡不应该跟着长 —— 玩家已经过关了，屏幕上还在冒新泡泡很奇怪。
        ///
        /// 勾上 FreezeAfterFinish 之后，这里会在结束的那一刻把时间**钉住**，
        /// 于是泡泡停在当前进度不动；取消勾选就恢复成跟着潮水走。
        /// </summary>
        private float ResolveElapsed()
        {
            if (m_Director == null)
            {
                return 0f;
            }

            if (m_FreezeAfterFinish && m_Director.IsFinished)
            {
                if (m_FreezeElapsed < 0f)
                {
                    // 刚结束的这一帧：记住当时的进度
                    m_FreezeElapsed = m_Director.Elapsed;
                }

                return m_FreezeElapsed;
            }

            // 还没结束（或者关掉了冻结）→ 清掉冻结值，继续跟着潮水走
            m_FreezeElapsed = -1f;
            return m_Director.Elapsed;
        }

        /// <summary>
        /// 每颗泡泡的四个差异值：相位 / 扰动种子 / 色调偏移 / 缩放。
        ///
        /// 这些值在**布局阶段**就用确定性哈希算好并存在 PlacedBubble 里了，
        /// 这里只是把它们打包成 float4。所以每帧不产生任何哈希开销，
        /// 而且和布局用的是同一个随机源 —— 不会出现「布局一套、颜色另一套」。
        /// </summary>
        private static Vector4 BuildInstanceParams(PlacedBubble bubble)
        {
            const float scaleBias = 1f;   // 预留：目前不额外缩放

            return new Vector4(
                bubble.phase,
                bubble.wobbleSeed,
                bubble.tintShift,
                scaleBias);
        }

        private static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)(salt * 2654435761u);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private Material ResolveMaterial()
        {
            if (m_Material != null)
            {
                return m_Material;
            }

            if (m_RuntimeMaterial != null)
            {
                return m_RuntimeMaterial;
            }

            if (m_Shader == null)
            {
                return null;
            }

            m_RuntimeMaterial = new Material(m_Shader);
            m_RuntimeMaterial.name = "BubbleTideToon (运行时生成)";
            m_RuntimeMaterial.enableInstancing = true;

            Debug.LogWarning("[泡泡潮·卡通] 没有指定材质，已在运行时用 Shader.Find 建了一个。\n" +
                             "发布版里 Shader.Find 可能失败，建议用菜单\n" +
                             "「工具 → PAO → 泡泡潮 → 新建卡通泡泡材质」建好再拖进 Inspector。");
            return m_RuntimeMaterial;
        }

        private static readonly int s_ParamsId = Shader.PropertyToID("_TideInstanceParams");
        private static readonly int s_TintAId  = Shader.PropertyToID("_TideTintA");
        private static readonly int s_TintBId  = Shader.PropertyToID("_TideTintB");

        // ==================================================================
        // 调试
        // ==================================================================

        private void OnDrawGizmosSelected()
        {
            if (!m_DrawGizmos || m_Bubbles.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_Bubbles.Count; i++)
            {
                PlacedBubble bubble = m_Bubbles[i];

                // 已出现的画实心、还没出现的画虚的，一眼看出生长顺序
                float elapsed = Application.isPlaying && m_Director != null ? m_Director.Elapsed : 0f;
                bool appeared = elapsed >= bubble.appearTime;

                Gizmos.color = appeared
                    ? new Color(0.45f, 0.85f, 1f, 0.35f)
                    : new Color(1f, 0.75f, 0.35f, 0.18f);

                Gizmos.DrawWireSphere(bubble.position, bubble.radius);
            }
        }

        private void OnGUI()
        {
            if (!m_DrawHud)
            {
                return;
            }

            string text;

            if (m_Field == null || !m_Field.IsBaked)
            {
                text = "泡泡潮·卡通：等待潮水场烤制";
            }
            else if (m_Bubbles.Count == 0)
            {
                text = "泡泡潮·卡通：布局为空（检查 BubbleTideDirector 的包围盒与障碍层）";
            }
            else
            {
                text = string.Format(
                    "泡泡潮·卡通  {0}/{1} 颗   半径 {2:F2} 米   网格 {3} 面",
                    m_Active.Count,
                    m_Bubbles.Count,
                    m_Bubbles[0].radius,
                    BubbleIcosphere.GetTriangleCount(m_Subdivisions));
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.normal.textColor = new Color(0.55f, 0.9f, 1f);

            GUI.Label(new Rect(13f, 40f, 900f, 24f), text, style);
        }
    }
}
