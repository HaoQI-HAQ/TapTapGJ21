using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 让物体**一直匀速转**。
    ///
    /// ============================================================
    /// 【和工程里其它旋转组件的区别】
    /// ============================================================
    ///   XYZRotator      —— 转一段 → 停一下 → 再转一段（定时步进）
    ///   RotatingHazard  —— 爬塔横杆，被炸弹炸到会停摆，还带破泡
    ///   本组件           —— 纯粹的"每秒转多少度"，没有别的行为
    ///
    /// 之所以单独做一个：只想让某个东西转起来时，
    /// 去配 XYZRotator 的「每段 360° + 间隔 0 + 循环」很容易配错，
    /// 而且那个组件还带着定时逻辑，读代码时会误以为有关卡含义。
    ///
    /// ============================================================
    /// 【用法】
    /// ============================================================
    ///   1. 挂到要转的物体上（Add Component → PAO → 关卡 → 持续旋转）
    ///   2. Axis 填旋转轴：默认 (0,1,0) = 绕自己头顶那根竖轴原地打转
    ///   3. Speed 填转速（度/秒）：90 = 4 秒转一圈
    ///
    /// ============================================================
    /// 【两个容易踩的坑（已在代码里处理/提示）】
    /// ============================================================
    /// 1. **物体上有非 Kinematic 的 Rigidbody 时转不动**
    ///    物理会每帧把 Transform 拽回它自己算出来的姿态，
    ///    所以你看到的是抖动或者干脆不转。要么把 Rigidbody 设成 Is Kinematic，
    ///    要么别给它挂刚体。进 Play 时会在 Console 里提醒。
    ///
    /// 2. **暂停时要跟着停**
    ///    默认用 Time.deltaTime，暂停菜单把 timeScale 设成 0 时它也会停。
    ///    如果某个东西就是要在暂停时继续转（比如 UI 上的加载圈），
    ///    勾上 Use Unscaled Time。
    /// ============================================================
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/持续旋转（Continuous Rotator）")]
    public sealed class ContinuousRotator : MonoBehaviour
    {
        [Header("旋转")]
        [Tooltip("绕哪个轴转（默认 Y = 绕自身竖轴原地打转）")]
        [SerializeField] private Vector3 m_Axis = Vector3.up;

        [Tooltip("转速（度/秒）。90 = 4 秒一圈；负数 = 反向")]
        [SerializeField] private float m_Speed = 90f;

        [Tooltip("按物体自己的坐标轴转；关掉则按世界坐标轴")]
        [SerializeField] private bool m_LocalSpace = true;

        [Header("时机")]
        [Tooltip("进入场景后延迟多少秒才开始转。0 = 立刻")]
        [SerializeField] private float m_StartDelay = 0f;

        [Tooltip("暂停时（timeScale = 0）是否继续转。一般不用勾")]
        [SerializeField] private bool m_UseUnscaledTime = false;

        [Header("调试")]
        [Tooltip("进 Play 时如果发现物体上有非 Kinematic 的 Rigidbody，打印一条提醒")]
        [SerializeField] private bool m_LogPhysicsWarning = true;

        // ---- 运行时 ----

        private bool m_Running = true;
        private float m_DelayLeft;

        /// <summary>当前转速（度/秒）。</summary>
        public float Speed
        {
            get { return m_Speed; }
            set { m_Speed = value; }
        }

        /// <summary>是否正在转（延迟期间也算"在转"，只是还没开始）。</summary>
        public bool IsRotating { get { return m_Running; } }

        /// <summary>距离开始旋转还有多少秒。</summary>
        public float DelayRemaining { get { return Mathf.Max(0f, m_DelayLeft); } }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            m_DelayLeft = Mathf.Max(0f, m_StartDelay);

            if (!m_LogPhysicsWarning)
            {
                return;
            }

            // 非 Kinematic 的刚体会和 Transform 打架
            Rigidbody body = GetComponent<Rigidbody>();

            if (body != null && !body.isKinematic)
            {
                Debug.LogWarning("[ContinuousRotator] \"" + name + "\" 上有非 Kinematic 的 Rigidbody，" +
                                 "物理会每帧把 Transform 拽回去，物体会抖动或者转不动。\n" +
                                 "把 Rigidbody 的 Is Kinematic 勾上，或者移除刚体。", this);
            }
        }

        private void Update()
        {
            if (!m_Running)
            {
                return;
            }

            float dt = m_UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            // 启动延迟
            if (m_DelayLeft > 0f)
            {
                m_DelayLeft -= dt;

                if (m_DelayLeft > 0f)
                {
                    return;
                }

                m_DelayLeft = 0f;
            }

            if (Mathf.Approximately(m_Speed, 0f))
            {
                return;
            }

            // 轴设成零向量时转了也没意义，避免 Quaternion.AngleAxis 拿到无效轴
            if (m_Axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            // Transform.Rotate 内部会归一化轴，所以不怕 Axis 填成 (0,2,0) 这种
            transform.Rotate(m_Axis, m_Speed * dt, m_LocalSpace ? Space.Self : Space.World);
        }

        // ==================== 对外接口 ====================

        /// <summary>停下，保持在当前朝向。</summary>
        public void Stop()
        {
            m_Running = false;
        }

        /// <summary>从当前朝向继续转。</summary>
        public void Resume()
        {
            m_Running = true;
        }

        /// <summary>改转速（度/秒）。运行时也能调。</summary>
        public void SetSpeed(float degreesPerSecond)
        {
            m_Speed = degreesPerSecond;
        }

        /// <summary>重新开始计时（比如重新触发启动延迟）。</summary>
        public void Restart()
        {
            m_Running = true;
            m_DelayLeft = Mathf.Max(0f, m_StartDelay);
        }

        /// <summary>给搭建脚本一次性设置。</summary>
        public void Configure(Vector3 axis, float speed,
                              bool localSpace = true, float startDelay = 0f)
        {
            m_Axis = axis;
            m_Speed = speed;
            m_LocalSpace = localSpace;
            m_StartDelay = Mathf.Max(0f, startDelay);
            m_DelayLeft = m_StartDelay;
        }

        private void OnValidate()
        {
            if (m_StartDelay < 0f)
            {
                m_StartDelay = 0f;
            }
        }

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmosSelected()
        {
            if (m_Axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            Vector3 axis = m_Axis.normalized;
            float len = 1.2f;

            Gizmos.color = new Color(0.75f, 0.6f, 1f, 0.9f);

            // 旋转轴：从物体中心往两边各画一段
            Vector3 a = transform.position - axis * len;
            Vector3 b = transform.position + axis * len;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawCube(b, Vector3.one * 0.1f);

            // 转方向：在轴末端画一小段弧，正反用不同颜色
            Gizmos.color = m_Speed >= 0f
                ? new Color(0.4f, 0.95f, 1f, 0.9f)
                : new Color(1f, 0.6f, 0.4f, 0.9f);

            DrawArc(transform.position, axis, 0.7f);
        }

        /// <summary>画一小段圆环示意转向（只是编辑器辅助，不参与运行）。</summary>
        private void DrawArc(Vector3 center, Vector3 axis, float radius)
        {
            // 找一个和轴不共线的向量，用来构造圆环的基
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f
                ? Vector3.right : Vector3.up;

            Vector3 u = Vector3.Normalize(Vector3.Cross(axis, reference));
            Vector3 v = Vector3.Cross(axis, u);

            int steps = 16;
            float sweep = 200f * Mathf.Deg2Rad * Mathf.Sign(m_Speed == 0f ? 1f : m_Speed);

            Vector3 prev = center + u * radius;

            for (int i = 1; i <= steps; i++)
            {
                float t = sweep * i / steps;
                Vector3 point = center + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                Gizmos.DrawLine(prev, point);
                prev = point;
            }
        }
    }
}
