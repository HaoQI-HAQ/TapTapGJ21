using System;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡值：三种泡泡共用一条资源池。
    ///
    /// 吹泡泡要花值（按蓄力大小在 minCost ~ maxCost 之间插值，向下取整）；
    /// 破坏泡泡地形、或者用炸弹炸掉粘住的泡泡，都能回收一部分。
    ///
    /// 【给 UI 的接口】
    ///   Current / Max / Normalized   直接读，适合每帧刷新
    ///   OnChanged                    值变化时才回调，UI 订阅它就不用轮询
    ///   OnInsufficient               值不够时触发，UI 可以拿它弹提示
    ///
    /// 挂在 Player 上即可，零配置可跑。
    /// </summary>
    public class BubbleResourceManager : MonoBehaviour
    {
        [Header("泡泡值")]
        [Tooltip("资源上限")]
        [SerializeField] private int m_MaxResource = 100;

        [Tooltip("开局时的值。小于 0 表示直接拉满")]
        [SerializeField] private int m_StartResource = -1;
        private int m_Current;

        /// <summary>当前值。</summary>
        public int Current
        {
            get { return m_Current; }
        }

        /// <summary>上限。</summary>
        public int Max
        {
            get { return m_MaxResource; }
        }

        /// <summary>0~1，UI 画进度条直接用。</summary>
        public float Normalized
        {
            get { return m_MaxResource > 0 ? Mathf.Clamp01((float)m_Current / m_MaxResource) : 0f; }
        }

        /// <summary>值变化时回调：(当前值, 上限)。</summary>
        public event Action<int, int> OnChanged;

        /// <summary>值不够时回调：参数是还差多少。UI 可以拿它弹提示。</summary>
        public event Action<int> OnInsufficient;

        private void Awake()
        {
            m_Current = m_StartResource >= 0
                ? Mathf.Clamp(m_StartResource, 0, m_MaxResource)
                : m_MaxResource;
        }

        private void Start()
        {
            RaiseChanged();
        }


        /// <summary>现在付得起吗。</summary>
        public bool CanAfford(int amount)
        {
            return amount <= 0 || m_Current >= amount;
        }

        /// <summary>
        /// 扣值。不够就一点都不扣、返回 false。
        /// 蓄力途中涨到付不起时会走这里，调用方据此停止继续变大。
        /// </summary>
        public bool TryConsume(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (m_Current < amount)
            {
                if (OnInsufficient != null)
                {
                    OnInsufficient(amount - m_Current);
                }

                return false;
            }

            m_Current -= amount;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 回复值。破坏地形、炸掉粘住的泡泡时用。
        /// </summary>
        public void Refund(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            m_Current = Mathf.Min(m_MaxResource, m_Current + amount);
            RaiseChanged();
        }

        /// <summary>直接设值（调试或读档用）。</summary>
        public void SetCurrent(int value)
        {
            m_Current = Mathf.Clamp(value, 0, m_MaxResource);
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (OnChanged != null)
            {
                OnChanged(m_Current, m_MaxResource);
            }
        }
    }
}
