namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        public void ShowPrototypeCatalog(UIPrototypeLab lab)
        {
            if (loading || lab == null) return;
            UIPage page = Push(T("UI 模块预览", "UI module preview"), T("以下叙事与建构内容均为测试示例。", "Narrative and construction content below are test examples."));
            ui.Button(page.Content, T("暂停与设置", "Pause & settings"), () => { while (pages.Count > 0) Pop(); ShowPause(); });
            ui.Button(page.Content, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Content, T("保存与读取", "Save & load"), ShowSaves);
            ui.Button(page.Content, T("对话与分支", "Dialogue & choices"), () => ShowDialogue(lab.dialogue));
            ui.Button(page.Content, T("建构预览", "Construction preview"), () => { while (pages.Count > 0) Pop(); ShowConstruction(lab.site); });
            ui.Button(page.Content, T("章节完成", "Chapter completion"), () => { while (pages.Count > 0) Pop(); CompleteChapter(); });
            ui.Button(page.Content, T("日志与教学", "Journal & tutorials"), ShowJournal);
            ui.Button(page.Footer, T("返回 HUD", "Return to HUD"), () => { while (pages.Count > 0) Pop(); });
            Finish(page);
        }
    }
}
