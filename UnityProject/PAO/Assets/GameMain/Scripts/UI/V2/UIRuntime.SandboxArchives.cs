using System;
using System.IO;
using PAO.Sandbox;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private SandboxWorkArchive sandboxWorkArchive;
        private SandboxWorkArchive WorkArchive => sandboxWorkArchive ?? (sandboxWorkArchive = new SandboxWorkArchive());

        public void ShowSandboxArchives()
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || loading) return;
            var works = WorkArchive.List(out string warning);
            UIPage page = Push("作品存档", "当前 " + world.Count + " 件造物 · 命名作品独立保留，自动保存不会覆盖它们。");
            page.Rebuild = ShowSandboxArchives;
            if (!string.IsNullOrEmpty(warning)) ui.Label(page.Content, warning, 19, new Color(.72f, .16f, .16f));
            RectTransform automatic = ui.Row("Sandbox autosave", page.Content);
            Text autoLabel = ui.Label(automatic, "自动存档  /  最近一次快速保存的作品", 20);
            UIWidgets.Size(autoLabel.transform, 58, -1, 1);
            Button restoreAuto = ui.Button(automatic, "读取自动存档", () => Confirm("读取自动存档？",
                "当前场地会被替换。需要保留当前创作时，请先保存为一份作品存档。", () =>
                {
                    if (world.LoadWorld()) CloseSandboxArchiveMenus();
                    else Toast(world.Status);
                }), false, File.Exists(world.SavePath));
            restoreAuto.name = "Load sandbox autosave";
            UIWidgets.Size(restoreAuto.transform, 58, 240).layoutPriority = 3;
            ui.Label(page.Content, "已保存作品  /  " + works.Count + " 份", 23);
            if (works.Count == 0)
                ui.Label(page.Content, "还没有作品存档。点击下方「保存当前作品」，给你的第一份作品起个名字。", 23);
            RectTransform grid = ui.Grid("Saved sandbox works", page.Content, 2, 500);
            foreach (SandboxWorkInfo work in works)
            {
                SandboxWorkInfo saved = work;
                RectTransform card = ui.Card("Saved work " + saved.Id, grid);
                Text name = ui.Label(card, saved.Name, 25); name.supportRichText = false;
                UIWidgets.Size(name.transform, 72);
                ui.Label(card, saved.CellCount + " 件造物  ·  " + saved.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 18);
                RectTransform actions = ui.Row("Work actions", card, 10);
                Button load = ui.Button(actions, "读取", () => Confirm("读取这份作品？",
                    "将用这份存档替换当前场地。需要保留当前创作时，请先另存一份。", () =>
                    {
                        if (WorkArchive.Load(world, saved.Id, out string error)) CloseSandboxArchiveMenus();
                        else Toast(error);
                    }), true);
                Button overwrite = ui.Button(actions, "覆盖", () => Confirm("覆盖这份作品存档？",
                    "这份存档将更新为当前场地，其他作品存档不受影响。", () =>
                    {
                        if (WorkArchive.Overwrite(world, saved.Id, out string error))
                        { RefreshVisiblePage(); Toast("作品存档已更新。"); }
                        else Toast(error);
                    }));
                Button rename = ui.Button(actions, "改名", () => ShowSandboxWorkName(saved));
                Button delete = ui.Button(actions, "删除", () => Confirm("删除这份作品存档？",
                    "删除后无法恢复。当前场地与其他作品存档会保留。", () =>
                    {
                        if (WorkArchive.Delete(saved.Id, out string error))
                        { RefreshVisiblePage(); Toast("作品存档已删除。"); }
                        else Toast(error);
                    }, true));
                Button[] buttons = { load, overwrite, rename, delete };
                string[] ids = { "Load", "Overwrite", "Rename", "Delete" };
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].name = ids[i] + " work " + saved.Id;
                    UIWidgets.Size(buttons[i].transform, 54, -1, 1);
                    FitSandboxActionLabel(buttons[i]);
                }
            }
            ui.Button(page.Footer, "保存当前作品", ShowSandboxSaveWork, true).name = "Save new sandbox work";
            ui.Button(page.Footer, "返回", Pop);
            Finish(page);
        }

        public void ShowSandboxSaveWork() => ShowSandboxWorkName(null);

        private void ShowSandboxWorkName(SandboxWorkInfo existing)
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || loading) return;
            UIPage page = Push(existing == null ? "保存作品" : "重命名作品", "", true);
            ui.Label(page.Content, existing == null ? "将当前的 " + world.Count + " 件造物保存为独立作品。" : "为这份作品换一个名字，造物内容会保留。", 23);
            ui.Label(page.Content, "作品名称", 21);
            Image field = ui.Surface("Sandbox work name", page.Content, Color.white, true);
            UIWidgets.Size(field.transform, 68);
            field.gameObject.AddComponent<Outline>().effectColor = Config.ink;
            InputField input = field.gameObject.AddComponent<InputField>(); input.targetGraphic = field;
            Text value = ui.Label(field.transform, "", 25); UIWidgets.Stretch(value.rectTransform, 14);
            value.supportRichText = false; input.textComponent = value;
            input.characterLimit = SandboxWorkArchive.NameLimit; input.lineType = InputField.LineType.SingleLine;
            input.text = existing != null ? existing.Name : "我的作品 " + DateTime.Now.ToString("MM-dd HHmm");
            field.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            Text hint = ui.Label(page.Content, "最多 32 个字符。保存新作品不会覆盖已有存档。", 18);
            Color hintColor = hint.color;
            input.onValueChanged.AddListener(_ =>
            {
                hint.text = "最多 32 个字符。保存新作品不会覆盖已有存档。";
                hint.color = hintColor;
            });
            ui.Button(page.Footer, "取消", Pop);
            Button save = ui.Button(page.Footer, existing == null ? "保存作品" : "确认改名", () =>
            {
                string error;
                bool success = existing == null ? WorkArchive.SaveNew(world, input.text, out _, out error) :
                    WorkArchive.Rename(existing.Id, input.text, out error);
                if (!success) { hint.text = error; hint.color = new Color(.72f, .16f, .16f); return; }
                Pop();
                if (pages.Count > 0 && pages[pages.Count - 1].Rebuild == (Action)ShowSandboxArchives) RefreshVisiblePage();
                Toast(existing == null ? "作品已存档。" : "作品已改名。");
            }, true);
            save.name = "Confirm sandbox work name";
            Finish(page);
        }

        private void CloseSandboxArchiveMenus()
        {
            // Disposes any preview under the archive page before returning to the newly loaded scene.
            while (pages.Count > 0) Pop();
        }
    }
}
