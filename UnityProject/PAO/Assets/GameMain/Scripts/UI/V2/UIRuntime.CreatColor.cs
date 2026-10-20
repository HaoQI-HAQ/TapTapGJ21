using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private void ShowCreatColorMixer(Action updated)
        {
            UIPage page = Push(T("自由调色", "Custom color"), "", false, UIPageDesign.Settings);
            page.Rebuild = () => ShowSandboxColorMixer(updated);
            page.BeforeClose = () => updated?.Invoke();
            UIFluidGradient gradient = page.GetComponent<UIFluidGradient>();
            if (gradient != null) gradient.enabled = false;
            foreach (UIPageArtwork artwork in page.GetComponentsInChildren<UIPageArtwork>(true)) artwork.enabled = false;
            page.GetComponent<Image>().color = Color.white;
            page.transform.Find("Page").gameObject.SetActive(false);

            RectTransform header = ui.Rect("Color atelier header", page.transform);
            Text eyebrow = UICreatStyle.Label(ui, header, "COLOR ATELIER   ///", 14);
            Text heading = UICreatStyle.Label(ui, header, T("自由调色", "Custom color"), 60); heading.fontStyle = FontStyle.Bold;
            heading.verticalOverflow = VerticalWrapMode.Overflow;
            heading.resizeTextForBestFit = true; heading.resizeTextMinSize = 32;
            Text instructions = UICreatStyle.Label(ui, header, T("拖动 RGB 滑杆，或输入 6 位 HEX 色号。", "Use RGB sliders or a 6-digit HEX color."), 22);
            Text explanation = UICreatStyle.Label(ui, header, T("颜色用于建造与染色", "Use this color to build and paint"), 17, true);
            RectTransform workspace = ui.Rect("Color atelier workspace", page.transform);
            RectTransform content = ui.Scroll(workspace);
            content.GetComponent<VerticalLayoutGroup>().enabled = false;
            content.GetComponent<ContentSizeFitter>().enabled = false;
            ScrollRect scroll = content.GetComponentInParent<ScrollRect>();
            UIWidgets.Stretch((RectTransform)scroll.transform);
            scroll.verticalScrollbar.GetComponent<Image>().color = new Color(0, 0, 0, .08f);
            scroll.verticalScrollbar.handleRect.GetComponent<Image>().color = new Color(0, 0, 0, .58f);

            RectTransform previewPanel = ui.Rect("Custom paint preview", content);
            UICreatStyle.Panel(ui, previewPanel);
            UICreatColorCube cube = ui.Rect("Current color isometric cube", previewPanel).gameObject.AddComponent<UICreatColorCube>();
            cube.raycastTarget = false;
            Image sample = ui.Surface("Custom color preview", previewPanel, sandboxBrush.SelectedColor);
            sample.raycastTarget = false;
            Outline sampleOutline = sample.gameObject.AddComponent<Outline>();
            sampleOutline.effectColor = Color.black; sampleOutline.effectDistance = new Vector2(1, -1);
            Text currentLabel = UICreatStyle.Label(ui, previewPanel, T("当前原色", "Current color"), 18, true);
            Text currentHex = UICreatStyle.Label(ui, previewPanel, "", 36); currentHex.fontStyle = FontStyle.Bold;
            currentHex.verticalOverflow = VerticalWrapMode.Overflow;
            Text exactColor = UICreatStyle.Label(ui, previewPanel, T("立体示意，下方色样为准确原色", "The flat swatch shows the exact color."), 13, true);

            RectTransform rgbPanel = ui.Rect("RGB channels", content);
            UICreatStyle.Panel(ui, rgbPanel);
            Slider[] channels = new Slider[3];
            Text[] values = new Text[3];
            RectTransform[] rows = new RectTransform[3];
            RectTransform[] sliderRects = new RectTransform[3];
            Text[] channelNames = new Text[3], minimums = new Text[3], maximums = new Text[3];
            InputField hex = null; Text hint = null;
            bool invalidHex = false;
            string[] names = { T("R 红", "R Red"), T("G 绿", "G Green"), T("B 蓝", "B Blue") };
            Color[] semantic = { new Color32(224, 65, 77, 255), new Color32(62, 174, 82, 255), new Color32(58, 111, 226, 255) };
            Action sync = () =>
            {
                invalidHex = false;
                Color selected = sandboxBrush.SelectedColor;
                sample.color = selected; cube.SelectedColor = selected;
                currentHex.text = "#" + ColorUtility.ToHtmlStringRGB(selected);
                for (int i = 0; i < channels.Length; i++)
                    if (channels[i] != null)
                    {
                        int value = Mathf.RoundToInt(selected[i] * 255);
                        channels[i].SetValueWithoutNotify(value); values[i].text = value.ToString();
                    }
                if (hex != null) hex.SetTextWithoutNotify(currentHex.text);
                if (hint != null) { hint.text = T("支持复制粘贴，按 Enter 应用", "Paste a color; press Enter to apply."); hint.color = UICreatStyle.Muted; }
                updated?.Invoke();
            };
            for (int i = 0; i < channels.Length; i++)
            {
                int channel = i;
                rows[i] = ui.Rect("Color channel " + i, rgbPanel);
                channelNames[i] = UICreatStyle.Label(ui, rows[i], names[i], 28); channelNames[i].fontStyle = FontStyle.Bold;
                values[i] = UICreatStyle.Label(ui, rows[i], "", 30); values[i].fontStyle = FontStyle.Bold;
                values[i].alignment = TextAnchor.MiddleCenter;
                minimums[i] = UICreatStyle.Label(ui, rows[i], "0", 15);
                maximums[i] = UICreatStyle.Label(ui, rows[i], "255", 15); maximums[i].alignment = TextAnchor.MiddleRight;
                RectTransform track = ui.Rect("RGB " + i, rows[i]); sliderRects[i] = track;
                Image hitArea = track.gameObject.AddComponent<Image>(); hitArea.color = new Color(1, 1, 1, .001f);
                Slider slider = track.gameObject.AddComponent<Slider>(); channels[i] = slider;
                slider.minValue = 0; slider.maxValue = 255; slider.wholeNumbers = true;
                Image rail = ui.Surface("Channel track", track, new Color(.9f, .9f, .9f)); rail.raycastTarget = false;
                rail.rectTransform.anchorMin = new Vector2(0, .5f); rail.rectTransform.anchorMax = new Vector2(1, .5f);
                rail.rectTransform.sizeDelta = new Vector2(0, 18); rail.rectTransform.anchoredPosition = Vector2.zero;
                RectTransform fillArea = ui.Rect("Fill area", track); UIWidgets.Stretch(fillArea);
                fillArea.offsetMin = new Vector2(10, 0); fillArea.offsetMax = new Vector2(-10, 0);
                Image fill = ui.Surface("Fill", fillArea, semantic[i]); fill.raycastTarget = false;
                UIWidgets.Stretch(fill.rectTransform); fill.rectTransform.offsetMin = new Vector2(0, 13); fill.rectTransform.offsetMax = new Vector2(0, -13);
                RectTransform handleArea = ui.Rect("Handle area", track); UIWidgets.Stretch(handleArea);
                handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
                Image handle = ui.Surface("Handle", handleArea, Color.black, true);
                handle.rectTransform.anchorMin = new Vector2(0, 0); handle.rectTransform.anchorMax = new Vector2(0, 1);
                handle.rectTransform.sizeDelta = new Vector2(20, -4);
                handle.gameObject.AddComponent<Outline>().effectColor = Color.white;
                slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
                slider.onValueChanged.AddListener(value =>
                {
                    Color selected = sandboxBrush.SelectedColor; selected[channel] = Mathf.Round(value) / 255;
                    sandboxBrush.SelectCustomColor(selected); sync();
                });
                track.gameObject.AddComponent<UIFocusRing>().Configure(Color.black);
                Image rule = ui.Surface("Channel separator", rows[i], new Color(0, 0, 0, .14f)); rule.raycastTarget = false;
                rule.rectTransform.anchorMin = Vector2.zero; rule.rectTransform.anchorMax = Vector2.right;
                rule.rectTransform.pivot = Vector2.zero; rule.rectTransform.sizeDelta = new Vector2(0, 1);
            }

            RectTransform inputRow = ui.Rect("HEX controls", rgbPanel);
            RectTransform fieldRoot = ui.Rect("HEX color", inputRow);
            Image field = UICreatStyle.Panel(ui, fieldRoot);
            field.raycastTarget = true;
            hex = fieldRoot.gameObject.AddComponent<InputField>(); hex.targetGraphic = field;
            Text input = UICreatStyle.Label(ui, fieldRoot, "", 26);
            UIWidgets.Stretch(input.rectTransform); input.rectTransform.offsetMin = new Vector2(18, 6); input.rectTransform.offsetMax = new Vector2(-12, -6);
            input.horizontalOverflow = HorizontalWrapMode.Overflow;
            hex.textComponent = input; hex.characterLimit = 7; hex.lineType = InputField.LineType.SingleLine;
            hex.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            hex.selectionColor = new Color(.5f, .85f, .75f, .5f);
            fieldRoot.gameObject.AddComponent<UIFocusRing>().Configure(Color.black);
            Action applyHex = () =>
            {
                if (sandboxBrush.SelectHexColor(hex.text)) sync();
                else { invalidHex = true; hint.text = T("请输入 6 位 HEX 色号，例如 #FF8800。", "Enter a 6-digit HEX color, e.g. #FF8800."); hint.color = new Color(.72f, .16f, .16f); }
            };
            hex.onEndEdit.AddListener(_ => applyHex());
            Button apply = ui.Button(inputRow, T("应用色号", "Apply HEX"), () => applyHex());
            FitSandboxActionLabel(apply); UICreatStyle.StyleButton(apply);
            hint = UICreatStyle.Label(ui, rgbPanel, "", 16, true);
            Button done = ui.Button(page.transform, T("完成调色   →", "Done   →"), Pop, true);
            done.name = "完成调色";
            FitSandboxActionLabel(done); UICreatStyle.StyleButton(done, true);
            Text doneLabel = done.GetComponentInChildren<Text>();
            doneLabel.GetComponent<UIStyledText>().Configure(30, false, Config);
            doneLabel.fontStyle = FontStyle.Bold;

            UICreatColorLayout layout = page.gameObject.AddComponent<UICreatColorLayout>();
            layout.Root = (RectTransform)page.transform; layout.Header = header; layout.Eyebrow = eyebrow.rectTransform;
            layout.Heading = heading.rectTransform; layout.Instructions = instructions.rectTransform; layout.Explanation = explanation.rectTransform;
            layout.Workspace = workspace; layout.Content = content; layout.PreviewPanel = previewPanel; layout.Cube = cube.rectTransform;
            layout.Sample = sample.rectTransform; layout.CurrentLabel = currentLabel.rectTransform; layout.CurrentHex = currentHex.rectTransform;
            layout.ExactColor = exactColor.rectTransform; layout.RgbPanel = rgbPanel; layout.Rows = rows; layout.SliderRects = sliderRects;
            layout.ChannelNames = channelNames; layout.Values = values; layout.Minimums = minimums; layout.Maximums = maximums;
            layout.InputRow = inputRow; layout.Field = fieldRoot; layout.Apply = (RectTransform)apply.transform;
            layout.Hint = hint.rectTransform; layout.Done = (RectTransform)done.transform;
            page.gameObject.AddComponent<UICreatPageTextLocale>().Configure(() =>
            {
                heading.text = T("自由调色", "Custom color");
                instructions.text = T("拖动 RGB 滑杆，或输入 6 位 HEX 色号。", "Use RGB sliders or a 6-digit HEX color.");
                explanation.text = T("颜色用于建造与染色", "Use this color to build and paint");
                currentLabel.text = T("当前原色", "Current color");
                exactColor.text = T("立体示意，下方色样为准确原色", "The flat swatch shows the exact color.");
                channelNames[0].text = T("R 红", "R Red"); channelNames[1].text = T("G 绿", "G Green"); channelNames[2].text = T("B 蓝", "B Blue");
                apply.GetComponentInChildren<Text>().text = T("应用色号", "Apply HEX");
                doneLabel.text = T("完成调色   →", "Done   →");
                hint.text = invalidHex ? T("请输入 6 位 HEX 色号，例如 #FF8800。", "Enter a 6-digit HEX color, e.g. #FF8800.") :
                    T("支持复制粘贴，按 Enter 应用", "Paste a color; press Enter to apply.");
                layout.Arrange();
            });
            sync(); layout.Arrange();
            page.Show(); page.SetVisible(true); done.Select();
        }
    }

    public sealed class UICreatColorLayout : MonoBehaviour
    {
        public RectTransform Root, Header, Eyebrow, Heading, Instructions, Explanation, Workspace, Content;
        public RectTransform PreviewPanel, Cube, Sample, CurrentLabel, CurrentHex, ExactColor, RgbPanel;
        public RectTransform[] Rows, SliderRects;
        public Text[] ChannelNames, Values, Minimums, Maximums;
        public RectTransform InputRow, Field, Apply, Hint, Done;
        private Vector2 previousSize;
        private float previousScale;
        private void LateUpdate()
        {
            if (Root != null && (Root.rect.size != previousSize || !Mathf.Approximately(previousScale, UISettings.Current.textScale))) Arrange();
        }
        public void Arrange()
        {
            previousSize = Root.rect.size; previousScale = UISettings.Current.textScale;
            float width = Mathf.Max(320, Root.rect.width), height = Mathf.Max(320, Root.rect.height);
            float margin = Mathf.Clamp(width * .03f, 20, 48), inner = width - margin * 2;
            bool narrow = width < 1100 || (width < 1280 && previousScale > 1.1f);
            float titleHeight = Heading.GetComponent<Text>().fontSize * 1.35f;
            Heading.GetComponent<Text>().resizeTextMaxSize = Mathf.RoundToInt(60 * previousScale);
            float headerHeight = narrow ? titleHeight + 116 : Mathf.Max(180, titleHeight + 50);
            Place(Header, 0, 0, width, headerHeight);
            Place(Eyebrow, margin, 8, inner, 28);
            Place(Heading, margin, 33, inner, titleHeight);
            if (narrow)
            {
                Place(Instructions, margin, titleHeight + 38, inner, 34 * previousScale);
                Place(Explanation, margin, titleHeight + 76, inner, 28 * previousScale);
            }
            else
            {
                Place(Heading, margin, 33, inner * .3f, titleHeight);
                Place(Instructions, margin + inner * .32f, 53, inner * .68f, 42 * previousScale);
                Place(Explanation, margin + inner * .32f, 96, inner * .68f, 30 * previousScale);
            }
            float footerHeight = Mathf.Max(104, 80 * previousScale + 24);
            UIWidgets.Stretch(Workspace); Workspace.offsetMin = new Vector2(margin, footerHeight);
            Workspace.offsetMax = new Vector2(-margin, -headerHeight);
            float contentWidth = Mathf.Max(240, inner - 18), gap = 24;
            float minimumRowHeight = Mathf.Max(104, Values[0].fontSize * 3.1f);
            float inputHeight = Mathf.Max(64, 54 * previousScale), hintHeight = Mathf.Max(34, 30 * previousScale);
            float rgbHeight = minimumRowHeight * 3 + inputHeight + hintHeight + 48;
            if (!narrow) rgbHeight = Mathf.Max(rgbHeight, Mathf.Clamp(height - headerHeight - footerHeight - 56, 560, 680));
            float rowHeight = (rgbHeight - inputHeight - hintHeight - 48) / 3;
            float rowInset = (rowHeight - minimumRowHeight) * .5f;
            float leftWidth = narrow ? contentWidth : Mathf.Max(300 * previousScale, contentWidth * .32f);
            float previewHeight = narrow ? 342 * Mathf.Min(previousScale, 1.15f) : rgbHeight;
            Place(PreviewPanel, 0, 0, leftWidth, previewHeight);
            float cubeHeight = previewHeight - 156 * previousScale;
            Place(Cube, 22, 12, leftWidth - 44, cubeHeight);
            float swatch = Mathf.Min(104, 78 * previousScale);
            Place(Sample, 24, cubeHeight + 18, swatch, swatch);
            Place(CurrentLabel, 40 + swatch, cubeHeight + 16, leftWidth - swatch - 64, 30 * previousScale);
            Place(CurrentHex, 40 + swatch, cubeHeight + 50 * previousScale, leftWidth - swatch - 64, 54 * previousScale);
            Place(ExactColor, 24, previewHeight - 36 * previousScale, leftWidth - 48, 30 * previousScale);
            float rgbTop = narrow ? previewHeight + gap : 0;
            float rgbWidth = narrow ? contentWidth : contentWidth - leftWidth - gap;
            Place(RgbPanel, narrow ? 0 : leftWidth + gap, rgbTop, rgbWidth, rgbHeight);
            for (int i = 0; i < 3; i++)
            {
                Place(Rows[i], 24, 12 + i * rowHeight, rgbWidth - 48, rowHeight);
                float available = rgbWidth - 48;
                float labelWidth = Mathf.Max(74, (UISettings.Current.language == 0 ? 62 : 164) * previousScale);
                float valueWidth = Mathf.Max(62, 55 * previousScale);
                float trackWidth = Mathf.Max(60, available - labelWidth - valueWidth - 16);
                Place(ChannelNames[i].rectTransform, 0, rowInset + 10, labelWidth - 6, 40 * previousScale);
                ChannelNames[i].resizeTextForBestFit = true;
                ChannelNames[i].resizeTextMinSize = Mathf.RoundToInt(20 * previousScale);
                ChannelNames[i].resizeTextMaxSize = Mathf.RoundToInt(28 * previousScale);
                ChannelNames[i].verticalOverflow = VerticalWrapMode.Truncate;
                Place(SliderRects[i], labelWidth, rowInset + 11, trackWidth, 44);
                Place(Values[i].rectTransform, labelWidth + trackWidth + 10, rowInset + 8, valueWidth, 48 * previousScale);
                Place(Minimums[i].rectTransform, labelWidth, rowInset + 57, trackWidth * .5f, 28 * previousScale);
                Place(Maximums[i].rectTransform, labelWidth + trackWidth * .5f, rowInset + 57, trackWidth * .5f, 28 * previousScale);
            }
            Place(InputRow, 24, rowHeight * 3 + 18, rgbWidth - 48, inputHeight);
            float applyWidth = Mathf.Min(190, Mathf.Max(136, rgbWidth * .26f));
            Place(Field, 0, 0, rgbWidth - 60 - applyWidth, inputHeight);
            Place(Apply, rgbWidth - 48 - applyWidth, 0, applyWidth, inputHeight);
            Place(Hint, 24, rowHeight * 3 + inputHeight + 23, rgbWidth - 48, hintHeight);
            Content.anchorMin = new Vector2(0, 1); Content.anchorMax = Vector2.one; Content.pivot = new Vector2(.5f, 1);
            Content.sizeDelta = new Vector2(0, narrow ? previewHeight + gap + rgbHeight : Mathf.Max(previewHeight, rgbHeight));
            Done.anchorMin = Done.anchorMax = new Vector2(.5f, 0); Done.pivot = new Vector2(.5f, 0);
            Done.anchoredPosition = new Vector2(0, 18); Done.sizeDelta = new Vector2(Mathf.Min(680, inner), footerHeight - 36);
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
    }

    // The cube suggests volume; the separate flat swatch retains the exact selected RGB value.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UICreatColorCube : MaskableGraphic
    {
        private Color selected = Color.white;
        public Color SelectedColor { get => selected; set { selected = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect rect = rectTransform.rect;
            float unit = Mathf.Min(rect.width * .4f, rect.height * .42f);
            Vector2 center = rect.center;
            Color grid = new Color(0, 0, 0, .075f);
            for (int i = -4; i <= 4; i++)
            {
                float shift = i * unit / 8;
                Stroke(mesh, center + new Vector2(-unit * 1.2f, shift - unit * .6f), center + new Vector2(unit * 1.2f, shift + unit * .6f), .7f, grid);
                Stroke(mesh, center + new Vector2(-unit * 1.2f, shift + unit * .6f), center + new Vector2(unit * 1.2f, shift - unit * .6f), .7f, grid);
            }
            Vector2 top = center + new Vector2(0, unit), left = center + new Vector2(-unit * .87f, unit * .5f);
            Vector2 right = center + new Vector2(unit * .87f, unit * .5f), middle = center;
            Vector2 lowerLeft = center + new Vector2(-unit * .87f, -unit * .5f), lowerRight = center + new Vector2(unit * .87f, -unit * .5f);
            Vector2 bottom = center + new Vector2(0, -unit);
            Face(mesh, top, right, middle, left, Color.Lerp(selected, Color.white, .10f));
            Face(mesh, left, middle, bottom, lowerLeft, Color.Lerp(selected, Color.black, .16f));
            Face(mesh, middle, right, lowerRight, bottom, Color.Lerp(selected, Color.black, .06f));
            Vector2[] outline = { top, right, lowerRight, bottom, lowerLeft, left, top };
            for (int i = 0; i < outline.Length - 1; i++) Stroke(mesh, outline[i], outline[i + 1], 1.6f, Color.black);
            Stroke(mesh, left, middle, 1, Color.black); Stroke(mesh, right, middle, 1, Color.black); Stroke(mesh, middle, bottom, 1, Color.black);
        }
        private static void Face(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
