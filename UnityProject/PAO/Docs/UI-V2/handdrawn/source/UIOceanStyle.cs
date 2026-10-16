using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    [Serializable]
    public sealed class UIMotionProfile
    {
        public float response = .12f;
        public float abilitySwitch = .26f;
        public float pageEnter = .28f;
        public float ripple = .46f;
        public float orbitPeriod = 7f;
    }

    // Vertex colours preserve the existing rounded sprite, masking and batching.
    public sealed class UIFluidGradient : BaseMeshEffect
    {
        public Color top = new Color32(170, 235, 248, 255);
        public Color bottom = new Color32(93, 164, 214, 255);
        public Color focusTint = new Color32(211, 178, 249, 255);
        public bool contrastDark;
        private float warmth;
        public float Warmth { get => warmth; set { warmth = value; if (graphic != null) graphic.SetVerticesDirty(); } }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            Rect rect = graphic.rectTransform.rect;
            var vertex = new UIVertex();
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                float y = Mathf.InverseLerp(rect.yMin, rect.yMax, vertex.position.y);
                Color tint = Color.Lerp(bottom, top, y);
                tint = Color.Lerp(tint, focusTint, warmth * (.4f + .4f * y));
                vertex.color = (Color)vertex.color * tint; mesh.SetUIVertex(vertex, i);
            }
        }
    }

    public enum UIPearlShape { Bubble, Orbit, Glow, Current }

    // Layered radial meshes provide a translucent bubble membrane without a material instance per node.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIPearlGraphic : MaskableGraphic
    {
        public UIPearlShape shape;
        private float phase, fill = 1, highlight;
        public float Phase { get => phase; set { phase = value; SetVerticesDirty(); } }
        public float Fill { get => fill; set { fill = Mathf.Clamp01(value); SetVerticesDirty(); } }
        public float Highlight { get => highlight; set { highlight = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * .5f;
            if (shape == UIPearlShape.Current)
            {
                for (int line = 0; line < 4; line++)
                    for (int i = 0; i < 64; i++)
                    {
                        float x = (float)i / 64, next = (i + 1f) / 64;
                        Vector2 a = new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * (.25f + line * .1f + Mathf.Sin(x * 6 + line * .6f) * .09f));
                        Vector2 b = new Vector2(rect.xMin + rect.width * next, rect.yMin + rect.height * (.25f + line * .1f + Mathf.Sin(next * 6 + line * .6f) * .09f));
                        Stroke(mesh, a, b, 1.3f, new Color(color.r, color.g, color.b, color.a * Mathf.Sin(x * Mathf.PI)));
                    }
                return;
            }
            if (shape == UIPearlShape.Glow) { Disc(mesh, rect.center, radius, new Color(color.r, color.g, color.b, color.a * .34f), new Color(color.r, color.g, color.b, 0)); return; }
            if (shape == UIPearlShape.Orbit)
            {
                Ring(mesh, rect.center, radius - 2, radius, 0, 360, color * new Color(1, 1, 1, .15f), false);
                if (fill < .999f) Ring(mesh, rect.center, radius - 4, radius, 90, 90 + 360 * fill, color, false);
                else
                {
                    Ring(mesh, rect.center, radius - 3, radius, phase, phase + 76, color, true);
                    Ring(mesh, rect.center, radius - 2, radius, phase + 180, phase + 224, color, true);
                }
                return;
            }
            Color core = Color.Lerp(new Color32(44, 79, 91, 230), color, .4f + highlight * .18f);
            core.a *= color.a;
            Disc(mesh, rect.center, radius - 2, core, Color.Lerp(core, color, .32f));
            Ring(mesh, rect.center, radius - 4, radius - 1, 0, 360, color, true);
            Ring(mesh, rect.center + Vector2.one * radius * .03f, radius * .85f, radius * .9f, 58, 105, new Color(1, .97f, .88f, .64f * color.a), false);
            Ring(mesh, rect.center, radius * .91f, radius * .94f, 220, 278, new Color(.8f, .76f, .86f, .3f * color.a), false);
        }
        private static void Disc(VertexHelper mesh, Vector2 center, float radius, Color core, Color edge)
        {
            int start = mesh.currentVertCount; mesh.AddVert(center, core, Vector2.zero);
            for (int i = 0; i <= 64; i++)
            {
                float a = i * Mathf.PI * 2 / 64;
                float dryEdge = radius + Mathf.Sin(a * 7) * .7f + Mathf.Cos(a * 13) * .35f;
                mesh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dryEdge, edge, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
        private static void Ring(VertexHelper mesh, Vector2 center, float inner, float outer, float from, float to, Color tint, bool pearl)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt((to - from) / 5));
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(from, to, (float)i / segments) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float contour = Mathf.Sin(a * 7) * .65f + Mathf.Cos(a * 13) * .3f;
                Color c = pearl ? Color.Lerp(tint, new Color(.9f, .66f, 1, tint.a), (Mathf.Sin(a * 2) + 1) * .34f) : tint;
                int v = mesh.currentVertCount;
                mesh.AddVert(center + direction * (inner + contour), c, Vector2.zero);
                mesh.AddVert(center + direction * (outer + contour), c, Vector2.zero);
                if (i > 0) { mesh.AddTriangle(v - 2, v - 1, v); mesh.AddTriangle(v - 1, v + 1, v); }
            }
        }
        internal static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int v = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2); mesh.AddTriangle(v, v + 2, v + 3);
        }
    }

    public sealed class UISeaBackdrop : MonoBehaviour
    {
        private readonly System.Collections.Generic.List<Tween> loops = new System.Collections.Generic.List<Tween>();
        private RectTransform[] bubbles;
        public void Configure(UIWidgets ui)
        {
            RectTransform currents = ui.Rect("Ocean currents", transform); UIWidgets.Stretch(currents);
            var waves = currents.gameObject.AddComponent<UIPearlGraphic>(); waves.shape = UIPearlShape.Current;
            waves.color = new Color(.36f, .78f, 1, .22f); waves.raycastTarget = false;
            bubbles = new RectTransform[5];
            Vector2[] anchors = { new Vector2(.88f, .75f), new Vector2(.7f, .3f), new Vector2(.15f, .2f), new Vector2(.52f, .9f), new Vector2(.97f, .06f) };
            for (int i = 0; i < bubbles.Length; i++)
            {
                RectTransform bubble = ui.Rect("Drifting pearl " + i, transform); bubbles[i] = bubble;
                bubble.anchorMin = bubble.anchorMax = anchors[i]; bubble.sizeDelta = Vector2.one * (i == 0 ? 320 : 60 + i * 22);
                var membrane = bubble.gameObject.AddComponent<UIPearlGraphic>(); membrane.shape = UIPearlShape.Bubble;
                membrane.color = new Color(.36f, .82f, 1, i == 0 ? .07f : .09f); membrane.raycastTarget = false;
            }
            Restart();
        }
        private void OnEnable() { UISettings.Changed += Restart; Restart(); }
        private void OnDisable() { UISettings.Changed -= Restart; Stop(); }
        private void OnDestroy() { Stop(); }
        private void Stop() { Tween[] previous = loops.ToArray(); loops.Clear(); foreach (Tween loop in previous) loop.Kill(); }
        private void Restart()
        {
            Stop(); if (bubbles == null) return;
            for (int i = 0; i < bubbles.Length; i++)
            {
                bubbles[i].anchoredPosition = Vector2.zero;
                if (!UISettings.Current.reducedMotion && !UISettings.Current.highContrast && isActiveAndEnabled)
                {
                    Tween loop = bubbles[i].DOAnchorPos(new Vector2(10, 14 + i * 3), 5 + i).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject);
                    loops.Add(loop); loop.OnKill(() => loops.Remove(loop));
                }
            }
        }
    }
}
