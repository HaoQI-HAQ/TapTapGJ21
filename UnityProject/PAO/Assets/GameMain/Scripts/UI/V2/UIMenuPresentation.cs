using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIMenuWordmarkMotion : MonoBehaviour
    {
        private CanvasGroup ink;
        private RectTransform rule;
        private Sequence reveal;
        private float ruleWidth;

        public void Configure(bool opening, float width = 0)
        {
            ink = gameObject.AddComponent<CanvasGroup>();
            ruleWidth = width > 0 ? width : opening ? 320 : 260;
            var line = new GameObject("Wordmark rule", typeof(RectTransform), typeof(Image));
            line.layer = 5; line.transform.SetParent(transform, false);
            rule = (RectTransform)line.transform;
            rule.anchorMin = rule.anchorMax = new Vector2(.5f, 0);
            rule.pivot = new Vector2(.5f, .5f);
            rule.anchoredPosition = new Vector2(0, 4);
            rule.sizeDelta = new Vector2(ruleWidth, 4);
            Image image = line.GetComponent<Image>(); image.color = Color.black; image.raycastTarget = false;
            if (UISettings.Current.reducedMotion) return;
            ink.alpha = 0;
            transform.localScale = Vector3.one * .93f;
            rule.sizeDelta = new Vector2(0, 4);
            reveal = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            reveal.Insert(.04f, ink.DOFade(1, .46f));
            reveal.Insert(.04f, transform.DOScale(1, .62f).SetEase(Ease.OutCubic));
            reveal.Insert(.28f, rule.DOSizeDelta(new Vector2(ruleWidth, 4), .48f).SetEase(Ease.OutCubic));
        }

        private void OnDisable()
        {
            if (reveal != null) reveal.Kill();
            if (ink != null) ink.alpha = 1;
            if (rule != null) rule.sizeDelta = new Vector2(ruleWidth, 4);
            transform.localScale = Vector3.one;
        }
    }

    public sealed class UIStartPromptMotion : MonoBehaviour
    {
        private CanvasGroup prompt;
        private Tween breath;
        private bool stopped;
        public void Configure()
        {
            stopped = false;
            prompt = gameObject.AddComponent<CanvasGroup>();
            Apply();
        }
        public void Stop()
        {
            stopped = true;
            if (breath != null) breath.Kill();
            breath = null;
            if (prompt != null) prompt.alpha = 1;
        }
        private void OnEnable() { UISettings.Changed += Apply; }
        private void OnDisable()
        {
            UISettings.Changed -= Apply;
            if (breath != null) breath.Kill();
            if (prompt != null) prompt.alpha = 1;
        }
        private void Apply()
        {
            if (prompt == null || stopped) return;
            if (breath != null) breath.Kill();
            prompt.alpha = UISettings.Current.reducedMotion ? 1 : .72f;
            if (!UISettings.Current.reducedMotion)
                breath = prompt.DOFade(1, 1.35f).SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject);
        }
    }

    public sealed class UIMenuHeroFit : MonoBehaviour
    {
        public float maximumWidth = 760;
        public int baseFontSize = 112;
        private void LateUpdate()
        {
            RectTransform rect = (RectTransform)transform;
            float available = ((RectTransform)rect.parent).rect.width - 32;
            LayoutGroup parentLayout = rect.parent.GetComponent<LayoutGroup>();
            if (available > 0 && (parentLayout == null || !parentLayout.enabled))
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(maximumWidth, available));
            if (UISettings.Current != null)
                GetComponent<Text>().resizeTextMaxSize = Mathf.RoundToInt(baseFontSize * UISettings.Current.textScale);
        }
    }

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

    public sealed class UIPauseActionStyle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private Image surface, marker, corner;
        private Text label, number;
        private UIGuideGraphic arrow;
        private Sequence response;
        private Tween press;
        private bool hovered, focused, pressed;

        public void Configure(UIWidgets ui, UIConfig theme, int index, bool compact)
        {
            Button button = GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            GetComponent<UIButtonFeedback>().enabled = false;
            GetComponent<UIFluidGradient>().enabled = false;
            GetComponent<UIAdaptiveButton>().enabled = false;
            UIWidgets.Size(transform, compact ? 58 : 72);
            UIControlAccent accent = GetComponent<UIControlAccent>();
            if (accent != null) accent.enabled = false;
            surface = GetComponent<Image>();
            surface.sprite = null; surface.type = Image.Type.Simple;
            foreach (Outline border in GetComponents<Outline>()) border.effectColor = Color.black;
            label = transform.Find("Text").GetComponent<Text>();
            label.GetComponent<UIStyledText>().Configure(compact ? 25 : 30, false, theme);
            label.fontStyle = FontStyle.Bold; label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(compact ? 64 : 74, 4);
            label.rectTransform.offsetMax = new Vector2(compact ? -34 : -44, -4);

            number = ui.Label(transform, index.ToString("00"), 18, Color.black, TextAnchor.MiddleCenter);
            number.fontStyle = FontStyle.Bold;
            RectTransform nr = number.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0, .5f); nr.pivot = new Vector2(.5f, .5f);
            nr.anchoredPosition = new Vector2(compact ? 31 : 37, 0);
            nr.sizeDelta = new Vector2(compact ? 40 : 46, 48);

            marker = ui.Surface("Navigation marker", transform, Color.black);
            marker.raycastTarget = false;
            RectTransform mr = marker.rectTransform;
            mr.anchorMin = mr.anchorMax = new Vector2(0, .5f);
            mr.pivot = new Vector2(.5f, .5f); mr.anchoredPosition = new Vector2(8, 0);
            mr.sizeDelta = new Vector2(4, 42);

            arrow = ui.Rect("Navigation arrow", transform).gameObject.AddComponent<UIGuideGraphic>();
            arrow.Symbol = UIGuideSymbol.Arrow; arrow.raycastTarget = false;
            RectTransform ar = arrow.rectTransform;
            ar.anchorMin = ar.anchorMax = new Vector2(1, .5f);
            ar.pivot = new Vector2(.5f, .5f); ar.anchoredPosition = new Vector2(-22, 0);
            ar.sizeDelta = new Vector2(23, 23);

            corner = ui.Surface("Drafting slash", transform, Color.black);
            corner.raycastTarget = false;
            RectTransform cr = corner.rectTransform;
            cr.anchorMin = cr.anchorMax = Vector2.one;
            cr.pivot = new Vector2(.5f, .5f); cr.anchoredPosition = new Vector2(-12, -10);
            cr.sizeDelta = new Vector2(15, 2); cr.localEulerAngles = new Vector3(0, 0, -45);
            Apply(true);
        }

        private void Apply(bool instant = false)
        {
            if (surface == null) return;
            bool active = hovered || focused;
            Color foreground = active ? Color.white : Color.black;
            Color background = active ? Color.black : Color.white;
            if (response != null) response.Kill();
            if (instant || UISettings.Current.reducedMotion)
            {
                surface.color = background;
                label.color = number.color = arrow.color = marker.color = corner.color = foreground;
                arrow.rectTransform.anchoredPosition = new Vector2(active ? -17 : -22, 0);
                return;
            }
            response = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            response.Join(surface.DOColor(background, .16f));
            response.Join(label.DOColor(foreground, .16f));
            response.Join(number.DOColor(foreground, .16f));
            response.Join(arrow.DOColor(foreground, .16f));
            response.Join(marker.DOColor(foreground, .16f));
            response.Join(corner.DOColor(foreground, .16f));
            response.Join(arrow.rectTransform.DOAnchorPosX(active ? -17 : -22, .18f).SetEase(Ease.OutCubic));
            response.OnKill(() => response = null);
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; pressed = false; Press(); Apply(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Press(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Press(); } }
        public void OnSelect(BaseEventData data) { focused = true; Apply(); }
        public void OnDeselect(BaseEventData data) { focused = false; Apply(); }

        private void Press()
        {
            if (press != null) press.Kill();
            RectTransform rect = (RectTransform)transform;
            Vector3 target = Vector3.one * (pressed ? .985f : 1);
            if (UISettings.Current.reducedMotion) rect.localScale = target;
            else press = rect.DOScale(target, .12f).SetEase(Ease.OutCubic).SetUpdate(true)
                .SetLink(gameObject).OnKill(() => press = null);
        }
        private void OnDisable()
        {
            if (response != null) response.Kill();
            if (press != null) press.Kill();
            transform.localScale = Vector3.one;
        }
    }

    // Pause entrance follows the supplied motion reference: two 70 ms spaced
    // underlays, a delayed panel, then numbered items rising in sequence.
    public sealed class UIPauseReveal : MonoBehaviour
    {
        private RectTransform panel;
        private RectTransform[] layers;
        private RectTransform title;
        private CanvasGroup titleGroup;
        private Image scrim;
        private Sequence entrance, exit;
        private float distance;
        private bool closing;

        public void Configure()
        {
            panel = (RectTransform)transform;
            if (UISettings.Current.reducedMotion) return;
            Canvas.ForceUpdateCanvases();
            RectTransform parent = (RectTransform)panel.parent;
            distance = Mathf.Max(panel.rect.width + 32, parent.rect.width * .4f, 480);
            RectTransform page = (RectTransform)parent.parent;
            var dimmer = new GameObject("Pause scene shade", typeof(RectTransform), typeof(Image));
            dimmer.layer = 5;
            RectTransform shadeRect = dimmer.GetComponent<RectTransform>();
            shadeRect.SetParent(page, false);
            shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            shadeRect.SetSiblingIndex(0);
            scrim = dimmer.GetComponent<Image>();
            scrim.color = new Color(0, 0, 0, 0); scrim.raycastTarget = false;
            title = page.Find("Pause overlay title") as RectTransform;
            if (title != null)
            {
                titleGroup = title.gameObject.AddComponent<CanvasGroup>();
                titleGroup.alpha = 0;
                title.anchoredPosition += new Vector2(0, -18);
            }
            layers = new RectTransform[2];
            for (int i = 0; i < layers.Length; i++)
            {
                var paper = new GameObject("Pause stagger layer " + i, typeof(RectTransform), typeof(Image));
                paper.layer = 5;
                RectTransform rect = paper.GetComponent<RectTransform>(); rect.SetParent(parent, false);
                rect.anchorMin = panel.anchorMin; rect.anchorMax = panel.anchorMax;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                Image image = paper.GetComponent<Image>(); image.raycastTarget = false;
                image.color = i == 0 ? new Color(.1f, .1f, .1f, 1) : new Color(.7f, .7f, .7f, 1);
                rect.SetSiblingIndex(panel.GetSiblingIndex());
                rect.anchoredPosition = new Vector2(distance, 0); layers[i] = rect;
            }
            panel.anchoredPosition = new Vector2(distance, 0);
            entrance = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            entrance.Insert(0, scrim.DOFade(.2f, .42f).SetEase(Ease.OutCubic));
            entrance.Insert(0, layers[0].DOAnchorPosX(0, .5f).SetEase(Ease.OutQuart));
            entrance.Insert(.07f, layers[1].DOAnchorPosX(0, .5f).SetEase(Ease.OutQuart));
            entrance.Insert(.15f, panel.DOAnchorPosX(0, .65f).SetEase(Ease.OutQuart));
            if (titleGroup != null)
            {
                entrance.Insert(.18f, titleGroup.DOFade(1, .38f));
                entrance.Insert(.18f, title.DOAnchorPosY(title.anchoredPosition.y + 18, .48f).SetEase(Ease.OutCubic));
            }
            Button[] actions = GetComponentsInChildren<Button>();
            for (int i = 0; i < actions.Length; i++)
            {
                CanvasGroup group = actions[i].gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
                Text text = actions[i].transform.Find("Text").GetComponent<Text>();
                RectTransform label = text.rectTransform;
                float homeY = label.anchoredPosition.y;
                label.anchoredPosition = new Vector2(label.anchoredPosition.x, homeY - 32);
                label.localEulerAngles = new Vector3(0, 0, -8);
                float start = .25f + i * .1f;
                entrance.Insert(start, group.DOFade(1, .45f));
                entrance.Insert(start, label.DOAnchorPosY(homeY, 1f).SetEase(Ease.OutQuart));
                entrance.Insert(start, label.DOLocalRotate(Vector3.zero, 1f).SetEase(Ease.OutQuart));
                bool first = i == 0;
                entrance.InsertCallback(start + .45f, () =>
                {
                    if (group == null) return;
                    group.interactable = true; group.blocksRaycasts = true;
                    if (first)
                    {
                        UIPage owner = GetComponentInParent<UIPage>();
                        if (owner != null) owner.RestoreFocus();
                    }
                });
            }
            entrance.OnKill(() => entrance = null);
        }

        public void Close(System.Action after)
        {
            if (closing) return;
            closing = true;
            UIPage owner = GetComponentInParent<UIPage>();
            if (owner != null) owner.SetInteractable(false);
            if (entrance != null) entrance.Kill();
            if (UISettings.Current.reducedMotion || panel == null) { after?.Invoke(); return; }
            exit = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            if (scrim != null) exit.Insert(0, scrim.DOFade(0, .3f));
            if (titleGroup != null) exit.Insert(0, titleGroup.DOFade(0, .2f));
            exit.Insert(0, panel.DOAnchorPosX(distance, .32f).SetEase(Ease.InCubic));
            if (layers != null)
                foreach (RectTransform layer in layers)
                    if (layer != null) exit.Insert(0, layer.DOAnchorPosX(distance, .32f).SetEase(Ease.InCubic));
            exit.OnComplete(() => { exit = null; after?.Invoke(); });
            exit.OnKill(() => exit = null);
        }
        private void OnDisable()
        {
            if (entrance != null) entrance.Kill();
            if (exit != null) exit.Kill();
        }
        private void OnDestroy() { OnDisable(); }
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

    // Cover-fit the painted menu sheet behind every control. The illustration stays in place under high contrast:
    // it is a pale watercolour, so black text keeps its contrast while the page still reads as one painting.
    public sealed class UIBackdropCover : MonoBehaviour
    {
        public Vector2 focus = new Vector2(.5f, .5f);
        private Image art;

        private void Awake() { art = GetComponent<Image>(); }

        private void LateUpdate()
        {
            if (art == null || art.sprite == null) return;
            RectTransform parent = transform.parent as RectTransform;
            if (parent == null) return;
            Vector2 area = parent.rect.size;
            if (area.x < 1 || area.y < 1) return;
            float aspect = art.sprite.rect.width / Mathf.Max(1, art.sprite.rect.height);
            float width = area.x, height = area.y;
            if (aspect > area.x / area.y) width = area.y * aspect; else height = area.x / aspect;
            RectTransform rect = (RectTransform)transform;
            // Centre anchors first: a stretched rect would add the page size on top of sizeDelta.
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            // Slide the excess outside the page so the requested part of the painting stays framed.
            rect.anchoredPosition = new Vector2((.5f - focus.x) * (width - area.x), (.5f - focus.y) * (height - area.y));
        }
    }

    // Poster composition: the wordmark is centred over the upper illustration, the entries sit low on the right,
    // and both collapse to a centred column when the window is taller than it is wide.
    public sealed class UIMenuPosterLayout : MonoBehaviour
    {
        public Text title;
        public RectTransform list;
        public RectTransform prompt;
        public UIMenuHeroFit fit;
        private Vector2 previous;
        private bool narrow;

        public void Configure(Text heading, RectTransform actionList, UIMenuHeroFit hero)
        {
            title = heading; list = actionList; fit = hero;
            previous = Vector2.zero; Apply();
        }

        public void Refresh() { previous = Vector2.zero; Apply(); }

        private void LateUpdate() { Apply(); }

        private void Apply()
        {
            RectTransform page = (RectTransform)transform;
            Vector2 size = page.rect.size;
            if (size.x < 1 || size.y < 1) return;
            bool portrait = size.x / size.y < 1.15f;
            if (size == previous && portrait == narrow) return;
            previous = size; narrow = portrait;

            if (title != null)
            {
                float width = portrait ? Mathf.Max(120, size.x - 64) : Mathf.Min(920, size.x * .74f);
                if (fit != null) fit.maximumWidth = width;
                RectTransform rect = title.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, portrait ? .78f : .76f);
                rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(width, portrait ? 280 : 340);
            }
            if (list != null)
            {
                list.anchorMin = portrait ? new Vector2(.06f, .06f) : new Vector2(.60f, .08f);
                list.anchorMax = portrait ? new Vector2(.94f, .50f) : new Vector2(.965f, .52f);
                list.offsetMin = list.offsetMax = Vector2.zero;
            }
            if (prompt != null)
            {
                RectTransform rect = prompt;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .50f);
                rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(320, 84);
            }
        }
    }

    // Legacy Text has no letter spacing. This mesh effect pushes every glyph right by its index, so a
    // short label can occupy the same block width as a long one instead of looking smaller.
    public sealed class UITextTracking : BaseMeshEffect
    {
        public float emWidths = 4f;
        private float tracking;
        private Text label;

        public float TargetWidth { get { return label != null ? label.fontSize * emWidths : 0f; } }

        private void LateUpdate()
        {
            if (label == null) label = GetComponent<Text>();
            if (label == null) return;
            int characters = string.IsNullOrEmpty(label.text) ? 0 : label.text.Length;
            float natural = label.preferredWidth;
            float target = label.fontSize * emWidths;
            float value = characters > 1 && natural > 1f && target > natural
                ? Mathf.Clamp((target - natural) / (characters - 1), 0f, label.fontSize * 2.2f) : 0f;
            if (Mathf.Abs(value - tracking) < .01f) return;
            tracking = value;
            label.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || Mathf.Approximately(tracking, 0f)) return;
            if (label == null) label = GetComponent<Text>();
            // Legacy Text emits four vertices per glyph. Bail out rather than scramble a different layout.
            if (label == null || label.text == null || mesh.currentVertCount != label.text.Length * 4) return;
            UIVertex vertex = default;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.position.x += tracking * (i / 4);
                mesh.SetUIVertex(vertex, i);
            }
        }
    }

    // Frameless menu row: the whole row eases to the right and a hairline grows under the label on hover
    // or keyboard focus. Idle rows sit slightly back in tone, so the active entry reads without any box.
    public sealed class UIMenuRowMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private RectTransform content;
        private UIGuideGraphic icon;
        private Text label;
        private Image rule;
        private Vector2 home;
        private Sequence response;
        private string measuredText;
        private int measuredSize = -1;
        private float measuredWidth = -1, textWidth = -1;
        private float reveal;
        private bool hovered, focused, pressed, available = true;

        public void Configure(RectTransform row, UIGuideGraphic symbol, Text text, Image underline)
        {
            content = row; icon = symbol; label = text; rule = underline;
            if (content != null) home = content.anchoredPosition;
            Respond(); Place();
        }

        private void Update() { RefreshAvailability(); }
        private void LateUpdate() { Place(); }

        // Without a surface there is no disabled tint, so an unavailable entry is dimmed by hand.
        private void RefreshAvailability()
        {
            Button button = GetComponent<Button>();
            bool now = button == null || button.IsInteractable();
            if (now == available) return;
            available = now; Respond();
        }

        public void OnPointerEnter(PointerEventData data) { hovered = true; Respond(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Respond(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Respond(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Respond(); } }
        public void OnSelect(BaseEventData data) { focused = true; Respond(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Respond(); }

        private void Respond()
        {
            if (response != null) response.Kill(); response = null;
            if (content == null) return;
            bool active = available && (hovered || focused);
            float target = active ? 1f : 0f;
            float shift = active ? (pressed ? 6 : 12) : 0;
            float rest = UISettings.Current != null && UISettings.Current.highContrast ? 1f : .78f;
            float tone = available ? Mathf.Lerp(rest, 1f, target) : .34f;

            if (UISettings.Current == null || UISettings.Current.reducedMotion)
            {
                reveal = target;
                content.anchoredPosition = home + Vector2.right * shift;
                Tint(tone); Place();
                return;
            }
            response = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            response.Join(DOTween.To(() => reveal, value => { reveal = value; Place(); }, target, .2f).SetEase(Ease.OutCubic));
            response.Join(content.DOAnchorPosX(home.x + shift, .22f).SetEase(Ease.OutCubic));
            if (label != null) response.Join(label.DOFade(tone, .18f));
            if (icon != null) response.Join(icon.DOFade(tone, .18f));
            response.OnKill(() => response = null);
        }

        private void Tint(float alpha)
        {
            if (label != null) { Color tint = label.color; tint.a = alpha; label.color = tint; }
            if (icon != null) { Color tint = icon.color; tint.a = alpha; icon.color = tint; }
        }

        private void Place()
        {
            if (label == null || rule == null) return;
            RectTransform area = (RectTransform)label.transform;
            float availableWidth = Mathf.Max(0, area.rect.width);
            if (availableWidth < 1) return;
            if (label.text != measuredText || label.fontSize != measuredSize || !Mathf.Approximately(availableWidth, measuredWidth))
            {
                measuredText = label.text; measuredSize = label.fontSize; measuredWidth = availableWidth;
                UITextTracking tracking = label.GetComponent<UITextTracking>();
                float natural = tracking != null ? Mathf.Max(tracking.TargetWidth, label.preferredWidth) : label.preferredWidth;
                textWidth = Mathf.Min(natural + 10, availableWidth);
            }
            if (textWidth <= 0) return;
            RectTransform line = rule.rectTransform;
            line.anchorMin = line.anchorMax = new Vector2(0, .5f);
            line.pivot = new Vector2(0, 1);
            line.anchoredPosition = new Vector2(-4, -label.fontSize * .56f);
            line.sizeDelta = new Vector2(textWidth * reveal, 2);
            Color ink = rule.color; ink.a = reveal * .9f; rule.color = ink;
        }
    }

    // Small text with a rule that reads as a hyperlink and still shows hover and keyboard focus.
    public sealed class UILinkUnderline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        private Text label;
        private Image rule;
        private Tween motion;
        private float reveal = .42f;
        private string measuredText;
        private int measuredSize = -1;
        private float measuredWidth = -1, textWidth = -1;
        private bool hovered, focused;

        public void Configure(Text text, Image underline) { label = text; rule = underline; Place(); }
        private void OnEnable() { UISettings.Changed += Place; }
        private void OnDisable() { UISettings.Changed -= Place; if (motion != null) motion.Kill(); motion = null; }
        private void LateUpdate() { Place(); }

        public void OnPointerEnter(PointerEventData data) { hovered = true; Respond(true); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Respond(focused); }
        public void OnSelect(BaseEventData data) { focused = true; Respond(true); }
        public void OnDeselect(BaseEventData data) { focused = false; Respond(hovered); }

        private void Respond(bool active)
        {
            if (motion != null) motion.Kill(); motion = null;
            float target = active ? 1 : .42f;
            if (UISettings.Current == null || UISettings.Current.reducedMotion) { reveal = target; Place(); return; }
            motion = DOTween.To(() => reveal, value => { reveal = value; Place(); }, target, .18f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject)
                .OnKill(() => motion = null);
        }

        private void Place()
        {
            if (label == null || rule == null) return;
            RectTransform area = (RectTransform)label.transform;
            float available = Mathf.Max(0, area.rect.width);
            if (available < 1) return;
            // Text generation is not free: only re-measure when the string, size or box actually changed.
            if (label.text != measuredText || label.fontSize != measuredSize || !Mathf.Approximately(available, measuredWidth))
            {
                measuredText = label.text; measuredSize = label.fontSize; measuredWidth = available;
                textWidth = Mathf.Min(label.preferredWidth + 10, available);
            }
            if (textWidth <= 0) return;
            RectTransform line = rule.rectTransform;
            bool left = label.alignment == TextAnchor.MiddleLeft || label.alignment == TextAnchor.UpperLeft || label.alignment == TextAnchor.LowerLeft;
            line.anchorMin = line.anchorMax = new Vector2(left ? 0 : 1, .5f);
            line.pivot = new Vector2(left ? 0 : 1, 1);
            line.sizeDelta = new Vector2(Mathf.Max(1, textWidth * Mathf.Lerp(.42f, 1, reveal)), 2);
            line.anchoredPosition = new Vector2(0, -label.fontSize * .52f);
            Color ink = rule.color; ink.a = Mathf.Lerp(.32f, 1, reveal); rule.color = ink;
        }
    }
}
