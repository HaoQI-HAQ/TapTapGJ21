using UnityEngine;
using UnityEngine.UI;

namespace PAO
{
    /// <summary>
    /// wengjie 的剧情字幕兜底层：自己拿一个 Canvas + 两行 Text，不依赖 UIRuntime。
    ///
    /// 为什么需要：
    ///   直接从编辑器 Play wengjie（不经过主菜单）时，V2 UI 可能初始化不全，
    ///   字幕接口会静默失败/抛异常 —— 看起来就是“字幕没了”。
    ///   这个组件由场景里预先摆好的对象驱动（字体、描边、位置都在场景里），
    ///   只要场景在，字幕就一定显示。
    /// </summary>
    public sealed class WengjieSubtitleOverlay : MonoBehaviour
    {
        private static WengjieSubtitleOverlay s_Instance;

        [SerializeField] private CanvasGroup m_Group;
        [SerializeField] private Text m_Speaker;
        [SerializeField] private Text m_Body;

        private float m_Remaining;
        private float m_Fade;
        private bool m_Showing;

        public static bool Show(string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (s_Instance == null) s_Instance = FindObjectOfType<WengjieSubtitleOverlay>(true);
            if (s_Instance == null) return false;
            s_Instance.Present(speaker, text);
            return true;
        }

        public static void Hide()
        {
            if (s_Instance == null) s_Instance = FindObjectOfType<WengjieSubtitleOverlay>(true);
            if (s_Instance != null) s_Instance.m_Remaining = 0f;
        }

        private void Awake()
        {
            s_Instance = this;
            if (m_Group == null) m_Group = GetComponent<CanvasGroup>();
            m_Fade = 0f;
            Apply(0f);
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        private void Present(string speaker, string text)
        {
            if (m_Body == null) return;
            m_Body.text = text;

            if (m_Speaker != null)
            {
                bool named = !string.IsNullOrWhiteSpace(speaker) && speaker != "旁白" &&
                             !string.Equals(speaker, "Narrator", System.StringComparison.OrdinalIgnoreCase);
                m_Speaker.text = named ? speaker : string.Empty;
                m_Speaker.gameObject.SetActive(named);
            }

            // 阅读时间：最短 4 秒，再按字数加，最后留 1 秒余量
            m_Remaining = Mathf.Max(4f, text.Length * 0.13f) + 1f;
            m_Showing = true;
        }

        private void Update()
        {
            // 编辑器里的导入/编译会造成几十秒的 delta 尖峰。
            // 用 clamp 过的 dt 扣时间，否则一次卡顿就会把字幕直接判过期（看起来就是"字幕没出来"）。
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (m_Showing)
            {
                m_Remaining -= dt;
                if (m_Remaining <= 0f) m_Showing = false;
            }

            float target = m_Showing ? 1f : 0f;
            float speed = target > 0f ? 1f / 0.22f : 1f / 0.35f;
            m_Fade = Mathf.MoveTowards(m_Fade, target, dt * speed);
            Apply(m_Fade);
        }

        private void Apply(float alpha)
        {
            if (m_Group != null)
            {
                m_Group.alpha = Mathf.Clamp01(alpha);
                m_Group.blocksRaycasts = false;
                m_Group.interactable = false;
            }
        }
    }
}
