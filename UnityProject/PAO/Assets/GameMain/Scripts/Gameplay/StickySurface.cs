using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 可粘附地形：给场景里的地形挂上这个脚本后，
    /// 浮粘泡泡在存活时间内碰到它就会牢牢粘在原地，不再移动。
    ///
    /// 只是个标记组件，本身不做任何事，判断逻辑在 Bubble.cs 里。
    /// 需要和碰撞体一起用。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StickySurface : MonoBehaviour
    {
        [Header("粘附")]
        [Tooltip("泡泡粘上来时是否对齐到接触点（关掉则停在原地）")]
        [SerializeField] private bool m_SnapToContactPoint = true;

        [Tooltip("粘附时是否把泡泡的旋转也固定住")]
        [SerializeField] private bool m_FreezeRotation = true;

        /// <summary>粘上去时是否对齐接触点。</summary>
        public bool SnapToContactPoint
        {
            get { return m_SnapToContactPoint; }
        }

        /// <summary>粘上去时是否冻结旋转。</summary>
        public bool FreezeRotation
        {
            get { return m_FreezeRotation; }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.6f);

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