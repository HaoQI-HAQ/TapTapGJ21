using UnityEngine;

namespace PAO
{
    /// <summary>
    /// Map0 教程启动兜底：
    ///   1) 玩家有时带着「CharacterController 未启用」的状态进场景（UIRuntime/读档流程切换导致），
    ///      不启用的话按 WASD 完全不动 —— 这里每 0.5 秒检查一次并补上；
    ///   2) 玩家站在原地不动时，触发区不会收到 OnTriggerEnter，第一句旁白会一直不响；
    ///      这里在开场直接请求 M-001，保证「有声音、有字幕」。
    /// 只挂在 Map0_Tutorial/Systems 上，不影响其它场景。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/第一章启动兜底（Map0）")]
    public sealed class Map0PlayerFix : MonoBehaviour
    {
        [SerializeField] private string m_OpeningBeat = "M-001";
        [SerializeField] private bool m_Log = true;

        private float m_NextCheck;
        private bool m_OpeningRequested;

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

            if (!m_OpeningRequested && !string.IsNullOrEmpty(m_OpeningBeat))
            {
                Map0NarrationDirector director = Map0NarrationDirector.Instance;
                if (director != null && !director.HasPlayed(m_OpeningBeat))
                {
                    m_OpeningRequested = true;
                    director.Request(m_OpeningBeat);
                    if (m_Log) Debug.Log("[Map0] 开场旁白已请求：" + m_OpeningBeat);
                }
            }
        }
    }
}
