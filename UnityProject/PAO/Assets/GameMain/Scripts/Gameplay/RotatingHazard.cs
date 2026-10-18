using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 绕着某个轴匀速转的障碍物（爬塔里的"扫过来的横杆"）。
    ///
    /// 【为什么单独一个组件，而不是塞进 BubblePopObstacle】
    /// 破裂判定和运动方式是两件独立的事：
    /// 尖刺群不转但要破泡，横杆转也要破泡，以后可能还有"上下移动的闸门"。
    /// 分成两个组件就能自由组合 —— 转不转由这个决定，
    /// 碰了会不会破由 <see cref="BubblePopObstacle"/> 决定。
    ///
    /// 【为什么挂在空的 pivot 上，而不是横杆自己身上】
    /// 横杆是个长条，绕自己的中心转会原地打转；
    /// 挂在 pivot 上才是"绕井道中心扫过去"，那才是要的效果。
    ///
    /// ============================================================
    /// 【被炸弹炸到会停摆】
    /// ============================================================
    /// 订阅 <see cref="Bubble.Exploded"/> 静态事件，被爆炸波及时
    /// **停止旋转 20 秒**（时长可调），时间到自动恢复。
    ///
    /// 这是给玩家的一个"反制窗口"：把炸弹泡泡送到横杆附近引爆，
    /// 横杆停摆，这段时间可以安全穿过去。
    ///
    /// 【判定距离怎么算】
    /// 不能拿 pivot 到爆炸中心的距离 —— 横杆是伸出去几米的长臂，
    /// 炸在杆子末端时离 pivot 很远，但明明已经炸到杆子了。
    /// 所以用 <c>Collider.ClosestPoint</c> 取**障碍本体上离爆炸最近的点**，
    /// 再算这个点到爆炸中心的距离。爆炸中心落在杆子内部时距离直接为 0。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/旋转障碍（Rotating Hazard）")]
    public sealed class RotatingHazard : MonoBehaviour
    {
        [Header("运动")]
        [Tooltip("绕哪个轴转（本地空间）")]
        [SerializeField] private Vector3 m_Axis = Vector3.up;

        [Tooltip("转速（度/秒）")]
        [SerializeField] private float m_Speed = 45f;

        [Tooltip("是否来回摆动而不是一直转。摆动更难预测，但不会把人绕晕")]
        [SerializeField] private bool m_PingPong = false;

        [Tooltip("摆动模式下的最大转角（度）")]
        [SerializeField] private float m_SwingAngle = 60f;

        // ---- 被炸停摆 ----

        [Header("被炸弹炸到")]
        [Tooltip("被炸弹爆炸波及时停止旋转")]
        [SerializeField] private bool m_StunByBomb = true;

        [Tooltip("停止旋转多少秒")]
        [SerializeField] private float m_StunDuration = 20f;

        [Tooltip("爆炸判定额外范围（米）。爆炸半径已经算进去了，这里只是余量")]
        [SerializeField] private float m_BlastReachBonus = 0.5f;

        [Tooltip("停摆时染一下色，让玩家一眼看出「现在安全」")]
        [SerializeField] private bool m_TintWhileStunned = true;

        [Tooltip("停摆时的染色")]
        [SerializeField] private Color m_StunnedTint = new Color(0.55f, 0.78f, 1.0f);

        [Header("调试")]
        [Tooltip("打印被炸停 / 恢复的日志")]
        [SerializeField] private bool m_Log = true;

        // ---- 运行时 ----

        private float m_Direction = 1f;
        private float m_Swung;

        /// <summary>剩余停摆时间（秒）。0 = 正在转。</summary>
        private float m_StunLeft;

        private Renderer[] m_Renderers;
        private Color[] m_BaseColors;
        private MaterialPropertyBlock m_Block;

        private static readonly int kBaseColorID = Shader.PropertyToID("_BaseColor");
        private static readonly int kColorID = Shader.PropertyToID("_Color");

        /// <summary>当前的转速（度/秒）。带符号；停摆时为 0。</summary>
        public float CurrentSpeed
        {
            get { return IsStunned ? 0f : m_Speed * (m_PingPong ? m_Direction : 1f); }
        }

        /// <summary>是否正在停摆。</summary>
        public bool IsStunned { get { return m_StunLeft > 0f; } }

        /// <summary>停摆还剩多少秒。</summary>
        public float StunRemaining { get { return m_StunLeft; } }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            CacheRenderers();
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

        private void CacheRenderers()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_BaseColors = new Color[m_Renderers.Length];
            m_Block = new MaterialPropertyBlock();

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                Color c = Color.white;
                Material mat = m_Renderers[i].sharedMaterial;

                if (mat != null)
                {
                    if (mat.HasProperty(kBaseColorID))
                    {
                        c = mat.GetColor(kBaseColorID);
                    }
                    else if (mat.HasProperty(kColorID))
                    {
                        c = mat.GetColor(kColorID);
                    }
                }

                m_BaseColors[i] = c;
            }
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            // 停摆倒计时
            if (m_StunLeft > 0f)
            {
                m_StunLeft -= Time.deltaTime;

                if (m_StunLeft > 0f)
                {
                    return;      // 还在停摆，这一帧不转
                }

                m_StunLeft = 0f;
                ApplyTint(false);

                if (m_Log)
                {
                    Debug.Log("[RotatingHazard] \"" + name + "\" 停摆结束，恢复旋转。", this);
                }
            }

            if (m_Speed == 0f)
            {
                return;
            }

            float delta = m_Speed * Time.deltaTime;

            if (m_PingPong)
            {
                // 摆到边界就反向
                if (m_Swung + delta * m_Direction > m_SwingAngle && m_Direction > 0f)
                {
                    m_Direction = -1f;
                }
                else if (m_Swung + delta * m_Direction < -m_SwingAngle && m_Direction < 0f)
                {
                    m_Direction = 1f;
                }

                float step = delta * m_Direction;
                m_Swung += step;
                transform.Rotate(m_Axis, step, Space.Self);
            }
            else
            {
                // 用 Time.deltaTime 走正常时间 —— 暂停时障碍也该停
                transform.Rotate(m_Axis, delta, Space.Self);
            }
        }

        // ==================== 被炸弹炸到 ====================

        private void HandleBubbleExploded(Vector3 center, float radius)
        {
            if (!m_StunByBomb)
            {
                return;
            }

            float distance = DistanceToExplosion(center);
            float reach = radius + m_BlastReachBonus;

            if (distance > reach)
            {
                return;
            }

            Stun(m_StunDuration);

            if (m_Log)
            {
                Debug.Log("[RotatingHazard] \"" + name + "\" 被炸停 " +
                          m_StunDuration.ToString("F1") + " 秒" +
                          "（距离 " + distance.ToString("F2") + "m，" +
                          "判定 " + reach.ToString("F2") + "m）", this);
            }
        }

        /// <summary>
        /// 爆炸中心到**障碍本体**的距离。
        ///
        /// 用 Collider.ClosestPoint 而不是 pivot 位置：
        /// 横杆是伸出去几米的长臂，炸在末端时离 pivot 很远，
        /// 但明明已经炸到杆子上了。用本体上最近的点才准。
        /// 爆炸中心落在碰撞体内部时，ClosestPoint 会直接返回它自己，距离为 0。
        /// </summary>
        private float DistanceToExplosion(Vector3 center)
        {
            Collider col = GetComponentInChildren<Collider>();

            if (col != null && col.enabled)
            {
                Vector3 closest = col.ClosestPoint(center);
                return Vector3.Distance(center, closest);
            }

            // 没有碰撞体就退回包围盒 / 位置估算
            Renderer r = GetComponentInChildren<Renderer>();

            if (r != null)
            {
                Vector3 closest = r.bounds.ClosestPoint(center);
                return Vector3.Distance(center, closest);
            }

            return Vector3.Distance(center, transform.position);
        }

        /// <summary>
        /// 让本障碍停摆指定秒数。外部脚本（比如别的机关）也可以调。
        /// 重复调用取较长的那个 —— 连炸两次不会缩短时间。
        /// </summary>
        public void Stun(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            bool wasStunned = m_StunLeft > 0f;

            m_StunLeft = Mathf.Max(m_StunLeft, seconds);

            if (!wasStunned)
            {
                ApplyTint(true);
            }
        }

        // ==================== 染色 ====================

        private void ApplyTint(bool stunned)
        {
            if (!m_TintWhileStunned || m_Renderers == null || m_Block == null)
            {
                return;
            }

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                Renderer r = m_Renderers[i];

                if (r == null)
                {
                    continue;
                }

                Color baseColor = m_BaseColors[i];

                Color final = stunned
                    ? new Color(baseColor.r * m_StunnedTint.r,
                                baseColor.g * m_StunnedTint.g,
                                baseColor.b * m_StunnedTint.b,
                                baseColor.a)
                    : baseColor;

                r.GetPropertyBlock(m_Block);

                // URP Lit 用 _BaseColor；有些自定义着色器只认 _Color，两个都写更保险
                m_Block.SetColor(kBaseColorID, final);
                m_Block.SetColor(kColorID, final);

                r.SetPropertyBlock(m_Block);
            }
        }

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmosSelected()
        {
            if (!m_StunByBomb)
            {
                return;
            }

            // 判定范围只是个示意（真正的半径要加上爆炸半径），
            // 画在这里是为了让人直观看到"炸多远能停它"。
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, m_BlastReachBonus));
        }

        /// <summary>由搭建脚本调用。</summary>
        public void Configure(Vector3 axis, float speed, bool pingPong = false, float swingAngle = 60f)
        {
            m_Axis = axis;
            m_Speed = speed;
            m_PingPong = pingPong;
            m_SwingAngle = swingAngle;
        }
    }
}
