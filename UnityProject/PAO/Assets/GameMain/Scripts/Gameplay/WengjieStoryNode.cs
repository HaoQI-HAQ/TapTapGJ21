using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 剧情 / 配音触发点：玩家走进范围 → 播一句配音（可选）+ 打一行字幕。
    ///
    /// 设计意图：每段路只放 1~2 个，放在「玩家刚好站稳、抬头看」的位置 ——
    /// 坡道中段、村台入口、井口、塔顶。不在跑动中打断，只在停顿处说话。
    ///
    /// 不依赖任何 UI/音频管理器，只用最基础的 AudioSource，
    /// 避免动到共享系统。以后接了正式字幕系统，把 ShowLine 换掉即可。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/剧情触发点（wengjie）")]
    public sealed class WengjieStoryNode : MonoBehaviour
    {
        [Header("内容")]
        [Tooltip("字幕文本。留空则只播音不写字。")]
        [SerializeField] private string m_Line = "";

        [Tooltip("配音片段。留空则只显示字幕。")]
        [SerializeField] private AudioClip m_Voice;

        [Tooltip("说话人（显示在字幕前）")]
        [SerializeField] private string m_Speaker = "泡泡";

        [Header("触发")]
        [Tooltip("只触发一次")]
        [SerializeField] private bool m_Once = true;

        [Tooltip("进入后延迟多久开口（秒）")]
        [SerializeField] private float m_Delay = 0.6f;

        [Tooltip("音量")]
        [SerializeField, Range(0f, 1f)] private float m_Volume = 1f;

        private bool m_Played;
        private AudioSource m_Source;

        private void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            if (m_Source == null) m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;       // 2D，配音不受位置影响
        }

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Played && m_Once) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;

            m_Played = true;
            Invoke(nameof(Speak), Mathf.Max(0f, m_Delay));
        }

        private void Speak()
        {
            if (m_Voice != null) m_Source.PlayOneShot(m_Voice, m_Volume);

            if (string.IsNullOrEmpty(m_Line)) return;

            // 走游戏已有的字幕系统（V2 UI），保证字体、排版、淡入淡出和其它旁白一致
            PAO.UI.V2.UIRuntime runtime = PAO.UI.V2.UIRuntime.Instance;
            if (runtime != null)
            {
                runtime.ShowSubtitle(m_Speaker, m_Line);
            }
            else
            {
                Debug.Log(string.Format("[旁白] {0}：{1}", m_Speaker, m_Line));
            }
        }
    }
}
