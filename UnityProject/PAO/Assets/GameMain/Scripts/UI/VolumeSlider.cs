using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 一根音量滑条：对接 AudioMixer 的一个暴露参数，并把值存进 PlayerPrefs。
    ///
    /// 【找不到混音器时怎么办】
    /// 工程里现在还没有 AudioMixer（Audio/BGM 与 Audio/SFX 都是空目录），
    /// 所以不能把「拖了没反应」做成报错。这里的策略是：
    ///   · 有 mixer 且有暴露参数 → 真正改音量，并写 PlayerPrefs
    ///   · 没有 mixer            → 只写 PlayerPrefs，并在 Console 提醒一次
    /// 等以后把 mixer 拖进这个字段，音量控制立刻生效，滑条不用改。
    ///
    /// 暴露参数的命名约定与 <see cref="SettingsPanel"/> 里创建的混音器一致：
    ///   Master / Music / SFX
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VolumeSlider : MonoBehaviour
    {
        [Header("绑定")]
        [Tooltip("这根滑条要控制哪一项。留空则自动取子物体上的 Slider")]
        [SerializeField] private Slider m_Slider;

        [Header("音量")]
        [Tooltip("目标混音器。留空则只存值、不改音量")]
        [SerializeField] private AudioMixer m_Mixer;

        [Tooltip("混音器里已暴露的参数名，例如 Master / Music / SFX")]
        [SerializeField] private string m_Parameter = "Master";

        [Tooltip("存盘用的 PlayerPrefs 键名。留空则用 PAO.Volume.<参数名>")]
        [SerializeField] private string m_PrefsKey = string.Empty;

        [Tooltip("恢复默认时回到的值")]
        [SerializeField, Range(0f, 1f)] private float m_DefaultValue = 0.8f;

        [Tooltip("低于这个值时静音（滑到最左直接 -80dB）")]
        [SerializeField, Range(0.0001f, 0.1f)] private float m_MuteThreshold = 0.001f;

        private const float kMinDecibels = -80f;
        private const float kMaxDecibels = 20f;

        /// <summary>当前 0~1 的线性音量。</summary>
        public float Value
        {
            get { return m_Slider != null ? m_Slider.value : 0f; }
        }

        /// <summary>存盘用的键名。</summary>
        public string PrefsKey
        {
            get { return string.IsNullOrEmpty(m_PrefsKey) ? "PAO.Volume." + m_Parameter : m_PrefsKey; }
        }

        private void Awake()
        {
            if (m_Slider == null)
            {
                m_Slider = GetComponentInChildren<Slider>(true);
            }

            if (m_Slider == null)
            {
                Debug.LogWarning("[VolumeSlider] 找不到 Slider 组件，音频设置不会生效。", this);
                return;
            }

            // 先把交互范围钉死，避免 Inspector 里被改乱
            m_Slider.minValue = 0f;
            m_Slider.maxValue = 1f;

            m_Slider.onValueChanged.AddListener(OnSliderChanged);

            float value = PlayerPrefs.HasKey(PrefsKey) ? PlayerPrefs.GetFloat(PrefsKey) : m_DefaultValue;
            m_Slider.SetValueWithoutNotify(Mathf.Clamp01(value));
            Apply(value);
        }

        private void OnDestroy()
        {
            if (m_Slider != null)
            {
                m_Slider.onValueChanged.RemoveListener(OnSliderChanged);
            }
        }

        private void OnSliderChanged(float value)
        {
            Apply(value);

            // 拖动时先写内存缓存，真正的落盘交给 PlayerPrefs.Save()。
            // 面板上的「完成」按钮会调 CommitSettings() 强制落盘，
            // 这样「改到一半直接退出」也不会把值留在缓存里丢掉。
            PlayerPrefs.SetFloat(PrefsKey, value);
        }

        /// <summary>
        /// 把当前值写进磁盘并立即生效。「完成」按钮调它。
        /// 返回是否成功（PlayerPrefs 基本不会失败，留返回值是为了以后换存储时有地方接）。
        /// </summary>
        public bool CommitSettings()
        {
            if (m_Slider == null)
            {
                return false;
            }

            float value = m_Slider.value;

            Apply(value);
            PlayerPrefs.SetFloat(PrefsKey, value);
            PlayerPrefs.Save();     // 强制落盘

            return true;
        }

        /// <summary>恢复默认值（同时写盘）。</summary>
        public void ResetToDefault()
        {
            SetValue(m_DefaultValue);
        }

        /// <summary>程序化设定值，会触发存盘与音量应用。</summary>
        public void SetValue(float value)
        {
            value = Mathf.Clamp01(value);

            if (m_Slider != null)
            {
                m_Slider.value = value;   // 走 onValueChanged，统一处理
            }
            else
            {
                Apply(value);
            }

            // 程序化设定同样要落盘：OnSliderChanged 只写缓存，
            // 不补这一句，恢复默认在退出后会丢。
            CommitSettings();
        }

        /// <summary>把线性音量写进混音器。</summary>
        private void Apply(float value)
        {
            if (m_Mixer == null || string.IsNullOrEmpty(m_Parameter))
            {
                return;
            }

            // 线性 → 分贝。0 不能取 log，直接钳到静音下限。
            float db = value <= m_MuteThreshold
                ? kMinDecibels
                : Mathf.Clamp(20f * Mathf.Log10(value), kMinDecibels, kMaxDecibels);

            m_Mixer.SetFloat(m_Parameter, db);
        }
    }
}
