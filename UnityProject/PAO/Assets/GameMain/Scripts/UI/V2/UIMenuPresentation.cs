using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
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
}
