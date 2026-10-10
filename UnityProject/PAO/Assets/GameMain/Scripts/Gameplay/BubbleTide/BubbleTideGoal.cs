using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 终点标记：只声明「我是终点」，不承担任何逻辑。
    ///
    /// 为什么不让终点脚本自己去管潮水（原本的想法是「挂到终点上」）：
    /// 那样终点是谁、八个角在哪，就都成了对场景硬编码的依赖，关卡一改就得重配。
    /// 拆开之后，终点想换位置、想加第二个终点、想从一个角开始蔓延做教学关，
    /// 都只需要动场景里的物体，不用改代码。
    ///
    /// 截止时刻 = 八个角的潮水里，最早摸到这个点的那一个的时刻。
    /// 从那一刻起进入「已被淹没」状态，由 BubbleTideDirector 决定后续（倒计时 / 失败）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/终点")]
    public sealed class BubbleTideGoal : MonoBehaviour
    {
        [Header("判定")]
        [Tooltip("把终点当成一个球来判定，这是半径（米）。\n" +
                 "0 = 只有一个点被淹才算数，数值大一点更好命中。")]
        [SerializeField, Min(0f)] private float m_Radius = 0.5f;

        [Tooltip("潮水摸到终点后，还剩多少秒的宽限时间。\n" +
                 "这段时间里玩家仍然可以冲进去通关，用来做「最后一线机会」的手感。")]
        [SerializeField, Min(0f)] private float m_GraceSeconds = 8f;

        [Header("玩法")]
        [Tooltip("玩家进入这个距离（米）就算到达终点。")]
        [SerializeField, Min(0.1f)] private float m_ReachDistance = 2f;

        [Tooltip("到达后要不要按一次键才算数。关掉则走进去就通关。")]
        [SerializeField] private bool m_RequireConfirmKey = false;

        [Tooltip("确认键。")]
        [SerializeField] private KeyCode m_ConfirmKey = KeyCode.G;

        [Header("调试")]
        [SerializeField] private bool m_DrawGizmo = true;
        [SerializeField] private Color m_GizmoColor = new Color(1f, 0.85f, 0.3f, 0.9f);

        /// <summary>终点判定半径。</summary>
        public float Radius { get { return Mathf.Max(0f, m_Radius); } }

        /// <summary>被淹之后的宽限时间（秒）。</summary>
        public float GraceSeconds { get { return Mathf.Max(0f, m_GraceSeconds); } }

        /// <summary>玩家多近算到达。</summary>
        public float ReachDistance { get { return Mathf.Max(0.1f, m_ReachDistance); } }

        /// <summary>是否需要按确认键。</summary>
        public bool RequireConfirmKey { get { return m_RequireConfirmKey; } }

        /// <summary>确认键。</summary>
        public KeyCode ConfirmKey { get { return m_ConfirmKey; } }

        /// <summary>
        /// 潮水最早淹没这个终点的时刻（秒）。
        /// 由 BubbleTideDirector 在烤制完成后写入 —— 组件自己算不了，它不知道场。
        /// </summary>
        public float FloodDeadline { get; internal set; }

        /// <summary>这个终点是否已经被潮水淹没。</summary>
        public bool IsFlooded { get; internal set; }

        /// <summary>玩家是否已经到达。</summary>
        public bool IsReached { get; internal set; }

        private void OnDrawGizmos()
        {
            if (!m_DrawGizmo)
            {
                return;
            }

            // 被淹 = 红，正常 = 黄，到达 = 绿
            Color color = IsReached
                ? new Color(0.4f, 1f, 0.5f, 0.9f)
                : (IsFlooded ? new Color(1f, 0.35f, 0.3f, 0.9f) : m_GizmoColor);

            Gizmos.color = color;

            // 潮水判定半径
            if (Radius > 0.001f)
            {
                Gizmos.DrawWireSphere(transform.position, Radius);
            }

            // 玩家到达判定
            Gizmos.DrawWireSphere(transform.position, ReachDistance);

            // 十字标记，方便在远处也能看见
            float cross = Mathf.Max(0.3f, Radius);
            Gizmos.DrawLine(transform.position - Vector3.up * cross, transform.position + Vector3.up * cross);

#if UNITY_EDITOR
            UnityEditor.Handles.color = color;

            string label = IsReached
                ? "终点（已到达）"
                : (IsFlooded
                    ? string.Format("终点（已淹没，剩 {0:F1}s）", GraceSeconds)
                    : (FloodDeadline > 0f
                        ? string.Format("终点（将在 {0:F1}s 被淹）", FloodDeadline)
                        : "终点"));

            UnityEditor.Handles.Label(transform.position + Vector3.up * (cross + 0.5f), label);
#endif
        }
    }
}
