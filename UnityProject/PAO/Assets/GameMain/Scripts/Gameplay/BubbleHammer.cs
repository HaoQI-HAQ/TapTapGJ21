using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡摆锤：绕**顶部固定支点**左右摆的大吊锤，锤头是一颗「泡泡墙」。
    ///
    /// 【它是干什么的】
    /// 关卡里那几根吊着的木柱 + 底下的青色泡泡球。玩家要从下面走过 / 跳过去，
    /// 摆锤扫过来会把人**直接拍飞**（走位失误就被打进深渊）。
    /// 锤头是泡泡墙 —— 弹力泡泡可以把它填满 / 打爆，过几秒会长回来；
    /// 炸弹泡泡炸掉就是**永久**没了，锤子也就停摆。
    ///
    /// 【为什么不复用 BombDrivenSwing】
    /// 那个是"被炸弹推着摆、靠重力衰减回正"的被动摆。
    /// 这个机关要的是**一直匀速摆动的陷阱**，节奏可预测、玩家靠走位躲；
    /// 而且它多了两件事：锤头可以被拆、被拆后要停摆。
    /// 两者的运动学完全不同，所以单独一个组件，别混在一起改。
    ///
    /// 【锤头怎么"是泡泡墙"】
    /// 不用自己写破坏逻辑 —— 直接给锤头挂 <see cref="TerrainBubble"/>：
    ///   · 弹力泡泡撞上来 → ContainBubble + Absorb（填容积），填满 → Break(Bouncy) → 藏起来 N 秒再长回来；
    ///   · 炸弹泡泡 → Break(Bomb) → 直接销毁，永久消失。
    /// 这套是关卡里已经在用的泡泡墙机制（见 TerrainBubble）。
    ///
    /// 【但炸弹的判定半径要自己兜一下】
    /// CSBubbleLoadout 里炸弹的 blastRadius 只有 0.3 米，
    /// 引擎那条"爆炸波及地形泡泡"的路径要求爆炸中心几乎贴住碰撞体，
    /// 而泡泡球半径有 2.5 米、炸弹是在球面上炸的 —— 基本打不中。
    /// 所以这里自己订阅 <see cref="Bubble.Exploded"/>，用**锤头半径 + 余量**判一次。
    ///
    /// 【载人 / 击飞】
    /// 摆锤没有 Rigidbody，角色是 CharacterController，靠碰撞是推不飞的。
    /// 所以每帧拿锤头球心到角色胶囊中心的距离做判定，命中就调
    /// <see cref="PlayerController.Launch"/> 把人朝**锤头当前速度方向**打出去
    /// （外加一点向上分量，看起来像被拍飞）。
    ///
    /// 结构（由编辑器菜单生成）：
    ///   Hammer_xxx            ← 支点，挂本组件
    ///   └─ Hammer_xxx_Body
    ///      ├─ 木柱
    ///      └─ 泡泡球（TerrainBubble）
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/泡泡摆锤（Bubble Hammer）")]
    public sealed class BubbleHammer : MonoBehaviour
    {
        // ==================== 摆动 ====================

        [Header("摆动")]
        [Tooltip("绕哪根轴摆（本地）。默认本地 Z：在 X–Y 平面里左右荡。")]
        [SerializeField] private Vector3 m_LocalAxis = Vector3.forward;

        [Tooltip("最大摆角（度）。柱子 9 米高时，30 度 ≈ 摆幅 4.5 米。")]
        [SerializeField] private float m_Amplitude = 30f;

        [Tooltip("一个来回多少秒。越大越慢、越好躲。")]
        [SerializeField] private float m_Period = 3.2f;

        [Tooltip("相位（度）。几架摆锤排成一排时错开相位，会像波浪一样依次扫过来。")]
        [SerializeField, Range(0f, 360f)] private float m_Phase = 0f;

        [Tooltip("锤头被拆（炸掉 / 正在等待重生）时，杆子是否继续摆。\n" +
                 "勾着 = 杆子照常摆，泡泡长回来时直接出现在杆子末端；\n" +
                 "去掉 = 回正停摆（旧行为）。")]
        [SerializeField] private bool m_SwingWithoutHead = true;

        [Tooltip("只有在上面那个关掉时才用：锤头没了之后多少秒内回正并停住。0 = 立刻停。")]
        [SerializeField] private float m_RestEase = 1.2f;

        // ==================== 锤头 ====================

        [Header("锤头")]
        [Tooltip("锤头物体（泡泡球）。留空会自动找子物体里的 TerrainBubble。")]
        [SerializeField] private Transform m_Head;

        [Tooltip("锤头半径（米）。0 = 按锤头碰撞体自动算。")]
        [SerializeField] private float m_HeadRadius = 0f;

        [Tooltip("炸弹永久炸掉锤头的判定范围（米，加在锤头半径之外）。\n" +
                 "炸弹自己的 blastRadius 只有 0.3 米，只靠它永远炸不到球。")]
        [SerializeField] private float m_HeadBlastReach = 2.5f;

        // ==================== 击飞玩家 ====================

        [Header("击飞玩家")]
        [Tooltip("锤子扫到人就把人打飞。")]
        [SerializeField] private bool m_KnockPlayer = true;

        [Tooltip("判定额外半径（米）：锤头半径之外再放宽这么多，算上角色自己的粗细")]
        [SerializeField] private float m_HitPadding = 0.7f;

        [Tooltip("锤头速度低于这个值（米/秒）就不算命中 —— 避免停在两端时贴着人乱拍")]
        [SerializeField] private float m_MinHeadSpeed = 2.5f;

        [Tooltip("被打出去的速度（米/秒）")]
        [SerializeField] private float m_HitSpeed = 16f;

        [Tooltip("打出去时额外向上的比例。0 = 纯水平，0.35 = 带一点掀飞")]
        [SerializeField] private float m_HitUp = 0.35f;

        [Tooltip("被打出去持续多少秒。速度 × 时间 ≈ 飞多远")]
        [SerializeField] private float m_HitDuration = 0.45f;

        [Tooltip("同一个人两次被拍飞的间隔（秒）")]
        [SerializeField] private float m_HitCooldown = 0.9f;

        // ==================== 调试 ====================

        [Header("调试")]
        [Tooltip("打印锤头被破坏 / 玩家被拍飞的日志。")]
        [SerializeField] private bool m_Log = false;

        // ==================== 运行时 ====================

        private Quaternion m_InitialLocalRotation = Quaternion.identity;
        private float m_Clock;
        private float m_Angle;

        private TerrainBubble m_HeadBubble;
        private float m_CachedHeadRadius;
        private bool m_HeadRadiusCached;

        private Vector3 m_LastHeadPosition;
        private Vector3 m_HeadVelocity;
        private Vector3 m_LastSwingDirection;
        private bool m_HasLastHeadPosition;

        private readonly List<PlayerController> m_Players = new List<PlayerController>(1);
        private readonly Dictionary<PlayerController, float> m_NextHitTime =
            new Dictionary<PlayerController, float>(2);

        private float m_NextPlayerSearch;
        private bool m_HeadDestroyed;

        /// <summary>当前摆角（度）。</summary>
        public float Angle { get { return m_Angle; } }

        /// <summary>锤头是否还在（没被永久炸掉、也没在重生等待中）。</summary>
        public bool HeadUsable
        {
            get
            {
                if (m_HeadDestroyed || m_Head == null || !m_Head.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (m_HeadColliders == null || m_HeadColliders.Length == 0)
                {
                    CacheHeadColliders();
                }

                if (m_HeadColliders == null)
                {
                    return false;
                }

                for (int i = 0; i < m_HeadColliders.Length; i++)
                {
                    if (m_HeadColliders[i] != null && m_HeadColliders[i].enabled)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>锤头球心（世界坐标）。</summary>
        public Vector3 HeadCenter
        {
            get { return m_Head != null ? m_Head.position : transform.TransformPoint(Vector3.down); }
        }

        /// <summary>锤头半径（米）。</summary>
        public float HeadRadius
        {
            get
            {
                if (m_HeadRadius > 0.01f)
                {
                    return m_HeadRadius;
                }

                if (!m_HeadRadiusCached)
                {
                    m_CachedHeadRadius = MeasureHeadRadius();
                    m_HeadRadiusCached = true;
                }

                return m_CachedHeadRadius;
            }
        }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            m_InitialLocalRotation = transform.localRotation;

            if (m_Head == null)
            {
                m_HeadBubble = GetComponentInChildren<TerrainBubble>(true);

                if (m_HeadBubble != null)
                {
                    m_Head = m_HeadBubble.transform;
                }
            }
            else
            {
                m_HeadBubble = m_Head.GetComponentInChildren<TerrainBubble>(true);
            }

            ResolveHeadRadius();
            CacheHeadColliders();
            ApplyRotation(0f);
            m_LastHeadPosition = HeadCenter;
            m_HasLastHeadPosition = true;
        }

        /// <summary>
        /// 缓存锤头的碰撞体：<see cref="HeadUsable"/> 每帧都要查"碰撞体还开着吗"，
        /// 用 GetComponentsInChildren 会每帧分配一个新数组，四架摆锤就是每秒几百次 GC。
        /// </summary>
        private void CacheHeadColliders()
        {
            m_HeadColliders = m_Head != null
                ? m_Head.GetComponentsInChildren<Collider>(true)
                : null;
        }

        /// <summary>锤头的碰撞体缓存（可能含 null：锤头被销毁时）。</summary>
        private Collider[] m_HeadColliders;

        private void OnEnable()
        {
            Bubble.Exploded += HandleBubbleExploded;
        }

        private void OnDisable()
        {
            Bubble.Exploded -= HandleBubbleExploded;
            m_Players.Clear();
            m_NextHitTime.Clear();
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            float dt = Time.deltaTime;

            if (dt <= 0f)
            {
                return;   // 暂停中
            }

            bool usable = HeadUsable;

            // 杆子照常摆：锤头被炸掉 / 正在等重生时**不要**回正待机 ——
            //   · 只看杆子会以为机关卡住了，节奏也断了；
            //   · 弹力泡泡破掉的球是"藏起来"不是"删掉"，它复原时会回到
            //     自己的本地位置 = 杆子末端，所以杆子摆到哪儿它就在哪儿长出来。
            // 想去掉这个行为就把 m_SwingWithoutHead 关掉，回到"回正停摆"。
            if (usable || m_SwingWithoutHead)
            {
                m_Clock += dt;
                float phase = m_Clock * (Mathf.PI * 2f / Mathf.Max(0.3f, m_Period))
                              + m_Phase * Mathf.Deg2Rad;
                m_Angle = Mathf.Sin(phase) * m_Amplitude;
            }
            else
            {
                // 锤头被拆了：慢慢回到竖直位，停摆
                m_Clock = 0f;

                if (m_RestEase > 0f)
                {
                    m_Angle = Mathf.MoveTowards(m_Angle, 0f, (Mathf.Abs(m_Amplitude) * 2f / m_RestEase) * dt);
                }
                else
                {
                    m_Angle = 0f;
                }
            }

            ApplyRotation(m_Angle);

            UpdateHeadVelocity(dt);

            if (usable && m_KnockPlayer)
            {
                CheckPlayerHits();
            }
        }

        private void ApplyRotation(float degrees)
        {
            Vector3 axis = m_LocalAxis.sqrMagnitude > 0.0001f ? m_LocalAxis.normalized : Vector3.forward;
            transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(degrees, axis);
        }

        private void UpdateHeadVelocity(float dt)
        {
            if (m_Head == null)
            {
                m_HeadVelocity = Vector3.zero;
                return;
            }

            Vector3 current = HeadCenter;

            if (m_HasLastHeadPosition)
            {
                m_HeadVelocity = (current - m_LastHeadPosition) / dt;
            }

            m_LastHeadPosition = current;
            m_HasLastHeadPosition = true;

            if (m_HeadVelocity.sqrMagnitude > 0.0001f)
            {
                m_LastSwingDirection = m_HeadVelocity.normalized;
            }
        }

        // ==================== 拍飞玩家 ====================

        private void CheckPlayerHits()
        {
            RefreshPlayersIfNeeded();

            float speed = m_HeadVelocity.magnitude;

            if (speed < m_MinHeadSpeed)
            {
                return;
            }

            Vector3 center = HeadCenter;
            float reach = HeadRadius + m_HitPadding;

            for (int i = 0; i < m_Players.Count; i++)
            {
                PlayerController player = m_Players[i];

                if (player == null || player.IsInCarrier)
                {
                    continue;
                }

                CharacterController controller = player.GetComponent<CharacterController>();

                if (controller == null || !controller.enabled)
                {
                    continue;
                }

                // 用胶囊中心算距离：锤头很大，用脚底算会漏
                float distance = Vector3.Distance(controller.bounds.center, center);

                if (distance > reach)
                {
                    continue;
                }

                float nextTime;

                if (m_NextHitTime.TryGetValue(player, out nextTime) && Time.time < nextTime)
                {
                    continue;
                }

                m_NextHitTime[player] = Time.time + m_HitCooldown;

                Vector3 direction = m_HeadVelocity.sqrMagnitude > 0.0001f
                    ? m_HeadVelocity.normalized
                    : m_LastSwingDirection;

                if (m_HitUp > 0f)
                {
                    direction = (direction + Vector3.up * m_HitUp).normalized;
                }

                player.Launch(direction, m_HitSpeed, m_HitDuration);

                if (m_Log)
                {
                    Debug.Log("[BubbleHammer] \"" + name + "\" 拍飞玩家：速度 " +
                              m_HitSpeed.ToString("F1") + " m/s，方向 " + direction.ToString("F2"), this);
                }
            }
        }

        private void RefreshPlayersIfNeeded()
        {
            if (m_Players.Count > 0 && m_Players[0] != null)
            {
                return;
            }

            if (Time.time < m_NextPlayerSearch)
            {
                return;
            }

            m_NextPlayerSearch = Time.time + 1f;
            m_Players.Clear();

            PlayerController[] found = FindObjectsByType<PlayerController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                {
                    m_Players.Add(found[i]);
                }
            }
        }

        // ==================== 锤头：被炸弹永久炸掉 ====================

        private void HandleBubbleExploded(Vector3 center, float radius)
        {
            if (!HeadUsable || m_HeadBubble == null)
            {
                return;
            }

            // 判定半径：锤头半径 + 自己的余量 + 炸弹半径。
            // 不能只跟炸弹半径走（它是 0.3 米），否则炸在球面上都打不到。
            float reach = HeadRadius + m_HeadBlastReach + Mathf.Max(0f, radius);
            float distance = Vector3.Distance(center, HeadCenter);

            if (distance > reach)
            {
                return;
            }

            // 炸弹：永久破坏（TerrainBubble 会直接销毁锤头对象）
            m_HeadBubble.Break(TerrainBubble.BreakCause.Bomb);
            m_HeadDestroyed = true;

            if (m_Log)
            {
                Debug.Log("[BubbleHammer] \"" + name + "\" 的锤头被炸弹炸毁（距离 " +
                          distance.ToString("F2") + "m，判定 " + reach.ToString("F2") + "m）", this);
            }
        }

        // ==================== 辅助 ====================

        private void ResolveHeadRadius()
        {
            m_HeadRadiusCached = false;

            if (m_HeadRadius > 0.01f)
            {
                return;
            }

            m_CachedHeadRadius = MeasureHeadRadius();
            m_HeadRadiusCached = true;
        }

        /// <summary>按锤头的碰撞体包围盒算半径（取最长的半边）。</summary>
        private float MeasureHeadRadius()
        {
            if (m_Head == null)
            {
                return 0.5f;
            }

            Collider[] colliders = m_Head.GetComponentsInChildren<Collider>(true);
            Bounds bounds = default(Bounds);
            bool has = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = colliders[i].bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
            }

            if (!has)
            {
                Renderer renderer = m_Head.GetComponentInChildren<Renderer>(true);

                if (renderer != null)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
            }

            if (!has)
            {
                return 0.5f;
            }

            return Mathf.Max(0.1f, Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)));
        }

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmos()
        {
            Vector3 pivot = transform.position;
            Vector3 axis = m_LocalAxis.sqrMagnitude > 0.0001f
                ? transform.TransformDirection(m_LocalAxis.normalized)
                : transform.forward;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(pivot, 0.25f);

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(pivot - axis * 1.5f, pivot + axis * 1.5f);

            float length = m_Head != null
                ? Vector3.Distance(pivot, m_Head.position)
                : 5f;

            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.8f);
            Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(-m_Amplitude, axis) * (Vector3.down * length));
            Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(m_Amplitude, axis) * (Vector3.down * length));

            if (m_Head != null)
            {
                Gizmos.DrawWireSphere(HeadCenter, HeadRadius);
            }
        }

        // ==================== 由搭建脚本调用 ====================

        /// <summary>编辑器 / 搭建脚本用的配置入口。</summary>
        public void Configure(Transform head, Vector3 localAxis, float amplitude, float period,
                              float phase, float hitSpeed = 16f)
        {
            m_Head = head;
            m_HeadBubble = head != null ? head.GetComponentInChildren<TerrainBubble>(true) : null;
            m_LocalAxis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.forward;
            m_Amplitude = Mathf.Clamp(amplitude, 1f, 70f);
            m_Period = Mathf.Max(0.5f, period);
            m_Phase = Mathf.Repeat(phase, 360f);
            m_HitSpeed = Mathf.Max(1f, hitSpeed);
            ResolveHeadRadius();
        }
    }
}
