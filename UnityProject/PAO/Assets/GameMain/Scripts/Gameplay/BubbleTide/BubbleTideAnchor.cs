using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡潮的源头：正方体的一个角。
    ///
    /// 这个组件本身不做任何计算，只是一份「声明」——
    /// 告诉 BubbleTideDirector「潮水从这里出发，速度是这个倍率」。
    /// 判定逻辑全在 BubbleTideField 的 Dijkstra 里。
    ///
    /// 这样拆的好处：角想加、想减、想单独调某一个角、想做「只从一个角开始蔓延」
    /// 的教学关，都不用动主逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/潮水源（角）")]
    public sealed class BubbleTideAnchor : MonoBehaviour
    {
        [Header("速度")]
        [Tooltip("这个角的速度倍率。1 = 按全局公式算出的速度，0.5 = 慢一半，2 = 快一倍。\n" +
                 "想让某一路潮水晚点出发就把它调小。")]
        [SerializeField, Min(0.01f)] private float m_SpeedMultiplier = 1f;

        [Tooltip("出发延迟（秒）。这个角会在关卡开始后等这么久才开始冒泡。\n" +
                 "用它做「先淹一边，另一边后来才起来」的节奏。")]
        [SerializeField, Min(0f)] private float m_StartDelay = 0f;

        [Header("调试")]
        [Tooltip("在 Scene 视图里画出这个角的影响范围，方便确认八个角摆对了位置。")]
        [SerializeField] private bool m_DrawGizmo = true;

        [SerializeField] private Color m_GizmoColor = new Color(0.45f, 0.85f, 1f, 0.9f);

        /// <summary>速度倍率。</summary>
        public float SpeedMultiplier { get { return Mathf.Max(0.01f, m_SpeedMultiplier); } }

        /// <summary>出发延迟（秒）。</summary>
        public float StartDelay { get { return Mathf.Max(0f, m_StartDelay); } }

        /// <summary>
        /// 这个角的「有效速度倍率」：把出发延迟折算进速度里。
        ///
        /// 为什么把延迟折成速度，而不是真的晚点开始跑 Dijkstra：
        /// 多源 Dijkstra 只跑一次就把八个角一起解决了，这是它最大的好处。
        /// 如果为了延迟去给每个角单独跑一遍，就跑八次了。
        ///
        /// 折算的方式是 v' = d / (d/v + delay) —— 也就是「本来要走 d/v 秒，
        /// 现在改成 d/v + delay 秒走完」，效果等同于延迟出发。
        /// 对固定的速度场来说这是精确等价的。
        /// </summary>
        public float GetEffectiveMultiplier(float baseSpeed, float distanceToGoal)
        {
            float delay = StartDelay;
            if (delay <= 0.0001f)
            {
                return SpeedMultiplier;
            }

            float baseWithMultiplier = Mathf.Max(0.01f, baseSpeed * SpeedMultiplier);
            float travelTime = distanceToGoal / baseWithMultiplier;
            float totalTime = travelTime + delay;

            if (totalTime <= 0.0001f)
            {
                return SpeedMultiplier;
            }

            // 新速度 = 距离 / 新总时间
            float newSpeed = distanceToGoal / totalTime;

            return Mathf.Max(0.01f, newSpeed / Mathf.Max(0.01f, baseSpeed));
        }

        private void OnDrawGizmos()
        {
            if (!m_DrawGizmo)
            {
                return;
            }

            Gizmos.color = m_GizmoColor;

            float size = 0.8f;
            Gizmos.DrawWireSphere(transform.position, size);
            Gizmos.DrawLine(transform.position - Vector3.right * size, transform.position + Vector3.right * size);
            Gizmos.DrawLine(transform.position - Vector3.up * size, transform.position + Vector3.up * size);
            Gizmos.DrawLine(transform.position - Vector3.forward * size, transform.position + Vector3.forward * size);

#if UNITY_EDITOR
            UnityEditor.Handles.color = m_GizmoColor;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * (size + 0.3f),
                string.Format("潮水 ×{0:F2}{1}",
                    m_SpeedMultiplier,
                    m_StartDelay > 0f ? string.Format("  (+{0:F1}s)", m_StartDelay) : string.Empty));
#endif
        }
    }
}
