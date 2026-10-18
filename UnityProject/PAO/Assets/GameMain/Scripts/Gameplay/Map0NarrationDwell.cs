using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 第一章旁白「停留」触发区：玩家在区域内停留够久才触发（H-001 看内壁刻痕、B-003 看墙恢复）。
    /// 只在玩家确实站着不动时计时，避免只是路过就播。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("PAO/关卡/第一章旁白停留区（Map0）")]
    public sealed class Map0NarrationDwell : MonoBehaviour
    {
        [Tooltip("旁白段 id")]
        [SerializeField] private string m_BeatId;

        [Tooltip("前置段（可空）")]
        [SerializeField] private string m_Requires;

        [Tooltip("需要停留多少秒")]
        [SerializeField, Min(0.5f)] private float m_Seconds = 5f;

        [Tooltip("玩家移动速度低于这个值才算「停留」")]
        [SerializeField, Min(0.01f)] private float m_MoveThreshold = 0.2f;

        [SerializeField] private bool m_Log = false;

        private float m_Timer;
        private bool m_Inside;
        private bool m_Fired;
        private PlayerController m_Player;

        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

        private void OnTriggerEnter(Collider other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            m_Player = player;
            m_Inside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            m_Inside = false;
            m_Timer = 0f;
        }

        private void Update()
        {
            if (m_Fired || !m_Inside || m_Player == null) return;
            Map0NarrationDirector director = Map0NarrationDirector.Instance;
            if (director == null || director.IsBusy) { m_Timer = 0f; return; }
            if (!string.IsNullOrEmpty(m_Requires) && !director.HasPlayed(m_Requires)) return;

            CharacterController controller = m_Player.GetComponent<CharacterController>();
            float speed = controller != null ? controller.velocity.magnitude : 0f;
            if (speed > m_MoveThreshold) { m_Timer = 0f; return; }

            m_Timer += Time.deltaTime;
            if (m_Timer < m_Seconds) return;

            m_Fired = true;
            if (m_Log) Debug.Log("[Map0Narration] 停留触发 " + m_BeatId);
            director.Request(m_BeatId);
        }

        public void Configure(string beatId, string requires, float seconds)
        {
            m_BeatId = beatId;
            m_Requires = requires;
            m_Seconds = Mathf.Max(0.5f, seconds);
        }
    }
}
