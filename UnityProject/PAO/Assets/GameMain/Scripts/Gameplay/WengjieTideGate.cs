using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 只在 wengjie 场景里用的「潮水开关」：玩家走进触发区，潮水才开始涨。
    ///
    /// 【为什么需要它】
    /// BubbleTideDirector 的 StartDelay 上限只有 60 秒，而中期关卡要求「玩家什么时候
    /// 踏进峡谷，潮水什么时候开始追」。所以这里不靠时间，靠触发。
    ///
    /// 【为什么单独一个文件】
    /// 不改任何共享脚本，也不影响别的场景 —— 别的场景里没有这个物体，它就永远不会跑。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [AddComponentMenu("PAO/关卡/潮水开关（wengjie）")]
    public sealed class WengjieTideGate : MonoBehaviour
    {
        [Tooltip("触发后等多久开始涨（秒），给玩家一点反应时间")]
        [SerializeField] private float m_Delay = 1.2f;

        [Tooltip("触发一次就销毁自己")]
        [SerializeField] private bool m_Once = true;

        private bool m_Armed = true;
        private float m_Timer = -1f;

        private void Reset()
        {
            // 挂上组件时自动把碰撞体设成触发器，免得忘了勾
            Collider own = GetComponent<Collider>();
            if (own != null) own.isTrigger = true;
        }

        private void Update()
        {
            if (m_Timer < 0f) return;

            m_Timer -= Time.deltaTime;
            if (m_Timer > 0f) return;

            m_Timer = -1f;
            PAO.BubbleTide.BubbleTideDirector director = PAO.BubbleTide.BubbleTideDirector.Instance;
            if (director != null) director.StartTide();
            else Debug.LogWarning("[WengjieTideGate] 场景里没有 BubbleTideDirector，潮水不会开始。", this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!m_Armed) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;

            m_Armed = false;
            m_Timer = Mathf.Max(0f, m_Delay);

            if (m_Once) gameObject.SetActive(false);
        }
    }
}
