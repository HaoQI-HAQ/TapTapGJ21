using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 一踩上就开始倒计时、到点塌掉的地板。
    ///
    /// 【为什么要有它】
    /// 爬塔玩法里泡泡一碰障碍就破，玩家会掉到下面的楼层上。
    /// 如果那些楼层是永久的，玩家可以一直在下面磨 —— 没有时间压力，
    /// 而且破泡几乎没有代价。
    ///
    /// 地板会塌就同时解决两件事：
    ///   · **补救机会**：破了还能落地，重新攒一颗泡泡再上
    ///   · **时间压力**：落点只保 5 秒，必须在它消失前重新起飞
    ///
    /// 【怎么知道玩家踩上来了】
    /// 玩家的 <see cref="PlayerController"/> 是 CharacterController，
    /// 它撞到静态碰撞体会触发 OnCollisionEnter —— 不需要给地板加 Rigidbody。
    ///
    /// 【塌了之后自己怎么办】
    /// 往下掉一段再销毁。直接 Destroy 会"啪"地凭空消失；
    /// 掉一段才像塌方，玩家也能看清"是脚下这块没了"。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/塌陷地板（Collapsing Floor）")]
    public sealed class CollapsingFloor : MonoBehaviour
    {
        [Header("倒计时")]
        [Tooltip("玩家踩上来后多久开始塌（秒）")]
        [SerializeField] private float m_Delay = 5f;

        [Tooltip("踩上来是否立刻变色预警。关掉则毫无预兆，更难")]
        [SerializeField] private bool m_WarnOnStep = true;

        [Tooltip("预警时变成的颜色")]
        [SerializeField] private Color m_WarnColor = new Color(0.96f, 0.43f, 0.20f);

        [Tooltip("预警闪烁的频率（次/秒）")]
        [SerializeField] private float m_FlashRate = 3.5f;

        [Header("塌陷")]
        [Tooltip("塌下去的速度（米/秒）")]
        [SerializeField] private float m_FallSpeed = 5f;

        [Tooltip("掉多久后销毁（秒）")]
        [SerializeField] private float m_FallDuration = 1.6f;

        [Tooltip("塌之前先缩一下的时长（秒）。0 = 不缩，直接掉")]
        [SerializeField] private float m_ShrinkDuration = 0.25f;

        [Header("复用")]
        [Tooltip("塌完之后是否重新长回来。开了就是「踩一次塌一次」的循环机关")]
        [SerializeField] private bool m_Respawn = false;

        [Tooltip("重新长回来的等待时间（秒）")]
        [SerializeField] private float m_RespawnDelay = 3f;

        [Header("调试")]
        [SerializeField] private bool m_Log = false;

        private bool m_Armed;
        private bool m_Collapsed;

        private Renderer m_Renderer;
        private MaterialPropertyBlock m_Block;
        private Color m_BaseColor = Color.white;
        private Vector3 m_OriginalPosition;
        private Vector3 m_OriginalScale;

        /// <summary>倒计时还剩多少秒。没被踩过时是 m_Delay。</summary>
        public float TimeLeft { get; private set; }

        /// <summary>是否已经在倒计时（玩家踩上来了）。</summary>
        public bool IsArmed { get { return m_Armed; } }

        private void Awake()
        {
            m_OriginalPosition = transform.position;
            m_OriginalScale = transform.localScale;
            TimeLeft = m_Delay;

            m_Renderer = GetComponent<Renderer>();

            if (m_Renderer != null && m_Renderer.sharedMaterial != null)
            {
                m_BaseColor = m_Renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? m_Renderer.sharedMaterial.GetColor("_BaseColor")
                    : m_Renderer.sharedMaterial.color;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (m_Collapsed || m_Armed)
            {
                return;
            }

            // 只有"玩家踩上来"才启动，泡泡掉上来不算
            if (collision.collider.GetComponentInParent<PlayerController>() == null)
            {
                return;
            }

            Arm();
        }

        /// <summary>启动倒计时。也可以由别的脚本（比如陷阱）远程调用。</summary>
        public void Arm()
        {
            if (m_Armed || m_Collapsed)
            {
                return;
            }

            m_Armed = true;
            TimeLeft = m_Delay;

            if (m_Log)
            {
                Debug.Log("[CollapsingFloor] \"" + name + "\" 被踩上，" + m_Delay + " 秒后塌。", this);
            }
        }

        private void Update()
        {
            if (!m_Armed || m_Collapsed)
            {
                return;
            }

            TimeLeft -= Time.deltaTime;

            // 预警：越接近塌越快闪
            if (m_WarnOnStep)
            {
                float progress = 1f - Mathf.Clamp01(TimeLeft / Mathf.Max(m_Delay, 0.001f));
                float rate = Mathf.Lerp(m_FlashRate, m_FlashRate * 3f, progress);
                float k = Mathf.Abs(Mathf.Sin(Time.unscaledTime * rate * Mathf.PI));

                SetTint(Color.Lerp(m_BaseColor, m_WarnColor, k * 0.85f));
            }

            if (TimeLeft <= 0f)
            {
                StartCoroutine(CollapseRoutine());
            }
        }

        private System.Collections.IEnumerator CollapseRoutine()
        {
            m_Collapsed = true;

            if (m_Log)
            {
                Debug.Log("[CollapsingFloor] \"" + name + "\" 塌了。", this);
            }

            // 先缩一下，像被踩塌
            if (m_ShrinkDuration > 0f)
            {
                float t = 0f;

                while (t < m_ShrinkDuration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / m_ShrinkDuration);
                    transform.localScale = new Vector3(
                        Mathf.Lerp(m_OriginalScale.x, m_OriginalScale.x * 0.86f, k),
                        m_OriginalScale.y,
                        Mathf.Lerp(m_OriginalScale.z, m_OriginalScale.z * 0.86f, k));

                    yield return null;
                }
            }

            // 往下掉（连碰撞体一起掉，玩家会跟着失去支撑）
            float elapsed = 0f;

            while (elapsed < m_FallDuration)
            {
                elapsed += Time.deltaTime;
                transform.position += Vector3.down * (m_FallSpeed * Time.deltaTime);
                yield return null;
            }

            if (m_Respawn)
            {
                StartCoroutine(RespawnRoutine());
                yield break;
            }

            Destroy(gameObject);
        }

        private System.Collections.IEnumerator RespawnRoutine()
        {
            SetTint(m_BaseColor);
            gameObject.SetActive(false);

            yield return new WaitForSeconds(m_RespawnDelay);

            transform.position = m_OriginalPosition;
            transform.localScale = m_OriginalScale;
            gameObject.SetActive(true);

            m_Collapsed = false;
            m_Armed = false;
            TimeLeft = m_Delay;
        }

        /// <summary>
        /// 改颜色。走 MaterialPropertyBlock 而不是改 material ——
        /// 地板材质是共享的（所有白盒地板用同一个 WB_Ground），
        /// 直接改材质会让**整座塔的地板一起变红**。
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

        /// <summary>由搭建脚本调用，写入倒计时时长。</summary>
        public void Configure(float delay, bool respawn = false)
        {
            m_Delay = delay;
            m_Respawn = respawn;
            TimeLeft = delay;
        }
    }
}
