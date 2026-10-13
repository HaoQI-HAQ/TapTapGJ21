using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // The selectable owns color and the focus ring; this component supplies a short, independent press response.
    public sealed class UIButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        private Button button;
        private Tween motion;
        private bool hovered, focused, pressed, wasInteractable;
        private void Awake() { button = GetComponent<Button>(); wasInteractable = button.IsInteractable(); }
        private void OnEnable() { UISettings.Changed += Refresh; }
        private void OnDisable()
        {
            UISettings.Changed -= Refresh; Kill(); hovered = focused = pressed = false; transform.localScale = Vector3.one;
        }
        private void Update()
        {
            bool available = button != null && button.IsInteractable();
            if (available != wasInteractable) { wasInteractable = available; Refresh(); }
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Refresh(); } }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(); }
        public void OnSubmit(BaseEventData data)
        {
            if (!CanAnimate()) { Refresh(); return; }
            Kill(); transform.localScale = Vector3.one * 0.97f;
            motion = transform.DOScale(focused ? 1.03f : 1, 0.12f).SetDelay(0.07f).SetUpdate(true).SetLink(gameObject);
        }
        private bool CanAnimate() => button != null && button.IsInteractable() && !UISettings.Current.reducedMotion;
        private void Refresh()
        {
            Kill();
            if (!CanAnimate()) { transform.localScale = Vector3.one; return; }
            float scale = pressed ? 0.97f : focused ? 1.03f : hovered ? 1.02f : 1;
            motion = transform.DOScale(scale, pressed ? 0.07f : focused ? 0.16f : 0.12f).SetUpdate(true).SetLink(gameObject);
        }
        private void Kill() { if (motion != null) motion.Kill(); motion = null; }
        private void OnDestroy() { Kill(); }
    }
}
