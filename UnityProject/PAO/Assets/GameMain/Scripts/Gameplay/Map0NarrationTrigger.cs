using System.Collections;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 第一章旁白触发区：玩家走进来 → 请求播放一段旁白。
    /// 可选「依赖段」（requires）：只有前置段播过之后才触发，用来做「回头」「第二次经过」。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("PAO/关卡/第一章旁白触发区（Map0）")]
    public sealed class Map0NarrationTrigger : MonoBehaviour
    {
        [Tooltip("旁白段 id，例如 PY-01-M-001")]
        [SerializeField] private string m_BeatId;

        [Tooltip("前置旁白段 id（可空）。填了就必须前置段已播过才触发。")]
        [SerializeField] private string m_Requires;

        [Tooltip("只触发一次")]
        [SerializeField] private bool m_Once = true;

        [Tooltip("进圈后延迟多少秒再请求")]
        [SerializeField, Min(0f)] private float m_Delay = 0f;

        private bool m_Fired;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Fired && m_Once) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;

            Map0NarrationDirector director = Map0NarrationDirector.Instance;
            if (director == null) return;
            if (!string.IsNullOrEmpty(m_Requires) && !director.HasPlayed(m_Requires)) return;

            m_Fired = true;
            if (m_Delay <= 0.01f) director.Request(m_BeatId);
            else StartCoroutine(RequestDelayed(m_BeatId, m_Delay));
        }

        private IEnumerator RequestDelayed(string id, float delay)
        {
            yield return new WaitForSeconds(delay);
            Map0NarrationDirector director = Map0NarrationDirector.Instance;
            if (director != null) director.Request(id);
        }

        /// <summary>由搭建脚本调用。</summary>
        public void Configure(string beatId, string requires, bool once, float delay)
        {
            m_BeatId = beatId;
            m_Requires = requires;
            m_Once = once;
            m_Delay = delay;
        }
    }
}
