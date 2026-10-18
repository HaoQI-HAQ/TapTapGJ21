using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 让物体按 X/Y/Z 设定的角度**一段一段地转**：
    /// 转一段 → 停「间隔」秒 → 再转一段，速度和间隔都能在 Inspector 里调。
    ///
    /// 【怎么用】
    ///   1. 挂到要转的物体上（Add Component → PAO → 关卡 → XYZ 定时旋转）
    ///   2. 「每段角度」填 (0, 90, 0)：每段绕 Y 轴转 90°
    ///   3. 「旋转速度」填 90（度/秒），「间隔」填 1（秒）
    ///   4. 运行：转 90°（1 秒）→ 停 1 秒 → 再转 90° → ……
    ///
    /// 【每段角度可以填 360】
    ///   填 (360, 0, 0) 就是每段绕 X 轴转一整圈。
    ///   想一直朝同一个方向转：关掉「往返」。
    ///
    /// 【往返】
    ///   勾上后这一段的转角和上一段相反：
    ///   +90° → -90° → +90° ……
    ///   注意：(360,0,0) 再加往返 = 转一圈再倒着转回来。
    ///
    /// 【角度是相对的】
    ///   每一段都是从**当前朝向**再转过去，不是「转到某个绝对角度」。
    ///   所以物体在场景里摆成什么角度都行，不用先算最终角度。
    ///
    /// 【暂停】
    ///   用 Time.deltaTime，暂停菜单把 timeScale 设为 0 时它也会停。
    ///   如果物体上有非 Kinematic 的 Rigidbody，Transform 会被物理拽回去，
    ///   要么把 Rigidbody 设成 Is Kinematic，要么别给它加刚体。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/XYZ 定时旋转（XYZ Rotator）")]
    public sealed class XYZRotator : MonoBehaviour
    {
        [Header("旋转")]
        [Tooltip("每一段转多少度（X/Y/Z 各轴的角度，不是方向向量）。填 360 就是转整圈")]
        [SerializeField]
        private Vector3 m_StepEuler = new Vector3(0f, 90f, 0f);

        [Tooltip("旋转速度（度/秒）。多轴同时转时，每个轴各按这个速度走")]
        [SerializeField]
        private float m_Speed = 90f;

        [Tooltip("每转完一段停多久（秒）再转下一段")]
        [SerializeField]
        private float m_Interval = 1f;

        [Header("循环")]
        [Tooltip("关掉 = 只转一段就停")]
        [SerializeField]
        private bool m_Loop = true;

        [Tooltip("往返：这一段的转角和上一段相反，转过去再转回来")]
        [SerializeField]
        private bool m_PingPong = false;

        [Tooltip("按物体自己的坐标轴转；关掉则按世界坐标轴")]
        [SerializeField]
        private bool m_LocalSpace = true;

        // 这一段还剩多少度没转（X/Y/Z 分别记）
        private Vector3 m_Remaining;

        private bool m_Running;
        private bool m_Turning;
        private bool m_Pausing;
        private float m_PauseRemaining;
        private int m_Direction = 1;

        /// <summary>当前是否在转（关掉「循环」且转完一段后会变成 false）。</summary>
        public bool IsRunning { get { return m_Running; } }

        private void OnEnable()
        {
            // 编辑模式下 AddComponent 也会触发 OnEnable，这里不启动 —— 免得一挂上就自己转
            if (Application.isPlaying)
            {
                Restart();
            }
        }

        private void Start()
        {
            // 兜底：个别 Enter Play Mode 设置下，进 Play 时 OnEnable 不一定再触发一次
            if (!m_Running)
            {
                Restart();
            }
        }

        private void Update()
        {
            if (!m_Running)
            {
                return;
            }

            if (m_Pausing)
            {
                m_PauseRemaining -= Time.deltaTime;

                if (m_PauseRemaining > 0f)
                {
                    return;
                }

                m_Pausing = false;
                BeginSegment();
                return;
            }

            if (!m_Turning)
            {
                return;
            }

            float maxDegrees = m_Speed * Time.deltaTime;

            if (maxDegrees <= 0f)
            {
                return;
            }

            // 每个轴各自朝 0 走。用"剩余角度"而不是四元数目标，
            // 因为 360° 和 0° 是同一个四元数 —— 拿目标四元数比的话整圈会被当成"已经到了"，原地不动。
            Vector3 delta = new Vector3(
                StepTowards(m_Remaining.x, maxDegrees),
                StepTowards(m_Remaining.y, maxDegrees),
                StepTowards(m_Remaining.z, maxDegrees));

            m_Remaining -= delta;
            ApplyDegrees(delta);

            if (m_Remaining.sqrMagnitude <= 0.0001f)
            {
                m_Remaining = Vector3.zero;
                FinishSegment();
            }
        }

        /// <summary>从当前朝向重新开始转。</summary>
        public void Restart()
        {
            m_Direction = 1;
            m_Remaining = Vector3.zero;
            m_Turning = false;
            m_Pausing = false;
            m_PauseRemaining = 0f;
            m_Running = true;
            BeginSegment();
        }

        /// <summary>停下，保持在当前朝向。</summary>
        public void Stop()
        {
            m_Running = false;
            m_Turning = false;
            m_Pausing = false;
        }

        /// <summary>给搭建脚本 / 其他代码一次性改参数。</summary>
        public void Configure(Vector3 stepEuler, float speed, float interval,
                              bool loop, bool pingPong, bool localSpace)
        {
            m_StepEuler = stepEuler;
            m_Speed = Mathf.Max(0.01f, speed);
            m_Interval = Mathf.Max(0f, interval);
            m_Loop = loop;
            m_PingPong = pingPong;
            m_LocalSpace = localSpace;

            if (Application.isPlaying)
            {
                Restart();
            }
        }

        /// <summary>开始新的一段：把这一步要转的角度记成"剩余量"。</summary>
        private void BeginSegment()
        {
            Vector3 step = m_PingPong ? m_StepEuler * m_Direction : m_StepEuler;

            if (step.sqrMagnitude <= 0.0001f)
            {
                m_Turning = false;
                m_Running = false;
                Debug.LogWarning("[XYZRotator] \"" + name + "\" 的「每段角度」是 0，不会转。", this);
                return;
            }

            m_Remaining = step;
            m_Turning = true;
        }

        /// <summary>一段转完：要么继续下一段，要么停下。</summary>
        private void FinishSegment()
        {
            m_Turning = false;

            if (!m_Loop)
            {
                m_Running = false;
                return;
            }

            if (m_PingPong)
            {
                m_Direction = -m_Direction;
            }

            if (m_Interval <= 0f)
            {
                BeginSegment();
                return;
            }

            m_Pausing = true;
            m_PauseRemaining = m_Interval;
        }

        /// <summary>某一轴这一帧该走多少度（不会走过头）。</summary>
        private static float StepTowards(float remaining, float maxDegrees)
        {
            if (Mathf.Abs(remaining) <= maxDegrees)
            {
                return remaining;
            }

            return Mathf.Sign(remaining) * maxDegrees;
        }

        /// <summary>按 X/Y/Z 的角度增量转一小步。</summary>
        private void ApplyDegrees(Vector3 degrees)
        {
            // 掉帧或速度特别快时，一帧的转角可能超过 360°。
            // Quaternion.Euler(360) 会变成 0，所以拆成每段不超过 90° 再叠加。
            float maxAxis = Mathf.Max(Mathf.Abs(degrees.x), Mathf.Abs(degrees.y), Mathf.Abs(degrees.z));
            int chunks = Mathf.Max(1, Mathf.CeilToInt(maxAxis / 90f));
            Vector3 chunk = degrees / chunks;

            for (int i = 0; i < chunks; i++)
            {
                Quaternion delta = Quaternion.Euler(chunk);

                if (m_LocalSpace)
                {
                    transform.localRotation = transform.localRotation * delta;
                }
                else
                {
                    transform.rotation = delta * transform.rotation;
                }
            }
        }

        private void OnValidate()
        {
            m_Speed = Mathf.Max(0.01f, m_Speed);
            m_Interval = Mathf.Max(0f, m_Interval);
        }

        [ContextMenu("重新开始（Restart）")]
        private void RestartFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[XYZRotator] 运行时才会转；进 Play 之后再点这个。", this);
                return;
            }

            Restart();
        }
    }
}
