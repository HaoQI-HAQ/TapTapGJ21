using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO
{
    /// <summary>
    /// 泡泡类型。
    /// </summary>
    public enum BubbleType
    {
        Sticky = 0,     // 浮粘泡泡：漂浮上升，会粘住碰到的泡泡（已实现）
        Bouncy = 1,     // 弹力泡泡：碰到东西会弹跳（待实现）
        Bomb = 2        // 炸弹泡泡：会爆炸（待实现）
    }

    /// <summary>
    /// 泡泡类型切换器。
    ///
    /// 操作：
    ///   按 1 / 2 / 3   直接选定
    ///   鼠标滚轮上下    循环切换
    ///
    /// UI：左下角显示当前泡泡名。没指定 Label 时会在运行时自动建一个。
    /// HUD 的亮圈订阅 <see cref="TypeChanged"/> 来跟着切换。
    /// </summary>
    public class BubbleTypeSwitcher : MonoBehaviour
    {
        [Header("切换输入")]
        [Tooltip("启用 1 / 2 / 3 快捷键")]
        [SerializeField] private bool m_EnableHotkeys = true;

        [Tooltip("启用鼠标滚轮循环切换")]
        [SerializeField] private bool m_EnableScrollWheel = true;

        [Tooltip("滚轮触发阈值。太小会被触控板抖动误触")]
        [SerializeField] private float m_ScrollThreshold = 0.05f;

        [Header("UI")]
        [Tooltip("显示当前泡泡名的 Text。留空则运行时自动创建在左下角")]
        [SerializeField] private Text m_Label;

        [Tooltip("距屏幕左下角的像素距离")]
        [SerializeField] private Vector2 m_UiOffset = new Vector2(28f, 24f);

        [SerializeField] private int m_FontSize = 30;

        [Header("输入开关")]
        [Tooltip("暂停菜单打开时由外部设为 false，避免在菜单里误切类型")]
        [SerializeField] private bool m_InputEnabled = true;

        private static readonly string[] s_DisplayNames = { "浮粘泡泡", "弹力泡泡", "炸弹泡泡" };

        private static readonly Color[] s_TypeColors =
        {
            new Color(0.55f, 0.85f, 1f),    // 浮粘：淡蓝
            new Color(0.55f, 1f, 0.62f),    // 弹力：淡绿
            new Color(1f, 0.62f, 0.42f)     // 炸弹：橙红
        };

        /// <summary>类型数量（HUD 建按钮时用）。</summary>
        public const int TypeCount = 3;

        /// <summary>场景里当前活动的切换器。</summary>
        public static BubbleTypeSwitcher Instance { get; private set; }

        /// <summary>类型被切换时抛出，参数是新类型。HUD 靠它刷新亮圈。</summary>
        public event Action<BubbleType> TypeChanged;

        /// <summary>某个类型的显示名，不需要实例也能取。</summary>
        public static string DisplayNameOf(BubbleType type)
        {
            int i = Mathf.Clamp((int)type, 0, s_DisplayNames.Length - 1);
            return s_DisplayNames[i];
        }

        /// <summary>某个类型的代表色，不需要实例也能取。</summary>
        public static Color ColorOf(BubbleType type)
        {
            int i = Mathf.Clamp((int)type, 0, s_TypeColors.Length - 1);
            return s_TypeColors[i];
        }

        /// <summary>当前选中的泡泡类型。</summary>
        public BubbleType Current { get; private set; }

        /// <summary>输入是否启用。暂停菜单打开时设为 false。</summary>
        public bool InputEnabled
        {
            get { return m_InputEnabled; }
            set { m_InputEnabled = value; }
        }

        /// <summary>当前类型的代表色，发射器用它给泡泡上色。</summary>
        public Color CurrentColor
        {
            get { return s_TypeColors[(int)Current]; }
        }

        /// <summary>当前类型的显示名。</summary>
        public string CurrentName
        {
            get { return s_DisplayNames[(int)Current]; }
        }

        /// <summary>暂停菜单打开时隐藏左下角的当前类型提示，关闭后恢复。</summary>
        public void SetLabelVisible(bool visible)
        {
            if (m_Label != null)
            {
                m_Label.gameObject.SetActive(visible);
            }
        }

        private void Awake()
        {
            Instance = this;
            EnsureLabel();
            Refresh();
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
            if (!m_InputEnabled)
            {
                return;
            }

            if (m_EnableHotkeys)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) { SetType(BubbleType.Sticky); return; }
                if (Input.GetKeyDown(KeyCode.Alpha2)) { SetType(BubbleType.Bouncy); return; }
                if (Input.GetKeyDown(KeyCode.Alpha3)) { SetType(BubbleType.Bomb); return; }
            }

            if (m_EnableScrollWheel)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) >= m_ScrollThreshold)
                {
                    Cycle(scroll > 0f ? 1 : -1);
                }
            }
        }

        /// <summary>直接指定类型。</summary>
        public void SetType(BubbleType type)
        {
            if (Current == type)
            {
                return;
            }

            Current = type;
            Refresh();

            // 通知外界（HUD 的亮圈就靠这一句跟着变）
            if (TypeChanged != null)
            {
                TypeChanged(Current);
            }
        }

        /// <summary>按方向循环切换：+1 下一个，-1 上一个。</summary>
        public void Cycle(int direction)
        {
            int count = s_DisplayNames.Length;
            int next = ((int)Current + direction) % count;
            if (next < 0)
            {
                next += count;
            }

            SetType((BubbleType)next);
        }

        private void Refresh()
        {
            if (m_Label == null)
            {
                return;
            }

            m_Label.text = CurrentName;
            m_Label.color = CurrentColor;
        }

        /// <summary>
        /// 没手动指定 Label 时，运行时自动在左下角建一个。
        /// </summary>
        private void EnsureLabel()
        {
            if (m_Label != null)
            {
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("BubbleUI");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);

                canvasObject.AddComponent<GraphicRaycaster>();
            }

            GameObject labelObject = new GameObject("BubbleTypeLabel");
            labelObject.transform.SetParent(canvas.transform, false);

            m_Label = labelObject.AddComponent<Text>();
            m_Label.font = ResolveFont();
            m_Label.fontSize = m_FontSize;
            m_Label.fontStyle = FontStyle.Bold;
            m_Label.alignment = TextAnchor.LowerLeft;
            m_Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Label.verticalOverflow = VerticalWrapMode.Overflow;
            m_Label.raycastTarget = false;

            RectTransform rect = m_Label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = m_UiOffset;
            rect.sizeDelta = new Vector2(480f, 64f);
        }

        /// <summary>
        /// 找一个能显示中文的字体。
        /// Unity 内置的 LegacyRuntime 只覆盖 ASCII，直接用中文会显示成方块。
        /// </summary>
        private Font ResolveFont()
        {
            Font font = Font.CreateDynamicFontFromOSFont(
                new string[] { "Microsoft YaHei", "SimHei", "SimSun", "PingFang SC", "Arial" },
                Mathf.Max(16, m_FontSize));

            if (font != null)
            {
                return font;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
