using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Keep one semantic column. The parent viewport handles large text without covering the footer.
    public sealed class UIMenuLayout : MonoBehaviour
    {
        private LayoutElement actions;
        private RectTransform content;
        private float previousWidth = -1;
        public bool Stacked { get; private set; }
        public void Configure(LayoutElement menu)
        {
            actions = menu; content = (RectTransform)transform.parent;
            Apply();
        }
        private void LateUpdate() { Apply(); }
        private void Apply()
        {
            if (actions == null || content == null) return;
            float width = content.rect.width - 24;
            if (width < 1 || Mathf.Abs(previousWidth - width) < 1) return;
            previousWidth = width; Stacked = width < 760;
            actions.minWidth = actions.preferredWidth = Mathf.Min(560, width);
            actions.flexibleWidth = 0;
            LayoutRebuilder.MarkLayoutForRebuild(content);
            Transform status = content.Find("Record status");
            if (status != null)
            {
                LayoutElement size = status.GetComponent<LayoutElement>();
                size.minWidth = size.preferredWidth = Mathf.Min(560, width);
            }
        }
    }

    public sealed class UIMenuButtonLayout : MonoBehaviour, ILayoutElement
    {
        private Text label, caption;
        public void Configure(Text title, Text detail) { label = title; caption = detail; }
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical()
        {
            if (caption == null) return;
            float height = caption.preferredHeight;
            caption.rectTransform.offsetMin = new Vector2(24, 10);
            caption.rectTransform.offsetMax = new Vector2(-48, -((RectTransform)transform).rect.height + height + 10);
            label.rectTransform.offsetMin = new Vector2(24, height + 18);
        }
        public float minWidth => 96;
        public float preferredWidth => -1;
        public float flexibleWidth => 1;
        public float minHeight => (caption == null ? 56 : 88) * UISettings.Current.textScale;
        public float preferredHeight => label == null ? minHeight : Mathf.Max(minHeight,
            label.preferredHeight + 16 + (caption == null ? 0 : caption.preferredHeight + 8));
        public float flexibleHeight => 0;
        public int layoutPriority => 4;
    }

    public sealed class UIMenuAmbience : MonoBehaviour
    {
        private Image art;
        private void Awake() { art = GetComponent<Image>(); }
        private void Update()
        {
            if (art == null || art.sprite == null) return;
            // A slow opacity breath on the illustration only. Text and controls stay stationary.
            Color color = new Color(1, .96f, .87f, .82f);
            color.a = UISettings.Current.reducedMotion || UISettings.Current.highContrast ? .82f : .8f + .02f * Mathf.Sin(Time.unscaledTime * 0.35f);
            art.color = color;
        }
    }

    public sealed class UIMenuResponsiveDesign : MonoBehaviour
    {
        private void LateUpdate()
        {
            RectTransform rect = (RectTransform)transform;
            RectTransform parent = (RectTransform)rect.parent;
            bool narrow = parent.rect.width < 1300;
            Vector2 maximum = new Vector2(narrow ? .9f : .53f, .95f);
            if (rect.anchorMax == maximum) return;
            rect.anchorMax = maximum; LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    public sealed class UIMenuActionContext : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Text target;
        private string description;
        public void Configure(Text label, string text) { target = label; description = text; }
        public void OnSelect(BaseEventData data) { Refresh(); }
        public void OnPointerEnter(PointerEventData data) { Refresh(); }
        public void OnPointerExit(PointerEventData data)
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            UIMenuActionContext context = selected != null ? selected.GetComponent<UIMenuActionContext>() : null;
            if (context != null) context.Refresh();
        }
        private void Refresh()
        {
            if (target == null || target.text == description) return;
            target.text = description;
            UIMenuDescriptionMotion echo = target.GetComponent<UIMenuDescriptionMotion>();
            if (echo != null) echo.Respond();
            UIPage page = GetComponentInParent<UIPage>();
            if (page != null) page.Resonate();
        }
    }

    public sealed class UIMenuNavigationHint : MonoBehaviour
    {
        private void OnEnable() { UIInput.DeviceChanged += Refresh; UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UIInput.DeviceChanged -= Refresh; UISettings.Changed -= Refresh; }
        private void Refresh()
        {
            GetComponent<Text>().text = UIInput.ControllerActive ? UISettings.Text("左摇杆 / 十字键  选择\nA  确认    B  返回", "Stick / D-pad  Navigate\nA  Confirm    B  Back") :
                UISettings.Text("方向键 / Tab  选择\nEnter  确认    Esc  返回", "Arrows / Tab  Navigate\nEnter  Confirm    Esc  Back");
        }
    }
}
