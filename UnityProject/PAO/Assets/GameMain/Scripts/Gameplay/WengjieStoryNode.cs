using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 剧情 / 配音触发点：玩家走进范围 → 播一句配音（可选）+ 打一行字幕。
    ///
    /// 行为：
    ///   · 玩家进入触发区 → 等 m_Delay 秒 → 播配音 + 出字幕；
    ///   · m_Once 勾上时只触发一次；
    ///   · 不依赖任何 UI/视频系统：字幕优先走场景里的 WengjieSubtitleOverlay，
    ///     场景里没有时才回退到 V2 UI 的字幕接口。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/剧情触发点（wengjie）")]
    public sealed class WengjieStoryNode : MonoBehaviour
    {
        [Header("台词")]
        [Tooltip("字幕文本，留空则只播配音。")]
        [SerializeField] private string m_Line = "";

        [Tooltip("配音片段，留空则只显示字幕。")]
        [SerializeField] private AudioClip m_Voice;

        [Tooltip("说话人，显示在字幕前。")]
        [SerializeField] private string m_Speaker = "旁白";

        [Header("触发")]
        [Tooltip("只触发一次")]
        [SerializeField] private bool m_Once = true;

        [Tooltip("触发到播放的延迟（秒）")]
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

            // 优先用场景里自带的字幕条（WengjieSubtitleOverlay）。
            // 直接从编辑器 Play 关卡时 V2 UI 有时初始化不全，走 UIRuntime 字幕会整条不显示；
            // 这个兜底层不依赖 UI 系统，只负责把这句话显示出来。
            if (WengjieSubtitleOverlay.Show(m_Speaker, m_Line)) return;

            // 场景里没有字幕条时，退回游戏已有的字幕系统（V2 UI）
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
