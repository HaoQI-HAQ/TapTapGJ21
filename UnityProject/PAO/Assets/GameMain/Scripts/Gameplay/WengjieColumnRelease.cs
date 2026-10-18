using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 只在 wengjie 用的「泡柱释放器」：楔子（TerrainBubble）没了，这根柱子才开始受物理控制。
    ///
    /// 【为什么不直接让它自由刚体】
    /// 8 米高、1.1 米粗的柱子在 Unity 里站不稳，开局就会自己抖、自己倒。
    /// 所以它平时是 kinematic（焊死），只有楔子被填满炸掉之后才交给物理 ——
    /// 这样「倒」这个瞬间是可控的、也是每次一样的，截图和演示都不会翻车。
    ///
    /// 【为什么不改 TerrainBubble 加事件】
    /// 面板里的引用被 Destroy 之后会变成 null，每帧判一次就够，不用动共享脚本。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/泡柱释放器（wengjie）")]
    public sealed class WengjieColumnRelease : MonoBehaviour
    {
        [Tooltip("楔子。它被填满炸掉之后，柱子才会倒")]
        [SerializeField] private TerrainBubble m_Chock;

        [Tooltip("要放倒的柱子刚体")]
        [SerializeField] private Rigidbody m_Column;

        [Tooltip("楔子消失后等多久再倒（秒）。留一点点时间让玩家看清")]
        [SerializeField] private float m_Delay = 0.25f;

        [Tooltip("倒下去时额外给的角速度（弧度/秒）。1~2 比较自然，别超过 3")]
        [SerializeField] private float m_KickTorque = 1.2f;

        [Tooltip("倒之前是否锁死柱子（强烈建议开，不然它会自己抖）")]
        [SerializeField] private bool m_LockUntilReleased = true;

        private float m_Timer = -1f;
        private bool m_Released;

        private void Awake()
        {
            if (m_LockUntilReleased && m_Column != null)
            {
                m_Column.isKinematic = true;
                m_Column.useGravity = false;
            }
        }

        private void Update()
        {
            if (m_Released) return;

            if (m_Chock != null) return;

            if (m_Timer < 0f)
            {
                m_Timer = Mathf.Max(0f, m_Delay);
            }

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f) return;

            Release();
        }

        private void Release()
        {
            m_Released = true;

            if (m_Column == null)
            {
                Debug.LogWarning("[WengjieColumnRelease] 没有指定柱子刚体。", this);
                return;
            }

            m_Column.useGravity = true;
            m_Column.isKinematic = false;
            m_Column.WakeUp();

            if (Mathf.Abs(m_KickTorque) > 0.01f)
            {
                m_Column.AddTorque(Vector3.forward * m_KickTorque, ForceMode.VelocityChange);
            }
        }
    }
}
