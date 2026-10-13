using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIAbilityMotion : MonoBehaviour
    {
        private sealed class Node
        {
            public RectTransform root;
            public UIPearlGraphic film, glow, orbit, ripple, charge;
            public Image ring;
        }
        private readonly List<Node> nodes = new List<Node>();
        private Sequence change, cast;
        private Tween orbitLoop, cardTint;
        private UIPearlGraphic traveller;
        private UIConfig config;
        private Image chargeFill, information;
        private Text title;
        private int selected = -1;
        private bool charging, ready;
        private float progress;
        public int Selected => selected;
        public int MovingOrbitCount => orbitLoop != null && orbitLoop.IsActive() ? 1 : 0;
        public float ChargeProgress => progress;
        public void Configure(UIWidgets ui, Image bar, Image card, Text abilityTitle)
        {
            config = ui.Config; chargeFill = bar; information = card; title = abilityTitle;
            RectTransform current = ui.Rect("Ability current", transform); UIWidgets.Stretch(current);
            var waves = current.gameObject.AddComponent<UIPearlGraphic>(); waves.shape = UIPearlShape.Current;
            waves.color = new Color(.52f, .83f, 1, .2f); waves.raycastTarget = false; current.SetAsFirstSibling();
            traveller = Graphic(ui, transform, "Travelling pearl", UIPearlShape.Bubble, 14);
            traveller.color = Color.clear;
        }
        public void AddNode(UIWidgets ui, Image circle, Image ring)
        {
            int index = nodes.Count;
            UIPearlGraphic glow = Graphic(ui, circle.transform, "Pearl glow", UIPearlShape.Glow, 160); glow.rectTransform.SetAsFirstSibling();
            UIPearlGraphic film = Graphic(ui, circle.transform, "Bubble membrane", UIPearlShape.Bubble, 96);
            UIPearlGraphic orbit = Graphic(ui, circle.transform, "Orbit arcs", UIPearlShape.Orbit, 128);
            UIPearlGraphic ripple = Graphic(ui, circle.transform, "Switch ripple", UIPearlShape.Orbit, 118);
            UIPearlGraphic charge = Graphic(ui, circle.transform, "Charge arc", UIPearlShape.Orbit, 112);
            charge.Fill = 0;
            circle.color = new Color(config.ink.r, config.ink.g, config.ink.b, .7f);
            circle.GetComponent<Button>().transition = Selectable.Transition.None;
            var node = new Node { root = circle.rectTransform, film = film, glow = glow, orbit = orbit, ripple = ripple, charge = charge, ring = ring };
            nodes.Add(node); circle.gameObject.AddComponent<UIAbilityHover>().Configure(film, config.motion);
            // Symbols and the stable selection boundary stay above the animated membrane.
            Transform icon = circle.transform.Find("Ability symbol"); if (icon != null) icon.SetAsLastSibling();
            Transform painted = circle.transform.Find("Painted ability"); if (painted != null) painted.SetAsLastSibling();
            ring.transform.SetAsLastSibling(); ApplyNode(index);
        }
        private static UIPearlGraphic Graphic(UIWidgets ui, Transform parent, string name, UIPearlShape shape, float size)
        {
            var graphic = ui.Rect(name, parent).gameObject.AddComponent<UIPearlGraphic>();
            graphic.shape = shape; graphic.raycastTarget = false;
            graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = Vector2.one * .5f;
            graphic.rectTransform.sizeDelta = Vector2.one * size;
            if (shape == UIPearlShape.Bubble) UIHandDrawn.Pigment(ui, graphic.transform);
            return graphic;
        }
        public void SetState(int ability, float charge, bool isCharging, bool isReady)
        {
            ability = Mathf.Clamp(ability, 0, nodes.Count - 1);
            if (selected != ability) Select(ability);
            bool released = charging && !isCharging;
            bool becameReady = !ready && isReady;
            charging = isCharging; ready = isReady; progress = isReady ? 1 : Mathf.Clamp01(charge);
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].charge.Fill = i == selected ? progress : 0;
                nodes[i].charge.color = i == selected && (isCharging || isReady) ? UIAbilityColor.ColorFor(i) : Color.clear;
            }
            if ((released || becameReady) && isActiveAndEnabled) CastPulse();
        }
        private void Select(int ability)
        {
            int previous = selected; KillChange(); StopOrbit(); selected = ability;
            for (int i = 0; i < nodes.Count; i++) ApplyNode(i);
            Color accent = UIAbilityColor.ColorFor(selected);
            if (chargeFill != null) chargeFill.color = accent;
            if (cardTint != null) cardTint.Kill(); cardTint = null;
            if (information != null)
            {
                Color terminal = Color.Lerp(config.paper, accent, .12f); terminal.a = .94f;
                if (previous < 0 || UISettings.Current.reducedMotion) information.color = terminal;
                else cardTint = information.DOColor(terminal, .12f).SetUpdate(true).SetLink(gameObject).OnKill(() => cardTint = null);
            }
            if (title != null) title.color = config.ink;
            if (previous >= 0 && !UISettings.Current.reducedMotion)
            {
                Node active = nodes[selected]; active.root.localScale = Vector3.one * .94f;
                change = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
                change.Append(active.root.DOScale(1.07f, config.motion.abilitySwitch * .6f).SetEase(Ease.OutCubic));
                change.Append(active.root.DOScale(1.03f, config.motion.abilitySwitch * .4f).SetEase(Ease.InOutSine));
                Node old = nodes[previous]; old.root.localScale = Vector3.one * 1.03f;
                change.Join(old.root.DOScale(.88f, config.motion.abilitySwitch * .4f));
                traveller.rectTransform.localPosition = old.root.localPosition; traveller.color = accent;
                change.Insert(0, traveller.rectTransform.DOLocalMove(active.root.localPosition, config.motion.abilitySwitch).SetEase(Ease.InOutCubic));
                change.Insert(0, traveller.DOFade(0, config.motion.abilitySwitch));
                active.ripple.color = new Color(accent.r, accent.g, accent.b, .9f);
                active.ripple.rectTransform.localScale = Vector3.one * .85f;
                change.Insert(0, active.ripple.rectTransform.DOScale(1.42f, config.motion.abilitySwitch).SetEase(Ease.OutQuad));
                change.Insert(0, active.ripple.DOFade(0, config.motion.abilitySwitch));
                change.OnKill(() => change = null);
            }
            StartOrbit();
        }
        private void ApplyNode(int i)
        {
            Node node = nodes[i]; bool active = i == selected;
            Color accent = UIAbilityColor.ColorFor(i);
            node.root.localScale = Vector3.one * (UISettings.Current.reducedMotion ? 1 : active ? 1.03f : .88f);
            node.film.color = new Color(accent.r, accent.g, accent.b, active ? 1 : .65f);
            node.glow.color = new Color(accent.r, accent.g, accent.b, active && !UISettings.Current.highContrast ? .8f : .08f);
            node.ring.enabled = active; node.ring.color = new Color(1, 1, 1, .9f);
            node.orbit.color = active ? accent : new Color(accent.r, accent.g, accent.b, .12f);
            node.orbit.Phase = 0; node.ripple.color = Color.clear;
            node.ripple.rectTransform.localScale = Vector3.one;
        }
        private void CastPulse()
        {
            if (cast != null) cast.Kill(); cast = null;
            if (UISettings.Current.reducedMotion || selected < 0) return;
            Node node = nodes[selected]; Color accent = UIAbilityColor.ColorFor(selected);
            node.glow.color = new Color(accent.r, accent.g, accent.b, 1);
            cast = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            cast.Append(node.glow.DOFade(.8f, .3f));
            cast.OnKill(() => cast = null);
        }
        private void StartOrbit()
        {
            if (selected < 0 || UISettings.Current.reducedMotion || !isActiveAndEnabled) return;
            UIPearlGraphic orbit = nodes[selected].orbit;
            orbitLoop = DOTween.To(() => orbit.Phase, value => orbit.Phase = value, 360, config.motion.orbitPeriod)
                .SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart).SetUpdate(true).SetLink(gameObject).OnKill(() => orbitLoop = null);
        }
        private void StopOrbit() { if (orbitLoop != null) orbitLoop.Kill(); orbitLoop = null; }
        private void KillChange() { if (change != null) change.Kill(); change = null; if (traveller != null) traveller.color = Color.clear; }
        private void SettingsChanged()
        {
            KillChange(); StopOrbit(); if (cast != null) cast.Kill(); cast = null;
            if (cardTint != null) cardTint.Kill(); cardTint = null;
            for (int i = 0; i < nodes.Count; i++) ApplyNode(i);
            if (selected >= 0)
            {
                Color accent = UIAbilityColor.ColorFor(selected);
                if (title != null) title.color = config.ink; if (chargeFill != null) chargeFill.color = accent;
                if (information != null) { Color tint = Color.Lerp(config.paper, accent, .12f); tint.a = .94f; information.color = tint; }
                SetState(selected, progress, charging, ready);
            }
            StartOrbit();
        }
        private void OnEnable() { UISettings.Changed += SettingsChanged; SettingsChanged(); }
        private void OnDisable()
        {
            UISettings.Changed -= SettingsChanged; KillChange(); StopOrbit();
            if (cast != null) cast.Kill(); cast = null; if (cardTint != null) cardTint.Kill(); cardTint = null;
            for (int i = 0; i < nodes.Count; i++) ApplyNode(i);
        }
        private void OnDestroy() { KillChange(); StopOrbit(); if (cast != null) cast.Kill(); if (cardTint != null) cardTint.Kill(); }
    }

    public sealed class UIAbilityHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private UIPearlGraphic film; private Tween tint;
        private bool hovered, focused, pressed;
        private UIMotionProfile motion;
        public void Configure(UIPearlGraphic target, UIMotionProfile profile) { film = target; motion = profile; }
        private void Refresh()
        {
            if (tint != null) tint.Kill(); tint = null; if (film == null) return;
            float target = pressed ? 1 : hovered || focused ? .55f : 0;
            tint = DOTween.To(() => film.Highlight, value => film.Highlight = value, target, motion.response).SetUpdate(true).SetLink(gameObject).OnKill(() => tint = null);
        }
        public void OnPointerEnter(PointerEventData e) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData e) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData e) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData e) { focused = pressed = false; Refresh(); }
        private void OnDisable() { if (tint != null) tint.Kill(); tint = null; hovered = focused = pressed = false; if (film != null) film.Highlight = 0; }
        private void OnDestroy() { if (tint != null) tint.Kill(); }
    }
}
