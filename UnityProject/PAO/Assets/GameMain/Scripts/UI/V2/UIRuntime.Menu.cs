using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private string menuSignature;

        private sealed class MenuRecordState
        {
            public SaveSlotData latest;
            public int damaged, unavailable;
            public string Signature => (latest == null ? "empty" : latest.slot + ":" + latest.savedAtUtc) + ":" + damaged + ":" + unavailable;
        }

        private MenuRecordState ReadMenuRecords()
        {
            var state = new MenuRecordState();
            for (int i = 0; i <= UIRunState.AutoSlot; i++)
            {
                SaveSlotData record = SaveSystem.LoadSlot(i);
                if (record == null) { if (SaveSystem.HasSlot(i) || SaveSystem.HasBackup(i)) state.damaged++; continue; }
                if (!Config.Supports(record.sceneName) || record.sceneName == Config.menuScene || !Application.CanStreamedLevelBeLoaded(record.sceneName))
                { state.unavailable++; continue; }
                if (state.latest == null || (record.SavedAtLocal ?? DateTime.MinValue) > (state.latest.SavedAtLocal ?? DateTime.MinValue))
                    state.latest = record;
            }
            return state;
        }

        private void ShowMenu()
        {
            MenuRecordState state = ReadMenuRecords(); menuSignature = state.Signature;
            if (state.latest != null) UIRunState.Apply(state.latest);
            else UIRunState.NewRun();
            UIPage page = Push(T("泡涌", "BUBBLE TIDE")); page.Rebuild = ShowMenu;
            VerticalLayoutGroup body = page.transform.Find("Page").GetComponent<VerticalLayoutGroup>();
            body.padding = new RectOffset(48, 48, 32, 32); body.spacing = 16;
            body.transform.GetChild(0).gameObject.SetActive(false);
            VerticalLayoutGroup contentLayout = page.Content.GetComponent<VerticalLayoutGroup>();
            contentLayout.childForceExpandWidth = false; contentLayout.childAlignment = TextAnchor.UpperCenter;
            RectTransform row = ui.Row("Menu and illustration", page.Content, 56);
            LayoutElement composition = row.gameObject.AddComponent<LayoutElement>();
            composition.preferredWidth = 1440; composition.minWidth = 0; composition.flexibleWidth = 0;
            RectTransform actions = ui.Column("Menu actions", row, 8, 8);
            LayoutElement actionSize = UIWidgets.Size(actions, -1, 440);
            ui.Label(actions, T("一段关于水、生命与意识的旅程", "A journey through water, life and consciousness"), 17);
            ui.Label(actions, T("泡涌", "BUBBLE TIDE"), 48);
            ui.Label(actions, T("意识之海", "THE SEA OF CONSCIOUSNESS"), 22);
            MenuRule(actions);
            ui.Label(actions, T("让每一次微小的涌动，\n成为抵达深处的方向。", "Let a small ripple\nguide you into the deep."), 22);

            RectTransform waters = ui.Column("Water and journey", row, 8, 16);
            UIWidgets.Size(waters, -1, -1, 1);
            Image art = ui.Surface("Jellyfish paper art", waters, Color.white);
            art.sprite = Config.menuArt; art.preserveAspect = true; art.raycastTarget = false;
            LayoutElement artSize = UIWidgets.Size(art.transform, 304, -1, 1);
            art.gameObject.AddComponent<UIMenuAmbience>();
            if (Config.menuArt == null) art.color = Config.mist;
            RectTransform journey = ui.Column("Journey overview", waters, 24, 8);
            Image card = journey.gameObject.AddComponent<Image>(); card.color = Config.mist;
            card.sprite = Config.round; card.type = Image.Type.Sliced; card.raycastTarget = false;
            BuildMenuOverview(journey, state);
            MenuRule(journey);
            Text context = ui.Label(journey, "", 20);
            context.name = "Action description";
            UIWidgets.Size(context.transform, 64).layoutPriority = 3;

            if (state.latest != null)
                MenuButton(actions, T("继续游戏", "Continue"), () => ContinueJourney(state.latest.slot), context,
                    T("恢复最近记录的章节、位置与意识碎片。", "Restore the latest chapter, position and memories."), true,
                    ChapterName(state.latest.sceneName) + "  ·  " + state.latest.SavedAtText);
            MenuButton(actions, T("新的旅程", "New journey"), RequestNewRun, context,
                state.latest == null ? T("从第一段水流开始，认识泡泡与环境。", "Start with the first current and discover bubbles and the environment.") :
                    T("从第一章重新出发。开始前会确认自动记录的更新。", "Begin again in chapter one. Updating the automatic record requires confirmation."), state.latest == null);
            MenuButton(actions, T("章节路径", "Chapter path"), ShowChapters, context,
                T("回看已抵达的水域，选择已经解锁的章节。", "Revisit explored waters and choose an unlocked chapter."));
            MenuButton(actions, T("读取记录", "Load record"), ShowSaves, context,
                T("浏览自动与手动记录，或恢复上一份有效备份。", "Browse automatic and manual records, or restore a previous backup."));
            MenuButton(actions, T("游戏设置", "Settings"), ShowSettings, context,
                T("调节声音、画面、键鼠、手柄与辅助选项。", "Adjust audio, video, keyboard, controller and accessibility."));
            MenuButton(actions, T("制作人员", "Credits"), ShowCredits, context,
                T("认识共同创造意识之海的人。", "Meet the people who created this sea."));
            row.gameObject.AddComponent<UIMenuLayout>().Configure(actionSize, artSize);

            MenuButton(page.Footer, T("操作与教学", "Controls & tutorials"), ShowHelp, context,
                T("查看当前操作绑定与已学会的探索技巧。", "Review current bindings and exploration lessons."));
            MenuButton(page.Footer, T("离开意识之海", "Quit"), ConfirmQuit, context,
                T("离开前需要再次确认。", "You will be asked to confirm before leaving."));
            Text navigation = ui.Label(page.Footer, "", 17, null, TextAnchor.MiddleRight);
            UIWidgets.Size(navigation.transform, -1, -1, 1);
            navigation.gameObject.AddComponent<UIMenuNavigationHint>();
            Finish(page);
        }

        private Button MenuButton(Transform parent, string title, Action click, Text context, string description,
            bool primary = false, string detail = null)
        {
            Button button = ui.Button(parent, title, click, primary);
            Text label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(24, detail == null ? 6 : 32);
            label.rectTransform.offsetMax = new Vector2(-48, -6);
            Text marker = ui.Label(button.transform, "›", 28, primary ? Color.white : ui.Ink, TextAnchor.MiddleRight);
            UIWidgets.Stretch(marker.rectTransform); marker.rectTransform.offsetMax = new Vector2(-20, 0);
            if (detail != null)
            {
                Text caption = ui.Label(button.transform, detail, 16, Color.white);
                caption.name = "Record detail"; UIWidgets.Stretch(caption.rectTransform);
                caption.rectTransform.offsetMin = new Vector2(24, 8); caption.rectTransform.offsetMax = new Vector2(-48, -42);
            }
            button.gameObject.AddComponent<UIMenuButtonLayout>().Configure(label, detail != null ? button.transform.Find("Record detail").GetComponent<Text>() : null);
            button.gameObject.AddComponent<UIMenuActionContext>().Configure(context, description);
            return button;
        }

        private void MenuRule(Transform parent)
        {
            Image rule = ui.Surface("Quiet divider", parent, new Color(Config.ink.r, Config.ink.g, Config.ink.b, 0.14f));
            rule.raycastTarget = false; UIWidgets.Size(rule.transform, 1);
        }

        private void BuildMenuOverview(Transform parent, MenuRecordState state)
        {
            SaveSlotData latest = state.latest;
            RunProgress progress = latest != null && latest.runProgress != null ? latest.runProgress : new RunProgress();
            ui.Label(parent, latest == null ? T("你的旅程，从这里开始", "Your journey begins here") :
                T("从上次的涟漪继续", "Continue from your last ripple"), 26);
            ui.Label(parent, latest == null ? T("尚未留下旅程记录 · 完成探索时会自动记录。", "No journey recorded yet. Progress is recorded as you explore.") :
                ChapterName(latest.sceneName) + "  ·  " + latest.SavedAtText, 18);
            int completed = 0;
            foreach (ChapterDefinition chapter in Config.chapters) if (progress.completed.Contains(chapter.scene)) completed++;
            RectTransform statistics = ui.Row("Journey statistics", parent, 24);
            ui.Label(statistics, T("已完成水域 ", "Chapters ") + completed + " / " + Config.chapters.Length, 18);
            ui.Label(statistics, T("意识碎片 ", "Memories ") + progress.memories.Count, 18);
            if (latest != null) ui.Label(parent, T("探索时间 ", "Time explored ") + Mathf.FloorToInt(progress.playSeconds / 3600) +
                T(" 小时 ", " h ") + Mathf.FloorToInt(progress.playSeconds / 60 % 60) + T(" 分钟", " min"), 18);
            RectTransform path = ui.Row("Journey progress", parent, 8); UIWidgets.Size(path, 8);
            foreach (ChapterDefinition chapter in Config.chapters)
            {
                Image segment = ui.Surface(progress.completed.Contains(chapter.scene) ? "Completed water" : "Unexplored water", path,
                    progress.completed.Contains(chapter.scene) ? (UISettings.Current.highContrast ? Color.black : Config.teal) :
                    new Color(Config.ink.r, Config.ink.g, Config.ink.b, 0.15f), true);
                segment.raycastTarget = false; UIWidgets.Size(segment.transform, 8, -1, 1);
            }
            if (state.damaged > 0 || state.unavailable > 0)
                ui.Label(parent, state.damaged > 0 ? T("部分记录无法读取。可在「读取记录」中尝试恢复备份。", "Some records are unreadable. Try restoring a backup in Load record.") :
                    T("部分记录的水域暂不可用。请选择其他记录或开启新的旅程。", "Some recorded waters are unavailable. Choose another record or begin a new journey."), 18);
        }

        private void ContinueJourney(int slot)
        {
            SaveSlotData record = SaveSystem.LoadSlot(slot);
            if (record == null || !Config.Supports(record.sceneName) || record.sceneName == Config.menuScene ||
                !Application.CanStreamedLevelBeLoaded(record.sceneName))
            { RefreshVisiblePage(); Toast(T("这份记录已不可用，请选择其他记录或恢复备份。", "This record is no longer available. Choose another record or restore its backup.")); return; }
            LoadScene(record.sceneName, record);
        }

        private void RefreshMenuOnReturn(UIPage parent)
        {
            if (parent.GetComponentInChildren<UIMenuLayout>() != null && ReadMenuRecords().Signature != menuSignature)
                RefreshVisiblePage();
        }
    }
}
