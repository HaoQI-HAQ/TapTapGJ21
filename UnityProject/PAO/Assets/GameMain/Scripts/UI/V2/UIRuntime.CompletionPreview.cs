#if UNITY_EDITOR
namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        // Editor-only visual fixture. It never completes a chapter or writes a save.
        public void ShowChapterCompletionStylePreview()
        {
            if (Config == null || loading) return;
            UIPage page = Push(T("这一段水流，已有回响", "This current now has an echo"), "", false, UIPageDesign.Completion);
            page.gameObject.AddComponent<UIChapterCompletionView>().Build(page, Config, T("水域 02", "WATERS 02"),
                2, 0, 1238, true, T("前往下一章", "Next chapter"), Pop, ShowJournal, ShowChapters,
                () => LoadScene(Config.menuScene));
            Finish(page);
        }
    }
}
#endif
