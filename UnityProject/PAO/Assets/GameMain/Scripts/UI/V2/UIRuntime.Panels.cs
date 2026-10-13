using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private void ShowMenu()
        {
            UIPage page = Push(T("泡涌", "BUBBLE TIDE"), T("意识之海  /  THE SEA OF CONSCIOUSNESS", "THE SEA OF CONSCIOUSNESS"));
            page.Rebuild = ShowMenu;
            VerticalLayoutGroup body = page.transform.Find("Page").GetComponent<VerticalLayoutGroup>();
            body.padding = new RectOffset(40, 40, 40, 40); body.spacing = 16;
            RectTransform row = ui.Row("Menu and illustration", page.Content, 64);
            RectTransform actions = ui.Column("Menu actions", row, 0, 10);
            UIWidgets.Size(actions, -1, 480);
            ui.Label(actions, T("让每一次微小的涌动，\n成为抵达深处的方向。", "Let a small ripple\nguide you into the deep."), 28);
            SaveSlotData latest = UIRunState.Latest();
            if (latest != null) ui.Button(actions, T("继续游戏", "Continue"), () => LoadScene(latest.sceneName, latest), true);
            ui.Button(actions, T("新的旅程", "New journey"), RequestNewRun, latest == null);
            ui.Button(actions, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(actions, T("读取记录", "Load record"), ShowSaves);
            ui.Button(actions, T("游戏设置", "Settings"), ShowSettings);
            ui.Button(actions, T("制作人员", "Credits"), ShowCredits);
            Image art = ui.Surface("Jellyfish paper art", row, Color.white);
            art.sprite = Config.menuArt; art.preserveAspect = true; art.raycastTarget = false;
            UIWidgets.Size(art.transform, 0, -1, 1).flexibleHeight = 1;
            ui.Button(page.Footer, T("操作与教学", "Controls & tutorials"), ShowHelp);
            ui.Button(page.Footer, T("离开意识之海", "Quit"), ConfirmQuit);
            Finish(page);
        }

        private void RequestNewRun()
        {
            Action begin = () =>
            {
                if (Config.chapters.Length == 0) { Toast(T("还没有可进入的章节。", "No chapters are available.")); return; }
                UIRunState.NewRun(); SaveOnNextEntry = true; LoadScene(Config.chapters[0].scene);
            };
            if (UIRunState.Latest() != null)
                Confirm(T("开启新的旅程？", "Start a new journey?"),
                    T("新的旅程会更新自动记录。需要保留当前旅程时，请先保存到手动槽位。", "The new journey updates the automatic record. Save your current journey to a manual slot first to keep it."), begin);
            else begin();
        }

        public void ShowPause()
        {
            if (!IsGameplay || IsBlockingGameplay) return;
            UIPage page = Push(T("在这里，停留片刻", "Take a moment"), ChapterName(lastScene));
            page.Rebuild = ShowPause;
            ui.Button(page.Content, T("继续探索", "Resume"), Pop, true);
            ui.Button(page.Content, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Content, T("保存与读取记录", "Save & load records"), ShowSaves);
            ui.Button(page.Content, T("游戏设置", "Settings"), ShowSettings);
            ui.Button(page.Content, T("意识碎片", "Memory journal"), ShowJournal);
            ui.Button(page.Content, T("操作与教学", "Controls & tutorials"), ShowHelp);
            ui.Button(page.Footer, T("重试本章", "Retry chapter"), () => Confirm(
                T("重新开始本章？", "Restart this chapter?"),
                T("本章尚未保存的变化会重置。", "Unsaved changes in this chapter will be reset."), () => LoadScene(lastScene)));
            ui.Button(page.Footer, T("返回主菜单", "Main menu"), () => Confirm(
                T("返回主菜单？", "Return to the menu?"),
                T("将尝试自动保存当前位置与旅程进度。", "Your position and journey progress will be saved automatically."),
                () => { if (SaveSnapshot(UIRunState.AutoSlot)) LoadScene(Config.menuScene); }));
            Finish(page);
        }

        public void ShowChapters()
        {
            UIPage page = Push(T("章节路径", "Chapter path"),
                T("沿着水流，一次抵达一个新的意识层。", "Follow the current, one layer of consciousness at a time."));
            page.Rebuild = ShowChapters;
            if (Config.chapters.Length == 0) ui.Label(page.Content, T("暂无章节", "No chapters yet"));
            for (int i = 0; i < Config.chapters.Length; i++)
            {
                int index = i; ChapterDefinition chapter = Config.chapters[i];
                bool completed = UIRunState.Progress.completed.Contains(chapter.scene);
                bool unlocked = UIRunState.IsUnlocked(Config, i);
                bool current = lastScene == chapter.scene;
                string state = completed ? T("已完成", "Completed") : current ? T("当前水域", "Current") :
                    unlocked ? T("可探索", "Available") : T("未解锁", "Locked");
                var card = ui.Column("Chapter " + i, page.Content, 20, 8);
                ui.Label(card, (i + 1).ToString("00") + "  ·  " + chapter.title + "  /  " + state, 28);
                ui.Label(card, chapter.description, 23);
                if (!unlocked) ui.Label(card, T("完成上一章后，这段水流将向你开放。", "Complete the previous chapter to open this path."), 21);
                ui.Button(card, unlocked ? T("进入这一章", "Enter chapter") : T("等待水流开启", "Awaiting unlock"), () =>
                {
                    Action enter = () => { SaveOnNextEntry = true; LoadScene(Config.chapters[index].scene); };
                    Confirm(chapter.title, T("进入后从本章起点开始。当前探索中的变化会重置。", "Enter from the chapter's starting point. Changes in the current exploration will be reset."), enter);
                }, current, unlocked && Application.CanStreamedLevelBeLoaded(chapter.scene));
                if (i < Config.chapters.Length - 1) ui.Label(page.Content, "↓", 28, Config.teal, TextAnchor.MiddleCenter);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void ShowSaves()
        {
            UIPage page = Push(T("旅程记录", "Journey records"),
                T("记录章节、位置与意识碎片。读取后将重新建立本章场景。", "Records keep chapters, position and memories. Loading rebuilds the chapter scene."));
            page.Rebuild = ShowSaves;
            for (int i = 0; i <= UIRunState.AutoSlot; i++)
            {
                int slot = i; SaveSlotData data = SaveSystem.LoadSlot(i);
                bool exists = SaveSystem.HasSlot(i), automatic = i == UIRunState.AutoSlot;
                string name = automatic ? T("自动记录", "Automatic record") : T("记录 ", "Record ") + (i + 1);
                RectTransform card = ui.Column(name, page.Content, 16, 8);
                ui.Label(card, name, 28);
                ui.Label(card, data == null ? (exists ? T("记录无法读取，可尝试恢复备份。", "This record is unreadable. Try restoring its backup.") :
                    T("这里还没有留下记录。", "No record here yet.")) : ChapterName(data.sceneName) + "  ·  " + data.SavedAtText, 22);
                if (data != null) ui.Label(card, T("意识碎片 ", "Memories ") + (data.runProgress != null ? data.runProgress.memories.Count : 0), 21);
                RectTransform actions = ui.Row("Record actions", card, 12);
                ui.Button(actions, T("读取", "Load"), () => Confirm(T("读取这份记录？", "Load this record?"),
                    T("当前尚未保存的探索会重置。", "Your unsaved exploration will be reset."), () => LoadScene(data.sceneName, data)),
                    false, data != null && Application.CanStreamedLevelBeLoaded(data.sceneName));
                if (IsGameplay && !automatic)
                    ui.Button(actions, T("保存", "Save"), () =>
                    {
                        Action save = () => { if (SaveSnapshot(slot)) RefreshSaves(page); };
                        if (exists) Confirm(T("覆盖这份记录？", "Overwrite this record?"),
                            T("原记录会保留为一份恢复备份。", "The previous record will be kept as a recovery backup."), save);
                        else save();
                    });
                if (SaveSystem.HasBackup(i)) ui.Button(actions, T("恢复备份", "Restore backup"), () => Confirm(
                    T("恢复上一份记录？", "Restore the previous record?"),
                    T("使用备份替换当前记录。损坏的内容会被保留。", "Replace this record with its backup. Damaged contents are preserved."),
                    () => { if (SaveSystem.RestoreBackup(slot)) { RefreshSaves(page); Toast(T("备份已恢复", "Backup restored")); } else Toast(T("恢复失败，请稍后再试。", "Recovery failed. Please retry.")); }));
                if (exists && !automatic) ui.Button(actions, T("删除", "Delete"), () => Confirm(
                    T("删除这份记录？", "Delete this record?"),
                    T("这份记录及其备份将被删除，此操作无法撤销。", "This record and its backup will be deleted. This cannot be undone."),
                    () => { if (SaveSystem.DeleteSlot(slot)) RefreshSaves(page); else Toast(T("删除失败，请稍后再试。", "Deletion failed. Please retry.")); }));
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        private void RefreshSaves(UIPage page)
        {
            if (pages.Count == 0 || pages[pages.Count - 1] != page) return;
            Pop(); ShowSaves();
        }

        private bool SaveSnapshot(int slot)
        {
            if (!IsGameplay || player == null) { Toast(T("没有可保存的玩家状态。", "There is no player state to save.")); return false; }
            if (switcher != null) UIRunState.Progress.bubbleType = (int)switcher.Current;
            bool saved = SaveSystem.SaveCurrentState(slot, player.transform, ChapterName(lastScene));
            Toast(saved ? T("已留下旅程记录", "Journey recorded") : T("保存失败，请检查存储空间后重试。", "Saving failed. Check storage space and retry."));
            return saved;
        }

        public bool SaveOnNextEntry { get; private set; }

        public void ShowSettings()
        {
            UISettingsData original = UISettings.Current.Copy(), draft = original.Copy();
            int originalQuality = QualitySettings.GetQualityLevel();
            UIPage page = Push(T("游戏设置", "Settings"),
                T("调整会即时预览，选择「应用」保存。返回将撤销本次调整。", "Changes preview immediately. Apply to save; Back cancels your changes."));
            bool committed = false;
            page.BeforeClose = () =>
            {
                if (committed) return;
                if (original.quality < 0) QualitySettings.SetQualityLevel(originalQuality);
                UISettings.Apply(original);
            };
            int tab = 0;
            RectTransform tabs = ui.Row("Settings tabs", page.Content, 12);
            RectTransform settingsBody = ui.Column("Settings body", page.Content, 8, 16);
            Action preview = () => UISettings.Apply(draft);
            Action refreshTab = null;
            refreshTab = () =>
            {
                foreach (Transform child in settingsBody) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                if (tab == 0)
                {
                    ui.Slider(settingsBody, T("总音量", "Master volume"), draft.master, 0, 1, v => { draft.master = v; preview(); });
                    ui.Slider(settingsBody, T("音乐", "Music"), draft.music, 0, 1, v => { draft.music = v; preview(); });
                    ui.Slider(settingsBody, T("音效", "Sound effects"), draft.effects, 0, 1, v => { draft.effects = v; preview(); });
                    ui.Toggle(settingsBody, T("声音文字提示", "Sound captions"), draft.soundCaptions, v => { draft.soundCaptions = v; preview(); });
                }
                else if (tab == 1)
                {
                    ui.Toggle(settingsBody, T("全屏", "Fullscreen"), draft.fullscreen, v => { draft.fullscreen = v; preview(); });
                    string quality = draft.quality < 0 ? T("保持当前", "Current") : QualitySettings.names[draft.quality];
                    ui.Button(settingsBody, T("画质", "Quality") + "  ·  " + quality, () =>
                    { draft.quality = (draft.quality + 1) % QualitySettings.names.Length; preview(); refreshTab(); });
                    ui.Toggle(settingsBody, T("高对比文字", "High contrast text"), draft.highContrast, v => { draft.highContrast = v; preview(); });
                }
                else if (tab == 2)
                {
                    ui.Slider(settingsBody, T("视角灵敏度", "Look sensitivity"), draft.sensitivity, 0.3f, 2, v => { draft.sensitivity = v; preview(); });
                    ui.Toggle(settingsBody, T("反转垂直视角", "Invert vertical look"), draft.invertLook, v => { draft.invertLook = v; preview(); });
                    ui.Toggle(settingsBody, T("点击蓄力，再次点击发射", "Click to charge, click again to release"), draft.toggleCharge, v => { draft.toggleCharge = v; preview(); });
                    string[] keyNames = { T("浮粘泡泡", "Sticky bubble"), T("弹力泡泡", "Bouncy bubble"), T("炸弹泡泡", "Bomb bubble"),
                        T("情境交互", "Context interaction"), T("意识日志", "Journal"), T("教学帮助", "Tutorial help") };
                    for (int i = 0; i < keyNames.Length; i++)
                    {
                        int action = i;
                        ui.Button(settingsBody, keyNames[i] + "  ·  " + draft.keys[i], () =>
                        {
                            UIPage prompt = Push(T("按下新的按键", "Press a new key"), T("Esc 取消，导航键保留用于界面操作。", "Esc cancels. Navigation keys are reserved for menus."), true);
                            prompt.BeforeClose = () => rebind = null;
                            rebind = key =>
                            {
                                int conflict = Array.IndexOf(draft.keys, key);
                                if (conflict >= 0) draft.keys[conflict] = draft.keys[action];
                                draft.keys[action] = key; preview(); refreshTab();
                            };
                            ui.Button(prompt.Footer, T("取消", "Cancel"), Pop); Finish(prompt);
                        });
                    }
                    ui.Label(settingsBody, T("WASD 移动 · 空格跳跃 · F 乘坐泡泡\n方向键导航 · Enter 确认 · Esc 返回\n手柄：方向导航 · A 确认 · B 返回 · LB/RB 切换", "WASD Move · Space Jump · F Ride bubble\nArrows Navigate · Enter Submit · Esc Back\nController: Navigate · A Submit · B Back · LB/RB Switch"), 22);
                }
                else
                {
                    ui.Slider(settingsBody, T("文字大小", "Text size"), draft.textScale, 1, 1.3f, v => { draft.textScale = v; preview(); });
                    ui.Toggle(settingsBody, T("减少动态效果", "Reduced motion"), draft.reducedMotion, v => { draft.reducedMotion = v; preview(); });
                    ui.Toggle(settingsBody, T("显示字幕", "Subtitles"), draft.subtitles, v => { draft.subtitles = v; preview(); });
                    ui.Toggle(settingsBody, T("情境教学", "Context tutorials"), draft.tutorials, v => { draft.tutorials = v; preview(); });
                    string[] languages = { "简体中文", "English", "［Pseudo-localization］" };
                    ui.Button(settingsBody, T("语言", "Language") + "  ·  " + languages[draft.language], () =>
                    { draft.language = (draft.language + 1) % 3; preview(); refreshTab(); });
                    ui.Button(settingsBody, T("重置已读教学", "Reset tutorials"), () => Confirm(T("重新显示教学？", "Show tutorials again?"),
                        T("后续探索中会再次显示首次提示。", "First-time hints will appear again during exploration."),
                        () => { UIRunState.Progress.tutorialsSeen.Clear(); Toast(T("教学提示已重置", "Tutorial hints reset")); }));
                }
                page.Refresh();
            };
            string[] tabNames = { T("声音", "Audio"), T("画面", "Video"), T("操作", "Controls"), T("辅助", "Accessibility") };
            for (int i = 0; i < tabNames.Length; i++)
            { int index = i; ui.Button(tabs, tabNames[i], () => { tab = index; refreshTab(); }); }
            refreshTab();
            ui.Button(page.Footer, T("返回", "Back"), Pop);
            ui.Button(page.Footer, T("恢复默认", "Restore defaults"), () => Confirm(T("恢复默认设置？", "Restore defaults?"),
                T("选择应用后才会保存。", "Changes are saved only after Apply."), () => { draft = new UISettingsData(); preview(); refreshTab(); }));
            ui.Button(page.Footer, T("应用", "Apply"), () =>
            {
                UISettings.Commit(draft); committed = true; Pop(); RefreshVisiblePage();
                Toast(T("设置已保存", "Settings saved"));
            }, true); Finish(page);
        }

        private void RefreshVisiblePage()
        {
            if (pages.Count == 0) { ClearHud(); BuildHud(); return; }
            UIPage top = pages[pages.Count - 1];
            if (top.Rebuild != null) { Action rebuild = top.Rebuild; Pop(); rebuild(); }
        }

        private string ChapterName(string scene)
        {
            int index = Config.IndexOf(scene);
            return index >= 0 ? Config.chapters[index].title : T("探索中的水域", "Exploration waters");
        }

        private void ShowCredits()
        {
            UIPage page = Push(T("制作人员", "Credits")); ui.Label(page.Content, Config.credits, 28);
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        private void ConfirmQuit()
        {
            Confirm(T("离开意识之海？", "Leave the sea?"), T("感谢你在这里留下涟漪。", "Thank you for leaving a ripple here."), () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }
    }
}
