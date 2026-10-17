using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 被**弹力泡泡**碰到时，沿指定轴移动一段距离。
    ///
    /// 【用法】
    ///   1. 挂到目标物体上（比如 chufaqi1）
    ///   2. 物体上要有 Collider，并且**勾上 Is Trigger**
    ///      （不勾也能工作，但会走实体碰撞那条路，泡泡可能被顶开）
    ///   3. 在 Inspector 里选轴、填距离和用时
    ///
    /// 【为什么必须勾 Is Trigger】
    /// 泡泡是靠物理飞过来的。如果这个物体是实心的，泡泡会先被弹开/停住，
    /// 然后才轮到脚本处理 —— 表现是"泡泡撞上去卡了一下才触发"。
    /// Trigger 是穿过去的那一瞬间就响应，手感干脆。
    ///
    /// 【只认弹力泡泡】
    /// 按你的要求，只有 <see cref="BubbleType.Bouncy"/> 碰到才触发。
    /// 浮粘和炸弹撞上去不理会 —— 这样这个方块就变成一道"要有弹力才能开"的机关。
    /// 想让任何泡泡都能触发，把 <see cref="m_OnlyBouncy"/> 取消勾选。
    ///
    /// 【移动是相对还是绝对】
    /// 相对 —— 记下触发时的位置，然后**加**上 axis × distance。
    /// 所以怎么摆、摆在哪都不用管，脚本不会覆盖你摆的初始位置。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/弹力泡泡触发移动（Bouncy Bubble Axis Mover）")]
    public sealed class BouncyBubbleAxisMover : MonoBehaviour
    {
        /// <summary>移动方向。</summary>
        public enum MoveAxis
        {
            X,
            Y,
            Z,
            NegX,
            NegY,
            NegZ,
        }

        [Header("触发条件")]
        [Tooltip("只认弹力泡泡。关掉则任何泡泡都能触发")]
        [SerializeField] private bool m_OnlyBouncy = true;

        [Tooltip("只触发一次。关掉则每次被撞都推一段（会越推越远）")]
        [SerializeField] private bool m_Once = true;

        [Header("移动")]
        [Tooltip("沿哪个轴移动")]
        [SerializeField] private MoveAxis m_Axis = MoveAxis.Z;

        [Tooltip("移动距离（米）。正负由上面的轴决定 —— 选 X 就是 +X 方向")]
        [SerializeField] private float m_Distance = 3f;

        [Tooltip("移动用多长时间（秒）。0 = 瞬间跳过去")]
        [SerializeField] private float m_Duration = 0.35f;

        [Tooltip("移动曲线。默认先快后慢（EaseOut），像被撞出去的")]
        [SerializeField] private AnimationCurve m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("回弹")]
        [Tooltip("推出去之后是否移回原位。关掉则停在目的地（原来的行为）")]
        [SerializeField] private bool m_ReturnToOrigin = true;

        [Tooltip("到达目的地后停多久再往回走（秒）")]
        [SerializeField] private float m_HoldDuration = 1.2f;

        [Tooltip("往回走用多长时间（秒）。0 = 瞬间弹回去")]
        [SerializeField] private float m_ReturnDuration = 0.5f;

        [Tooltip("回弹曲线。默认带一点过冲 —— 回到原位时略微越过再回到原位，像弹簧")]
        [SerializeField] private AnimationCurve m_ReturnCurve =
            new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.72f, 1.06f),   // 越过 6%
                new Keyframe(1f, 1f));

        [Header("表现（可选）")]
        [Tooltip("触发时是否把自己临时染一下色，用来确认「确实触发了」")]
        [SerializeField] private bool m_FlashOnTrigger = true;

        [SerializeField] private Color m_FlashColor = new Color(1f, 0.85f, 0.35f);

        [Tooltip("染色持续（秒）")]
        [SerializeField] private float m_FlashDuration = 0.25f;

        [Header("调试")]
        [Tooltip("打印触发日志（排查「为什么没动」时打开）")]
        [SerializeField] private bool m_Log = true;

        private bool m_Triggered;
        private bool m_Moving;
        private Renderer m_Renderer;
        private MaterialPropertyBlock m_Block;
        private Color m_BaseColor = Color.white;

        /// <summary>是否已经触发过。</summary>
        public bool HasTriggered { get { return m_Triggered; } }

        private void Awake()
        {
            m_Renderer = GetComponent<Renderer>();

            if (m_Renderer != null && m_Renderer.sharedMaterial != null)
            {
                m_BaseColor = m_Renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? m_Renderer.sharedMaterial.GetColor("_BaseColor")
                    : m_Renderer.sharedMaterial.color;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Moving)
            {
                return;
            }

            if (m_Once && m_Triggered)
            {
                return;
            }

            // 撞进来的是不是泡泡
            Bubble bubble = other.GetComponentInParent<Bubble>();

            if (bubble == null)
            {
                if (m_Log)
                {
                    Debug.Log("[BouncyBubbleAxisMover] \"" + name + "\" 被 \"" +
                              other.name + "\" 碰到，但它不是泡泡，忽略。", this);
                }

                return;
            }

            // 类型对不对
            if (m_OnlyBouncy && bubble.Type != BubbleType.Bouncy)
            {
                if (m_Log)
                {
                    Debug.Log("[BouncyBubbleAxisMover] \"" + name + "\" 被 " +
                              BubbleTypeSwitcher.DisplayNameOf(bubble.Type) +
                              " 碰到 —— 只认弹力泡泡，忽略。", this);
                }

                return;
            }

            Trigger();
        }

        // 没有勾 Is Trigger 时的兜底：走实体碰撞也能触发
        private void OnCollisionEnter(Collision collision)
        {
            if (m_Moving || (m_Once && m_Triggered))
            {
                return;
            }

            Bubble bubble = collision.collider.GetComponentInParent<Bubble>();

            if (bubble == null)
            {
                return;
            }

            if (m_OnlyBouncy && bubble.Type != BubbleType.Bouncy)
            {
                return;
            }

            Trigger();
        }

        /// <summary>外部也可以直接调它来触发（比如被别的事件联动）。</summary>
        public void Trigger()
        {
            if (m_Moving)
            {
                return;
            }

            if (m_Once && m_Triggered)
            {
                return;
            }

            m_Triggered = true;

            if (m_Log)
            {
                Debug.Log("[BouncyBubbleAxisMover] \"" + name + "\" 触发！沿 " +
                          m_Axis + " 移动 " + m_Distance + " 米" +
                          (m_ReturnToOrigin ? "，停留 " + m_HoldDuration + " 秒后回弹" : "") + "。",
                          this);
            }

            if (m_FlashOnTrigger)
            {
                StartCoroutine(FlashRoutine());
            }

            Vector3 direction = AxisToVector(m_Axis);
            Vector3 origin = transform.position;
            Vector3 destination = origin + direction * m_Distance;

            StartCoroutine(MoveRoutine(origin, destination));
        }

        /// <summary>
        /// 移出去 →（可选）停一会儿 → 回到原位。
        ///
        /// 【为什么整段都包在 m_Moving 里】
        /// 回弹过程中如果又被撞一下，不该重入（会把起点算错、从而越飘越远）。
        /// 所以 m_Moving 覆盖"出去 + 停留 + 回来"全程。
        /// </summary>
        private System.Collections.IEnumerator MoveRoutine(Vector3 origin, Vector3 destination)
        {
            m_Moving = true;

            // ---- 1. 移出去 ----
            if (m_Duration <= 0.001f)
            {
                transform.position = destination;
            }
            else
            {
                float elapsed = 0f;

                while (elapsed < m_Duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / m_Duration);
                    float eased = m_Curve != null ? m_Curve.Evaluate(t) : t;

                    // 直接改 position，不用 Rigidbody —— 这个物体应该是静态的。
                    // 如果以后要让它推动别的东西，再改成 Rigidbody.MovePosition。
                    transform.position = Vector3.LerpUnclamped(origin, destination, eased);
                    yield return null;
                }

                transform.position = destination;
            }

            // ---- 2. 停一会儿 ----
            if (m_ReturnToOrigin && m_HoldDuration > 0f)
            {
                yield return new WaitForSeconds(m_HoldDuration);
            }

            // ---- 3. 回弹 ----
            if (m_ReturnToOrigin)
            {
                if (m_ReturnDuration <= 0.001f)
                {
                    transform.position = origin;
                }
                else
                {
                    float elapsed = 0f;

                    while (elapsed < m_ReturnDuration)
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(elapsed / m_ReturnDuration);
                        float eased = m_ReturnCurve != null ? m_ReturnCurve.Evaluate(t) : t;

                        // 这里是 destination → origin 的插值，
                        // 所以曲线超过 1 时会**越过原位再回来** —— 就是弹簧回弹的过冲。
                        transform.position = Vector3.LerpUnclamped(destination, origin, eased);
                        yield return null;
                    }

                    transform.position = origin;
                }

                if (m_Log)
                {
                    Debug.Log("[BouncyBubbleAxisMover] \"" + name + "\" 已回弹到原位。", this);
                }
            }

            m_Moving = false;
        }

        private System.Collections.IEnumerator FlashRoutine()
        {
            SetTint(m_FlashColor);
            yield return new WaitForSeconds(m_FlashDuration);

            if (m_Triggered && m_Moving && m_Once)
            {
                // 移动中就不急着恢复，等移完再说
                while (m_Moving)
                {
                    yield return null;
                }
            }

            SetTint(m_BaseColor);
        }

        /// <summary>
        /// 改颜色。走 MaterialPropertyBlock 而不是改 material ——
        /// 白盒材质是共享的（所有方块用同一个 WB_*），
        /// 直接改会让**整片方块一起变色**。
        /// </summary>
        private void SetTint(Color color)
        {
            if (m_Renderer == null)
            {
                return;
            }

            if (m_Block == null)
            {
                m_Block = new MaterialPropertyBlock();
            }

            m_Renderer.GetPropertyBlock(m_Block);

            if (m_Renderer.sharedMaterial != null && m_Renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                m_Block.SetColor("_BaseColor", color);
            }
            else
            {
                m_Block.SetColor("_Color", color);
            }

            m_Renderer.SetPropertyBlock(m_Block);
        }

        private static Vector3 AxisToVector(MoveAxis axis)
        {
            switch (axis)
            {
                case MoveAxis.X: return Vector3.right;
                case MoveAxis.Y: return Vector3.up;
                case MoveAxis.Z: return Vector3.forward;
                case MoveAxis.NegX: return Vector3.left;
                case MoveAxis.NegY: return Vector3.down;
                default: return Vector3.back;
            }
        }
    }
}
