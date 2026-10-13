using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public enum UIGuideSymbol { Goal, Memory, Build, Ride, Steer, Exit, Charge, Move, Journal, Pause, Arrow, Sticky, Bouncy, Bomb, MouseLeft, MouseRight, Check, Current }

    // Vector strokes stay sharp at every UI scale and convey meaning without depending on color.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIGuideGraphic : MaskableGraphic
    {
        private UIGuideSymbol symbol;
        private VertexHelper mesh;
        public UIGuideSymbol Symbol { get => symbol; set { if (symbol != value) { symbol = value; SetVerticesDirty(); } } }
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); mesh = helper;
            switch (symbol)
            {
                case UIGuideSymbol.Goal:
                    Line(-.3f, -.35f, -.3f, .15f); Line(.3f, -.35f, .3f, .15f); Arc(Vector2.up * .15f, .3f, 0, 180);
                    Line(0, -.2f, 0, .2f); Line(0, .2f, -.12f, .07f); Line(0, .2f, .12f, .07f); break;
                case UIGuideSymbol.Memory:
                    Line(0, .45f, .3f, 0); Line(.3f, 0, 0, -.45f); Line(0, -.45f, -.3f, 0); Line(-.3f, 0, 0, .45f);
                    Line(-.12f, 0, .12f, 0); break;
                case UIGuideSymbol.Build:
                    Line(-.28f, -.4f, .18f, .24f); Line(-.1f, .35f, .35f, .02f);
                    Line(-.1f, .35f, -.2f, .2f); Line(.35f, .02f, .24f, -.12f); Line(-.2f, .2f, .24f, -.12f); break;
                case UIGuideSymbol.Ride:
                    Arc(Vector2.zero, .4f, 0, 360); Arc(new Vector2(0, .13f), .08f, 0, 360);
                    Line(0, .04f, 0, -.18f); Line(-.16f, -.2f, .16f, -.2f); break;
                case UIGuideSymbol.Steer:
                    Arc(Vector2.zero, .36f, 0, 360); Line(-.3f, 0, .3f, 0); Line(0, 0, 0, -.32f); break;
                case UIGuideSymbol.Exit:
                    Line(-.3f, -.35f, -.3f, .35f); Line(-.3f, .35f, 0, .35f); Line(-.3f, -.35f, 0, -.35f);
                    Line(-.05f, 0, .4f, 0); Line(.4f, 0, .22f, .18f); Line(.4f, 0, .22f, -.18f); break;
                case UIGuideSymbol.Charge:
                    Arc(Vector2.zero, .35f, 45, 315); Line(0, -.2f, 0, .42f); Line(0, .42f, -.14f, .24f); Line(0, .42f, .14f, .24f); break;
                case UIGuideSymbol.Move:
                    Line(0, -.38f, 0, .38f); Line(-.38f, 0, .38f, 0);
                    Line(0, .38f, -.12f, .23f); Line(0, .38f, .12f, .23f);
                    Line(-.38f, 0, -.23f, .12f); Line(.38f, 0, .23f, .12f); break;
                case UIGuideSymbol.Journal:
                    Line(0, -.35f, 0, .35f); Line(0, .35f, -.38f, .42f); Line(-.38f, .42f, -.38f, -.26f);
                    Line(-.38f, -.26f, 0, -.35f); Line(0, .35f, .38f, .42f); Line(.38f, .42f, .38f, -.26f); Line(.38f, -.26f, 0, -.35f); break;
                case UIGuideSymbol.Pause:
                    Line(-.16f, -.35f, -.16f, .35f, .12f); Line(.16f, -.35f, .16f, .35f, .12f); break;
                case UIGuideSymbol.Arrow:
                    Line(0, -.35f, 0, .4f); Line(0, .4f, -.2f, .15f); Line(0, .4f, .2f, .15f); break;
                case UIGuideSymbol.Sticky:
                    Arc(new Vector2(-.18f, -.1f), .23f, 0, 360); Arc(new Vector2(.18f, .14f), .23f, 0, 360); break;
                case UIGuideSymbol.Bouncy:
                    Arc(new Vector2(0, .17f), .22f, 0, 360); Line(-.35f, -.33f, .35f, -.33f);
                    Line(-.22f, -.16f, 0, -.27f); Line(0, -.27f, .22f, -.16f); break;
                case UIGuideSymbol.Bomb:
                    Arc(new Vector2(0, -.08f), .28f, 0, 360); Line(.12f, .18f, .23f, .35f);
                    Line(.2f, .44f, .3f, .44f); Line(.36f, .33f, .44f, .28f); break;
                case UIGuideSymbol.MouseLeft:
                case UIGuideSymbol.MouseRight:
                    Arc(new Vector2(0, .16f), .27f, 0, 180); Arc(new Vector2(0, -.16f), .27f, 180, 360);
                    Line(-.27f, -.16f, -.27f, .16f); Line(.27f, -.16f, .27f, .16f); Line(-.27f, .1f, .27f, .1f); Line(0, .1f, 0, .4f);
                    float side = symbol == UIGuideSymbol.MouseLeft ? -.13f : .13f; Line(side, .18f, side, .3f, .13f); break;
                case UIGuideSymbol.Check:
                    Line(-.3f, 0, -.08f, -.22f); Line(-.08f, -.22f, .34f, .28f); break;
                case UIGuideSymbol.Current:
                    for (int row = 0; row < 3; row++)
                    {
                        float y = .24f - row * .24f;
                        Line(-.4f, y, -.2f, y + .08f); Line(-.2f, y + .08f, 0, y);
                        Line(0, y, .2f, y + .08f); Line(.2f, y + .08f, .4f, y);
                    }
                    break;
            }
        }
        private Vector2 Point(Vector2 normalized)
        { Rect rect = rectTransform.rect; return rect.center + normalized * Mathf.Min(rect.width, rect.height); }
        private void Line(float x1, float y1, float x2, float y2, float thickness = .065f)
        {
            Vector2 a = Point(new Vector2(x1, y1)), b = Point(new Vector2(x2, y2));
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized;
            float width = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * thickness * .5f;
            int segments = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 6));
            for (int i = 0; i < segments; i++)
            {
                float from = (float)i / segments, to = (i + 1f) / segments;
                Vector2 p = Vector2.Lerp(a, b, from) + normal * Mathf.Sin(from * Mathf.PI * 2) * .2f;
                Vector2 q = Vector2.Lerp(a, b, to) + normal * Mathf.Sin(to * Mathf.PI * 2) * .2f;
                Vector2 offset = normal * width * (1 + Mathf.Sin((from + x1) * 9) * .07f);
                int start = mesh.currentVertCount;
                mesh.AddVert(p - offset, color, Vector2.zero); mesh.AddVert(p + offset, color, Vector2.zero);
                mesh.AddVert(q + offset, color, Vector2.zero); mesh.AddVert(q - offset, color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
        private void Arc(Vector2 center, float radius, float from, float to)
        {
            int segments = Mathf.CeilToInt((to - from) / 12);
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(from, to, (float)i / segments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(from, to, (float)(i + 1) / segments) * Mathf.Deg2Rad;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + Mathf.Sin(a * 7) * .004f),
                    q = center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (radius + Mathf.Sin(b * 7) * .004f);
                Line(p.x, p.y, q.x, q.y);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIChargeArc : MaskableGraphic
    {
        private float progress;
        public float Progress { get => progress; set { value = Mathf.Clamp01(value); if (Mathf.Abs(progress - value) > .001f) { progress = value; SetVerticesDirty(); } } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect; float radius = Mathf.Min(rect.width, rect.height) * .5f;
            int count = Mathf.CeilToInt(64 * progress);
            for (int i = 0; i < count; i++)
            {
                float a = (90 - Mathf.Min(i / 64f, progress) * 360) * Mathf.Deg2Rad;
                float b = (90 - Mathf.Min((i + 1) / 64f, progress) * 360) * Mathf.Deg2Rad;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), q = new Vector2(Mathf.Cos(b), Mathf.Sin(b)); int start = mesh.currentVertCount;
                mesh.AddVert(rect.center + p * (radius - 4), color, Vector2.zero); mesh.AddVert(rect.center + p * radius, color, Vector2.zero);
                mesh.AddVert(rect.center + q * radius, color, Vector2.zero); mesh.AddVert(rect.center + q * (radius - 4), color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
