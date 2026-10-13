using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Switch the whole composition at a width breakpoint; the enclosing scroll view remains a fallback for large text.
    public sealed class UIMenuLayout : MonoBehaviour
    {
        private LayoutElement actions, art;
        private RectTransform stack;
        private Transform waters;
        private float previousWidth = -1;
        public bool Stacked { get; private set; }
        public void Configure(LayoutElement menu, LayoutElement illustration)
        {
            actions = menu; art = illustration; waters = illustration.transform.parent;
            stack = new GameObject("Stacked menu", typeof(RectTransform)).GetComponent<RectTransform>();
            stack.gameObject.layer = 5; stack.SetParent(transform, false);
            VerticalLayoutGroup vertical = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 24; vertical.childControlWidth = vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true; vertical.childForceExpandHeight = false;
            stack.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            stack.gameObject.SetActive(false);
            Apply();
        }
        private void LateUpdate() { Apply(); }
        private void Apply()
        {
            if (actions == null) return;
            float width = ((RectTransform)transform).rect.width;
            if (width < 1 || Mathf.Abs(previousWidth - width) < 1) return;
            previousWidth = width; bool narrow = width < 1080;
            if (narrow != Stacked)
            {
                Stacked = narrow;
                actions.transform.SetParent(Stacked ? stack : transform, false);
                waters.SetParent(Stacked ? stack : transform, false);
                if (!Stacked) { actions.transform.SetSiblingIndex(0); waters.SetSiblingIndex(1); }
                stack.gameObject.SetActive(Stacked);
            }
            actions.minWidth = actions.preferredWidth = Stacked ? 0 : Mathf.Clamp(width * 0.34f, 400, 488);
            actions.flexibleWidth = Stacked ? 1 : 0;
            art.minHeight = art.preferredHeight = Stacked ? 240 : Mathf.Clamp(width * 0.28f, 320, 400);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
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
            Color color = Color.white;
            color.a = UISettings.Current.reducedMotion || UISettings.Current.highContrast ? 1 : 0.97f + 0.03f * Mathf.Sin(Time.unscaledTime * 0.35f);
            art.color = color;
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
        private void Refresh() { if (target != null) target.text = description; }
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
