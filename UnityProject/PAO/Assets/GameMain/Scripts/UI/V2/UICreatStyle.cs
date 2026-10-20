using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    /// <summary>Presentation used only by Creat's explicitly styled controls.</summary>
    public static class UICreatStyle
    {
        public static readonly Color Paper = new Color32(246, 247, 242, 255);
        public static readonly Color Muted = new Color32(92, 99, 95, 255);
        public static readonly Color Ink = Color.black;
        public static readonly Color Accent = new Color32(104, 202, 176, 255);
        public static readonly Color PanelColor = Paper;
        private static Font creatLabelFont;
        private static bool creatLabelFontResolved;

        public static Image Panel(UIWidgets ui, RectTransform root)
        { return Panel(ui, root, true); }

        public static Image Panel(UIWidgets ui, RectTransform root, bool decorated)
        {
            Image image = root.GetComponent<Image>();
            if (image == null) image = root.gameObject.AddComponent<UICreatPanelImage>();
            else if (!(image is UICreatPanelImage))
            {
                image.enabled = false; image.raycastTarget = false;
                Transform stock = root.Find("Creat paper surface");
                if (stock != null) image = stock.GetComponent<Image>();
                else
                {
                    RectTransform rect = ui.Rect("Creat paper surface", root); UIWidgets.Stretch(rect);
                    rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                    rect.SetAsFirstSibling(); image = rect.gameObject.AddComponent<UICreatPanelImage>();
                }
            }
            image.sprite = null; image.type = Image.Type.Simple; image.raycastTarget = false;
            foreach (BaseMeshEffect effect in root.GetComponents<BaseMeshEffect>()) effect.enabled = false;
            Transform previous = root.Find("Creat panel edge");
            UICreatFrame edge;
            if (previous != null) edge = previous.GetComponent<UICreatFrame>();
            else
            {
                RectTransform rect = ui.Rect("Creat panel edge", root); UIWidgets.Stretch(rect);
                rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                edge = rect.gameObject.AddComponent<UICreatFrame>();
            }
            if (edge != null) edge.raycastTarget = false;
            Transform detailRoot = root.Find("Creat paper registration");
            UICreatPaperDetail detail;
            if (detailRoot != null) detail = detailRoot.GetComponent<UICreatPaperDetail>();
            else
            {
                RectTransform rect = ui.Rect("Creat paper registration", root); UIWidgets.Stretch(rect);
                rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                detail = rect.gameObject.AddComponent<UICreatPaperDetail>();
            }
            if (detail != null) detail.raycastTarget = false;
            if (detailRoot != null) detailRoot.gameObject.SetActive(decorated);
            else if (detail != null) detail.gameObject.SetActive(decorated);
            UICreatPanelTint tint = root.GetComponent<UICreatPanelTint>();
            if (tint == null) tint = root.gameObject.AddComponent<UICreatPanelTint>();
            tint.Configure(image, edge, detail);
            return image;
        }

        public static void SetPanelDetail(RectTransform root, bool visible)
        {
            if (root == null) return;
            Transform detail = root.Find("Creat paper registration");
            if (detail != null) detail.gameObject.SetActive(visible);
        }

        public static Text Label(UIWidgets ui, Transform parent, string value, int size, bool muted = false)
        {
            Text label = ui.Label(parent, value, size, muted ? Muted : Ink);
            if (!creatLabelFontResolved)
            {
                creatLabelFontResolved = true;
                try
                {
                    string[] installed = Font.GetOSInstalledFontNames();
                    foreach (string candidate in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC" })
                    {
                        if (!System.Array.Exists(installed, name => string.Equals(name, candidate, System.StringComparison.OrdinalIgnoreCase))) continue;
                        creatLabelFont = Font.CreateDynamicFontFromOSFont(candidate, 48);
                        if (creatLabelFont != null) { creatLabelFont.hideFlags = HideFlags.DontSave; break; }
                    }
                }
                catch (System.Exception) { creatLabelFont = null; }
            }
            if (creatLabelFont != null) label.font = creatLabelFont;
            UICreatLabelTint tint = label.gameObject.AddComponent<UICreatLabelTint>();
            tint.Configure(muted);
            return label;
        }

        public static void StyleButton(Button button, bool primary = false)
        {
            if (button == null) return;
            Disable<UIButtonFeedback>(button); Disable<UIControlAccent>(button);
            Disable<UIFocusRing>(button); Disable<UIFluidGradient>(button);
            foreach (Shadow effect in button.GetComponents<Shadow>()) effect.enabled = false;
            foreach (string name in new[] { "Press ripple", "Current category", "Control accent" })
            {
                Transform decoration = button.transform.Find(name);
                if (decoration != null) decoration.gameObject.SetActive(false);
            }
            button.transition = Selectable.Transition.None;
            button.transform.localScale = Vector3.one;
            Image hitArea = button.GetComponent<Image>();
            if (hitArea == null) hitArea = button.gameObject.AddComponent<Image>();
            hitArea.sprite = null; hitArea.type = Image.Type.Simple;
            hitArea.CrossFadeColor(Color.white, 0, true, true);
            hitArea.color = Color.clear; hitArea.raycastTarget = true;
            button.targetGraphic = hitArea;
            Transform previousSurface = button.transform.Find("Creat button surface");
            UICreatPanelImage surface;
            if (previousSurface != null) surface = previousSurface.GetComponent<UICreatPanelImage>();
            else
            {
                var rect = new GameObject("Creat button surface", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
                rect.gameObject.layer = button.gameObject.layer; rect.SetParent(button.transform, false); UIWidgets.Stretch(rect);
                rect.GetComponent<LayoutElement>().ignoreLayout = true;
                surface = rect.gameObject.AddComponent<UICreatPanelImage>();
            }
            surface.raycastTarget = false; surface.transform.SetAsFirstSibling();
            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                UIStyledText styled = label.GetComponent<UIStyledText>();
                if (styled != null)
                {
                    float scale = UISettings.Current != null ? UISettings.Current.textScale : 1;
                    styled.Configure(Mathf.RoundToInt(label.fontSize / Mathf.Max(.1f, scale)), false,
                        UIRuntime.Instance != null ? UIRuntime.Instance.Config : null);
                }
                UICreatLabelTint labelTint = label.GetComponent<UICreatLabelTint>();
                if (labelTint != null) labelTint.enabled = false;
            }
            UICreatButtonFeedback feedback = button.GetComponent<UICreatButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<UICreatButtonFeedback>();
            feedback.Configure(button, surface, label, primary);
        }

        public static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            UICreatButtonFeedback feedback = button.GetComponent<UICreatButtonFeedback>();
            if (feedback == null) { StyleButton(button); feedback = button.GetComponent<UICreatButtonFeedback>(); }
            feedback.SetSelected(selected);
        }

        private static void Disable<T>(Button button) where T : Behaviour
        {
            foreach (T component in button.GetComponents<T>())
            {
                MonoBehaviour behaviour = component as MonoBehaviour;
                if (behaviour != null) behaviour.StopAllCoroutines();
                component.enabled = false;
            }
        }

        internal static bool HighContrast => UISettings.Current != null && UISettings.Current.highContrast;
        internal static bool ReducedMotion => UISettings.Current != null && UISettings.Current.reducedMotion;
    }

    public sealed class UICreatPanelTint : MonoBehaviour
    {
        private Image surface;
        private UICreatFrame edge;
        private UICreatPaperDetail detail;
        public void Configure(Image image, UICreatFrame border, UICreatPaperDetail registration)
        { surface = image; edge = border; detail = registration; Refresh(); }
        private void OnEnable() { UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UISettings.Changed -= Refresh; }
        private void Refresh()
        {
            if (surface != null) surface.color = UICreatStyle.HighContrast ? Color.white : UICreatStyle.PanelColor;
            if (edge != null)
            {
                edge.color = UICreatStyle.Ink;
                edge.Thickness = UICreatStyle.HighContrast ? 2 : 1;
            }
            if (detail != null) detail.color = UICreatStyle.HighContrast ? UICreatStyle.Ink : new Color(0, 0, 0, .62f);
        }
    }

    public sealed class UICreatLabelTint : MonoBehaviour
    {
        private bool muted;
        public void Configure(bool secondary) { muted = secondary; Refresh(); }
        private void OnEnable() { UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UISettings.Changed -= Refresh; }
        private void Refresh()
        {
            GetComponent<Text>().color = UICreatStyle.HighContrast ? UICreatStyle.Ink : muted ? UICreatStyle.Muted : UICreatStyle.Ink;
        }
    }

    /// <summary>Unequal clipped corners keep the paper stock crisp and slightly irregular.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatPanelImage : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Vector2[] points = UICreatFrame.Corners(rectTransform.rect);
            mesh.AddVert(rectTransform.rect.center, color, Vector2.zero);
            foreach (Vector2 point in points) mesh.AddVert(point, color, Vector2.zero);
            for (int i = 0; i < points.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % points.Length + 1);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatFrame : MaskableGraphic
    {
        private float thickness = 1;
        public float Thickness { get => thickness; set { if (Mathf.Approximately(thickness, value)) return; thickness = value; SetVerticesDirty(); } }
        internal static Vector2[] Corners(Rect rect)
        {
            float cut = Mathf.Min(20, Mathf.Min(rect.width, rect.height) * .18f);
            float lowerCut = cut * .42f;
            return new[] {
                new Vector2(rect.xMin + lowerCut, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax - cut), new Vector2(rect.xMax - cut, rect.yMax),
                new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin + lowerCut) };
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            Vector2[] outer = Corners(rect);
            Rect inset = new Rect(rect.xMin + thickness, rect.yMin + thickness,
                Mathf.Max(0, rect.width - thickness * 2), Mathf.Max(0, rect.height - thickness * 2));
            Vector2[] inner = Corners(inset);
            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length, start = mesh.currentVertCount;
                mesh.AddVert(outer[i], color, Vector2.zero); mesh.AddVert(outer[next], color, Vector2.zero);
                mesh.AddVert(inner[next], color, Vector2.zero); mesh.AddVert(inner[i], color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }

    /// <summary>Original registration dots and short bars sit entirely inside the paper's narrow margins.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatPaperDetail : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            if (rect.width < 120 || rect.height < 68) return;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 2; column++)
                    Quad(mesh, rect.xMin + 6 + column * 3, rect.yMax - 24 - row * 3, 1.2f, 1.2f);
            // A compact, intentionally non-scannable sequence; it never enters the text column.
            for (int i = 0; i < 7; i++)
                Quad(mesh, rect.xMax - 10, rect.yMax - 30 - i * 2.5f, i % 3 == 0 ? 5 : 3, i % 2 == 0 ? 1 : 1.5f);
        }
        private void Quad(VertexHelper mesh, float x, float y, float width, float height)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector2(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector2(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector2(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatToolGlyph : MaskableGraphic
    {
        public int Kind;
        protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            float size = Mathf.Min(rect.width, rect.height); Vector2 center = rect.center;
            if (size <= 0) return;
            float weight = Mathf.Clamp(size * .042f, 1.2f, 2.4f);
            if (Kind == 0)
            {
                Vector2 top = center + new Vector2(0, .36f) * size;
                Vector2 left = center + new Vector2(-.34f, .16f) * size;
                Vector2 right = center + new Vector2(.34f, .16f) * size;
                Vector2 middle = center + new Vector2(0, -.04f) * size;
                Vector2 lowerLeft = center + new Vector2(-.34f, -.24f) * size;
                Vector2 lowerRight = center + new Vector2(.34f, -.24f) * size;
                Vector2 bottom = center + new Vector2(0, -.44f) * size;
                Color mint = UICreatStyle.Accent; mint.a *= color.a * .85f;
                UICreatPrintMesh.Quad(mesh, top, right, middle, left, mint);
                UICreatPrintMesh.Line(mesh, top, left, weight, color);
                UICreatPrintMesh.Line(mesh, top, right, weight, color);
                UICreatPrintMesh.Line(mesh, left, middle, weight, color);
                UICreatPrintMesh.Line(mesh, right, middle, weight, color);
                UICreatPrintMesh.Line(mesh, left, lowerLeft, weight, color);
                UICreatPrintMesh.Line(mesh, right, lowerRight, weight, color);
                UICreatPrintMesh.Line(mesh, lowerLeft, bottom, weight, color);
                UICreatPrintMesh.Line(mesh, lowerRight, bottom, weight, color);
                UICreatPrintMesh.Line(mesh, middle, bottom, weight, color);
            }
            else if (Kind == 1)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = (i * 45 + 22.5f) * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    UICreatPrintMesh.Line(mesh, center + direction * size * .16f,
                        center + direction * size * (i % 2 == 0 ? .39f : .32f), weight, color);
                }
                Color mint = UICreatStyle.Accent; mint.a *= color.a;
                UICreatPrintMesh.Rect(mesh, new Rect(center - Vector2.one * size * .035f, Vector2.one * size * .07f), mint);
            }
            else
            {
                Vector2[] corners = { center + new Vector2(-.34f, -.34f) * size,
                    center + new Vector2(-.34f, .34f) * size, center + new Vector2(.34f, .34f) * size,
                    center + new Vector2(.34f, -.34f) * size };
                for (int side = 0; side < 4; side++)
                    for (int dash = 0; dash < 3; dash++)
                        UICreatPrintMesh.Line(mesh, Vector2.Lerp(corners[side], corners[(side + 1) % 4], dash / 3f),
                            Vector2.Lerp(corners[side], corners[(side + 1) % 4], (dash + .7f) / 3f), weight, color);
                Color mint = UICreatStyle.Accent; mint.a *= color.a;
                UICreatPrintMesh.Line(mesh, center - Vector2.right * size * .07f, center + Vector2.right * size * .07f, weight, mint);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatHudPrint : MaskableGraphic
    {
        public int Kind;
        protected override void OnEnable() { base.OnEnable(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            if (rect.width < 90 || rect.height < 36) return;
            if (Kind == 0)
            {
                float left = Mathf.Max(rect.xMin + rect.width * .73f, rect.xMax - 79);
                float step = Mathf.Max(2, (rect.xMax - 18 - left) / 13);
                for (int i = 0; i < 12; i++)
                    UICreatPrintMesh.Rect(mesh, new Rect(left + i * step, rect.yMin + 9,
                        i % 3 == 0 ? 2 : 1, i % 4 == 0 ? 14 : 9), color);
            }
            else if (Kind == 1)
            {
                float left = rect.xMin + 18, width = Mathf.Max(1, rect.width - 36);
                for (int i = 0; i <= 10; i++)
                {
                    float height = i % 5 == 0 ? 5 : 2;
                    UICreatPrintMesh.Rect(mesh, new Rect(left + width * i / 10, rect.yMin + 5, 1, height), color);
                    UICreatPrintMesh.Rect(mesh, new Rect(left + width * i / 10, rect.yMax - 5 - height, 1, height), color);
                }
            }
            else
            {
                // Leave the outermost ten pixels to the panel's own registration detail.
                for (int i = 0; i < 9; i++)
                    UICreatPrintMesh.Rect(mesh, new Rect(rect.xMax - 35, rect.center.y - 18 + i * 4,
                        i % 3 == 0 ? 16 : 11, i % 2 == 0 ? 2 : 1), color);
            }
        }
    }

    internal static class UICreatPrintMesh
    {
        public static void Rect(VertexHelper mesh, Rect rect, Color tint)
        { Quad(mesh, rect.min, new Vector2(rect.xMin, rect.yMax), rect.max, new Vector2(rect.xMax, rect.yMin), tint); }
        public static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
        {
            Vector2 direction = to - from;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width * .5f;
            Quad(mesh, from - normal, from + normal, to + normal, to - normal, tint);
        }
        public static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    /// <summary>One finite unscaled response, shared by pointer, keyboard and controller input.</summary>
    public sealed class UICreatButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        private Button button;
        private Image surface, selectedMark, focusMark, paintChip;
        private Text label;
        private UICreatFrame edge;
        private bool primary, selected, hovered, focused, pressed, available, animating, contentPending;
        private float elapsed, duration, submitRemaining;
        private Color fromSurface, toSurface, fromEdge, toEdge;

        public void Configure(Button control, Image background, Text title, bool main)
        {
            button = control; surface = background; label = title; primary = main;
            if (edge == null)
            {
                var rect = new GameObject("Creat button edge", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
                rect.gameObject.layer = gameObject.layer; rect.SetParent(transform, false); UIWidgets.Stretch(rect);
                rect.GetComponent<LayoutElement>().ignoreLayout = true;
                edge = rect.gameObject.AddComponent<UICreatFrame>(); edge.raycastTarget = false;
            }
            if (selectedMark == null)
            {
                var rect = new GameObject("Creat selected marker", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
                rect.gameObject.layer = gameObject.layer; rect.SetParent(transform, false);
                rect.anchorMin = new Vector2(.25f, 0); rect.anchorMax = new Vector2(.75f, 0);
                rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 4); rect.sizeDelta = new Vector2(0, 3);
                rect.GetComponent<LayoutElement>().ignoreLayout = true;
                selectedMark = rect.gameObject.AddComponent<Image>(); selectedMark.raycastTarget = false;
            }
            if (focusMark == null)
            {
                var rect = new GameObject("Creat focus marker", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
                rect.gameObject.layer = gameObject.layer; rect.SetParent(transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                rect.pivot = new Vector2(0, .5f); rect.anchoredPosition = new Vector2(5, 0); rect.sizeDelta = new Vector2(3, 18);
                rect.GetComponent<LayoutElement>().ignoreLayout = true;
                focusMark = rect.gameObject.AddComponent<Image>(); focusMark.raycastTarget = false;
            }
            focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            contentPending = true;
            Refresh(true);
        }
        public void SetSelected(bool value) { if (selected == value) return; selected = value; Refresh(false); }
        private bool Available() => button != null && button.IsActive() && button.IsInteractable() && isActiveAndEnabled;
        private void OnEnable()
        {
            UISettings.Changed += SettingsChanged;
            focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            Refresh(true);
        }
        private void OnDisable()
        {
            UISettings.Changed -= SettingsChanged; StopAllCoroutines();
            animating = hovered = focused = pressed = false; submitRemaining = 0;
            Refresh(true);
        }
        private void OnDestroy() { UISettings.Changed -= SettingsChanged; animating = false; StopAllCoroutines(); }
        private void SettingsChanged() { submitRemaining = 0; Refresh(true); }
        private void OnTransformChildrenChanged() { contentPending = true; }
        private void Update()
        {
            if (contentPending)
            {
                // Swatches may be inserted after styling; keep the cut paper behind their actual paint color.
                if (surface != null && surface.transform.GetSiblingIndex() != 0) surface.transform.SetAsFirstSibling();
                Transform chip = transform.Find("Paint swatch");
                paintChip = chip != null ? chip.GetComponent<Image>() : null;
                contentPending = false;
                Refresh(true);
            }
            bool nowAvailable = Available();
            if (available != nowAvailable)
            {
                if (!nowAvailable) { pressed = false; submitRemaining = 0; }
                Refresh(false);
            }
            if (submitRemaining > 0)
            {
                submitRemaining -= Time.unscaledDeltaTime;
                if (submitRemaining <= 0) Refresh(false);
            }
            if (!animating) return;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration); t = 1 - Mathf.Pow(1 - t, 3);
            if (surface != null) surface.color = Color.Lerp(fromSurface, toSurface, t);
            if (edge != null) edge.color = Color.Lerp(fromEdge, toEdge, t);
            if (elapsed >= duration) animating = false;
        }
        private void Refresh(bool instant)
        {
            if (button == null || edge == null) return;
            available = Available();
            bool contrast = UICreatStyle.HighContrast, active = available && (hovered || focused);
            bool down = available && (pressed || submitRemaining > 0);
            if (paintChip == null)
            {
                Transform chip = transform.Find("Paint swatch");
                if (chip != null) paintChip = chip.GetComponent<Image>();
            }
            bool reversed = primary || selected;
            if (down) reversed = !reversed;
            Color textColor = reversed ? UICreatStyle.Paper : UICreatStyle.Ink;
            if (paintChip != null)
            {
                Color linear = paintChip.color.linear;
                float luminance = linear.r * .2126f + linear.g * .7152f + linear.b * .0722f;
                textColor = luminance > .18f ? Color.black : Color.white;
            }
            Color face = reversed ? UICreatStyle.Ink : contrast || active ? Color.white : UICreatStyle.Paper;
            toSurface = available ? face : new Color32(224, 226, 221, 255);
            toEdge = !available ? new Color32(143, 147, 142, 255) : active && !contrast ? UICreatStyle.Accent :
                reversed ? UICreatStyle.Paper : UICreatStyle.Ink;
            edge.Thickness = active || selected || down || contrast ? 2 : 1;
            if (label != null) label.color = !available ? UICreatStyle.Muted : textColor;
            UICreatToolGlyph glyph = GetComponentInChildren<UICreatToolGlyph>();
            if (glyph != null) glyph.color = !available ? UICreatStyle.Muted : textColor;
            if (selectedMark != null)
            {
                selectedMark.gameObject.SetActive(selected);
                selectedMark.color = !available ? UICreatStyle.Muted : paintChip != null ? textColor : contrast ? textColor : UICreatStyle.Accent;
            }
            if (focusMark != null)
            {
                focusMark.gameObject.SetActive(active);
                focusMark.color = paintChip != null || contrast ? textColor : UICreatStyle.Accent;
            }
            // Invert ink and paper together, keeping labels readable throughout the finite edge response.
            if (surface != null) surface.color = toSurface;
            animating = !instant && !UICreatStyle.ReducedMotion && isActiveAndEnabled;
            if (!animating)
            {
                if (surface != null) surface.color = toSurface;
                edge.color = toEdge;
                return;
            }
            fromSurface = surface != null ? surface.color : toSurface; fromEdge = edge.color;
            elapsed = 0; duration = down ? .09f : .12f;
        }
        public void OnPointerEnter(PointerEventData data)
        {
            hovered = true; Refresh(false);
            if (Available()) PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UIHover, Vector3.zero, .35f);
        }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(false); }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !Available()) return;
            pressed = true; Refresh(false); PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UIConfirm, Vector3.zero);
        }
        public void OnPointerUp(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = false; Refresh(false); } }
        public void OnSelect(BaseEventData data)
        {
            focused = true; Refresh(false); RevealSelection();
            if (isActiveAndEnabled) StartCoroutine(RevealAfterLayout());
        }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; submitRemaining = 0; Refresh(false); }
        public void OnSubmit(BaseEventData data)
        {
            if (!Available()) return;
            submitRemaining = .09f; Refresh(false); PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UIConfirm, Vector3.zero);
        }
        private IEnumerator RevealAfterLayout()
        {
            yield return null;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) RevealSelection();
        }
        private void RevealSelection()
        {
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, (RectTransform)transform);
            Rect view = scroll.viewport.rect;
            float x = scroll.horizontal && bounds.min.x < view.xMin ? view.xMin - bounds.min.x :
                scroll.horizontal && bounds.max.x > view.xMax ? view.xMax - bounds.max.x : 0;
            float y = scroll.vertical && bounds.min.y < view.yMin ? view.yMin - bounds.min.y :
                scroll.vertical && bounds.max.y > view.yMax ? view.yMax - bounds.max.y : 0;
            scroll.StopMovement(); scroll.content.anchoredPosition += new Vector2(x, y);
        }
    }
}
