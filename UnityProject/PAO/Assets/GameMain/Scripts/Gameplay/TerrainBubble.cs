using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 地形泡泡：场景里的固定容器，被发射出去的弹力泡泡填充。
    ///
    /// 和「吸收即销毁」不同，打进来的弹力泡泡会保留本体留在里面，
    /// 持续朝球心收拢、互相挤在一起；地形被填满炸掉时，
    /// 里面所有泡泡会跟着一起炸掉。
    ///
    /// 挂在场景中当作地形的泡泡物体上，需要带碰撞体。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TerrainBubble : MonoBehaviour
    {
        [Header("容积")]
        [Tooltip("容积上限。被填满就炸")]
        [SerializeField] private int m_Capacity = 5;

        [Header("收拢")]
        [Tooltip("打进来的泡泡被吸向球心的力度。越大收得越快")]
        [SerializeField] private float m_GatherForce = 12f;

        [Tooltip("泡泡进到里面后额外增加的阻力，让它尽快安定、不来回弹")]
        [SerializeField] private float m_ContainedDrag = 5f;

        [Tooltip("里面的泡泡之间是否互相粘住（用固定关节连起来）")]
        [SerializeField] private bool m_StickContainedBubbles = true;

        [Header("填充反馈")]
        [Tooltip("是否根据填充进度染色，方便肉眼看出快满了")]
        [SerializeField] private bool m_TintByFill = true;

        [Tooltip("空的时候的颜色")]
        [SerializeField] private Color m_EmptyColor = new Color(0.62f, 0.62f, 0.68f, 1f);

        [Tooltip("快满时的颜色")]
        [SerializeField] private Color m_FullColor = new Color(1f, 0.45f, 0.35f, 1f);

        [Header("爆开")]
        [Tooltip("填满后延迟多久消失（秒），0 为立即")]
        [SerializeField] private float m_PopDelay = 0f;

        private int m_CurrentFill;
        private bool m_IsExploding;      // 正在爆炸中，用来防止连锁时重复引爆自己
        private Renderer m_Renderer;
        private MaterialPropertyBlock m_PropertyBlock;
        private Collider m_OwnCollider;

        // 已经装进来的泡泡。地形炸掉时要连带它们一起处理
        private readonly List<Bubble> m_Contained = new List<Bubble>();

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");   // URP
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");           // 内置管线

        /// <summary>容积上限。</summary>
        public int Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>当前已填充的容积。</summary>
        public int CurrentFill
        {
            get { return m_CurrentFill; }
        }

        /// <summary>是否已经填满。</summary>
        public bool IsFull
        {
            get { return m_CurrentFill >= m_Capacity; }
        }

        /// <summary>填充进度 0~1，UI 想画进度条可以读它。</summary>
        public float FillProgress
        {
            get { return m_Capacity > 0 ? Mathf.Clamp01((float)m_CurrentFill / m_Capacity) : 1f; }
        }

        /// <summary>球心坐标。里面泡泡的收拢目标就是它。</summary>
        public Vector3 Center
        {
            get { return transform.position; }
        }

        /// <summary>收拢力度，供里面的泡泡读取。</summary>
        public float GatherForce
        {
            get { return m_GatherForce; }
        }

        /// <summary>里面泡泡的额外阻力。</summary>
        public float ContainedDrag
        {
            get { return m_ContainedDrag; }
        }

        /// <summary>里面泡泡之间要不要互相粘住。</summary>
        public bool StickContainedBubbles
        {
            get { return m_StickContainedBubbles; }
        }

        private void Awake()
        {
            m_Renderer = GetComponentInChildren<Renderer>();
            m_PropertyBlock = new MaterialPropertyBlock();
            m_OwnCollider = GetComponent<Collider>();
            RefreshTint();
        }

        /// <summary>
        /// 吸收一个泡泡带来的容积。返回 true 表示因此被填满。
        /// </summary>
        public bool Absorb(int amount)
        {
            if (amount <= 0 || IsFull)
            {
                return false;
            }

            m_CurrentFill = Mathf.Min(m_CurrentFill + amount, m_Capacity);
            RefreshTint();

            if (!IsFull)
            {
                return false;
            }

            Explode();
            return true;
        }

        /// <summary>
        /// 把打进来的弹力泡泡收进容器：不销毁它，让它留在里面朝球心收拢。
        /// </summary>
        public void ContainBubble(Bubble bubble)
        {
            if (bubble == null || m_Contained.Contains(bubble))
            {
                return;
            }

            m_Contained.Add(bubble);
            bubble.OnContained(this);
        }

        /// <summary>
        /// 地形炸掉：里面的泡泡一起炸，然后自己消失。
        /// </summary>
        public void Explode()
        {
            // 防重入：里面的炸弹泡泡爆炸时会反过来引爆周围地形，
            // 而自己此时还没销毁，会被再次命中，加个闸挡住
            if (m_IsExploding)
            {
                return;
            }

            m_IsExploding = true;

            // 先让里面的泡泡全部爆掉。用倒序遍历，因为 Explode 会改动场景对象
            for (int i = m_Contained.Count - 1; i >= 0; i--)
            {
                Bubble contained = m_Contained[i];
                if (contained != null)
                {
                    contained.Explode();
                }
            }

            m_Contained.Clear();

            if (m_PopDelay > 0f)
            {
                Destroy(gameObject, m_PopDelay);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 按填充进度染色，让玩家能看出哪个快满了。
        /// 用 MaterialPropertyBlock 是为了不复制材质实例。
        /// </summary>
        private void RefreshTint()
        {
            if (!m_TintByFill || m_Renderer == null || m_PropertyBlock == null)
            {
                return;
            }

            Color color = Color.Lerp(m_EmptyColor, m_FullColor, FillProgress);

            m_Renderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(s_BaseColorId, color);
            m_PropertyBlock.SetColor(s_ColorId, color);
            m_Renderer.SetPropertyBlock(m_PropertyBlock);
        }

        /// <summary>
        /// 让刚进来的泡泡忽略与地形的碰撞，否则它会一直被外壳弹开、挤不进去。
        /// </summary>
        public void IgnoreCollisionWith(Collider bubbleCollider)
        {
            if (bubbleCollider == null)
            {
                return;
            }

            // 连着子物体上的碰撞体一起忽略。
            // 只忽略自己那一个的话，泡泡会被漏掉的那些弹开，
            // 表现就是刚进去又被顶出来、来回闪。
            Collider[] ownColliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < ownColliders.Length; i++)
            {
                if (ownColliders[i] != null)
                {
                    Physics.IgnoreCollision(bubbleCollider, ownColliders[i], true);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.3f, 0.7f);

            Collider ownCollider = GetComponent<Collider>();
            if (ownCollider != null)
            {
                Gizmos.DrawWireCube(ownCollider.bounds.center, ownCollider.bounds.size);
            }
            else
            {
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }
    }
}
