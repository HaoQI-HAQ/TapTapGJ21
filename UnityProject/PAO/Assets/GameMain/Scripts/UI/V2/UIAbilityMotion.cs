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
            public Vector2 home;
            public UIPearlGraphic film, glow, orbit, ripple, charge;
            public Image ring;
        }
        private readonly List<Node> nodes = new List<Node>();
        private Sequence change, cast;
        private Tween orbitLoop, cardTint;
        private UIPearlGraphic traveller;
        private UIBubbleTransferGraphic transfer;
        private UIConfig config;
        private Image chargeFill, information;
        private Text title;
        private int selected = -1;
        private int lastSelected = -1;
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
            waves.color = new Color(config.teal.r, config.teal.g, config.teal.b, .16f); waves.raycastTarget = false; current.SetAsFirstSibling();
            RectTransform stream = ui.Rect("Bubble transfer", transform); UIWidgets.Stretch(stream);
            transfer = stream.gameObject.AddComponent<UIBubbleTransferGraphic>(); transfer.raycastTarget = false;
            traveller = Graphic(ui, transform, "Travelling pearl", UIPearlShape.Bubble, 32);
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
            circle.color = config.paper;
            circle.GetComponent<Button>().transition = Selectable.Transition.None;
            var node = new Node { root = circle.rectTransform, home = circle.rectTransform.anchoredPosition,
                film = film, glow = glow, orbit = orbit, ripple = ripple, charge = charge, ring = ring };
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
            int previous = selected; KillChange(); StopOrbit();
            var scales = new Vector3[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) scales[i] = nodes[i].root.localScale;
            selected = ability; lastSelected = previous;
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
                float duration = Mathf.Max(.56f, config.motion.abilitySwitch);
                change = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
                for (int i = 0; i < nodes.Count; i++)
                {
                    Node node = nodes[i];
                    Vector3 size = node.root.localScale;
                    node.root.localScale = scales[i];
                    if (i == selected)
                    {
                        change.Insert(0, node.root.DOScale(size * 1.12f, duration * .72f).SetEase(Ease.OutCubic));
                        change.Insert(duration * .72f,
                            node.root.DOScale(size, duration * .28f).SetEase(Ease.OutCubic));
                    }
                    else change.Insert(0, node.root.DOScale(size, duration).SetEase(Ease.InOutCubic));
                }
                Node active = nodes[selected]; Node old = nodes[previous];
                Vector3 from = old.root.localPosition, to = active.root.localPosition;
                transfer.From = transfer.rectTransform.InverseTransformPoint(old.root.position);
                transfer.To = transfer.rectTransform.InverseTransformPoint(active.root.position);
                transfer.Progress = 0;
                change.Insert(0, DOTween.To(() => transfer.Progress, value => transfer.Progress = value,
                    1, duration).SetEase(Ease.InOutSine));
                traveller.rectTransform.localPosition = from; traveller.color = new Color(accent.r, accent.g, accent.b, .96f);
                traveller.rectTransform.localScale = Vector3.one * .65f;
                change.Insert(0, traveller.rectTransform.DOLocalMove(to, duration * .86f).SetEase(Ease.InOutSine));
                change.Insert(0, traveller.rectTransform.DOScale(1.5f, duration * .58f).SetEase(Ease.OutCubic));
                change.Insert(duration * .58f, traveller.DOFade(0, duration * .28f));
                active.ripple.color = new Color(accent.r, accent.g, accent.b, 0);
                active.ripple.rectTransform.localScale = Vector3.one * .85f;
                change.Insert(duration * .48f, active.ripple.rectTransform.DOScale(1.75f, duration * .52f).SetEase(Ease.OutQuad));
                change.Insert(duration * .48f, active.ripple.DOFade(.95f, duration * .12f));
                change.Insert(duration * .60f, active.ripple.DOFade(0, duration * .40f));
                change.OnKill(() => { change = null; transfer.Progress = 0; traveller.color = Color.clear; });
            }
            StartOrbit();
        }
        private void ApplyNode(int i)
        {
            Node node = nodes[i]; bool active = i == selected;
            bool previous = i == lastSelected && !active;
            node.root.anchoredPosition = node.home;
            Color accent = UIAbilityColor.ColorFor(i);
            node.root.localScale = Vector3.one * (active ? 1.26f : previous ? 1.04f : .78f);
            node.film.color = new Color(accent.r, accent.g, accent.b, active ? .18f : .08f);
            node.glow.color = new Color(accent.r, accent.g, accent.b, active && !UISettings.Current.highContrast ? .12f : .03f);
            node.ring.enabled = active; node.ring.color = config.teal;
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
        private void KillChange()
        {
            if (change != null) change.Kill(); change = null;
            if (transfer != null) transfer.Progress = 0;
            if (traveller != null) traveller.color = Color.clear;
        }
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

    // A short liquid bridge passes behind fixed ability buttons during a selection change.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIBubbleTransferGraphic : MaskableGraphic
    {
        private Vector2 from, to;
        private float progress;
        public Vector2 From { get => from; set { from = value; SetVerticesDirty(); } }
        public Vector2 To { get => to; set { to = value; SetVerticesDirty(); } }
        public float Progress { get => progress; set { progress = Mathf.Clamp01(value); SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (progress <= .001f || progress >= .999f || (to - from).sqrMagnitude < 1) return;
            Vector2 direction = (to - from).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            // The stream grows out of the old bubble, reaches the new one, then retracts.
            // A dark silhouette and white center keep it readable over both sea and sky.
            float head = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .78f));
            float tail = .88f * Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .52f) / .48f));
            float appear = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .1f));
            float disappear = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .84f) / .16f));
            float opacity = appear * disappear;
            Color outline = new Color(0, 0, 0, .97f * opacity);
            Color core = new Color(1, 1, 1, .99f * opacity);
            Ribbon(mesh, normal, tail, head, 0, outline);
            Disc(mesh, Vector2.Lerp(from, to, tail), 13, outline);
            Disc(mesh, Vector2.Lerp(from, to, head), 21, outline);
            Ribbon(mesh, normal, tail, head, -5, core);
            Disc(mesh, Vector2.Lerp(from, to, tail), 8, core);
            Disc(mesh, Vector2.Lerp(from, to, head), 16, core);
            // A pair of detached droplets makes the direction of travel legible.
            float bead = Mathf.Min(1, head + .1f);
            Vector2 beadCenter = Vector2.Lerp(from, to, bead) + normal * 17;
            Disc(mesh, beadCenter, 8 * opacity, outline);
            Disc(mesh, beadCenter, 4 * opacity, core);
        }

        private void Ribbon(VertexHelper mesh, Vector2 normal, float tail, float head, float inset, Color tint)
        {
            const int segments = 32;
            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float t = Mathf.Lerp(tail, head, u);
                float curve = Mathf.Sin(u * Mathf.PI);
                float wobble = Mathf.Sin(u * 9 + progress * 14) * curve * 5;
                Vector2 center = Vector2.Lerp(from, to, t) + normal * wobble;
                float radius = Mathf.Max(1, 13 + 7 * curve + 8 * Mathf.Pow(u, 4) +
                    2 * curve * Mathf.Sin(u * 17 + progress * 11) + inset);
                int vertex = mesh.currentVertCount;
                mesh.AddVert(center - normal * radius, tint, Vector2.zero);
                mesh.AddVert(center + normal * radius, tint, Vector2.zero);
                if (i > 0)
                {
                    mesh.AddTriangle(vertex - 2, vertex - 1, vertex);
                    mesh.AddTriangle(vertex - 1, vertex + 1, vertex);
                }
            }
        }

        private static void Disc(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            if (radius <= .1f) return;
            int start = mesh.currentVertCount;
            mesh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= 16; i++)
            {
                float angle = i * Mathf.PI * 2 / 16;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
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
