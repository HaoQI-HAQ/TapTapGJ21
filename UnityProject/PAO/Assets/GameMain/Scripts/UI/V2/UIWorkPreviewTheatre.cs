using System;
using PAO.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    /// <summary>The presentation for Creat's actual work preview; viewing and archives keep their existing APIs.</summary>
    public sealed class UIWorkPreviewTheatre : MonoBehaviour
    {
        private static readonly Color White = new Color(.95f, .94f, .91f);
        private static readonly Color Muted = new Color(.65f, .65f, .63f);
        private static Font titleFont;
        private UIWidgets ui;
        private UIPage page;
        private SandboxPreviewView preview;
        private RectTransform root, viewport, header, actions, identity, tools, help;
        private Text brand, title, empty;
        private UIWorkTheatreGraphic frame;
        private UIWorkTheatreGraphic rules;
        private UIWorkTheatreAction[] top, controls;
        private Text[] instructions;
        private UIWorkTheatreGraphic[] mouseIcons;
        private Vector2 lastSize;
        private float lastScale = -1;
        private int lastView = -2;
        private int lastLanguage = -1;
        private bool? lastGround;
        private string lastTitle;

        private void OnEnable() { UISettings.Changed += SettingsChanged; }
        private void OnDisable() { UISettings.Changed -= SettingsChanged; }
        private void SettingsChanged()
        {
            if (root == null || controls == null || instructions == null) return;
            RefreshLanguage(); Arrange();
        }

        public static UIWorkPreviewTheatre Create(UIWidgets ui, UIPage page, SandboxPreviewView preview,
            RawImage viewport, Action save, Action archives, Action back)
        {
            var presentation = ui.Rect("Creation theatre", page.transform).gameObject.AddComponent<UIWorkPreviewTheatre>();
            presentation.ui = ui; presentation.page = page; presentation.preview = preview;
            presentation.viewport = viewport.rectTransform;
            presentation.root = (RectTransform)presentation.transform; UIWidgets.Stretch(presentation.root);
            presentation.Build(save, archives, back);
            return presentation;
        }

        private void Build(Action save, Action archives, Action back)
        {
            page.GetComponent<Image>().color = new Color(.025f, .029f, .034f);
            RectTransform background = ui.Rect("Theatre atmosphere", page.transform);
            background.SetAsFirstSibling(); UIWidgets.Stretch(background);
            UIWorkTheatreGraphic wash = background.gameObject.AddComponent<UIWorkTheatreGraphic>();
            wash.kind = UIWorkTheatreGraphic.Kind.Backdrop; wash.raycastTarget = false;
            frame = Graphic("Viewfinder corners", root, UIWorkTheatreGraphic.Kind.Frame);
            UIWidgets.Stretch(frame.rectTransform);
            header = ui.Rect("Preview header", root);
            brand = Label(header, "P A O   /   C R E A T I O N   T H E A T R E", 12);
            actions = ui.Rect("Preview actions", header);
            top = new[] {
                ActionButton(actions, "Save preview work", "", UIWorkTheatreGraphic.Icon.Save, save),
                ActionButton(actions, "Open preview work archives", "", UIWorkTheatreGraphic.Icon.Archives, archives),
                ActionButton(actions, "Close creation preview", "", UIWorkTheatreGraphic.Icon.Escape, back)
            };

            identity = ui.Rect("Preview artwork information", root);
            title = Label(identity, "Next", 48);
            if (titleFont == null) titleFont = Font.CreateDynamicFontFromOSFont(new[] { "SimSun", "Noto Serif CJK SC", "Songti SC" }, 64);
            title.font = titleFont != null ? titleFont : title.font;
            title.name = "Current artwork name"; title.supportRichText = false;
            title.resizeTextForBestFit = true; title.resizeTextMinSize = 18;
            title.lineSpacing = 1.05f; title.alignment = TextAnchor.UpperLeft;
            RefreshTitle();
            empty = Label(root, "", 23, Muted);
            empty.alignment = TextAnchor.MiddleCenter; empty.gameObject.SetActive(preview.ObjectCount == 0);

            tools = ui.Rect("Preview controls", root);
            rules = Graphic("Viewing tool rules", tools, UIWorkTheatreGraphic.Kind.Rule);
            UIWidgets.Stretch(rules.rectTransform); rules.color = new Color(1, 1, 1, .28f);
            controls = new UIWorkTheatreAction[6];
            controls[0] = ActionButton(tools, "F  取景全部", "", UIWorkTheatreGraphic.Icon.Frame, () => preview.FrameAll());
            string[] names = { "透视", "正面", "侧面", "顶视" };
            for (int i = 0; i < 4; i++)
            {
                int view = i;
                controls[i + 1] = ActionButton(tools, names[i], "",
                    (UIWorkTheatreGraphic.Icon)((int)UIWorkTheatreGraphic.Icon.Perspective + i), () => SelectView(view));
            }
            controls[5] = ActionButton(tools, "地面 开/关", "", UIWorkTheatreGraphic.Icon.Grid, () => preview.ToggleGrid());

            help = ui.Rect("Preview instructions", root);
            instructions = new Text[4]; mouseIcons = new UIWorkTheatreGraphic[4];
            for (int i = 0; i < 4; i++)
            {
                mouseIcons[i] = Graphic("Mouse instruction " + i, help, UIWorkTheatreGraphic.Kind.Symbol);
                mouseIcons[i].icon = i == 3 ? UIWorkTheatreGraphic.Icon.Frame :
                    i == 0 ? UIWorkTheatreGraphic.Icon.MouseLeft : i == 1 ? UIWorkTheatreGraphic.Icon.MouseWheel : UIWorkTheatreGraphic.Icon.MouseMiddle;
                instructions[i] = Label(help, "", 16);
                instructions[i].resizeTextForBestFit = true; instructions[i].resizeTextMinSize = 12;
            }
            page.Content = identity; page.Footer = tools;
            // Toasts must remain visible when the stock page body is hidden.
            page.NoticeArea.SetParent(root, false);
            page.NoticeArea.GetComponent<Image>().color = new Color(.1f, .095f, .08f, .98f);
            page.NoticeLabel.GetComponent<UIStyledText>().Configure(21, false, ui.Config);
            page.NoticeLabel.color = White;
            RefreshLanguage(); RefreshSelection(); Arrange();
        }

        private Text Label(Transform parent, string value, int size, Color? color = null)
        {
            Text text = ui.Label(parent, value, size, color ?? White); text.fontStyle = FontStyle.Normal;
            return text;
        }
        private UIWorkTheatreGraphic Graphic(string name, Transform parent, UIWorkTheatreGraphic.Kind kind)
        {
            var graphic = ui.Rect(name, parent).gameObject.AddComponent<UIWorkTheatreGraphic>();
            graphic.kind = kind; graphic.color = White; graphic.raycastTarget = false; return graphic;
        }
        private UIWorkTheatreAction ActionButton(Transform parent, string name, string label, UIWorkTheatreGraphic.Icon icon, Action click)
        {
            RectTransform rect = ui.Rect(name, parent);
            Image surface = rect.gameObject.AddComponent<UIWorkTheatreSurface>(); surface.color = Color.clear;
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = surface; button.transition = Selectable.Transition.None;
            surface.CrossFadeColor(Color.white, 0, true, true);
            button.onClick.AddListener(() => click?.Invoke());
            UIWorkTheatreGraphic symbol = Graphic("Line icon", rect, UIWorkTheatreGraphic.Kind.Symbol); symbol.icon = icon;
            Text text = Label(rect, label, 20);
            var action = rect.gameObject.AddComponent<UIWorkTheatreAction>(); action.Configure(surface, symbol, text);
            return action;
        }

        public void SelectView(int view)
        {
            if (preview == null) return;
            if (view == 0) preview.SetView(35, 24);
            else if (view == 1) preview.SetView(0, 0);
            else if (view == 2) preview.SetView(90, 0);
            else preview.SetView(0, 89);
            RefreshSelection();
        }
        private bool Matches(float yaw, float pitch) => Mathf.Abs(Mathf.DeltaAngle(preview.Yaw, yaw)) < .05f && Mathf.Abs(preview.Pitch - pitch) < .05f;
        private void RefreshSelection()
        {
            int view = Matches(35, 24) ? 1 : Matches(0, 0) ? 2 : Matches(90, 0) ? 3 : Matches(0, 89) ? 4 : -1;
            if (view != lastView)
            {
                lastView = view;
                for (int i = 1; i <= 4; i++) controls[i].SetSelected(i == view);
            }
            if (lastGround != preview.GridVisible)
            {
                lastGround = preview.GridVisible;
                controls[5].SetSwitch(preview.GridVisible);
            }
        }
        private void LateUpdate()
        {
            if (root == null || preview == null) return;
            if (lastLanguage != UISettings.Current.language) { RefreshLanguage(); Arrange(); }
            if (root.rect.size != lastSize || !Mathf.Approximately(lastScale, UISettings.Current.textScale)) Arrange();
            RefreshSelection();
            RefreshTitle();
        }

        private void RefreshLanguage()
        {
            lastLanguage = UISettings.Current.language;
            top[0].Label.text = UISettings.Text("保存作品", "Save work");
            top[1].Label.text = UISettings.Text("作品存档", "Saved works");
            top[2].Label.text = UISettings.Text("返回建造", "Back");
            empty.text = UISettings.Text("返回建造，开始你的第一份作品。", "Return to build and create your first work.");
            string[] views = { UISettings.Text("取景全部", "Fit all"), UISettings.Text("透视", "Perspective"),
                UISettings.Text("正面", "Front"), UISettings.Text("侧面", "Side"), UISettings.Text("顶视", "Top"), UISettings.Text("地面", "Ground") };
            for (int i = 0; i < controls.Length; i++) controls[i].Label.text = views[i];
            string[] hints = { UISettings.Text("拖动旋转", "Rotate"), UISettings.Text("滚轮缩放", "Zoom"),
                UISettings.Text("中键 / Shift + 拖动平移", "Middle / Shift + drag"), UISettings.Text("重新取景", "Fit all") };
            for (int i = 0; i < instructions.Length; i++) instructions[i].text = hints[i];
        }

        private void RefreshTitle()
        {
            string name = BubbleSandbox.Instance != null ? BubbleSandbox.Instance.CurrentWorkName : null;
            name = string.IsNullOrWhiteSpace(name) ? "Next" : name;
            if (lastTitle == name) return;
            lastTitle = title.text = name;
        }

        public void Arrange()
        {
            float w = root.rect.width, h = root.rect.height;
            if (w < 1 || h < 1) return;
            lastSize = root.rect.size; lastScale = UISettings.Current.textScale;
            bool narrow = w < 1100;
            bool twoRows = w < 900 || (w < 1250 && (lastScale > 1.15f || lastLanguage != 0));
            float margin = narrow ? 34 : Mathf.Min(100, w * .057f);
            Box(header, 30, 16, w - 60, narrow ? 112 : 48);
            Box(brand.rectTransform, 0, 0, narrow ? w - 60 : Mathf.Max(350, w - 690), 28); brand.fontSize = 12;
            float actionWidth = narrow ? w - 60 : 570;
            Box(actions, narrow ? 0 : w - 60 - actionWidth, narrow ? 42 : 0, actionWidth, 44);
            for (int i = 0; i < 3; i++)
            {
                Box((RectTransform)top[i].transform, i * actionWidth / 3, 0, actionWidth / 3 - 8, 44);
                top[i].Layout(Mathf.RoundToInt(17 * lastScale), false);
            }
            frame.frameTop = narrow ? 126 : 74; frame.headerRuleEnd = narrow ? 0 : w - actionWidth - 90; frame.SetVerticesDirty();
            // The image fills the page. The title and compact controls float over it,
            // rather than reserving a left information column or a separate model panel.
            UIWidgets.Stretch(viewport);
            float titleY = narrow ? 132 : 82;
            float titleW = narrow ? w - margin * 2 : Mathf.Min(560, w * .4f);
            Box(identity, margin, titleY, titleW, narrow ? 76 : 108);
            Box(title.rectTransform, 0, 0, titleW, identity.rect.height);
            title.fontSize = title.resizeTextMaxSize = Mathf.RoundToInt((narrow ? 38 : 48) * Mathf.Min(lastScale, 1.15f));
            Box(empty.rectTransform, margin, h * .46f, w - margin * 2, 80);
            float toolsTop = h - (twoRows ? 174 : 114);
            Box(tools, margin, toolsTop, w - margin * 2, twoRows ? 120 : 62);
            preview.SetFramingInsets(narrow ? 214 / h : 88 / h, (twoRows ? 188 : 124) / h);
            int columns = twoRows ? 3 : 6;
            rules.columns = columns; rules.SetVerticesDirty();
            float cell = tools.rect.width / columns;
            for (int i = 0; i < controls.Length; i++)
            {
                Box((RectTransform)controls[i].transform, i % columns * cell + 5, 5 + i / columns * 58, cell - 10, twoRows ? 52 : 48);
                controls[i].Layout(Mathf.RoundToInt((narrow ? 17 : 19) * lastScale), i == 5);
            }
            float instructionWidth = Mathf.Min(w - margin * 2, 880);
            Box(help, (w - instructionWidth) * .5f, h - 46, instructionWidth, 36);
            float[] widths = { .18f, .18f, .44f, .20f };
            float x = 0;
            for (int i = 0; i < 4; i++)
            {
                float width = instructionWidth * widths[i];
                Box(mouseIcons[i].rectTransform, x, 7, 28, 28);
                Box(instructions[i].rectTransform, x + 36, 0, width - 38, 36);
                instructions[i].fontSize = Mathf.RoundToInt((narrow ? 13 : 15) * Mathf.Min(lastScale, 1.2f));
                instructions[i].resizeTextMaxSize = instructions[i].fontSize;
                x += width;
            }
            float noticeWidth = Mathf.Min(w - margin * 2, 800);
            Box(page.NoticeArea, (w - noticeWidth) * .5f, toolsTop - 96, noticeWidth, 82);
        }

        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Mathf.Max(1, width), Mathf.Max(1, height));
        }
    }
    public sealed class UIWorkTheatreAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        private Image surface;
        private UIWorkTheatreGraphic icon;
        private UIWorkTheatreGraphic toggle;
        private bool selected, hover, focus, isSwitch, switchOn;
        public Text Label { get; private set; }
        public void Configure(Image surface, UIWorkTheatreGraphic icon, Text label)
        { this.surface = surface; this.icon = icon; Label = label; Refresh(); }
        public void SetSelected(bool value) { if (selected == value) return; selected = value; Refresh(); }
        public void SetSwitch(bool value)
        {
            isSwitch = true; switchOn = value;
            if (toggle == null)
            {
                RectTransform rect = new GameObject("Ground switch", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.gameObject.layer = gameObject.layer; rect.SetParent(transform, false);
                toggle = rect.gameObject.AddComponent<UIWorkTheatreGraphic>();
                toggle.kind = UIWorkTheatreGraphic.Kind.Symbol; toggle.icon = UIWorkTheatreGraphic.Icon.Toggle; toggle.raycastTarget = false;
            }
            toggle.switchOn = value; toggle.SetVerticesDirty(); Refresh();
        }
        public void Layout(int size, bool toggle)
        {
            RectTransform r = (RectTransform)transform;
            float symbolSize = 32, left = Mathf.Max(12, (r.rect.width - (toggle ? 180 : 140)) * .32f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
            icon.rectTransform.pivot = new Vector2(.5f, .5f); icon.rectTransform.anchoredPosition = new Vector2(left + 16, 0);
            icon.rectTransform.sizeDelta = Vector2.one * symbolSize;
            UIWidgets.Stretch(Label.rectTransform);
            Label.rectTransform.offsetMin = new Vector2(left + 46, 0); Label.rectTransform.offsetMax = new Vector2(-10, 0);
            Label.fontSize = size;
            Label.resizeTextForBestFit = true;
            Label.resizeTextMinSize = Mathf.Max(13, Mathf.RoundToInt(size * .75f));
            Label.resizeTextMaxSize = size;
            if (toggle && this.toggle != null)
            {
                this.toggle.rectTransform.anchorMin = this.toggle.rectTransform.anchorMax = new Vector2(1, .5f);
                this.toggle.rectTransform.pivot = new Vector2(1, .5f); this.toggle.rectTransform.sizeDelta = new Vector2(46, 26);
                this.toggle.rectTransform.anchoredPosition = new Vector2(-10, 0);
                Label.rectTransform.offsetMax = new Vector2(-62, 0);
            }
        }
        public void OnPointerEnter(PointerEventData data) { hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hover = false; Refresh(); }
        public void OnSelect(BaseEventData data) { focus = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focus = false; Refresh(); }
        private void Refresh()
        {
            if (surface == null) return;
            surface.color = selected ? (focus ? new Color(.87f, .81f, .7f) : new Color(.94f, .93f, .9f)) : focus || hover ? new Color(1, 1, 1, .065f) : Color.clear;
            Color ink = selected ? new Color(.035f, .04f, .045f) : new Color(.94f, .93f, .89f);
            icon.color = Label.color = ink;
            if (isSwitch) { Label.text = UISettings.Text("地面", "Ground"); if (toggle != null) toggle.color = ink; }
            icon.SetVerticesDirty();
        }
    }

    public sealed class UIWorkTheatreSurface : Image
    {
        public override Texture mainTexture => Texture2D.whiteTexture;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = GetPixelAdjustedRect();
            float radius = Mathf.Min(7, r.height * .2f);
            mesh.AddVert(r.center, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = new Vector2(corner < 2 ? r.xMax - radius : r.xMin + radius,
                    corner == 0 || corner == 3 ? r.yMax - radius : r.yMin + radius);
                for (int step = 0; step <= 6; step++)
                {
                    float angle = (90 - corner * 90 - step * 15) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                }
            }
            for (int i = 1; i <= 28; i++) mesh.AddTriangle(0, i, i == 28 ? 1 : i + 1);
        }
    }

    // Small vector ornaments use the same stroke weight as the reference's viewfinder UI.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIWorkTheatreGraphic : MaskableGraphic
    {
        public enum Kind { Backdrop, Frame, Identity, Rule, Symbol }
        public enum Icon { Rotate, Build, Escape, Frame, Perspective, Front, Side, Top, Grid, Save, Archives, MouseLeft, MouseWheel, MouseMiddle, Toggle }
        public Kind kind;
        public Icon icon;
        public float frameTop = 74, headerRuleEnd, identityRuleY = 264;
        public bool showBarcode = true;
        public bool switchOn;
        public int columns = 6;
        private VertexHelper mesh;
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); mesh = helper; Rect r = rectTransform.rect;
            if (kind == Kind.Backdrop)
            {
                Quad(r, new Color(.025f, .029f, .034f));
                Glow(new Vector2(r.xMin + r.width * .28f, r.yMax - r.height * .12f), r.width * .55f, new Color(.48f, .35f, .2f, .15f));
                Glow(new Vector2(r.xMin + r.width * .55f, r.yMin + r.height * .28f), r.width * .32f, new Color(.47f, .34f, .19f, .12f));
            }
            else if (kind == Kind.Frame)
            {
                float x = 30;
                foreach (Vector2 sign in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) })
                {
                    Vector2 p = new Vector2(sign.x < 0 ? r.xMin + x : r.xMax - x,
                        sign.y < 0 ? r.yMin + 74 : r.yMax - frameTop);
                    Line(p, p - Vector2.right * sign.x * 27, 1); Line(p, p - Vector2.up * sign.y * 27, 1);
                }
                if (headerRuleEnd > 390) Line(new Vector2(r.xMin + 370, r.yMax - 32), new Vector2(r.xMin + headerRuleEnd, r.yMax - 32), .6f);
            }
            else if (kind == Kind.Identity)
            {
                Line(new Vector2(r.xMin, r.yMax - 8), new Vector2(r.xMin, r.yMin + 6), .65f);
                if (identityRuleY > 0)
                {
                    float y = r.yMax - identityRuleY;
                    Line(new Vector2(r.xMin + 24, y), new Vector2(r.xMin + Mathf.Min(r.width - 30, 224), y), .8f);
                }
                if (showBarcode)
                {
                    for (int i = 0; i < 24; i++)
                    { float x = r.xMin + 26 + i * 4.1f; Line(new Vector2(x, r.yMin + 4), new Vector2(x, r.yMin + 24), i % 3 == 0 ? 2.4f : 1); }
                }
            }
            else if (kind == Kind.Rule)
            {
                Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), .45f);
                Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), .45f);
                int rows = columns == 3 ? 2 : 1;
                for (int row = 0; row < rows; row++) for (int i = 1; i < columns; i++)
                {
                    float x = r.xMin + r.width * i / columns, y = r.yMax - 31 - row * 58;
                    Line(new Vector2(x, y - 14), new Vector2(x, y + 14), .7f);
                }
            }
            else DrawIcon(r);
        }
        private void DrawIcon(Rect rect)
        {
            Rect r = new Rect(rect.center - Vector2.one * 14, Vector2.one * 28);
            Vector2 c = r.center;
            if (icon == Icon.Toggle)
            {
                float radius = 11, half = 11;
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI / 24, b = (i + 1) * Mathf.PI / 24;
                    Vector2 aPoint = c + new Vector2(Mathf.Cos(a) * radius + (Mathf.Cos(a) >= 0 ? half : -half), Mathf.Sin(a) * radius);
                    Vector2 bPoint = c + new Vector2(Mathf.Cos(b) * radius + (Mathf.Cos(b) >= 0 ? half : -half), Mathf.Sin(b) * radius);
                    Line(aPoint, bPoint, 1.5f);
                }
                Vector2 knob = c + Vector2.right * (switchOn ? half : -half);
                int v = mesh.currentVertCount; mesh.AddVert(knob, color, Vector2.zero);
                for (int i = 0; i <= 32; i++)
                { float a = i * Mathf.PI / 16; mesh.AddVert(knob + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9, color, Vector2.zero); }
                for (int i = 0; i < 32; i++) mesh.AddTriangle(v, v + i + 1, v + i + 2);
            }
            else if (icon == Icon.Escape || icon == Icon.Frame)
            {
                if (icon == Icon.Escape) r = new Rect(c.x - 18, c.y - 12, 36, 24);
                Outline(r, 1.2f);
                // Key-cap letters are drawn as strokes to stay independent of font fallbacks.
                if (icon == Icon.Frame)
                { Line(c + new Vector2(-4, -8), c + new Vector2(-4, 8)); Line(c + new Vector2(-4, 8), c + new Vector2(6, 8)); Line(c + new Vector2(-4, 1), c + new Vector2(4, 1)); }
                else
                {
                    Line(c + new Vector2(-11, -6), c + new Vector2(-11, 6), 1.2f);
                    foreach (float y in new[] { -6f, 0f, 6f }) Line(c + new Vector2(-11, y), c + new Vector2(-6, y), 1.2f);
                    Vector2[] s = { new Vector2(2, 3), new Vector2(-2, 3), new Vector2(-3, 1), new Vector2(2, -2), new Vector2(2, -5), new Vector2(-3, -5) };
                    for (int i = 1; i < s.Length; i++) Line(c + s[i - 1], c + s[i], 1.2f);
                    Line(c + new Vector2(11, 3), c + new Vector2(6, 3), 1.2f);
                    Line(c + new Vector2(6, 3), c + new Vector2(6, -5), 1.2f);
                    Line(c + new Vector2(6, -5), c + new Vector2(11, -5), 1.2f);
                }
            }
            else if (icon == Icon.Rotate)
            {
                for (int i = 0; i < 28; i++)
                { float a = (i * 10 + 35) * Mathf.Deg2Rad, b = ((i + 1) * 10 + 35) * Mathf.Deg2Rad; Line(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 12, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 12); }
                Line(c + new Vector2(10, 8), c + new Vector2(10, -1)); Line(c + new Vector2(10, 8), c + new Vector2(1, 8));
            }
            else if (icon == Icon.Save)
            {
                Outline(r, 1.5f);
                Outline(new Rect(c.x - 7, c.y + 3, 14, 11), 1.3f);
                Outline(new Rect(c.x - 8, c.y - 14, 16, 11), 1.3f);
                Line(c + new Vector2(4, 5), c + new Vector2(4, 12), 1.5f);
            }
            else if (icon == Icon.Archives)
            {
                Outline(new Rect(c.x - 13, c.y - 13, 26, 20), 1.5f);
                Line(c + new Vector2(-15, 11), c + new Vector2(15, 11), 2);
                Line(c + new Vector2(-12, 15), c + new Vector2(12, 15), 1.4f);
                Line(c + new Vector2(-4, 1), c + new Vector2(4, 1), 1.8f);
            }
            else if (icon == Icon.MouseLeft || icon == Icon.MouseWheel || icon == Icon.MouseMiddle)
            {
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI / 24, b = (i + 1) * Mathf.PI / 24;
                    Line(c + new Vector2(Mathf.Cos(a) * 8, Mathf.Sin(a) * 14), c + new Vector2(Mathf.Cos(b) * 8, Mathf.Sin(b) * 14), 1.2f);
                }
                Line(c + new Vector2(-8, 2), c + new Vector2(8, 2), 1);
                Line(c + new Vector2(0, 2), c + new Vector2(0, 14), 1);
                if (icon == Icon.MouseLeft) Quad(new Rect(c.x - 7, c.y + 3, 6, 7), color);
                else Line(c + new Vector2(0, 5), c + new Vector2(0, 10), icon == Icon.MouseWheel ? 3 : 4);
            }
            else if (icon == Icon.Grid)
            {
                Outline(r, 1.3f);
                for (int i = 1; i < 4; i++)
                { float x = r.xMin + i * 7, y = r.yMin + i * 7; Line(new Vector2(x, r.yMin), new Vector2(x, r.yMax), 1); Line(new Vector2(r.xMin, y), new Vector2(r.xMax, y), 1); }
            }
            else
            {
                Vector2 top = c + new Vector2(0, 16), left = c + new Vector2(-14, 8), right = c + new Vector2(14, 8);
                Vector2 low = c + new Vector2(0, -16), ll = c + new Vector2(-14, -8), rr = c + new Vector2(14, -8);
                Line(top, left); Line(top, right); Line(left, ll); Line(right, rr); Line(ll, low); Line(rr, low);
                Line(left, c); Line(right, c); Line(c, low);
                if (icon == Icon.Front) { Line(left, ll, 3); Line(ll, low, 3); }
                if (icon == Icon.Side) { Line(right, rr, 3); Line(rr, low, 3); }
                if (icon == Icon.Top) { Line(top, left, 3); Line(top, right, 3); }
                if (icon == Icon.Build) { Line(c + new Vector2(-5, 1), c + new Vector2(0, -4), 2.5f); Line(c + new Vector2(0, -4), c + new Vector2(7, 5), 2.5f); }
            }
        }
        private void Outline(Rect r, float width) { Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), width); Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), width); Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), width); Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMin, r.yMin), width); }
        private void Line(Vector2 a, Vector2 b, float width = 1.8f) => UIPearlGraphic.Stroke(mesh, a, b, width, color);
        private void Quad(Rect r, Color tint)
        {
            int v = mesh.currentVertCount;
            mesh.AddVert(new Vector2(r.xMin, r.yMin), tint, Vector2.zero); mesh.AddVert(new Vector2(r.xMin, r.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax, r.yMax), tint, Vector2.zero); mesh.AddVert(new Vector2(r.xMax, r.yMin), tint, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2); mesh.AddTriangle(v, v + 2, v + 3);
        }
        private void Glow(Vector2 center, float radius, Color tint)
        {
            int v = mesh.currentVertCount; mesh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= 64; i++)
            { float a = i * Mathf.PI / 32; mesh.AddVert(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, new Color(tint.r, tint.g, tint.b, 0), Vector2.zero); }
            for (int i = 0; i < 64; i++) mesh.AddTriangle(v, v + i + 1, v + i + 2);
        }
    }
}
