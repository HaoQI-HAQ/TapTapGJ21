using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private void RequestNewRun()
        {
            Action begin = () =>
            {
                if (Config.chapters.Length == 0) { Toast(T("还没有可进入的章节。", "No chapters are available.")); return; }
                if (!Application.CanStreamedLevelBeLoaded(Config.chapters[0].scene))
                { Toast(T("第一章暂不可用，请稍后重试。", "The first chapter is unavailable. Please retry later.")); return; }
                UIRunState.NewRun(); SaveOnNextEntry = true; LoadScene(Config.chapters[0].scene);
            };
            if (ReadMenuRecords().latest != null || SaveSystem.HasSlot(UIRunState.AutoSlot) || SaveSystem.HasBackup(UIRunState.AutoSlot))
                Confirm(T("开启新的旅程？", "Start a new journey?"),
                    T("新的旅程会更新自动记录，手动记录会保留。需要保留当前自动记录时，请先继续旅程并存入手动槽位。", "The new journey updates the automatic record and keeps manual records. To keep your automatic record, continue that journey and save it to a manual slot first."), begin, true);
            else begin();
        }

        public void ShowPause()
        {
            if (!IsGameplay || IsBlockingGameplay) return;
            UIPage page = Push(T("在这里，停留片刻", "Take a moment"), ChapterName(lastScene), false, UIPageDesign.Pause);
            page.Rebuild = ShowPause;
            RectTransform rest = ui.Card("Resting current", page.Content, true);
            ui.Emblem(rest, UIGuideSymbol.Pause, T("水流在此停留", "THE CURRENT RESTS"));
            ui.IconButton(rest, T("继续探索", "Resume"), UIGuideSymbol.Arrow, Pop, true);
            RectTransform destinations = ui.Grid("Pause destinations", page.Content, 2, 360);
            ui.IconButton(destinations, T("章节路径", "Chapter path"), UIGuideSymbol.Goal, ShowChapters);
            ui.IconButton(destinations, T("保存与读取记录", "Save & load records"), UIGuideSymbol.Journal, ShowSaves);
            ui.IconButton(destinations, T("游戏设置", "Settings"), UIGuideSymbol.Steer, ShowSettings);
            ui.IconButton(destinations, T("意识碎片", "Memory journal"), UIGuideSymbol.Memory, ShowJournal);
            ui.IconButton(destinations, T("操作与教学", "Controls & tutorials"), UIGuideSymbol.Move, ShowHelp);
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
                T("沿着水流，一次抵达一个新的意识层。", "Follow the current, one layer of consciousness at a time."), false, UIPageDesign.Chapters);
            page.Rebuild = ShowChapters;
            if (Config.chapters.Length == 0) ui.Label(page.Content, T("暂无章节", "No chapters yet"));
            SaveSlotData latest = !IsGameplay ? ReadMenuRecords().latest : null;
            string currentScene = IsGameplay ? lastScene : latest != null ? latest.sceneName : "";
            int completedCount = 0;
            foreach (ChapterDefinition chapter in Config.chapters) if (UIRunState.Progress.completed.Contains(chapter.scene)) completedCount++;
            ui.Emblem(page.Content, UIGuideSymbol.Current, T("已完成水域 ", "Completed waters ") + completedCount + " / " + Config.chapters.Length);
            for (int i = 0; i < Config.chapters.Length; i++)
            {
                int index = i; ChapterDefinition chapter = Config.chapters[i];
                bool completed = UIRunState.Progress.completed.Contains(chapter.scene);
                bool unlocked = UIRunState.IsUnlocked(Config, i);
                bool current = currentScene == chapter.scene;
                bool available = Application.CanStreamedLevelBeLoaded(chapter.scene);
                string state = completed ? T("已完成", "Completed") : current ? T("当前水域", "Current") :
                    unlocked ? T("可探索", "Available") : T("未解锁", "Locked");
                RectTransform landmark = ui.Row("Chapter landmark " + i, page.Content, 24);
                Image node = ui.Rect("Route node " + i, landmark).gameObject.AddComponent<UICircleImage>();
                node.color = current ? UIDesignPalette.Gold : completed ? new Color32(213, 237, 222, 255) : UIDesignPalette.Cream;
                UIWidgets.Size(node.transform, 104, 104);
                Text number = ui.Label(node.transform, (i + 1).ToString("00"), 36, Config.ink, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(number.rectTransform);
                var card = ui.Card("Chapter " + i, landmark, current);
                UIWidgets.Size(card, -1, -1, 1);
                StyleRecordCard(card, current);
                ui.Label(card, (i + 1).ToString("00") + "  ·  " + chapter.title + "  /  " + state, 28);
                ui.Label(card, chapter.description, 23);
                if (!unlocked) ui.Label(card, T("完成上一章后，这段水流将向你开放。", "Complete the previous chapter to open this path."), 21);
                else if (!available) ui.Label(card, T("这段水域暂不可进入，请选择其他已开放章节。", "These waters are temporarily unavailable. Choose another open chapter."), 21);
                ui.Button(card, !unlocked ? T("等待水流开启", "Awaiting unlock") : !available ? T("暂不可进入", "Temporarily unavailable") : T("进入这一章", "Enter chapter"), () =>
                {
                    Action enter = () => { SaveOnNextEntry = true; LoadScene(Config.chapters[index].scene); };
                    Confirm(chapter.title, T("进入后从本章起点开始。当前探索中的变化会重置。", "Enter from the chapter's starting point. Changes in the current exploration will be reset."), enter);
                }, current, unlocked && available);
                if (i < Config.chapters.Length - 1)
                {
                    RectTransform connector = ui.Rect("Route connector", page.Content); UIWidgets.Size(connector, 24);
                    Image line = ui.Surface("Current line", connector, Config.teal); line.raycastTarget = false;
                    line.rectTransform.anchorMin = line.rectTransform.anchorMax = new Vector2(0, .5f);
                    line.rectTransform.anchoredPosition = new Vector2(52, 0); line.rectTransform.sizeDelta = new Vector2(2, 24);
                }
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void ShowSaves()
        {
            UIPage page = Push(T("旅程记录", "Journey records"),
                T("记录章节、位置与意识碎片。", "Keep a chapter, a place and a memory."), false, UIPageDesign.Records);
            page.Rebuild = ShowSaves;
            RectTransform records = ui.Grid("Record shelf", page.Content, 2, 440);
            for (int i = 0; i <= UIRunState.AutoSlot; i++)
            {
                int slot = i; SaveSlotData data = SaveSystem.LoadSlot(i);
                bool exists = SaveSystem.HasSlot(i), automatic = i == UIRunState.AutoSlot;
                string name = automatic ? T("自动记录", "Automatic record") : T("记录 ", "Record ") + (i + 1);
                RectTransform card = ui.Card(name, records, automatic);
                StyleRecordCard(card, automatic);
                ui.Emblem(card, automatic ? UIGuideSymbol.Current : UIGuideSymbol.Journal, name);
                ui.Label(card, data == null ? (exists ? T("记录无法读取，可尝试恢复备份。", "This record is unreadable. Try restoring its backup.") :
                    T("这里还没有留下记录。", "No record here yet.")) : ChapterName(data.sceneName) + "  ·  " + data.SavedAtText, 22);
                if (data != null) ui.Label(card, T("意识碎片 ", "Memories ") + (data.runProgress != null ? data.runProgress.memories.Count : 0), 21);
                if (data != null && !Application.CanStreamedLevelBeLoaded(data.sceneName))
                    ui.Label(card, T("记录所在的水域暂不可用。记录仍会保留。", "The recorded waters are unavailable. Your record is preserved."), 21);
                if (automatic) ui.Label(card, T("章节开始、完成及返回主菜单时会更新。", "Updated when entering or completing a chapter and returning to the menu."), 18);
                RectTransform actions = ui.Grid("Record actions", card, 2, 140);
                ui.Button(actions, T("读取", "Load"), () => Confirm(T("读取这份记录？", "Load this record?"),
                    T("当前尚未保存的探索会重置。", "Your unsaved exploration will be reset."), () => LoadScene(data.sceneName, data)),
                    false, data != null && Application.CanStreamedLevelBeLoaded(data.sceneName));
                if (IsGameplay && !automatic)
                    ui.Button(actions, T("保存", "Save"), () =>
                    {
                        Action save = () => { if (SaveSnapshot(slot)) RefreshSaves(page); };
                        if (exists) Confirm(T("覆盖这份记录？", "Overwrite this record?"),
                            T("原记录会保留为一份恢复备份。", "The previous record will be kept as a recovery backup."), save, true);
                        else save();
                    });
                if (SaveSystem.HasBackup(i)) ui.Button(actions, T("恢复备份", "Restore backup"), () => Confirm(
                    T("恢复上一份记录？", "Restore the previous record?"),
                    T("使用备份替换当前记录。损坏的内容会被保留。", "Replace this record with its backup. Damaged contents are preserved."),
                    () => { if (SaveSystem.RestoreBackup(slot)) { RefreshSaves(page); Toast(T("备份已恢复", "Backup restored")); } else Toast(T("恢复失败，请稍后再试。", "Recovery failed. Please retry.")); }));
                if (exists && !automatic) ui.Button(actions, T("删除", "Delete"), () => Confirm(
                    T("删除这份记录？", "Delete this record?"),
                    T("这份记录及其备份将被删除，此操作无法撤销。", "This record and its backup will be deleted. This cannot be undone."),
                    () => { if (SaveSystem.DeleteSlot(slot)) RefreshSaves(page); else Toast(T("删除失败，请稍后再试。", "Deletion failed. Please retry.")); }, true), false, true, true);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        private void RefreshSaves(UIPage page)
        {
            if (pages.Count == 0 || pages[pages.Count - 1] != page) return;
            Pop(); ShowSaves();
        }

        private void StyleRecordCard(RectTransform card, bool highlighted)
        {
            Image surface = card.GetComponent<Image>(); if (surface == null) surface = card.gameObject.AddComponent<Image>();
            surface.color = highlighted ? Config.mist : UIDesignPalette.ControlTop;
            surface.sprite = Config.round; surface.type = Image.Type.Sliced; surface.raycastTarget = false;
            if (!highlighted) return;
            Image accent = ui.Surface("Current record accent", card, Config.teal);
            accent.raycastTarget = false; accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            accent.rectTransform.anchorMin = Vector2.zero; accent.rectTransform.anchorMax = Vector2.up;
            accent.rectTransform.offsetMin = new Vector2(0, 16); accent.rectTransform.offsetMax = new Vector2(4, -16);
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

        private void RefreshVisiblePage()
        {
            if (pages.Count == 0) { ClearHud(); BuildHud(); return; }
            UIPage top = pages[pages.Count - 1];
            if (top.Rebuild != null)
            {
                GameObject focus = UnityEngine.EventSystems.EventSystem.current != null ?
                    UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
                string name = focus != null && focus.transform.IsChildOf(top.transform) ? focus.name : null;
                Action rebuild = top.Rebuild; Pop(); rebuild();
                if (name != null && pages.Count > 0)
                    foreach (Selectable item in pages[pages.Count - 1].GetComponentsInChildren<Selectable>())
                        if (item.name == name && item.IsInteractable()) { item.Select(); break; }
            }
        }

        private string ChapterName(string scene)
        {
            int index = Config.IndexOf(scene);
            return index >= 0 ? Config.chapters[index].title : T("探索中的水域", "Exploration waters");
        }

        private void ShowCredits()
        {
            UIPage page = Push(T("制作人员", "Credits"), T("每一段水流，都来自共同的创作。", "Every current begins with a shared creation."), false, UIPageDesign.Credits);
            page.Rebuild = ShowCredits;
            RectTransform card = ui.Card("Credits card", page.Content);
            ui.Emblem(card, UIGuideSymbol.Current, T("共同留下的回响", "CREATED TOGETHER"));
            ui.Label(card, string.IsNullOrWhiteSpace(Config.credits) ?
                T("感谢每一位参与创作与体验的朋友。", "Thank you to everyone who created and explored this world.") : Config.credits, 26);
            ui.Label(page.Content, T("感谢你在意识之海留下涟漪。", "Thank you for leaving a ripple in the sea of consciousness."), 22);
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
            }, true);
        }
    }
}
