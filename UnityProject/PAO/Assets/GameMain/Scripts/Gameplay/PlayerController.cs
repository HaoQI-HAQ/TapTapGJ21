using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 玩家控制器：第三人称 RPG 风格（原神 / 塞尔达那种）。
    ///
    /// 【视角设计】
    /// 1. 鼠标控制摄像机「环绕」角色旋转，角色本体不会跟着鼠标甩头
    /// 2. 按 WASD 时角色才平滑转向移动方向
    /// 3. 移动方向以摄像机朝向为基准——按 W 永远是朝屏幕深处走
    /// 4. 摄像机撞墙自动拉近，离开后平滑退回；拉近快、推远慢
    ///
    /// 【场景层级】
    ///   Player                 CharacterController + 本脚本
    ///   └── CameraPivot        空物体，Position (0, 1.5, 0)
    ///       └── Main Camera    Position (0, 0, -4.5)
    ///
    /// 脚本字段对应：Camera Pivot → CameraPivot ；Camera Transform → Main Camera
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("行走速度（米/秒）")]
        [SerializeField] private float m_MoveSpeed = 6f;

        [Tooltip("按住 Shift 时的速度倍率")]
        [SerializeField] private float m_SprintMultiplier = 1.7f;

        [Tooltip("跳跃高度（米）")]
        [SerializeField] private float m_JumpHeight = 1.2f;

        [Tooltip("重力加速度，取负值")]
        [SerializeField] private float m_Gravity = -20f;

        [Tooltip("角色转身的平滑速度，越大转得越快。8~20 比较自然")]
        [SerializeField] private float m_RotationSpeed = 12f;

        [Tooltip("在泡泡里驾驶时施加的推力（米/秒²）。越大越灵活，越小越飘")]
        [SerializeField] private float m_CarrierMoveForce = 6f;

        [Tooltip("被泡泡提着时的水平移动速度倍率。比走路慢，体现「微微操控方向」")]
        [SerializeField] private float m_LiftMoveMultiplier = 0.45f;

        [Header("视角")]
        [Tooltip("摄像机枢轴（空物体）。留空则自动取 MainCamera")]
        [SerializeField] private Transform m_CameraPivot;

        [Tooltip("枢轴相对角色的高度（米）。水平位置由脚本强制归零，避免转身时镜头漂移")]
        [SerializeField] private float m_PivotHeight = 1.5f;

        [Tooltip("鼠标灵敏度")]
        [SerializeField] private float m_MouseSensitivity = 2.5f;

        [Tooltip("俯仰角下限（往下看），RPG 视角一般不超过 -40")]
        [SerializeField] private float m_MinPitch = -35f;

        [Tooltip("俯仰角上限（往上看）")]
        [SerializeField] private float m_MaxPitch = 70f;

        [Header("第三人称摄像机")]
        [Tooltip("摄像机本体（枢轴的子物体）。留空表示第一人称模式，不做距离与碰撞处理")]
        [SerializeField] private Transform m_CameraTransform;

        [Tooltip("摄像机相对枢轴的目标距离（米）。RPG 一般 4~6")]
        [SerializeField] private float m_CameraDistance = 4.5f;

        [Tooltip("被墙挤压时允许的最近距离（米）")]
        [SerializeField] private float m_MinCameraDistance = 0.8f;

        [Tooltip("防穿墙探测球半径（米）。太大会在贴墙时误判，建议 0.15~0.25")]
        [SerializeField] private float m_CameraCollisionRadius = 0.2f;

        [Tooltip("参与遮挡检测的层，默认全部")]
        [SerializeField] private LayerMask m_CameraCollisionMask = ~0;

        [Tooltip("被遮挡时拉近的速度，要快，慢了会穿模")]
        [SerializeField] private float m_CameraPullInSpeed = 25f;

        [Tooltip("脱离遮挡后推远的速度，要慢，快了画面会突兀")]
        [SerializeField] private float m_CameraReturnSpeed = 6f;

        [Header("光标")]
        [Tooltip("进入运行时锁定并隐藏鼠标光标")]
        [SerializeField] private bool m_LockCursorOnStart = true;

        [Tooltip("光标意外脱离锁定（编辑器切窗口、失焦、多显示器换焦点等）后自动重新锁定，\n" +
                 "延迟从 0.35 秒起逐次加倍。关掉则只保留手动点击重新锁定")]
        [SerializeField] private bool m_AutoRelockCursor = true;

        [Tooltip("鼠标静止时点击画面即可重新锁定光标（不需要按 Esc 或打开暂停面板）")]
        [SerializeField] private bool m_ClickToRelock = true;

        private readonly RaycastHit[] m_HitBuffer = new RaycastHit[8];

        private CharacterController m_Controller;
        private float m_Yaw;                    // 摄像机水平角（独立于角色朝向）
        private float m_Pitch;                  // 摄像机俯仰角
        private float m_VerticalSpeed;          // 垂直速度（由重力累积而来）
        private bool m_WasGrounded;
        private float m_StepTimer;
        private float m_CurrentCameraDistance;  // 摄像机当前实际距离

        /// <summary>
        /// 是否身处载具内（比如钻进了泡泡）。
        /// 开启后不施加重力、不走 CharacterController，改为直接小幅位移，
        /// 位置由载具那边每帧夹在活动范围内。视角与转身不受影响。
        /// </summary>
        public bool IsInCarrier { get; set; }

        /// <summary>
        /// 当前乘坐的载具刚体（比如泡泡）。设置后 WASD 会变成对它施加推力，
        /// 也就是「带着载具走」，而不是自己在里头走。
        /// </summary>
        public Rigidbody CarrierBody { get; set; }

        /// <summary>
        /// 相机跟随的锚点。为空时跟随自己（正常情况）；
        /// 设为别的物体时，相机就切到那个物体上，而人留在原地不动。
        /// E 键远程操控泡泡就是靠它实现的。
        /// </summary>
        public Transform CameraAnchor { get; set; }

        /// <summary>
        /// 是否正被泡泡提着上升。为 true 时不走重力，
        /// 垂直速度由 LiftSpeed 决定（由泡泡每帧写入）。
        /// </summary>
        public bool IsLifted { get; set; }

        /// <summary>被提着时的垂直速度（米/秒），由泡泡写入。</summary>
        public float LiftSpeed { get; set; }

        public float LookSensitivityMultiplier { get; set; } = 1f;
        public bool InvertVerticalLook { get; set; }

        /// <summary>
        /// 锁定的光标是否应该在「回到游戏」时自动重新锁上。
        /// 由 UIRuntime 统一管理：任何界面打开时设为 false（光标交给玩家点按钮），
        /// 回到游戏时恢复为 true；玩家在菜单里选了「解锁鼠标」则是 false 且不再自动接管。
        /// </summary>
        public bool ReleaseCursorForMenus { get; set; }

        /// <summary>
        /// 脚本主动要求的锁定状态。Esc / 菜单里解锁后为 false，
        /// 之后只有重新锁定（点击画面、菜单里打开视角锁定）才会回到 true。
        /// </summary>
        public bool CursorWanted { get { return m_CursorLockDesired && !ReleaseCursorForMenus; } }

        private bool m_CursorLockDesired;       // Esc / 菜单解锁后会变 false
        private int m_CursorRelockAttempts;
        private float m_NextCursorRelockTime;

        /// <summary>
        /// 重新锁定光标并隐藏鼠标。暂停菜单里的「锁定鼠标」按钮、点击画面都会走到这里。
        /// </summary>
        public void LockCursor()
        {
            m_CursorLockDesired = true;
            m_CursorRelockAttempts = 0;
            m_NextCursorRelockTime = 0f;
            // 有界面开着的时候（暂停、设置……）别抢光标，交给玩家点按钮；
            // 界面关掉的下一帧 ReleaseCursorForMenus 变回 false，UpdateCursorLock 会自动补锁。
            if (!ReleaseCursorForMenus) SetCursorLocked(true);
        }

        /// <summary>解锁光标（Esc 或菜单里选「解锁鼠标」）。</summary>
        public void UnlockCursor()
        {
            m_CursorLockDesired = false;
            SetCursorLocked(false);
        }

        /// <summary>
        /// 当前有没有移动输入（WASD 是否按着）。
        /// 弹力泡泡靠它判断玩家「松手了」，好把攒下的弹力放出去。
        /// </summary>
        public bool HasMoveInput { get; private set; }

        // ---------- 跳出冲刺 ----------
        private float m_DashSpeed;          // 冲刺速度（米/秒）
        private Vector3 m_DashDirection;    // 冲刺方向（世界空间）
        private float m_DashTimeLeft;       // 冲刺剩余时间（秒）

        /// <summary>
        /// 往角色正前方冲一小段。从浮粘泡泡里炸出来时用，
        /// 比单纯自然落下更有「被冲击抛出去」的感觉。
        /// </summary>
        /// <summary>往角色正前方冲一小段。从浮粘泡泡里炸出来时用。</summary>
        public void LaunchForward(float speed, float duration)
        {
            Launch(transform.forward, speed, duration);
        }

        /// <summary>
        /// 往指定方向冲一段。弹力泡泡贴墙弹射时，人会跟着泡泡一起往后飞。
        /// 方向传世界空间向量，内部会归一化。
        /// </summary>
        public void Launch(Vector3 direction, float speed, float duration)
        {
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            m_DashDirection = direction.normalized;
            m_DashSpeed = speed;
            m_DashTimeLeft = Mathf.Max(0f, duration);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool controllerEnabled = m_Controller != null && m_Controller.enabled;
            if (m_Controller != null) m_Controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            m_VerticalSpeed = 0f;
            m_DashTimeLeft = 0f;
            m_DashSpeed = 0f;
            m_Yaw = rotation.eulerAngles.y;
            if (m_Controller != null) m_Controller.enabled = controllerEnabled;
        }

        private void Awake()
        {
            m_Controller = GetComponent<CharacterController>();

            if (m_CameraPivot == null && Camera.main != null)
            {
                m_CameraPivot = Camera.main.transform;
            }

            // 用枢轴的初始朝向作为起点，避免开局视角跳变
            if (m_CameraPivot != null)
            {
                Vector3 initialEuler = m_CameraPivot.eulerAngles;
                m_Yaw = initialEuler.y;
                m_Pitch = NormalizePitch(initialEuler.x);
            }

            m_CurrentCameraDistance = m_CameraDistance;
            m_CursorLockDesired = m_LockCursorOnStart;
        }

        private void OnEnable()
        {
            // 暂停面板、设置之类的界面会把本组件临时禁用再启用。
            // 重新启用时只在「玩家本来就想要锁定、且没有界面开着」时补锁，
            // 否则从菜单里解锁过的光标一回到游戏又被藏起来了。
            SetCursorLocked(m_LockCursorOnStart && m_CursorLockDesired && !ReleaseCursorForMenus);
            m_CursorRelockAttempts = 0;
            m_NextCursorRelockTime = 0f;
        }

        private void OnDisable()
        {
            // 组件被禁用时必须解锁光标，否则编辑器里点不动界面
            SetCursorLocked(false);
        }

        private void OnApplicationFocus(bool focused)
        {
            // 切回窗口时 Unity 不会自动恢复强制锁定（失焦那次解锁往往就发生在切出去的时候），
            // 这里立刻补锁一次，避免「切回来鼠标还在外面漂」。
            if (focused && CursorWanted)
            {
                m_CursorRelockAttempts = 0;
                m_NextCursorRelockTime = 0f;
                SetCursorLocked(true);
            }
        }

        private void Update()
        {
            if (PAO.UI.V2.UIInput.Blocked) return;
            UpdateCursorLock();
            UpdateLookInput();
            UpdateMove();
        }

        /// <summary>
        /// 让光标始终待在锁定状态。
        ///
        /// 【为什么需要这段】
        /// 原先只在 OnEnable 里锁一次、Esc 再解锁一次，之后就不管了。
        /// 可 Windows 编辑器里只要有一次外部原因把锁解除 —— 切换窗口 / Alt+Tab、
        /// Game 视图失焦、分辨率与全屏模式切换（Screen.SetResolution）、
        /// 拖到第二块屏幕 —— 光标就会留在外面自由移动：
        /// 视角仍然能转，但鼠标会漂到屏幕边缘被裁掉，甚至点回编辑器，
        /// 而且没有任何代码会把它锁回来，看起来就像「鼠标脱离了游戏」。
        ///
        /// 【现在的策略】
        ///   1. 有界面（暂停、设置、结算……）时由 UIRuntime 负责解锁，这里不抢；
        ///   2. 回到游戏后如果发现锁没了，等一小会儿就自动补锁，失败则延迟加倍重试
        ///      （刚切回窗口的那一帧系统可能还没准备好，立刻锁会静默失败）；
        ///   3. 鼠标静止时点一下画面也能立刻重新锁定，作为兜底。
        /// UIRuntime 在打开任意界面时会把 ReleaseCursorForMenus 置为 true，
        /// 那种情况下这里一律不抢光标。
        /// </summary>
        private void UpdateCursorLock()
        {
            if (!CursorWanted || !Application.isFocused)
            {
                return;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                m_CursorRelockAttempts = 0;
                return;
            }

            if (ReleaseCursorForMenus)
            {
                return;
            }

            // 鼠标已经回到 Game 视图里点了一下：立刻锁，不用等自动补锁的延迟
            bool clicked = m_ClickToRelock && Input.GetMouseButtonDown(0) && IsPointerInsideGameView();
            if (!m_AutoRelockCursor && !clicked)
            {
                return;
            }

            if (!clicked && Time.unscaledTime < m_NextCursorRelockTime)
            {
                return;
            }

            if (clicked) m_CursorRelockAttempts = 0;

            SetCursorLocked(true);
            m_CursorRelockAttempts++;
            m_NextCursorRelockTime = Time.unscaledTime +
                Mathf.Min(4f, 0.35f * Mathf.Pow(1.7f, m_CursorRelockAttempts - 1));
        }

        /// <summary>
        /// 鼠标是否在 Game 视图内。编辑器里把视角锁定关掉后，
        /// 玩家要能正常点编辑器面板，所以那种情况下不做「点一下重新锁定」。
        /// </summary>
        private static bool IsPointerInsideGameView()
        {
#if UNITY_EDITOR
            // GameView 是 internal，不能直接做类型判断，只能比类型名。
            UnityEditor.EditorWindow window = UnityEditor.EditorWindow.mouseOverWindow;
            return window != null && window.GetType().FullName == "UnityEditor.GameView";
#else
            return true;
#endif
        }

        /// <summary>
        /// 放在 LateUpdate：等角色本帧移动与转身都算完了，
        /// 再摆摄像机的朝向和位置，否则跟着角色转会出现一帧抖动。
        /// </summary>
        private void LateUpdate()
        {
            ApplyCameraTransform();
            UpdateCameraDistance();
        }

        /// <summary>
        /// 采样鼠标输入，更新摄像机的 yaw / pitch。
        /// 注意：这里只改摄像机，不碰角色朝向——这是 RPG 视角与 FPS 的关键区别。
        /// </summary>
        private void UpdateLookInput()
        {
            if (m_CameraPivot == null)
            {
                return;
            }

            // 光标没锁定时不接受视角输入，避免在编辑器里误操作
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            float lookX = Input.GetAxis("Mouse X"), lookY = Input.GetAxis("Mouse Y");
            Vector2 stick = Vector2.zero;
            if (PAO.UI.V2.UIInput.UsesV2)
            {
                stick = PAO.UI.V2.UIInput.Look() * (120f * Time.unscaledDeltaTime);
            }
            m_Yaw += lookX * m_MouseSensitivity * LookSensitivityMultiplier + stick.x;
            m_Pitch = Mathf.Clamp(m_Pitch - lookY * m_MouseSensitivity *
                LookSensitivityMultiplier * (InvertVerticalLook ? -1f : 1f) - stick.y, m_MinPitch, m_MaxPitch);
        }

        /// <summary>
        /// 把 yaw / pitch 应用到枢轴。
        /// 用世界旋转赋值，这样即使枢轴挂在角色下面，也不会被角色转身带着甩。
        /// </summary>
        private void ApplyCameraTransform()
        {
            if (m_CameraPivot == null)
            {
                return;
            }

            // 枢轴只跟随角色的「位置」和固定高度，水平位置强制归零。
            // 若放任它作为角色的子物体继承旋转，角色一转身枢轴就绕圈，
            // 摄像机会跟着漂出去——表现为按 WASD 时镜头莫名偏移或拉远拉近。
            // 有锚点就跟随锚点（比如被操控的泡泡），否则跟随自己
            Vector3 anchorPosition = CameraAnchor != null ? CameraAnchor.position : transform.position;
            m_CameraPivot.position = anchorPosition + Vector3.up * m_PivotHeight;
            m_CameraPivot.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }

        /// <summary>
        /// WASD 移动：方向以摄像机朝向为基准，角色平滑转向移动方向。
        /// </summary>
        private void UpdateMove()
        {

            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");
            if (PAO.UI.V2.UIInput.UsesV2)
            {
                Vector2 move = PAO.UI.V2.UIInput.Move(); inputX = move.x; inputY = move.y;
            }

            // 把摄像机的朝向压平到水平面，作为移动参考系
            Vector3 referenceForward;
            Vector3 referenceRight;

            if (m_CameraPivot != null)
            {
                referenceForward = m_CameraPivot.forward;
                referenceRight = m_CameraPivot.right;
                referenceForward.y = 0f;
                referenceRight.y = 0f;
                referenceForward.Normalize();
                referenceRight.Normalize();
            }
            else
            {
                referenceForward = transform.forward;
                referenceRight = transform.right;
            }

            Vector3 moveDirection = referenceForward * inputY + referenceRight * inputX;

            // 归一化，避免斜向移动比直线更快
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            // 记下这一帧有没有移动输入（转身和弹力泡泡都要用）
            HasMoveInput = moveDirection.sqrMagnitude > 0.0001f;

            // 只有真的要移动时才转身，松手时保持当前朝向
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);

                // 帧率无关的平滑：t 只取决于经过的时间，不受帧数影响
                float t = 1f - Mathf.Exp(-m_RotationSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
            }

            // 载具内：WASD 用来驾驶载具（泡泡），而不是自己走。
            // 只推水平方向，垂直交给泡泡自身的浮力，这样抬头低头不会乱飞。
            //
            // 【放在 CharacterController 拦截之前】
            // 钻进泡泡时 CharacterController 是被禁用的，而这里用的是
            // CarrierBody.AddForce，压根不需要它。放后面就会被那道拦截挡掉，
            // 表现就是「进了弹力泡泡却推不动」。
            if (IsInCarrier)
            {
                if (CarrierBody != null)
                {
                    Vector3 horizontal = new Vector3(moveDirection.x, 0f, moveDirection.z);
                    CarrierBody.AddForce(horizontal * m_CarrierMoveForce, ForceMode.Acceleration);
                }

                return;
            }

            // 到这里为止「读输入 + 转身」都已经做完了 —— 转身不该受控制器状态影响，
            // 否则钻进泡泡（CharacterController 被禁用）时人就转不了身了。
            // 再往下才是真正的「移动」，控制器不可用时才该停下来。
            if (m_Controller == null
                || !m_Controller.enabled
                || !m_Controller.gameObject.activeInHierarchy)
            {
                return;
            }

            // 跳出冲刺：这段时间每帧额外往正前方推一点。
            // 用 Move 直接叠加，所以冲刺和正常移动、重力是并行生效的。
            if (m_DashTimeLeft > 0f)
            {
                m_DashTimeLeft -= Time.deltaTime;
                // 用记录下来的方向，而不是永远朝前 ——
                // 弹力泡泡把人往后弹时，方向是往后的
                m_Controller.Move(m_DashDirection * (m_DashSpeed * Time.deltaTime));
            }

            // 被滞留的浮粘泡泡提着：水平还能微微操控方向，
            // 垂直完全交给泡泡 —— 它升多快人就升多快。
            if (IsLifted)
            {
                Vector3 liftVelocity = moveDirection * (m_MoveSpeed * m_LiftMoveMultiplier)
                    + Vector3.up * LiftSpeed;

                m_Controller.Move(liftVelocity * Time.deltaTime);
                return;
            }


            float speed = m_MoveSpeed;
            if (PAO.UI.V2.UIInput.UsesV2 ? PAO.UI.V2.UIInput.Held(PAO.UI.V2.UIInput.Sprint) :
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= m_SprintMultiplier;
            }

            // 重力与跳跃
            bool grounded = m_Controller.isGrounded;
            if (grounded && !m_WasGrounded && m_VerticalSpeed < -3f)
                PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.PlayerLand, transform.position,
                    Mathf.InverseLerp(3f, 14f, -m_VerticalSpeed));
            m_WasGrounded = grounded;
            if (grounded && moveDirection.sqrMagnitude > .08f)
            {
                m_StepTimer -= Time.deltaTime * (speed / Mathf.Max(.1f, m_MoveSpeed));
                if (m_StepTimer <= 0f)
                {
                    m_StepTimer = .43f;
                    PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.PlayerStep, transform.position, .45f);
                }
            }
            else m_StepTimer = 0f;
            if (grounded)
            {
                // 落地时保持一个向下的微小速度，确保稳定贴地
                if (m_VerticalSpeed < 0f)
                {
                    m_VerticalSpeed = -2f;
                }

                if (PAO.UI.V2.UIInput.UsesV2 ? PAO.UI.V2.UIInput.Down(PAO.UI.V2.UIInput.Jump) : Input.GetButtonDown("Jump"))
                {
                    // 由目标高度反推初速度：v = sqrt(2 * g * h)
                    m_VerticalSpeed = Mathf.Sqrt(m_JumpHeight * -2f * m_Gravity);
                    m_WasGrounded = false;
                    PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.PlayerJump, transform.position);
                }
            }

            m_VerticalSpeed += m_Gravity * Time.deltaTime;

            Vector3 velocity = moveDirection * speed + Vector3.up * m_VerticalSpeed;
            m_Controller.Move(velocity * Time.deltaTime);
        }

        /// <summary>
        /// 第三人称摄像机防穿墙：从枢轴向后探测，撞到东西就把摄像机拉近。
        /// 第一人称模式（未指定 Camera Transform）时直接跳过。
        /// </summary>
        private void UpdateCameraDistance()
        {
            if (m_CameraTransform == null || m_CameraPivot == null)
            {
                return;
            }

            Vector3 origin = m_CameraPivot.position;
            Vector3 direction = -m_CameraPivot.forward;   // 摄像机在枢轴正后方
            float targetDistance = ResolveCameraDistance(origin, direction, m_CameraDistance);

            targetDistance = Mathf.Max(targetDistance, m_MinCameraDistance);

            // 拉近要快（慢一帧就穿模），推远要慢（否则画面突兀弹开）
            float speed = targetDistance < m_CurrentCameraDistance ? m_CameraPullInSpeed : m_CameraReturnSpeed;
            m_CurrentCameraDistance = Mathf.MoveTowards(m_CurrentCameraDistance, targetDistance, speed * Time.deltaTime);

            // 只改 Z，保留你在 Inspector 里给摄像机设的 X/Y 偏移
            Vector3 cameraLocalPosition = m_CameraTransform.localPosition;
            cameraLocalPosition.z = -m_CurrentCameraDistance;
            m_CameraTransform.localPosition = cameraLocalPosition;
        }

        /// <summary>
        /// 用球形投射找出枢轴到摄像机之间最近的遮挡物，返回可用距离。
        /// </summary>
        private float ResolveCameraDistance(Vector3 origin, Vector3 direction, float desiredDistance)
        {
            // 用 NonAlloc 版本避免每帧产生垃圾
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                m_CameraCollisionRadius,
                direction,
                m_HitBuffer,
                desiredDistance,
                m_CameraCollisionMask,
                QueryTriggerInteraction.Ignore);

            float nearest = desiredDistance;

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = m_HitBuffer[i].collider;
                if (hitCollider == null)
                {
                    continue;
                }

                // 起点就与碰撞体重叠时，Unity 会给出 distance 为 0 的无效命中。
                // 贴墙站立时枢轴离墙很近就会触发，若不排除，摄像机会被莫名吸到最近距离
                if (m_HitBuffer[i].distance <= 0f)
                {
                    continue;
                }

                // 枢轴本来就在角色体内，探测必然打到自己的碰撞体，要跳过
                Transform hitTransform = hitCollider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform))
                {
                    continue;
                }

                if (m_HitBuffer[i].distance < nearest)
                {
                    nearest = m_HitBuffer[i].distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 把 eulerAngles 里的俯仰角（0~360）换算成 -180~180，方便与 pitch 上下限比较。
        /// </summary>
        private static float NormalizePitch(float angle)
        {
            if (angle > 180f)
            {
                angle -= 360f;
            }

            return angle;
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (locked) m_CursorLockDesired = true;
        }
    }
}
