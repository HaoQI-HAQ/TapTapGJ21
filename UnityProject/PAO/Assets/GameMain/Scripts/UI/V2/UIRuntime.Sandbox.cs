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
            BuildCreatHud();
        }
        private Text SandboxLabel(Transform parent, string value, int size, Vector2 position, Vector2 extent)
        {
            Text label = UICreatStyle.Label(ui, parent, value, size);
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
            bool selecting = sandboxBrush.Tool == SandboxTool.Select;
            int selected = sandboxBrush.Selection != null ? sandboxBrush.Selection.Count : 0;
            bool destroyingSelection = sandboxBrush.Tool == SandboxTool.Destroy && selected > 0;
            sandboxStatus.text = world.Status;
            sandboxColor.color = sandboxBrush.SelectedColor;
            sandboxTarget.text = destroyingSelection ? string.Format(T("将拆除已选中的 {0} 件造物 · Ctrl+Z 可整批恢复", "Erase {0} selected pieces · Ctrl+Z to undo"), selected) :
                selecting ? (sandboxBrush.HoveredPiece != null ?
                (sandboxBrush.Selection.Contains(sandboxBrush.HoveredPiece) ? T("再次点击取消选中", "Click again to deselect") : T("点击选中造物", "Click to select this piece")) : T("将十字准心对准造物", "Aim at a piece")) :
                selected > 0 && (sandboxBrush.Tool == SandboxTool.Lift || sandboxBrush.Tool == SandboxTool.Lower) ? string.Format(T("将移动已选中的 {0} 件造物", "Move {0} selected pieces"), selected) :
                sandboxBrush.HasTarget ? "[ " + sandboxBrush.TargetCell.x + ", " + sandboxBrush.TargetCell.y + ", " + sandboxBrush.TargetCell.z + " ]  " +
                (!sandboxBrush.CanPlaceTarget ? T("该格被占用或碰到玩家 · 跳起后再放置", "Space occupied or touching you · Jump, then place") :
                    sandboxBrush.Tool == SandboxTool.Lift ? T("相连造物整体上浮", "Raise connected pieces") : sandboxBrush.Tool == SandboxTool.Lower ? T("相连造物整体下浮", "Lower connected pieces") :
                    sandboxBrush.Tool == SandboxTool.Destroy ? T("拆除方框内全部造物", "Erase all pieces in the box") :
                    sandboxBrush.TargetingUnderfoot ? T("脚下放置 · 跳起后吹泡泡搭高", "Place below · Jump to build higher") : T("贴着瞄准面的相邻格放置", "Place against the aimed surface")) : T("瞄准场地或造物 · Home 返回入口", "Aim at the arena or a piece · Home to return");
            for (int i = 0; i < sandboxToolMarks.Length; i++) if (sandboxToolMarks[i] != null) sandboxToolMarks[i].enabled = i == (int)sandboxBrush.Tool;
            sandboxSelectionHud.gameObject.SetActive(selecting);
            sandboxSelectionUp.interactable = sandboxSelectionDown.interactable = selected > 0 || sandboxBrush.HoveredPiece != null;
            sandboxSelectionClear.interactable = selected > 0;
            foreach (Image arm in sandboxCrosshairArms)
                if (arm != null) arm.color = (selecting && sandboxBrush.HoveredPiece != null) ||
                    (sandboxBrush.HasTarget && !sandboxBrush.CanPlaceTarget) ? new Color(1, .22f, .25f) : UICreatStyle.Paper;
            RefreshCreatToolSelection();
        }
        private void FitSandboxHud()
        {
            LayoutCreatHud();
        }
        public void ShowSandboxWorkshop()
        {
            if (BubbleSandbox.Instance == null) return;
            sandboxBrush = FindFirstObjectByType<SandboxBrush>();
            UIPage page = Push(T("调色工具台", "Color workbench"), T("创造与染色都通过吹出的泡泡完成。", "Blow bubbles to build and paint."), false, UIPageDesign.Settings);
            page.Rebuild = ShowSandboxWorkshop;
            RectTransform selected = ui.Row("Selected brush color", page.Content);
            Image preview = ui.Surface("Selected color preview", selected, sandboxBrush.SelectedColor, true);
            UIWidgets.Size(preview.transform, 50, 64);
            Text selection = ui.Label(selected, "", 21); UIWidgets.Size(selection.transform, 50, -1, 1);
            Button freeColor = ui.Button(selected, T("自由调色 / HEX", "Custom / HEX"), null);
            freeColor.name = "Free color mixer";
            UIWidgets.Size(freeColor.transform, 58, 300).layoutPriority = 3;
            Text freeColorLabel = freeColor.GetComponentInChildren<Text>();
            freeColorLabel.GetComponent<UIStyledText>().Configure(22, false, Config);
            freeColorLabel.rectTransform.offsetMin = new Vector2(8, 2);
            freeColorLabel.rectTransform.offsetMax = new Vector2(-8, -2);
            freeColorLabel.resizeTextForBestFit = true;
            freeColorLabel.resizeTextMinSize = Mathf.RoundToInt(18 * UISettings.Current.textScale);
            freeColorLabel.resizeTextMaxSize = Mathf.RoundToInt(22 * UISettings.Current.textScale);
            Text[] swatchLabels = new Text[BubbleSandbox.Palette.Length];
            System.Action refreshSelection = () =>
            {
                preview.color = sandboxBrush.SelectedColor;
                selection.text = sandboxBrush.ColorName + "   #" + ColorUtility.ToHtmlStringRGB(sandboxBrush.SelectedColor);
                if (sandboxBrush.HasCustomColor) selection.text = T("自定义   ", "Custom   ") + sandboxBrush.ColorName;
                for (int i = 0; i < swatchLabels.Length; i++)
                    if (swatchLabels[i] != null) swatchLabels[i].text =
                        (!sandboxBrush.HasCustomColor && sandboxBrush.ColorIndex == i ? "● " : "") + BubbleSandbox.ColorName(i);
                RefreshSandboxHud();
            };
            freeColor.onClick.AddListener(() => ShowSandboxColorMixer(refreshSelection));
            RectTransform palette = ui.Grid("Bubble palette", page.Content, 8, 120);
            palette.GetComponent<UIFlowLayout>().spacing = 10;
            for (int i = 0; i < BubbleSandbox.Palette.Length; i++)
            {
                int index = i;
                Button swatch = ui.Button(palette, BubbleSandbox.ColorName(i),
                    () => { sandboxBrush.SelectColor(index); refreshSelection(); });
                swatch.name = "Palette " + i;
                UIWidgets.Size(swatch.transform, 54).layoutPriority = 3;
                Image chip = ui.Surface("Paint swatch", swatch.transform, BubbleSandbox.Palette[i], true);
                chip.raycastTarget = false; UIWidgets.Stretch(chip.rectTransform, 3); chip.transform.SetAsFirstSibling();
                Text label = swatch.GetComponentInChildren<Text>();
                label.GetComponent<UIStyledText>().Configure(18, false, Config);
                UICreatStyle.StyleButton(swatch);
                label.rectTransform.offsetMin = new Vector2(4, 2); label.rectTransform.offsetMax = new Vector2(-4, -2);
                FitCreatHudLine(label, 12);
                swatchLabels[i] = label;
            }
            refreshSelection();
            ui.Label(page.Content, T("40 种预设色 · 自由调色支持 RGB 与 HEX · C / V 快速切换预设", "40 presets · RGB and HEX custom colors · C / V to cycle presets"), 18);
            RectTransform options = ui.Grid("Brush options", page.Content, 2, 360);
            ui.Button(options, T("范围：", "Size: ") + sandboxBrush.Diameter + "×" + sandboxBrush.Diameter + "×" + sandboxBrush.Diameter + T(" 格（X）", " cells (X)"), () => { sandboxBrush.CycleSize(); RefreshVisiblePage(); });
            ui.Button(options, T("造物外形：", "Shape: ") + (sandboxBrush.Shape == SandboxShape.Bubble ? T("圆泡", "Sphere") : T("方泡", "Block")) + " (G)", () => { sandboxBrush.ToggleShape(); RefreshVisiblePage(); });
            ui.Button(options, T("染色笔刷：", "Paint brush: ") + (sandboxBrush.Paint ? T("开启", "On") : T("关闭", "Off")) + " (P)", () => { sandboxBrush.TogglePaint(); RefreshVisiblePage(); });
            ui.Button(options, T("连续吹泡泡：", "Continuous bubbles: ") + (sandboxBrush.Continuous ? T("开启", "On") : T("关闭", "Off")) + " (T)", () => { sandboxBrush.Continuous = !sandboxBrush.Continuous; RefreshVisiblePage(); });
            ui.Label(page.Content, T("Q 上浮 / E 下浮：移动已选物体；未选择时移动准心对准的相连造物。\n1 建造：填充方框内的空格；开启染色后给框内已有造物染色。\n2 破坏：有选择时整批拆除所选物件，无选择时拆除方框内全部造物；Ctrl+Z 可整批恢复。\n3 选中：点击添加红框，再次点击取消；R 清空选择。", "Q Raise / E Lower: move selected pieces, or the connected pieces under the crosshair.\n1 Build: fill empty cells in the box; paint mode recolors existing pieces.\n2 Erase: remove the selection, or all pieces in the box. Ctrl+Z restores the edit.\n3 Select: click to select or deselect. R clears the selection."), 21);
            ui.Label(page.Content, T("移动中的圆泡与其他圆泡重叠时合并为一颗，保留移动圆泡的颜色。方块仍会阻挡；Ctrl+Z 可恢复合并前的泡泡。", "Moving spheres merge with overlapping spheres and keep the moving sphere's color. Blocks still stop them. Ctrl+Z restores the bubbles before merging."), 19);
            ui.Label(page.Content, T("贴面放置：瞄准造物的面，在相邻格建造。右键快速吹泡泡；按住 Alt 瞄准脚下，跳起后放置可向上搭高。红框表示被占用或碰到玩家。", "Aim at a surface to build beside it. Right-click for quick release. Hold Alt to aim below your feet; jump to build higher. Red means occupied or touching you."), 19);
            ui.Label(page.Content, T("Ctrl+Z / Y 撤销 / 重做 · F5 保存 · Home 返回入口\n作品每 30 秒自动保存；进入 Creat 时会恢复上次保存的作品。", "Ctrl+Z / Y Undo / Redo · F5 Save · Home Return\nAutosaves every 30 seconds. Creat restores your last saved work on entry."), 19);
            ui.Button(page.Footer, T("返回", "Back"), Pop, true); FinishCreatPage(page);
        }
        private void ShowSandboxColorMixer(System.Action updated)
        {
            ShowCreatColorMixer(updated);
        }
        private void ShowSandboxPause()
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            UIPage page = Push(T("让灵感停留片刻", "Take a creative break"), T("CREAT · 究极泡泡沙盒", "CREAT · Bubble sandbox"), false, UIPageDesign.Pause);
            page.Rebuild = ShowSandboxPause;
            ui.Button(page.Content, T("继续创造", "Resume creating"), Pop, true);
            RectTransform actions = ui.Grid("Sandbox actions", page.Content, 2, 360);
            ui.Button(actions, T("调色与操作", "Colors and controls"), ShowSandboxWorkshop);
            ui.Button(actions, T("作品预览", "Preview"), ShowSandboxPreview);
            ui.Button(actions, T("游戏设置", "Settings"), ShowSettings);
            ui.Button(actions, T("保存作品", "Save work"), ShowSandboxSaveWork);
            ui.Button(actions, T("作品存档", "Work archive"), ShowSandboxArchives);
            ui.Button(actions, T("撤销上一笔", "Undo last edit"), () => world.Undo(), false, world.CanUndo);
            ui.Button(actions, T("清空场地", "Clear arena"), () => Confirm(T("清空场地？", "Clear the arena?"), T("清空后仍可用 Ctrl+Z 恢复。", "You can restore it with Ctrl+Z."), world.ClearWorld, true));
            ui.Button(actions, T("返回入口", "Return to entry"), world.Respawn);
            ui.Button(page.Footer, T("保存并返回主菜单", "Save and return to menu"), () => { if (world.SaveWorld()) LoadScene(Config.menuScene); });
            ui.Button(page.Footer, T("继续创造", "Resume creating"), Pop, true); FinishCreatPage(page);
        }
    }
}
