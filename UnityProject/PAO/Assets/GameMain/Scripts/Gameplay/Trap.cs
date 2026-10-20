using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 陷阱：让挂着的这个物体**每隔一段时间动一下** —— 往 X / Y / Z 平移，或者绕 X / Y / Z 旋转，
    /// 两样可以同时来。
    ///
    /// 【它动的是谁】
    /// 动的是脚本挂着的那个物体的 Transform。挂在父物体上 = 带着所有子物体一起动，
    /// 所以「转轴空物体 + 底下的尖刺」这种摆法，把脚本挂到空物体上就行。
    ///
    /// 【怎么用】
    ///   1. 挂到要动的物体上：Add Component → PAO → 关卡 → 陷阱
    ///   2. 「位移」填 (0, 2, 0) = 每次往上抬 2 米；(2, 0, 0) = 每次往 +X 走 2 米。
    ///      某个轴填 0 就不动那个轴，三轴都填 0 就只旋转。
    ///   3. 「旋转」填 (0, 90, 0) = 每次绕 Y 轴拧 90°。多轴可以一起填。
    ///   4. 「用时」= 做完这一次要几秒；「间隔」= 两次动作之间停几秒。
    ///   5. 勾「往返」= 第一次 +2 米、第二次 -2 米，来回弹；不勾 = 每次都 +2 米，一直往一个方向走。
    ///
    /// 【默认参数就是「陷阱」的样子】
    /// 位移 (0,2,0)、旋转 0、用时 0.6 秒、间隔 2 秒、往返：
    /// 升起来 → 停 2 秒 → 落回去 → 停 2 秒 → 再升起来……
    ///
    /// 【角度和距离都是「再走多远」，不是「走到哪」】
    /// 和 XYZRotator 一样是相对量：物体在场景里摆成什么角度、什么位置都行，
    /// 不用先算终点，也不怕拖来拖去。每次动作都从「触发那一刻的姿势」重新开始算，
    /// 中途被别的脚本挪过也不会越跑越偏。
    ///
    /// 【暂停】
    /// 用 Time.deltaTime 计时，暂停菜单把 timeScale 设成 0 时它也跟着停。
    /// 暂停菜单恢复时会把玩法组件重新 enable —— 这个脚本不会因此从头再来（见 OnEnable），
    /// 否则每暂停一次就会多走一段。
    ///
    /// 【它不负责伤害】
    /// 只负责动。想让它伤人，同一个物体上再挂 <see cref="DeathTrigger"/>（碰到就死）
    /// 或 <see cref="BubblePopObstacle"/>（碰到破泡），和 RotatingHazard 的分工一样：
    /// 运动 / 判定拆成两个组件，自由组合。
    ///
    /// 【它不背人】
    /// 站上去的玩家不会被带着走（没有 CharacterController.Move 那套）。
    /// 要「载人平台」用 WhiteboxPathPlatform。
    ///
    /// 【有刚体时】
    /// 物体上如果有**非 Kinematic** 的 Rigidbody，直接改 Transform 会被物理拽回去。
    /// 「用刚体推动」（默认勾上）会改走 MovePosition / MoveRotation，位置才是对的。
    ///
    /// 【想「踩到才动」】
    /// 关掉「自动循环」，再从别的脚本（触发区、拉杆）调 <see cref="Trigger"/>()。
    /// 自动循环开着时 Trigger() 也能用，效果是插队：不等间隔，立刻做一次。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/陷阱（Trap）")]
    public sealed class Trap : MonoBehaviour
    {
        /// <summary>现在走到哪一步了。</summary>
        private enum State
        {
            /// <summary>没在跑，等外部 Trigger()。</summary>
            Idle,

            /// <summary>在等下一次触发（倒计时 m_Timer）。</summary>
            Waiting,

            /// <summary>正在动（用 m_Elapsed / m_Duration 取曲线位置）。</summary>
            Acting,
        }

        [Header("动作（每触发一次做一遍）")]
        [Tooltip("每次沿 X / Y / Z 移动多少米（是「再走多远」，不是「走到哪」）。\n" +
                 "填 0 的轴不动；三轴都填 0 = 只旋转。\n" +
                 "例：(0, 2, 0) 每次抬 2 米；(2, 0, 0) 每次往 +X 走 2 米")]
        [SerializeField]
        private Vector3 m_MoveOffset = new Vector3(0f, 2f, 0f);

        [Tooltip("每次绕 X / Y / Z 转多少度（同样是在当前朝向上再拧）。\n" +
                 "填 0 的轴不转；三轴都填 0 = 只平移。\n" +
                 "例：(0, 90, 0) 每次绕 Y 轴拧 90°")]
        [SerializeField]
        private Vector3 m_RotateEuler = Vector3.zero;

        [Tooltip("做完这一次用多少秒。0 = 瞬间到位（像闪现）")]
        [SerializeField]
        private float m_Duration = 0.6f;

        [Tooltip("缓动曲线：横轴 0→1 是时间，纵轴 0→1 是走了多少。\n" +
                 "默认两头慢、中间快。想「猛地弹出去」把曲线提前拉满；\n" +
                 "想带一点过冲（弹簧感），把中段抬到 1 以上")]
        [SerializeField]
        private AnimationCurve m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("节奏")]
        [Tooltip("进游戏后先等多少秒才动第一次")]
        [SerializeField]
        private float m_StartDelay = 0f;

        [Tooltip("两次动作之间停多少秒")]
        [SerializeField]
        private float m_Interval = 2f;

        [Tooltip("自动循环：按「间隔」一段一段地自己动。\n" +
                 "关掉 = 不自己动，只等别的脚本调 Trigger()（做「踩到才触发」的陷阱用这个）")]
        [SerializeField]
        private bool m_AutoLoop = true;

        [Tooltip("只触发一次，之后彻底停住（一次性陷阱）")]
        [SerializeField]
        private bool m_Once = false;

        [Tooltip("往返：下一次动作反向（+2 米 → -2 米 → +2 米……）。\n" +
                 "关掉：每次都朝同一个方向累加（一直往一个方向走 / 一直朝一个方向转）")]
        [SerializeField]
        private bool m_PingPong = true;

        [Header("坐标空间")]
        [Tooltip("按物体自己的坐标轴移动 / 旋转（推荐：物体怎么摆都行）。\n" +
                 "关掉则按世界坐标轴 X / Y / Z")]
        [SerializeField]
        private bool m_LocalSpace = true;

        [Header("物理（一般不用动）")]
        [Tooltip("物体上有「非 Kinematic」的 Rigidbody 时，改用 MovePosition / MoveRotation 推它，\n" +
                 "免得物理把 Transform 拽回去。没有刚体、或刚体是 Kinematic 时这个选项不起作用")]
        [SerializeField]
        private bool m_DriveRigidbody = true;

        [Header("调试")]
        [Tooltip("每次开始 / 停住时在 Console 打一行日志")]
        [SerializeField]
        private bool m_Log = false;

        // ==================== 运行时 ====================

        private State m_State;
        private bool m_Running;
        private bool m_FinishedOnce;
        private int m_Direction = 1;
        private int m_TriggerCount;
        private float m_Timer;
        private float m_Elapsed;

        // 这一次动作的起点。每次都从「触发那一刻」重新取，
        // 所以中途被物理或别的脚本挪过，也不会一帧一帧地累积误差。
        private Vector3 m_ActionStartPosition;
        private Quaternion m_ActionStartRotation;
        private Vector3 m_ActionMove;
        private Vector3 m_ActionEuler;

        private Rigidbody m_Rigidbody;

        /// <summary>是否在跑（「只触发一次」做完后会变回 false）。</summary>
        public bool IsRunning { get { return m_Running; } }

        /// <summary>这一刻是不是正在动。</summary>
        public bool IsActing { get { return m_State == State.Acting; } }

        /// <summary>一共做过几次动作（排查「到底触发了没有」时看这个）。</summary>
        public int TriggerCount { get { return m_TriggerCount; } }

        /// <summary>有非 Kinematic 刚体时改用物理移动。</summary>
        private bool DrivesRigidbody
        {
            get { return m_DriveRigidbody && m_Rigidbody != null && !m_Rigidbody.isKinematic; }
        }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();

            if (m_MoveOffset.sqrMagnitude <= 0.0001f && m_RotateEuler.sqrMagnitude <= 0.0001f)
            {
                Debug.LogWarning("[Trap] \"" + name + "\" 的「位移」和「旋转」都是 0，" +
                                 "它不会动。至少给一个轴填个数。", this);
            }

            if (m_Rigidbody != null && !m_Rigidbody.isKinematic && !m_DriveRigidbody)
            {
                Debug.LogWarning("[Trap] \"" + name + "\" 上有非 Kinematic 的 Rigidbody，" +
                                 "但「用刚体推动」没勾 —— 物理会把位置拽回去。" +
                                 "要么勾上它，要么把 Rigidbody 设成 Is Kinematic。", this);
            }
        }

        private void OnEnable()
        {
            // 编辑模式下挂组件不该自己动，免得在 Scene 视图里一挂上就跑了
            if (!Application.isPlaying)
            {
                return;
            }

            // 暂停菜单恢复时会把玩法组件重新 enable。
            // 这里不能无脑重来：物体可能正动到一半，从头来的话
            // 每暂停 / 恢复一次就会多走一段，位置越飘越远。
            if (m_Running || m_FinishedOnce)
            {
                return;
            }

            if (m_AutoLoop)
            {
                StartTrap();
            }
        }

        private void Start()
        {
            // 兜底：个别 Enter Play Mode 设置下，进 Play 时 OnEnable 不一定再触发一次
            if (m_Running || m_FinishedOnce)
            {
                return;
            }

            if (m_AutoLoop)
            {
                StartTrap();
            }
        }

        private void Update()
        {
            if (!m_Running)
            {
                return;
            }

            // ---- 等间隔 ----
            if (m_State == State.Waiting)
            {
                m_Timer -= Time.deltaTime;

                if (m_Timer > 0f)
                {
                    return;
                }

                BeginAction();
                return;
            }

            if (m_State != State.Acting)
            {
                return;
            }

            // ---- 正在动 ----
            m_Elapsed += Time.deltaTime;

            float t = m_Duration > 0.0001f ? Mathf.Clamp01(m_Elapsed / m_Duration) : 1f;

            ApplyPose(t);

            if (t >= 1f)
            {
                FinishAction();
            }
        }

        // ==================== 对外接口 ====================

        /// <summary>
        /// 从当前位置重新开始：先等「首次延迟」，然后按间隔一段段地动。
        /// </summary>
        public void StartTrap()
        {
            m_Running = true;
            m_FinishedOnce = false;
            m_Direction = 1;
            m_Elapsed = 0f;
            m_State = State.Waiting;
            m_Timer = Mathf.Max(0f, m_StartDelay);

            if (m_Log)
            {
                Debug.Log("[Trap] \"" + name + "\" 开始，" +
                          m_Timer.ToString("F2") + " 秒后第一次触发。", this);
            }
        }

        /// <summary>
        /// 立刻做一次动作（不等间隔）。
        /// 「自动循环」关掉时就是靠它触发；开着时它相当于插队。
        /// 正在动的时候调用会被忽略 —— 不打断当前这一段。
        /// </summary>
        public void Trigger()
        {
            if (m_State == State.Acting)
            {
                return;
            }

            if (m_Once && m_FinishedOnce)
            {
                return;
            }

            m_Running = true;
            BeginAction();
        }

        /// <summary>停下，停在当前姿势（不会自己走回起点）。</summary>
        public void Stop()
        {
            m_Running = false;
            m_State = State.Idle;
            m_Elapsed = 0f;
            m_Timer = 0f;
        }

        /// <summary>给搭建脚本 / 编辑器工具一次性改参数。</summary>
        public void Configure(Vector3 moveOffset, Vector3 rotateEuler, float duration, float interval,
                              bool localSpace = true, bool pingPong = true)
        {
            m_MoveOffset = moveOffset;
            m_RotateEuler = rotateEuler;
            m_Duration = Mathf.Max(0f, duration);
            m_Interval = Mathf.Max(0f, interval);
            m_LocalSpace = localSpace;
            m_PingPong = pingPong;

            if (Application.isPlaying && m_AutoLoop)
            {
                StartTrap();
            }
        }

        // ==================== 动作 ====================

        /// <summary>开始这一次动作：记下起点，算出这一次要走的世界空间位移 / 角度。</summary>
        private void BeginAction()
        {
            // 用刚体时以刚体自己的姿势为起点：MovePosition 要到下一个物理步才生效，
            // 这时候 transform.position 可能还停在前一帧。
            m_ActionStartPosition = DrivesRigidbody ? m_Rigidbody.position : transform.position;
            m_ActionStartRotation = DrivesRigidbody ? m_Rigidbody.rotation : transform.rotation;

            Vector3 move = m_PingPong ? m_MoveOffset * m_Direction : m_MoveOffset;
            Vector3 euler = m_PingPong ? m_RotateEuler * m_Direction : m_RotateEuler;

            // 本体坐标轴 → 世界方向。世界空间就直接用 Inspector 里填的数。
            m_ActionMove = m_LocalSpace ? transform.rotation * move : move;
            m_ActionEuler = euler;

            m_State = State.Acting;
            m_Elapsed = 0f;
            m_TriggerCount++;

            if (m_Log)
            {
                Debug.Log("[Trap] \"" + name + "\" 第 " + m_TriggerCount + " 次触发：" +
                          "移动 " + move.ToString("F2") + " 米，旋转 " + euler.ToString("F1") + " 度，" +
                          m_Duration.ToString("F2") + " 秒做完。", this);
            }

            // 用时为 0：当场到位，不用等下一帧
            if (m_Duration <= 0.0001f)
            {
                ApplyPose(1f);
                FinishAction();
            }
        }

        /// <summary>按曲线把物体摆到「这一次动作的 t（0~1）」。</summary>
        private void ApplyPose(float t)
        {
            // 曲线可能在 Inspector 里被清空，兜底用线性
            float eased = m_Curve != null && m_Curve.length > 0 ? m_Curve.Evaluate(t) : t;

            Vector3 position = m_ActionStartPosition + m_ActionMove * eased;

            Quaternion rotation;

            if (m_ActionEuler.sqrMagnitude > 0.0001f)
            {
                Quaternion delta = Quaternion.Euler(m_ActionEuler * eased);

                // 本体空间：绕自己的轴转 = 在起点朝向上右乘；
                // 世界空间：绕世界轴转 = 左乘。
                rotation = m_LocalSpace
                    ? m_ActionStartRotation * delta
                    : delta * m_ActionStartRotation;
            }
            else
            {
                rotation = m_ActionStartRotation;
            }

            if (DrivesRigidbody)
            {
                m_Rigidbody.MovePosition(position);
                m_Rigidbody.MoveRotation(rotation);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
        }

        /// <summary>这一段做完了：翻转向，决定是等下一次、还是彻底停住。</summary>
        private void FinishAction()
        {
            m_State = State.Idle;
            m_Elapsed = 0f;
            m_Timer = 0f;

            if (m_PingPong)
            {
                m_Direction = -m_Direction;
            }

            if (m_Once)
            {
                m_Running = false;
                m_FinishedOnce = true;

                if (m_Log)
                {
                    Debug.Log("[Trap] \"" + name + "\" 只触发一次，停住。", this);
                }

                return;
            }

            if (!m_AutoLoop)
            {
                // 手动模式：停在这儿，等下一次 Trigger()
                return;
            }

            m_State = State.Waiting;
            m_Timer = Mathf.Max(0f, m_Interval);
        }

        // ==================== 编辑器 ====================

        private void OnValidate()
        {
            m_Duration = Mathf.Max(0f, m_Duration);
            m_Interval = Mathf.Max(0f, m_Interval);
            m_StartDelay = Mathf.Max(0f, m_StartDelay);
        }

        [ContextMenu("手动触发一次（Trigger）")]
        private void TriggerFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[Trap] 运行时才会动；进 Play 之后再点这个。", this);
                return;
            }

            Trigger();
        }

        /// <summary>
        /// 选中时画出：这一次会往哪走（黄线）、会朝哪个轴拧多少度（蓝扇面）。
        /// 只是示意，往返的反方向没画。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = transform.position;

            if (m_MoveOffset.sqrMagnitude > 0.0001f)
            {
                Vector3 move = m_LocalSpace ? transform.rotation * m_MoveOffset : m_MoveOffset;

                Gizmos.color = new Color(1f, 0.78f, 0.25f, 0.9f);
                Gizmos.DrawLine(origin, origin + move);
                Gizmos.DrawWireSphere(origin + move, 0.12f);
            }

            if (m_RotateEuler.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 localAxis = DominantAxis(m_RotateEuler);
            Vector3 worldAxis = m_LocalSpace ? transform.rotation * localAxis : localAxis;

            // 取「主方向轴」上的带符号角度，别的轴上的分量忽略（画图只是示意）
            float signedAngle = Vector3.Dot(localAxis, m_RotateEuler);

            // 找一个和转轴垂直的起点方向；转轴和 up 平行时换一个参照
            Vector3 from = Vector3.Cross(worldAxis, Vector3.up);

            if (from.sqrMagnitude < 0.001f)
            {
                from = Vector3.Cross(worldAxis, Vector3.right);
            }

            from.Normalize();

            const int kSegments = 24;
            const float kRadius = 1f;

            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.9f);

            for (int i = 1; i <= kSegments; i++)
            {
                Quaternion step = Quaternion.AngleAxis(signedAngle * i / kSegments, worldAxis);
                Vector3 point = origin + step * from * kRadius;

                Gizmos.DrawLine(origin, point);

                if (i == kSegments)
                {
                    Gizmos.DrawWireSphere(point, 0.08f);
                }
            }
        }

        /// <summary>X / Y / Z 里哪一轴的角度最大。</summary>
        private static Vector3 DominantAxis(Vector3 euler)
        {
            Vector3 abs = new Vector3(Mathf.Abs(euler.x), Mathf.Abs(euler.y), Mathf.Abs(euler.z));

            if (abs.x >= abs.y && abs.x >= abs.z)
            {
                return Vector3.right;
            }

            return abs.y >= abs.z ? Vector3.up : Vector3.forward;
        }
    }
}
