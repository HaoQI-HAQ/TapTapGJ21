using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡潮的体素场：把关卡离散成格子，算出「每一格在第几秒被泡泡淹到」。
    ///
    /// 【为什么是场，而不是真的生成泡泡去漂】
    /// 如果按「每个角定期生成泡泡、泡泡自己漂向终点」来做，会有两个绕不过去的问题：
    ///
    ///   1. 泡泡是物理体，会被卡住。玩家随便在角上放几个黏浮泡泡堆出一道堰塞湖，
    ///      这一路潮水就永远到不了终点了。潮水一旦能被卡住，它就不再是计时器，
    ///      而是可以被玩家玩坏的东西。
    ///   2. 要绕障碍物就得给每颗泡泡做寻路，几千颗泡泡寻路是不可行的。
    ///
    /// 所以这里先把「第几秒被淹到」这张表算出来，泡泡只是这张表的可视化。
    /// 绕墙、进门、爬台阶全部自然发生（Dijkstra 走的是真实路径，不是直线距离），
    /// 而且**每帧只是一次查表**，潮水大小完全不影响帧率。
    ///
    /// 【八条潮水怎么并行推进】
    /// 用**多源** Dijkstra：八个角同时作为起点、初始时间都是 0，
    /// 沿边的代价是「走这一格要多少秒」= 1 / 该角的速度。
    /// 因为每个角的速度是常数，所以一次遍历就把八条潮水一起解决了，不用跑八遍。
    /// 某个格子最终属于谁，取决于哪个角先到它。
    ///
    /// 【速度怎么定】
    ///     v_i = baseSpeed * speedMultiplier_i * (d_i / dMin)^alpha
    ///
    ///   d_i   = 第 i 个角到终点的直线距离
    ///   alpha = 0 → 八个角速度一样，最远的角最晚到（压迫感最弱）
    ///   alpha = 1 → 八个角同时抵达终点（仪式感最强，但中段会突然一起到）
    ///   0.7 是个好起点：远角明显更快，但近角仍然先到。
    ///
    ///   注意这里算速度用的是**直线距离**，而实际路径会被障碍物拉长。
    ///   所以某个角明明离终点近、却隔着一堵墙，它实际会晚到 ——
    ///   这个「意外」是有趣的，不用去修正，正好给关卡制造变化。
    ///
    /// 【单位】
    /// 内部时间单位是**秒**。输出给 GPU 时才归一化到 0~1（见 BuildNormalized），
    /// 这样 Dijkstra 的语义保持干净，不会被归一化污染。
    /// </summary>
    public sealed class BubbleTideField
    {
        // ---------------- 格子状态 ----------------

        /// <summary>格子里是实心障碍物。</summary>
        public const byte StateSolid = 0;

        /// <summary>格子是空的、能被泡泡淹到。</summary>
        public const byte StateAir = 1;

        /// <summary>格子是空的，但从八個角都走不到（封闭空腔）。永远不会被淹。</summary>
        public const byte StateSealed = 2;

        /// <summary>从来没有潮水到达（未烤制 / 不可达）。</summary>
        public const float NeverFlood = float.PositiveInfinity;

        private Vector3 m_Origin;           // 体素网格原点的世界坐标（第 0 格的中心）
        private Vector3 m_Max;              // 网格末端（用于 AABB 判断）
        private float m_CellSize;
        private int m_SizeX;
        private int m_SizeY;
        private int m_SizeZ;
        private int m_CellCount;

        private byte[] m_State;             // 实心 / 空 / 封闭
        private float[] m_ArrivalTime;      // 单位：秒。NeverFlood 表示不会被淹
        private int[] m_OwnerSeed;          // 被哪个角淹的（-1 = 没有）

        private float[] m_SortedTimes;      // 上面那堆时间的升序副本，用来做 O(1) 的分帧推进
        private int[] m_SortedCells;
        private int m_SortedCount;

        private int m_FrontierMinCell = -1; // 当前潮水面所在的格子范围，给光线步进用
        private int m_FrontierMaxCell = -1;

        private bool m_IsBaked;

        // ---------------- 对外只读属性 ----------------

        /// <summary>烤制是否完成。没烤完之前所有查询都返回无效值。</summary>
        public bool IsBaked { get { return m_IsBaked; } }

        /// <summary>格子的边长（米）。</summary>
        public float CellSize { get { return m_CellSize; } }

        public int SizeX { get { return m_SizeX; } }
        public int SizeY { get { return m_SizeY; } }
        public int SizeZ { get { return m_SizeZ; } }

        /// <summary>体素网格原点的世界坐标（第 0 格的中心）。</summary>
        public Vector3 Origin { get { return m_Origin; } }

        /// <summary>网格在世界空间里的包围盒。</summary>
        public Bounds WorldBounds
        {
            get
            {
                Vector3 size = new Vector3(m_SizeX, m_SizeY, m_SizeZ) * m_CellSize;
                Vector3 center = m_Origin + (size - Vector3.one * m_CellSize) * 0.5f;
                return new Bounds(center, size);
            }
        }

        /// <summary>所有可达格子中最晚被淹的时间（秒）。也就是「整个房间淹满」的时刻。</summary>
        public float LastArrivalTime { get; private set; }

        /// <summary>八个角里，最早摸到终点的时间（秒）。这就是关卡截止时刻。</summary>
        public float Deadline { get; private set; }

        /// <summary>
        /// 归一化用的除数：把「秒」映射到 0~1。
        /// 取 LastArrivalTime，于是 1.0 正好代表整个房间被淹满。
        /// </summary>
        public float NormalizeScale { get; private set; }

        /// <summary>
        /// 潮水的代表速度（米/秒），也就是烤制时传进来的基准速度。
        ///
        /// 用来把「还有多少秒」粗略换算成「还有多少米」。
        /// 不做精确反查，是因为八个角速度各不相同、场里也没有存速度场 ——
        /// 而调用方（推挤的提前量、UI 的距离显示）要的只是个量级。
        /// </summary>
        public float EstimatedSpeed { get; private set; }

        /// <summary>烤制诊断信息，出问题时看这个。</summary>
        public string BakeReport { get; private set; }

        // ==================================================================
        // 索引换算
        // ==================================================================

        private int Index(int x, int y, int z)
        {
            return x + m_SizeX * (y + m_SizeY * z);
        }

        private bool InRange(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < m_SizeX && y < m_SizeY && z < m_SizeZ;
        }

        /// <summary>世界坐标 → 格子坐标。可能落在网格外，调用方自己用 InRange 判断。</summary>
        public Vector3Int WorldToCell(Vector3 world)
        {
            Vector3 local = (world - m_Origin) / m_CellSize;
            return new Vector3Int(
                Mathf.FloorToInt(local.x + 0.5f),
                Mathf.FloorToInt(local.y + 0.5f),
                Mathf.FloorToInt(local.z + 0.5f));
        }

        /// <summary>格子坐标 → 格子中心的世界坐标。</summary>
        public Vector3 CellCenter(int x, int y, int z)
        {
            return m_Origin + new Vector3(x, y, z) * m_CellSize;
        }

        /// <summary>格子是不是实心障碍物。</summary>
        public bool IsSolid(int x, int y, int z)
        {
            if (m_State == null || !InRange(x, y, z))
            {
                return false;
            }

            return m_State[Index(x, y, z)] == StateSolid;
        }

        // ==================================================================
        // 烤制
        // ==================================================================

        /// <summary>
        /// 一次烤制参数。
        /// </summary>
        public struct BakeSettings
        {
            /// <summary>体素边长（米）。0.5 左右比较平衡；越小越精细但格数按立方增长。</summary>
            public float cellSize;

            /// <summary>参与碰撞的层。只有这些层上的碰撞体会被当成实心障碍。</summary>
            public LayerMask obstacleMask;

            /// <summary>潮水基准速度（米/秒）。</summary>
            public float baseSpeed;

            /// <summary>距离指数 alpha。见类注释。1 = 八路同时抵达。</summary>
            public float speedExponent;

            /// <summary>速度倍率的上下限，防止长条形房间出现夸张的速度。</summary>
            public float minSpeedScale;
            public float maxSpeedScale;

            /// <summary>允许走斜线（18 邻接）。关掉的话潮水只能沿轴向扩散，边缘会很方。</summary>
            public bool allowDiagonal;

            /// <summary>把障碍物膨胀一圈再判定。能避免泡泡贴着薄墙「渗」进隔壁。</summary>
            public float obstaclePadding;
        }

        /// <summary>
        /// 烤制潮水场。这是整个功能里唯一的重计算，**只在关卡开始时跑一次**。
        /// </summary>
        /// <param name="bounds">要覆盖的世界空间包围盒。一般传八个角的外接盒。</param>
        /// <param name="seedWorldPositions">八个角的世界坐标。</param>
        /// <param name="seedSpeedMultipliers">每个角的速度倍率，长度与上一参数一致。</param>
        /// <param name="goalWorldPosition">终点世界坐标，用来算各角的距离。</param>
        /// <param name="settings">烤制参数。</param>
        public void Bake(
            Bounds bounds,
            Vector3[] seedWorldPositions,
            float[] seedSpeedMultipliers,
            Vector3 goalWorldPosition,
            BakeSettings settings)
        {
            m_IsBaked = false;
            BakeReport = null;

            float cell = Mathf.Max(0.1f, settings.cellSize);
            m_CellSize = cell;

            m_SizeX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cell));
            m_SizeY = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cell));
            m_SizeZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cell));
            m_CellCount = m_SizeX * m_SizeY * m_SizeZ;

            // 原点取包围盒最小角 + 半格，这样第 (0,0,0) 格的中心正好贴着包围盒角。
            m_Origin = bounds.min + Vector3.one * (cell * 0.5f);
            m_Max = m_Origin + new Vector3(m_SizeX - 1, m_SizeY - 1, m_SizeZ - 1) * cell;

            if (m_State == null || m_State.Length != m_CellCount)
            {
                m_State = new byte[m_CellCount];
                m_ArrivalTime = new float[m_CellCount];
                m_OwnerSeed = new int[m_CellCount];
            }

            int solidCount = MarkSolidCells(settings.obstacleMask, settings.obstaclePadding);
            int airCount = m_CellCount - solidCount;

            // ---- 算八个角各自的速度 ----
            float dMin = float.MaxValue;
            float[] distances = new float[seedWorldPositions.Length];
            for (int i = 0; i < seedWorldPositions.Length; i++)
            {
                distances[i] = Vector3.Distance(seedWorldPositions[i], goalWorldPosition);
                if (distances[i] < dMin)
                {
                    dMin = distances[i];
                }
            }

            if (dMin < 1e-4f)
            {
                dMin = 1e-4f;   // 终点正好压在某个角上，防除零
            }

            float[] speeds = new float[seedWorldPositions.Length];
            float minScale = Mathf.Max(0.01f, settings.minSpeedScale);
            float maxScale = Mathf.Max(minScale, settings.maxSpeedScale);

            for (int i = 0; i < seedWorldPositions.Length; i++)
            {
                // 相对距离，最近的那个角是 1.0
                float ratio = Mathf.Max(1e-4f, distances[i] / dMin);

                // alpha = 0 → 距离不影响速度；alpha = 1 → 速度正比于距离（同时抵达）
                float scale = Mathf.Pow(ratio, Mathf.Clamp01(settings.speedExponent));
                scale = Mathf.Clamp(scale, minScale, maxScale);

                float multiplier = (seedSpeedMultipliers != null && i < seedSpeedMultipliers.Length)
                    ? Mathf.Max(0.01f, seedSpeedMultipliers[i])
                    : 1f;

                speeds[i] = Mathf.Max(0.01f, settings.baseSpeed) * scale * multiplier;
            }

            // ---- 多源 Dijkstra ----
            RunMultiSourceDijkstra(
                seedWorldPositions,
                speeds,
                settings.allowDiagonal);

            // ---- 汇总 ----
            float last = 0f;
            m_SortedCount = 0;
            for (int i = 0; i < m_CellCount; i++)
            {
                if (m_State[i] != StateAir)
                {
                    continue;
                }

                float t = m_ArrivalTime[i];
                if (float.IsPositiveInfinity(t))
                {
                    m_State[i] = StateSealed;   // 走不到：封闭空腔
                    continue;
                }

                if (t > last)
                {
                    last = t;
                }

                m_SortedCount++;
            }

            LastArrivalTime = last;
            NormalizeScale = last > 1e-4f ? last : 1f;
            EstimatedSpeed = Mathf.Max(0.05f, settings.baseSpeed);

            // 终点被淹的时刻 = 关卡截止时刻
            Deadline = SampleArrivalTime(goalWorldPosition);
            if (float.IsPositiveInfinity(Deadline))
            {
                Deadline = last;
            }

            BuildSortedFrontier();

            m_IsBaked = true;
            BakeReport = string.Format(
                "泡泡潮场：{0}×{1}×{2} = {3} 格（格距 {4} 米）\n" +
                "  实心 {5}，可达空腔 {6}，封闭空腔 {7}\n" +
                "  各角速度 {8} 米/秒\n" +
                "  终点被淹于 {9:F1} 秒，整个房间淹满于 {10:F1} 秒",
                m_SizeX, m_SizeY, m_SizeZ, m_CellCount, cell,
                solidCount, m_SortedCount, airCount - m_SortedCount,
                FormatSpeeds(speeds), Deadline, last);

            Debug.Log("[泡泡潮] " + BakeReport);
        }

        private static string FormatSpeeds(float[] speeds)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < speeds.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(" / ");
                }

                sb.Append(speeds[i].ToString("F2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 逐格做 OverlapSphere，标记哪些格子是实心障碍。
        /// 这是烤制里最慢的一步 —— 15 万格大约 0.1~0.5 秒。
        /// </summary>
        private int MarkSolidCells(LayerMask obstacleMask, float padding)
        {
            int solid = 0;
            float radius = m_CellSize * 0.5f + Mathf.Max(0f, padding);

            for (int z = 0; z < m_SizeZ; z++)
            {
                for (int y = 0; y < m_SizeY; y++)
                {
                    for (int x = 0; x < m_SizeX; x++)
                    {
                        int index = Index(x, y, z);
                        Vector3 center = CellCenter(x, y, z);

                        bool blocked = Physics.CheckSphere(
                            center, radius, obstacleMask, QueryTriggerInteraction.Ignore);

                        if (blocked)
                        {
                            m_State[index] = StateSolid;
                            m_ArrivalTime[index] = NeverFlood;
                            m_OwnerSeed[index] = -1;
                            solid++;
                        }
                        else
                        {
                            m_State[index] = StateAir;
                            m_ArrivalTime[index] = NeverFlood;
                            m_OwnerSeed[index] = -1;
                        }
                    }
                }
            }

            return solid;
        }

        /// <summary>
        /// 多源 Dijkstra。八个角同时从 t = 0 出发，边的代价是「走完这一格要多少秒」。
        ///
        /// 用二叉堆而不是 Unity 的 NavMesh，是因为我们要的是「真实路径长度 + 每源不同速度」，
        /// 这正好是带权图的最短路问题。
        /// </summary>
        private void RunMultiSourceDijkstra(Vector3[] seeds, float[] speeds, bool allowDiagonal)
        {
            // 收集所有能用的种子格子。种子可能落在实心格里（角刚好嵌在墙里），
            // 那就往外找最近的一个空格，找不到就放弃这个种子。
            int[] seedCells = new int[seeds.Length];
            float[] seedSpeeds = new float[seeds.Length];
            int seedCount = 0;

            for (int i = 0; i < seeds.Length; i++)
            {
                int cell = FindNearestAirCell(seeds[i]);
                if (cell < 0)
                {
                    Debug.LogWarning(string.Format(
                        "[泡泡潮] 第 {0} 个角 {1} 附近找不到空格，这个角不会出泡泡。",
                        i, seeds[i]));
                    continue;
                }

                seedCells[seedCount] = cell;
                seedSpeeds[seedCount] = speeds[i];
                seedCount++;
            }

            if (seedCount == 0)
            {
                Debug.LogError("[泡泡潮] 八个角全部无效，潮水不会出现。检查包围盒与障碍层。");
                return;
            }

            MinHeap heap = new MinHeap(m_CellCount);

            for (int i = 0; i < seedCount; i++)
            {
                int cell = seedCells[i];
                m_ArrivalTime[cell] = 0f;
                m_OwnerSeed[cell] = i;
                heap.Push(cell, 0f);
            }

            // 邻居偏移。前 6 个是轴向，后 12 个是面对角线。
            // 面对角线只在「两侧轴向都通」时才允许通过，否则泡泡会从墙缝斜着钻过去。
            int[] offsets =
            {
                 1, 0, 0,   -1, 0, 0,
                 0, 1, 0,    0,-1, 0,
                 0, 0, 1,    0, 0,-1,
                 1, 1, 0,   -1, 1, 0,    1,-1, 0,   -1,-1, 0,
                 1, 0, 1,   -1, 0, 1,    1, 0,-1,   -1, 0,-1,
                 0, 1, 1,    0, 1,-1,    0,-1, 1,    0,-1,-1,
            };

            int offsetCount = allowDiagonal ? 18 : 6;
            float diagonalFactor = Mathf.Sqrt(2f);

            while (heap.Count > 0)
            {
                int cell;
                float time;
                heap.Pop(out cell, out time);

                // 堆里可能有同一个格子的旧条目（没有做 decrease-key），跳过过期的
                if (time > m_ArrivalTime[cell])
                {
                    continue;
                }

                int seedIndex = m_OwnerSeed[cell];
                float speed = seedSpeeds[seedIndex];

                // 解出这个格子的三维坐标
                int cx = cell % m_SizeX;
                int cy = (cell / m_SizeX) % m_SizeY;
                int cz = cell / (m_SizeX * m_SizeY);

                for (int o = 0; o < offsetCount; o++)
                {
                    int dx = offsets[o * 3];
                    int dy = offsets[o * 3 + 1];
                    int dz = offsets[o * 3 + 2];

                    int nx = cx + dx;
                    int ny = cy + dy;
                    int nz = cz + dz;

                    if (!InRange(nx, ny, nz))
                    {
                        continue;
                    }

                    int n = Index(nx, ny, nz);
                    if (m_State[n] != StateAir)
                    {
                        continue;
                    }

                    // 斜线要检查两侧 —— 否则泡泡会穿过墙角
                    bool isDiagonal = (dx != 0 && dy != 0) || (dx != 0 && dz != 0) || (dy != 0 && dz != 0);
                    if (isDiagonal && !IsDiagonalPassable(cx, cy, cz, dx, dy, dz))
                    {
                        continue;
                    }

                    // 代价 = 距离 / 速度。轴向距离是 1 格，斜向是 sqrt(2) 格。
                    float step = isDiagonal ? diagonalFactor : 1f;
                    float candidate = time + (step * m_CellSize) / speed;

                    if (candidate < m_ArrivalTime[n])
                    {
                        m_ArrivalTime[n] = candidate;
                        m_OwnerSeed[n] = seedIndex;
                        heap.Push(n, candidate);
                    }
                }
            }
        }

        /// <summary>
        /// 斜向移动是否合法：要求两条「L 形」路径中至少有一条是通的。
        ///
        /// 不做这个检查的话，泡泡会从墙与墙的夹角斜着钻过去 ——
        /// 视觉上就是潮水渗进了本该封闭的角落。
        /// </summary>
        private bool IsDiagonalPassable(int cx, int cy, int cz, int dx, int dy, int dz)
        {
            bool x = dx != 0;
            bool y = dy != 0;
            bool z = dz != 0;

            // 两条 L 形里任意一条通得过就行
            bool first;
            bool second;

            if (x && y && z)
            {
                // 体对角线：三条 L 形，取前两条
                first = (IsAir(cx + dx, cy, cz) && IsAir(cx + dx, cy + dy, cz))
                     || (IsAir(cx, cy + dy, cz) && IsAir(cx, cy + dy, cz + dz));
                second = (IsAir(cx + dx, cy, cz) && IsAir(cx + dx, cy, cz + dz))
                      || (IsAir(cx, cy, cz + dz) && IsAir(cx, cy + dy, cz + dz));
            }
            else if (x && y)
            {
                first = IsAir(cx + dx, cy, cz) && IsAir(cx + dx, cy + dy, cz);
                second = IsAir(cx, cy + dy, cz) && IsAir(cx + dx, cy + dy, cz);
            }
            else if (x && z)
            {
                first = IsAir(cx + dx, cy, cz) && IsAir(cx + dx, cy, cz + dz);
                second = IsAir(cx, cy, cz + dz) && IsAir(cx + dx, cy, cz + dz);
            }
            else if (y && z)
            {
                first = IsAir(cx, cy + dy, cz) && IsAir(cx, cy + dy, cz + dz);
                second = IsAir(cx, cy, cz + dz) && IsAir(cx, cy + dy, cz + dz);
            }
            else
            {
                return true;   // 轴向移动，不需要检查
            }

            return first || second;
        }

        /// <summary>指定格子是不是可通行的空气（越界算不通）。</summary>
        private bool IsAir(int x, int y, int z)
        {
            if (!InRange(x, y, z))
            {
                return false;
            }

            return m_State[Index(x, y, z)] == StateAir;
        }

        /// <summary>
        /// 从给定世界坐标往外螺旋找最近的一个空格。
        /// 角多半正好嵌在墙角或地板里，所以这一步是必需的不是保险。
        /// </summary>
        private int FindNearestAirCell(Vector3 world)
        {
            Vector3Int c = WorldToCell(world);

            // 先在紧邻的小范围内找，找不到再扩大
            for (int radius = 0; radius <= 12; radius++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            // 只检查这一层壳，不然会重复扫内部
                            int chebyshev = Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
                            if (chebyshev != radius)
                            {
                                continue;
                            }

                            int x = c.x + dx;
                            int y = c.y + dy;
                            int z = c.z + dz;

                            if (InRange(x, y, z) && m_State[Index(x, y, z)] == StateAir)
                            {
                                return Index(x, y, z);
                            }
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// 把所有可达格子按时间排好序，运行期就能用一个指针 O(1) 地推进潮水面。
        /// 1500 个格子还是 15 万个格子，这一步都是一次性成本。
        /// </summary>
        private void BuildSortedFrontier()
        {
            if (m_SortedTimes == null || m_SortedTimes.Length != m_SortedCount)
            {
                m_SortedTimes = new float[m_SortedCount];
                m_SortedCells = new int[m_SortedCount];
            }

            int w = 0;
            for (int i = 0; i < m_CellCount; i++)
            {
                if (m_State[i] != StateAir)
                {
                    continue;
                }

                m_SortedTimes[w] = m_ArrivalTime[i];
                m_SortedCells[w] = i;
                w++;
            }

            System.Array.Sort(m_SortedTimes, m_SortedCells);
            m_SortedCount = w;
            m_FrontierMinCell = -1;
            m_FrontierMaxCell = -1;
        }

        // ==================================================================
        // 运行期查询
        // ==================================================================

        /// <summary>
        /// 这个世界坐标在第几秒会被淹到。返回 NeverFlood 表示不会被淹（实心或封闭空腔）。
        ///
        /// 实心格直接返回 NeverFlood，**不做邻域兜底** ——
        /// 兜底会让墙里的位置也报出一个有限的到达时间，
        /// 「玩家被推进墙里就被算作没事」这种错会很难查。
        /// </summary>
        public float SampleArrivalTime(Vector3 world)
        {
            if (!m_IsBaked)
            {
                return NeverFlood;
            }

            Vector3Int c = WorldToCell(world);
            if (!InRange(c.x, c.y, c.z))
            {
                return NeverFlood;
            }

            int index = Index(c.x, c.y, c.z);
            if (m_State[index] != StateAir)
            {
                return NeverFlood;
            }

            return m_ArrivalTime[index];
        }

        /// <summary>归一化后的被淹时间 0~1。1 表示整个房间淹满。</summary>
        public float SampleNormalized(Vector3 world)
        {
            float t = SampleArrivalTime(world);
            if (float.IsPositiveInfinity(t))
            {
                return 1f;
            }

            return Mathf.Clamp01(t / NormalizeScale);
        }

        /// <summary>这个世界坐标现在被淹了吗。</summary>
        public bool IsFlooded(Vector3 world, float elapsedSeconds)
        {
            return elapsedSeconds >= SampleArrivalTime(world);
        }

        /// <summary>
        /// 潮水推进的方向（世界空间单位向量）。
        /// 用的是「被淹时间」的负梯度 —— 时间越小的地方越早被淹，
        /// 所以指向时间减少的方向，就是泡泡正在前进的方向。
        /// 玩家被潮水推回去、以及泡泡的漂移方向都用它。
        /// </summary>
        public Vector3 SampleFlowDirection(Vector3 world)
        {
            if (!m_IsBaked)
            {
                return Vector3.zero;
            }

            float h = m_CellSize;

            float tx0 = SampleArrivalTime(world - new Vector3(h, 0f, 0f));
            float tx1 = SampleArrivalTime(world + new Vector3(h, 0f, 0f));
            float ty0 = SampleArrivalTime(world - new Vector3(0f, h, 0f));
            float ty1 = SampleArrivalTime(world + new Vector3(0f, h, 0f));
            float tz0 = SampleArrivalTime(world - new Vector3(0f, 0f, h));
            float tz1 = SampleArrivalTime(world + new Vector3(0f, 0f, h));

            Vector3 grad = new Vector3(
                SafeDelta(tx1, tx0),
                SafeDelta(ty1, ty0),
                SafeDelta(tz1, tz0));

            // 负梯度 = 朝「更早被淹」的方向 = 潮水推进方向
            Vector3 dir = -grad;

            float mag = dir.magnitude;
            if (mag < 1e-5f)
            {
                return Vector3.zero;
            }

            return dir / mag;
        }

        private static float SafeDelta(float a, float b)
        {
            bool aInf = float.IsPositiveInfinity(a);
            bool bInf = float.IsPositiveInfinity(b);

            if (aInf && bInf)
            {
                return 0f;
            }

            // 一边是墙，另一边是空气：给一个很大的梯度，让方向明确
            if (aInf)
            {
                return 1e3f;
            }

            if (bInf)
            {
                return -1e3f;
            }

            return a - b;
        }

        /// <summary>
        /// 按时间推进潮水面，并把这一帧「刚被淹到」的格子范围记下来。
        /// 每帧只是一次指针推进，和房间大小无关。
        /// </summary>
        /// <returns>这一帧新淹到的格子数。返回 0 表示没有变化（可以跳过重建）。</returns>
        public int AdvanceFrontier(float elapsedSeconds)
        {
            if (!m_IsBaked || m_SortedTimes == null || m_SortedCount == 0)
            {
                return 0;
            }

            // 时间被倒回去（重开关卡 / 调试时拖时间轴）→ 整条前沿重来
            bool rewind = m_FrontierMaxCell >= 0
                && m_SortedTimes[Mathf.Min(m_FrontierMaxCell, m_SortedCount - 1)] > elapsedSeconds;

            if (rewind)
            {
                m_FrontierMinCell = -1;
                m_FrontierMaxCell = -1;
            }

            int before = m_FrontierMaxCell + 1;

            while (m_FrontierMaxCell + 1 < m_SortedCount
                && m_SortedTimes[m_FrontierMaxCell + 1] <= elapsedSeconds)
            {
                m_FrontierMaxCell++;
            }

            // 前沿的下界：已经淹过的部分里，最后一帧之后才淹到的那一段。
            // 局部修改（炸弹炸洞）会把某些格子的时间往后推，那时的下界要靠重扫兜底，
            // 所以这里只在「倒带」时把它复位到 0。
            if (rewind)
            {
                m_FrontierMinCell = m_FrontierMaxCell >= 0 ? 0 : -1;
            }
            else if (m_FrontierMinCell < 0 && m_FrontierMaxCell >= 0)
            {
                m_FrontierMinCell = 0;
            }

            if (rewind)
            {
                return m_FrontierMaxCell + 1;
            }

            return m_FrontierMaxCell + 1 - before;
        }

        /// <summary>
        /// 已被淹的格子数（按当前推进到的时间算）。
        /// </summary>
        public int FloodedCellCount
        {
            get { return m_FrontierMaxCell + 1; }
        }

        /// <summary>可达格子总数。</summary>
        public int ReachableCellCount
        {
            get { return m_SortedCount; }
        }

        /// <summary>整条潮水前沿是否已经推到头了。</summary>
        public bool IsFullyFlooded
        {
            get { return m_SortedCount > 0 && m_FrontierMaxCell + 1 >= m_SortedCount; }
        }

        /// <summary>
        /// 把场导出成一张 0~1 的字节表，直接喂给 Texture3D。
        /// 索引顺序与 Texture3D 一致：x 变化最快。
        ///
        /// 这里之所以不直接存秒，是因为传给 GPU 的必须是和墙钟时间无关的归一化值 ——
        /// 关卡的截止时间一改，纹理不用重烤。
        /// </summary>
        public byte[] BuildNormalizedVolume()
        {
            if (!m_IsBaked)
            {
                return null;
            }

            byte[] volume = new byte[m_CellCount];
            float scale = NormalizeScale > 1e-4f ? 1f / NormalizeScale : 0f;

            for (int i = 0; i < m_CellCount; i++)
            {
                if (m_State[i] != StateAir)
                {
                    volume[i] = 255;   // 实心与封闭空腔：永远不淹
                    continue;
                }

                float t = m_ArrivalTime[i];
                if (float.IsPositiveInfinity(t))
                {
                    volume[i] = 255;
                    continue;
                }

                volume[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(t * scale * 255f), 0, 254);
            }

            return volume;
        }

        // ==================================================================
        // 运行期局部修改（给「反制」用）
        // ==================================================================

        /// <summary>
        /// 把某个球形范围内的潮水往后推若干秒 —— 也就是「炸出一个洞」。
        /// 炸弹泡泡的爆炸会调它。视觉上潮水会真的凹进去一块。
        /// </summary>
        /// <returns>实际被推迟的格子数。</returns>
        public int DelayFloodInRadius(Vector3 center, float radius, float delaySeconds)
        {
            if (!m_IsBaked || Mathf.Approximately(delaySeconds, 0f) || radius <= 0f)
            {
                return 0;
            }

            Vector3Int min = WorldToCell(center - Vector3.one * radius);
            Vector3Int max = WorldToCell(center + Vector3.one * radius);

            int touched = 0;
            float radiusSq = radius * radius;

            for (int z = Mathf.Max(0, min.z); z <= Mathf.Min(m_SizeZ - 1, max.z); z++)
            {
                for (int y = Mathf.Max(0, min.y); y <= Mathf.Min(m_SizeY - 1, max.y); y++)
                {
                    for (int x = Mathf.Max(0, min.x); x <= Mathf.Min(m_SizeX - 1, max.x); x++)
                    {
                        int index = Index(x, y, z);
                        if (m_State[index] != StateAir)
                        {
                            continue;
                        }

                        if ((CellCenter(x, y, z) - center).sqrMagnitude > radiusSq)
                        {
                            continue;
                        }

                        m_ArrivalTime[index] = Mathf.Max(0f, m_ArrivalTime[index] + delaySeconds);
                        touched++;
                    }
                }
            }

            if (touched > 0)
            {
                // 时间表变了：重新排序并重建推进范围。
                // 注意这里【不】重跑 Dijkstra —— 改的是「到达时间」本身，而不是速度场。
                // 对「炸个洞」这种局部效果足够了，而且便宜几个数量级。
                RecalculateAfterModification();
            }

            return touched;
        }

        /// <summary>
        /// 让某个球形范围内的潮水整体变快/变慢。
        /// 黏浮泡泡粘成的墙就是靠它 —— 给周围刷一个正的偏移，那片区域推进就慢了。
        /// </summary>
        public int SlowFloodInRadius(Vector3 center, float radius, float addSeconds)
        {
            return DelayFloodInRadius(center, radius, addSeconds);
        }

        /// <summary>
        /// 把**所有**格子的到达时间乘一个系数，也就是整条潮水一起变慢。
        /// 弹力泡泡被地形泡泡吸收时用它 —— 等于从潮水里抽走了一份容量。
        /// </summary>
        /// <param name="factor">大于 1 = 变慢，小于 1 = 变快。</param>
        public void ScaleAllArrivalTimes(float factor)
        {
            if (!m_IsBaked || factor <= 0f || Mathf.Approximately(factor, 1f))
            {
                return;
            }

            for (int i = 0; i < m_CellCount; i++)
            {
                if (m_State[i] != StateAir)
                {
                    continue;
                }

                float t = m_ArrivalTime[i];
                if (float.IsPositiveInfinity(t))
                {
                    continue;
                }

                m_ArrivalTime[i] = t * factor;
            }

            RecalculateAfterModification();

            // 终点被淹的时刻也跟着推后
            Deadline *= factor;
        }

        /// <summary>
        /// 局部修改之后重算派生数据。
        /// 注意：这不重跑 Dijkstra —— 改的是「到达时间」本身，而不是速度场。
        /// 对「炸个洞」这种局部效果来说足够了，而且便宜得多。
        /// </summary>
        private void RecalculateAfterModification()
        {
            float last = 0f;
            m_SortedCount = 0;

            for (int i = 0; i < m_CellCount; i++)
            {
                if (m_State[i] != StateAir)
                {
                    continue;
                }

                if (m_ArrivalTime[i] > last)
                {
                    last = m_ArrivalTime[i];
                }

                m_SortedCount++;
            }

            LastArrivalTime = last;
            NormalizeScale = last > 1e-4f ? last : 1f;

            BuildSortedFrontier();
        }

        // ==================================================================
        // 最小堆
        // ==================================================================

        /// <summary>
        /// 给 Dijkstra 用的二叉最小堆。
        /// 不做 decrease-key（那要额外维护位置索引），改成允许同一个格子重复入堆、
        /// 出堆时用「时间是否比记录值更大」来丢弃过期条目 —— 实现简单且总代价更低。
        /// </summary>
        private sealed class MinHeap
        {
            private int[] m_Cells;
            private float[] m_Keys;
            private int m_Count;

            public int Count { get { return m_Count; } }

            public MinHeap(int capacity)
            {
                capacity = Mathf.Max(16, capacity);
                m_Cells = new int[capacity];
                m_Keys = new float[capacity];
                m_Count = 0;
            }

            public void Push(int cell, float key)
            {
                if (m_Count == m_Cells.Length)
                {
                    System.Array.Resize(ref m_Cells, m_Count * 2);
                    System.Array.Resize(ref m_Keys, m_Count * 2);
                }

                int i = m_Count++;
                m_Cells[i] = cell;
                m_Keys[i] = key;

                // 上浮
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (m_Keys[parent] <= m_Keys[i])
                    {
                        break;
                    }

                    Swap(parent, i);
                    i = parent;
                }
            }

            public void Pop(out int cell, out float key)
            {
                cell = m_Cells[0];
                key = m_Keys[0];

                m_Count--;
                if (m_Count > 0)
                {
                    m_Cells[0] = m_Cells[m_Count];
                    m_Keys[0] = m_Keys[m_Count];

                    // 下沉
                    int i = 0;
                    while (true)
                    {
                        int left = (i << 1) + 1;
                        int right = left + 1;
                        int smallest = i;

                        if (left < m_Count && m_Keys[left] < m_Keys[smallest])
                        {
                            smallest = left;
                        }

                        if (right < m_Count && m_Keys[right] < m_Keys[smallest])
                        {
                            smallest = right;
                        }

                        if (smallest == i)
                        {
                            break;
                        }

                        Swap(i, smallest);
                        i = smallest;
                    }
                }
            }

            private void Swap(int a, int b)
            {
                int c = m_Cells[a];
                m_Cells[a] = m_Cells[b];
                m_Cells[b] = c;

                float k = m_Keys[a];
                m_Keys[a] = m_Keys[b];
                m_Keys[b] = k;
            }
        }
    }
}
