using System.Collections;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 接近引爆：玩家走进触发范围 → 短暂延迟 → 炸开 + 把玩家弹飞。
    ///
    /// 【为什么需要它】
    /// TerrainBubble 只在「被弹力泡填满」时才炸，对玩家完全没有反应 ——
    /// 玩家可以贴着它站半天，手感是「这是个死物」。
    /// 这个组件把「某几颗泡」变成活的：靠近就炸，而且炸的时候把人掀出去。
    ///
    /// 【为什么弹飞要调 PlayerController.Launch】
    /// 玩家是 CharacterController，不是刚体 —— 爆炸的 AddExplosionForce 推不动他。
    /// PlayerController.Launch 是唯一能让玩家「被抛」的入口（出泡冲刺也走它）。
    ///
    /// 只在 wengjie 用，不影响别的场景。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/接近引爆（wengjie）")]
    public sealed class WengjieProximityBurst : MonoBehaviour
    {
        [Header("触发")]
        [Tooltip("玩家进入后多久炸（秒）。0.12~0.2 手感最好：给玩家一帧的「糟了」")]
        [SerializeField] private float m_Delay = 0.15f;

        [Tooltip("只炸一次")]
        [SerializeField] private bool m_Once = true;

        [Header("弹飞")]
        [Tooltip("弹飞初速度（米/秒）")]
        [SerializeField] private float m_LaunchSpeed = 20f;

        [Tooltip("弹飞持续（秒）。速度 × 时间 ≈ 抛出去多远")]
        [SerializeField] private float m_LaunchDuration = 0.45f;

        [Tooltip("向上偏置（0 = 纯水平，1 = 45° 左右）。太小会贴着地滑，太大会原地起跳")]
        [SerializeField] private float m_UpBias = 0.55f;

        [Header("爆炸")]
        [Tooltip("要一起炸掉的地形泡。留空则只弹飞，不炸自己")]
        [SerializeField] private TerrainBubble m_Target;

        [Tooltip("炸完之后是否销毁自己")]
        [SerializeField] private bool m_DestroySelf = true;

        [Tooltip("多久后重新上膛（秒）。0 = 只炸一次；巡逻雷设 2~4 秒")]
        [SerializeField] private float m_RearmSeconds = 0f;

        private bool m_Fired;

        private void Reset()
        {
            Collider own = GetComponent<Collider>();
            if (own != null) own.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Fired && m_Once) return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            m_Fired = true;
            StartCoroutine(Burst(player));
        }

        private IEnumerator Burst(PlayerController player)
        {
            if (m_Delay > 0f) yield return new WaitForSeconds(m_Delay);

            // 方向：从爆心指向玩家，再抬起来一点
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.0001f) toPlayer = player.transform.forward;

            Vector3 direction = (toPlayer.normalized + Vector3.up * m_UpBias).normalized;
            player.Launch(direction, m_LaunchSpeed, m_LaunchDuration);

            if (m_Target != null)
            {
                m_Target.Break(TerrainBubble.BreakCause.Bomb);
            }

            if (m_DestroySelf)
            {
                gameObject.SetActive(false);
                yield break;
            }

            // 巡逻雷：炸完不消失，等几秒重新上膛
            if (m_RearmSeconds > 0f)
            {
                yield return new WaitForSeconds(m_RearmSeconds);
                m_Fired = false;
            }
        }
    }
}
