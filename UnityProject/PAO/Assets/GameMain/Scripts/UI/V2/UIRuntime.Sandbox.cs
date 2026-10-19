using PAO.Sandbox;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private Text sandboxStats, sandboxStatus, sandboxTool, sandboxBrushInfo, sandboxTarget;
        private readonly Image[] sandboxToolMarks = new Image[5];
        private readonly Image[] sandboxCrosshairArms = new Image[4];
        private RectTransform sandboxSelectionHud;
        private Text sandboxSelectionInfo;
        private Button sandboxSelectionUp, sandboxSelectionDown, sandboxSelectionClear;
        private Image sandboxColor, sandboxCharge;
        private SandboxBrush sandboxBrush;

        private void BuildSandboxHud()
        {
            sandboxBrush = FindFirstObjectByType<SandboxBrush>();
            hud = ui.Rect("Creat sandbox HUD", safeRoot); UIWidgets.Stretch(hud);
            FitSandboxHud();
            RectTransform title = ui.Rect("Sandbox title", hud);
            Anchor(title, Vector2.up, new Vector2(28, -24), new Vector2(500, 112), Vector2.up);
            AddHudBacking(title);
            SandboxLabel(title, "CREAT  /  泡泡造物场", 28, new Vector2(18, -14), new Vector2(462, 46));
            SandboxLabel(title, "无限泡泡 · 自由创造 · F6 作品预览", 19, new Vector2(18, -64), new Vector2(462, 30));
            Button previewEntry = ui.Button(hud, "F6  作品预览", ShowSandboxPreview);
            previewEntry.name = "Open creation preview";
            Anchor((RectTransform)previewEntry.transform, new Vector2(.5f, 1), new Vector2(0, -40), new Vector2(230, 56), new Vector2(.5f, 1));
            FitSandboxActionLabel(previewEntry);

            RectTransform stats = ui.Rect("World capacity", hud);
            Anchor(stats, Vector2.one, new Vector2(-28, -24), new Vector2(430, 112), Vector2.one);
            AddHudBacking(stats);
            sandboxStats = SandboxLabel(stats, "", 22, new Vector2(18, -12), new Vector2(394, 38));
            SandboxLabel(stats, "B 调色工具台    F5 保存    Esc 暂停", 18, new Vector2(18, -63), new Vector2(394, 32));

            RectTransform crosshair = ui.Rect("Center crosshair", hud);
            Anchor(crosshair, new Vector2(.5f, .5f), Vector2.zero, new Vector2(38, 38), new Vector2(.5f, .5f));
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                Image arm = ui.Surface("Crosshair arm " + i, crosshair, Config.paper);
                arm.raycastTarget = false;
                Anchor(arm.rectTransform, new Vector2(.5f, .5f),
                    horizontal ? new Vector2(i == 0 ? -10 : 10, 0) : new Vector2(0, i == 2 ? -10 : 10),
                    horizontal ? new Vector2(11, 2) : new Vector2(2, 11), new Vector2(.5f, .5f));
                arm.gameObject.AddComponent<Outline>().effectColor = Config.ink;
                sandboxCrosshairArms[i] = arm;
            }
            sandboxTarget = ui.Label(hud, "", 18, Config.paper, TextAnchor.MiddleCenter);
            Anchor(sandboxTarget.rectTransform, new Vector2(.5f, .5f), new Vector2(0, -42), new Vector2(620, 34), new Vector2(.5f, .5f));
            sandboxTarget.gameObject.AddComponent<Outline>().effectColor = Config.ink;

            RectTransform tools = ui.Rect("Bubble tool belt", hud);
            Anchor(tools, Vector2.zero, new Vector2(28, 24), new Vector2(1020, 180), Vector2.zero);
            AddHudBacking(tools);
            sandboxTool = SandboxLabel(tools, "", 25, new Vector2(18, -10), new Vector2(980, 38));
            SandboxTool[] modes = { SandboxTool.Build, SandboxTool.Destroy, SandboxTool.Select };
            string[] names = { "1  建造", "2  破坏", "3  选中" };
            for (int i = 0; i < modes.Length; i++)
            {
                SandboxTool mode = modes[i];
                Button button = ui.Button(tools, names[i], () => sandboxBrush.SelectTool(mode));
                button.name = "Sandbox tool " + mode;
                Anchor((RectTransform)button.transform, Vector2.up, new Vector2(18 + i * 228, -57), new Vector2(212, 56), Vector2.up);
                FitSandboxActionLabel(button);
                Image mark = ui.Surface("Selected tool", button.transform, Config.teal);
                mark.raycastTarget = false;
                Anchor(mark.rectTransform, Vector2.zero, new Vector2(12, 4), new Vector2(188, 3), Vector2.zero);
                sandboxToolMarks[(int)mode] = mark;
            }
            Button workshop = ui.Button(tools, "B  调色工具台", ShowSandboxWorkshop);
            Anchor((RectTransform)workshop.transform, Vector2.up, new Vector2(702, -57), new Vector2(298, 56), Vector2.up);
            FitSandboxActionLabel(workshop);
            sandboxStatus = SandboxLabel(tools, "", 18, new Vector2(18, -129), new Vector2(982, 34));
            Image track = ui.Surface("Charge track", tools, new Color(Config.ink.r, Config.ink.g, Config.ink.b, .15f));
            Anchor(track.rectTransform, Vector2.zero, new Vector2(18, 9), new Vector2(982, 4), Vector2.zero);
            sandboxCharge = ui.Surface("Charge", track.transform, Config.teal);
            sandboxCharge.rectTransform.anchorMin = Vector2.zero; sandboxCharge.rectTransform.anchorMax = Vector2.zero;
            sandboxCharge.rectTransform.offsetMin = sandboxCharge.rectTransform.offsetMax = Vector2.zero;

            sandboxSelectionHud = ui.Rect("Selection HUD", hud);
            Anchor(sandboxSelectionHud, Vector2.zero, new Vector2(28, 220), new Vector2(1020, 100), Vector2.zero);
            AddHudBacking(sandboxSelectionHud);
            sandboxSelectionInfo = SandboxLabel(sandboxSelectionHud, "", 22, new Vector2(18, -9), new Vector2(426, 38));
            SandboxLabel(sandboxSelectionHud, "3 选中模式 · 点击添加 / 再次点击取消", 17,
                new Vector2(18, -53), new Vector2(450, 30));
            sandboxSelectionUp = ui.Button(sandboxSelectionHud, "Q  上浮", () => sandboxBrush.MoveVertical(1));
            sandboxSelectionDown = ui.Button(sandboxSelectionHud, "E  下浮", () => sandboxBrush.MoveVertical(-1));
            sandboxSelectionClear = ui.Button(sandboxSelectionHud, "R  清空选择", () => sandboxBrush.ClearSelection());
            Button[] actions = { sandboxSelectionUp, sandboxSelectionDown, sandboxSelectionClear };
            for (int i = 0; i < actions.Length; i++)
            {
                actions[i].name = "Selection action " + i;
                Anchor((RectTransform)actions[i].transform, Vector2.up, new Vector2(480 + i * 174, -23), new Vector2(162, 54), Vector2.up);
                FitSandboxActionLabel(actions[i]);
            }

            RectTransform brush = ui.Rect("Paint brush", hud);
            Anchor(brush, Vector2.right, new Vector2(-28, 24), new Vector2(490, 180), Vector2.right);
            AddHudBacking(brush);
            sandboxColor = ui.Surface("Selected paint", brush, BubbleSandbox.Palette[0], true);
            Anchor(sandboxColor.rectTransform, Vector2.up, new Vector2(18, -18), new Vector2(38, 38), Vector2.up);
            sandboxBrushInfo = SandboxLabel(brush, "", 21, new Vector2(72, -14), new Vector2(400, 82));
            SandboxLabel(brush, "Q/E 上下浮 · 1/2/3 工具 · C/V 换色\nX 大小 · G 形状 · P 染色 · T 连吹 · Ctrl+Z/Y 撤销", 16,
                new Vector2(18, -100), new Vector2(456, 66));
            RefreshSandboxHud();
        }
        private Text SandboxLabel(Transform parent, string value, int size, Vector2 position, Vector2 extent)
        {
            Text label = ui.Label(parent, value, size);
            Anchor(label.rectTransform, Vector2.up, position, extent, Vector2.up); return label;
        }
        private void FitSandboxActionLabel(Button button)
        {
            Text label = button.GetComponentInChildren<Text>();
            label.GetComponent<UIStyledText>().Configure(18, false, Config);
            label.rectTransform.offsetMin = new Vector2(8, 2);
            label.rectTransform.offsetMax = new Vector2(-8, -2);
        }
        private void RefreshSandboxHud()
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || sandboxStats == null || sandboxBrush == null || switcher == null) return;
            FitSandboxHud();
            Vector3 size = (Vector3)world.dimensions * world.cellSize;
            sandboxStats.text = world.Count + " / " + world.Capacity + " 格    " + size.x + " × " + size.y + " × " + size.z + " m";
            string input = UIInput.Prompt(UIInput.Charge);
            bool selecting = sandboxBrush.Tool == SandboxTool.Select;
            int selected = sandboxBrush.Selection != null ? sandboxBrush.Selection.Count : 0;
            bool destroyingSelection = sandboxBrush.Tool == SandboxTool.Destroy && selected > 0;
            string toolName = destroyingSelection ? "批量破坏" : sandboxBrush.ToolName + "泡泡";
            sandboxTool.text = selecting ? "选中模式   /   " + input + " 添加或取消选择" : toolName + "   /   " + (sandboxBrush.Continuous ?
                (destroyingSelection ? input + " 吹出整批破坏泡泡" : "按住 " + input + " 连续吹泡泡") :
                UIInput.ToggleCharge ? input + " 点击蓄力，再次点击吹出" : "按住 " + input + " 蓄力，松开吹出");
            sandboxStatus.text = world.Status;
            sandboxColor.color = sandboxBrush.SelectedColor;
            sandboxBrushInfo.text = sandboxBrush.ColorName + "  ·  " + (sandboxBrush.Shape == SandboxShape.Bubble ? "圆泡" : "方泡") +
                "  ·  " + sandboxBrush.Diameter + " 格\n" + (sandboxBrush.Paint ? "染色笔刷：只改变已有造物的颜色" : UIInput.Prompt(UIInput.Detonate) + "快放 · Alt 瞄准脚下");
            sandboxTarget.text = destroyingSelection ? "将拆除已选中的 " + selected + " 件造物 · Ctrl+Z 可整批恢复" :
                selecting ? (sandboxBrush.HoveredPiece != null ?
                (sandboxBrush.Selection.Contains(sandboxBrush.HoveredPiece) ? "再次点击取消选中" : "点击选中造物") : "将十字准心对准造物") :
                selected > 0 && (sandboxBrush.Tool == SandboxTool.Lift || sandboxBrush.Tool == SandboxTool.Lower) ? "将移动已选中的 " + selected + " 件造物" :
                sandboxBrush.HasTarget ? "[ " + sandboxBrush.TargetCell.x + ", " + sandboxBrush.TargetCell.y + ", " + sandboxBrush.TargetCell.z + " ]  " +
                (!sandboxBrush.CanPlaceTarget ? "该格被占用或碰到玩家 · 跳起后再放置" :
                    sandboxBrush.Tool == SandboxTool.Lift ? "相连造物整体上浮" : sandboxBrush.Tool == SandboxTool.Lower ? "相连造物整体下浮" :
                    sandboxBrush.Tool == SandboxTool.Destroy ? "拆除方框内全部造物" :
                    sandboxBrush.TargetingUnderfoot ? "脚下放置 · 跳起后吹泡泡搭高" : "贴着瞄准面的相邻格放置") : "瞄准场地或造物 · Home 返回入口";
            for (int i = 0; i < sandboxToolMarks.Length; i++) if (sandboxToolMarks[i] != null) sandboxToolMarks[i].enabled = i == (int)sandboxBrush.Tool;
            sandboxSelectionHud.gameObject.SetActive(selecting);
            sandboxSelectionInfo.text = selected > 0 ? "已选中  " + selected + " 件造物  ·  红框标记" : "上下浮  /  准心对准造物即可操作";
            sandboxSelectionUp.interactable = sandboxSelectionDown.interactable = selected > 0 || sandboxBrush.HoveredPiece != null;
            sandboxSelectionClear.interactable = selected > 0;
            foreach (Image arm in sandboxCrosshairArms)
                if (arm != null) arm.color = (selecting && sandboxBrush.HoveredPiece != null) ||
                    (sandboxBrush.HasTarget && !sandboxBrush.CanPlaceTarget) ? new Color(1, .22f, .25f) : Config.paper;
            sandboxCharge.rectTransform.anchorMax = new Vector2(sandboxBrush.IsCharging ? sandboxBrush.ChargeProgress : 0, 1);
        }
        private void FitSandboxHud()
        {
            if (hud == null || safeRoot == null) return;
            float scale = Mathf.Min(1, Mathf.Max(1, safeRoot.rect.width) / 1600);
            hud.anchorMin = hud.anchorMax = new Vector2(.5f, .5f);
            hud.anchoredPosition = Vector2.zero;
            hud.sizeDelta = safeRoot.rect.size / scale;
            hud.localScale = Vector3.one * scale;
        }
        public void ShowSandboxWorkshop()
        {
            if (BubbleSandbox.Instance == null) return;
            sandboxBrush = FindFirstObjectByType<SandboxBrush>();
            UIPage page = Push("调色工具台", "创造与染色都通过吹出的泡泡完成。", false, UIPageDesign.Settings);
            page.Rebuild = ShowSandboxWorkshop;
            RectTransform selected = ui.Row("Selected brush color", page.Content);
            Image preview = ui.Surface("Selected color preview", selected, sandboxBrush.SelectedColor, true);
            UIWidgets.Size(preview.transform, 50, 64);
            Text selection = ui.Label(selected, "", 21); UIWidgets.Size(selection.transform, 50, -1, 1);
            Button freeColor = ui.Button(selected, "自由调色 / HEX", null);
            freeColor.name = "Free color mixer";
            UIWidgets.Size(freeColor.transform, 58, 300).layoutPriority = 3;
            Text[] swatchLabels = new Text[BubbleSandbox.Palette.Length];
            System.Action refreshSelection = () =>
            {
                preview.color = sandboxBrush.SelectedColor;
                selection.text = sandboxBrush.ColorName + "   #" + ColorUtility.ToHtmlStringRGB(sandboxBrush.SelectedColor);
                if (sandboxBrush.HasCustomColor) selection.text = "自定义   " + sandboxBrush.ColorName;
                for (int i = 0; i < swatchLabels.Length; i++)
                    if (swatchLabels[i] != null) swatchLabels[i].text =
                        (!sandboxBrush.HasCustomColor && sandboxBrush.ColorIndex == i ? "● " : "") + BubbleSandbox.ColorNames[i];
                RefreshSandboxHud();
            };
            freeColor.onClick.AddListener(() => ShowSandboxColorMixer(refreshSelection));
            RectTransform palette = ui.Grid("Bubble palette", page.Content, 8, 120);
            palette.GetComponent<UIFlowLayout>().spacing = 10;
            for (int i = 0; i < BubbleSandbox.Palette.Length; i++)
            {
                int index = i;
                Button swatch = ui.Button(palette, BubbleSandbox.ColorNames[i],
                    () => { sandboxBrush.SelectColor(index); refreshSelection(); });
                swatch.name = "Palette " + i;
                UIWidgets.Size(swatch.transform, 54).layoutPriority = 3;
                Image chip = ui.Surface("Paint swatch", swatch.transform, BubbleSandbox.Palette[i], true);
                chip.raycastTarget = false; UIWidgets.Stretch(chip.rectTransform, 3); chip.transform.SetAsFirstSibling();
                Text label = swatch.GetComponentInChildren<Text>();
                label.GetComponent<UIStyledText>().Configure(18, false, Config);
                Color color = BubbleSandbox.Palette[i];
                bool dark = color.grayscale < .49f;
                swatch.GetComponent<UIFluidGradient>().contrastDark = dark;
                swatch.GetComponent<UIButtonFeedback>().SetLabelColor(dark ? Color.white : Config.ink);
                label.rectTransform.offsetMin = new Vector2(4, 2); label.rectTransform.offsetMax = new Vector2(-4, -2);
                swatchLabels[i] = label;
            }
            refreshSelection();
            ui.Label(page.Content, "40 种预设色 · 自由调色支持 RGB 与 HEX · C / V 快速切换预设", 18);
            RectTransform options = ui.Grid("Brush options", page.Content, 2, 360);
            ui.Button(options, "范围：" + sandboxBrush.Diameter + "×" + sandboxBrush.Diameter + "×" + sandboxBrush.Diameter + " 格（X）", () => { sandboxBrush.CycleSize(); RefreshVisiblePage(); });
            ui.Button(options, "造物外形：" + (sandboxBrush.Shape == SandboxShape.Bubble ? "圆泡" : "方泡") + "（G）", () => { sandboxBrush.ToggleShape(); RefreshVisiblePage(); });
            ui.Button(options, "染色笔刷：" + (sandboxBrush.Paint ? "开启" : "关闭") + "（P）", () => { sandboxBrush.TogglePaint(); RefreshVisiblePage(); });
            ui.Button(options, "连续吹泡泡：" + (sandboxBrush.Continuous ? "开启" : "关闭") + "（T）", () => { sandboxBrush.Continuous = !sandboxBrush.Continuous; RefreshVisiblePage(); });
            ui.Label(page.Content, "Q 上浮 / E 下浮：移动已选物体；未选择时移动准心对准的相连造物。\n1 建造：填充方框内的空格；开启染色后给框内已有造物染色。\n2 破坏：有选择时整批拆除所选物件，无选择时拆除方框内全部造物；Ctrl+Z 可整批恢复。\n3 选中：点击添加红框，再次点击取消；R 清空选择。", 21);
            ui.Label(page.Content, "移动中的圆泡与其他圆泡重叠时合并为一颗，保留移动圆泡的颜色。方块仍会阻挡；Ctrl+Z 可恢复合并前的泡泡。", 19);
            ui.Label(page.Content, "贴面放置：瞄准造物的面，在相邻格建造。右键快速吹泡泡；按住 Alt 瞄准脚下，跳起后放置可向上搭高。红框表示被占用或碰到玩家。", 19);
            ui.Label(page.Content, "Ctrl+Z / Y 撤销 / 重做 · F5 保存 · Home 返回入口\n作品每 30 秒自动保存；进入 Creat 时会恢复上次保存的作品。", 19);
            ui.Button(page.Footer, "返回", Pop, true); Finish(page);
        }
        private void ShowSandboxColorMixer(System.Action updated)
        {
            UIPage page = Push("自由调色", "拖动 RGB 滑杆，或输入 6 位 HEX 色号。颜色立即用于建造与染色。", false, UIPageDesign.Settings);
            page.Rebuild = () => ShowSandboxColorMixer(updated);
            page.BeforeClose = () => updated?.Invoke();
            RectTransform row = ui.Row("Custom paint preview", page.Content);
            Image preview = ui.Surface("Custom color preview", row, sandboxBrush.SelectedColor, true);
            UIWidgets.Size(preview.transform, 84, 116);
            Text caption = ui.Label(row, "", 26); UIWidgets.Size(caption.transform, 84, -1, 1);
            Slider[] channels = new Slider[3];
            Text[] channelLabels = new Text[3];
            InputField hex = null;
            Text hint = null;
            string[] names = { "红 R", "绿 G", "蓝 B" };
            System.Action sync = () =>
            {
                Color color = sandboxBrush.SelectedColor;
                preview.color = color; caption.text = "当前颜色   #" + ColorUtility.ToHtmlStringRGB(color);
                for (int i = 0; i < channels.Length; i++)
                    if (channels[i] != null)
                    {
                        int value = Mathf.RoundToInt(color[i] * 255);
                        channels[i].SetValueWithoutNotify(value);
                        channelLabels[i].text = names[i] + "   " + value;
                    }
                if (hex != null) hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(color));
                if (hint != null) { hint.text = "例如 #FF8800；支持复制粘贴，输入完成后按回车应用。"; hint.color = ui.Ink; }
                updated?.Invoke();
            };
            RectTransform rgb = ui.Grid("RGB channels", page.Content, 3, 280);
            Color[] channelColors = { new Color32(216, 76, 76, 255), new Color32(65, 158, 94, 255), new Color32(65, 128, 211, 255) };
            for (int i = 0; i < channels.Length; i++)
            {
                int channel = i;
                channels[i] = ui.Slider(rgb, names[i], sandboxBrush.SelectedColor[i] * 255, 0, 255, value =>
                {
                    Color color = sandboxBrush.SelectedColor; color[channel] = Mathf.Round(value) / 255;
                    sandboxBrush.SelectCustomColor(color); sync();
                });
                channels[i].name = "RGB " + i; channels[i].wholeNumbers = true;
                channelLabels[i] = channels[i].transform.parent.GetComponentInChildren<Text>();
                channels[i].fillRect.GetComponent<Image>().color = channelColors[i];
            }
            ui.Label(page.Content, "HEX 色号", 21);
            RectTransform inputRow = ui.Row("HEX controls", page.Content);
            Image field = ui.Surface("HEX color", inputRow, Color.white, true);
            UIWidgets.Size(field.transform, 64, -1, 1);
            field.gameObject.AddComponent<Outline>().effectColor = Config.ink;
            hex = field.gameObject.AddComponent<InputField>(); hex.targetGraphic = field;
            Text input = ui.Label(field.transform, "", 26);
            UIWidgets.Stretch(input.rectTransform, 12);
            hex.textComponent = input; hex.characterLimit = 7;
            hex.lineType = InputField.LineType.SingleLine;
            hex.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            field.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            System.Action applyHex = () =>
            {
                if (sandboxBrush.SelectHexColor(hex.text)) sync();
                else { hint.text = "请输入 6 位十六进制色号，例如 #FF8800。"; hint.color = new Color(.72f, .16f, .16f); }
            };
            hex.onEndEdit.AddListener(_ => applyHex());
            Button apply = ui.Button(inputRow, "应用色号", () => applyHex());
            UIWidgets.Size(apply.transform, 64, 210).layoutPriority = 3;
            hint = ui.Label(page.Content, "", 18);
            ui.Label(page.Content, "选择的颜色会随造物保存，也支持染色、浮空和撤销 / 重做。", 19);
            sync();
            ui.Button(page.Footer, "完成调色", Pop, true); Finish(page);
        }
        private void ShowSandboxPause()
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            UIPage page = Push("让灵感停留片刻", "CREAT · 究极泡泡沙盒", false, UIPageDesign.Pause);
            page.Rebuild = ShowSandboxPause;
            ui.Button(page.Content, "继续创造", Pop, true);
            RectTransform actions = ui.Grid("Sandbox actions", page.Content, 2, 360);
            ui.Button(actions, "调色与操作", ShowSandboxWorkshop);
            ui.Button(actions, "作品预览", ShowSandboxPreview);
            ui.Button(actions, "游戏设置", ShowSettings);
            ui.Button(actions, "保存作品", ShowSandboxSaveWork);
            ui.Button(actions, "作品存档", ShowSandboxArchives);
            ui.Button(actions, "撤销上一笔", () => world.Undo(), false, world.CanUndo);
            ui.Button(actions, "清空场地", () => Confirm("清空场地？", "清空后仍可用 Ctrl+Z 恢复。", world.ClearWorld, true));
            ui.Button(actions, "返回入口", world.Respawn);
            ui.Button(page.Footer, "保存并返回主菜单", () => { if (world.SaveWorld()) LoadScene(Config.menuScene); });
            ui.Button(page.Footer, "继续创造", Pop, true); Finish(page);
        }
    }
}
