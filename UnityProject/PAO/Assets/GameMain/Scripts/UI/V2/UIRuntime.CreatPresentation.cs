using PAO.Sandbox;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private RectTransform creatTitle, creatCapacity, creatEntries, creatTools, creatBrush, creatChargeTrack;
        private Text creatHeading, creatSubtitle, creatDimensions, creatBrushHint, creatSelectionHint;
        private Button creatPause, creatSave, creatUndo;
        private readonly Button[] creatToolsButtons = new Button[4];
        private readonly Button[] creatEntryButtons = new Button[3];
        private readonly UICreatToolGlyph[] creatToolGlyphs = new UICreatToolGlyph[3];

        private void BuildCreatHud()
        {
            sandboxBrush = FindFirstObjectByType<SandboxBrush>();
            hud = ui.Rect("Creat sandbox HUD", safeRoot); UIWidgets.Stretch(hud);
            creatTitle = CreatPanel("Sandbox title", 0);
            creatHeading = CreatText(creatTitle, "泡泡造物场", 48, new Vector2(24, -8), new Vector2(430, 72));
            creatHeading.fontStyle = FontStyle.Bold;
            creatHeading.verticalOverflow = VerticalWrapMode.Overflow;
            creatSubtitle = CreatText(creatTitle, "CREAT  /  BUILD MODE", 15, new Vector2(26, -82), new Vector2(256, 26));
            creatSubtitle.fontStyle = FontStyle.Bold;
            creatCapacity = CreatPanel("World capacity", 1);
            sandboxStats = CreatText(creatCapacity, "", 32, new Vector2(24, -26), new Vector2(245, 48));
            sandboxStats.fontStyle = FontStyle.Bold;
            creatDimensions = CreatText(creatCapacity, "", 16, new Vector2(270, -32), new Vector2(170, 36));
            creatPause = CreatButton(creatCapacity, "Ⅱ", ShowSandboxPause, true);
            creatPause.name = "Pause creation";
            creatEntries = ui.Rect("Creation quick actions", hud);
            creatEntryButtons[1] = CreatButton(creatEntries, "F6  作品预览", ShowSandboxPreview);
            creatEntryButtons[1].name = "Open creation preview";

            RectTransform crosshair = ui.Rect("Center crosshair", hud);
            Anchor(crosshair, Vector2.one * .5f, Vector2.zero, new Vector2(30, 30), Vector2.one * .5f);
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                Image arm = ui.Surface("Crosshair arm " + i, crosshair, UICreatStyle.Paper);
                arm.raycastTarget = false;
                Anchor(arm.rectTransform, Vector2.one * .5f,
                    horizontal ? new Vector2(i == 0 ? -7 : 7, 0) : new Vector2(0, i == 2 ? -7 : 7),
                    horizontal ? new Vector2(7, 2) : new Vector2(2, 7), Vector2.one * .5f);
                arm.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;
                sandboxCrosshairArms[i] = arm;
            }
            sandboxTarget = ui.Label(hud, "", 17, UICreatStyle.Paper, TextAnchor.MiddleCenter);
            Anchor(sandboxTarget.rectTransform, Vector2.one * .5f, new Vector2(0, 60), new Vector2(650, 58), Vector2.one * .5f);
            sandboxTarget.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;

            creatTools = ui.Rect("Bubble tool belt", hud);
            SandboxTool[] modes = { SandboxTool.Build, SandboxTool.Destroy, SandboxTool.Select };
            string[] names = { "1  建造", "2  破坏", "3  选中" };
            for (int i = 0; i < modes.Length; i++)
            {
                SandboxTool mode = modes[i];
                Button button = creatToolsButtons[i] = CreatButton(creatTools, names[i], () => sandboxBrush.SelectTool(mode));
                button.name = "Sandbox tool " + mode;
                Text label = button.GetComponentInChildren<Text>();
                label.GetComponent<UIStyledText>().Configure(28, false, Config); label.fontStyle = FontStyle.Bold;
                label.rectTransform.offsetMin = new Vector2(66, 6); label.alignment = TextAnchor.MiddleCenter;
                RectTransform icon = ui.Rect("Tool symbol", button.transform);
                Anchor(icon, new Vector2(0, .5f), new Vector2(20, 0), new Vector2(44, 44), new Vector2(0, .5f));
                creatToolGlyphs[i] = icon.gameObject.AddComponent<UICreatToolGlyph>();
                creatToolGlyphs[i].Kind = i; creatToolGlyphs[i].color = UICreatStyle.Ink; creatToolGlyphs[i].raycastTarget = false;
            }
            sandboxTool = ui.Label(hud, "", 17, UICreatStyle.Paper);
            sandboxTool.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;
            sandboxStatus = ui.Label(hud, "", 15, UICreatStyle.Paper, TextAnchor.MiddleRight);
            sandboxStatus.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;
            Image track = ui.Surface("Charge track", hud, new Color(1, 1, 1, .28f));
            track.raycastTarget = false; creatChargeTrack = track.rectTransform;
            sandboxCharge = ui.Surface("Charge", track.transform, UICreatStyle.Accent); sandboxCharge.raycastTarget = false;
            sandboxCharge.rectTransform.anchorMin = Vector2.zero;
            sandboxCharge.rectTransform.anchorMax = new Vector2(0, 1);
            sandboxCharge.rectTransform.offsetMin = sandboxCharge.rectTransform.offsetMax = Vector2.zero;

            creatBrush = CreatPanel("Paint brush", 2);
            sandboxColor = ui.Surface("Selected paint", creatBrush, BubbleSandbox.Palette[0]); sandboxColor.raycastTarget = false;
            Anchor(sandboxColor.rectTransform, Vector2.up, new Vector2(20, -22), new Vector2(42, 42), Vector2.up);
            sandboxColor.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;
            sandboxBrushInfo = CreatText(creatBrush, "", 23, new Vector2(80, -20), new Vector2(340, 48));
            sandboxBrushInfo.fontStyle = FontStyle.Bold;
            creatToolsButtons[3] = CreatButton(hud, "B  调色", ShowSandboxWorkshop);
            creatSave = CreatButton(hud, "F5  保存", () => { if (BubbleSandbox.Instance != null) BubbleSandbox.Instance.SaveWorld(); });
            creatUndo = CreatButton(hud, "Ctrl+Z  撤销", () => { if (BubbleSandbox.Instance != null) BubbleSandbox.Instance.Undo(); });
            creatBrushHint = ui.Label(hud, "C/V 换色  ·  X 大小  ·  G 形状", 14, UICreatStyle.Paper, TextAnchor.MiddleRight);
            creatBrushHint.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;

            sandboxSelectionHud = ui.Rect("Selection HUD", hud);
            sandboxSelectionInfo = ui.Label(sandboxSelectionHud, "", 16, UICreatStyle.Paper);
            sandboxSelectionInfo.gameObject.AddComponent<Outline>().effectColor = UICreatStyle.Ink;
            creatSelectionHint = CreatText(sandboxSelectionHud, "点击添加或取消选择", 14, new Vector2(18, -40), new Vector2(300, 26), true);
            sandboxSelectionUp = CreatButton(sandboxSelectionHud, "Q  上浮", () => sandboxBrush.MoveVertical(1));
            sandboxSelectionDown = CreatButton(sandboxSelectionHud, "E  下浮", () => sandboxBrush.MoveVertical(-1));
            sandboxSelectionClear = CreatButton(sandboxSelectionHud, "R  清空", () => sandboxBrush.ClearSelection());
            sandboxSelectionUp.name = "Selection action 0"; sandboxSelectionDown.name = "Selection action 1"; sandboxSelectionClear.name = "Selection action 2";
            var motion = hud.gameObject.AddComponent<UICreatHudMotion>();
            motion.Brush = sandboxBrush; motion.Charge = sandboxCharge; motion.Status = sandboxStatus;
            LayoutCreatHud(); RefreshSandboxHud();
        }

        private RectTransform CreatPanel(string name, int art = -1)
        {
            RectTransform panel = ui.Rect(name, hud); UICreatStyle.Panel(ui, panel, art < 0);
            if (art >= 0)
            {
                RectTransform drawing = ui.Rect("Printed drafting marks", panel); UIWidgets.Stretch(drawing, 6);
                UICreatHudPrint print = drawing.gameObject.AddComponent<UICreatHudPrint>(); print.Kind = art;
                print.color = UICreatStyle.Ink; print.raycastTarget = false;
            }
            return panel;
        }
        private Text CreatText(Transform parent, string value, int size, Vector2 position, Vector2 extent, bool muted = false)
        {
            Text label = UICreatStyle.Label(ui, parent, value, size, muted);
            Anchor(label.rectTransform, Vector2.up, position, extent, Vector2.up); return label;
        }
        private Button CreatButton(Transform parent, string label, System.Action action, bool primary = false)
        {
            Button button = ui.Button(parent, label, action); FitSandboxActionLabel(button);
            UICreatStyle.StyleButton(button, primary); return button;
        }
        private void LayoutCreatHud()
        {
            if (hud == null || safeRoot == null || creatTitle == null) return;
            UIWidgets.Stretch(hud); hud.localScale = Vector3.one;
            float w = safeRoot.rect.width, scale = UISettings.Current.textScale;
            float margin = 18, available = w - margin * 2;
            bool compact = w < 1180;
            float rowHeight = scale > 1.15f ? 54 : 46;
            float titleWidth = compact ? 172 : 196;
            float meterWidth = compact ? Mathf.Min(278, available * .48f) : 310;
            Anchor(creatTitle, Vector2.up, new Vector2(margin, -18), new Vector2(titleWidth, 44), Vector2.up);
            creatHeading.GetComponent<UIStyledText>().Configure(20, false, Config);
            Anchor(creatHeading.rectTransform, Vector2.up, new Vector2(12, -2), new Vector2(titleWidth - 24, 40), Vector2.up);
            creatSubtitle.gameObject.SetActive(false);
            creatTitle.Find("Printed drafting marks").gameObject.SetActive(false);
            Anchor(creatCapacity, Vector2.one, new Vector2(-margin, -18), new Vector2(meterWidth, 44), Vector2.one);
            creatCapacity.Find("Printed drafting marks").gameObject.SetActive(false);
            creatDimensions.gameObject.SetActive(false);
            sandboxStats.GetComponent<UIStyledText>().Configure(18, false, Config);
            Anchor(sandboxStats.rectTransform, Vector2.up, new Vector2(12, -2), new Vector2(meterWidth - 66, 40), Vector2.up);
            Anchor((RectTransform)creatPause.transform, Vector2.one, new Vector2(-6, -4), new Vector2(36, 36), Vector2.one);
            Text pauseLabel = creatPause.GetComponentInChildren<Text>();
            pauseLabel.GetComponent<UIStyledText>().Configure(18, false, Config);
            pauseLabel.rectTransform.offsetMin = new Vector2(2, 2);
            pauseLabel.rectTransform.offsetMax = new Vector2(-2, -2);
            pauseLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            float previewWidth = compact ? 156 : 172;
            Anchor(creatEntries, Vector2.up, new Vector2(margin + (compact ? 0 : titleWidth + 8), compact ? -70 : -18),
                new Vector2(previewWidth, 40), Vector2.up);
            UIWidgets.Stretch((RectTransform)creatEntryButtons[1].transform);
            creatEntryButtons[1].GetComponentInChildren<Text>().GetComponent<UIStyledText>().Configure(15, false, Config);

            // One narrow strip on wide displays; two edge rows when there is not enough width.
            float toolWidth = compact ? available * .56f : Mathf.Min(420 * Mathf.Min(scale, 1.1f), available * .36f);
            float brushWidth = compact ? available - toolWidth - 10 : Mathf.Min(300, available * .25f);
            float actionWidth = compact ? Mathf.Min(390, available) : Mathf.Min(366, available - toolWidth - brushWidth - 24);
            float rowY = compact ? 82 : 34;
            Anchor(creatTools, Vector2.zero, new Vector2(margin, rowY), new Vector2(toolWidth, rowHeight), Vector2.zero);
            float cell = (toolWidth - 12) / 3;
            for (int i = 0; i < 3; i++)
            {
                Button button = creatToolsButtons[i];
                Anchor((RectTransform)button.transform, Vector2.zero, new Vector2(i * (cell + 6), 0), new Vector2(cell, rowHeight), Vector2.zero);
                Text label = button.GetComponentInChildren<Text>();
                label.GetComponent<UIStyledText>().Configure(compact ? 16 : 18, false, Config);
                label.rectTransform.offsetMin = new Vector2(compact ? 30 : 38, 2);
                label.rectTransform.offsetMax = new Vector2(-5, -2);
                RectTransform glyph = creatToolGlyphs[i].rectTransform;
                Anchor(glyph, new Vector2(0, .5f), new Vector2(8, 0), Vector2.one * (compact ? 22 : 26), new Vector2(0, .5f));
            }
            float brushX = compact ? w - margin - brushWidth : margin + toolWidth + 10;
            Anchor(creatBrush, Vector2.zero, new Vector2(brushX, rowY), new Vector2(brushWidth, rowHeight), Vector2.zero);
            creatBrush.Find("Printed drafting marks").gameObject.SetActive(false);
            Anchor(sandboxColor.rectTransform, Vector2.up, new Vector2(10, -(rowHeight - 26) * .5f), new Vector2(26, 26), Vector2.up);
            Anchor(sandboxBrushInfo.rectTransform, Vector2.up, new Vector2(46, -2), new Vector2(brushWidth - 56, rowHeight - 4), Vector2.up);
            sandboxBrushInfo.GetComponent<UIStyledText>().Configure(compact ? 14 : 16, false, Config);
            bool selecting = sandboxBrush.Tool == SandboxTool.Select;
            creatBrush.gameObject.SetActive(!selecting);
            Button[] actions = { creatToolsButtons[3], creatSave, creatUndo };
            float actionCell = (actionWidth - 12) / 3;
            float actionX = w - margin - actionWidth;
            float actionY = compact ? 28 : rowY;
            float actionOffset = 0;
            for (int i = 0; i < actions.Length; i++)
            {
                float width = actionCell + (i == 2 ? 20 : -10);
                Anchor((RectTransform)actions[i].transform, Vector2.zero, new Vector2(actionX + actionOffset, actionY), new Vector2(width, 42), Vector2.zero);
                actionOffset += width + 6;
                actions[i].GetComponentInChildren<Text>().GetComponent<UIStyledText>().Configure(15, false, Config);
                actions[i].gameObject.SetActive(!selecting);
            }
            creatBrushHint.gameObject.SetActive(false);
            Anchor(sandboxSelectionHud, Vector2.zero, Vector2.zero, new Vector2(w, 1), Vector2.zero);
            Anchor(sandboxSelectionInfo.rectTransform, Vector2.zero, new Vector2(brushX, rowY), new Vector2(brushWidth, rowHeight), Vector2.zero);
            sandboxSelectionInfo.GetComponent<UIStyledText>().Configure(compact ? 14 : 16, false, Config);
            creatSelectionHint.gameObject.SetActive(false);
            Button[] selectionActions = { sandboxSelectionUp, sandboxSelectionDown, sandboxSelectionClear };
            for (int i = 0; i < selectionActions.Length; i++)
            {
                Anchor((RectTransform)selectionActions[i].transform, Vector2.zero,
                    new Vector2(actionX + i * (actionCell + 6), actionY), new Vector2(actionCell, 42), Vector2.zero);
                selectionActions[i].GetComponentInChildren<Text>().GetComponent<UIStyledText>().Configure(15, false, Config);
            }
            Anchor(sandboxTool.rectTransform, Vector2.zero, new Vector2(margin, 2), new Vector2(available, 24), Vector2.zero);
            sandboxTool.GetComponent<UIStyledText>().Configure(13, false, Config);
            Anchor(creatChargeTrack, Vector2.zero, new Vector2(margin, rowY - 5), new Vector2(toolWidth, 2), Vector2.zero);
            Anchor(sandboxStatus.rectTransform, Vector2.one, new Vector2(-margin, -68), new Vector2(Mathf.Min(520, available), 28), Vector2.one);
            sandboxStatus.GetComponent<UIStyledText>().Configure(13, false, Config);
            sandboxTarget.rectTransform.anchoredPosition = new Vector2(0, 38);
            sandboxTarget.rectTransform.sizeDelta = new Vector2(Mathf.Min(540, available), 28);
            sandboxTarget.GetComponent<UIStyledText>().Configure(14, false, Config);
        }
        private static void RectWidth(RectTransform rect, float width)
        { if (rect != null) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1, width)); }
        private void RefreshCreatToolSelection()
        {
            if (sandboxBrush == null || BubbleSandbox.Instance == null) return;
            SandboxTool[] modes = { SandboxTool.Build, SandboxTool.Destroy, SandboxTool.Select };
            for (int i = 0; i < 3; i++)
            {
                bool chosen = sandboxBrush.Tool == modes[i]; UICreatStyle.SetSelected(creatToolsButtons[i], chosen);
            }
            BubbleSandbox world = BubbleSandbox.Instance;
            Vector3 size = (Vector3)world.dimensions * world.cellSize;
            sandboxStats.text = world.Count + " / " + world.Capacity;
            sandboxBrushInfo.text = sandboxBrush.ColorName + " · " + (sandboxBrush.Shape == SandboxShape.Bubble ? "圆泡" : "方泡") + " · " + sandboxBrush.Diameter + "格";
            sandboxTool.text = sandboxBrush.Tool == SandboxTool.Select ? "点击选择  ·  Q/E 上下浮  ·  R 清空" :
                (UIInput.ToggleCharge ? "点击 " + UIInput.Prompt(UIInput.Charge) + " 蓄力，再次点击吹出" : "按住 " + UIInput.Prompt(UIInput.Charge) + " 蓄力，松开吹出") + (sandboxBrush.Paint ? "  /  染色" : "");
            creatUndo.interactable = world.CanUndo;
            int selected = sandboxBrush.Selection != null ? sandboxBrush.Selection.Count : 0;
            sandboxSelectionInfo.text = selected > 0 ? "已选中 " + selected : "点击造物选择";
            sandboxTarget.gameObject.SetActive((sandboxBrush.HasTarget && !sandboxBrush.CanPlaceTarget) ||
                (sandboxBrush.Tool == SandboxTool.Select && sandboxBrush.HoveredPiece != null));
        }

        private void FinishCreatPage(UIPage page)
        {
            UIFluidGradient background = page.GetComponent<UIFluidGradient>();
            if (background != null) background.enabled = false;
            page.GetComponent<Image>().color = UICreatStyle.Paper;
            foreach (UIPageArtwork art in page.GetComponentsInChildren<UIPageArtwork>(true)) art.enabled = false;
            foreach (Image image in page.GetComponentsInChildren<Image>(true))
                if (image.GetComponent<VerticalLayoutGroup>() != null)
                {
                    image.color = UICreatStyle.PanelColor;
                    Outline outline = image.GetComponent<Outline>();
                    if (outline != null) outline.effectColor = new Color(.54f, .79f, .77f, .28f);
                }
            foreach (Text label in page.GetComponentsInChildren<Text>(true))
            {
                bool palette = false;
                for (Transform ancestor = label.transform.parent; ancestor != null && ancestor != page.transform; ancestor = ancestor.parent)
                    if (ancestor.name.StartsWith("Palette ")) { palette = true; break; }
                if (palette || label.GetComponentInParent<InputField>() != null) continue;
                UIStyledText styled = label.GetComponent<UIStyledText>();
                if (styled != null) styled.Configure(Mathf.RoundToInt(label.fontSize / Mathf.Max(.5f, UISettings.Current.textScale)), false, Config);
                label.color = UICreatStyle.Ink;
            }
            foreach (Button button in page.GetComponentsInChildren<Button>(true))
            {
                if (button.name.StartsWith("Palette ")) continue;
                bool primary = button.transform.parent == page.Footer && button.transform.GetSiblingIndex() == page.Footer.childCount - 1;
                UICreatStyle.StyleButton(button, primary);
            }
            Finish(page);
        }
    }

    // The progress reads the live brush each frame. It is independent of the 10 Hz text refresh.
    public sealed class UICreatHudMotion : MonoBehaviour
    {
        public SandboxBrush Brush;
        public Image Charge;
        public Text Status;
        private CanvasGroup group;
        private float entered;
        private string lastStatus;
        private float statusRemaining;
        private void OnEnable()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            entered = 0;
            group.alpha = UISettings.Current != null && UISettings.Current.reducedMotion ? 1 : 0;
        }
        private void Update()
        {
            entered += Time.unscaledDeltaTime;
            group.alpha = UISettings.Current.reducedMotion ? 1 : Mathf.SmoothStep(0, 1, entered / .2f);
            if (Brush != null && Charge != null)
            {
                Charge.rectTransform.anchorMax = new Vector2(Brush.IsCharging ? Brush.ChargeProgress : 0, 1);
                Charge.transform.parent.gameObject.SetActive(Brush.IsCharging);
            }
            if (Status != null)
            {
                if (lastStatus != Status.text)
                {
                    lastStatus = Status.text;
                    statusRemaining = string.IsNullOrEmpty(lastStatus) || lastStatus == "吹一颗泡泡，让想象落地。" ? 0 : 4;
                }
                statusRemaining -= Time.unscaledDeltaTime;
                Status.gameObject.SetActive(statusRemaining > 0);
            }
        }
        private void OnDisable() { if (group != null) group.alpha = 1; }
    }
}
