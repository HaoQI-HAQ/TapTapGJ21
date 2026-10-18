using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 炸弹秋千：以**顶部一个固定支点**为轴，前后自由摆动的机关。
    ///
    /// 【它解决什么问题】
    /// 关卡里有些门架 / 吊板 / 悬台，希望它像秋千一样前后荡，
    /// 而且**只能靠玩家的炸弹能力推动** —— 炸一下荡起来，荡到够高就能过去。
    /// 直接给 Rigidbody + HingeJoint 也能做，但那个东西对本项目的角色不友好：
    ///   · CharacterController 不会被刚体平台带着走（人会被"落在原地"）；
    ///   · 物理关节在低帧率 / 大质量下会抖，角度也难夹；
    ///   · 爆炸力是 AddExplosionForce，推的是刚体质心，力臂全看运气。
    /// 所以这里自己写一个**刚性摆**：角度 + 角速度两个量，用重力做回复力，
    /// 爆炸按真正的「叉乘力臂」换算成角冲量。便宜、稳、可调、能载人。
    ///
    /// ============================================================
    /// 【只有一个自由度】
    /// ============================================================
    /// 只有一个支点 + 一根轴（<see cref="m_LocalAxis"/>，默认本地 X）。
    /// 支点在 transform.position，物体挂在下面；
    /// 每帧只改 transform.localRotation，所以是绕支点的刚性旋转，
    /// 不会出现"位置和旋转各飘各的"。
    ///
    /// 【挂了谁】
    /// 编辑器菜单（Tools/PAO/关卡机关/把选中的物体做成炸弹秋千）会自动：
    ///   新建 SwingRoot（放在支点）→ 子物体 SwingBody → 把选中的物体塞进 SwingBody。
    /// 这样原始物体只是换了父级，世界坐标不变。
    ///
    /// ============================================================
    /// 【动力学：为什么是 θ'' = -(g/L)·sinθ】
    /// ============================================================
    /// 这就是单摆方程。L 是"等效摆长"（支点到重心），
    /// 周期 = 2π√(L/g)：L = 4m 时约 4 秒一个来回，接近真实秋千。
    /// 阻尼用指数衰减（每秒损失一定比例），所以不会永远荡；
    /// 到了 |角速度| 和 |角度| 都很小时直接归零，避免"永远在抖一点"。
    ///
    /// ============================================================
    /// 【炸弹怎么推】
    /// ============================================================
    /// 订阅 <see cref="Bubble.Exploded"/>（和 RotatingHazard / BouncyPad 同一套），
    /// 判定用"爆炸中心到**本体碰撞体**的最近距离"，而不是到支点的距离 ——
    /// 门架有几米长，炸在底板上离支点很远，但明明已经炸到了。
    ///
    /// 冲量按真正的力臂算：r = 支点→受力点，F = 爆炸→受力点的方向，
    /// 绕轴的力矩 = dot(axis, cross(r, F))。所以：
    ///   · 炸在底板一侧 → 力臂最大，荡得最狠；
    ///   · 炸在支点正下方 → 理论上力臂为 0，这时按"水平偏移方向"给一个最小推力，
    ///     保证玩家炸了总是有反应（见 <see cref="m_BlastMinLever"/>）。
    ///
    /// ============================================================
    /// 【载人】
    /// ============================================================
    /// 本组件是 kinematic 的（没有 Rigidbody），CharacterController 不会自动跟着走，
    /// 所以每帧自己做：开转之前记下骑乘者相对本体的本地坐标，转完再变换回去，
    /// 差值就是"脚下那块板这一帧走过的距离"，用 CharacterController.Move 补上。
    /// 判定骑乘者的方式：从角色胶囊中心往下打一条短射线，打到的碰撞体属于本机关。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/炸弹秋千（Bomb Driven Swing）")]
    public sealed class BombDrivenSwing : MonoBehaviour
    {
        // ==================== 支点与摆动 ====================

        [Header("支点与摆动")]
        [Tooltip("绕哪根轴摆（本地空间）。默认本地 X：前后荡。")]
        [SerializeField] private Vector3 m_LocalAxis = Vector3.right;

        [Tooltip("最大摆角（度）。撞到这个角度会小幅回弹，像秋千碰到限位。")]
        [SerializeField] private float m_MaxAngle = 50f;

        [Tooltip("等效摆长（米）：支点到重心。决定摆动周期 2π√(L/g)。L=4 时约 4 秒一个来回。")]
        [SerializeField] private float m_PendulumLength = 4f;

        [Tooltip("初始摆角（度）。0 = 竖直垂着。")]
        [SerializeField] private float m_StartAngle = 0f;

        [Tooltip("开局就让它荡起来（给一个初始角速度），方便在编辑器里直接看到效果。")]
        [SerializeField] private bool m_StartSwinging = false;

        [Tooltip("开局角速度（度/秒），只有勾了上面那个才用。")]
        [SerializeField] private float m_StartAngularSpeed = 60f;

        // ==================== 阻尼 ====================

        [Header("阻尼")]
        [Tooltip("每秒损失的速度比例。0 = 永远荡，0.1 = 大约十秒后明显慢下来。")]
        [SerializeField] private float m_Damping = 0.10f;

        [Tooltip("角速度低于这个值（度/秒）且快回到竖直时，直接停住，避免无限微抖。")]
        [SerializeField] private float m_RestSpeed = 3f;

        [Tooltip("离竖直多近（度）才算「快回到竖直」，配合上面的静止速度用。")]
        [SerializeField] private float m_RestAngle = 0.8f;

        // ==================== 被炸弹推 ====================

        [Header("被炸弹推动")]
        [Tooltip("是否响应炸弹爆炸。关掉就是一个纯被动摆（测试用）。")]
        [SerializeField] private bool m_ReactToBomb = true;

        [Tooltip("一次满力度爆炸给多少角速度（度/秒）。爆炸离得越远、力臂越短，给得越少。")]
        [SerializeField] private float m_BlastImpulse = 90f;

        [Tooltip("爆炸判定额外范围（米）。爆炸半径已经算进去了，这里只是余量。")]
        [SerializeField] private float m_BlastReachBonus = 0.4f;

        [Tooltip("最小力臂系数。爆炸正好在支点正下方时力臂是 0，这里保底给一点推力，避免「炸了没反应」。")]
        [SerializeField, Range(0f, 1f)] private float m_BlastMinLever = 0.25f;

        [Tooltip("角速度上限（度/秒）。防止几颗炸弹叠加把秋千甩飞。")]
        [SerializeField] private float m_MaxAngularSpeed = 260f;

        [Tooltip("撞到最大摆角后反弹多少（0~1）。0 = 硬停，0.3 = 弹回来一点。")]
        [SerializeField, Range(0f, 1f)] private float m_BounceBack = 0.3f;

        // ==================== 载人 ====================

        [Header("载人")]
        [Tooltip("站在秋千上的角色是否跟着一起走。关掉人就停在半空了。")]
        [SerializeField] private bool m_CarryRiders = true;

        [Tooltip("判断「站在秋千上」的射线多探一点（米），给脚下的余量。")]
        [SerializeField] private float m_RiderProbe = 0.35f;

        // ==================== 调试 ====================

        [Header("调试")]
        [Tooltip("打印被炸 / 停稳的日志。")]
        [SerializeField] private bool m_Log = false;

        // ==================== 运行时状态 ====================

        /// <summary>当前摆角（度）。带符号，正 = 绕轴正方向。</summary>
        private float m_Angle;

        /// <summary>当前角速度（度/秒）。</summary>
        private float m_AngularSpeed;

        /// <summary>初始本地旋转。摆动叠加在它上面，不覆盖美术摆的朝向。</summary>
        private Quaternion m_InitialLocalRotation = Quaternion.identity;

        private bool m_Started;

        // 骑乘者缓存：角色、以及旋转前各自相对本体的本地坐标
        private readonly List<PlayerController> m_Riders = new List<PlayerController>(2);
        private readonly List<Vector3> m_RiderLocals = new List<Vector3>(2);

        // 场景里的玩家。玩家可能是运行时生成的，所以空了会隔一会儿重找。
        private readonly List<PlayerController> m_Players = new List<PlayerController>(1);
        private float m_NextPlayerSearch;

        /// <summary>当前摆角（度）。</summary>
        public float Angle { get { return m_Angle; } }

        /// <summary>当前角速度（度/秒）。</summary>
        public float AngularSpeed { get { return m_AngularSpeed; } }

        /// <summary>最大摆角（度）。</summary>
        public float MaxAngle { get { return m_MaxAngle; } }

        /// <summary>等效摆长（米）。</summary>
        public float PendulumLength { get { return m_PendulumLength; } }

        /// <summary>还在动吗。</summary>
        public bool IsSwinging { get { return Mathf.Abs(m_AngularSpeed) > 0.01f || Mathf.Abs(m_Angle) > 0.01f; } }

        /// <summary>摆动轴（世界空间，已归一化）。</summary>
        public Vector3 AxisWorld
        {
            get
            {
                Vector3 axis = m_LocalAxis.sqrMagnitude > 0.0001f ? m_LocalAxis : Vector3.right;
                return transform.TransformDirection(axis).normalized;
            }
        }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            m_InitialLocalRotation = transform.localRotation;
            m_Angle = Mathf.Clamp(m_StartAngle, -m_MaxAngle, m_MaxAngle);
            m_AngularSpeed = 0f;

            if (m_PendulumLength < 0.25f)
            {
                m_PendulumLength = 0.25f;
            }

            ApplyRotation();

            if (m_StartSwinging)
            {
                m_AngularSpeed = m_StartAngularSpeed;
            }

            m_Started = true;
        }

        private void OnEnable()
        {
            Bubble.Exploded += HandleBubbleExploded;
        }

        private void OnDisable()
        {
            // 静态事件必须退订，否则物体销毁后会留下悬空委托
            Bubble.Exploded -= HandleBubbleExploded;
            m_Riders.Clear();
            m_RiderLocals.Clear();
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            if (!m_Started)
            {
                return;
            }

            float dt = Time.deltaTime;

            if (dt <= 0f)
            {
                return;   // 暂停中
            }

            // ① 转之前先把骑乘者记下来（本地坐标）
            if (m_CarryRiders)
            {
                CollectRiders();
            }

            // ② 角速度：重力回复 + 阻尼
            float gravity = Mathf.Max(0.01f, -Physics.gravity.y);   // 9.81
            float accel = -(gravity / m_PendulumLength) * Mathf.Sin(m_Angle * Mathf.Deg2Rad);
            m_AngularSpeed += accel * Mathf.Rad2Deg * dt;

            if (m_Damping > 0f)
            {
                m_AngularSpeed *= Mathf.Exp(-m_Damping * dt);
            }

            m_AngularSpeed = Mathf.Clamp(m_AngularSpeed, -m_MaxAngularSpeed, m_MaxAngularSpeed);

            // ③ 角度积分 + 撞限位回弹
            m_Angle += m_AngularSpeed * dt;

            if (Mathf.Abs(m_Angle) > m_MaxAngle)
            {
                m_Angle = Mathf.Sign(m_Angle) * m_MaxAngle;
                m_AngularSpeed = -m_AngularSpeed * m_BounceBack;
            }

            // ④ 快停了就收干净：否则会永远在竖直附近抖一点点
            if (Mathf.Abs(m_AngularSpeed) < m_RestSpeed && Mathf.Abs(m_Angle) < m_RestAngle)
            {
                m_AngularSpeed = 0f;
                m_Angle = Mathf.MoveTowards(m_Angle, 0f, m_RestSpeed * dt);
            }

            // ⑤ 应用旋转
            ApplyRotation();

            // ⑥ 载人
            if (m_CarryRiders)
            {
                MoveRiders();
            }
        }

        private void ApplyRotation()
        {
            Vector3 axis = m_LocalAxis.sqrMagnitude > 0.0001f ? m_LocalAxis.normalized : Vector3.right;
            transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(m_Angle, axis);
        }

        // ==================== 被炸弹炸到 ====================

        private void HandleBubbleExploded(Vector3 center, float radius)
        {
            if (!m_ReactToBomb)
            {
                return;
            }

            if (PushFromBlast(center, radius))
            {
                if (m_Log)
                {
                    Debug.Log("[BombDrivenSwing] \"" + name + "\" 被炸到 → 角速度 " +
                              m_AngularSpeed.ToString("F1") + " 度/秒（角度 " +
                              m_Angle.ToString("F1") + " 度）", this);
                }
            }
        }

        /// <summary>
        /// 按一次爆炸给角冲量。返回是否真的推到了（用来打日志）。
        ///
        /// 力臂算法：r = 支点→受力点，F = 爆炸中心→受力点，
        /// 绕轴力矩 = dot(axis, cross(r, F))。力臂有符号，所以从左边炸和从右边炸
        /// 会往相反方向推 —— 这正是"前后摆动"要的。
        /// </summary>
        public bool PushFromBlast(Vector3 center, float radius)
        {
            Collider body = FindRepresentativeCollider();

            // 判定距离：拿**本体**上离爆炸最近的点，而不是支点
            Vector3 closest;
            float distance;

            if (body != null && body.enabled)
            {
                closest = body.ClosestPoint(center);
                distance = Vector3.Distance(center, closest);
            }
            else
            {
                closest = transform.position;
                distance = Vector3.Distance(center, closest);
            }

            float reach = Mathf.Max(0.01f, radius) + m_BlastReachBonus;

            if (distance > reach)
            {
                return false;
            }

            // 爆炸越近推得越狠。用平方衰减，贴脸炸才够夸张。
            float falloff = 1f - Mathf.Clamp01(distance / reach);
            falloff *= falloff;

            Vector3 axis = AxisWorld;
            Vector3 r = closest - transform.position;              // 支点 → 受力点
            Vector3 pushDir = closest - center;                    // 爆炸 → 受力点

            if (pushDir.sqrMagnitude < 0.0001f)
            {
                // 爆炸中心就在本体上：方向退化成"从支点往爆炸点"的水平方向
                pushDir = Vector3.ProjectOnPlane(transform.position - center, axis);
            }

            if (pushDir.sqrMagnitude < 0.0001f)
            {
                pushDir = -transform.forward;
            }

            pushDir.Normalize();

            // 有符号力臂，归一化到摆长 —— 1 表示"在最远端、并且力垂直于摆杆"
            float torque = Vector3.Dot(axis, Vector3.Cross(r, pushDir));
            float lever = Mathf.Clamp(torque / Mathf.Max(0.5f, m_PendulumLength), -1f, 1f);

            float sign = lever >= 0f ? 1f : -1f;
            float magnitude = Mathf.Max(Mathf.Abs(lever), m_BlastMinLever);

            AddAngularSpeed(sign * magnitude * falloff * m_BlastImpulse);
            return true;
        }

        /// <summary>直接给一个角速度增量（度/秒），外部机关也能调。</summary>
        public void AddAngularSpeed(float degreesPerSecond)
        {
            m_AngularSpeed = Mathf.Clamp(m_AngularSpeed + degreesPerSecond,
                                         -m_MaxAngularSpeed, m_MaxAngularSpeed);
        }

        /// <summary>把秋千摆到某个角度并停住（过场 / 重置用）。</summary>
        public void SnapToAngle(float degrees)
        {
            m_Angle = Mathf.Clamp(degrees, -m_MaxAngle, m_MaxAngle);
            m_AngularSpeed = 0f;
            ApplyRotation();
        }

        /// <summary>停稳在竖直位置。</summary>
        public void ResetSwing()
        {
            SnapToAngle(0f);
        }

        /// <summary>
        /// 挑一个代表本体的碰撞体：优先取包住整个机关的（尺寸最大的那个），
        /// 这样"最近点"落在结构边缘而不是某根装饰细杆上。
        /// </summary>
        private Collider FindRepresentativeCollider()
        {
            Collider best = null;
            float bestVolume = -1f;

            Collider[] colliders = GetComponentsInChildren<Collider>(false);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];

                if (c == null || !c.enabled || c.isTrigger)
                {
                    continue;
                }

                Vector3 size = c.bounds.size;
                float volume = size.x * size.y * size.z;

                if (volume > bestVolume)
                {
                    bestVolume = volume;
                    best = c;
                }
            }

            return best;
        }

        // ==================== 载人 ====================

        /// <summary>
        /// 找出这一刻站在秋千上的角色，并记下他们相对本体的本地坐标。
        /// 判定：从角色胶囊中心往下打一条短射线，打中的碰撞体在本机关下面。
        /// </summary>
        private void CollectRiders()
        {
            m_Riders.Clear();
            m_RiderLocals.Clear();

            RefreshPlayersIfNeeded();

            for (int i = 0; i < m_Players.Count; i++)
            {
                PlayerController player = m_Players[i];

                if (player == null)
                {
                    continue;
                }

                CharacterController controller = player.GetComponent<CharacterController>();

                if (controller == null || !controller.enabled || !controller.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!IsStandingOnMe(controller))
                {
                    continue;
                }

                m_Riders.Add(player);
                m_RiderLocals.Add(transform.InverseTransformPoint(player.transform.position));
            }
        }

        private bool IsStandingOnMe(CharacterController controller)
        {
            Bounds bounds = controller.bounds;
            Vector3 origin = bounds.center;
            float distance = bounds.extents.y + Mathf.Max(0.05f, m_RiderProbe);

            RaycastHit hit;

            if (!Physics.Raycast(origin, Vector3.down, out hit, distance, Physics.AllLayers,
                                 QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        }

        /// <summary>人本来站在板上，板转走了 → 人也得走同样的位移。</summary>
        private void MoveRiders()
        {
            for (int i = 0; i < m_Riders.Count; i++)
            {
                PlayerController player = m_Riders[i];

                if (player == null)
                {
                    continue;
                }

                CharacterController controller = player.GetComponent<CharacterController>();

                if (controller == null || !controller.enabled)
                {
                    continue;
                }

                // 钻进泡泡 / 被平台托着的时候角色不走 CharacterController，
                // 这时候硬 Move 会和别的系统打架
                if (player.IsInCarrier)
                {
                    continue;
                }

                Vector3 target = transform.TransformPoint(m_RiderLocals[i]);
                Vector3 delta = target - player.transform.position;

                if (delta.sqrMagnitude > 0.0000001f)
                {
                    controller.Move(delta);
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

            // 玩家可能是运行时生成的，所以找不到就过一秒再找
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

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmos()
        {
            Vector3 pivot = transform.position;
            Vector3 axis = AxisWorld;

            // 支点
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(pivot, 0.18f);

            // 摆动轴（两侧各画一段）
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(pivot - axis * 1.2f, pivot + axis * 1.2f);

            // 竖直方向 + 两个极限角度
            Vector3 down = Vector3.down * m_PendulumLength;
            Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            Gizmos.DrawLine(pivot, pivot + down);

            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.85f);
            Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(-m_MaxAngle, axis) * down);
            Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(m_MaxAngle, axis) * down);
        }

        // ==================== 由搭建脚本调用 ====================

        /// <summary>
        /// 编辑器 / 搭建脚本用的配置入口。摆长一般传"支点到包围盒中心的高度"。
        /// </summary>
        public void Configure(Vector3 localAxis, float maxAngle, float pendulumLength,
                              float startAngle = 0f, float blastImpulse = 90f)
        {
            m_LocalAxis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.right;
            m_MaxAngle = Mathf.Clamp(maxAngle, 5f, 80f);
            m_PendulumLength = Mathf.Max(0.5f, pendulumLength);
            m_StartAngle = Mathf.Clamp(startAngle, -m_MaxAngle, m_MaxAngle);
            m_BlastImpulse = Mathf.Max(1f, blastImpulse);
        }
    }
}
