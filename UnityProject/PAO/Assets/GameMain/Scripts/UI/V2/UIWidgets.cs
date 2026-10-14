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
        public Color Ink { get { return UISettings.Current.highContrast ? Color.black : Config.text; } }
        private static Font interfaceFont;
        private Font InterfaceFont
        {
            get
            {
                if (interfaceFont == null)
                {
                    string[] available = Font.GetOSInstalledFontNames();
                    string name = Array.Find(available, n => n == "Microsoft YaHei UI") ?? Array.Find(available, n => n == "Microsoft YaHei") ??
                        Array.Find(available, n => n == "Noto Sans CJK SC") ?? Array.Find(available, n => n == "PingFang SC");
                    if (name != null) interfaceFont = Font.CreateDynamicFontFromOSFont(name, 32);
                }
                return interfaceFont != null ? interfaceFont : Config.font;
            }
        }

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
            Image image = rounded ? rect.gameObject.AddComponent<UILowPolyImage>() : rect.gameObject.AddComponent<Image>();
            image.color = color;

            return image;
        }

        public Text Label(Transform parent, string text, int size = 24, Color? color = null,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            Text label = Rect("Text", parent).gameObject.AddComponent<Text>();
            label.font = InterfaceFont != null ? InterfaceFont : Config.font != null ? Config.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        public Button Button(Transform parent, string text, Action click, bool primary = false, bool enabled = true, bool danger = false)
        {
            Image surface = Surface(text, parent, Color.white, true);
            var gradient = surface.gameObject.AddComponent<UIFluidGradient>();
            gradient.top = danger ? new Color32(252, 219, 209, 255) : primary ? UIDesignPalette.PrimaryTop : UIDesignPalette.ControlTop;
            gradient.bottom = danger ? new Color32(238, 190, 177, 255) : primary ? UIDesignPalette.PrimaryBottom : UIDesignPalette.ControlBottom;
            gradient.focusTint = new Color32(184, 223, 219, 255);
            gradient.contrastDark = false;
            var edge = surface.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(Config.ink.r, Config.ink.g, Config.ink.b, .16f);
            edge.effectDistance = new Vector2(1, -1);
            Button button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface; button.interactable = enabled;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.93f, 0.93f, 0.93f);
            colors.disabledColor = new Color(1, 1, 1, 0.55f);
            colors.fadeDuration = 0.1f; button.colors = colors;
            Text label = Label(surface.transform, text, 24, Config.ink, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(20, 4);
            label.rectTransform.offsetMax = new Vector2(-20, -4);
            Size(surface.transform, 72, -1, 1);
            button.onClick.AddListener(() => { if (click != null) click(); });
            surface.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            surface.gameObject.AddComponent<UIAdaptiveButton>();
            surface.gameObject.AddComponent<UIButtonFeedback>().Configure(Config, gradient);
            return button;
        }

        public Button IconButton(Transform parent, string text, UIGuideSymbol symbol, Action click, bool primary = false, bool enabled = true, bool danger = false)
        {
            Button button = Button(parent, text, click, primary, enabled, danger);
            UIGuideGraphic icon = Rect("Action symbol", button.transform).gameObject.AddComponent<UIGuideGraphic>();
            icon.Symbol = symbol; icon.color = Ink; icon.raycastTarget = false;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(34, 0); icon.rectTransform.sizeDelta = new Vector2(30, 30);
            Text label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(66, 8); label.rectTransform.offsetMax = new Vector2(-24, -8);
            return button;
        }

        public RectTransform Grid(string name, Transform parent, int columns = 2, float minimumWidth = 400)
        {
            RectTransform rect = Rect(name, parent);
            UIFlowLayout layout = rect.gameObject.AddComponent<UIFlowLayout>(); layout.maxColumns = columns; layout.minimumCellWidth = minimumWidth;
            return rect;
        }

        public RectTransform Card(string name, Transform parent, bool highlighted = false)
        {
            RectTransform rect = Column(name, parent, 24, 12);
            Image image = rect.gameObject.AddComponent<UILowPolyImage>();
            image.color = highlighted ? UIDesignPalette.Cream : UIDesignPalette.ControlTop; image.raycastTarget = false;
            Outline edge = rect.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(.1f, .3f, .35f, .12f); edge.effectDistance = new Vector2(1, -1);
            return rect;
        }

        public void Emblem(Transform parent, UIGuideSymbol symbol, string caption, Color? accent = null)
        {
            RectTransform row = Row("Card emblem", parent, 12); Size(row, 48).preferredHeight = -1;
            UIGuideGraphic icon = Rect("Card symbol", row).gameObject.AddComponent<UIGuideGraphic>();
            icon.Symbol = symbol; icon.color = accent ?? Config.teal; icon.raycastTarget = false; Size(icon.transform, 40, 40);
            Text label = Label(row, caption, 20, accent ?? Config.teal);
            Size(label.transform, -1, -1, 1).minHeight = 32;
        }

        public RectTransform Scroll(Transform parent)
        {
            RectTransform root = Rect("Scroll", parent);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60;
            Image background = root.gameObject.AddComponent<Image>(); background.color = new Color(1, 1, 1, 0.001f);
            RectTransform viewport = Rect("Viewport", root); Stretch(viewport);
            viewport.offsetMax = new Vector2(-18, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Column("Content", viewport, 4, 16);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            Image rail = Surface("Scroll rail", root, new Color(Config.teal.r, Config.teal.g, Config.teal.b, .16f), true);
            RectTransform railRect = rail.rectTransform;
            railRect.anchorMin = new Vector2(1, 0); railRect.anchorMax = Vector2.one;
            railRect.pivot = new Vector2(1, .5f); railRect.sizeDelta = new Vector2(8, 0); railRect.anchoredPosition = Vector2.zero;
            Scrollbar scrollbar = rail.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            Navigation scrollbarNavigation = scrollbar.navigation; scrollbarNavigation.mode = Navigation.Mode.None;
            scrollbar.navigation = scrollbarNavigation;
            RectTransform sliding = Rect("Sliding area", railRect); Stretch(sliding);
            Image handle = Surface("Scroll position", sliding, new Color(Config.teal.r, Config.teal.g, Config.teal.b, .78f), true);
            Stretch(handle.rectTransform); scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            var layout = root.gameObject.AddComponent<LayoutElement>(); layout.flexibleHeight = 1; layout.minHeight = 120;
            return content;
        }

        public Slider Slider(Transform parent, string title, float value, float min, float max, Action<float> changed)
        {
            RectTransform column = Column(title, parent, 0, 6);
            Text label = Label(column, title + "  " + value.ToString("0.00"), 22);
            RectTransform track = Rect("Slider", column); Size(track, 38);
            Image background = track.gameObject.AddComponent<UILowPolyImage>(); background.color = Config.mist;
            Slider slider = track.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max;
            RectTransform fillArea = Rect("Fill area", track); Stretch(fillArea);
            fillArea.offsetMin = new Vector2(14, 6); fillArea.offsetMax = new Vector2(-14, -6);
            Image fill = Surface("Fill", fillArea, Config.teal, true); Stretch(fill.rectTransform);
            RectTransform handleArea = Rect("Handle area", track); Stretch(handleArea);
            handleArea.offsetMin = new Vector2(14, 0); handleArea.offsetMax = new Vector2(-14, 0);
            Image handle = Surface("Handle", handleArea, Config.text, true);
            // Slider drives the handle's horizontal anchor and stretches its vertical anchors.
            handle.rectTransform.anchorMin = Vector2.zero; handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.sizeDelta = new Vector2(28, 0);
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle; slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => { label.text = title + "  " + v.ToString("0.00"); changed(v); });
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
            RevealSelection();
            StartCoroutine(RevealAfterLayout());
        }
        private System.Collections.IEnumerator RevealAfterLayout()
        {
            // Dynamic settings rebuild their content in this frame; reveal after the layout settles.
            yield return null;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                RevealSelection();
        }
        private void RevealSelection()
        {
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            Canvas.ForceUpdateCanvases();
            var rect = transform as RectTransform;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, rect);
            Rect view = scroll.viewport.rect;
            float shift = bounds.min.y < view.yMin ? view.yMin - bounds.min.y :
                          bounds.max.y > view.yMax ? view.yMax - bounds.max.y : 0;
            scroll.StopMovement();
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
        public UIPageDesign Design;
        private CanvasGroup group;
        private Sequence transition;
        private UIMotionProfile profile = new UIMotionProfile();
        public void Configure(UIMotionProfile motion) { profile = motion ?? new UIMotionProfile(); }

        public void Show()
        {
            Kill(); group = GetComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = true; group.interactable = true;
            transition = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            transition.Append(group.DOFade(1, UISettings.Current.reducedMotion ? .12f : profile.pageEnter));
            transition.OnKill(() => transition = null);
            if (!UISettings.Current.reducedMotion && Content != null)
            {
                CanvasGroup contentGroup = Content.GetComponent<CanvasGroup>();
                if (contentGroup == null) contentGroup = Content.gameObject.AddComponent<CanvasGroup>();
                contentGroup.alpha = 0;
                transition.Join(contentGroup.DOFade(1, profile.pageEnter * .75f).SetDelay(.04f));
                if (Footer != null)
                {
                    CanvasGroup footerGroup = Footer.GetComponent<CanvasGroup>();
                    if (footerGroup == null) footerGroup = Footer.gameObject.AddComponent<CanvasGroup>();
                    footerGroup.alpha = 0;
                    transition.Join(footerGroup.DOFade(1, profile.pageEnter * .65f).SetDelay(.08f));
                }
            }
        }
        public void Resonate(bool pressed = false)
        {
            UIMenuResonance hero = GetComponentInChildren<UIMenuResonance>();
            if (hero != null) hero.Respond(pressed);
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
                if (!(control is Scrollbar) && control.IsActive() && control.IsInteractable()) { control.Select(); return; }
        }
        private void Kill()
        {
            if (transition != null) transition.Kill(); transition = null;
            if (Content != null && Content.GetComponent<CanvasGroup>() != null) Content.GetComponent<CanvasGroup>().alpha = 1;
            if (Footer != null && Footer.GetComponent<CanvasGroup>() != null) Footer.GetComponent<CanvasGroup>().alpha = 1;
        }
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
