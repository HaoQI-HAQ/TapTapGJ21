using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIWidgets
    {
        public readonly UIConfig Config;
        public UIWidgets(UIConfig config) { Config = config; }
        public Color Ink { get { return UISettings.Current.highContrast ? Color.black : Config.ink; } }

        public RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect, float inset = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
        }

        public Image Surface(string name, Transform parent, Color color, bool rounded = false)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded && Config.round != null) { image.sprite = Config.round; image.type = Image.Type.Sliced; }
            return image;
        }

        public Text Label(Transform parent, string text, int size = 24, Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = Rect("Text", parent).gameObject.AddComponent<Text>();
            label.font = Config.font != null ? Config.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text; label.fontSize = Mathf.RoundToInt(size * UISettings.Current.textScale);
            label.color = color ?? Ink; label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = label.fontSize * 1.6f;
            label.gameObject.AddComponent<UIStyledText>().Configure(size, color == null, Config);
            return label;
        }

        public RectTransform Column(string name, Transform parent, int padding = 0, float gap = 16)
        {
            RectTransform rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = gap; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            return rect;
        }

        public RectTransform Row(string name, Transform parent, float gap = 16)
        {
            RectTransform rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return rect;
        }

        public static LayoutElement Size(Transform target, float height, float width = -1, float flexibleWidth = 0)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height; element.preferredHeight = height;
            if (width >= 0) { element.minWidth = width; element.preferredWidth = width; }
            element.flexibleWidth = flexibleWidth;
            return element;
        }

        public Button Button(Transform parent, string text, Action click, bool primary = false, bool enabled = true)
        {
            Image surface = Surface(text, parent, primary ? Config.teal : Config.mist, true);
            Button button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface; button.interactable = enabled;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.78f, 0.91f, 0.88f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.64f, 0.8f, 0.78f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.52f);
            colors.fadeDuration = 0.1f; button.colors = colors;
            Text label = Label(surface.transform, text, 24, primary ? Color.white : Ink, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(20, 4);
            label.rectTransform.offsetMax = new Vector2(-20, -4);
            Size(surface.transform, 72, -1, 1);
            button.onClick.AddListener(() => { if (click != null) click(); });
            surface.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            surface.gameObject.AddComponent<UIAdaptiveButton>();
            return button;
        }

        public RectTransform Scroll(Transform parent)
        {
            RectTransform root = Rect("Scroll", parent);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            Image background = root.gameObject.AddComponent<Image>(); background.color = new Color(1, 1, 1, 0.001f);
            RectTransform viewport = Rect("Viewport", root); Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Column("Content", viewport, 4, 16);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            var layout = root.gameObject.AddComponent<LayoutElement>(); layout.flexibleHeight = 1; layout.minHeight = 120;
            return content;
        }

        public Slider Slider(Transform parent, string title, float value, float min, float max, Action<float> changed)
        {
            RectTransform column = Column(title, parent, 0, 6);
            Text label = Label(column, title + "  " + value.ToString("0.0"), 22);
            RectTransform track = Rect("Slider", column); Size(track, 38);
            Image background = track.gameObject.AddComponent<Image>(); background.color = Config.mist;
            Slider slider = track.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max;
            Image fill = Surface("Fill", track, Config.teal, true); Stretch(fill.rectTransform);
            Image handle = Surface("Handle", track, Config.ink, true);
            handle.rectTransform.sizeDelta = new Vector2(28, 38);
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle; slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => { label.text = title + "  " + v.ToString("0.0"); changed(v); });
            track.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            return slider;
        }

        public void Toggle(Transform parent, string title, bool value, Action<bool> changed)
        {
            bool current = value;
            string on = UISettings.Text("开启", "On"), off = UISettings.Text("关闭", "Off");
            Button button = null;
            button = Button(parent, title + "  ·  " + (current ? on : off), () =>
            {
                current = !current; changed(current);
                button.GetComponentInChildren<Text>().text = title + "  ·  " + (current ? on : off);
            });
        }
    }

    // Selection remains visible in addition to the button's tint, including without color cues.
    public sealed class UIFocusRing : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Outline outline;
        public void Configure(Color color)
        {
            outline = gameObject.AddComponent<Outline>(); outline.effectColor = color;
            outline.effectDistance = new Vector2(3, -3); outline.enabled = false;
        }
        public void OnSelect(BaseEventData data)
        {
            if (outline != null) outline.enabled = true;
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            Canvas.ForceUpdateCanvases();
            var rect = transform as RectTransform;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, rect);
            Rect view = scroll.viewport.rect;
            float shift = bounds.min.y < view.yMin ? view.yMin - bounds.min.y :
                          bounds.max.y > view.yMax ? view.yMax - bounds.max.y : 0;
            scroll.content.anchoredPosition += Vector2.up * shift;
        }
        public void OnDeselect(BaseEventData data) { if (outline != null) outline.enabled = false; }
    }

    public sealed class UIPage : MonoBehaviour
    {
        public RectTransform Content;
        public RectTransform Footer;
        public RectTransform NoticeArea;
        public Text NoticeLabel;
        public GameObject ReturnFocus;
        public Action BeforeClose;
        public Action Rebuild;
        public bool CanClose = true;
        private CanvasGroup group;
        private Sequence transition;

        public void Show()
        {
            Kill(); group = GetComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = true; group.interactable = true;
            transition = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            transition.Append(group.DOFade(1, UISettings.Current.reducedMotion ? 0.12f : 0.22f));
        }
        public void Hide() { Kill(); gameObject.SetActive(false); }
        public void Refresh() { Canvas.ForceUpdateCanvases(); }
        public void SetInteractable(bool value)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            group.interactable = value; group.blocksRaycasts = value;
        }
        public void SetVisible(bool value)
        {
            Kill();
            if (group == null) group = GetComponent<CanvasGroup>();
            group.alpha = value ? 1 : 0;
        }
        public void RestoreFocus()
        {
            foreach (Selectable control in GetComponentsInChildren<Selectable>())
                if (control.IsActive() && control.IsInteractable()) { control.Select(); return; }
        }
        private void Kill() { if (transition != null) transition.Kill(); transition = null; }
        private void OnDisable() { Kill(); }
        private void OnDestroy() { Kill(); }
    }

    public sealed class UISafeArea : MonoBehaviour
    {
        private Rect previous;
        private Vector2 previousSize;
        private void Update()
        {
            Rect area = Screen.safeArea;
            Vector2 size = new Vector2(Screen.width, Screen.height);
            if (size.x <= 0 || size.y <= 0 || (area == previous && size == previousSize)) return;
            previous = area; previousSize = size;
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = area.position / size; rect.anchorMax = (area.position + area.size) / size;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
