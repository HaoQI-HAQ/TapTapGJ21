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
            UIPage page = Push(T("作品存档", "Saved works"), string.Format(T("当前 {0} 件造物 · 命名作品独立保留，自动保存不会覆盖它们。", "{0} objects in this work. Named works are stored separately from autosave."), world.Count));
            page.Rebuild = ShowSandboxArchives;
            if (!string.IsNullOrEmpty(warning)) ui.Label(page.Content, warning, 19, new Color(.72f, .16f, .16f));
            RectTransform automatic = ui.Row("Sandbox autosave", page.Content);
            Text autoLabel = ui.Label(automatic, T("自动存档  /  最近一次快速保存的作品", "Autosave  /  Latest quick save"), 20);
            UIWidgets.Size(autoLabel.transform, 58, -1, 1);
            Button restoreAuto = ui.Button(automatic, T("读取自动存档", "Load autosave"), () => Confirm(T("读取自动存档？", "Load autosave?"),
                T("当前场地会被替换。需要保留当前创作时，请先保存为一份作品存档。", "This replaces the current scene. Save your current work first to keep it."), () =>
                {
                    if (world.LoadWorld()) CloseSandboxArchiveMenus();
                    else Toast(world.Status);
                }), false, File.Exists(world.SavePath));
            restoreAuto.name = "Load sandbox autosave";
            UIWidgets.Size(restoreAuto.transform, 58, 240).layoutPriority = 3;
            ui.Label(page.Content, string.Format(T("已保存作品  /  {0} 份", "Saved works  /  {0}"), works.Count), 23);
            if (works.Count == 0)
                ui.Label(page.Content, T("还没有作品存档。点击下方「保存当前作品」，给你的第一份作品起个名字。", "No saved works yet. Choose Save current work below and give your first work a name."), 23);
            RectTransform grid = ui.Grid("Saved sandbox works", page.Content, 2, 500);
            foreach (SandboxWorkInfo work in works)
            {
                SandboxWorkInfo saved = work;
                RectTransform card = ui.Card("Saved work " + saved.Id, grid);
                Text name = ui.Label(card, saved.Name, 25); name.supportRichText = false;
                UIWidgets.Size(name.transform, 72);
                ui.Label(card, string.Format(T("{0} 件造物  ·  {1}", "{0} objects  ·  {1}"), saved.CellCount, saved.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")), 18);
                RectTransform actions = ui.Row("Work actions", card, 10);
                Button load = ui.Button(actions, T("读取", "Load"), () => Confirm(T("读取这份作品？", "Load this work?"),
                    T("将用这份存档替换当前场地。需要保留当前创作时，请先另存一份。", "This replaces the current scene. Save a separate copy of your current work first to keep it."), () =>
                    {
                        if (WorkArchive.Load(world, saved.Id, out string error)) CloseSandboxArchiveMenus();
                        else Toast(error);
                    }), true);
                Button overwrite = ui.Button(actions, T("覆盖", "Overwrite"), () => Confirm(T("覆盖这份作品存档？", "Overwrite this saved work?"),
                    T("这份存档将更新为当前场地，其他作品存档不受影响。", "This save will be replaced with the current scene. Other saved works will be kept."), () =>
                    {
                        if (WorkArchive.Overwrite(world, saved.Id, out string error))
                        { RefreshVisiblePage(); Toast(T("作品存档已更新。", "Saved work updated.")); }
                        else Toast(error);
                    }));
                Button rename = ui.Button(actions, T("改名", "Rename"), () => ShowSandboxWorkName(saved));
                Button delete = ui.Button(actions, T("删除", "Delete"), () => Confirm(T("删除这份作品存档？", "Delete this saved work?"),
                    T("删除后无法恢复。当前场地与其他作品存档会保留。", "This cannot be undone. Your current scene and other saved works will be kept."), () =>
                    {
                        if (WorkArchive.Delete(saved.Id, out string error))
                        { RefreshVisiblePage(); Toast(T("作品存档已删除。", "Saved work deleted.")); }
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
            ui.Button(page.Footer, T("保存当前作品", "Save current work"), ShowSandboxSaveWork, true).name = "Save new sandbox work";
            ui.Button(page.Footer, T("返回", "Back"), Pop);
            FinishCreatPage(page);
        }

        public void ShowSandboxSaveWork() => ShowSandboxWorkName(null);

        private void ShowSandboxWorkName(SandboxWorkInfo existing)
        {
            BubbleSandbox world = BubbleSandbox.Instance;
            if (world == null || loading) return;
            UIPage page = Push(existing == null ? T("保存作品", "Save work") : T("重命名作品", "Rename work"), "", true);
            Text introduction = ui.Label(page.Content, "", 23);
            Text nameLabel = ui.Label(page.Content, "", 21);
            Image field = ui.Surface("Sandbox work name", page.Content, Color.white, true);
            UIWidgets.Size(field.transform, 68);
            field.gameObject.AddComponent<Outline>().effectColor = Config.ink;
            InputField input = field.gameObject.AddComponent<InputField>(); input.targetGraphic = field;
            Text value = ui.Label(field.transform, "", 25); UIWidgets.Stretch(value.rectTransform, 14);
            value.supportRichText = false; input.textComponent = value;
            input.characterLimit = SandboxWorkArchive.NameLimit; input.lineType = InputField.LineType.SingleLine;
            input.text = existing != null ? existing.Name : T("我的作品 ", "My work ") + DateTime.Now.ToString("MM-dd HHmm");
            field.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            Text hint = ui.Label(page.Content, "", 18);
            Color hintColor = hint.color;
            bool failed = false;
            input.onValueChanged.AddListener(_ =>
            {
                failed = false;
                hint.text = T("最多 32 个字符。保存新作品不会覆盖已有存档。", "Up to 32 characters. A new work will not overwrite existing saves.");
                hint.color = hintColor;
            });
            Button cancel = ui.Button(page.Footer, T("取消", "Cancel"), Pop);
            Button save = ui.Button(page.Footer, existing == null ? T("保存作品", "Save work") : T("确认改名", "Rename"), () =>
            {
                string error;
                bool success = existing == null ? WorkArchive.SaveNew(world, input.text, out _, out error) :
                    WorkArchive.Rename(existing.Id, input.text, out error);
                if (!success) { failed = true; hint.text = error; hint.color = new Color(.72f, .16f, .16f); return; }
                Pop();
                if (pages.Count > 0 && pages[pages.Count - 1].Rebuild == (Action)ShowSandboxArchives) RefreshVisiblePage();
                Toast(existing == null ? T("作品已存档。", "Work saved.") : T("作品已改名。", "Work renamed."));
            }, true);
            save.name = "Confirm sandbox work name";
            page.gameObject.AddComponent<UICreatPageTextLocale>().Configure(() =>
            {
                Text heading = page.transform.Find("Page/Page title")?.GetComponent<Text>();
                if (heading != null) heading.text = existing == null ? T("保存作品", "Save work") : T("重命名作品", "Rename work");
                introduction.text = existing == null ? string.Format(T("将当前的 {0} 件造物保存为独立作品。", "Save the current {0} objects as a separate work."), world.Count) : T("为这份作品换一个名字，造物内容会保留。", "Give this work a new name. Its contents will be kept.");
                nameLabel.text = T("作品名称", "Work name");
                cancel.GetComponentInChildren<Text>().text = T("取消", "Cancel");
                save.GetComponentInChildren<Text>().text = existing == null ? T("保存作品", "Save work") : T("确认改名", "Rename");
                if (!failed) hint.text = T("最多 32 个字符。保存新作品不会覆盖已有存档。", "Up to 32 characters. A new work will not overwrite existing saves.");
                else if (!SandboxWorkArchive.ValidateName(input.text, out _, out string nameError)) hint.text = nameError;
                else hint.text = existing == null ?
                    T("保存失败，请检查存储空间或访问权限。", "Save failed. Check storage space and access permissions.") :
                    T("重命名失败，原作品存档已保留。", "Rename failed. The original saved work has been kept.");
            });
            FinishCreatPage(page);
        }

        private void CloseSandboxArchiveMenus()
        {
            // Disposes any preview under the archive page before returning to the newly loaded scene.
            while (pages.Count > 0) Pop();
        }
    }

    // Update only labels so unfinished work names and HEX inputs survive language previews.
    public sealed class UICreatPageTextLocale : MonoBehaviour
    {
        private Action refresh;
        public void Configure(Action update) { refresh = update; refresh?.Invoke(); }
        private void OnEnable() { UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UISettings.Changed -= Refresh; }
        private void Refresh() { refresh?.Invoke(); }
    }
}
