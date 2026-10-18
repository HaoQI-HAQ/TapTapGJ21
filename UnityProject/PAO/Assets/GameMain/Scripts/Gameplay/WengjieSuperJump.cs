using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 超级跳（推弹力泡泡本体）：不推玩家，只给所骑泡泡一个向上速度，
    /// 玩家由骑乘系统自然带上去，完全走游戏原本的机制。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/超级跳·推泡泡（仅弹力泡）")]
    public sealed class WengjieSuperJump : MonoBehaviour
    {
        [Tooltip("给泡泡的向上速度（米/秒）")]
        [SerializeField] private float m_Speed = 28f;

        [Tooltip("冷却（秒）")]
        [SerializeField] private float m_Cooldown = 1.5f;

        [Tooltip("玩家周围多远内找泡泡")]
        [SerializeField] private float m_Radius = 5f;

        private float m_NextTime;

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Time.time < m_NextTime) return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            Rigidbody bubble = FindBouncyBody(player.transform.position);
            if (bubble == null) return;

            m_NextTime = Time.time + m_Cooldown;

            Vector3 v = bubble.velocity;
            v.y = m_Speed;
            bubble.velocity = v;
        }

        private Rigidbody FindBouncyBody(Vector3 center)
        {
            Collider[] near = Physics.OverlapSphere(center, m_Radius, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < near.Length; i++)
            {
                if (near[i] == null) continue;
                Rigidbody rb = near[i].attachedRigidbody;
                if (rb == null) continue;

                Component[] parts = rb.GetComponentsInParent<Component>(true);
                for (int k = 0; k < parts.Length; k++)
                {
                    if (parts[k] == null) continue;
                    if (parts[k].GetType().Name.ToLowerInvariant().Contains("bouncy")) return rb;
                }
            }
            return null;
        }
    }
}