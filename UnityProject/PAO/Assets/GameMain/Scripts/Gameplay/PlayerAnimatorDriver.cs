// ---------------------------------------------------------------------------
// PAO / PlayerAnimatorDriver —— 把 PlayerController 的运动状态翻译成 Animator 参数
//
// 状态机本体在 AnimatorController 资产里（见 Editor/PlayerAnimatorSetup.cs 生成器），
// 本脚本只负责「读状态、喂参数」，不自己决定播哪段动画。
//
// 参数契约（名字必须与 controller 里的完全一致）：
//   Grounded   bool    是否站在地上。载具内一律按 false 处理
//   Lifted     bool    是否正被浮粘泡泡提着上升
//   MoveSpeed  float   当前水平移动速度（米/秒），驱动 Idle ↔ Move
//   Jump       trigger 起跳瞬间
//
// 状态流转：
//   Idle  ⇄ Move ⇄ JumpStart → AirLoop → Land → Idle/Move
//   空中（或滞空/被提）统一走 AirLoop
//
// 挂载位置：与 PlayerController 同一个 GameObject。
// ---------------------------------------------------------------------------
using UnityEngine;

namespace PAO
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashLifted = Animator.StringToHash("Lifted");
        private static readonly int HashMoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int HashJump = Animator.StringToHash("Jump");

        [Header("绑定")]
        [Tooltip("留空则自动在子物体里找（模型根节点上那个 Animator）")]
        [SerializeField] private Animator m_Animator;

        [Header("参数阈值")]
        [Tooltip("小于这个速度直接归零，避免角色基本站定时残留的微小位移把状态抖到 Move")]
        [SerializeField] private float m_MoveSpeedThreshold = 0.1f;

        [Tooltip("速度参数的平滑时间（秒）。0 = 不平滑")]
        [SerializeField] private float m_StateDamp = 0.1f;

        private PlayerController m_Player;
        private CharacterController m_Character;
        private Vector3 m_LastPosition;
        private float m_SmoothedSpeed;  // 自己维护平滑值，不用 SetFloat 的阻尼版 —— 那样没法做阈值归零
        private bool m_WasAirborne;     // 上一帧是否处于空中/滞空
        private bool m_JumpThisFrame;   // 本帧是否刚发出起跳，用来压制「起跳瞬间还判在地面」

        /// <summary>当前播的状态名，调试用。</summary>
        public string CurrentStateName { get; private set; } = "(未绑定)";

        /// <summary>
        /// 当前是否在水平移动（用位移算的平滑速度判断）。
        /// 表情系统靠它判断「从移动到停下」的那一刻。
        /// </summary>
        public bool IsMoving
        {
            get { return m_SmoothedSpeed >= m_MoveSpeedThreshold; }
        }

        private void Awake()
        {
            m_Player = GetComponent<PlayerController>();
            m_Character = GetComponent<CharacterController>();

            if (m_Animator == null)
            {
                m_Animator = GetComponentInChildren<Animator>(true);
            }

            m_LastPosition = transform.position;
        }

        private void Update()
        {
            if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
            {
                return;
            }

            // 用实际位移算速度，而不是读输入 —— 被泡泡推着走、被弹飞时也算「在动」。
            // deltaTime 为 0 是可能的（一帧内多次 Update），必须挡住，否则会算出 NaN/Infinity。
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            Vector3 delta = transform.position - m_LastPosition;
            m_LastPosition = transform.position;

            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z) / deltaTime;
            float rawSpeed = horizontal.magnitude;

            // 阈值以下直接归零：角色站着不动时位置仍有极微小抖动，
            // 不掐掉的话 Idle/Move 会来回闪。
            float target = rawSpeed < m_MoveSpeedThreshold ? 0f : rawSpeed;

            if (m_StateDamp > 0f)
            {
                // 用 Lerp 而不是指数公式：大 deltaTime 下也不会过冲，帧率无关
                m_SmoothedSpeed = Mathf.Lerp(m_SmoothedSpeed, target, deltaTime / m_StateDamp);
            }
            else
            {
                m_SmoothedSpeed = target;
            }

            m_Animator.SetFloat(HashMoveSpeed, m_SmoothedSpeed);

            // 载具内（钻在泡泡里）既不算落地也不算滞空，统一交给 Lifted 分支
            bool lifted = m_Player.IsLifted;
            bool grounded = !lifted && !m_Player.IsInCarrier && IsGrounded();

            // Grounded / Lifted 必须瞬时生效，不能平滑，否则落地判定会晚好几帧
            m_Animator.SetBool(HashLifted, lifted);
            m_Animator.SetBool(HashGrounded, grounded);

            // 起跳：只在真正站在地上时受理，避免空中狂按 Jump 把状态机踢回 JumpStart
            m_JumpThisFrame = grounded && Input.GetButtonDown("Jump");
            if (m_JumpThisFrame)
            {
                m_Animator.SetTrigger(HashJump);
            }

            UpdateStateName();

            // 记录本帧的离地状态，供 Land 判定
            m_WasAirborne = !grounded;
        }

        /// <summary>
        /// CharacterController 在载具内会被禁用，此时读 isGrounded 无意义且可能报错，
        /// 所以先判可用性。
        /// </summary>
        private bool IsGrounded()
        {
            if (m_Character == null || !m_Character.enabled || !m_Character.gameObject.activeInHierarchy)
            {
                return false;
            }

            return m_Character.isGrounded;
        }

        private void UpdateStateName()
        {
            if (m_Animator.IsInTransition(0))
            {
                return;
            }

            var info = m_Animator.GetCurrentAnimatorStateInfo(0);
            var clips = m_Animator.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
            {
                CurrentStateName = clips[0].clip.name;
            }
            else
            {
                CurrentStateName = "hash:" + info.shortNameHash;
            }
        }

        /// <summary>外部想知道「现在算不算在空中」，供别处复用（例如音效、拖尾）。</summary>
        public bool IsAirborne => !IsGrounded() && !m_Player.IsInCarrier;

        /// <summary>上一帧是否在空中，用于判定落地那一刻。</summary>
        public bool WasAirborne => m_WasAirborne;
    }
}
