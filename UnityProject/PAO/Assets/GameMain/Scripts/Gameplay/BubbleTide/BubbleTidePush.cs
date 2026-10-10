using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 玩家被泡泡潮推回去。
    ///
    /// 【为什么不给潮水加碰撞体】
    /// 一个会淹没 43×13×33 米房间的潮水，前沿是一张不断变形的曲面。
    /// 想用碰撞体表达它，要么摆几千个球（PhysX 直接跪），
    /// 要么每帧重建一张碰撞网格（CPU 直接跪）。
    ///
    /// 但潮水本来就有「每个点第几秒被淹」这张表，所以判断玩家有没有被淹
    /// 只需要**一次查表**，方向就是这张表的梯度。零物理开销，而且因为场是连续的，
    /// 推的方向永远是自然的。
    ///
    /// 【为什么不直接改 transform】
    /// 玩家是 CharacterController。直接改 transform 会绕过它的碰撞处理，
    /// 表现就是被推进墙里或者穿过地板。所以这里走 CharacterController.Move，
    /// 让它自己去做碰撞与滑动 —— 玩家贴着墙被推时会被自然地挤向侧面。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/潮水推挤")]
    public sealed class BubbleTidePush : MonoBehaviour
    {
        [Header("推力")]
        [Tooltip("被潮水推动的速度（米/秒）。0 = 只判定不推。")]
        [SerializeField, Range(0f, 20f)] private float m_PushSpeed = 3.5f;

        [Tooltip("提前量（米）。潮水面离玩家还有这么远时就开始推，手感更自然，\n" +
                 "不会出现「贴上了才突然开始动」的顿挫。")]
        [SerializeField, Range(0f, 3f)] private float m_LookAhead = 0.5f;

        [Tooltip("推力的平滑时间（秒）。0 = 立即生效。给一点平滑能避免帧率波动被放大。")]
        [SerializeField, Range(0f, 1f)] private float m_SmoothTime = 0.15f;

        [Header("判定")]
        [Tooltip("玩家被判定为「已淹没」时是否广播事件（掉血、播特效之类接在这里）。")]
        [SerializeField] private bool m_RaiseSubmergedEvent = true;

        // ---------------- 运行时 ----------------

        private BubbleTideField m_Field;
        private Transform m_Player;
        private CharacterController m_Controller;

        private Vector3 m_CurrentPush;
        private Vector3 m_PushVelocity;      // SmoothDamp 用的中间量
        private bool m_WasSubmerged;

        /// <summary>玩家这一帧被判定为淹没。</summary>
        public bool IsSubmerged { get; private set; }

        /// <summary>玩家刚被淹到的那一帧触发一次。</summary>
        public event System.Action Submerged;

        /// <summary>玩家脱离潮水的那一帧触发一次。</summary>
        public event System.Action Surfaced;

        /// <summary>玩家当前位置。别的系统想查潮水又懒得找玩家就读它。</summary>
        public Vector3 PlayerPosition
        {
            get { return m_Player != null ? m_Player.position : transform.position; }
        }

        /// <summary>玩家离潮水面还有多远（米）。负值表示已经被淹。</summary>
        public float DistanceToFront { get; private set; }

        /// <summary>
        /// Director 在烤制完成后调用。
        /// </summary>
        public void Bind(BubbleTideField field, Transform player)
        {
            m_Field = field;
            SetPlayer(player);
        }

        /// <summary>
        /// 由 Director 注入 Inspector 上的数值（组件是自动挂的，读不到面板）。
        /// </summary>
        public void Configure(float pushSpeed, float lookAhead, Transform player)
        {
            m_PushSpeed = Mathf.Max(0f, pushSpeed);
            m_LookAhead = Mathf.Max(0f, lookAhead);
            SetPlayer(player);
        }

        private void SetPlayer(Transform player)
        {
            m_Player = player;

            if (m_Player == null)
            {
                m_Controller = null;
                return;
            }

            m_Controller = m_Player.GetComponent<CharacterController>();

            if (m_Controller == null)
            {
                Debug.LogWarning("[泡泡潮] 玩家身上没有 CharacterController，" +
                                 "潮水只能判定、不能推人。");
            }
        }

        private void Update()
        {
            if (m_Field == null || !m_Field.IsBaked || m_Player == null)
            {
                return;
            }

            float arrival = m_Field.SampleArrivalTime(m_Player.position);
            float elapsed = GetElapsedSeconds();

            // 站在实心格里（比如卡进墙里）时 arrival 是无穷大，此时不推也不判定
            bool valid = !float.IsPositiveInfinity(arrival);
            bool submerged = valid && elapsed >= arrival;

            IsSubmerged = submerged;

            // 到潮水面的距离：正 = 还没被淹，单位是米。
            // 用「还有多少秒」乘基准速度近似 —— 够准，而且省掉一次速度场反查。
            DistanceToFront = valid
                ? (arrival - elapsed) * m_Field.EstimatedSpeed
                : float.PositiveInfinity;

            if (m_RaiseSubmergedEvent)
            {
                if (submerged && !m_WasSubmerged && Submerged != null)
                {
                    Submerged();
                }
                else if (!submerged && m_WasSubmerged && Surfaced != null)
                {
                    Surfaced();
                }

                m_WasSubmerged = submerged;
            }

            ApplyPush(elapsed, arrival, valid);
        }

        private float GetElapsedSeconds()
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            return director != null ? director.Elapsed : 0f;
        }

        /// <summary>
        /// 施力。方向取场的负梯度（潮水推进方向），大小随「离潮水面多近」渐变。
        /// </summary>
        private void ApplyPush(float elapsed, float arrival, bool valid)
        {
            Vector3 target = Vector3.zero;

            if (valid && m_PushSpeed > 0.0001f)
            {
                // 提前量换算成秒：让「还有 LookAhead 米」时就开始有推力
                float lookAheadSeconds = m_LookAhead / Mathf.Max(0.05f, m_Field.EstimatedSpeed);
                float remaining = arrival - elapsed;

                // remaining 从 lookAheadSeconds 线性衰减到 0 时，推力从 0 涨到满
                float strength = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.0001f, lookAheadSeconds));

                if (strength > 0f)
                {
                    Vector3 direction = m_Field.SampleFlowDirection(m_Player.position);
                    if (direction.sqrMagnitude > 1e-5f)
                    {
                        target = direction * (m_PushSpeed * strength);
                    }
                }
            }

            if (m_SmoothTime > 0.0001f)
            {
                m_CurrentPush = Vector3.SmoothDamp(
                    m_CurrentPush, target, ref m_PushVelocity, m_SmoothTime);
            }
            else
            {
                m_CurrentPush = target;
            }

            if (m_CurrentPush.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 delta = m_CurrentPush * Time.deltaTime;

            if (m_Controller != null && m_Controller.enabled)
            {
                // 走 CharacterController 而不是直接改 transform：
                // 这样贴墙被推时会自然沿墙滑动，而不是被塞进墙里
                m_Controller.Move(delta);
            }
            else
            {
                // 玩家在泡泡里时 CharacterController 是关掉的（见 BubbleRideInteractor），
                // 这时泡泡的载具逻辑会接管位置，这里就不插手，免得两边打架
            }
        }
    }
}
