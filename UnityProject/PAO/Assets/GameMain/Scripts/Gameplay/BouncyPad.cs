using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 弹力跳板：必须**按顺序**两段充能才会变强。
    ///
    /// ============================================================
    /// 【三段状态，只能逐级升】
    /// ============================================================
    ///
    ///   Normal（普通板）
    ///       ↓  被弹力泡泡碰到，**且那颗弹力泡泡消失之后**才升级
    ///   Bouncy（弹力板）       玩家踩上去弹起 4 米
    ///       ↓  被炸弹爆炸波及（只有这一种方式能升到 Super）
    ///   Super（超级弹力板）    玩家踩上去弹起 11 米
    ///
    /// 【两条硬规则】
    ///   1. **不能跳级**：普通板被炸弹炸到 → 什么都不发生。
    ///      必须先是弹力板，炸弹才能把它升成超级板。
    ///   2. **弹力泡泡要先消失**：碰到只算"登记"，等那颗泡泡真的不见了
    ///      才升级。所以接触和升级之间隔了一帧以上，不是瞬间完成。
    ///
    /// ============================================================
    /// 【为什么是"泡泡消失后才升级"】
    /// ============================================================
    /// 这样这块板才是"吃掉一颗弹力泡泡"来充能的机关，
    /// 而不是"被泡泡擦一下就有反应"。玩家也必须付出一个泡泡。
    ///
    /// 实现上用 m_ChargePending 记住"有一颗弹力泡泡正在充能"，
    /// 每帧检查它是否已经被销毁（Unity 里被 Destroy 的组件 == null 为 true）。
    ///
    /// m_PopBubbleOnCharge（默认开）会在碰到时**直接把泡泡吃掉**，
    /// 于是"碰到 → 泡泡消失 → 变成弹力板"一气呵成；
    /// 关掉它就变成"等泡泡自己消失"，适合做"把泡泡运到板上再弄破"的谜题。
    ///
    /// ============================================================
    /// 【两种触发怎么接】
    /// ============================================================
    /// 1. 弹力泡泡 → OnCollisionEnter（弹力泡泡是 Rigidbody，会与本碰撞体互推）
    /// 2. 炸弹爆炸 → 订阅 <see cref="Bubble.Exploded"/>。
    ///    炸弹是引信到点才炸，落下时早就没有碰撞回调了，靠碰撞抓不到。
    ///
    /// ============================================================
    /// 【玩家踩上去怎么检测】
    /// ============================================================
    /// 不能用 OnCollisionEnter：玩家是 CharacterController，
    /// 它只在自己身上发 OnControllerColliderHit，静态碰撞体这侧收不到回调。
    /// 所以每帧在板面上方做一次 OverlapBox 找 PlayerController。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/弹力跳板（Bouncy Pad）")]
    public sealed class BouncyPad : MonoBehaviour
    {
        public enum PadState
        {
            Normal = 0,
            Bouncy = 1,
            Super = 2,
        }

        // ---- 弹力 ----

        [Header("弹力高度（米）")]
        [Tooltip("被弹力泡泡充能后，玩家踩上去弹起的高度")]
        [SerializeField] private float m_BouncyHeight = 4.0f;

        [Tooltip("被炸弹炸过之后，玩家踩上去弹起的高度（超级弹力板）")]
        [SerializeField] private float m_SuperHeight = 11.0f;

        // ---- 充能规则 ----

        [Header("充能规则")]
        [Tooltip("被弹力泡泡碰到时，是否必须等那颗泡泡消失才升级为弹力板")]
        [SerializeField] private bool m_RequireBubbleConsumed = true;

        [Tooltip("碰到弹力泡泡时直接把它吃掉（泡泡当场消失 → 板子随即升级）。" +
                 "关掉则等泡泡自己消失，适合「把泡泡运到板上再弄破」的谜题")]
        [SerializeField] private bool m_PopBubbleOnCharge = true;

        [Tooltip("等待泡泡消失的最长时间（秒）。超时就放弃这次充能")]
        [SerializeField] private float m_ChargeTimeout = 10f;

        [Tooltip("炸弹只能把「弹力板」升成「超级弹力板」，不能直接把普通板炸成超级板")]
        [SerializeField] private bool m_BombRequiresBouncy = true;

        [Tooltip("炸弹的判定范围额外放大（米）。爆炸半径已经算进去了，这里只是保险量")]
        [SerializeField] private float m_BlastReachBonus = 0.5f;

        // ---- 手感 ----

        [Header("手感")]
        [Tooltip("两次弹起之间的最短间隔（秒）。防止同一帧被重复触发")]
        [SerializeField] private float m_Cooldown = 0.20f;

        [Tooltip("板面上方多高范围内算「踩在板上」")]
        [SerializeField] private float m_DetectHeight = 0.55f;

        [Tooltip("弹一次之后是否退回普通板")]
        [SerializeField] private bool m_ResetAfterBounce = false;

        // ---- 表现 ----

        [Header("表现")]
        [Tooltip("状态变化时把板子染一下色，让玩家一眼看出「这块板激活了」")]
        [SerializeField] private bool m_TintByState = true;

        [Tooltip("普通板的染色（相对原色）。白色 = 不改动")]
        [SerializeField] private Color m_NormalTint = Color.white;

        [Tooltip("弹力板的染色")]
        [SerializeField] private Color m_BouncyTint = new Color(0.72f, 0.95f, 1.00f);

        [Tooltip("超级弹力板的染色")]
        [SerializeField] private Color m_SuperTint = new Color(1.00f, 0.92f, 0.55f);

        [Tooltip("成功弹起时闪一下的时长（秒）")]
        [SerializeField] private float m_FlashDuration = 0.16f;

        // ---- 调试 ----

        [Header("调试")]
        [SerializeField] private bool m_Log = true;

        // ---- 运行时状态 ----

        private PadState m_State = PadState.Normal;

        // 充能等待：登记一颗"碰到过本板的弹力泡泡"，等它消失后才升级
        private bool m_ChargePending;
        private Bubble m_PendingBubble;
        private float m_PendingTimer;

        private float m_CooldownLeft;
        private float m_FlashLeft;
        private Color m_FlashColor = Color.white;

        private Collider m_SolidCollider;       // 用来取板面的世界范围
        private Renderer m_Renderer;
        private MaterialPropertyBlock m_Block;
        private Color m_BaseColor = Color.white;

        private readonly Collider[] m_HitBuffer = new Collider[16];

        private static readonly int kBaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int kColorID = Shader.PropertyToID("_Color");

        /// <summary>当前状态。</summary>
        public PadState State { get { return m_State; } }

        /// <summary>是否正在等待某颗弹力泡泡消失。</summary>
        public bool IsCharging { get { return m_ChargePending; } }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            // 取一个**非 Trigger** 的碰撞体当"板面"。
            // 本物体上如果有多个碰撞体（比如额外挂了个触发区），这里要跳过它们。
            Collider[] cols = GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < cols.Length; i++)
            {
                if (!cols[i].isTrigger)
                {
                    m_SolidCollider = cols[i];
                    break;
                }
            }

            if (m_SolidCollider == null)
            {
                Debug.LogWarning("[BouncyPad] \"" + name + "\" 上没有非 Trigger 的碰撞体，" +
                                 "玩家踩不到它，跳板无法工作。", this);
            }

            m_Renderer = GetComponentInChildren<Renderer>();

            if (m_Renderer != null && m_Renderer.sharedMaterial != null)
            {
                Material mat = m_Renderer.sharedMaterial;

                if (mat.HasProperty(kBaseColorID))
                {
                    m_BaseColor = mat.GetColor(kBaseColorID);
                }
                else if (mat.HasProperty(kColorID))
                {
                    m_BaseColor = mat.GetColor(kColorID);
                }
            }

            m_Block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            Bubble.Exploded += HandleBubbleExploded;
        }

        private void OnDisable()
        {
            // 必须退订：静态事件不会随物体销毁自动清理，
            // 不退订会留下指向已销毁物体的悬空委托。
            Bubble.Exploded -= HandleBubbleExploded;
        }

        private void Start()
        {
            ApplyTint();
        }

        // ==================== 充能：弹力泡泡 ====================

        /// <summary>
        /// 弹力泡泡撞上来。
        ///
        /// 用 OnCollisionEnter 而不是 OnTriggerEnter ——
        /// 本板子是个实心碰撞体（玩家要站在上面），泡泡撞上来是碰撞而非触发。
        /// 万一有人把板子设成 Trigger，OnTriggerEnter 也兜住。
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            Bubble bubble = collision.collider.GetComponentInParent<Bubble>();

            if (bubble != null)
            {
                TryCharge(bubble);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Bubble bubble = other.GetComponentInParent<Bubble>();

            if (bubble != null)
            {
                TryCharge(bubble);
            }
        }

        /// <summary>
        /// 判断这颗泡泡能不能给本板充能。
        /// </summary>
        private void TryCharge(Bubble bubble)
        {
            // 只有普通板需要充能；已经是弹力板/超级板就不再处理
            if (m_State != PadState.Normal)
            {
                return;
            }

            // 已经在等某颗泡泡了，不重复登记
            if (m_ChargePending)
            {
                return;
            }

            if (bubble.Type != BubbleType.Bouncy)
            {
                if (m_Log)
                {
                    Debug.Log("[BouncyPad] \"" + name + "\" 被 " +
                              BubbleTypeSwitcher.DisplayNameOf(bubble.Type) +
                              " 碰到 —— 只有弹力泡泡能充能，忽略。", this);
                }

                return;
            }

            // ---- 命中弹力泡泡 ----

            // 【不能吃掉正在被玩家驾驶的泡泡】
            // Bubble.Explode() 在"没有炸弹参数"这条分支里是直接 Destroy 自己，
            // 而它的 OnDestroy 只清掉了 IsLifted（被提着）的状态，
            // **没有清 IsInCarrier（在驾驶）** ——
            // 那个状态由 BubbleRideInteractor 管理。
            // 所以如果玩家正坐在泡泡里，把这个泡泡销毁掉，人就会卡在
            // "还在载具里"的状态出不来。
            //
            // 载着人的泡泡不参与充能，让玩家先把人弄出来再来踩板子。
            if (bubble.Rider != null)
            {
                if (m_Log)
                {
                    Debug.Log("[BouncyPad] \"" + name + "\" 碰到一颗**载着人**的弹力泡泡，" +
                              "不消耗它（否则玩家会卡在载具里）。", this);
                }

                return;
            }

            if (!m_RequireBubbleConsumed)
            {
                // 不要求"先消失"：直接升级
                Upgrade(PadState.Bouncy, "被弹力泡泡碰到（不要求消失）");
                return;
            }

            // 登记这颗泡泡，等它消失
            m_ChargePending = true;
            m_PendingBubble = bubble;
            m_PendingTimer = m_ChargeTimeout;

            if (m_Log)
            {
                Debug.Log("[BouncyPad] \"" + name + "\" 被弹力泡泡碰到 —— " +
                          "等这颗泡泡消失后才变成弹力板。", this);
            }

            if (m_PopBubbleOnCharge)
            {
                // 直接把泡泡吃掉。"碰到 → 泡泡消失 → 变成弹力板"一气呵成。
                // 对弹力泡泡来说 Explode() 没有炸弹参数，只会销毁自己、不产生爆炸，
                // 所以这里不会误伤周围的东西。
                bubble.Explode();
            }
        }

        /// <summary>每帧检查正在等待的那颗泡泡是否已经消失。</summary>
        private void UpdateCharge()
        {
            if (!m_ChargePending)
            {
                return;
            }

            // Unity 里被 Destroy 的组件用 == null 判断为 true
            if (m_PendingBubble == null)
            {
                m_ChargePending = false;
                m_PendingBubble = null;
                Upgrade(PadState.Bouncy, "弹力泡泡已消失");
                return;
            }

            m_PendingTimer -= Time.deltaTime;

            if (m_PendingTimer <= 0f)
            {
                m_ChargePending = false;
                m_PendingBubble = null;

                if (m_Log)
                {
                    Debug.Log("[BouncyPad] \"" + name + "\" 等待弹力泡泡消失超时，" +
                              "取消这次充能。", this);
                }
            }
        }

        // ==================== 升级：炸弹爆炸 ====================

        private void HandleBubbleExploded(Vector3 center, float radius)
        {
            // 【硬规则】炸弹不能跳级：只有弹力板才能被炸成超级板。
            // 普通板被炸到什么都不发生 —— 必须先被弹力泡泡充能过。
            if (m_BombRequiresBouncy && m_State != PadState.Bouncy)
            {
                if (m_Log && m_State == PadState.Normal)
                {
                    Debug.Log("[BouncyPad] \"" + name + "\" 是普通板，被炸弹炸到无效 —— " +
                              "必须先用弹力泡泡充能成弹力板。", this);
                }

                return;
            }

            Vector3 padCenter;
            float padRadius;

            if (m_SolidCollider != null)
            {
                Bounds b = m_SolidCollider.bounds;
                padCenter = b.center;
                padRadius = Mathf.Max(b.extents.x, b.extents.z);
            }
            else
            {
                padCenter = transform.position;
                padRadius = 0.5f;
            }

            // 球到球的距离判定：爆炸球有没有碰到板子的外接圆
            float reach = radius + padRadius + m_BlastReachBonus;

            if (Vector3.Distance(center, padCenter) > reach)
            {
                return;
            }

            Upgrade(PadState.Super, "被炸弹炸到（距离 " +
                                    Vector3.Distance(center, padCenter).ToString("F2") +
                                    "m，判定 " + reach.ToString("F2") + "m）");
        }

        // ==================== 状态升级 ====================

        private void Upgrade(PadState target, string reason)
        {
            // 只升不降
            if ((int)target <= (int)m_State)
            {
                return;
            }

            m_State = target;

            if (m_Log)
            {
                Debug.Log("[BouncyPad] \"" + name + "\" → " + StateName() +
                          "（" + reason + "）", this);
            }

            ApplyTint();
        }

        private string StateName()
        {
            switch (m_State)
            {
                case PadState.Bouncy: return "弹力板";
                case PadState.Super:  return "超级弹力板";
                default:              return "普通板";
            }
        }

        private float BounceHeight()
        {
            switch (m_State)
            {
                case PadState.Bouncy: return m_BouncyHeight;
                case PadState.Super:  return m_SuperHeight;
                default:              return 0f;
            }
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            UpdateCharge();

            if (m_CooldownLeft > 0f)
            {
                m_CooldownLeft -= Time.deltaTime;
            }

            if (m_FlashLeft > 0f)
            {
                m_FlashLeft -= Time.deltaTime;

                if (m_FlashLeft <= 0f)
                {
                    ApplyTint();      // 闪光结束，恢复状态色
                }
            }

            // 普通板不检测玩家，省掉每帧的物理查询
            if (m_State == PadState.Normal || m_CooldownLeft > 0f)
            {
                return;
            }

            TryBouncePlayer();
        }

        private void TryBouncePlayer()
        {
            if (m_SolidCollider == null)
            {
                return;
            }

            Bounds b = m_SolidCollider.bounds;

            // 检测区：板面上方一小块。
            // 横向缩到 92%，避免玩家站在板子边缘外时被误判。
            Vector3 center = new Vector3(b.center.x,
                                         b.max.y + m_DetectHeight * 0.5f,
                                         b.center.z);

            Vector3 half = new Vector3(b.extents.x * 0.92f,
                                       m_DetectHeight * 0.5f,
                                       b.extents.z * 0.92f);

            int count = Physics.OverlapBoxNonAlloc(center, half, m_HitBuffer,
                                                   Quaternion.identity, ~0,
                                                   QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = m_HitBuffer[i];

                if (hit == null)
                {
                    continue;
                }

                PlayerController player = hit.GetComponentInParent<PlayerController>();

                if (player == null)
                {
                    continue;
                }

                // 玩家正在往上升时不弹，否则每帧重复触发会把速度叠成无限高
                if (player.VerticalSpeed > 0.5f)
                {
                    continue;
                }

                float height = BounceHeight();

                if (height <= 0.01f)
                {
                    return;
                }

                player.BounceUp(height);

                m_CooldownLeft = m_Cooldown;
                m_FlashLeft = m_FlashDuration;
                m_FlashColor = Color.white;
                ApplyTint();

                if (m_Log)
                {
                    Debug.Log("[BouncyPad] \"弹\" —— " + StateName() +
                              " 把玩家弹起 " + height.ToString("F2") + " 米", this);
                }

                if (m_ResetAfterBounce)
                {
                    m_State = PadState.Normal;
                    m_FlashLeft = 0f;
                    ApplyTint();
                }

                return;
            }
        }

        // ==================== 染色 ====================

        private void ApplyTint()
        {
            if (!m_TintByState || m_Renderer == null || m_Block == null)
            {
                return;
            }

            Color tint;

            if (m_FlashLeft > 0f)
            {
                tint = m_FlashColor;
            }
            else
            {
                switch (m_State)
                {
                    case PadState.Bouncy: tint = m_BouncyTint; break;
                    case PadState.Super:  tint = m_SuperTint;  break;
                    default:              tint = m_NormalTint; break;
                }
            }

            Color final = new Color(m_BaseColor.r * tint.r,
                                    m_BaseColor.g * tint.g,
                                    m_BaseColor.b * tint.b,
                                    m_BaseColor.a);

            m_Renderer.GetPropertyBlock(m_Block);

            // URP Lit 用 _BaseColor；有些自定义着色器只认 _Color，两个都写更保险
            m_Block.SetColor(kBaseColorID, final);
            m_Block.SetColor(kColorID, final);

            m_Renderer.SetPropertyBlock(m_Block);
        }

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmosSelected()
        {
            Collider col = m_SolidCollider;

            if (col == null)
            {
                Collider[] cols = GetComponentsInChildren<Collider>(true);

                for (int i = 0; i < cols.Length; i++)
                {
                    if (!cols[i].isTrigger) { col = cols[i]; break; }
                }
            }

            if (col == null)
            {
                return;
            }

            Bounds b = col.bounds;

            // 检测区（玩家会被弹起的范围）
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.28f);
            Gizmos.DrawCube(new Vector3(b.center.x, b.max.y + m_DetectHeight * 0.5f, b.center.z),
                            new Vector3(b.extents.x * 1.84f, m_DetectHeight, b.extents.z * 1.84f));

            // 弹起高度
            float h = BounceHeight();

            if (h > 0.01f)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                Vector3 from = new Vector3(b.center.x, b.max.y, b.center.z);
                Gizmos.DrawLine(from, from + Vector3.up * h);
                Gizmos.DrawCube(from + Vector3.up * h, Vector3.one * 0.12f);
            }
        }
    }
}
