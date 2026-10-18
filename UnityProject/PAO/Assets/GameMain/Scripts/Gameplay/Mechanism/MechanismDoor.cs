using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace PAO
{
    /// <summary>
    /// 机关门：收到开启指令后，先等 Delay 秒，再用 Duration 秒旋转 / 平移 / 两者一起，
    /// 打开时还可以顺手启动门边的镜头。
    ///
    /// 【推荐的层级】
    ///   Door（挂本脚本）
    ///   ├── Frame           门框（不动）
    ///   └── Hinge           空物体，放在门轴位置
    ///       └── Leaf        门板（会动，赋给 Moving Part）
    ///
    /// 【两种开法】
    ///   Rotate：绕 Hinge 的本地轴转 Open Angle 度。Axis 一般填 (0,1,0)。
    ///   Slide：沿 Slide Offset 平移。Offset 按门板父级的坐标轴算，
    ///          比如 (0,0,1.5) 就是沿父级 Z 轴推出 1.5 米。
    ///   Rotate And Slide：先转，再叠加平移；做“旋转门 + 前移一点”挺方便。
    ///
    /// 【联动】
    ///   一般由 <see cref="LeverInteractable"/> 的 Target Door 调 Open()。
    ///   也可以从按钮脚本、Timeline、UnityEvent 直接调 Open / Close / Toggle。
    ///
    /// 【暂停】
    ///   延迟和运动都用 Time.deltaTime，暂停菜单把 timeScale 设为 0 时会跟着停。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/机关门（Mechanism Door）")]
    public sealed class MechanismDoor : MonoBehaviour
    {
        public enum MotionMode
        {
            Rotate = 0,
            Slide = 1,
            RotateAndSlide = 2
        }

        [Header("门体")]
        [Tooltip("真正会动的门板。留空 = 转 / 移本物体")]
        [SerializeField] private Transform m_MovingPart;

        [Tooltip("旋转时绕的门轴。留空 = 绕门板自己的原点转。建议放一个空物体在合页位置")]
        [SerializeField] private Transform m_Hinge;

        [Header("开启方式")]
        [Tooltip("旋转 / 平移 / 旋转加平移")]
        [SerializeField] private MotionMode m_Motion = MotionMode.Rotate;

        [Tooltip("旋转轴（Hinge 的本地坐标轴）。普通门填 (0,1,0)")]
        [SerializeField] private Vector3 m_RotateAxis = Vector3.up;

        [Tooltip("开门转多少度。正负决定往哪边开")]
        [SerializeField] private float m_OpenAngle = 95f;

        [Tooltip("平移位移。按门板父级的坐标轴算，不是世界轴")]
        [SerializeField] private Vector3 m_SlideOffset = new Vector3(1.5f, 0f, 0f);

        [Header("时间")]
        [Tooltip("收到指令后先等多久才开始动（秒）。默认 0.5")]
        [SerializeField] private float m_Delay = 0.5f;

        [Tooltip("从关到开需要多久（秒）")]
        [SerializeField] private float m_Duration = 1.2f;

        [Tooltip("缓动曲线。默认两头慢中间快；想要机械感可以换成线性")]
        [SerializeField] private AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("一开始就是打开状态（不播动画、不触发镜头）")]
        [SerializeField] private bool m_StartOpen = false;

        [Header("镜头")]
        [Tooltip("开门时启动的门边镜头。留空 = 不切镜头")]
        [SerializeField] private MechanismCameraShot m_OpenCameraShot;

        [Header("事件")]
        [Tooltip("收到开门指令、开始等待 Delay 时")]
        [SerializeField] private UnityEvent m_OnOpenStarted;

        [Tooltip("门完全打开后")]
        [SerializeField] private UnityEvent m_OnOpened;

        [Tooltip("收到关门指令、开始等待 Delay 时")]
        [SerializeField] private UnityEvent m_OnCloseStarted;

        [Tooltip("门完全关上后")]
        [SerializeField] private UnityEvent m_OnClosed;

        private Vector3 m_ClosedPosition;
        private Quaternion m_ClosedRotation;
        private Vector3 m_HingePosition;
        private Vector3 m_WorldAxis;
        private Vector3 m_SlideWorld;

        private float m_Progress;
        private float m_FromProgress;
        private float m_TargetProgress;
        private Coroutine m_Routine;
        private bool m_IsMoving;
        private bool m_Initialised;

        /// <summary>目标状态是不是打开（延迟期间也算）。</summary>
        public bool IsOpen { get { return m_TargetProgress >= 0.999f; } }

        /// <summary>目标状态是不是关闭（延迟期间也算）。</summary>
        public bool IsClosed { get { return m_TargetProgress <= 0.001f; } }

        /// <summary>门正在延迟或运动。</summary>
        public bool IsMoving { get { return m_IsMoving; } }

        /// <summary>门已经完全打开。</summary>
        public bool IsFullyOpen { get { return m_Progress >= 0.999f; } }

        /// <summary>0 = 完全关，1 = 完全开。</summary>
        public float Progress { get { return m_Progress; } }

        private bool HasRotation
        {
            get { return m_Motion == MotionMode.Rotate || m_Motion == MotionMode.RotateAndSlide; }
        }

        private bool HasSlide
        {
            get { return m_Motion == MotionMode.Slide || m_Motion == MotionMode.RotateAndSlide; }
        }

        private void Awake()
        {
            if (m_MovingPart == null)
            {
                m_MovingPart = transform;
            }

            // 本脚本用 Transform 摆门，刚体必须交给脚本控制，否则物理会把门拽回去。
            Rigidbody body = m_MovingPart.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.isKinematic = true;
            }
        }

        private void Start()
        {
            Initialise();
        }

        private void OnDisable()
        {
            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_IsMoving = false;
        }

        /// <summary>开门。重复调用不会重播；门已经在开的过程中也会忽略。</summary>
        public void Open()
        {
            EnsureInitialised();

            if (m_TargetProgress >= 0.999f)
            {
                return;
            }

            Begin(1f, true);
        }

        /// <summary>关门。</summary>
        public void Close()
        {
            EnsureInitialised();

            if (m_TargetProgress <= 0.001f)
            {
                return;
            }

            Begin(0f, false);
        }

        /// <summary>开 → 关 → 开，给“再按一次”的拉杆用。</summary>
        public void Toggle()
        {
            if (IsOpen || (m_IsMoving && m_TargetProgress > 0.5f))
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void Begin(float target, bool opening)
        {
            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_FromProgress = m_Progress;
            m_TargetProgress = target;
            m_IsMoving = true;

            if (opening)
            {
                if (m_OpenCameraShot != null)
                {
                    m_OpenCameraShot.Play();
                }

                m_OnOpenStarted?.Invoke();
            }
            else
            {
                m_OnCloseStarted?.Invoke();
            }

            if (m_Delay <= 0.0001f && m_Duration <= 0.0001f)
            {
                ApplyProgress(target);
                FinishTransition();
                return;
            }

            m_Routine = StartCoroutine(Transition());
        }

        private IEnumerator Transition()
        {
            if (m_Delay > 0.0001f)
            {
                yield return new WaitForSeconds(m_Delay);
            }

            float duration = Mathf.Max(0.0001f, m_Duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (m_Ease != null && m_Ease.length > 0)
                {
                    t = m_Ease.Evaluate(t);
                }

                ApplyProgress(Mathf.LerpUnclamped(m_FromProgress, m_TargetProgress, t));
                yield return null;
            }

            ApplyProgress(m_TargetProgress);
            FinishTransition();
        }

        private void FinishTransition()
        {
            m_Routine = null;
            m_IsMoving = false;

            if (m_TargetProgress >= 0.5f)
            {
                m_OnOpened?.Invoke();
            }
            else
            {
                m_OnClosed?.Invoke();
            }
        }

        private void Initialise()
        {
            if (m_Initialised)
            {
                return;
            }

            if (m_MovingPart == null)
            {
                m_MovingPart = transform;
            }

            m_ClosedPosition = m_MovingPart.position;
            m_ClosedRotation = m_MovingPart.rotation;
            m_HingePosition = m_Hinge != null ? m_Hinge.position : m_ClosedPosition;

            Vector3 axis = m_RotateAxis.sqrMagnitude > 0.0001f ? m_RotateAxis.normalized : Vector3.up;
            m_WorldAxis = m_Hinge != null ? m_Hinge.TransformDirection(axis) : m_ClosedRotation * axis;
            m_WorldAxis.Normalize();

            Transform parent = m_MovingPart.parent;
            m_SlideWorld = (parent != null ? parent.rotation : Quaternion.identity) * m_SlideOffset;

            m_Progress = m_StartOpen ? 1f : 0f;
            m_TargetProgress = m_Progress;
            m_Initialised = true;
            ApplyProgress(m_Progress);
        }

        private void EnsureInitialised()
        {
            if (!m_Initialised)
            {
                Initialise();
            }
        }

        /// <summary>
        /// 按进度摆门。旋转时位置也要沿圆弧走，
        /// 所以不能用“关门位置 → 开门位置”的直线插值，否则门板会从门框里穿过去。
        /// </summary>
        private void ApplyProgress(float progress)
        {
            m_Progress = progress;

            if (m_MovingPart == null)
            {
                return;
            }

            Vector3 position = m_ClosedPosition;
            Quaternion rotation = m_ClosedRotation;

            if (HasRotation)
            {
                Quaternion delta = Quaternion.AngleAxis(m_OpenAngle * progress, m_WorldAxis);
                rotation = delta * m_ClosedRotation;

                if (m_Hinge != null)
                {
                    position = m_HingePosition + delta * (m_ClosedPosition - m_HingePosition);
                }
            }

            if (HasSlide)
            {
                position += m_SlideWorld * progress;
            }

            m_MovingPart.SetPositionAndRotation(position, rotation);
        }

        /// <summary>直接摆到开 / 关，不播动画、不触发镜头和事件。给读档用。</summary>
        public void SetOpenInstant(bool open)
        {
            EnsureInitialised();

            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_IsMoving = false;
            m_TargetProgress = open ? 1f : 0f;
            ApplyProgress(m_TargetProgress);
        }

        /// <summary>直接打开，不等待。给“场景一开始门就是开的”之类的情况用。</summary>
        public void OpenInstant()
        {
            SetOpenInstant(true);
        }

        private void OnValidate()
        {
            m_Delay = Mathf.Max(0f, m_Delay);
            m_Duration = Mathf.Max(0f, m_Duration);

            if (m_Ease == null || m_Ease.length == 0)
            {
                m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform moving = m_MovingPart != null ? m_MovingPart : transform;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.85f);
            Gizmos.DrawWireCube(moving.position, Vector3.one * 0.25f);

            if (m_Hinge != null)
            {
                Vector3 axis = m_RotateAxis.sqrMagnitude > 0.0001f ? m_RotateAxis.normalized : Vector3.up;
                Vector3 worldAxis = (m_Hinge.rotation * axis).normalized;
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
                Gizmos.DrawLine(m_Hinge.position - worldAxis * 0.7f, m_Hinge.position + worldAxis * 0.7f);
                Gizmos.DrawWireSphere(m_Hinge.position, 0.08f);
            }

            if (HasSlide)
            {
                Transform parent = moving.parent;
                Vector3 offset = (parent != null ? parent.rotation : Quaternion.identity) * m_SlideOffset;
                Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
                Gizmos.DrawLine(moving.position, moving.position + offset);
            }
        }
    }
}
