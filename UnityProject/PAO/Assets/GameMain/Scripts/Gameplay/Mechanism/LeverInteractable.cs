using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace PAO
{
    /// <summary>
    /// 推拉杆交互：玩家靠近 → 出现白色 E 提示 → 按 E → 手柄绕转轴转到目标角度 → 触发门。
    ///
    /// 【推荐的层级】
    ///   Lever（挂本脚本）
    ///   ├── Base              固定底座（不转）
    ///   ├── Hinge             空物体，放在齿轮 / 转轴的正中心
    ///   │   └── Handle        会转的手柄模型（也可以放到 Hinge 外面，只要赋值对）
    ///   └── PromptAnchor      空物体，决定 E 提示悬在哪儿
    ///
    /// 【为什么用 Hinge 而不是直接转手柄】
    ///   模型原点常常不在转轴上。只转手柄会绕着自己的中心打转，
    ///   这里用 Hinge 的位置作为圆心，手柄离圆心多远就转多大的圆弧。
    ///
    /// 【参数怎么调】
    ///   Axis 是 Hinge 的本地坐标轴。拉杆在 XY 平面里摆动通常填 (0,0,1)；
    ///   Angle 的正负决定往哪边掰。先在 Scene 里点中本物体看青色轴线，
    ///   转一下 Angle 试运行，不对就改符号。
    ///
    /// 【联动门】
    ///   把门拖到 Target Door，拉到底会自动调用 Door.Open()；
    ///   勾了 Toggle Back 时，再按 E 推回去会自动调用 Door.Close()。
    ///   也可以不用 Target Door，改用 On Pulled / On Returned 事件接别的逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/拉杆交互（Lever Interactable）")]
    public sealed class LeverInteractable : MonoBehaviour
    {
        [Header("交互")]
        [Tooltip("玩家离拉杆多近会出现提示（米）")]
        [SerializeField] private float m_InteractDistance = 2.8f;

        [Tooltip("提示悬在哪个位置。留空 = 本物体位置 + Prompt Offset")]
        [SerializeField] private Transform m_PromptAnchor;

        [Tooltip("提示相对本物体的高度偏移（米）。Prompt Anchor 为空时才用")]
        [SerializeField] private Vector3 m_PromptOffset = new Vector3(0f, 1.8f, 0f);

        [Tooltip("提示下方的小字，可留空。例如：拉下拉杆")]
        [SerializeField] private string m_PromptLabel = "";

        [Tooltip("拉到底后，还能不能再按 E 推回去。关掉 = 一次性开关，拉完就不再提示")]
        [SerializeField] private bool m_ToggleBack = false;

        [Header("转动")]
        [Tooltip("真正转动的部件（拉杆手柄）。留空 = 转本物体")]
        [SerializeField] private Transform m_RotatingPart;

        [Tooltip("转轴位置。留空 = 绕转动部件自己的原点转。建议放一个空物体在齿轮中心")]
        [SerializeField] private Transform m_Hinge;

        [Tooltip("转轴方向（Hinge 的本地坐标轴）。XY 平面里的拉杆一般填 (0,0,1)")]
        [SerializeField] private Vector3 m_Axis = Vector3.forward;

        [Tooltip("拉到底转多少度。正负决定方向")]
        [SerializeField] private float m_Angle = 60f;

        [Tooltip("转动用时（秒）。0.25~0.5 比较像拉杆，太慢会显得拖沓")]
        [SerializeField] private float m_Duration = 0.35f;

        [Tooltip("缓动曲线。默认两头慢中间快，不需要改")]
        [SerializeField] private AnimationCurve m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("一开始就是拉到底的状态")]
        [SerializeField] private bool m_StartPulled = false;

        [Header("联动")]
        [Tooltip("拉到底时自动打开这扇门。不需要联动就留空，改用下面的 UnityEvent")]
        [SerializeField] private MechanismDoor m_TargetDoor;

        [Header("事件")]
        [Tooltip("开始往下拉的那一刻")]
        [SerializeField] private UnityEvent m_OnPullStarted;

        [Tooltip("拉到底、停稳之后。门一般接这里")]
        [SerializeField] private UnityEvent m_OnPulled;

        [Tooltip("开始推回去的那一刻（只在 Toggle Back 开启时有用）")]
        [SerializeField] private UnityEvent m_OnReturnStarted;

        [Tooltip("推回原位、停稳之后")]
        [SerializeField] private UnityEvent m_OnReturned;

        private Transform m_Player;
        private PlayerController m_PlayerController;
        private float m_PlayerSearchTimer;

        private Vector3 m_StartPosition;
        private Quaternion m_StartRotation;
        private Vector3 m_PivotPosition;
        private Vector3 m_WorldAxis;

        private float m_Progress;
        private float m_FromProgress;
        private float m_TargetProgress;
        private Coroutine m_Routine;
        private bool m_Busy;
        private bool m_Initialised;

        /// <summary>手柄正在转。</summary>
        public bool IsBusy { get { return m_Busy; } }

        /// <summary>0 = 完全没拉，1 = 拉到底。</summary>
        public float Progress { get { return m_Progress; } }

        /// <summary>是否已经拉到底。</summary>
        public bool IsPulled { get { return m_Progress >= 0.999f; } }

        private void Start()
        {
            Initialise();
        }

        private void Update()
        {
            if (!m_Initialised || m_Busy || !CanInteract || MechanismCameraShot.IsAnyPlaying)
            {
                InteractionPrompt.Release(this);
                return;
            }

            EnsurePlayer();

            if (m_Player == null ||
                (m_PlayerController != null && (m_PlayerController.IsInCarrier || m_PlayerController.ControlLocked)))
            {
                InteractionPrompt.Release(this);
                return;
            }

            Vector3 delta = m_Player.position - transform.position;
            if (delta.sqrMagnitude > m_InteractDistance * m_InteractDistance)
            {
                InteractionPrompt.Release(this);
                return;
            }

            InteractionPrompt.Request(this, PromptPoint, InteractKeyLabel(), m_PromptLabel);

            if (InteractPressed())
            {
                BeginPull();
            }
        }

        private void OnDisable()
        {
            InteractionPrompt.Release(this);

            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_Busy = false;
        }

        private bool CanInteract
        {
            get { return !m_Busy && (m_ToggleBack || m_Progress < 0.999f); }
        }

        private Vector3 PromptPoint
        {
            get { return m_PromptAnchor != null ? m_PromptAnchor.position : transform.position + m_PromptOffset; }
        }

        private void Initialise()
        {
            if (m_RotatingPart == null)
            {
                m_RotatingPart = transform;
            }

            m_StartPosition = m_RotatingPart.position;
            m_StartRotation = m_RotatingPart.rotation;
            m_PivotPosition = m_Hinge != null ? m_Hinge.position : m_StartPosition;

            Vector3 axis = m_Axis.sqrMagnitude > 0.0001f ? m_Axis.normalized : Vector3.forward;
            m_WorldAxis = m_Hinge != null ? m_Hinge.TransformDirection(axis) : m_StartRotation * axis;
            m_WorldAxis.Normalize();

            m_Progress = m_StartPulled ? 1f : 0f;
            m_Initialised = true;
            ApplyProgress(m_Progress);
        }

        private void EnsurePlayer()
        {
            if (m_Player != null)
            {
                return;
            }

            m_PlayerSearchTimer -= Time.deltaTime;
            if (m_PlayerSearchTimer > 0f)
            {
                return;
            }

            // 玩家可能是运行时由流程生成的，所以找不到时每 0.5 秒再试一次。
            m_PlayerSearchTimer = 0.5f;
            m_PlayerController = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
            if (m_PlayerController != null)
            {
                m_Player = m_PlayerController.transform;
            }
        }

        private void BeginPull()
        {
            if (m_Busy)
            {
                return;
            }

            if (!m_ToggleBack && m_Progress >= 0.999f)
            {
                return;
            }

            // 0 = 没拉，1 = 拉到底。往哪边动由当前进度决定。
            bool pulling = m_Progress < 0.5f;

            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_FromProgress = m_Progress;
            m_TargetProgress = pulling ? 1f : 0f;
            m_Busy = true;

            InteractionPrompt.Release(this);

            if (pulling)
            {
                m_OnPullStarted?.Invoke();
            }
            else
            {
                m_OnReturnStarted?.Invoke();
            }

            if (m_Duration <= 0.0001f)
            {
                ApplyProgress(m_TargetProgress);
                FinishMotion();
                return;
            }

            m_Routine = StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            float duration = Mathf.Max(0.0001f, m_Duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                // 用 scale 时间：暂停菜单把 timeScale 设为 0 时拉杆也会停。
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
            FinishMotion();
        }

        private void FinishMotion()
        {
            m_Routine = null;
            m_Busy = false;

            if (m_TargetProgress >= 0.999f)
            {
                m_OnPulled?.Invoke();

                if (m_TargetDoor != null)
                {
                    m_TargetDoor.Open();
                }
            }
            else
            {
                m_OnReturned?.Invoke();

                if (m_TargetDoor != null)
                {
                    m_TargetDoor.Close();
                }
            }
        }

        /// <summary>
        /// 把“拉杆进度”应用到模型。绕 Hinge 圆心旋转，位置和朝向一起算，
        /// 所以手柄挂在哪个父物体下面都能正确转。
        /// </summary>
        private void ApplyProgress(float progress)
        {
            m_Progress = progress;

            if (m_RotatingPart == null)
            {
                return;
            }

            Quaternion delta = Quaternion.AngleAxis(m_Angle * progress, m_WorldAxis);
            Vector3 position = m_PivotPosition + delta * (m_StartPosition - m_PivotPosition);
            m_RotatingPart.SetPositionAndRotation(position, delta * m_StartRotation);
        }

        /// <summary>
        /// 直接摆到“拉起 / 放下”的状态，不播动画。给读档、场景初始化用。
        /// </summary>
        public void SetPulledInstant(bool pulled)
        {
            if (!m_Initialised)
            {
                Initialise();
            }

            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            m_Busy = false;
            ApplyProgress(pulled ? 1f : 0f);
        }

        /// <summary>手动触发一次拉动（例如别的机关联动它）。</summary>
        public void Pull()
        {
            BeginPull();
        }

        private static bool InteractPressed()
        {
            // 走项目统一的按键绑定：默认 E，支持手柄，也会在暂停 / 面板打开时自动屏蔽。
            return PAO.UI.V2.UIInput.Down(PAO.UI.V2.UIInput.Interact);
        }

        private static string InteractKeyLabel()
        {
            string label = PAO.UI.V2.UIInput.Prompt(PAO.UI.V2.UIInput.Interact);
            return string.IsNullOrEmpty(label) ? "E" : label;
        }

        private void OnValidate()
        {
            m_InteractDistance = Mathf.Max(0.1f, m_InteractDistance);
            m_Duration = Mathf.Max(0f, m_Duration);

            if (m_Ease == null || m_Ease.length == 0)
            {
                m_Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 0.9f, 0.55f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, m_InteractDistance));

            Transform hinge = m_Hinge != null ? m_Hinge : (m_RotatingPart != null ? m_RotatingPart : transform);
            Vector3 axis = m_Axis.sqrMagnitude > 0.0001f ? m_Axis.normalized : Vector3.forward;
            Vector3 worldAxis = (hinge.rotation * axis).normalized;

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.95f);
            Gizmos.DrawLine(hinge.position - worldAxis * 0.6f, hinge.position + worldAxis * 0.6f);
            Gizmos.DrawWireSphere(hinge.position, 0.08f);

            Vector3 prompt = m_PromptAnchor != null ? m_PromptAnchor.position : transform.position + m_PromptOffset;
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(prompt, Vector3.one * 0.12f);
        }
    }
}
