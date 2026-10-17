using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO
{
    /// <summary>
    /// 走进范围就播一句旁白。
    ///
    /// 【为什么做成组件而不是写进关卡脚本】
    /// 旁白是**文案**，不是地形。做成组件后，文案在 Inspector 里直接改，
    /// 不用回来碰搭建脚本；而且以后别的关卡也能直接复用。
    ///
    /// 【接到哪套系统上】
    /// 用工程既有的 <c>UIRuntime.ShowNarration(说话人, 文本)</c>，
    /// 也就是 HUD 上那个对话条 —— 不自己造一套字幕。
    /// 不传 duration 时它按字数自动算停留时间（max(5, 字数×0.12) 秒）。
    ///
    /// 【触发规则】
    ///   · 认玩家：工程里没有自定义 Tag，所以按组件找 PlayerController
    ///   · 默认只播一次（走回头路不会重复唠叨）
    ///   · 必须有 IsTrigger 的 Collider —— 忘了勾会自动补上并打警告，
    ///     否则 OnTriggerEnter 永远不触发，而且一点报错都没有
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/旁白触发（Narration Trigger）")]
    public sealed class NarrationTrigger : MonoBehaviour
    {
        [Header("台词")]
        [Tooltip("说话人。留空则只显示正文（旁白通常留空或写「编织者」）")]
        [SerializeField] private string m_Speaker = "";

        [Tooltip("正文。太长的话考虑换行，HUD 那个条是窄的")]
        [TextArea(2, 5)]
        [SerializeField] private string m_Text = "";

        [Tooltip("Optional stable narration audio ID; defaults to SceneName/ObjectName")]
        [SerializeField] private string m_AudioKey = "";
        [Tooltip("Optional direct voice clip. Otherwise PAOAudio/NarrationLibrary resolves the ID.")]
        [SerializeField] private AudioClip m_VoiceClip;

        [Tooltip("停留时长（秒）。0 = 按字数自动算，负数 = 常驻直到被别的旁白顶掉")]
        [SerializeField] private float m_Duration = 0f;

        [Header("触发")]
        [Tooltip("只播一次。关掉则每次进入范围都播")]
        [SerializeField] private bool m_Once = true;

        [Tooltip("进入范围后延迟多久才播（秒）。0 = 立刻")]
        [SerializeField] private float m_Delay = 0f;

        [Header("调试")]
        [SerializeField] private bool m_Log = true;

        private bool m_Played;
        private bool m_Armed;

        private void Awake()
        {
            EnsureTriggerCollider();
        }

        /// <summary>
        /// 兜底：没有 Collider、或者忘了勾 Is Trigger 时自动补上。
        ///
        /// 这两种情况都会让 OnTriggerEnter **静默不触发** —— 没有报错、
        /// 没有任何提示，只能靠"怎么走了没反应"来发现，最难查。
        /// </summary>
        private void EnsureTriggerCollider()
        {
            Collider col = GetComponent<Collider>();

            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(10f, 6f, 4f);   // 给一个够大的默认范围
                box.center = new Vector3(0f, 3f, 0f);

                Debug.LogWarning("[NarrationTrigger] \"" + name +
                                 "\" 上没有 Collider，已自动补一个 Trigger BoxCollider。", this);
                return;
            }

            if (!col.isTrigger)
            {
                col.isTrigger = true;
                Debug.LogWarning("[NarrationTrigger] \"" + name +
                                 "\" 的 Collider 没勾 Is Trigger，已自动勾上。", this);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Once && m_Played)
            {
                return;
            }

            // 认玩家：工程里没有自定义 Tag，所以按组件判断。
            // 玩家的 CharacterController 能触发 OnTriggerEnter，触发区这边不需要刚体。
            if (other.GetComponentInParent<PlayerController>() == null &&
                other.GetComponentInParent<CharacterController>() == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(m_Text))
            {
                return;
            }

            m_Played = true;

            if (m_Delay > 0f)
            {
                // 延迟期间用 unscaled：暂停时（timeScale = 0）不该把计时冻住
                m_Armed = true;
                StartCoroutine(PlayDelayed());
                return;
            }

            Play();
        }

        private IEnumerator PlayDelayed()
        {
            yield return new WaitForSecondsRealtime(m_Delay);
            if (isActiveAndEnabled && m_Armed) Play();
        }

        private void Play()
        {
            m_Armed = false;

            string audioKey = string.IsNullOrEmpty(m_AudioKey)
                ? SceneManager.GetActiveScene().name + "/" + name : m_AudioKey;
            float voiceLength = PAO.AV.PAOAudioDirector.Instance != null
                ? PAO.AV.PAOAudioDirector.Instance.PlayNarration(audioKey, m_VoiceClip) : 0f;
            PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.Narration, transform.position);

            UI.V2.UIRuntime runtime = UI.V2.UIRuntime.Instance;

            if (runtime == null)
            {
                // HUD 还没起来时不要静默失败 —— 旁白看不到往往是因为这个
                if (m_Log)
                {
                    Debug.LogWarning("[NarrationTrigger] \"" + name +
                                     "\" 触发了，但 UIRuntime 不在场景里，旁白无处显示。" +
                                     "（白盒关卡需要先装 HUD）", this);
                }

                return;
            }

            float displayDuration = m_Duration == 0f && voiceLength > 0f
                ? Mathf.Max(voiceLength + .5f, Mathf.Max(5f, m_Text.Length * .12f)) : m_Duration;
            runtime.ShowNarration(m_Speaker, m_Text, displayDuration);

            if (m_Log)
            {
                Debug.Log("[NarrationTrigger] \"" + name + "\" 播放旁白：" + m_Text, this);
            }
        }

        /// <summary>由搭建脚本在生成时调用，写入台词。</summary>
        public void Configure(string speaker, string text, float duration, float delay = 0f)
        {
            m_Speaker = speaker;
            m_Text = text;
            m_Duration = duration;
            m_Delay = delay;
        }

#if UNITY_EDITOR
        /// <summary>选中时把触发范围画出来，方便摆位置。</summary>
        private void OnDrawGizmosSelected()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.45f);
            Gizmos.matrix = transform.localToWorldMatrix;

            var box = col as BoxCollider;
            if (box != null)
            {
                Gizmos.DrawWireCube(box.center, box.size);
                return;
            }

            var sphere = col as SphereCollider;
            if (sphere != null)
            {
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
        }
#endif
    }
}
