using DG.Tweening;
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
            float width = content.rect.width;
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
        private float minimumHeight = 56;
        public void Configure(Text title, Text detail, float height = 56) { label = title; caption = detail; minimumHeight = height; }
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
        public float minHeight => (caption == null ? minimumHeight : Mathf.Max(88, minimumHeight)) * UISettings.Current.textScale;
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
        private Text title, subtitle;
        private VerticalLayoutGroup content;
        private UIFlowLayout footer;
        private Vector2 previousSize;
        private float previousScale = -1;
        public void Configure(Text heading, Text secondary, VerticalLayoutGroup list, UIFlowLayout actions)
        { title = heading; subtitle = secondary; content = list; footer = actions; Apply(); }
        private void LateUpdate()
        { Apply(); }
        private void Apply()
        {
            RectTransform rect = (RectTransform)transform;
            RectTransform parent = (RectTransform)rect.parent;
            float scale = UISettings.Current.textScale;
            if (previousSize == parent.rect.size && Mathf.Approximately(previousScale, scale)) return;
            previousSize = parent.rect.size; previousScale = scale;
            if (parent.rect.width < 1) return;
            bool narrow = parent.rect.width < 1300;
            float width = Mathf.Min(620, parent.rect.width * (narrow ? .9f : .46f));
            float left = narrow ? (parent.rect.width - width) * .5f : parent.rect.width * .055f;
            rect.anchorMin = new Vector2(left / parent.rect.width, .06f);
            rect.anchorMax = new Vector2((left + width) / parent.rect.width, .95f);
            if (title != null)
            {
                float perPoint = title.preferredWidth / Mathf.Max(1, title.fontSize);
                int fontSize = Mathf.FloorToInt(Mathf.Clamp((width - 48) / Mathf.Max(1, perPoint) / scale, 44, 72));
                title.GetComponent<UIStyledText>().Configure(fontSize, true, GetComponentInParent<UIRuntime>().Config);
                title.alignment = subtitle.alignment = narrow ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                content.childAlignment = narrow ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
                footer.minimumCellWidth = 150 * scale;
            }
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    // Respond inside the button's reserved icon space; text and layout anchors stay still.
    public sealed class UIMenuEntryMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private UIGuideGraphic icon;
        private UIConfig config;
        private Vector2 origin;
        private Tween response;
        private bool hovered, focused, pressed;
        public void Configure(UIGuideGraphic symbol, UIConfig theme)
        { icon = symbol; config = theme; origin = icon.rectTransform.anchoredPosition; Refresh(); }
        private void OnEnable() { UISettings.Changed += Refresh; }
        private void OnDisable()
        {
            UISettings.Changed -= Refresh; Kill(); hovered = focused = pressed = false;
            if (icon != null) { icon.rectTransform.anchoredPosition = origin; icon.color = config.ink; }
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Refresh(); } }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(); }
        private void Refresh()
        {
            Kill(); if (icon == null || config == null) return;
            bool active = (hovered || focused) && GetComponent<Button>().IsInteractable();
            icon.color = UISettings.Current.highContrast ? Color.black : active ? config.teal : config.ink;
            Vector2 target = origin + Vector2.right * (active && !UISettings.Current.reducedMotion ? (pressed ? 2 : 6) : 0);
            if (UISettings.Current.reducedMotion) icon.rectTransform.anchoredPosition = target;
            else response = icon.rectTransform.DOAnchorPos(target, .14f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
        }
        private void Kill() { if (response != null) response.Kill(); response = null; }
        private void OnDestroy() { Kill(); }
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
