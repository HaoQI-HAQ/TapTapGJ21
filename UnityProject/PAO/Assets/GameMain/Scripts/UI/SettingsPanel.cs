using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 游戏设置面板：由主菜单的「游戏设置」按钮打开，覆盖在主菜单之上。
    ///
    /// 【标签页】
    /// 顶部四个标签：声音 / 画面 / 操作 / 辅助。
    /// 每个标签各有一个内容根物体（m_Content），切换时只开当前标签的内容，
    /// 其余关闭。所以「画面 / 操作 / 辅助」现在就是三个空页，
    /// 以后往里塞内容即可，不用改这里的逻辑。
    ///
    /// 【层次】
    /// 面板自己带一张全屏半透明底图，负责挡住下面的主菜单。
    /// 打开时会禁用主菜单按钮：一方面挡住鼠标穿透，
    /// 另一方面避免手柄方向键跑到面板底下的按钮上去。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Settings Panel")]
    public sealed class SettingsPanel : MonoBehaviour
    {
        /// <summary>一个标签页：顶部标签 + 它对应的内容根物体。</summary>
        [Serializable]
        public class TabItem
        {
            [Tooltip("顶部标签文字")]
            public Text label;

            [Tooltip("该标签对应的内容根物体，切换时整体开关")]
            public GameObject content;

            [Tooltip("标签下方的小圆点，可为空")]
            public Image dot;
        }

        [Header("面板")]
        [Tooltip("面板根物体。留空则用本物体")]
        [SerializeField] private GameObject m_Root;

        [Header("标签页")]
        [Tooltip("声音 / 画面 / 操作 / 辅助，顺序与界面一致")]
        [SerializeField] private TabItem[] m_Tabs = new TabItem[0];

        [Tooltip("打开面板时默认选中第几个标签")]
        [SerializeField] private int m_DefaultTab = 0;

        [Header("按钮")]
        [Tooltip("底部「完成」按钮，点了关闭面板")]
        [SerializeField] private Button m_DoneButton;

        [Tooltip("底部「恢复默认」按钮，点了把所有音量滑条复位")]
        [SerializeField] private Button m_ResetButton;

        [Tooltip("左下角「返回」按钮，作用是关闭面板")]
        [SerializeField] private Button m_BackButton;

        [Header("音量滑条")]
        [Tooltip("恢复默认时要一起复位的滑条：总音量 / 音乐 / 音效")]
        [SerializeField] private VolumeSlider[] m_VolumeSliders = new VolumeSlider[0];

        [Header("主菜单")]
        [Tooltip("打开面板时要禁用的主菜单按钮，防止手柄导航穿到面板底下")]
        [SerializeField] private MenuButton[] m_MenuButtons = new MenuButton[0];

        [Header("外观")]
        [Tooltip("选中的标签颜色")]
        [SerializeField] private Color m_ActiveTabColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Tooltip("未选中的标签颜色")]
        [SerializeField] private Color m_InactiveTabColor = new Color(0.45f, 0.55f, 0.57f, 1f);

        [Tooltip("选中的标签字号")]
        [SerializeField] private int m_ActiveTabFontSize = 34;

        [Tooltip("未选中的标签字号")]
        [SerializeField] private int m_InactiveTabFontSize = 28;

        [Tooltip("不需要「返回」按钮时勾上")]
        [SerializeField] private bool m_HideBackButton = true;

        private bool m_IsOpen;

        /// <summary>场景里当前活动的设置面板。用于与其它全屏面板互斥。</summary>
        public static SettingsPanel Instance { get; private set; }

        /// <summary>面板当前是否打开。</summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        /// <summary>打开 / 关闭时抛出，参数是「现在是否打开」。</summary>
        public event Action<bool> OpenStateChanged;

        private bool m_Initialized;

        private void Awake()
        {
            Instance = this;
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 幂等初始化：注册按钮监听、把面板摆成关闭状态。
        ///
        /// 面板在场景里是「未激活」的，所以它自己的 Awake 不会在场景加载时执行，
        /// 而是第一次被打开时才跑。而每次打开都会触发一次 Awake，
        /// 若不做幂等保护，监听会被重复注册，点一次「完成」就调用多次 Close。
        /// </summary>
        private void EnsureInitialized()
        {
            if (m_Initialized)
            {
                return;
            }

            m_Initialized = true;

            if (m_Root == null)
            {
                m_Root = gameObject;
            }

            if (m_DoneButton != null)
            {
                m_DoneButton.onClick.RemoveListener(ConfirmAndClose);
                m_DoneButton.onClick.AddListener(ConfirmAndClose);
            }

            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveListener(Close);
                m_BackButton.onClick.AddListener(Close);

                if (m_HideBackButton)
                {
                    m_BackButton.gameObject.SetActive(false);
                }
            }

            if (m_ResetButton != null)
            {
                m_ResetButton.onClick.RemoveListener(RestoreDefaultVolumes);
                m_ResetButton.onClick.AddListener(RestoreDefaultVolumes);
            }
        }

        private void Update()
        {
            if (m_IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                PauseMenu.MarkOverlayEscapeHandled();
                Close();
            }
        }

        // ==================== 开关 ====================

        /// <summary>打开面板。</summary>
        public void Open()
        {
            if (m_IsOpen)
            {
                return;
            }

            m_IsOpen = true;

            // 面板未激活时它自己的 Awake 不会跑，这里兜底确保监听已注册
            EnsureInitialized();
            m_Root.SetActive(true);

            SetMenuButtonsEnabled(false);
            SelectTab(m_DefaultTab);

            // 手柄：把焦点交给第一个可交互的滑条，方向键立刻可用。
            // 这里不设 EventSystem.firstSelectedGameObject，而是直接设置选中物体，
            // 免得下次打开又出现「没碰它就高亮」的问题。
            SelectFirstControl();

            RaiseChanged();
        }

        /// <summary>
        /// 「完成」按钮：先把当前设置落盘，再关闭面板回主菜单。
        /// 面板上的「返回」(←) 与 Esc 只关闭不保存，方便反悔。
        /// </summary>
        public void ConfirmAndClose()
        {
            SaveCurrentSettings();
            Close();
        }

        /// <summary>
        /// 把所有设置项显式写盘。
        ///
        /// 每根滑条在 OnSliderChanged 里只写 PlayerPrefs 的缓存，
        /// 真正的落盘（PlayerPrefs.Save）由这里统一触发，
        /// 这样「中途退出」不会把值留在缓存里丢掉。
        /// </summary>
        public void SaveCurrentSettings()
        {
            int saved = 0;

            if (m_VolumeSliders != null)
            {
                for (int i = 0; i < m_VolumeSliders.Length; i++)
                {
                    VolumeSlider slider = m_VolumeSliders[i];
                    if (slider != null && slider.CommitSettings())
                    {
                        saved++;
                    }
                }
            }

            Debug.Log("[SettingsPanel] 设置已保存（音量项 " + saved + " 项）。");
        }

        /// <summary>关闭面板。</summary>
        public void Close()
        {
            if (!m_IsOpen)
            {
                return;
            }

            m_IsOpen = false;
            m_Root.SetActive(false);

            SetMenuButtonsEnabled(true);

            // 收掉所有主菜单按钮的高亮，并遗忘「用过方向键」的状态。
            // 否则关闭面板后，之前手柄选中的那颗按钮会一直亮着。
            MenuButton.ResetNavigationState();

            RaiseChanged();
        }

        /// <summary>开着就关、关着就开。</summary>
        public void Toggle()
        {
            if (m_IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void RaiseChanged()
        {
            if (OpenStateChanged != null)
            {
                OpenStateChanged(m_IsOpen);
            }
        }

        // ==================== 标签页 ====================

        /// <summary>切到第 index 个标签页。</summary>
        public void SelectTab(int index)
        {
            if (m_Tabs == null || m_Tabs.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, m_Tabs.Length - 1);

            for (int i = 0; i < m_Tabs.Length; i++)
            {
                TabItem tab = m_Tabs[i];
                if (tab == null)
                {
                    continue;
                }

                bool active = i == index;

                if (tab.content != null)
                {
                    tab.content.SetActive(active);
                }

                if (tab.label != null)
                {
                    tab.label.color = active ? m_ActiveTabColor : m_InactiveTabColor;
                    tab.label.fontSize = active ? m_ActiveTabFontSize : m_InactiveTabFontSize;
                }

                if (tab.dot != null)
                {
                    Color dotColor = active ? m_ActiveTabColor : m_InactiveTabColor;
                    dotColor.a = active ? 1f : 0.5f;
                    tab.dot.color = dotColor;
                }
            }
        }

        // ==================== 默认值 ====================

        /// <summary>把所有音量滑条恢复到默认值。</summary>
        public void RestoreDefaultVolumes()
        {
            if (m_VolumeSliders == null)
            {
                return;
            }

            for (int i = 0; i < m_VolumeSliders.Length; i++)
            {
                if (m_VolumeSliders[i] != null)
                {
                    m_VolumeSliders[i].ResetToDefault();
                }
            }
        }

        // ==================== 内部 ====================

        private void SetMenuButtonsEnabled(bool enabled)
        {
            if (m_MenuButtons == null)
            {
                return;
            }

            for (int i = 0; i < m_MenuButtons.Length; i++)
            {
                MenuButton button = m_MenuButtons[i];
                if (button == null)
                {
                    continue;
                }

                Button target = button.GetComponent<Button>();
                if (target != null)
                {
                    target.interactable = enabled;
                }
            }
        }

        /// <summary>把 EventSystem 的选中物体交给面板里第一个可交互控件。</summary>
        private void SelectFirstControl()
        {
            GameObject first = null;

            // 优先交给第一根音量滑条，打开后方向键立刻能调音量
            if (m_VolumeSliders != null)
            {
                for (int i = 0; i < m_VolumeSliders.Length; i++)
                {
                    VolumeSlider volume = m_VolumeSliders[i];
                    if (volume == null)
                    {
                        continue;
                    }

                    Slider slider = volume.GetComponentInChildren<Slider>(true);
                    if (slider != null)
                    {
                        first = slider.gameObject;
                        break;
                    }
                }
            }

            if (first == null && m_ResetButton != null)
            {
                first = m_ResetButton.gameObject;
            }

            if (first == null && m_DoneButton != null)
            {
                first = m_DoneButton.gameObject;
            }

            if (first != null)
            {
                EventSystem es = EventSystem.current;
                if (es != null)
                {
                    es.SetSelectedGameObject(first);
                }
            }
        }
    }
}
