using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 关卡选择面板：由主菜单的「关卡选择」按钮打开，点某个关卡就加载对应场景。
    ///
    /// 和 SettingsPanel / LoadPanel 同一套用法：
    ///   · 场景里保持未激活，Open() 激活、Close() 收起
    ///   · 与另外两个全屏面板互斥，同时只会有一个开着
    ///   · 打开时禁用主菜单按钮，关掉时恢复并收掉高亮
    ///
    /// 选中的关卡会记进 PlayerPrefs，<see cref="LastPlayedLevelScene"/> 供暂停页的
    /// 「重开」读取，不用再猜上一次玩的是哪张图。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Level Select Panel")]
    public sealed class LevelSelectPanel : MonoBehaviour
    {
        /// <summary>一个关卡按钮。</summary>
        [Serializable]
        public class LevelEntry
        {
            [Tooltip("按钮根物体")]
            public GameObject root;

            [Tooltip("关卡名字文字，例如 Map0")]
            public TMPro.TMP_Text label;

            [Tooltip("点击这个关卡的按钮")]
            public Button button;

            [Tooltip("要加载的场景名，例如 Map0")]
            public string sceneName = string.Empty;
        }

        /// <summary>一次性把界面引用接好，给编辑器搭建脚本用。</summary>
        public sealed class Config
        {
            public GameObject root;
            public Button closeButton;
            public LevelEntry[] levels;
            public MenuButton[] menuButtons;
            public bool hideCloseButton;
        }

        /// <summary>上一次选过的关卡场景名，存在 PlayerPrefs 里。</summary>
        public const string LastLevelPrefsKey = "PAO.LastLevelScene";

        [Header("面板")]
        [Tooltip("面板根物体。留空则用本物体")]
        [SerializeField] private GameObject m_Root;

        [Header("关卡")]
        [Tooltip("关卡列表，顺序即按钮顺序")]
        [SerializeField] private LevelEntry[] m_Levels = new LevelEntry[0];

        [Header("按钮")]
        [Tooltip("关闭按钮")]
        [SerializeField] private Button m_CloseButton;

        [Tooltip("不需要「关闭」按钮时勾上")]
        [SerializeField] private bool m_HideCloseButton;

        [Header("主菜单")]
        [Tooltip("打开面板时要禁用的主菜单按钮")]
        [SerializeField] private MenuButton[] m_MenuButtons = new MenuButton[0];

        private bool m_Initialized;
        private bool m_IsOpen;

        /// <summary>场景里当前活动的关卡选择面板。</summary>
        public static LevelSelectPanel Instance { get; private set; }

        /// <summary>面板当前是否打开。</summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        /// <summary>打开 / 关闭时抛出，参数是「现在是否打开」。</summary>
        public event Action<bool> OpenStateChanged;

        /// <summary>选了某个关卡时抛出，参数是要加载的场景名。</summary>
        public event Action<string> LevelChosen;

        /// <summary>上一次选过的关卡场景名（没有则返回空串）。</summary>
        public static string LastPlayedLevelScene
        {
            get { return PlayerPrefs.GetString(LastLevelPrefsKey, string.Empty); }
        }

        private void Awake()
        {
            Instance = this;

            // 场景里保持未激活，运行时才由 Open() 点亮。
            // 注意：只在运行时隐藏自己 —— 否则 Open() 里 SetActive(true) 触发的
            // 这次 Awake 会把刚点亮的面板又关掉（LoadPanel 踩过这个坑）。
            if (Application.isPlaying && m_Root != null && !m_IsOpen)
            {
                m_Root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (m_IsOpen)
            {
                UiEscapeRouter.Handle();
            }
        }

        /// <summary>由编辑器搭建脚本或运行时初始化调用，写入界面引用。</summary>
        public void Configure(Config config)
        {
            if (config == null)
            {
                return;
            }

            m_Root = config.root != null ? config.root : gameObject;
            m_CloseButton = config.closeButton;
            m_Levels = config.levels ?? new LevelEntry[0];
            m_MenuButtons = config.menuButtons ?? new MenuButton[0];
            m_HideCloseButton = config.hideCloseButton;
        }

        /// <summary>
        /// 幂等初始化：注册监听。面板未激活时 Awake 只在第一次打开时跑，
        /// 每次打开都会触发一次，所以必须防重复注册。
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

            if (m_CloseButton != null)
            {
                m_CloseButton.onClick.RemoveListener(Close);
                m_CloseButton.onClick.AddListener(Close);

                if (m_HideCloseButton)
                {
                    m_CloseButton.gameObject.SetActive(false);
                }
            }

            if (m_Levels == null)
            {
                return;
            }

            for (int i = 0; i < m_Levels.Length; i++)
            {
                LevelEntry entry = m_Levels[i];
                if (entry == null || entry.button == null)
                {
                    continue;
                }

                int index = i;   // 闭包捕获：不能用循环变量本身
                entry.button.onClick.AddListener(delegate { SelectLevel(index); });
            }
        }

        // ==================== 开关 ====================

        /// <summary>打开面板（幂等：已经开着也会保证可见并刷新）。</summary>
        public void Open()
        {
            if (m_Root == null)
            {
                Debug.LogError("[LevelSelectPanel] m_Root 为空，面板打不开。", this);
                return;
            }

            // 与另外两个全屏面板互斥
            if (SettingsPanel.Instance != null && SettingsPanel.Instance.IsOpen)
            {
                SettingsPanel.Instance.Close();
            }

            if (LoadPanel.Instance != null && LoadPanel.Instance.IsOpen)
            {
                LoadPanel.Instance.Close();
            }
            if (LevelPathPanel.Instance != null && LevelPathPanel.Instance.IsOpen) LevelPathPanel.Instance.Close();

            m_IsOpen = true;
            m_Root.SetActive(true);

            EnsureInitialized();
            SetMenuButtonsEnabled(false);
            RefreshLevels();
            if (m_Levels != null && m_Levels.Length > 0 && m_Levels[0] != null)
                UiEscapeRouter.Select(m_Levels[0].button);

            Debug.Log("[LevelSelectPanel] 关卡选择面板已打开。", this);
        }

        /// <summary>关闭面板，回主菜单。</summary>
        public void Close()
        {
            if (!m_IsOpen)
            {
                return;
            }

            m_IsOpen = false;

            if (m_Root != null)
            {
                m_Root.SetActive(false);
            }

            SetMenuButtonsEnabled(true);
            MenuButton.ResetNavigationState();
            UiEscapeRouter.SelectMenuButton(m_MenuButtons);

            if (OpenStateChanged != null)
            {
                OpenStateChanged(false);
            }
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

        // ==================== 关卡 ====================

        /// <summary>按名字刷新每个关卡按钮的文字。</summary>
        private void RefreshLevels()
        {
            if (m_Levels == null)
            {
                return;
            }

            for (int i = 0; i < m_Levels.Length; i++)
            {
                LevelEntry entry = m_Levels[i];
                if (entry == null || entry.label == null)
                {
                    continue;
                }

                // 文字留空时，用场景名兜底
                if (string.IsNullOrEmpty(entry.label.text) && !string.IsNullOrEmpty(entry.sceneName))
                {
                    entry.label.text = entry.sceneName;
                }
            }
        }

        /// <summary>加载第 index 个关卡。场景名无效时只打日志。</summary>
        public void SelectLevel(int index)
        {
            if (m_Levels == null || index < 0 || index >= m_Levels.Length)
            {
                return;
            }

            LevelEntry entry = m_Levels[index];
            if (entry == null || string.IsNullOrEmpty(entry.sceneName))
            {
                Debug.LogWarning("[LevelSelectPanel] 第 " + index + " 个关卡没有填场景名。", this);
                return;
            }

            LoadLevel(entry.sceneName);
        }

        /// <summary>按场景名加载关卡，并记下「上一次玩的关卡」。</summary>
        public void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                PlayerPrefs.SetString(LastLevelPrefsKey, sceneName);
                PlayerPrefs.Save();

                Debug.Log("[LevelSelectPanel] 加载关卡：" + sceneName);

                if (LevelChosen != null)
                {
                    LevelChosen(sceneName);
                }

                SceneManager.LoadScene(sceneName);
                return;
            }

            // 最常见的原因：场景没加进 Build Settings
            Debug.LogError("[LevelSelectPanel] 场景 \"" + sceneName + "\" 不在 Build Settings 里，无法加载。" +
                           "菜单 PAO → UI → 构建关卡选择面板 会自动把它们加进去。", this);
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

        /// <summary>组件右键菜单里的一键开关，编辑期间点亮面板用。</summary>
        [ContextMenu("切换编辑显示（编辑器用）")]
        private void ToggleEditVisibility()
        {
            if (m_Root == null)
            {
                m_Root = gameObject;
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning("[LevelSelectPanel] 正在运行中，请用 Open()/Close() 控制面板。");
                return;
            }

            bool next = !m_Root.activeSelf;
            m_Root.SetActive(next);
            Debug.Log("[LevelSelectPanel] 编辑显示已" + (next ? "点亮" : "隐藏") + "，改完记得 Ctrl+S 保存场景。");
        }
    }
}
