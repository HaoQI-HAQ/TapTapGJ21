using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 第一章旁白播放器（Map0 专用）。
    ///
    /// 数据来自生成文件 <see cref="Map0NarrationData"/>（源自《泡涌》(2).docx 第一章「诞生」）。
    /// 行为：
    ///   · 主线 / 反应段以「一段 = 一条音频 + 多行字幕」播放；音频长度按字数比例分配每行字幕时长；
    ///   · 新的触发段进入队列，按顺序播完（同一段只播一次）；
    ///   · 支持「静止 N 秒」类反应段（不按任何键、不在播放时计时）；
    ///   · 暂停时音频跟着游戏暂停，字幕也停。
    ///
    /// 字幕走 wengjie 的独立字幕条（WengjieSubtitleOverlay），不依赖 UIRuntime，
    /// 所以直接从编辑器 Play 也能看到。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/第一章旁白播放器（Map0）")]
    public sealed class Map0NarrationDirector : MonoBehaviour
    {
        [System.Serializable]
        public struct ClipEntry
        {
            public string id;
            public AudioClip clip;
        }

        [System.Serializable]
        public struct IdleBeat
        {
            [Tooltip("静止多少秒后播放")]
            public float seconds;

            [Tooltip("旁白段 id，例如 PY-01-B-001")]
            public string id;
        }

        [Header("配音")]
        [SerializeField] private ClipEntry[] m_Clips = new ClipEntry[0];

        [Header("节奏")]
        [Tooltip("每行字幕之间的停顿（秒）")]
        [SerializeField, Range(0f, 2f)] private float m_LineGap = 0.25f;

        [Tooltip("没有配音时每行最短显示时间")]
        [SerializeField, Min(0.5f)] private float m_MinLineSeconds = 2.4f;

        [Tooltip("没有配音时每秒读几个字")]
        [SerializeField, Min(1f)] private float m_CharsPerSecond = 6f;

        [Header("静止触发")]
        [SerializeField] private IdleBeat[] m_IdleBeats = new IdleBeat[0];

        [Header("调试")]
        [SerializeField] private bool m_Log = true;

        public static Map0NarrationDirector Instance { get; private set; }

        private readonly Queue<string> m_Queue = new Queue<string>();
        private readonly HashSet<string> m_Played = new HashSet<string>();
        private readonly Dictionary<string, AudioClip> m_ClipIndex = new Dictionary<string, AudioClip>();
        private AudioSource m_Source;
        private bool m_Busy;
        private float m_IdleSeconds;
        private bool m_PausedByTimeScale;

        public bool IsBusy { get { return m_Busy; } }

        private void Awake()
        {
            Instance = this;
            m_Source = GetComponent<AudioSource>();
            if (m_Source == null) m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.loop = false;
            m_Source.spatialBlend = 0f;
            m_Source.volume = 1f;

            m_ClipIndex.Clear();
            for (int i = 0; i < m_Clips.Length; i++)
            {
                if (string.IsNullOrEmpty(m_Clips[i].id) || m_Clips[i].clip == null) continue;
                m_ClipIndex[m_Clips[i].id] = m_Clips[i].clip;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // 暂停时把配音也停住（游戏用 Time.timeScale = 0 表示暂停）
            bool paused = Time.timeScale <= 0.0001f;
            if (paused != m_PausedByTimeScale)
            {
                m_PausedByTimeScale = paused;
                if (paused) m_Source.Pause();
                else m_Source.UnPause();
            }

            if (m_Busy || paused)
            {
                m_IdleSeconds = 0f;
                return;
            }

            bool active = Input.anyKeyDown
                || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)
                || Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f
                || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f;
            if (active) m_IdleSeconds = 0f;
            else m_IdleSeconds += Time.deltaTime;

            for (int i = 0; i < m_IdleBeats.Length; i++)
            {
                IdleBeat beat = m_IdleBeats[i];
                if (string.IsNullOrEmpty(beat.id) || m_Played.Contains(beat.id)) continue;
                if (m_IdleSeconds < beat.seconds) continue;
                Request(beat.id);
                break;
            }
        }

        /// <summary>是否已经播过某段（给触发器的 requires 用）。</summary>
        public bool HasPlayed(string id)
        {
            return !string.IsNullOrEmpty(id) && m_Played.Contains(id);
        }

        /// <summary>请求播放一段旁白。已经在队列/播过/找不到数据时忽略。</summary>
        public void Request(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (m_Played.Contains(id) || m_Queue.Contains(id)) return;
            if (Map0NarrationData.Find(id) == null) return;   // 旧版触发器的 id 已不存在，静默跳过

            m_Queue.Enqueue(id);
            if (!m_Busy) StartCoroutine(Pump());
        }

        private IEnumerator Pump()
        {
            m_Busy = true;
            m_Source.Play();   // 先给 AudioSource 一个播放状态，避免第一次 PlayOneShot 前的延迟
            m_Source.Stop();
            while (m_Queue.Count > 0)
            {
                string id = m_Queue.Dequeue();
                if (m_Played.Contains(id)) continue;
                Map0NarrationData.Beat beat = Map0NarrationData.Find(id);
                if (beat == null) continue;

                m_Played.Add(id);
                if (m_Log) Debug.Log("[Map0Narration] 播放 " + id + " · " + beat.title + "（" + beat.lines.Length + " 句）");
                yield return PlayBeat(beat);
            }
            m_Busy = false;
        }

        private IEnumerator PlayBeat(Map0NarrationData.Beat beat)
        {
            AudioClip clip = null;
            m_ClipIndex.TryGetValue(beat.id, out clip);

            int totalChars = 0;
            for (int i = 0; i < beat.lines.Length; i++) totalChars += Mathf.Max(1, beat.lines[i].Length);

            if (clip != null)
            {
                m_Source.clip = clip;
                m_Source.time = 0f;
                m_Source.Play();
            }

            for (int i = 0; i < beat.lines.Length; i++)
            {
                string line = beat.lines[i];
                if (string.IsNullOrEmpty(line)) continue;

                WengjieSubtitleOverlay.Show("旁白", line);

                float duration;
                if (clip != null && clip.length > 0.05f && totalChars > 0)
                    duration = clip.length * Mathf.Max(1, line.Length) / totalChars;
                else
                    duration = Mathf.Max(m_MinLineSeconds, line.Length / m_CharsPerSecond);

                yield return new WaitForSeconds(duration + m_LineGap);
            }

            WengjieSubtitleOverlay.Hide();
            yield return new WaitForSeconds(m_LineGap);
        }
    }
}
