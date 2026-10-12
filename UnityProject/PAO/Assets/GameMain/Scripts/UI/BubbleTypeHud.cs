using System;
using PAO.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PAO
{
    /// <summary>
    /// 泡泡类型 HUD：右下角三个圆，当前选中的那种泡泡对应的圆会亮起。
    ///
    /// 布局由编辑器脚本 LevelHudBuilder 搭好，这里只负责「跟着类型变」。
    /// 切换来源可以是 1/2/3 快捷键、滚轮，或直接点这三个圆。
    ///
    /// 顺带负责 Esc 开关暂停菜单 —— 因为暂停面板是未激活的，
    /// 它自己的 Update 不会跑，必须由外面这个常驻组件来收 Esc。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Bubble Type HUD")]
    public sealed class BubbleTypeHud : MonoBehaviour
    {
        /// <summary>一个类型对应的圆圈。</summary>
        [Serializable]
        public class TypeSlot
        {
            [Tooltip("按钮根物体（负责缩放）")]
            public GameObject root;

            [Tooltip("圆圈底图，颜色和透明度由这里控制")]
            public Image ring;

            [Tooltip("圆里的图标，留空则只有纯色圆")]
            public Image icon;

            [Tooltip("类型名字")]
            public TMPro.TMP_Text label;

            [Tooltip("点击这个圆就切到这个类型")]
            public Button button;
        }

        [Header("三个类型圆")]
        [Tooltip("顺序必须和 BubbleType 的 Sticky / Bouncy / Bomb 一致")]
        [SerializeField] private TypeSlot[] m_Slots = new TypeSlot[0];

        [Header("亮起表现")]
        [Tooltip("选中的圆放大到多少")]
        [SerializeField] private float m_ActiveScale = 1.18f;

        [Tooltip("未选中的圆缩到多少")]
        [SerializeField] private float m_IdleScale = 1f;

        [Tooltip("未选中的圆透明度")]
        [SerializeField] private float m_IdleAlpha = 0.32f;

        [Tooltip("选中的圆透明度")]
        [SerializeField] private float m_ActiveAlpha = 1f;

        [Tooltip("未选中时颜色变灰的程度，0 = 保留原色")]
        [SerializeField] private float m_IdleDesaturate = 0.55f;

        [Tooltip("过渡速度")]
        [SerializeField] private float m_LerpSpeed = 14f;

        [Header("暂停菜单")]
        [Tooltip("按 Esc 开关这个暂停面板")]
        [SerializeField] private PauseMenu m_PauseMenu;

        [Tooltip("暂停时禁用泡泡类型快捷键，免得在菜单里误切")]
        [SerializeField] private bool m_DisableSwitcherWhenPaused = true;

        private BubbleTypeSwitcher m_InitedSwitcher;
        private float[] m_CurrentScale;
        private float[] m_CurrentAlpha;
        private bool m_VisualsVisible = true;

        // 设计稿中的原始三角布局，整体固定在屏幕右下角。
        private static readonly Vector2[] s_OriginalSlotPositions =
        {
            new Vector2(109.3f, -42f),
            new Vector2(12.7f, -192f),
            new Vector2(202f, -192f)
        };

        private void Awake()
        {
            RestoreBottomRightLayout();

            int count = (m_Slots != null) ? m_Slots.Length : 0;
            m_CurrentScale = new float[count];
            m_CurrentAlpha = new float[count];

            for (int i = 0; i < count; i++)
            {
                m_CurrentScale[i] = m_IdleScale;
                m_CurrentAlpha[i] = m_IdleAlpha;
            }

            // 圆可以点：点了就切类型
            for (int i = 0; i < count; i++)
            {
                if (m_Slots[i] == null || m_Slots[i].button == null)
                {
                    continue;
                }

                int index = i;   // 闭包捕获
                m_Slots[i].button.onClick.AddListener(delegate { SelectSlot(index); });
            }
        }

        /// <summary>
        /// 场景实例可能保留旧的 RectTransform 覆盖值，运行时统一恢复为原始右下角布局。
        /// 左下角的当前泡泡名称由 BubbleTypeSwitcher 独立显示。
        /// </summary>
        private void RestoreBottomRightLayout()
        {
            RectTransform root = transform as RectTransform;
            if (root == null)
            {
                return;
            }

            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-30f, 30f);
            root.sizeDelta = new Vector2(540f, 540f);

            int count = Mathf.Min(m_Slots != null ? m_Slots.Length : 0, s_OriginalSlotPositions.Length);
            for (int i = 0; i < count; i++)
            {
                if (m_Slots[i] == null || m_Slots[i].root == null)
                {
                    continue;
                }

                RectTransform slot = m_Slots[i].root.transform as RectTransform;
                if (slot == null)
                {
                    continue;
                }

                slot.anchorMin = new Vector2(0.5f, 0.5f);
                slot.anchorMax = new Vector2(0.5f, 0.5f);
                slot.pivot = new Vector2(0.5f, 0.5f);
                slot.anchoredPosition = s_OriginalSlotPositions[i];
                slot.sizeDelta = new Vector2(150f, 150f);
            }
        }

        private void Update()
        {
            BubbleTypeSwitcher switcher = BubbleTypeSwitcher.Instance;
            EnsureBound(switcher);

            if (switcher != null)
            {
                ApplyVisuals(switcher.Current);
            }

            HandleEscape();
        }

        /// <summary>只隐藏 HUD 图形，控制器本身继续运行以接收 Esc。</summary>
        public void SetVisualsVisible(bool visible)
        {
            if (m_VisualsVisible == visible)
            {
                return;
            }

            m_VisualsVisible = visible;
            if (m_Slots == null)
            {
                return;
            }

            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i] != null && m_Slots[i].root != null)
                {
                    m_Slots[i].root.SetActive(visible);
                }
            }
        }

        /// <summary>把三个圆的显示刷新成「type 亮起、其它暗下去」。</summary>
        public void ApplyVisuals(BubbleType type)
        {
            if (m_Slots == null)
            {
                return;
            }

            int active = (int)type;
            float delta = Time.unscaledDeltaTime * m_LerpSpeed;   // 暂停时也要能看见过渡

            for (int i = 0; i < m_Slots.Length; i++)
            {
                TypeSlot slot = m_Slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool isActive = (i == active);

                float targetScale = isActive ? m_ActiveScale : m_IdleScale;
                float targetAlpha = isActive ? m_ActiveAlpha : m_IdleAlpha;

                if (m_CurrentScale != null && i < m_CurrentScale.Length)
                {
                    m_CurrentScale[i] = Mathf.Lerp(m_CurrentScale[i], targetScale, delta);
                    m_CurrentAlpha[i] = Mathf.Lerp(m_CurrentAlpha[i], targetAlpha, delta);
                }

                if (slot.root != null && m_CurrentScale != null && i < m_CurrentScale.Length)
                {
                    slot.root.transform.localScale = Vector3.one * m_CurrentScale[i];
                }

                float alpha = (m_CurrentAlpha != null && i < m_CurrentAlpha.Length) ? m_CurrentAlpha[i] : targetAlpha;

                // 每个类型有自己的代表色，未选中时掺灰 + 降透明度
                Color baseColor = BubbleTypeSwitcher.ColorOf((BubbleType)i);
                Color shown = isActive ? baseColor : Color.Lerp(baseColor, Color.gray, m_IdleDesaturate);
                shown.a = alpha;

                if (slot.ring != null)
                {
                    slot.ring.color = shown;
                }

                if (slot.icon != null)
                {
                    Color iconColor = isActive ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                    iconColor.a *= alpha;
                    slot.icon.color = iconColor;
                }

                if (slot.label != null)
                {
                    slot.label.color = new Color(0.15f, 0.15f, 0.15f, Mathf.Max(alpha, 0.35f));
                }
            }
        }

        /// <summary>点第 index 个圆：切到对应类型。</summary>
        public void SelectSlot(int index)
        {
            BubbleTypeSwitcher switcher = BubbleTypeSwitcher.Instance;
            if (switcher == null)
            {
                return;
            }

            if (index < 0 || index >= BubbleTypeSwitcher.TypeCount)
            {
                return;
            }

            switcher.SetType((BubbleType)index);
            ApplyVisuals((BubbleType)index);
        }

        /// <summary>Esc 开关暂停菜单。刚绑定时先用场景里的实际状态对齐一次。</summary>
        private void HandleEscape()
        {
            if (m_PauseMenu == null)
            {
                return;
            }

            UiEscapeRouter.Handle(m_PauseMenu);
        }

        /// <summary>
        /// 首次绑定：拿到切换器实例、必要时找到暂停面板，并把当前的暂停状态同步到切换器输入开关。
        ///
        /// 暂停面板是在运行时查找的 —— 这样 HUD 预制体不需要跨预制体引用 PauseMenu
        /// （Unity 不支持一个预制体引用另一个预制体里的组件）。
        /// </summary>
        private void EnsureBound(BubbleTypeSwitcher switcher)
        {
            // 暂停面板是独立的预制体实例，序列化引用可能是空的，运行时补上
            if (m_PauseMenu == null)
            {
                m_PauseMenu = FindPauseMenu();
            }

            if (m_InitedSwitcher == switcher)
            {
                return;
            }

            m_InitedSwitcher = switcher;

            if (switcher == null || !m_DisableSwitcherWhenPaused)
            {
                return;
            }

            bool paused = (m_PauseMenu != null) && m_PauseMenu.IsOpen;
            switcher.InputEnabled = !paused;
        }

        /// <summary>
        /// 在场景里找暂停菜单（包含未激活的物体）。
        /// 这里必须写全 UnityEngine.Object —— 本文件同时 using System 和 UnityEngine，
        /// 光写 Object 会撞上 System.Object 而报 CS0104。
        /// </summary>
        private static PauseMenu FindPauseMenu()
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
#else
            PauseMenu[] all = Resources.FindObjectsOfTypeAll<PauseMenu>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.scene.IsValid())
                {
                    return all[i];
                }
            }
            return null;
#endif
        }

        /// <summary>由编辑器搭建脚本或运行时初始化调用，写入三个圆的引用。</summary>
        public void Configure(TypeSlot[] slots, PauseMenu pauseMenu)
        {
            m_Slots = slots ?? new TypeSlot[0];
            m_PauseMenu = pauseMenu;
        }

        /// <summary>组件右键菜单：编辑期间让面板亮起来方便调。</summary>
        [ContextMenu("刷新亮圈显示")]
        private void RefreshInEditor()
        {
            BubbleType current = (BubbleTypeSwitcher.Instance != null)
                ? BubbleTypeSwitcher.Instance.Current
                : BubbleType.Sticky;

            ApplyVisuals(current);
            Debug.Log("[BubbleTypeHud] 已刷新亮圈显示（当前类型：" + current + "）。");
        }
    }
}
