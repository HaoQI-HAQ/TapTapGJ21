using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 表情导演：根据玩家当前状态决定该显示哪张脸，然后交给 JellyFaceSwitcher 去切。
    ///
    /// 【优先级（高 → 低）】
    ///   1. 从泡泡里掉出来、还在下落   → Dead（持续到落地）
    ///   2. 蓄力吐泡泡（按住左键）     → Relaxed
    ///   3. 滞空（跳跃）               → Cute
    ///   4. 在泡泡里（按 F 钻进去）    → Smug
    ///   5. 泡泡值低于阈值             → Cry / Dead 随机（进入低值时随一次）
    ///   6. 待机 / 移动                → 四个里等权随机
    ///
    /// 【待机随机的时机】
    /// 只在「移动 → 停下」的那一刻换一次，不是定时轮换 ——
    /// 站着不动时脸会一直保持，不会自己跳来跳去。
    ///
    /// 【为什么严格判断「变了才切」】
    /// JellyFaceSwitcher 换材质会触发一次 renderer 重建，
    /// 每帧无脑调会有额外开销和不必要的抖动。
    ///
    /// 挂在 Player 上即可，引用留空会自动找。
    /// </summary>
    [DisallowMultipleComponent]
    public class JellyFaceDirector : MonoBehaviour
    {
        [Header("绑定（留空自动找）")]
        [SerializeField] private JellyFaceSwitcher m_Switcher;
        [SerializeField] private PlayerAnimatorDriver m_AnimatorDriver;
        [SerializeField] private BubbleLauncher m_Launcher;
        [SerializeField] private BubbleRideInteractor m_RideInteractor;
        [SerializeField] private BubbleResourceManager m_Resource;
        [SerializeField] private CharacterController m_Character;

        [Header("① 下落（从泡泡里掉出来，持续到落地）")]
        [SerializeField] private JellyFace m_FallFace = JellyFace.Dead;

        [Header("② 吐泡泡（蓄力中）")]
        [SerializeField] private JellyFace m_BlowFace = JellyFace.Relaxed;

        [Header("③ 跳跃（滞空期）")]
        [SerializeField] private JellyFace m_JumpFace = JellyFace.Cute;

        [Header("④ 在泡泡里（按 F）")]
        [SerializeField] private JellyFace m_RideFace = JellyFace.Smug;

        [Header("⑤ 泡泡值低")]
        [Tooltip("低于这个值就换成下面的候补表情")]
        [SerializeField] private int m_LowResourceThreshold = 20;

        [Tooltip("泡泡值低时从这里随机挑一张，进入低值时随一次")]
        [SerializeField] private JellyFace[] m_LowResourceFaces = { JellyFace.Cry, JellyFace.Dead };

        [Header("⑥ 待机 / 移动")]
        [Tooltip("待机时从这里等权随机，只在「移动 → 停下」那一刻换一次")]
        [SerializeField] private JellyFace[] m_IdleFaces =
        {
            JellyFace.Normal, JellyFace.Smug, JellyFace.Cute, JellyFace.Drool
        };

        // ---------- 运行时状态 ----------
        private bool m_WasRiding;              // 上一帧是否在泡泡里
        private bool m_FallingFromBubble;      // 是不是「从泡泡掉出来」的下落中
        private bool m_WasMoving;              // 上一帧是否在移动
        private bool m_WasLowResource;         // 上一帧泡泡值是否偏低
        private JellyFace m_IdleFace;          // 当前待机表情
        private JellyFace m_LowFace;           // 当前低值表情

        /// <summary>当前该显示的表情（调试用）。</summary>
        public JellyFace TargetFace { get; private set; }

        private void Awake()
        {
            CacheReferences();

            // 开局先随机一张待机脸，别一上来就是 Normal
            m_IdleFace = PickRandom(m_IdleFaces, JellyFace.Normal);
            m_LowFace = PickRandom(m_LowResourceFaces, JellyFace.Cry);
        }

        private void CacheReferences()
        {
            if (m_Switcher == null) { m_Switcher = GetComponent<JellyFaceSwitcher>(); }
            if (m_Switcher == null) { m_Switcher = GetComponentInChildren<JellyFaceSwitcher>(true); }

            if (m_AnimatorDriver == null) { m_AnimatorDriver = GetComponent<PlayerAnimatorDriver>(); }
            if (m_Launcher == null) { m_Launcher = GetComponent<BubbleLauncher>(); }
            if (m_RideInteractor == null) { m_RideInteractor = GetComponent<BubbleRideInteractor>(); }
            if (m_Resource == null) { m_Resource = GetComponent<BubbleResourceManager>(); }
            if (m_Character == null) { m_Character = GetComponent<CharacterController>(); }
        }

        private void Update()
        {
            if (m_Switcher == null)
            {
                return;
            }

            bool grounded = IsGrounded();
            bool riding = m_RideInteractor != null && m_RideInteractor.IsRiding;
            bool blowing = m_Launcher != null && m_Launcher.IsCharging;
            bool airborne = m_AnimatorDriver != null && m_AnimatorDriver.IsAirborne;
            bool moving = m_AnimatorDriver != null && m_AnimatorDriver.IsMoving;

            UpdateFallState(grounded, riding);
            UpdateLowResourceFace();
            UpdateIdleFace(moving);

            TargetFace = DecideFace(riding, blowing, airborne);

            // 只在真的变了才切 —— 换材质会触发 renderer 重建
            if (TargetFace != m_Switcher.Current)
            {
                m_Switcher.SetExpression(TargetFace);
            }
        }

        /// <summary>
        /// 从泡泡里掉出来的下落：离开泡泡那一刻开始，落地那一刻结束。
        /// </summary>
        private void UpdateFallState(bool grounded, bool riding)
        {
            // 刚离开泡泡 → 进入下落
            if (m_WasRiding && !riding)
            {
                m_FallingFromBubble = true;
            }

            m_WasRiding = riding;

            // 落地就结束
            if (m_FallingFromBubble && grounded)
            {
                m_FallingFromBubble = false;
            }
        }

        /// <summary>
        /// 泡泡值低的脸：只在「刚进入偏低」时随一次，
        /// 不然每帧都随会导致表情疯狂闪。
        /// </summary>
        private void UpdateLowResourceFace()
        {
            bool low = m_Resource != null && m_Resource.Current < m_LowResourceThreshold;

            if (low && !m_WasLowResource)
            {
                m_LowFace = PickRandom(m_LowResourceFaces, JellyFace.Cry);
            }

            m_WasLowResource = low;
        }

        /// <summary>
        /// 待机随机：只在「移动 → 停下」的那一刻换一次。
        /// 站着不动时保持当前脸，不会自己跳。
        /// </summary>
        private void UpdateIdleFace(bool moving)
        {
            if (m_WasMoving && !moving)
            {
                m_IdleFace = PickRandom(m_IdleFaces, JellyFace.Normal);
            }

            m_WasMoving = moving;
        }

        /// <summary>
        /// 按优先级挑出这一帧该显示的脸。
        /// </summary>
        private JellyFace DecideFace(bool riding, bool blowing, bool airborne)
        {
            // 1. 从泡泡掉出来、还在下落 —— 持续到落地
            if (m_FallingFromBubble)
            {
                return m_FallFace;
            }

            // 2. 蓄力吐泡泡
            if (blowing)
            {
                return m_BlowFace;
            }

            // 3. 滞空（跳跃）。IsAirborne 内部已经排除了「在泡泡里」
            if (airborne)
            {
                return m_JumpFace;
            }

            // 4. 在泡泡里
            if (riding)
            {
                return m_RideFace;
            }

            // 5. 泡泡值低
            if (m_WasLowResource)
            {
                return m_LowFace;
            }

            // 6. 待机 / 移动
            return m_IdleFace;
        }

        /// <summary>
        /// CharacterController 在泡泡里会被禁用，那时读 isGrounded 没意义。
        /// </summary>
        private bool IsGrounded()
        {
            if (m_Character == null || !m_Character.enabled || !m_Character.gameObject.activeInHierarchy)
            {
                return false;
            }

            return m_Character.isGrounded;
        }

        /// <summary>从数组里等权随机一个。数组为空时用 fallback。</summary>
        private static JellyFace PickRandom(JellyFace[] faces, JellyFace fallback)
        {
            if (faces == null || faces.Length == 0)
            {
                return fallback;
            }

            return faces[Random.Range(0, faces.Length)];
        }
    }
}