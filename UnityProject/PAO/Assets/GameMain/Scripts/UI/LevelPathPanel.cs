using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 关卡选择面板：蜿蜒路径上排布关卡节点，当前关卡居中放大并标出「当前」。
    ///
    /// 交互：
    ///   · 左右滑动 / 拖动 / 点击左右箭头 → 切换当前关卡（带缓动）
    ///   · 点击某个节点 → 直接选它；已经是当前节点则进入该关卡
    ///   · A / D、← / →、滚轮      → 切换
    ///   · Enter / 空格            → 进入当前关卡
    ///   · 点击「开始」            → 进入当前关卡
    ///   · Esc                     → 关闭面板
    ///
    /// 路径形状由 m_Spacing / m_Amplitude / m_WaveLength 决定：
    /// 节点按索引排在一条正弦曲线上，当前节点始终被平移到自己那一格的中心。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Level Path Panel")]
    public sealed class LevelPathPanel : MonoBehaviour
    {
        /// <summary>一个关卡节点。所有位置/缩放/透明度都由本脚本驱动。</summary>
        [Serializable]
        public class Node
        {
            [Tooltip("节点根物体，负责位置与缩放")]
            public GameObject root;

            [Tooltip("节点底图（大圆）")]
            public Image disc;

            [Tooltip("内圈小圆")]
            public Image inner;

            [Tooltip("关卡名")]
            public TMPro.TMP_Text label;

            [Tooltip("「当前」标记，只在当前节点显示")]
            public GameObject currentBadge;

            [Tooltip("点击这个节点")]
            public Button button;

            [Tooltip("要加载的场景名")]
            public string sceneName = string.Empty;
        }

        /// <summary>一次性接线，给编辑器搭建脚本用。</summary>
        public sealed class Config
        {
            public GameObject root;
            public Node[] nodes;
            public RectTransform track;      // 所有节点的父物体，随滚动平移
            public RectTransform lineLayer;  // 路径曲线所在层
            public Button prevButton;
            public Button nextButton;
            public Button startButton;
            public Button closeButton;
            public MenuButton[] menuButtons;
            public int defaultIndex;
        }

        /// <summary>上一次选过的关卡场景名，存在 PlayerPrefs 里。</summary>
        public const string LastLevelPrefsKey = "PAO.LastLevelScene";

        [Header("面板")]
        [SerializeField] private GameObject m_Root;

        [Header("节点")]
        [Tooltip("顺序即路径顺序")]
        [SerializeField] private Node[] m_Nodes = new Node[0];

        [Tooltip("承载所有节点的容器，滚动时平移它")]
        [SerializeField] private RectTransform m_Track;

        [Tooltip("路径曲线所在层（与节点同级）")]
        [SerializeField] private RectTransform m_LineLayer;

        [Header("路径形状（1920x1080 参考分辨率）")]
        [Tooltip("相邻节点的水平间距")]
        [SerializeField] private float m_Spacing = 360f;

        [Tooltip("上下起伏的幅度")]
        [SerializeField] private float m_Amplitude = 90f;

        [Tooltip("每几个节点完成一个完整起伏")]
        [SerializeField] private float m_WaveLength = 2.6f;

        [Tooltip("垂直方向偏移，让路径整体上下移动")]
        [SerializeField] private float m_VerticalOffset = -20f;

        [Header("当前节点表现")]
        [Tooltip("当前节点的缩放")]
        [SerializeField] private float m_FocusScale = 1.55f;

        [Tooltip("普通节点的缩放")]
        [SerializeField] private float m_IdleScale = 1f;

        [Tooltip("当前节点的底图颜色")]
        [SerializeField] private Color m_FocusDiscColor = new Color(0.38f, 0.71f, 0.75f, 0.96f);

        [Tooltip("普通节点的底图颜色")]
        [SerializeField] private Color m_IdleDiscColor = new Color(0.80f, 0.88f, 0.89f, 0.88f);

        [Tooltip("内圈小圆的颜色")]
        [SerializeField] private Color m_InnerColor = new Color(0.24f, 0.61f, 0.67f, 1f);

        [Tooltip("当前节点的文字颜色")]
        [SerializeField] private Color m_FocusLabelColor = new Color(0.18f, 0.48f, 0.54f, 1f);

        [Tooltip("普通节点的文字颜色")]
        [SerializeField] private Color m_IdleLabelColor = new Color(0.39f, 0.49f, 0.54f, 0.88f);

        [Tooltip("距离中心多远开始完全淡出（按节点格数算）")]
        [SerializeField] private float m_FadeDistance = 3.2f;

        [Tooltip("缓动速度")]
        [SerializeField] private float m_LerpSpeed = 9f;

        [Header("按钮")]
        [SerializeField] private Button m_PrevButton;
        [SerializeField] private Button m_NextButton;
        [SerializeField] private Button m_StartButton;
        [SerializeField] private Button m_CloseButton;

        [Header("主菜单")]
        [SerializeField] private MenuButton[] m_MenuButtons = new MenuButton[0];

        [Header("输入")]
        [Tooltip("拖动多少像素算翻过一格")]
        [SerializeField] private float m_SwipeThreshold = 120f;

        [Tooltip("启用手柄方向键 / 键盘切换")]
        [SerializeField] private bool m_EnableKeys = true;

        private bool m_Initialized;
        private bool m_IsOpen;
        private int m_Index;

        private float m_TrackX;          // 当前实际平移量
        private float m_TrackTarget;     // 目标平移量
        private float[] m_NodeScale;     // 每个节点当前缩放
        private float[] m_NodeAlpha;     // 每个节点当前透明度

        private bool m_Dragging;
        private bool m_DragStartedByTouch;
        private Vector2 m_DragStart;
        private float m_DragStartTrackX;

        /// <summary>场景里当前活动的关卡选择面板。</summary>
        public static LevelPathPanel Instance { get; private set; }

        public bool IsOpen { get { return m_IsOpen; } }
        public int CurrentIndex { get { return m_Index; } }

        public event Action<bool> OpenStateChanged;
        public event Action<string> LevelChosen;

        public static string LastPlayedLevelScene
        {
            get { return PlayerPrefs.GetString(LastLevelPrefsKey, string.Empty); }
        }

        private void Awake()
        {
            Instance = this;

            int count = (m_Nodes != null) ? m_Nodes.Length : 0;
            m_NodeScale = new float[count];
            m_NodeAlpha = new float[count];

            // 面板常态隐藏；运行时只有在 Open() 里才会点亮
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

        /// <summary>由编辑器搭建脚本或运行时初始化调用。</summary>
        public void Configure(Config config)
        {
            if (config == null)
            {
                return;
            }

            m_Root = config.root != null ? config.root : gameObject;
            m_Nodes = config.nodes ?? new Node[0];
            m_Track = config.track;
            m_LineLayer = config.lineLayer;
            m_PrevButton = config.prevButton;
            m_NextButton = config.nextButton;
            m_StartButton = config.startButton;
            m_CloseButton = config.closeButton;
            m_MenuButtons = config.menuButtons ?? new MenuButton[0];

            if (config.defaultIndex > 0 && config.defaultIndex < m_Nodes.Length)
            {
                m_Index = config.defaultIndex;
            }
        }

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
            }

            if (m_PrevButton != null)
            {
                m_PrevButton.onClick.AddListener(Previous);
            }

            if (m_NextButton != null)
            {
                m_NextButton.onClick.AddListener(Next);
            }

            if (m_StartButton != null)
            {
                m_StartButton.onClick.AddListener(EnterCurrent);
            }

            for (int i = 0; i < m_Nodes.Length; i++)
            {
                if (m_Nodes[i] == null || m_Nodes[i].button == null)
                {
                    continue;
                }

                int index = i;
                m_Nodes[i].button.onClick.AddListener(delegate { OnNodeClicked(index); });
            }
        }

        // ==================== 开关 ====================

        /// <summary>打开面板（幂等）。</summary>
        public void Open()
        {
            if (m_Root == null)
            {
                Debug.LogError("[LevelPathPanel] m_Root 为空，面板打不开。", this);
                return;
            }

            if (SettingsPanel.Instance != null && SettingsPanel.Instance.IsOpen)
            {
                SettingsPanel.Instance.Close();
            }

            if (LoadPanel.Instance != null && LoadPanel.Instance.IsOpen)
            {
                LoadPanel.Instance.Close();
            }
            if (LevelSelectPanel.Instance != null && LevelSelectPanel.Instance.IsOpen) LevelSelectPanel.Instance.Close();

            m_IsOpen = true;
            m_Root.SetActive(true);

            EnsureInitialized();
            SetMenuButtonsEnabled(false);

            // 直接跳到「上次玩过的那一关」，没有就停在默认格
            m_Index = ResolveStartIndex();
            SnapToIndex();
            if (m_Nodes != null && m_Index < m_Nodes.Length && m_Nodes[m_Index] != null)
                UiEscapeRouter.Select(m_Nodes[m_Index].button);
        }

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

        // ==================== 选择与滚动 ====================

        /// <summary>切到上一个 / 下一个关卡，带缓动。</summary>
        public void Previous() { Step(-1); }
        public void Next() { Step(1); }

        /// <summary>按方向切换（+1 下一个，-1 上一个）。</summary>
        public void Step(int direction)
        {
            if (m_Nodes == null || m_Nodes.Length == 0)
            {
                return;
            }

            int target = Mathf.Clamp(m_Index + direction, 0, m_Nodes.Length - 1);
            if (target == m_Index)
            {
                return;
            }

            m_Index = target;
            m_TrackTarget = TargetTrackX(m_Index);
        }

        /// <summary>立即对齐到当前索引，不做缓动（打开面板时用）。</summary>
        public void SnapToIndex()
        {
            m_TrackTarget = TargetTrackX(m_Index);
            m_TrackX = m_TrackTarget;
            ApplyVisuals(1f);
        }

        private void OnNodeClicked(int index)
        {
            if (index != m_Index)
            {
                // 点非当前节点 → 先把它滚到中间
                m_Index = index;
                m_TrackTarget = TargetTrackX(m_Index);
                return;
            }

            EnterCurrent();
        }

        /// <summary>进入当前关卡。</summary>
        public void EnterCurrent()
        {
            if (m_Nodes == null || m_Index < 0 || m_Index >= m_Nodes.Length)
            {
                return;
            }

            Node node = m_Nodes[m_Index];
            if (node == null || string.IsNullOrEmpty(node.sceneName))
            {
                Debug.LogWarning("[LevelPathPanel] 第 " + m_Index + " 个节点没有填场景名。", this);
                return;
            }

            LoadLevel(node.sceneName);
        }

        /// <summary>按场景名加载关卡，并记下「上一次玩的关卡」。</summary>
        public void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[LevelPathPanel] 场景 \"" + sceneName + "\" 不在 Build Settings 里，无法加载。", this);
                return;
            }

            PlayerPrefs.SetString(LastLevelPrefsKey, sceneName);
            PlayerPrefs.Save();

            Debug.Log("[LevelPathPanel] 加载关卡：" + sceneName);

            if (LevelChosen != null)
            {
                LevelChosen(sceneName);
            }

            SceneManager.LoadScene(sceneName);
        }

        /// <summary>打开时停在哪个节点：优先上次玩过的，其次 Prefab 里设的默认值。</summary>
        private int ResolveStartIndex()
        {
            string lastName = LastPlayedLevelScene;
            if (!string.IsNullOrEmpty(lastName))
            {
                for (int i = 0; i < m_Nodes.Length; i++)
                {
                    if (m_Nodes[i] != null && m_Nodes[i].sceneName == lastName)
                    {
                        return i;
                    }
                }
            }

            return Mathf.Clamp(m_Index, 0, Mathf.Max(0, m_Nodes.Length - 1));
        }

        /// <summary>第 index 个节点居中时，容器应该平移多少。</summary>
        private float TargetTrackX(int index)
        {
            return -(index - (m_Nodes.Length - 1) * 0.5f) * m_Spacing;
        }

        /// <summary>第 index 个节点在路径上的纵向位置。</summary>
        private float PathY(int index)
        {
            float phase = (index - (m_Nodes.Length - 1) * 0.5f) / Mathf.Max(0.01f, m_WaveLength);
            return Mathf.Sin(phase * Mathf.PI) * m_Amplitude + m_VerticalOffset;
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            if (!m_IsOpen)
            {
                return;
            }

            HandleInput();
            if (!m_IsOpen) return;

            float dt = Time.unscaledDeltaTime * m_LerpSpeed;

            if (m_Dragging)
            {
                // 拖动时容器直接跟手，不做缓动
                m_TrackX = m_DragStartTrackX + (GetPointerPosition().x - m_DragStart.x);
                m_TrackTarget = m_TrackX;
            }
            else
            {
                m_TrackX = Mathf.Lerp(m_TrackX, m_TrackTarget, dt);
            }

            ApplyVisuals(dt);
        }

        private void ApplyVisuals(float dt)
        {
            if (m_Track != null)
            {
                Vector2 pos = m_Track.anchoredPosition;
                pos.x = m_TrackX;
                m_Track.anchoredPosition = pos;
            }

            if (m_Nodes == null)
            {
                return;
            }

            for (int i = 0; i < m_Nodes.Length; i++)
            {
                Node node = m_Nodes[i];
                if (node == null || node.root == null)
                {
                    continue;
                }

                // 位置：沿正弦路径排布
                RectTransform rt = node.root.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float x = (i - (m_Nodes.Length - 1) * 0.5f) * m_Spacing;
                    rt.anchoredPosition = new Vector2(x, PathY(i));
                }

                bool isCurrent = (i == m_Index);
                float targetScale = isCurrent ? m_FocusScale : m_IdleScale;

                // 越远越淡
                float distance = Mathf.Abs(i - m_Index);
                float fade = Mathf.Clamp01(1f - distance / Mathf.Max(0.01f, m_FadeDistance));
                float targetAlpha = Mathf.Max(0.15f, fade);

                if (m_NodeScale != null && i < m_NodeScale.Length)
                {
                    m_NodeScale[i] = Mathf.Lerp(m_NodeScale[i], targetScale, dt);
                    m_NodeAlpha[i] = Mathf.Lerp(m_NodeAlpha[i], targetAlpha, dt);
                    node.root.transform.localScale = Vector3.one * m_NodeScale[i];
                }

                float alpha = (m_NodeAlpha != null && i < m_NodeAlpha.Length) ? m_NodeAlpha[i] : targetAlpha;

                if (node.disc != null)
                {
                    Color c = isCurrent ? m_FocusDiscColor : m_IdleDiscColor;
                    c.a *= alpha;
                    node.disc.color = c;
                }

                if (node.inner != null)
                {
                    // 内圈实心圆只在当前节点显示 —— 其它节点是空心圈，
                    // 对应设计稿里「只有一个节点被填实」的样子
                    node.inner.enabled = isCurrent;
                    if (isCurrent)
                    {
                        Color c = m_InnerColor;
                        c.a *= alpha;
                        node.inner.color = c;
                    }
                }

                if (node.label != null)
                {
                    Color c = isCurrent ? m_FocusLabelColor : m_IdleLabelColor;
                    c.a *= alpha;
                    node.label.color = c;
                }

                if (node.currentBadge != null)
                {
                    node.currentBadge.SetActive(isCurrent);
                }
            }
        }

        // ==================== 输入 ====================

        private Vector2 GetPointerPosition()
        {
            if (Input.touchCount > 0)
            {
                return Input.GetTouch(0).position;
            }

            return Input.mousePosition;
        }

        private void HandleInput()
        {
            if (m_EnableKeys)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
                {
                    Next();
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
                {
                    Previous();
                }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
                         Input.GetKeyDown(KeyCode.Space))
                {
                    EnterCurrent();
                }

                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.05f)
                {
                    Step(scroll > 0f ? 1 : -1);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                UiEscapeRouter.Handle();
                return;
            }

            // 拖动 / 滑动
            if (Input.GetMouseButtonDown(0))
            {
                m_Dragging = true;
                m_DragStartedByTouch = false;
                m_DragStart = Input.mousePosition;
                m_DragStartTrackX = m_TrackX;
            }
            else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                m_Dragging = true;
                m_DragStartedByTouch = true;
                m_DragStart = Input.GetTouch(0).position;
                m_DragStartTrackX = m_TrackX;
            }

            if (!m_Dragging)
            {
                return;
            }

            bool released =
                (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended) ||
                (Input.touchCount == 0 && Input.GetMouseButtonUp(0));

            if (released)
            {
                EndDrag();
            }
        }

        /// <summary>松手：按拖动距离决定翻不翻页。</summary>
        private void EndDrag()
        {
            m_Dragging = false;

            float dx = GetPointerPosition().x - m_DragStart.x;

            if (Mathf.Abs(dx) >= m_SwipeThreshold)
            {
                // 手指向左拖 = 看后面的关卡
                Step(dx < 0f ? 1 : -1);
            }
            else
            {
                // 没拖动够：回到当前节点的位置
                m_TrackTarget = TargetTrackX(m_Index);
            }
        }

        // ==================== 杂项 ====================

        private void SetMenuButtonsEnabled(bool enabled)
        {
            if (m_MenuButtons == null)
            {
                return;
            }

            for (int i = 0; i < m_MenuButtons.Length; i++)
            {
                MenuButton mb = m_MenuButtons[i];
                if (mb == null)
                {
                    continue;
                }

                Button b = mb.GetComponent<Button>();
                if (b != null)
                {
                    b.interactable = enabled;
                }
            }
        }

        /// <summary>组件右键菜单：编辑期间点亮面板用。</summary>
        [ContextMenu("切换编辑显示（编辑器用）")]
        private void ToggleEditVisibility()
        {
            if (m_Root == null)
            {
                m_Root = gameObject;
            }

            if (Application.isPlaying)
            {
                Debug.LogWarning("[LevelPathPanel] 正在运行中，请用 Open()/Close() 控制面板。");
                return;
            }

            bool next = !m_Root.activeSelf;
            m_Root.SetActive(next);
            Debug.Log("[LevelPathPanel] 编辑显示已" + (next ? "点亮" : "隐藏") + "，改完记得 Ctrl+S。");
        }
    }
}
