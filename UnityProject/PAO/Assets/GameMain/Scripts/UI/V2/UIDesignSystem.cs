using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public enum UIPageDesign { Plain, Menu, Pause, Chapters, Records, Settings, Guide, Journal, Dialogue, Construction, Completion, Ending, Credits, Failure, Loading, Controls, Confirmation }

    // One palette for both menu and gameplay pages. Accent colours carry state; paper carries content.
    public static class UIDesignPalette
    {
        public static readonly Color Paper = new Color32(255, 249, 235, 255);
        public static readonly Color Cream = new Color32(255, 242, 212, 255);
        public static readonly Color Ink = new Color32(40, 65, 65, 255);
        public static readonly Color Teal = new Color32(48, 117, 112, 255);
        public static readonly Color Coral = new Color32(177, 82, 60, 255);
        public static readonly Color Gold = new Color32(241, 185, 81, 255);
        public static readonly Color ControlTop = new Color32(255, 252, 242, 255);
        public static readonly Color ControlBottom = new Color32(255, 235, 195, 255);
        public static readonly Color PrimaryTop = new Color32(255, 222, 135, 255);
        public static readonly Color PrimaryBottom = new Color32(245, 184, 86, 255);
        public static Color Accent(UIPageDesign design)
        {
            switch (design)
            {
                case UIPageDesign.Chapters: case UIPageDesign.Construction: case UIPageDesign.Controls: return Teal;
                case UIPageDesign.Journal: case UIPageDesign.Dialogue: case UIPageDesign.Ending: case UIPageDesign.Confirmation: return Coral;
                default: return Gold;
            }
        }
    }

    public sealed class UICompactPageLayout : MonoBehaviour
    {
        private void LateUpdate()
        {
            RectTransform rect = (RectTransform)transform;
            RectTransform parent = (RectTransform)rect.parent;
            float margin = parent.rect.width < 1200 ? .06f : .2f;
            Vector2 minimum = new Vector2(margin, .15f), maximum = new Vector2(1 - margin, .85f);
            if (rect.anchorMin == minimum && rect.anchorMax == maximum) return;
            rect.anchorMin = minimum; rect.anchorMax = maximum;
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    public sealed class UIConstructionPreviewLayout : MonoBehaviour
    {
        private bool? compact;
        private void LateUpdate()
        {
            RectTransform rect = (RectTransform)transform;
            float width = ((RectTransform)rect.parent).rect.width;
            bool narrow = width < 1100;
            if (compact != narrow)
            {
                compact = narrow;
                VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
                int padding = narrow ? 16 : 32;
                layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = narrow ? 12 : 16;
                Text heading = transform.Find("Page title").GetComponent<Text>();
                heading.GetComponent<UIStyledText>().Configure(narrow ? 32 : 44, true, GetComponentInParent<UIRuntime>().Config);
                UIWidgets.Size(heading.transform, narrow ? 52 : 72).preferredHeight = -1;
            }
            Vector2 minimum = narrow ? new Vector2(.06f, .04f) : new Vector2(.63f, .08f);
            Vector2 maximum = narrow ? new Vector2(.94f, .46f) : new Vector2(.98f, .92f);
            if (rect.anchorMin == minimum && rect.anchorMax == maximum) return;
            rect.anchorMin = minimum; rect.anchorMax = maximum; LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    // Keep the action name above its controls when a horizontal binding row would overflow.
    public sealed class UIBindingRowLayout : LayoutGroup
    {
        private const float Gap = 16;
        private float ControlsWidth => 280 + Mathf.Max(0, rectChildren.Count - 2) * 160 + Mathf.Max(0, rectChildren.Count - 1) * Gap;
        private bool Stacked => rectTransform.rect.width - ControlsWidth < 220;
        private float Height(int index) => Mathf.Max(LayoutUtility.GetMinHeight(rectChildren[index]), LayoutUtility.GetPreferredHeight(rectChildren[index]));
        private float ControlsHeight
        {
            get { float height = 0; for (int i = 1; i < rectChildren.Count; i++) height = Mathf.Max(height, Height(i)); return height; }
        }
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); SetLayoutInputForAxis(0, 0, 1, 0); }
        public override void CalculateLayoutInputVertical()
        {
            float height = rectChildren.Count == 0 ? 0 : Stacked ? Height(0) + Gap + ControlsHeight : Mathf.Max(Height(0), ControlsHeight);
            SetLayoutInputForAxis(height, height, 0, 1);
        }
        public override void SetLayoutHorizontal()
        {
            if (rectChildren.Count == 0) return;
            float width = rectTransform.rect.width;
            float titleWidth = Stacked ? width : width - ControlsWidth;
            SetChildAlongAxis(rectChildren[0], 0, 0, titleWidth);
            float controlWidth = (width - Mathf.Max(0, rectChildren.Count - 2) * Gap) / Mathf.Max(1, rectChildren.Count - 1);
            float x = Stacked ? 0 : titleWidth + Gap;
            for (int i = 1; i < rectChildren.Count; i++)
            {
                float childWidth = Stacked ? controlWidth : i == 1 ? 280 : 160;
                SetChildAlongAxis(rectChildren[i], 0, x, childWidth); x += childWidth + Gap;
            }
        }
        public override void SetLayoutVertical()
        {
            if (rectChildren.Count == 0) return;
            float labelHeight = Height(0), controlsHeight = ControlsHeight;
            float rowHeight = Mathf.Max(labelHeight, controlsHeight);
            SetChildAlongAxis(rectChildren[0], 1, 0, Stacked ? labelHeight : rowHeight);
            for (int i = 1; i < rectChildren.Count; i++) SetChildAlongAxis(rectChildren[i], 1, Stacked ? labelHeight + Gap : 0, Stacked ? controlsHeight : rowHeight);
        }
    }

    // Content-sized cards reflow into a single column on narrow windows and large UI scales.
    public sealed class UIFlowLayout : LayoutGroup
    {
        public int maxColumns = 2;
        public float minimumCellWidth = 400;
        public float spacing = 20;
        private int Columns => Mathf.Clamp(Mathf.FloorToInt((rectTransform.rect.width - padding.horizontal + spacing) / (minimumCellWidth + spacing)), 1, maxColumns);
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            SetLayoutInputForAxis(0, 0, 1, 0);
        }
        public override void CalculateLayoutInputVertical()
        {
            float height = padding.vertical;
            for (int row = 0; row * Columns < rectChildren.Count; row++) height += RowHeight(row) + (row > 0 ? spacing : 0);
            SetLayoutInputForAxis(height, height, 0, 1);
        }
        private float RowHeight(int row)
        {
            float height = 0;
            for (int i = row * Columns; i < Mathf.Min((row + 1) * Columns, rectChildren.Count); i++)
                height = Mathf.Max(height, LayoutUtility.GetMinHeight(rectChildren[i]), LayoutUtility.GetPreferredHeight(rectChildren[i]));
            return height;
        }
        public override void SetLayoutHorizontal()
        {
            float width = Mathf.Max(0, (rectTransform.rect.width - padding.horizontal - (Columns - 1) * spacing) / Columns);
            for (int i = 0; i < rectChildren.Count; i++) SetChildAlongAxis(rectChildren[i], 0, padding.left + (i % Columns) * (width + spacing), width);
        }
        public override void SetLayoutVertical()
        {
            float y = padding.top;
            for (int row = 0; row * Columns < rectChildren.Count; row++)
            {
                float height = RowHeight(row);
                for (int i = row * Columns; i < Mathf.Min((row + 1) * Columns, rectChildren.Count); i++) SetChildAlongAxis(rectChildren[i], 1, y, height);
                y += height + spacing;
            }
        }
    }

    // Each page has its own crisp, quiet illustration. Nothing in this layer receives input.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIPageArtwork : MaskableGraphic
    {
        public UIPageDesign design;
        private VertexHelper mesh;
        private Vector2 origin;
        private float unit;
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); mesh = helper;
            Rect rect = rectTransform.rect;
            origin = new Vector2(rect.xMax - rect.width * .17f, rect.center.y + rect.height * .16f);
            unit = Mathf.Min(rect.width * .22f, rect.height * .34f);
            Color wash = color; wash.a *= .12f;
            Disc(new Vector2(.15f, .06f), 1.06f, wash);
            Disc(new Vector2(-.28f, -.44f), .58f, wash);
            switch (design)
            {
                case UIPageDesign.Menu:
                    Arc(new Vector2(0, .35f), .65f, 0, 180, 2.5f);
                    Curve(new Vector2(-.65f, .35f), new Vector2(.65f, .35f), .08f, 2);
                    for (int i = 0; i < 7; i++)
                    {
                        float x = -.5f + i / 6f;
                        for (int j = 0; j < 36; j++)
                        {
                            float a = j / 36f, b = (j + 1) / 36f;
                            Stroke(new Vector2(x + Mathf.Sin(a * 6 + i) * .09f, .3f - a * 1.3f), new Vector2(x + Mathf.Sin(b * 6 + i) * .09f, .3f - b * 1.3f), 1.6f);
                        }
                    }
                    break;
                case UIPageDesign.Pause:
                    for (int i = 0; i < 10; i++)
                    {
                        float angle = Mathf.Lerp(20, 160, i / 9f) * Mathf.Deg2Rad;
                        Stroke(new Vector2(0, -.5f), new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .82f, 2);
                    }
                    Arc(new Vector2(0, -.5f), .88f, 18, 162, 2.5f); break;
                case UIPageDesign.Chapters:
                    for (int i = 0; i < 70; i++)
                    {
                        float a = i / 70f, b = (i + 1) / 70f;
                        Stroke(new Vector2(Mathf.Sin(a * 8) * .4f, .85f - a * 1.7f), new Vector2(Mathf.Sin(b * 8) * .4f, .85f - b * 1.7f), 2);
                    }
                    for (int i = 0; i < 5; i++) Arc(new Vector2(Mathf.Sin(i * 2) * .4f, .85f - i * .425f), .07f, 0, 360, 3);
                    break;
                case UIPageDesign.Journal:
                    Stroke(new Vector2(-.65f, .75f), new Vector2(-.65f, -.75f), 3);
                    for (int i = 0; i < 7; i++) Stroke(new Vector2(-.45f, .65f - i * .2f), new Vector2(.65f, .65f - i * .2f), 1.5f);
                    for (int i = 0; i < 5; i++) Arc(new Vector2(-.65f, .6f - i * .3f), .065f, 100, 280, 2); break;
                case UIPageDesign.Records:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = .65f - i * .52f;
                        Stroke(new Vector2(-.7f, y), new Vector2(.66f, y), 2);
                        Stroke(new Vector2(.66f, y), new Vector2(.66f, y - .36f), 2);
                        Stroke(new Vector2(.66f, y - .36f), new Vector2(-.7f, y - .36f), 2);
                        Stroke(new Vector2(-.7f, y - .36f), new Vector2(-.7f, y), 2);
                        Arc(new Vector2(.4f, y - .18f), .085f, 0, 360, 2);
                        Stroke(new Vector2(-.52f, y - .14f), new Vector2(.08f, y - .14f), 1.5f);
                    }
                    break;
                case UIPageDesign.Credits:
                    Vector2[] stars = { new Vector2(-.7f, .2f), new Vector2(-.2f, .68f), new Vector2(.6f, .38f),
                        new Vector2(.4f, -.4f), new Vector2(-.38f, -.64f) };
                    for (int i = 0; i < stars.Length; i++)
                    {
                        Stroke(stars[i], stars[(i + 1) % stars.Length], 1.2f);
                        Arc(stars[i], .075f, 0, 360, 2.5f);
                    }
                    break;
                case UIPageDesign.Construction:
                    for (int i = 0; i < 9; i++)
                    {
                        float p = -.8f + i * .2f;
                        Stroke(new Vector2(p, -.8f), new Vector2(p, .8f), .7f);
                        Stroke(new Vector2(-.8f, p), new Vector2(.8f, p), .7f);
                    }
                    Stroke(new Vector2(-.6f, -.25f), new Vector2(.2f, .55f), 3);
                    Stroke(new Vector2(.2f, .55f), new Vector2(.7f, .1f), 3);
                    Stroke(new Vector2(.7f, .1f), new Vector2(-.1f, -.7f), 3);
                    Stroke(new Vector2(-.1f, -.7f), new Vector2(-.6f, -.25f), 3); break;
                case UIPageDesign.Completion:
                    Arc(Vector2.zero, .56f, 0, 360, 2.5f);
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i * Mathf.PI / 8;
                        Vector2 v = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); Stroke(v * .69f, v * .84f, 2);
                    }
                    Stroke(new Vector2(-.3f, 0), new Vector2(-.06f, -.24f), 4); Stroke(new Vector2(-.06f, -.24f), new Vector2(.36f, .27f), 4); break;
                case UIPageDesign.Ending:
                    for (int i = 0; i < 3; i++) Curve(new Vector2(0, -.8f), new Vector2((i - 1) * .6f, .65f), .18f, 2.5f);
                    for (int i = 0; i < 3; i++) Arc(new Vector2((i - 1) * .6f, .65f), .09f, 0, 360, 2); break;
                case UIPageDesign.Guide:
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = (90 + i * 120) * Mathf.Deg2Rad;
                        Vector2 center = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .6f;
                        Arc(center, .22f, 0, 360, 2.5f);
                        Stroke(center + new Vector2(-.08f, 0), center + new Vector2(.08f, 0), 2);
                        Stroke(center + new Vector2(0, -.08f), center + new Vector2(0, .08f), 2);
                    }
                    Arc(Vector2.zero, .6f, -15, 55, 2); Arc(Vector2.zero, .6f, 110, 180, 2);
                    Arc(Vector2.zero, .6f, 235, 305, 2); break;
                case UIPageDesign.Failure:
                    Arc(Vector2.zero, .6f, 0, 360, 3); Arc(Vector2.zero, .31f, 0, 360, 2);
                    for (int i = 0; i < 4; i++)
                    {
                        float angle = (45 + i * 90) * Mathf.Deg2Rad;
                        Vector2 v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)); Stroke(v * .31f, v * .6f, 6);
                    }
                    Curve(new Vector2(-1, -.7f), new Vector2(1, -.7f), .1f, 2); break;
                case UIPageDesign.Loading:
                    Arc(Vector2.zero, .6f, 12, 285, 3);
                    Arc(Vector2.zero, .43f, 195, 455, 1.5f);
                    Curve(new Vector2(-.85f, -.8f), new Vector2(.85f, -.8f), .08f, 2); break;
                case UIPageDesign.Dialogue:
                    for (int i = 0; i < 3; i++) Arc(Vector2.zero, .22f + i * .19f, -40, 215, 2 - i * .3f);
                    Arc(new Vector2(-.6f, -.5f), .06f, 0, 360, 2); break;
                case UIPageDesign.Confirmation:
                    Arc(Vector2.zero, .62f, 0, 360, 2);
                    Stroke(new Vector2(0, -.1f), new Vector2(0, .32f), 4);
                    Arc(new Vector2(0, -.28f), .025f, 0, 360, 4); break;
                case UIPageDesign.Settings: case UIPageDesign.Controls:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = .45f - i * .45f; Stroke(new Vector2(-.7f, y), new Vector2(.7f, y), 2);
                        Arc(new Vector2((i - 1) * .3f, y), .095f, 0, 360, 3);
                    }
                    break;
                default:
                    for (int i = 0; i < 4; i++) Curve(new Vector2(-1, -.5f + i * .28f), new Vector2(1, -.5f + i * .28f), .12f, 1.8f); break;
            }
            Arc(new Vector2(-.95f, .7f), .1f, 0, 360, 1.4f);
            Arc(new Vector2(.85f, -.72f), .14f, 0, 360, 1.4f);
        }
        private void Curve(Vector2 a, Vector2 b, float amplitude, float width)
        {
            for (int i = 0; i < 40; i++)
            {
                float t = i / 40f, next = (i + 1) / 40f;
                Stroke(Vector2.Lerp(a, b, t) + Vector2.up * Mathf.Sin(t * Mathf.PI * 3) * amplitude,
                    Vector2.Lerp(a, b, next) + Vector2.up * Mathf.Sin(next * Mathf.PI * 3) * amplitude, width);
            }
        }
        private void Arc(Vector2 center, float radius, float start, float end, float width)
        {
            for (int i = 0; i < 64; i++)
            {
                float a = Mathf.Lerp(start, end, i / 64f) * Mathf.Deg2Rad, b = Mathf.Lerp(start, end, (i + 1) / 64f) * Mathf.Deg2Rad;
                Stroke(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width);
            }
        }
        private void Disc(Vector2 center, float radius, Color tint)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(origin + center * unit, tint, Vector2.zero);
            for (int i = 0; i <= 64; i++)
            {
                float a = i * Mathf.PI / 32; mesh.AddVert(origin + (center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius) * unit, tint, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }
        private void Stroke(Vector2 a, Vector2 b, float width)
        {
            a = origin + a * unit; b = origin + b * unit;
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(a + normal, color, Vector2.zero); mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first + 1, first + 3, first + 2);
        }
    }
}
