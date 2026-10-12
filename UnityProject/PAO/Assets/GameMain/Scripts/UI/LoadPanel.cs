using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 读取存档面板：一张全屏卡片，横向排布若干存档槽，右侧是所选记录的预览，
    /// 右下角「读取此记录」确认读取。
    ///
    /// 由主菜单的 Loading Button 打开，返回主菜单（Esc / 返回 / 读完）时隐藏。
    /// 面板在场景里是未激活的，所以它自己的 Awake 只在第一次打开时执行，
    /// 因此初始化写成幂等的 <see cref="EnsureInitialized"/>。
    ///
    /// 与 <see cref="SettingsPanel"/> 互斥：同一时刻只允许一个全屏面板打开。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Load Panel")]
    public sealed class LoadPanel : MonoBehaviour
    {
        /// <summary>一个存档槽在界面上对应的控件。</summary>
        [Serializable]
        public class SlotView
        {
            [Tooltip("整张卡的根（选中框与内容都在它下面）")]
            public GameObject root;

            [Tooltip("选中高亮框，选中时显示")]
            public GameObject selection;

            [Tooltip("槽位名字，例如「记录一」；空槽显示「空记录」")]
            public TMPro.TMP_Text nameLabel;

            [Tooltip("缩略图。空槽时整体变暗")]
            public Image thumbnail;

            [Tooltip("整张卡的按钮，点击即选中该槽")]
            public Button button;
        }

        /// <summary>
        /// 一次性把界面引用接好。给编辑器搭建脚本用，
        /// 把十几个引用打包成一个对象，避免长参数列表接错位置。
        /// </summary>
        public sealed class Config
        {
            public GameObject root;
            public SlotView[] slots;
            public TMPro.TMP_Text previewName;
            public TMPro.TMP_Text previewTime;
            public TMPro.TMP_Text previewPosition;
            public Image previewThumbnail;
            public Button loadButton;
            public Button backButton;
            public MenuButton[] menuButtons;
            public int defaultSlot;
            public string emptySlotName = "空记录";
            public bool hideBackButton;
        }

        /// <summary>由编辑器搭建脚本或运行时初始化调用，写入所有界面引用。</summary>
        public void Configure(Config config)
        {
            if (config == null)
            {
                return;
            }

            m_Root = config.root != null ? config.root : gameObject;
            m_Slots = config.slots ?? new SlotView[0];
            m_PreviewName = config.previewName;
            m_PreviewTime = config.previewTime;
            m_PreviewPosition = config.previewPosition;
            m_PreviewThumbnail = config.previewThumbnail;
            m_LoadButton = config.loadButton;
            m_BackButton = config.backButton;
            m_MenuButtons = config.menuButtons ?? new MenuButton[0];
            m_DefaultSlot = config.defaultSlot;

            if (!string.IsNullOrEmpty(config.emptySlotName))
            {
                m_EmptySlotName = config.emptySlotName;
            }

            m_HideBackButton = config.hideBackButton;
        }

        [Header("面板")]
        [Tooltip("面板根物体。留空则用本物体")]
        [SerializeField] private GameObject m_Root;

        [Header("存档槽")]
        [Tooltip("槽位控件，顺序即显示顺序")]
        [SerializeField] private SlotView[] m_Slots = new SlotView[0];

        [Tooltip("打开时默认选中第几个槽")]
        [SerializeField] private int m_DefaultSlot = 0;

        [Tooltip("空槽的名字文案")]
        [SerializeField] private string m_EmptySlotName = "空记录";

        [Header("预览")]
        [Tooltip("预览区：记录名")]
        [SerializeField] private TMPro.TMP_Text m_PreviewName;

        [Tooltip("预览区：保存时间")]
        [SerializeField] private TMPro.TMP_Text m_PreviewTime;

        [Tooltip("预览区：恢复位置")]
        [SerializeField] private TMPro.TMP_Text m_PreviewPosition;

        [Tooltip("预览区：缩略图")]
        [SerializeField] private Image m_PreviewThumbnail;

        [Header("按钮")]
        [Tooltip("「读取此记录」按钮")]
        [SerializeField] private Button m_LoadButton;

        [Tooltip("「返回」/关闭按钮")]
        [SerializeField] private Button m_BackButton;

        [Tooltip("不需要「返回」按钮时勾上")]
        [SerializeField] private bool m_HideBackButton;

        [Header("主菜单")]
        [Tooltip("打开面板时要禁用的主菜单按钮，避免手柄导航穿到面板底下")]
        [SerializeField] private MenuButton[] m_MenuButtons = new MenuButton[0];

        [Header("外观")]
        [Tooltip("选中槽名字的颜色")]
        [SerializeField] private Color m_SelectedTextColor = new Color(0.15f, 0.45f, 0.5f, 1f);

        [Tooltip("未选中槽名字的颜色")]
        [SerializeField] private Color m_NormalTextColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [Tooltip("空槽缩略图的变暗程度")]
        [SerializeField, Range(0f, 1f)] private float m_EmptyThumbnailDim = 0.35f;

        private bool m_Initialized;
        private bool m_IsOpen;
        private int m_SelectedSlot = -1;
        private SaveSlotData m_SelectedData;

        /// <summary>场景里当前活动的读档面板。</summary>
        public static LoadPanel Instance { get; private set; }

        /// <summary>面板当前是否打开。</summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        /// <summary>当前选中的槽位序号，未选中为 -1。</summary>
        public int SelectedSlot
        {
            get { return m_SelectedSlot; }
        }

        /// <summary>打开 / 关闭时抛出，参数是「现在是否打开」。</summary>
        public event Action<bool> OpenStateChanged;

        /// <summary>成功读取一份存档时抛出。</summary>
        public event Action<SaveSlotData> SlotLoaded;

        /// <summary>
        /// 初始化。
        ///
        /// 【为什么只在运行时隐藏自己】
        /// 面板在场景里是未激活的，所以第一次 SetActive(true)（就是 Open() 里那次）
        /// 会触发 Awake —— 如果这里无条件 SetActive(false)，就会把刚点亮的面板
        /// 立刻又关掉，表现是「点了没反应」。所以运行时才隐藏（此时 m_IsOpen
        /// 已被 Open() 置为 true，不会误判）；编辑器里保持可见，方便直接拖拽编辑。
        /// </summary>
        private void Awake()
        {
            Instance = this;

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

        /// <summary>
        /// 组件右键菜单里的一键开关：编辑期间点亮面板，改完点灭。
        /// 运行时（正在游戏里）不允许灭掉，否则会把打开着的面板收起来。
        /// 编辑器里改完记得 Ctrl+S 保存场景。
        /// </summary>
        [ContextMenu("切换编辑显示（编辑器用）")]
        private void ToggleEditVisibility()
        {
            if (m_Root == null)
            {
                m_Root = gameObject;
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning("[LoadPanel] 正在运行中，请用 Open()/Close() 控制面板；" +
                                 "这个开关只在编辑器里用来方便编辑。");
                return;
            }

            bool next = !m_Root.activeSelf;
            m_Root.SetActive(next);
            Debug.Log("[LoadPanel] 编辑显示已" + (next ? "点亮" : "隐藏") + "，改完记得 Ctrl+S 保存场景。");
        }

        /// <summary>
        /// 幂等初始化。面板未激活时它自己的 Awake 只在第一次打开时执行，
        /// 而每次打开都会触发一次 Awake，所以必须防重复注册监听。
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

            if (m_LoadButton != null)
            {
                m_LoadButton.onClick.RemoveListener(OnLoadButtonClicked);
                m_LoadButton.onClick.AddListener(OnLoadButtonClicked);
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

            if (m_Slots != null)
            {
                for (int i = 0; i < m_Slots.Length; i++)
                {
                    SlotView slot = m_Slots[i];
                    if (slot == null || slot.button == null)
                    {
                        continue;
                    }

                    int index = i;   // 闭包捕获：不能用循环变量本身
                    slot.button.onClick.RemoveAllListeners();
                    slot.button.onClick.AddListener(delegate { SelectSlot(index); });
                }
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

        /// <summary>
        /// 打开面板。
        ///
        /// 刻意写成幂等：即使面板已经是打开/激活状态，再调用一次也会保证它可见并刷新内容。
        /// 之前这里是「if (m_IsOpen) return;」，结果面板在场景里被手动点亮时
        /// m_IsOpen 已经是 true，点按钮就不做任何事 —— 表现就是「点了没反应」。
        /// </summary>
        public void Open()
        {
            // 面板物体可能未激活，引用先兜一层
            if (m_Root == null)
            {
                Debug.LogError("[LoadPanel] m_Root 为空，面板打不开。" +
                               "请在 Load Panel 组件上确认 Root 字段指向面板根物体。", this);
                return;
            }

            // 与设置面板互斥，避免两个全屏面板叠在一起
            if (SettingsPanel.Instance != null && SettingsPanel.Instance.IsOpen)
            {
                SettingsPanel.Instance.Close();
            }

            m_IsOpen = true;
            m_Root.SetActive(true);

            // 面板隐藏期间它自己是 disabled 的，Awake/OnEnable 不会跑，
            // 所以监听要在激活之后补注册一次（EnsureInitialized 是幂等的）。
            EnsureInitialized();

            SetMenuButtonsEnabled(false);
            RefreshSlots();
            SelectSlot(m_DefaultSlot);
            SelectFirstControl();

            RaiseChanged();

            // 排查用的确认日志：能打印出「已是激活」才说明面板真的显示出来了
            Debug.Log("[LoadPanel] 面板已打开：activeSelf=" + m_Root.activeSelf +
                      "  activeInHierarchy=" + m_Root.activeInHierarchy +
                      "  面板父物体=" + (m_Root.transform.parent != null ? m_Root.transform.parent.name : "无"), this);
        }

        /// <summary>关闭面板，回到主菜单。</summary>
        public void Close()
        {
            if (!m_IsOpen)
            {
                return;
            }

            m_IsOpen = false;
            m_Root.SetActive(false);

            SetMenuButtonsEnabled(true);
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

        // ==================== 槽位 ====================

        /// <summary>刷新所有槽位的显示（名字、空/非空、选中框）。</summary>
        public void RefreshSlots()
        {
            if (m_Slots == null)
            {
                return;
            }

            for (int i = 0; i < m_Slots.Length; i++)
            {
                SlotView view = m_Slots[i];
                if (view == null)
                {
                    continue;
                }

                SaveSlotData data = SaveSystem.LoadSlot(i);
                bool hasData = data != null;

                if (view.nameLabel != null)
                {
                    view.nameLabel.text = hasData
                        ? (string.IsNullOrEmpty(data.displayName) ? SaveSystem.DefaultNameForSlot(i) : data.displayName)
                        : m_EmptySlotName;
                }

                if (view.thumbnail != null)
                {
                    Color c = view.thumbnail.color;
                    c.a = hasData ? 1f : m_EmptyThumbnailDim;
                    view.thumbnail.color = c;
                }
            }
        }

        /// <summary>选中第 index 个槽，并把内容填进预览区。</summary>
        public void SelectSlot(int index)
        {
            if (m_Slots == null || m_Slots.Length == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, m_Slots.Length - 1);
            m_SelectedSlot = index;
            m_SelectedData = SaveSystem.LoadSlot(index);

            for (int i = 0; i < m_Slots.Length; i++)
            {
                SlotView view = m_Slots[i];
                if (view == null)
                {
                    continue;
                }

                bool selected = i == index;

                if (view.selection != null)
                {
                    view.selection.SetActive(selected);
                }

                if (view.nameLabel != null)
                {
                    view.nameLabel.color = selected ? m_SelectedTextColor : m_NormalTextColor;
                }
            }

            RefreshPreview();
            RefreshLoadButton();
        }

        private void RefreshPreview()
        {
            bool hasData = m_SelectedData != null;

            if (m_PreviewName != null)
            {
                m_PreviewName.text = hasData
                    ? (string.IsNullOrEmpty(m_SelectedData.displayName)
                        ? SaveSystem.DefaultNameForSlot(m_SelectedSlot)
                        : m_SelectedData.displayName)
                    : m_EmptySlotName;
            }

            if (m_PreviewTime != null)
            {
                m_PreviewTime.text = hasData ? m_SelectedData.SavedAtText : "—";
            }

            if (m_PreviewPosition != null)
            {
                m_PreviewPosition.text = hasData ? m_SelectedData.PositionText : "—";
            }

            if (m_PreviewThumbnail != null)
            {
                Color c = m_PreviewThumbnail.color;
                c.a = hasData ? 1f : m_EmptyThumbnailDim;
                m_PreviewThumbnail.color = c;
            }
        }

        private void RefreshLoadButton()
        {
            if (m_LoadButton != null)
            {
                // 空槽不能读取
                m_LoadButton.interactable = m_SelectedData != null;
            }
        }

        // ==================== 读取 ====================

        /// <summary>
        /// 按钮回调。UnityAction 要求无返回值，所以这里包一层，
        /// 返回值交给 <see cref="LoadSelected"/> 给代码调用方用。
        /// </summary>
        private void OnLoadButtonClicked()
        {
            LoadSelected();
        }

        /// <summary>
        /// 读取当前选中的槽。成功返回 true。
        /// 读取会把数据抛给 <see cref="SlotLoaded"/> 与 <see cref="SaveSystem.SlotLoaded"/>，
        /// 具体「进游戏 / 传送玩家」由游戏侧订阅后处理，这里不越权。
        /// </summary>
        public bool LoadSelected()
        {
            if (m_SelectedData == null)
            {
                Debug.LogWarning("[LoadPanel] 选中的是空槽，无法读取。");
                return false;
            }

            SaveSlotData data = m_SelectedData;
            Debug.Log("[LoadPanel] 读取存档：" + data.displayName + " / " + data.SavedAtText);

            if (SlotLoaded != null)
            {
                SlotLoaded(data);
            }

            SaveSystem.NotifyLoaded(data);

            // 读完先回主菜单；是否立即加载场景由订阅方决定
            Close();
            return true;
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

        private void SelectFirstControl()
        {
            GameObject first = null;

            if (m_Slots != null && m_Slots.Length > 0 && m_Slots[0] != null && m_Slots[0].button != null)
            {
                first = m_Slots[0].button.gameObject;
            }

            if (first == null && m_LoadButton != null)
            {
                first = m_LoadButton.gameObject;
            }

            if (first != null)
            {
                UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null)
                {
                    es.SetSelectedGameObject(first);
                }
            }
        }
    }
}
