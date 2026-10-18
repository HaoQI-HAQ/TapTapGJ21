using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 可回收泡泡液：玩家碰到就回收成泡泡值（对应旁白 M-009/M-010 的资源回收教学）。
    /// 用完自己消失，不掉落、不重生 —— 这一点和「墙会恢复」形成对比。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    [AddComponentMenu("PAO/关卡/可回收泡泡液（Map0）")]
    public sealed class Map0DropletPickup : MonoBehaviour
    {
        [Tooltip("回收后恢复多少泡泡值")]
        [SerializeField, Min(0)] private int m_Amount = 6;

        [Tooltip("回收时是否把物体隐藏（关掉则只停判定）")]
        [SerializeField] private bool m_HideOnPickup = true;

        [SerializeField] private bool m_Log = true;

        private bool m_Taken;

        private void Reset() { GetComponent<SphereCollider>().isTrigger = true; }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Taken) return;
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            BubbleResourceManager resource = player.GetComponent<BubbleResourceManager>();
            if (resource == null) return;

            m_Taken = true;
            resource.Refund(m_Amount);
            if (m_Log) Debug.Log("[Map0] 回收到泡泡液 +" + m_Amount + "（当前 " + resource.Current + "）");
            if (m_HideOnPickup) gameObject.SetActive(false);
        }

        public void Configure(int amount)
        {
            m_Amount = Mathf.Max(0, amount);
        }
    }
}
