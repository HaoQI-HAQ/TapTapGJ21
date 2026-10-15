using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        private Button button;
        private Sequence motion, pulse;
        private UIFluidGradient film;
        private UIPearlGraphic ripple;
        private UIConfig config;
        private Color top, bottom;
        private Text label;
        private Image selectionMark;
        private Color labelColor;
        private bool hovered, focused, pressed, wasInteractable;
        private bool emphasized;
        public void SetEmphasized(bool value) { emphasized = value; Refresh(); }
        private void Awake() { button = GetComponent<Button>(); wasInteractable = button.IsInteractable(); }
        public void Configure(UIConfig theme, UIFluidGradient gradient)
        {
            config = theme; film = gradient; top = gradient.top; bottom = gradient.bottom;
            label = GetComponentInChildren<Text>();
            if (label != null) labelColor = label.color;
            var rect = new GameObject("Press ripple", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5; rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = Vector2.one * .5f; rect.sizeDelta = Vector2.one * 52;
            ripple = rect.gameObject.AddComponent<UIPearlGraphic>(); ripple.shape = UIPearlShape.Orbit;
            ripple.raycastTarget = false; ripple.color = Color.clear; rect.SetAsFirstSibling();
            var mark = new GameObject("Current category", typeof(RectTransform)).GetComponent<RectTransform>();
            mark.gameObject.layer = 5; mark.SetParent(transform, false);
            mark.anchorMin = new Vector2(.15f, 0); mark.anchorMax = new Vector2(.85f, 0);
            mark.anchoredPosition = new Vector2(0, 5); mark.sizeDelta = new Vector2(0, 3);
            selectionMark = mark.gameObject.AddComponent<Image>(); selectionMark.raycastTarget = false;
            Refresh();
        }
        private void OnEnable() { UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable()
        {
            UISettings.Changed -= Refresh; Kill(); hovered = focused = pressed = false;
            transform.localScale = Vector3.one;
            if (film != null) film.Warmth = 0;
            if (ripple != null) ripple.color = Color.clear;
        }
        private void Update()
        {
            bool available = button != null && button.IsInteractable();
            if (available != wasInteractable) { wasInteractable = available; Refresh(); }
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left && Available()) { pressed = true; Refresh(); Ripple(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Refresh(); } }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(); }
        public void OnSubmit(BaseEventData data)
        {
            if (!Available()) return;
            Ripple(); pressed = false; Refresh();
            if (!UISettings.Current.reducedMotion)
            {
                KillMotion(); transform.localScale = Vector3.one * .96f;
                motion = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
                motion.Append(transform.DOScale(focused ? 1.015f : 1, .16f).SetEase(Ease.OutCubic));
                motion.OnKill(() => motion = null);
            }
        }
        private bool Available() => button != null && button.IsInteractable() && isActiveAndEnabled;
        private void Refresh()
        {
            KillMotion(); if (film == null || config == null) return;
            if (UISettings.Current.reducedMotion && pulse != null)
            { pulse.Kill(); pulse = null; ripple.color = Color.clear; ripple.rectTransform.localScale = Vector3.one; }
            bool contrast = UISettings.Current.highContrast;
            Color contrastTop = film.contrastDark ? Color.black : Color.white;
            Color contrastBottom = film.contrastDark ? Color.black : Color.white;
            film.top = contrast ? contrastTop : emphasized ? UIDesignPalette.PrimaryTop : top;
            film.bottom = contrast ? contrastBottom : emphasized ? UIDesignPalette.PrimaryBottom : bottom;
            film.GetComponent<Graphic>().SetVerticesDirty();
            if (selectionMark != null)
            {
                selectionMark.gameObject.SetActive(emphasized);
                selectionMark.color = contrast ? Color.black : config.teal;
            }
            if (label != null) label.color = contrast ? (film.contrastDark ? Color.white : Color.black) : labelColor;
            float target = Available() && (hovered || focused) && !contrast ? 1 : 0;
            motion = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            motion.Join(DOTween.To(() => film.Warmth, value => film.Warmth = value, target, .12f));
            if (!UISettings.Current.reducedMotion)
                motion.Join(transform.DOScale(!Available() ? 1 : pressed ? .97f : target > 0 ? 1.015f : 1, config.motion.response).SetEase(Ease.OutCubic));
            else transform.localScale = Vector3.one;
            motion.OnKill(() => motion = null);
        }
        private void Ripple()
        {
            if (pulse != null) pulse.Kill(); pulse = null;
            if (ripple == null || UISettings.Current.reducedMotion) return;
            ripple.rectTransform.localScale = Vector3.one * .4f;
            ripple.color = new Color(0, 0, 0, .8f);
            pulse = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            pulse.Join(ripple.rectTransform.DOScale(1.2f, config.motion.ripple).SetEase(Ease.OutQuad));
            pulse.Join(ripple.DOFade(0, config.motion.ripple)); pulse.OnKill(() => pulse = null);
            UIPage page = GetComponentInParent<UIPage>();
            if (page != null) page.Resonate(true);
        }
        private void KillMotion() { if (motion != null) motion.Kill(); motion = null; }
        private void Kill() { KillMotion(); if (pulse != null) pulse.Kill(); pulse = null; }
        private void OnDestroy() { Kill(); }
    }
}
