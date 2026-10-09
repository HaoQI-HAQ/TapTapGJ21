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

        [Tooltip("操控模式按键：相机跟到泡泡上，WASD 主动驾驶它飞")]
        [SerializeField] private KeyCode m_ControlKey = KeyCode.E;

        [Header("跳出冲刺")]
        [Tooltip("按 F 从浮粘泡泡里炸出来时，往角色正前方冲出去的速度（米/秒）")]
        [SerializeField] private float m_EjectForwardSpeed = 14f;

        [Tooltip("冲刺持续的时间（秒）。速度 × 时间 ≈ 冲出多远")]
        [SerializeField] private float m_EjectForwardDuration = 0.35f;

        [Header("UI")]
        [Tooltip("提示文字大小")]
        [SerializeField] private int m_HintFontSize = 22;

        [Tooltip("提示显示在泡泡上方多高处（米）")]
        [SerializeField] private float m_HintHeightOffset = 0.6f;

        [Header("乘坐位置")]
        [Tooltip("在泡泡里的位置微调（米）。0 = 正球心，正数往上，负数往下")]
        [SerializeField] private float m_RideHeightOffset = 0f;

        [Tooltip("跳出泡泡时是否把泡泡弄破（消失）")]
        [SerializeField] private bool m_PopBubbleOnExit = true;

        private Bubble m_NearbyBubble;      // 附近可进入的泡泡
        private Bubble m_RidingBubble;      // 正在乘坐的泡泡
        private bool m_IsRiding;            // 显式标记：不能只靠 m_RidingBubble 判空
        private bool m_IsControlling;       // 是否处于 E 键操控模式（可 WASD 驾驶）

        private PlayerController m_PlayerController;
        private CharacterController m_CharacterController;
        private Collider[] m_PlayerColliders;   // 玩家身上所有碰撞体，进出泡泡时统一开关
        private Camera m_Camera;
        private GUIStyle m_HintStyle;
        private GUIStyle m_HintShadowStyle;

        private const string kControlHintText = "[E] 操控泡泡";

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
            // ===== E 模式：远程操控中（人留在原地，相机在泡泡上）=====
            if (m_IsControlling)
            {
                // 泡泡自爆了或被打掉了
                if (m_RidingBubble == null)
                {
                    ExitControl(false);
                    return;
                }

                // 粘上了可粘地形：交还控制权，泡泡留在那儿
                if (m_RidingBubble.IsStuck)
                {
                    ExitControl(false);
                    return;
                }

                // 再按一次 E 退出操控
                if (Input.GetKeyDown(m_ControlKey))
                {
                    ExitControl(false);
                    return;
                }

                return;
            }

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
                    // 弹力泡泡：第一次按 F 是连人带泡一起弹射出去，人还坐在里面；
                    // 已经弹射过、或者是浮粘泡泡，按 F 就是正常跳出来
                    if (m_RidingBubble.IsBouncy && !m_RidingBubble.HasEjected)
                    {
                        m_RidingBubble.EjectRide(transform.forward);
                        return;
                    }

                    // 浮粘泡泡：按 F 直接炸开，人会随着冲击被抛出去。
                    // 先记下是不是浮粘泡泡 —— ExitBubble 之后引用就没了
                    bool ejectForward = m_RidingBubble != null && !m_RidingBubble.IsBouncy;

                    if (ejectForward)
                    {
                        m_RidingBubble.Explode();
                    }

                    ExitBubble();

                    // 往角色正前方冲一小段，而不是单纯自然落下。
                    // 速度和时长都是独立参数，在 Inspector 的「跳出冲刺」里调。
                    if (ejectForward && m_PlayerController != null)
                    {
                        m_PlayerController.LaunchForward(m_EjectForwardSpeed, m_EjectForwardDuration);
                    }
                }
                return;
            }

            m_NearbyBubble = FindRideableBubble();

            // E 键：进入操控模式，相机跟到泡泡上，WASD 主动驾驶
            if (m_NearbyBubble != null && m_NearbyBubble.CanControl
                && Input.GetKeyDown(m_ControlKey))
            {
                EnterControl(m_NearbyBubble);
                return;
            }

            // 装了炸弹的弹力泡泡：按 F 不是进去，而是把它弹射出去。
            // 方向取「玩家 → 泡泡」，也就是往玩家面朝的那一侧推出去。
            if (m_NearbyBubble != null && m_NearbyBubble.CanLaunchWithBomb
                && Input.GetKeyDown(m_RideKey))
            {
                Vector3 launchDirection = m_NearbyBubble.transform.position - transform.position;
                launchDirection.y = 0f;
                m_NearbyBubble.LaunchWithBomb(launchDirection);
                return;
            }

            if (m_NearbyBubble != null && Input.GetKeyDown(m_RideKey))
            {
                EnterBubble(m_NearbyBubble);
            }
        }

        private void LateUpdate()
        {
            // 只有 F 模式（人真的钻进泡泡）才把人钉过去。
            // E 模式人留在原地，相机自己会跟到泡泡上。
            if (!m_IsRiding || m_IsControlling || m_RidingBubble == null)
            {
                return;
            }

            // 直接钉在泡泡球心上。之前是「超出范围才夹回来」，
            // 结果进入瞬间人还停在原地，看起来偏在球的一侧。
            // 钉中心还有个好处：不会贴到内壁把泡泡带歪。
            transform.position = m_RidingBubble.transform.position
                + Vector3.up * m_RideHeightOffset;
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

            // 关掉碰撞体，免得角色顶到泡泡内壁把泡泡顶歪
            if (m_CharacterController != null)
            {
                m_CharacterController.enabled = false;
            }

            // 但 PlayerController 保持启用：视角、转身、移动都还要用。
            // 只是切成载具模式——不走 CharacterController、不受重力。
            //
            // 关键区别在这里：
            //   浮粘泡泡 = 被动上升，不给刚体，所以 WASD 推不动它
            //   弹力泡泡 = 要能贴着地面四处走，必须给刚体才推得动
            if (m_PlayerController != null)
            {
                m_PlayerController.IsInCarrier = true;
                m_PlayerController.CameraAnchor = null;

                m_PlayerController.CarrierBody = bubble.IsBouncy
                    ? bubble.GetComponent<Rigidbody>()
                    : null;
            }
        }

        private void ExitBubble()
        {
            if (m_RidingBubble != null)
            {
                Bubble bubbleToPop = m_RidingBubble;
                bubbleToPop.ExitRide();

                // 跳出时把泡泡弄破。走 Explode 而不是直接 Destroy，
                // 这样以后给它加破裂特效或冲击波也能直接接上。
                if (m_PopBubbleOnExit)
                {
                    bubbleToPop.Explode();
                }
            }

            m_RidingBubble = null;
            m_IsRiding = false;
            m_IsControlling = false;

            SetPlayerColliders(true);

            // 恢复角色控制，让重力把人带下去
            if (m_CharacterController != null)
            {
                m_CharacterController.enabled = true;
            }

            if (m_PlayerController != null)
            {
                m_PlayerController.IsInCarrier = false;
                m_PlayerController.CarrierBody = null;
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
                // 只要满足任意一种交互就算「可互动」：
                //   CanRide           → 能钻进去（F）
                //   CanControl        → 能远程操控（E）
                //   CanLaunchWithBomb → 能带弹弹射（F）
                // 装了炸弹的泡泡 CanRide 是 false，所以不能只看它，
                // 否则 NPC 都找不到它，F 和 E 就全哑了。
                bool interactive = bubble != null &&
                    (bubble.CanRide || bubble.CanControl || bubble.CanLaunchWithBomb);

                if (!interactive)
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

            // 带弹的弹力泡泡按 F 是弹射，提示要跟着变，否则玩家会以为能进去
            string textF;
            if (m_IsRiding)
            {
                textF = "[F] 跳出泡泡";
            }
            else if (m_NearbyBubble != null && m_NearbyBubble.CanLaunchWithBomb)
            {
                textF = "[F] 带弹弹射";
            }
            else
            {
                textF = "[F] 进入泡泡";
            }

            // 只有在「还没进去」的时候才显示 E 的提示，画在 F 的正下方
            // 弹力泡泡没有 E 功能，所以它的提示只显示 F 那一行
            bool showHintE = !m_IsRiding && target.CanControl;

            Vector2 sizeF = m_HintStyle.CalcSize(new GUIContent(textF));
            Vector2 sizeE = showHintE ? m_HintStyle.CalcSize(new GUIContent(kControlHintText)) : Vector2.zero;

            float width = Mathf.Max(sizeF.x, sizeE.x);
            float lineHeight = sizeF.y;

            // 第一行：F。居中到泡泡上方
            Rect rectF = new Rect(
                screenPoint.x - width * 0.5f,
                Screen.height - screenPoint.y - lineHeight,
                width,
                lineHeight);

            DrawHint(rectF, textF);

            // 第二行：E，紧跟在 F 的正下方
            if (showHintE)
            {
                Rect rectE = new Rect(rectF.x, rectF.y + lineHeight, width, lineHeight);
                DrawHint(rectE, kControlHintText);
            }
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
        /// <summary>
        /// E 键操控模式：进泡泡，并且把泡泡的刚体交给控制器，
        /// 于是 WASD 能推着它飞（浮力照旧，所以会自动上升）。
        /// 视角仍然是第三人称，跟着泡泡走。
        /// </summary>
        /// <summary>
        /// E 键远程操控：人留在原地不动，只把相机切到泡泡上，
        /// WASD 用来推泡泡飞（浮力照旧，所以会自动上升）。
        /// 结束时：再按一次 E、泡泡到时间自爆、或者粘上了可粘地形。
        /// </summary>
        private void EnterControl(Bubble bubble)
        {
            // 刻意不调 bubble.EnterRide()：
            // 那个方法会先查 CanRide，而装了炸弹的泡泡 CanRide 是 false。
            // 但 E 是远程操控、人根本没进去，不该受「能不能进人」的限制。
            if (bubble == null)
            {
                return;
            }

            m_RidingBubble = bubble;
            m_NearbyBubble = null;

            // 注意这里不设 m_IsRiding：人根本没上去，只是远程操控
            m_IsControlling = true;

            // 被操控了，体内的炸弹进入待爆状态：操控途中按右键就能引爆
            bubble.ArmAbsorbedBomb();

            // 人不动，所以碰撞体、CharacterController 都不用关

            if (m_PlayerController != null)
            {
                // 相机切到泡泡身上
                m_PlayerController.CameraAnchor = bubble.transform;

                // 载具模式：WASD 不再让自己走路，而是去推泡泡
                m_PlayerController.IsInCarrier = true;
                m_PlayerController.CarrierBody = bubble.GetComponent<Rigidbody>();
            }
        }

        /// <summary>
        /// 结束 E 键远程操控：相机回到自己身上，控制权交还。
        /// </summary>
        private void ExitControl(bool popBubble)
        {
            Bubble bubbleToRelease = m_RidingBubble;

            if (bubbleToRelease != null)
            {
                bubbleToRelease.ExitRide();

                if (popBubble)
                {
                    bubbleToRelease.Explode();
                }
            }

            m_RidingBubble = null;
            m_IsControlling = false;

            if (m_PlayerController != null)
            {
                m_PlayerController.CameraAnchor = null;
                m_PlayerController.IsInCarrier = false;
                m_PlayerController.CarrierBody = null;
            }
        }

        /// <summary>是否处于操控模式。</summary>
        public bool IsControlling
        {
            get { return m_IsControlling; }
        }
        /// <summary>画一行带黑色描边的提示文字。</summary>
        private void DrawHint(Rect rect, string text)
        {
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, m_HintShadowStyle);
            GUI.Label(rect, text, m_HintStyle);
        }
    }
}

