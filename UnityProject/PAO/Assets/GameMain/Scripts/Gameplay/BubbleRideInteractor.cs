using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡载人交互：靠近够大的浮粘泡泡时显示 [F] 提示，
    /// 按 F 钻进去，再按 F 跳出来；泡泡被打破或到寿命则自动掉出来。
    ///
    /// 挂在 Player 上（和 BubbleLauncher、PlayerController 同一个物体）。
    /// </summary>
    public class BubbleRideInteractor : MonoBehaviour
    {
        [Header("交互")]
        [Tooltip("能触发进入提示的距离（米）")]
        [SerializeField] private float m_SearchRadius = 4f;

        [Tooltip("进入 / 退出的按键")]
        [SerializeField] private KeyCode m_RideKey = KeyCode.F;

        [Header("UI")]
        [Tooltip("提示文字大小")]
        [SerializeField] private int m_HintFontSize = 22;

        [Tooltip("提示显示在泡泡上方多高处（米）")]
        [SerializeField] private float m_HintHeightOffset = 0.6f;

        private Bubble m_NearbyBubble;      // 附近可进入的泡泡
        private Bubble m_RidingBubble;      // 正在乘坐的泡泡
        private bool m_IsRiding;            // 显式标记：不能只靠 m_RidingBubble 判空

        private PlayerController m_PlayerController;
        private CharacterController m_CharacterController;
        private Collider[] m_PlayerColliders;   // 玩家身上所有碰撞体，进出泡泡时统一开关
        private Camera m_Camera;
        private GUIStyle m_HintStyle;
        private GUIStyle m_HintShadowStyle;

        /// <summary>是否正在泡泡里。</summary>
        public bool IsRiding
        {
            get { return m_IsRiding; }
        }

        private void Awake()
        {
            m_PlayerController = GetComponent<PlayerController>();
            m_CharacterController = GetComponent<CharacterController>();

            // 连子物体上的碰撞体一起收。只关 CharacterController 不够——
            // 角色模型自带的碰撞体会顶到泡泡内壁，把泡泡顶得乱转
            m_PlayerColliders = GetComponentsInChildren<Collider>(true);
        }

        private void Update()
        {
            if (m_IsRiding)
            {
                // 泡泡被打破或到寿命销毁时，Unity 重载的 == 会返回 true。
                // 这里必须显式判断，否则角色会一直卡在「控制被禁用」的状态里掉下去。
                if (m_RidingBubble == null)
                {
                    ExitBubble();
                    return;
                }

                if (Input.GetKeyDown(m_RideKey))
                {
                    ExitBubble();
                }

                return;
            }

            m_NearbyBubble = FindRideableBubble();

            if (m_NearbyBubble != null && Input.GetKeyDown(m_RideKey))
            {
                EnterBubble(m_NearbyBubble);
            }
        }

        private void LateUpdate()
        {
            // 载人时把玩家钉在泡泡里
            if (m_IsRiding && m_RidingBubble != null)
            {
                transform.position = m_RidingBubble.RideAnchorPosition;
            }
        }

        private void EnterBubble(Bubble bubble)
        {
            if (bubble == null || !bubble.EnterRide(transform))
            {
                return;
            }

            m_RidingBubble = bubble;
            m_NearbyBubble = null;
            m_IsRiding = true;

            SetPlayerColliders(false);

            // 关掉角色控制与碰撞体，免得和泡泡的刚体互相打架
            if (m_PlayerController != null)
            {
                m_PlayerController.enabled = false;
            }

            if (m_CharacterController != null)
            {
                m_CharacterController.enabled = false;
            }
        }

        private void ExitBubble()
        {
            if (m_RidingBubble != null)
            {
                m_RidingBubble.ExitRide();
            }

            m_RidingBubble = null;
            m_IsRiding = false;

            SetPlayerColliders(true);

            // 恢复角色控制，让重力把人带下去
            if (m_CharacterController != null)
            {
                m_CharacterController.enabled = true;
            }

            if (m_PlayerController != null)
            {
                m_PlayerController.enabled = true;
            }
        }

        /// <summary>
        /// 找范围内最近的那个可以钻进去的泡泡。
        /// </summary>
        private Bubble FindRideableBubble()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, m_SearchRadius);

            Bubble nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Bubble bubble = hits[i].GetComponentInParent<Bubble>();
                if (bubble == null || !bubble.CanRide)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, bubble.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = bubble;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 把 [F] 提示画在泡泡上方。用 IMGUI 是为了不依赖 Canvas 也不吃字体资源。
        /// </summary>
        private void OnGUI()
        {
            Bubble target = m_IsRiding ? m_RidingBubble : m_NearbyBubble;
            if (target == null)
            {
                return;
            }

            Camera camera = m_Camera != null ? m_Camera : (m_Camera = Camera.main);
            if (camera == null)
            {
                return;
            }

            Vector3 worldPoint = target.transform.position + Vector3.up * m_HintHeightOffset;
            Vector3 screenPoint = camera.WorldToScreenPoint(worldPoint);

            // 在摄像机背后就不画
            if (screenPoint.z <= 0f)
            {
                return;
            }

            if (m_HintStyle == null)
            {
                m_HintStyle = new GUIStyle(GUI.skin.label);
                m_HintStyle.fontSize = m_HintFontSize;
                m_HintStyle.fontStyle = FontStyle.Bold;
                m_HintStyle.alignment = TextAnchor.MiddleCenter;
                m_HintStyle.normal.textColor = Color.white;

                m_HintShadowStyle = new GUIStyle(m_HintStyle);
                m_HintShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }

            string text = m_IsRiding ? "[F] 跳出泡泡" : "[F] 进入泡泡";
            Vector2 size = m_HintStyle.CalcSize(new GUIContent(text));

            Rect rect = new Rect(
                screenPoint.x - size.x * 0.5f,
                Screen.height - screenPoint.y - size.y * 0.5f,
                size.x,
                size.y);

            // 先画一层黑色描边，保证在任何背景上都看得清
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, m_HintShadowStyle);
            GUI.Label(rect, text, m_HintStyle);
        }

        /// <summary>
        /// 统一开关玩家身上所有碰撞体。
        /// 进泡泡时必须全关，否则角色模型会顶到泡泡内壁，让泡泡乱转。
        /// </summary>
        private void SetPlayerColliders(bool enabled)
        {
            if (m_PlayerColliders == null)
            {
                return;
            }

            for (int i = 0; i < m_PlayerColliders.Length; i++)
            {
                if (m_PlayerColliders[i] != null)
                {
                    m_PlayerColliders[i].enabled = enabled;
                }
            }
        }
    }
}
