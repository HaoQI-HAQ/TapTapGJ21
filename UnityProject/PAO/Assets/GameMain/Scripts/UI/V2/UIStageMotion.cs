using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // A deliberately sparse route illustration: faceted coast, contour lines,
    // and connected survey points. Every chapter gets a different shoreline.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIStageMapGraphic : MaskableGraphic
    {
        private int variant;
        public int Variant { get => variant; set { variant = value; SetVerticesDirty(); } }
        private Rect drawing;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            drawing = rectTransform.rect;
            if (drawing.width < 1 || drawing.height < 1) return;
            float shift = (variant % 4 - 1.5f) * .035f;
            Color ink = Color.black;
            Color quiet = new Color(.72f, .72f, .72f, 1);
            Color light = new Color(.94f, .94f, .94f, 1);
            Color mid = new Color(.82f, .82f, .82f, 1);

            for (int line = 0; line < 6; line++)
            {
                float y = .13f + line * .13f;
                Stroke(mesh, new Vector2(0, y), new Vector2(1, y), line == 0 ? 1.8f : .8f, quiet);
            }
            Polygon(mesh, light, new Vector2(.06f, .28f), new Vector2(.21f, .4f),
                new Vector2(.4f + shift, .36f), new Vector2(.58f + shift, .52f),
                new Vector2(.78f, .32f), new Vector2(.95f, .27f), new Vector2(.95f, .13f),
                new Vector2(.75f, .17f), new Vector2(.56f, .1f), new Vector2(.33f, .19f));
            Polygon(mesh, mid, new Vector2(.21f, .4f), new Vector2(.4f + shift, .36f),
                new Vector2(.58f + shift, .52f), new Vector2(.49f, .26f), new Vector2(.33f, .19f));
            Polygon(mesh, new Color(.88f, .88f, .88f, 1), new Vector2(.58f + shift, .52f),
                new Vector2(.78f, .32f), new Vector2(.95f, .27f), new Vector2(.75f, .17f),
                new Vector2(.49f, .26f));
            Vector2[] coast = { new Vector2(.06f, .28f), new Vector2(.21f, .4f),
                new Vector2(.4f + shift, .36f), new Vector2(.58f + shift, .52f),
                new Vector2(.78f, .32f), new Vector2(.95f, .27f) };
            for (int i = 0; i < coast.Length - 1; i++) Stroke(mesh, coast[i], coast[i + 1], 1.8f, ink);
            Stroke(mesh, coast[0], new Vector2(.33f, .19f), 1.4f, ink);
            Stroke(mesh, new Vector2(.33f, .19f), new Vector2(.56f, .1f), 1.4f, ink);
            Stroke(mesh, new Vector2(.56f, .1f), new Vector2(.75f, .17f), 1.4f, ink);
            Stroke(mesh, new Vector2(.75f, .17f), new Vector2(.95f, .27f), 1.4f, ink);
            Stroke(mesh, new Vector2(.4f + shift, .36f), new Vector2(.33f, .19f), 1, quiet);
            Stroke(mesh, new Vector2(.58f + shift, .52f), new Vector2(.49f, .26f), 1, quiet);
            Stroke(mesh, new Vector2(.78f, .32f), new Vector2(.75f, .17f), 1, quiet);

            Vector2[] route = { new Vector2(.1f, .75f), new Vector2(.27f, .64f),
                new Vector2(.49f + shift, .7f), new Vector2(.73f, .61f), new Vector2(.91f, .76f) };
            for (int i = 0; i < route.Length - 1; i++) Stroke(mesh, route[i], route[i + 1], 1.5f, ink);
            for (int i = 0; i < route.Length; i++)
            {
                Disc(mesh, route[i], i == variant % route.Length ? 7 : 4, ink);
                if (i == variant % route.Length) Disc(mesh, route[i], 3.1f, Color.white);
            }
            Stroke(mesh, new Vector2(.03f, .96f), new Vector2(.19f, .96f), 2, ink);
            Stroke(mesh, new Vector2(.03f, .96f), new Vector2(.03f, .86f), 2, ink);
            Stroke(mesh, new Vector2(.97f, .04f), new Vector2(.81f, .04f), 2, ink);
            Stroke(mesh, new Vector2(.97f, .04f), new Vector2(.97f, .14f), 2, ink);
        }

        private Vector2 Point(Vector2 normalized) =>
            new Vector2(drawing.xMin + normalized.x * drawing.width, drawing.yMin + normalized.y * drawing.height);

        private void Polygon(VertexHelper mesh, Color color, params Vector2[] points)
        {
            int first = mesh.currentVertCount;
            foreach (Vector2 point in points) mesh.AddVert(Point(point), color, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(first, first + i, first + i + 1);
        }

        private void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 a = Point(from), b = Point(to);
            Vector2 normal = (b - a).normalized;
            normal = new Vector2(-normal.y, normal.x) * width * .5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero);
            mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }

        private void Disc(VertexHelper mesh, Vector2 center, float radius, Color color)
        {
            Vector2 origin = Point(center);
            int first = mesh.currentVertCount;
            mesh.AddVert(origin, color, Vector2.zero);
            for (int i = 0; i <= 12; i++)
            {
                float angle = i * Mathf.PI * 2 / 12;
                mesh.AddVert(origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }
    }

    public sealed class UIStageCardMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        private Image currentBar;
        private RectTransform shutter;
        private Tween reveal;
        private Tween response;
        private Button button;
        private bool current;
        private bool hovered, focused, pressed;

        public void Configure(Image bar, RectTransform cover, int order)
        {
            currentBar = bar; shutter = cover;
            if (UISettings.Current.reducedMotion) { shutter.gameObject.SetActive(false); return; }
            button = GetComponent<Button>();
            button.interactable = false;
            reveal = shutter.DOSizeDelta(new Vector2(0, 0), .54f)
                .SetEase(Ease.InOutCubic).SetDelay(.12f + Mathf.Min(order, 8) * .09f)
                .SetUpdate(true).SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (shutter != null) shutter.gameObject.SetActive(false);
                    if (button != null) button.interactable = true;
                    if (current && button != null) button.Select();
                });
        }

        public void SetCurrent(bool current)
        {
            this.current = current;
            if (currentBar != null) currentBar.gameObject.SetActive(current);
        }

        private void Respond()
        {
            float target = pressed ? 1.005f : hovered || focused ? 1.025f : 1f;
            if (response != null) response.Kill();
            if (UISettings.Current.reducedMotion) { transform.localScale = Vector3.one * target; return; }
            response = transform.DOScale(target, .2f).SetEase(Ease.OutCubic)
                .SetUpdate(true).SetLink(gameObject);
        }

        public void OnPointerEnter(PointerEventData data) { hovered = true; Respond(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Respond(); }
        public void OnPointerDown(PointerEventData data) { pressed = true; Respond(); }
        public void OnPointerUp(PointerEventData data) { pressed = false; Respond(); }
        public void OnSelect(BaseEventData data) { focused = true; Respond(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Respond(); }
        private void OnDisable()
        {
            if (reveal != null) reveal.Kill();
            if (response != null) response.Kill();
            transform.localScale = Vector3.one;
        }
    }

    public sealed class UIStageDossierMotion : MonoBehaviour
    {
        private Sequence sequence;
        public void Configure(RectTransform chart)
        {
            Image rail = new GameObject("Dossier opening rule", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            rail.transform.SetParent(chart, false);
            rail.color = Color.black; rail.raycastTarget = false;
            RectTransform rule = rail.rectTransform;
            rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(0, 1);
            rule.offsetMin = Vector2.zero; rule.offsetMax = new Vector2(6, 0);
            if (UISettings.Current.reducedMotion) return;
            CanvasGroup drawing = chart.gameObject.AddComponent<CanvasGroup>();
            drawing.alpha = 0;
            rule.localScale = new Vector3(1, 0, 1);
            transform.localScale = Vector3.one * .96f;
            sequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            sequence.Insert(0, transform.DOScale(1, .48f).SetEase(Ease.OutCubic));
            sequence.Insert(.12f, drawing.DOFade(1, .32f));
            sequence.Insert(.17f, rule.DOScaleY(1, .35f).SetEase(Ease.OutCubic));
        }

        private void OnDisable()
        {
            if (sequence != null) sequence.Kill();
            transform.localScale = Vector3.one;
        }
    }
}
