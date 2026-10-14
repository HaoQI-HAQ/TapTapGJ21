using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡值的简易屏幕显示（IMGUI，纯测试用，不依赖 Canvas）。
    ///
    /// 【正式 UI 怎么接】
    /// 别用这个脚本，直接读 BubbleResourceManager：
    ///   Current / Max / Normalized        每帧读，画进度条
    ///   OnChanged(current, max)           值变了才回调，适合做动画
    ///   OnInsufficient(missing)           值不够，弹「泡泡值不足」提示
    ///
    /// 挂在 Player 上即可，零配置可跑。
    /// </summary>
    public class BubbleResourceHud : MonoBehaviour
    {
        [Header("显示")]
        [Tooltip("距离屏幕左上角的偏移")]
        [SerializeField] private Vector2 m_ScreenOffset = new Vector2(24f, 24f);

        [Tooltip("字号")]
        [SerializeField] private int m_FontSize = 24;

        [Header("不足提示")]
        [Tooltip("提示停留时间（秒）")]
        [SerializeField] private float m_HintDuration = 1.5f;

        private BubbleResourceManager m_Resource;
        private GUIStyle m_Style;
        private GUIStyle m_ShadowStyle;
        private GUIStyle m_HintStyle;
        private GUIStyle m_HintShadowStyle;

        private string m_Hint;
        private float m_HintTimeLeft;

        private void Awake()
        {
            m_Resource = GetComponent<BubbleResourceManager>();
        }

        private void OnEnable()
        {
            if (m_Resource != null)
            {
                m_Resource.OnInsufficient += HandleInsufficient;
            }
        }

        private void OnDisable()
        {
            if (m_Resource != null)
            {
                m_Resource.OnInsufficient -= HandleInsufficient;
            }
        }

        private void Update()
        {
            if (m_HintTimeLeft <= 0f)
            {
                return;
            }

            m_HintTimeLeft -= Time.deltaTime;
            if (m_HintTimeLeft <= 0f)
            {
                m_Hint = null;
            }
        }

        private void HandleInsufficient(int missing)
        {
            m_Hint = "泡泡值不足（还差 " + missing + "）";
            m_HintTimeLeft = m_HintDuration;
        }

        private void OnGUI()
        {
            if (m_Resource == null)
            {
                return;
            }

            EnsureStyles();

            string text = "泡泡值  " + m_Resource.Current + " / " + m_Resource.Max;
            Vector2 size = m_Style.CalcSize(new GUIContent(text));

            Rect rect = new Rect(m_ScreenOffset.x, m_ScreenOffset.y, size.x, size.y);
            DrawLabel(rect, text, m_Style, m_ShadowStyle);

            if (!string.IsNullOrEmpty(m_Hint))
            {
                Rect hintRect = new Rect(
                    m_ScreenOffset.x,
                    m_ScreenOffset.y + size.y + 6f,
                    m_Style.CalcSize(new GUIContent(m_Hint)).x,
                    size.y);

                DrawLabel(hintRect, m_Hint, m_HintStyle, m_HintShadowStyle);
            }
        }

        private void DrawLabel(Rect rect, string text, GUIStyle style, GUIStyle shadowStyle)
        {
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, shadowStyle);
            GUI.Label(rect, text, style);
        }

        private void EnsureStyles()
        {
            if (m_Style != null)
            {
                return;
            }

            m_Style = new GUIStyle(GUI.skin.label);
            m_Style.fontSize = m_FontSize;
            m_Style.fontStyle = FontStyle.Bold;
            m_Style.normal.textColor = Color.white;

            m_ShadowStyle = new GUIStyle(m_Style);
            m_ShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);

            m_HintStyle = new GUIStyle(m_Style);
            m_HintStyle.normal.textColor = new Color(1f, 0.55f, 0.45f, 1f);

            m_HintShadowStyle = new GUIStyle(m_HintStyle);
            m_HintShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }
    }
}