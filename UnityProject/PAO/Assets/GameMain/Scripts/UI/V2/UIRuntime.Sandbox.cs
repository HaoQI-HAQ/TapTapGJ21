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
                UICreatStyle.StyleButton(swatch);
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
            ui.Button(page.Footer, "返回", Pop, true); FinishCreatPage(page);
        }
        private void ShowSandboxColorMixer(System.Action updated)
        {
            ShowCreatColorMixer(updated);
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
            ui.Button(page.Footer, "继续创造", Pop, true); FinishCreatPage(page);
        }
    }
}
