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

        private readonly RaycastHit[] m_HitBuffer = new RaycastHit[8];

        private CharacterController m_Controller;
        private float m_Yaw;                    // 摄像机水平角（独立于角色朝向）
        private float m_Pitch;                  // 摄像机俯仰角
        private float m_VerticalSpeed;          // 垂直速度（由重力累积而来）
        private float m_CurrentCameraDistance;  // 摄像机当前实际距离

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
        }

        private void OnEnable()
        {
            if (m_LockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            // 组件被禁用时必须解锁光标，否则编辑器里点不动界面
            SetCursorLocked(false);
        }

        private void Update()
        {
            UpdateLookInput();
            UpdateMove();

            // Esc 解锁光标，方便在编辑器里退出 Play
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetCursorLocked(false);
            }
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

            m_Yaw += Input.GetAxis("Mouse X") * m_MouseSensitivity;
            m_Pitch = Mathf.Clamp(m_Pitch - Input.GetAxis("Mouse Y") * m_MouseSensitivity, m_MinPitch, m_MaxPitch);
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
            m_CameraPivot.position = transform.position + Vector3.up * m_PivotHeight;
            m_CameraPivot.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }

        /// <summary>
        /// WASD 移动：方向以摄像机朝向为基准，角色平滑转向移动方向。
        /// </summary>
        private void UpdateMove()
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputY = Input.GetAxisRaw("Vertical");

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

            // 只有真的要移动时才转身，松手时保持当前朝向
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);

                // 帧率无关的平滑：t 只取决于经过的时间，不受帧数影响
                float t = 1f - Mathf.Exp(-m_RotationSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, t);
            }

            float speed = m_MoveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= m_SprintMultiplier;
            }

            // 重力与跳跃
            if (m_Controller.isGrounded)
            {
                // 落地时保持一个向下的微小速度，确保稳定贴地
                if (m_VerticalSpeed < 0f)
                {
                    m_VerticalSpeed = -2f;
                }

                if (Input.GetButtonDown("Jump"))
                {
                    // 由目标高度反推初速度：v = sqrt(2 * g * h)
                    m_VerticalSpeed = Mathf.Sqrt(m_JumpHeight * -2f * m_Gravity);
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
        }
    }
}
