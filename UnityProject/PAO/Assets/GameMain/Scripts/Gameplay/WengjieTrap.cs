using System.Collections;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 联动陷阱：玩家踩进触发区后，按设定的延时**依次**引爆几个机关。
    ///
    /// 【为什么需要它】
    /// 单个机关只是障碍；真正「阴」的是组合：
    ///   先让脚下的地板开始塌（断退路）→ 再放扫杆把玩家往边缘赶（推下平台）
    ///   → 最后让巡逻刺墙扫过来（把泡泡打爆，榨干资源）。
    /// 这几个动作必须**有时间差**，早了玩家还没走到位，晚了就没威胁。
    /// 用这个组件把「谁先谁后」写死，比靠转速凑相位可靠得多。
    ///
    /// 【平时看不见】
    /// Awake 时把目标机关全部 disable，踩进去才逐个打开 —— 玩家第一次路过时
    /// 看不出这里有东西，符合「场景很阴」的要求。
    ///
    /// 只在 wengjie 用，不影响别的场景。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/联动陷阱（wengjie）")]
    public sealed class WengjieTrap : MonoBehaviour
    {
        public enum TrapAction
        {
            /// <summary>启动（enable）。适合扫杆 / 巡逻平台。</summary>
            Enable = 0,

            /// <summary>给 CollapsingFloor 上弦，它自己倒计时后塌。</summary>
            ArmFloor = 1,

            /// <summary>关掉（enable=false）。适合「先让路开着，事后把它关上」。</summary>
            Disable = 2,
        }

        [System.Serializable]
        public struct Step
        {
            [Tooltip("要操作的机关组件（RotatingHazard / WhiteboxPathPlatform / CollapsingFloor / BubblePopObstacle 都行）")]
            public MonoBehaviour target;

            [Tooltip("从触发到执行，等多少秒")]
            public float delay;

            public TrapAction action;
        }

        [Header("步骤（按 delay 从小到大排好）")]
        [SerializeField] private Step[] m_Steps;

        [Header("行为")]
        [Tooltip("只触发一次")]
        [SerializeField] private bool m_Once = true;

        [Tooltip("触发前把目标机关全部关掉，让玩家看不出这里有东西")]
        [SerializeField] private bool m_SilentUntilTriggered = true;

        [Tooltip("触发后多少秒把自己也关掉（0 = 一直留着）")]
        [SerializeField] private float m_SelfDisableAfter = 8f;

        private bool m_Fired;

        private void Awake()
        {
            if (!m_SilentUntilTriggered || m_Steps == null) return;

            for (int i = 0; i < m_Steps.Length; i++)
            {
                Step step = m_Steps[i];
                if (step.target == null) continue;

                // CollapsingFloor 靠 Arm 启动，不需要提前关；其它一律先关
                if (!(step.target is CollapsingFloor))
                {
                    step.target.enabled = false;
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Fired && m_Once) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;

            m_Fired = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            float elapsed = 0f;

            for (int i = 0; i < m_Steps.Length; i++)
            {
                Step step = m_Steps[i];
                if (step.target == null) continue;

                float wait = Mathf.Max(0f, step.delay - elapsed);
                if (wait > 0f) yield return new WaitForSeconds(wait);
                elapsed = Mathf.Max(elapsed, step.delay);

                switch (step.action)
                {
                    case TrapAction.Enable:
                        step.target.enabled = true;
                        break;

                    case TrapAction.Disable:
                        step.target.enabled = false;
                        break;

                    case TrapAction.ArmFloor:
                        CollapsingFloor floor = step.target as CollapsingFloor;
                        if (floor != null) floor.Arm();
                        break;
                }
            }

            if (m_SelfDisableAfter > 0f)
            {
                yield return new WaitForSeconds(m_SelfDisableAfter);
                gameObject.SetActive(false);
            }
        }
    }
}
