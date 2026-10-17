using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 绕着某个轴匀速转的障碍物（爬塔里的"扫过来的横杆"）。
    ///
    /// 【为什么单独一个组件，而不是塞进 BubblePopObstacle】
    /// 破裂判定和运动方式是两件独立的事：
    /// 尖刺群不转但要破泡，横杆转也要破泡，以后可能还有"上下移动的闸门"。
    /// 分成两个组件就能自由组合 —— 转不转由这个决定，
    /// 碰了会不会破由 <see cref="BubblePopObstacle"/> 决定。
    ///
    /// 【为什么挂在空的 pivot 上，而不是横杆自己身上】
    /// 横杆是个长条，绕自己的中心转会原地打转；
    /// 挂在 pivot 上才是"绕井道中心扫过去"，那才是要的效果。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/旋转障碍（Rotating Hazard）")]
    public sealed class RotatingHazard : MonoBehaviour
    {
        [Header("运动")]
        [Tooltip("绕哪个轴转（本地空间）")]
        [SerializeField] private Vector3 m_Axis = Vector3.up;

        [Tooltip("转速（度/秒）")]
        [SerializeField] private float m_Speed = 45f;

        [Tooltip("是否来回摆动而不是一直转。摆动更难预测，但不会把人绕晕")]
        [SerializeField] private bool m_PingPong = false;

        [Tooltip("摆动模式下的最大转角（度）")]
        [SerializeField] private float m_SwingAngle = 60f;

        private float m_Direction = 1f;
        private float m_Swung;

        /// <summary>当前的转速（度/秒）。带符号。</summary>
        public float CurrentSpeed { get { return m_Speed * (m_PingPong ? m_Direction : 1f); } }

        private void Update()
        {
            if (m_Speed == 0f)
            {
                return;
            }

            float delta = m_Speed * Time.deltaTime;

            if (m_PingPong)
            {
                // 摆到边界就反向
                if (m_Swung + delta * m_Direction > m_SwingAngle && m_Direction > 0f)
                {
                    m_Direction = -1f;
                }
                else if (m_Swung + delta * m_Direction < -m_SwingAngle && m_Direction < 0f)
                {
                    m_Direction = 1f;
                }

                float step = delta * m_Direction;
                m_Swung += step;
                transform.Rotate(m_Axis, step, Space.Self);
            }
            else
            {
                // 用 Time.timeScale 走正常时间 —— 暂停时障碍也该停
                transform.Rotate(m_Axis, delta, Space.Self);
            }
        }

        /// <summary>由搭建脚本调用。</summary>
        public void Configure(Vector3 axis, float speed, bool pingPong = false, float swingAngle = 60f)
        {
            m_Axis = axis;
            m_Speed = speed;
            m_PingPong = pingPong;
            m_SwingAngle = swingAngle;
        }
    }
}
