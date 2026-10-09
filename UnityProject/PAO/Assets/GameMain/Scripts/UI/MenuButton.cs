using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 菜单按钮的通用交互表现：
    ///   · 默认只显示文字，底图完全透明（不可见但可点击）
    ///   · 鼠标悬停 / 按住 / 触碰 / 手柄（键盘）选中时，底图淡入，文字轻微放大
    ///   · 松开或失去焦点时，底图淡出，文字缩回
    ///
    /// 为什么不用 Button 自带的 Transition：
    ///   uGUI 的 ColorTint 只能表现 Highlighted（悬停）与 Pressed（按下），
    ///   而「手柄 / 键盘导航选中」走的是 Selected 状态，三者在同一张 Graphic 上
    ///   会互相覆盖。这里统一由本脚本驱动，行为在四种输入下完全一致。
    ///
    /// 选中态依赖 EventSystem.currentSelectedGameObject 轮询：
    ///   手柄与键盘导航由 StandaloneInputModule 维护选中物体，没有对应的
    ///   MonoBehaviour 回调接口，轮询是唯一不依赖反射的做法，且只比较引用，开销可忽略。
    ///
    /// 【重要】选中高亮必须等玩家真的用了方向输入才亮：
    ///   EventSystem 的 First Selected 会让某个按钮在开局就处于「已选中」状态，
    ///   那是为了让手柄一上来就能导航。但若直接把 Selected 当成高亮条件，
    ///   这个按钮的底图会从开局就常亮、不按方向键就永远不灭。
    ///   所以这里用 m_NavigationEngaged 把「已选中」与「已导航」区分开。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("PAO/UI/Menu Button")]
    public sealed class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [Header("引用")]
        [Tooltip("悬停/选中时淡入的底图。留空则自动取第一个名为 Background 的子物体")]
        [SerializeField] private Image m_Background;

        [Tooltip("需要放大的文字。留空则自动取子物体上的 TMP_Text / Text")]
        [SerializeField] private RectTransform m_Text;

        [Header("表现")]
        [Tooltip("悬停 / 按下 / 选中时底图的最终不透明度")]
        [SerializeField, Range(0f, 1f)] private float m_ActiveAlpha = 0.92f;

        [Tooltip("文字放大到的倍数")]
        [SerializeField, Range(1f, 2f)] private float m_TextScale = 1.12f;

        [Tooltip("底图淡入淡出速度（每秒）")]
        [SerializeField, Min(0.01f)] private float m_FadeSpeed = 12f;

        [Tooltip("文字缩放速度（每秒）")]
        [SerializeField, Min(0.01f)] private float m_ScaleSpeed = 14f;

        private Button m_Button;
        private bool m_PointerInside;
        private bool m_PointerDown;
        private bool m_Selected;
        private bool m_NavigationEngaged;
        private float m_Alpha;
        private float m_Scale = 1f;

        /// <summary>
        /// 玩家是否已经用过方向输入（方向键 / WASD / 手柄摇杆 / 十字键）。
        /// 只有为 true 时，被选中的按钮才显示高亮；鼠标悬停与按下不受它限制。
        /// 存档界面回到前台时可以设为 false，把高亮收掉。
        /// </summary>
        public bool NavigationEngaged
        {
            get { return m_NavigationEngaged; }
            set
            {
                m_NavigationEngaged = value;
                if (!value)
                {
                    // 不能碰 m_Selected：选中物体本身要留着，手柄才有落点
                    m_Alpha = 0f;
                    m_Scale = 1f;
                    ApplyVisual();
                }
            }
        }

        /// <summary>当前是否处于「激活」表现。导航选中需要配合 NavigationEngaged。</summary>
        public bool IsActive
        {
            get { return m_PointerInside || m_PointerDown || (m_Selected && m_NavigationEngaged); }
        }

        private void Awake()
        {
            m_Button = GetComponent<Button>();
            ResolveReferences();

            // 首帧就摆到「只有文字」的状态，避免出现一帧的底图闪动
            m_Alpha = 0f;
            m_Scale = 1f;
            ApplyVisual();
        }

        private void OnValidate()
        {
            // 只补引用，不改颜色/缩放 —— OnValidate 会写回场景，
            // 在这里改表现会把「默认隐藏」的状态弄脏
            if (m_Button == null)
            {
                m_Button = GetComponent<Button>();
            }

            ResolveReferences();
        }

        private void Update()
        {
            // 选中态每帧轮询一次：手柄导航时 uGUI 只改 EventSystem 的选中物体，
            // 不保证会走 ISelectHandler（例如鼠标点击后手柄接管的情况）
            EventSystem es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;
            m_Selected = selected != null && selected == gameObject;

            // 玩家一旦动过方向键 / 摇杆，之后的选中就该亮起来
            if (!m_NavigationEngaged && HasNavigationInput())
            {
                m_NavigationEngaged = true;
            }

            // 按钮被禁用 / 不可交互时，不应该保留激活表现
            bool interactable = m_Button == null || m_Button.interactable;
            bool wantActive = IsActive && interactable;

            float targetAlpha = wantActive ? m_ActiveAlpha : 0f;
            float targetScale = wantActive ? m_TextScale : 1f;

            m_Alpha = Mathf.MoveTowards(m_Alpha, targetAlpha, m_FadeSpeed * Time.unscaledDeltaTime);
            m_Scale = Mathf.MoveTowards(m_Scale, targetScale, m_ScaleSpeed * Time.unscaledDeltaTime);

            ApplyVisual();
        }

        /// <summary>把当前的 m_Alpha / m_Scale 写到 Graphic 上。</summary>
        private void ApplyVisual()
        {
            if (m_Background != null)
            {
                Color c = m_Background.color;
                if (!Mathf.Approximately(c.a, m_Alpha))
                {
                    c.a = m_Alpha;
                    m_Background.color = c;
                }

                // 全透明时关掉射线检测，避免看不到的底图抢走点击
                bool raycast = m_Alpha > 0.001f;
                if (m_Background.raycastTarget != raycast)
                {
                    m_Background.raycastTarget = raycast;
                }
            }

            if (m_Text != null && !Mathf.Approximately(m_Text.localScale.x, m_Scale))
            {
                m_Text.localScale = new Vector3(m_Scale, m_Scale, 1f);
            }
        }

        /// <summary>
        /// 这一帧玩家有没有动方向。用 GetAxisRaw，死区内的摇杆漂移不会误触发。
        /// 轴名沿用 Unity 默认的 Horizontal / Vertical（方向键、WASD、手柄都在里面）。
        /// </summary>
        private static bool HasNavigationInput()
        {
            return Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f
                || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;
        }

        /// <summary>把引用补齐，尽量做到挂上就能用、不需要手拖。</summary>
        private void ResolveReferences()
        {
            if (m_Background == null)
            {
                m_Background = FindChildImage("Background");
            }

            if (m_Text == null)
            {
                // 优先 TMP，其次 uGUI Text，最后退化到第一个非底图的子 RectTransform
                Component text = GetComponentInChildren<TMPro.TMP_Text>(true);
                if (text == null)
                {
                    text = GetComponentInChildren<Text>(true);
                }

                if (text != null)
                {
                    m_Text = text.transform as RectTransform;
                }
                else
                {
                    Transform fallback = transform.Find("Text (TMP)");
                    if (fallback == null)
                    {
                        fallback = transform.Find("Text");
                    }

                    m_Text = fallback as RectTransform;
                }
            }
        }

        private Image FindChildImage(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                return null;
            }

            return child.GetComponent<Image>();
        }

        // ==================== 指针 / 选中事件 ====================

        public void OnPointerEnter(PointerEventData eventData)
        {
            m_PointerInside = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_PointerInside = false;
            m_PointerDown = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_PointerDown = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_PointerDown = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            // 只记录选中；是否显示高亮交给 Update 里的 NavigationEngaged 判断，
            // 否则 First Selected 那个按钮会从开局就一直亮着
            m_Selected = true;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            m_Selected = false;
        }
    }
}
