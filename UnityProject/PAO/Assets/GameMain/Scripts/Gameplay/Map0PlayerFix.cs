using UnityEngine;

namespace PAO
{
    /// <summary>
    /// Map0 教程启动兜底：
    ///   玩家有时带着「CharacterController 未启用」的状态进场景（UIRuntime/读档流程切换导致），
    ///   不启用的话按 WASD 完全不动 —— 这里每 0.5 秒检查一次并补上。
    /// 旁白已精简为只保留关键机制教学（M-005 起），开场不再强播 M-001 语音。
    /// 只挂在 Map0_Tutorial/Systems 上，不影响其它场景。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/第一章启动兜底（Map0）")]
    public sealed class Map0PlayerFix : MonoBehaviour
    {
        [SerializeField] private bool m_Log = true;

        private float m_NextCheck;

        private void Update()
        {
            if (Time.time < m_NextCheck) return;
            m_NextCheck = Time.time + 0.5f;

            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller != null && !controller.enabled)
                {
                    controller.enabled = true;
                    if (m_Log) Debug.Log("[Map0] CharacterController 未启用，已自动补上。");
                }
            }
        }
    }
}
