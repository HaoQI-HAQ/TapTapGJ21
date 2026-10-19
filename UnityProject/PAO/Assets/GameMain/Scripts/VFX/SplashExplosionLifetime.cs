using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 爆炸特效的寿命：到点销毁，并把 <see cref="SplashExplosionVFX"/> 的并发计数还回去。
    ///
    /// 【为什么不用预制体自带的 Stop Action = Destroy】
    /// 这个预制体有 7 个粒子系统，各自的时长并不完全一致。
    /// 谁先播完谁就销毁的话，剩下没播完的会被连根拔掉，尾巴被砍断。
    /// 统一在这儿按「最长的那条 + 余量」计时更稳。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SplashExplosionLifetime : MonoBehaviour
    {
        private float m_EndTime;
        private bool m_Released;

        /// <summary>开始计时。lifeTime 传 0 或负数时退回 1.8 秒。</summary>
        public void Begin(float lifeTime)
        {
            if (lifeTime <= 0.01f)
            {
                lifeTime = 1.8f;
            }

            m_EndTime = Time.time + lifeTime;
        }

        private void Update()
        {
            if (Time.time < m_EndTime)
            {
                return;
            }

            ReleaseOnce();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // 场景卸载、或者被别的逻辑提前 Destroy 时也要还计数，
            // 否则并发上限会被「漏」光，之后再也不会播爆炸特效。
            ReleaseOnce();
        }

        private void ReleaseOnce()
        {
            if (m_Released)
            {
                return;
            }

            m_Released = true;
            SplashExplosionVFX.Release();
        }
    }
}
