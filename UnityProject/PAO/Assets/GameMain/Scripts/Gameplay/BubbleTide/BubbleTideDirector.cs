using System;
using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡潮的关卡状态。
    /// </summary>
    public enum BubbleTideState
    {
        /// <summary>还没开始计时（等待触发）。</summary>
        Idle = 0,

        /// <summary>潮水正在蔓延，玩家还有时间。</summary>
        Flooding = 1,

        /// <summary>终点已经被淹没，进入最后的宽限倒计时。</summary>
        GoalFlooded = 2,

        /// <summary>玩家在潮水摸到终点之前（或在宽限期内）到达了终点。</summary>
        Won = 3,

        /// <summary>宽限时间耗尽，失败。</summary>
        Lost = 4,

        /// <summary>场还没烤好，或者烤制失败。</summary>
        NotReady = 5,
    }

    /// <summary>
    /// 泡泡潮总控。整个功能只需要在场景里放这一个组件。
    ///
    /// 【它做什么】
    ///   1. 关卡开始时把八个角 + 障碍物烤成一张「第几秒被淹」的场（只烤一次）
    ///   2. 每帧推进计时，把场导出成 3D 纹理喂给渲染
    ///   3. 判定终点是否被淹、玩家是否到达、宽限时间是否耗尽
    ///   4. 对外提供「反制」接口：炸弹炸洞 / 黏浮泡泡筑墙 / 弹力泡泡抽水
    ///
    /// 【它不做什么】
    ///   不生成任何泡泡 GameObject。潮水是场 + 着色器，不是实体。
    ///   这一点是性能的关键：房间多大、淹了多深，每帧的 CPU 开销都是常数。
    ///
    /// 【挂哪】
    ///   挂在一个空物体上（比如叫 BubbleTide），不要挂在终点上。
    ///   八个角各自挂 BubbleTideAnchor，终点挂 BubbleTideGoal。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/潮水总控")]
    public sealed class BubbleTideDirector : MonoBehaviour
    {
        // ==================================================================
        // 单例
        // ==================================================================

        private static BubbleTideDirector s_Instance;

        /// <summary>当前场景里的总控。没有则为 null —— 调用方要自己判空。</summary>
        public static BubbleTideDirector Instance { get { return s_Instance; } }

        /// <summary>潮水场的全局 3D 纹理，渲染 Feature 直接读它。没有潮水时为 null。</summary>
        public static Texture3D FieldTexture { get; private set; }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("烤制")]
        [Tooltip("体素边长（米）。0.5 大约 15 万格、烤制 0.3~1 秒；\n" +
                 "1.0 只有约 1.9 万格、快到几乎无感，但潮水边缘会明显变方。")]
        [SerializeField, Range(0.25f, 2f)] private float m_CellSize = 0.5f;

        [Tooltip("哪些层算实心障碍物。默认全部 —— 泡泡潮会绕开所有碰撞体。")]
        [SerializeField] private LayerMask m_ObstacleMask = ~0;

        [Tooltip("障碍物膨胀量（米）。调大能让泡泡不贴着薄墙渗过去，调大太多会堵死窄门。")]
        [SerializeField, Range(0f, 0.5f)] private float m_ObstaclePadding = 0f;

        [Tooltip("允许走斜线。关掉的话潮水只能沿轴向扩散，边缘会非常方正。")]
        [SerializeField] private bool m_AllowDiagonal = true;

        [Tooltip("包围盒外扩（米）。八个角通常正好嵌在墙角，留一点余量免得第一格就是实心。")]
        [SerializeField, Range(0f, 3f)] private float m_BoundsPadding = 0.5f;

        [Tooltip("手动指定潮水覆盖的范围，而不是由八个角自动推算。\n" +
                 "**建议开启**：自动推算要等八个角摆好才有意义，而且房间不是标准长方体时容易偏。")]
        [SerializeField] private bool m_UseExplicitBounds = true;

        [Tooltip("潮水覆盖范围的最小角（世界坐标）。")]
        [SerializeField] private Vector3 m_BoundsMin = new Vector3(-22f, -3.5f, -10f);

        [Tooltip("潮水覆盖范围的最大角（世界坐标）。")]
        [SerializeField] private Vector3 m_BoundsMax = new Vector3(19.5f, 9.5f, 23.5f);

        [Header("速度")]
        [Tooltip("潮水基准速度（米/秒）。1 米/秒 = 43 米的房间大约 40 秒被淹穿。")]
        [SerializeField, Range(0.2f, 12f)] private float m_BaseSpeed = 1f;

        [Tooltip("距离指数 alpha。\n" +
                 "  0   = 八个角速度一样，最远的角最晚到\n" +
                 "  0.7 = 远角明显更快，但近角仍然先到（推荐）\n" +
                 "  1   = 八个角同时抵达终点（仪式感最强，但中段会突然一起到）")]
        [SerializeField, Range(0f, 1f)] private float m_SpeedExponent = 0.7f;

        [Tooltip("速度倍率下限。防止长条形房间里近角慢得像停了。")]
        [SerializeField, Range(0.05f, 1f)] private float m_MinSpeedScale = 0.5f;

        [Tooltip("速度倍率上限。防止远角快得看起来很假。")]
        [SerializeField, Range(1f, 6f)] private float m_MaxSpeedScale = 3f;

        [Header("时间")]
        [Tooltip("自动开始计时（进 Play 就开始）。关掉则等你调 StartTide()。")]
        [SerializeField] private bool m_AutoStart = true;

        [Tooltip("开局的额外宽限（秒）。给玩家一点认路的时间，这段时间潮水先不动。")]
        [SerializeField, Range(0f, 60f)] private float m_StartDelay = 3f;

        [Tooltip("整条潮水的时间流速。调试用，0.5 = 慢放，3 = 快进。")]
        [SerializeField, Range(0f, 20f)] private float m_TimeScale = 1f;

        [Header("玩家")]
        [Tooltip("玩家对象。留空则自动找带 PlayerController 的物体。")]
        [SerializeField] private Transform m_Player;

        [Tooltip("玩家被潮水推动的力度（米/秒）。0 = 只判定不推。")]
        [SerializeField, Range(0f, 20f)] private float m_PushSpeed = 3.5f;

        [Tooltip("潮水面到玩家多近就开始推（米）。提前一点推手感更自然。")]
        [SerializeField, Range(0f, 3f)] private float m_PushLookAhead = 0.5f;

        [Header("反制")]
        [Tooltip("允许炸弹泡泡在潮水里炸出一个洞。")]
        [SerializeField] private bool m_EnableBombCounter = true;

        [Tooltip("炸弹炸洞时，范围内潮水被推迟多少秒。")]
        [SerializeField, Range(0f, 60f)] private float m_BombDelaySeconds = 6f;

        [Tooltip("炸洞半径相对炸弹爆炸半径的倍率。")]
        [SerializeField, Range(0.1f, 3f)] private float m_BombDelayRadiusScale = 1.2f;

        [Tooltip("允许黏浮泡泡粘住后变成一道墙，让周围潮水变慢。")]
        [SerializeField] private bool m_EnableStickyCounter = true;

        [Tooltip("黏浮泡泡墙每次生效时，范围内潮水被推迟多少秒。")]
        [SerializeField, Range(0f, 60f)] private float m_StickyDelaySeconds = 10f;

        [Tooltip("黏浮泡泡墙的影响半径（米）。")]
        [SerializeField, Range(0.5f, 20f)] private float m_StickyDelayRadius = 3f;

        [Header("调试")]
        [Tooltip("在 Scene 视图里画出体素网格与包围盒。")]
        [SerializeField] private bool m_DrawGridGizmo = true;

        [Tooltip("在 Game 视图左上角画潮水状态。")]
        [SerializeField] private bool m_DrawHud = true;

        [Tooltip("调试快捷键：1 = 时间快进 10 秒，2 = 时间倒回 10 秒，3 = 直接淹到终点。")]
        [SerializeField] private bool m_EnableDebugKeys = true;

        // ==================================================================
        // 运行时状态
        // ==================================================================

        private BubbleTideField m_Field;
        private BubbleTideAnchor[] m_Anchors;
        private BubbleTideGoal[] m_Goals;

        private float m_Elapsed;            // 已经过的潮水时间（秒，已含速度倍率）
        private float m_GraceRemaining;     // 终点被淹后剩下的宽限时间
        private bool m_Started;

        private Texture3D m_Texture;
        private byte[] m_VolumeCache;

        private BubbleTideCellRenderer m_CellRenderer;
        private BubbleTidePush m_Push;

        // 玩家位置缓存：CharacterController 在泡泡里时 transform 会被钉到泡泡中心，
        // 这里每帧取一次就够了，不用到处 GetComponent
        private Transform m_PlayerTransform;

        // ==================================================================
        // 对外查询
        // ==================================================================

        /// <summary>当前状态。</summary>
        public BubbleTideState State { get; private set; }

        /// <summary>潮水已经推进了多少秒（不含 StartDelay 之前的等待）。</summary>
        public float Elapsed { get { return m_Elapsed; } }

        /// <summary>终点被淹的时刻（秒）。0 表示还没烤出来。</summary>
        public float Deadline { get; private set; }

        /// <summary>距离终点被淹还剩多少秒。负数表示已经过了。</summary>
        public float TimeUntilGoalFlooded
        {
            get { return Deadline > 0f ? Deadline - m_Elapsed : 0f; }
        }

        /// <summary>终点被淹之后的宽限倒计时还剩多少秒。</summary>
        public float GraceRemaining { get { return m_GraceRemaining; } }

        /// <summary>潮水在终点之前的总进度 0~1。1 = 刚好摸到终点。</summary>
        public float Progress
        {
            get
            {
                if (Deadline <= 0.0001f)
                {
                    return 0f;
                }

                return Mathf.Clamp01(m_Elapsed / Deadline);
            }
        }

        /// <summary>整条潮水把整个房间淹满的进度 0~1。</summary>
        public float FloodFillProgress
        {
            get
            {
                if (m_Field == null || !m_Field.IsBaked || m_Field.ReachableCellCount <= 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)m_Field.FloodedCellCount / m_Field.ReachableCellCount);
            }
        }

        /// <summary>潮水场。可能为 null（还没烤完）。</summary>
        public BubbleTideField Field { get { return m_Field; } }

        /// <summary>是不是已经结束（通关或失败）。</summary>
        public bool IsFinished
        {
            get { return State == BubbleTideState.Won || State == BubbleTideState.Lost; }
        }

        // ==================================================================
        // 事件
        // ==================================================================

        /// <summary>场烤制完成。参数是诊断文本。</summary>
        public event Action<string> Baked;

        /// <summary>潮水摸到终点的那一刻。</summary>
        public event Action GoalFlooded;

        /// <summary>玩家通关。</summary>
        public event Action Won;

        /// <summary>宽限时间耗尽，失败。</summary>
        public event Action Lost;

        // ==================================================================
        // 生命周期
        // ==================================================================

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Debug.LogWarning("[泡泡潮] 场景里有多个 BubbleTideDirector，只保留第一个。" +
                                 "请删掉多余的：" + s_Instance.gameObject.name);
                enabled = false;
                return;
            }

            s_Instance = this;
            State = BubbleTideState.NotReady;
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
                FieldTexture = null;
            }

            if (m_Texture != null)
            {
                Destroy(m_Texture);
                m_Texture = null;
            }
        }

        private void Start()
        {
            Bake();

            if (m_AutoStart)
            {
                StartTide();
            }
        }

        private void Update()
        {
            if (!m_Started || m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            HandleDebugKeys();

            float delta = Time.deltaTime * Mathf.Max(0f, m_TimeScale);

            // StartDelay 是「开局宽限」，这段时间潮水完全不动
            if (m_Elapsed <= 0f && m_StartDelay > 0f)
            {
                m_StartDelayRemaining -= delta;
                if (m_StartDelayRemaining > 0f)
                {
                    return;
                }

                delta = -m_StartDelayRemaining;   // 把剩下的不足一帧的时间补上
                m_StartDelayRemaining = 0f;
            }

            if (IsFinished)
            {
                // 结束后仍然推进画面，只是不再判定，方便玩家看潮水淹满
                m_Elapsed += delta;
                UpdateFieldAndVisual();
                return;
            }

            m_Elapsed += delta;

            UpdateFieldAndVisual();
            UpdateGoalState(delta);
            UpdatePlayerReached();
        }

        private float m_StartDelayRemaining;

        // ==================================================================
        // 烤制
        // ==================================================================

        /// <summary>
        /// 烤制潮水场。正常情况下由 Start 自动调用；
        /// 想在加载画面里提前烤就手动调它。
        /// </summary>
        public void Bake()
        {
            if (m_Field != null && m_Field.IsBaked)
            {
                return;
            }

            CollectSceneReferences();

            if (m_Anchors == null || m_Anchors.Length == 0)
            {
                Debug.LogError("[泡泡潮] 场景里找不到任何 BubbleTideAnchor（潮水源）。" +
                               "潮水不会出现。请用菜单「工具 → PAO → 泡泡潮 → 一键搭建潮水」生成八个角。");
                State = BubbleTideState.NotReady;
                return;
            }

            if (m_Goals == null || m_Goals.Length == 0)
            {
                Debug.LogError("[泡泡潮] 场景里找不到 BubbleTideGoal（终点）。" +
                               "没有终点就没有截止时刻，潮水会一直淹下去。");
                State = BubbleTideState.NotReady;
                return;
            }

            // 终点只用第一个。多个终点是后续扩展，现在先把判定做扎实。
            Vector3 goalPosition = m_Goals[0].transform.position;

            // ---- 包围盒：由八个角自己决定 ----
            Bounds bounds = ComputeBounds();

            // ---- 每个角的速度倍率（把出发延迟折算进去）----
            Vector3[] seedPositions = new Vector3[m_Anchors.Length];
            float[] speedMultipliers = new float[m_Anchors.Length];

            for (int i = 0; i < m_Anchors.Length; i++)
            {
                seedPositions[i] = m_Anchors[i].transform.position;

                float distance = Vector3.Distance(seedPositions[i], goalPosition);
                speedMultipliers[i] = m_Anchors[i].GetEffectiveMultiplier(m_BaseSpeed, distance);
            }

            BubbleTideField.BakeSettings settings = new BubbleTideField.BakeSettings
            {
                cellSize = m_CellSize,
                obstacleMask = m_ObstacleMask,
                baseSpeed = m_BaseSpeed,
                speedExponent = m_SpeedExponent,
                minSpeedScale = m_MinSpeedScale,
                maxSpeedScale = m_MaxSpeedScale,
                allowDiagonal = m_AllowDiagonal,
                obstaclePadding = m_ObstaclePadding,
            };

            float beginTime = Time.realtimeSinceStartup;

            m_Field = new BubbleTideField();
            m_Field.Bake(bounds, seedPositions, speedMultipliers, goalPosition, settings);

            if (!m_Field.IsBaked)
            {
                State = BubbleTideState.NotReady;
                return;
            }

            Deadline = m_Field.Deadline;

            for (int i = 0; i < m_Goals.Length; i++)
            {
                m_Goals[i].FloodDeadline = Deadline;
                m_Goals[i].IsFlooded = false;
                m_Goals[i].IsReached = false;
            }

            BuildFieldTexture();
            EnsureVisualComponents();

            if (m_CellRenderer != null)
            {
                m_CellRenderer.Bind(m_Field, m_Anchors);
            }

            if (m_Push != null)
            {
                m_Push.Bind(m_Field, m_Player);
            }

            float cost = (Time.realtimeSinceStartup - beginTime) * 1000f;
            State = BubbleTideState.Idle;

            Debug.Log(string.Format(
                "[泡泡潮] 烤制完成，耗时 {0:F0} 毫秒。{1}\n  截止时刻 {2:F1} 秒（终点被淹）",
                cost, m_Field.BakeReport, Deadline));

            if (Baked != null)
            {
                Baked(m_Field.BakeReport);
            }
        }

        /// <summary>
        /// 算出潮水要覆盖的范围。
        ///
        /// 优先用手动指定的范围：由八个角自动推算听起来很聪明，但房间不是标准长方体时
        /// 容易算歪，而且必须先把八个角摆好才有意义 —— 手动指定更可控也更好排错。
        /// </summary>
        private Bounds ComputeBounds()
        {
            if (m_UseExplicitBounds)
            {
                Bounds explicitBounds = new Bounds();
                explicitBounds.SetMinMax(m_BoundsMin, m_BoundsMax);
                return explicitBounds;
            }

            bool hasAny = false;
            Bounds bounds = new Bounds();

            for (int i = 0; i < m_Anchors.Length; i++)
            {
                Vector3 p = m_Anchors[i].transform.position;

                if (!hasAny)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            }

            // 八个角是「点」，包围盒会是零厚度 —— 外扩一点，否则高度只有 1 格。
            bounds.Expand(m_BoundsPadding * 2f);

            // 保险：某个轴完全没有厚度时给一个最小值，避免出现 1 格的网格
            Vector3 size = bounds.size;
            const float minExtent = 2f;
            if (size.x < minExtent) size.x = minExtent;
            if (size.y < minExtent) size.y = minExtent;
            if (size.z < minExtent) size.z = minExtent;
            bounds.size = size;

            return bounds;
        }

        /// <summary>
        /// 供编辑器工具读取当前的生效范围，用来在 Scene 里画框、放八个角。
        /// </summary>
        public Bounds GetEffectiveBounds()
        {
#if UNITY_2023_1_OR_NEWER
            m_Anchors = FindObjectsByType<BubbleTideAnchor>(FindObjectsSortMode.None);
#else
            m_Anchors = FindObjectsOfType<BubbleTideAnchor>();
#endif
            return ComputeBounds();
        }

        /// <summary>由编辑器工具写入手动范围。</summary>
        public void SetExplicitBounds(Vector3 min, Vector3 max)
        {
            m_UseExplicitBounds = true;
            m_BoundsMin = min;
            m_BoundsMax = max;
        }

        /// <summary>手动范围的开关。编辑器工具建完之后可以把它关掉，改回自动推算。</summary>
        public bool UseExplicitBounds
        {
            get { return m_UseExplicitBounds; }
            set { m_UseExplicitBounds = value; }
        }

        /// <summary>
        /// 把归一化的场导出成 3D 纹理。只在烤制后做一次，运行期不动。
        /// </summary>
        private void BuildFieldTexture()
        {
            byte[] volume = m_Field.BuildNormalizedVolume();
            if (volume == null)
            {
                return;
            }

            m_VolumeCache = volume;

            if (m_Texture != null)
            {
                Destroy(m_Texture);
                m_Texture = null;
            }

            // R8：单通道字节。配合 NormalizeScale 已经够表达「第几秒被淹」，
            // 而且 15 万格只占 150 KB —— 值得为它省下显存。
            m_Texture = new Texture3D(
                m_Field.SizeX, m_Field.SizeY, m_Field.SizeZ,
                TextureFormat.R8, false);

            m_Texture.name = "BubbleTideField";
            m_Texture.wrapMode = TextureWrapMode.Clamp;
            m_Texture.filterMode = FilterMode.Bilinear;
            m_Texture.SetPixelData(volume, 0);
            m_Texture.Apply(false, false);

            FieldTexture = m_Texture;

            // 给渲染 Feature 用的全局参数：把「格子索引」映射回世界空间。
            // 先在这里设一次，让场在 Director 之外也能被查询；
            // 每帧由 Render Pass 再绑一次（RenderGraph 路径会重置全局纹理绑定）。
            Shader.SetGlobalTexture(Shader.PropertyToID("_BubbleTideField"), m_Texture);
            Shader.SetGlobalVector(Shader.PropertyToID("_BubbleTideOrigin"), m_Field.Origin);
            Shader.SetGlobalVector(Shader.PropertyToID("_BubbleTideSize"),
                new Vector4(m_Field.SizeX, m_Field.SizeY, m_Field.SizeZ, m_CellSize));
        }

        /// <summary>
        /// 自动把渲染与推挤组件挂上，让「只放一个 Director」也能跑起来。
        /// </summary>
        private void EnsureVisualComponents()
        {
            m_CellRenderer = GetComponent<BubbleTideCellRenderer>();
            if (m_CellRenderer == null)
            {
                m_CellRenderer = gameObject.AddComponent<BubbleTideCellRenderer>();
            }

            m_Push = GetComponent<BubbleTidePush>();
            if (m_Push == null)
            {
                m_Push = gameObject.AddComponent<BubbleTidePush>();
            }

            m_Push.Configure(m_PushSpeed, m_PushLookAhead, m_Player);
        }

        private void CollectSceneReferences()
        {
#if UNITY_2023_1_OR_NEWER
            m_Anchors = FindObjectsByType<BubbleTideAnchor>(FindObjectsSortMode.None);
            m_Goals = FindObjectsByType<BubbleTideGoal>(FindObjectsSortMode.None);
#else
            m_Anchors = FindObjectsOfType<BubbleTideAnchor>();
            m_Goals = FindObjectsOfType<BubbleTideGoal>();
#endif

            if (m_Player == null)
            {
                PAO.PlayerController controller = FindObjectOfType<PAO.PlayerController>();
                if (controller != null)
                {
                    m_Player = controller.transform;
                }
            }

            m_PlayerTransform = m_Player;
        }

        // ==================================================================
        // 每帧推进
        // ==================================================================

        private void UpdateFieldAndVisual()
        {
            int newlyFlooded = m_Field.AdvanceFrontier(m_Elapsed);

            if (m_CellRenderer != null)
            {
                m_CellRenderer.UpdateFlood(m_Elapsed, newlyFlooded);
            }
        }

        private void UpdateGoalState(float delta)
        {
            bool anyFlooded = false;

            for (int i = 0; i < m_Goals.Length; i++)
            {
                BubbleTideGoal goal = m_Goals[i];
                if (goal.IsFlooded)
                {
                    anyFlooded = true;
                    continue;
                }

                float arrival = m_Field.SampleArrivalTime(goal.transform.position);
                if (m_Elapsed < arrival)
                {
                    continue;
                }

                goal.IsFlooded = true;
                anyFlooded = true;

                // 宽限时间取所有终点里最短的那个，最严格
                m_GraceRemaining = goal.GraceSeconds;

                Debug.Log(string.Format(
                    "[泡泡潮] 终点「{0}」已被淹没（第 {1:F1} 秒）。宽限 {2:F1} 秒。",
                    goal.name, m_Elapsed, goal.GraceSeconds));
            }

            if (!anyFlooded)
            {
                if (State == BubbleTideState.Idle || State == BubbleTideState.Flooding)
                {
                    State = BubbleTideState.Flooding;
                }

                return;
            }

            if (State != BubbleTideState.GoalFlooded)
            {
                State = BubbleTideState.GoalFlooded;

                if (GoalFlooded != null)
                {
                    GoalFlooded();
                }
            }

            // 宽限倒计时。注意这一段【不能】放进上面的 else 分支里 ——
            // 那样只有「已经是 GoalFlooded」的下一帧才会开始扣，
            // 而第一帧刚切换状态时不会扣，倒计时会永远差一帧、也就永远不会归零。
            m_GraceRemaining -= delta;

            if (m_GraceRemaining <= 0f)
            {
                Lose();
            }
        }

        private void UpdatePlayerReached()
        {
            if (State == BubbleTideState.Won || m_PlayerTransform == null || m_Goals == null)
            {
                return;
            }

            for (int i = 0; i < m_Goals.Length; i++)
            {
                BubbleTideGoal goal = m_Goals[i];

                float distance = Vector3.Distance(m_PlayerTransform.position, goal.transform.position);
                if (distance > goal.ReachDistance)
                {
                    continue;
                }

                if (goal.RequireConfirmKey && !Input.GetKeyDown(goal.ConfirmKey))
                {
                    continue;
                }

                Win(goal);
                return;
            }
        }

        // ==================================================================
        // 状态切换
        // ==================================================================

        /// <summary>开始计时。AutoStart 关掉时由外部调用。</summary>
        public void StartTide()
        {
            if (m_Field == null || !m_Field.IsBaked)
            {
                Debug.LogWarning("[泡泡潮] 场还没烤好，无法开始计时。");
                return;
            }

            m_Started = true;
            m_Elapsed = 0f;
            m_StartDelayRemaining = m_StartDelay;
            State = BubbleTideState.Flooding;
        }

        /// <summary>玩家通关。</summary>
        public void Win(BubbleTideGoal goal)
        {
            if (IsFinished)
            {
                return;
            }

            if (goal != null)
            {
                goal.IsReached = true;
            }

            State = BubbleTideState.Won;

            Debug.Log(string.Format("[泡泡潮] 通关！用时 {0:F1} 秒。", m_Elapsed));

            if (Won != null)
            {
                Won();
            }
        }

        /// <summary>强制失败。</summary>
        public void Lose()
        {
            if (IsFinished)
            {
                return;
            }

            State = BubbleTideState.Lost;

            Debug.Log(string.Format("[泡泡潮] 失败：宽限时间耗尽（第 {0:F1} 秒）。", m_Elapsed));

            if (Lost != null)
            {
                Lost();
            }
        }

        /// <summary>
        /// 把时间往前拨（秒）。调试与「跳过开场」用。
        /// </summary>
        public void SkipTime(float seconds)
        {
            if (m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            m_Elapsed = Mathf.Max(0f, m_Elapsed + seconds);
            UpdateFieldAndVisual();
        }

        /// <summary>把时间定格在某个绝对秒数。调试用。</summary>
        public void SetTime(float seconds)
        {
            if (m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            m_Elapsed = Mathf.Max(0f, seconds);
            UpdateFieldAndVisual();
        }

        // ==================================================================
        // 反制接口
        // ==================================================================

        /// <summary>
        /// 在潮水里炸出一个洞：范围内所有格子的被淹时间往后推。
        /// 由炸弹泡泡的爆炸调用。
        /// </summary>
        /// <returns>受影响的格子数。0 表示没生效（没烤好、或功能被关了）。</returns>
        public int CreateHole(Vector3 center, float radius, float delaySeconds)
        {
            if (!m_EnableBombCounter || m_Field == null || !m_Field.IsBaked)
            {
                return 0;
            }

            int touched = m_Field.DelayFloodInRadius(center, radius, delaySeconds);

            if (touched > 0 && m_CellRenderer != null)
            {
                m_CellRenderer.MarkFieldDirty();
            }

            return touched;
        }

        /// <summary>
        /// 炸弹泡泡爆炸时的标准入口：半径按 Inspector 里的倍率自动放大。
        /// </summary>
        public int OnBombExploded(Vector3 center, float blastRadius)
        {
            if (!m_EnableBombCounter)
            {
                return 0;
            }

            float radius = Mathf.Max(0.5f, blastRadius * m_BombDelayRadiusScale);
            int touched = CreateHole(center, radius, m_BombDelaySeconds);

            if (touched > 0)
            {
                Debug.Log(string.Format(
                    "[泡泡潮] 炸弹在第 {0:F1} 秒炸出一个洞：{1} 格潮水被推迟 {2:F1} 秒。",
                    m_Elapsed, touched, m_BombDelaySeconds));
            }

            return touched;
        }

        /// <summary>
        /// 黏浮泡泡粘住变成墙：周围潮水变慢。由 Bubble 的 BecomeStuck / StickToSurface 调用。
        /// </summary>
        public int OnStickyBubbleAnchored(Vector3 center, float radius)
        {
            if (!m_EnableStickyCounter || m_Field == null || !m_Field.IsBaked)
            {
                return 0;
            }

            float useRadius = radius > 0.01f ? radius : m_StickyDelayRadius;
            int touched = m_Field.DelayFloodInRadius(center, useRadius, m_StickyDelaySeconds);

            if (touched > 0)
            {
                if (m_CellRenderer != null)
                {
                    m_CellRenderer.MarkFieldDirty();
                }

                Debug.Log(string.Format(
                    "[泡泡潮] 黏浮泡泡在 {0} 处筑墙：{1} 格潮水被推迟 {2:F1} 秒。",
                    center, touched, m_StickyDelaySeconds));
            }

            return touched;
        }

        /// <summary>
        /// 弹力泡泡被地形泡泡吸收：等于从潮水里抽走一份容量，整条潮水整体变慢。
        /// 做法是把所有格子的到达时间乘一个大于 1 的系数。
        /// </summary>
        /// <param name="slowdownRatio">每抽一份慢多少，0.02 = 慢 2%。</param>
        public void OnBubbleConsumedByTerrain(float slowdownRatio)
        {
            if (m_Field == null || !m_Field.IsBaked || slowdownRatio <= 0f)
            {
                return;
            }

            m_Field.ScaleAllArrivalTimes(1f + slowdownRatio);

            if (m_CellRenderer != null)
            {
                m_CellRenderer.MarkFieldDirty();
            }
        }

        // ==================================================================
        // 调试
        // ==================================================================

        private void HandleDebugKeys()
        {
            if (!m_EnableDebugKeys)
            {
                return;
            }

            // 用主键盘数字，和泡泡类型切换的 1/2/3 冲突，
            // 所以这里用带修饰键的组合，免得玩的时候误触。
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.Equals))
                {
                    SkipTime(10f);
                    Debug.Log("[泡泡潮] 调试：快进到 " + m_Elapsed.ToString("F1") + " 秒");
                }
                else if (Input.GetKeyDown(KeyCode.Minus))
                {
                    SetTime(m_Elapsed - 10f);
                    Debug.Log("[泡泡潮] 调试：倒回到 " + m_Elapsed.ToString("F1") + " 秒");
                }
                else if (Input.GetKeyDown(KeyCode.Alpha0))
                {
                    SetTime(Deadline + 0.01f);
                    Debug.Log("[泡泡潮] 调试：直接跳到终点被淹。");
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!m_DrawGridGizmo)
            {
                return;
            }

            // 没烤过的时候用八个角现算一个包围盒，方便摆位时预览
            Bounds bounds;
            if (m_Field != null && m_Field.IsBaked)
            {
                bounds = m_Field.WorldBounds;
            }
            else
            {
#if UNITY_2023_1_OR_NEWER
                BubbleTideAnchor[] anchors = FindObjectsByType<BubbleTideAnchor>(FindObjectsSortMode.None);
#else
                BubbleTideAnchor[] anchors = FindObjectsOfType<BubbleTideAnchor>();
#endif
                if (anchors == null || anchors.Length == 0)
                {
                    return;
                }

                bounds = new Bounds(anchors[0].transform.position, Vector3.zero);
                for (int i = 1; i < anchors.Length; i++)
                {
                    bounds.Encapsulate(anchors[i].transform.position);
                }

                bounds.Expand(m_BoundsPadding * 2f);
            }

            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            if (m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            // 把当前潮水面的包围范围画出来，能直观看出潮水推到哪了
            if (m_Push != null)
            {
                Vector3 playerPosition = m_Push.PlayerPosition;
                float arrival = m_Field.SampleArrivalTime(playerPosition);
                if (!float.IsPositiveInfinity(arrival))
                {
                    bool flooded = m_Elapsed >= arrival;
                    Gizmos.color = flooded ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.4f, 1f, 0.5f, 0.9f);
                    Gizmos.DrawWireSphere(playerPosition, 0.6f);
                    Gizmos.DrawLine(playerPosition, playerPosition + m_Field.SampleFlowDirection(playerPosition) * 2f);
                }
            }
        }

        private void OnGUI()
        {
            if (!m_DrawHud)
            {
                return;
            }

            if (m_Field == null || !m_Field.IsBaked)
            {
                GUI.Label(new Rect(12f, 12f, 400f, 24f), "泡泡潮：尚未烤制");
                return;
            }

            string text;
            Color color;

            switch (State)
            {
                case BubbleTideState.Won:
                    text = string.Format("通关！用时 {0:F1} 秒", m_Elapsed);
                    color = new Color(0.45f, 1f, 0.55f);
                    break;

                case BubbleTideState.Lost:
                    text = "被泡泡淹没了";
                    color = new Color(1f, 0.35f, 0.35f);
                    break;

                case BubbleTideState.GoalFlooded:
                    text = string.Format("终点已淹没 —— 最后 {0:F1} 秒！", Mathf.Max(0f, m_GraceRemaining));
                    color = new Color(1f, 0.55f, 0.3f);
                    break;

                default:
                    text = string.Format(
                        "距离终点被淹 {0:F1} 秒   （已过 {1:F1}s / 全场 {2:F1}s）",
                        Mathf.Max(0f, TimeUntilGoalFlooded), m_Elapsed, m_Field.LastArrivalTime);
                    color = TimeUntilGoalFlooded < 10f
                        ? new Color(1f, 0.7f, 0.35f)
                        : new Color(1f, 1f, 1f);
                    break;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = color;

            GUI.Label(new Rect(13f, 13f, 900f, 28f), text, style);
        }
    }
}
