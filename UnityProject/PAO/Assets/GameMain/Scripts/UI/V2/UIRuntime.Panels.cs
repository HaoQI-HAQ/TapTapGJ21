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
            AutoSaveQuietly();
            UIPage page = Push(T("暂停", "PAUSED"), "", false, UIPageDesign.Pause);
            page.Rebuild = ShowPause;
            RectTransform body = (RectTransform)page.transform.Find("Page");
            body.GetComponent<VerticalLayoutGroup>().enabled = false;
            UIWidgets.Stretch(body);
            body.GetChild(0).gameObject.SetActive(false);
            RectTransform panel = page.Content.GetComponentInParent<ScrollRect>().GetComponent<RectTransform>();
            float availableWidth = Mathf.Max(1, safeRoot.rect.width);
            float panelWidth = Mathf.Min(Mathf.Max(1, availableWidth - 12),
                Mathf.Clamp(availableWidth * .45f, 420, 500));
            bool compact = availableWidth < 760 || safeRoot.rect.height < 700;
            panel.anchorMin = new Vector2(1 - panelWidth / availableWidth, 0);
            panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Image sheet = panel.GetComponent<Image>(); sheet.color = Color.white;
            Outline edge = panel.gameObject.AddComponent<Outline>(); edge.effectColor = Color.black; edge.effectDistance = Vector2.one;
            VerticalLayoutGroup list = page.Content.GetComponent<VerticalLayoutGroup>();
            list.padding = compact ? new RectOffset(18, 18, 104, 18) : new RectOffset(26, 26, 168, 34);
            list.spacing = compact ? 6 : 10;
            PauseAction(page.Content, 1, T("继续", "Resume"), ClosePause, compact);
            PauseAction(page.Content, 2, T("地图", "Map"), ShowChapters, compact);
            PauseAction(page.Content, 3, T("重开", "Restart"), () => Confirm(
                T("重新开始本章？", "Restart this chapter?"),
                T("本章尚未保存的变化会重置。", "Unsaved changes in this chapter will be reset."), () => LoadScene(lastScene)), compact);
            PauseAction(page.Content, 4, T("读档", "Load game"), ShowSaves, compact);
            PauseAction(page.Content, 5, T("设置", "Settings"), ShowSettings, compact);
            PauseAction(page.Content, 6, T("主菜单", "Main menu"), () => Confirm(
                T("返回主菜单？", "Return to the main menu?"),
                T("将保存当前位置与旅程进度。", "Your location and journey progress will be saved."),
                () => { if (SaveSnapshot(UIRunState.AutoSlot)) LoadScene(Config.menuScene); }), compact);
            page.Footer.gameObject.SetActive(false);
            Text title = ui.Label(page.transform, T("暂停", "PAUSED"), 94, Color.black, TextAnchor.MiddleCenter);
            title.name = "Pause overlay title"; title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, new Vector2(.5f, .91f), Vector2.zero, new Vector2(720, 138), new Vector2(.5f, .5f));
            title.transform.SetAsLastSibling();
            panel.gameObject.AddComponent<UIPauseReveal>().Configure();
            Finish(page);
        }

        private void PauseAction(Transform parent, int index, string label, Action action, bool compact)
        {
            Button button = ui.Button(parent, label, action);
            button.gameObject.AddComponent<UIPauseActionStyle>().Configure(ui, Config, index, compact);
        }

        private void ClosePause()
        {
            if (pages.Count == 0 || pages[pages.Count - 1].Design != UIPageDesign.Pause) return;
            UIPauseReveal reveal = pages[pages.Count - 1].GetComponentInChildren<UIPauseReveal>();
            if (reveal != null) reveal.Close(Pop);
            else Pop();
        }

        private void AutoSaveQuietly()
        {
            if (!IsGameplay || player == null || loading) return;
            if (switcher != null) UIRunState.Progress.bubbleType = (int)switcher.Current;
            SaveSystem.SaveCurrentState(UIRunState.AutoSlot, player.transform, ChapterName(lastScene));
        }

        public void ShowChapters()
        {
            UIPage page = Push(T("章节地图", "Chapter map"), "", false, UIPageDesign.Chapters);
            page.Rebuild = ShowChapters;
            BuildChapterAtlas(page.Content);
            ui.Button(page.Footer, T("返回", "Back"), Pop);
            Finish(page);
        }
        private Action<SaveSlotData> BuildChapterAtlas(Transform parent, bool recordPreview = false)
        {
            if (Config.chapters == null || Config.chapters.Length == 0)
            {
                ui.Label(parent, T("暂无可浏览的章节", "No chapters available"));
                return null;
            }
            RectTransform atlas = ui.Card("Chapter atlas", parent);
            ui.Emblem(atlas, UIGuideSymbol.Goal, T("航线图册", "ROUTE ATLAS"));
            RectTransform viewport = ui.Rect("Horizontal chapter map", atlas);
            // The route is the main content of this page: each stop reads as a full
            // stage card at 1920x1080 rather than a small chapter tab.
            UIWidgets.Size(viewport, 560);
            Image paper = viewport.gameObject.AddComponent<Image>(); paper.color = Color.white;
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<UIHorizontalScrollRect>();
            scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36; scroll.viewport = viewport;
            RectTransform strip = ui.Row("Chapter tiles", viewport, 22);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, .5f); strip.anchoredPosition = Vector2.zero;
            ContentSizeFitter fitter = strip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = strip;
            Image route = ui.Surface("Drawn route", strip, Color.black);
            route.raycastTarget = false;
            route.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            route.rectTransform.anchorMin = new Vector2(0, .5f);
            route.rectTransform.anchorMax = new Vector2(1, .5f);
            route.rectTransform.offsetMin = new Vector2(0, -1);
            route.rectTransform.offsetMax = new Vector2(0, 1);
            SaveSlotData latest = IsGameplay ? null : ReadMenuRecords().latest;
            SaveSlotData previewRecord = recordPreview ? latest : null;
            RunProgress previewProgress = previewRecord != null && previewRecord.runProgress != null ?
                previewRecord.runProgress : UIRunState.Progress;
            string currentScene = IsGameplay ? lastScene : latest != null ? latest.sceneName : "";
            int selected = Mathf.Max(0, Config.IndexOf(currentScene));
            Button[] tiles = new Button[Config.chapters.Length];
            Action redraw = () =>
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    if (tiles[i] == null) continue;
                    ChapterDefinition chapter = Config.chapters[i];
                    bool completed = previewProgress.completed.Contains(chapter.scene);
                    bool unlocked = i == 0 || previewProgress.completed.Contains(Config.chapters[i - 1].scene);
                    bool available = Application.CanStreamedLevelBeLoaded(chapter.scene);
                    Text state = tiles[i].transform.Find("Stage status").GetComponent<Text>();
                    state.text = completed ? T("已完成", "COMPLETED") : currentScene == chapter.scene ?
                        T("当前章节", "CURRENT") : unlocked && available ? T("已开放", "AVAILABLE") :
                        T("未解锁", "LOCKED");
                    tiles[i].GetComponent<Outline>().effectDistance = i == selected ? new Vector2(3, 3) : Vector2.one;
                    tiles[i].GetComponent<UIStageCardMotion>().SetCurrent(i == selected);
                }
            };
            for (int i = 0; i < Config.chapters.Length; i++)
            {
                int index = i;
                ChapterDefinition chapter = Config.chapters[i];
                Button tile = CreateChapterTile(strip, index, chapter, () =>
                {
                    selected = index; redraw();
                    ShowChapterInfo(index, previewProgress, currentScene, recordPreview);
                });
                tiles[i] = tile;
            }
            redraw();
            if (!recordPreview) return null;
            return data =>
            {
                if (data == null) return;
                previewProgress = data.runProgress ?? new RunProgress();
                currentScene = data.sceneName;
                selected = Mathf.Max(0, Config.IndexOf(currentScene));
                redraw();
                if (tiles[selected] != null && tiles[selected].IsInteractable())
                    tiles[selected].Select();
            };
        }

        private Button CreateChapterTile(Transform strip, int index, ChapterDefinition chapter, Action open)
        {
            Button tile = ui.Button(strip, "", open);
            tile.name = "Stage " + (index + 1).ToString("00");
            // The card occupies a full atlas leaf (390 x 540 design units), rather
            // than the height of a chapter tab. Details live in the dossier popup.
            UIWidgets.Size(tile.transform, 540, 390);
            Image surface = tile.GetComponent<Image>(); surface.color = Color.white; surface.sprite = null;
            tile.GetComponent<UIFluidGradient>().enabled = false;
            tile.GetComponent<UIButtonFeedback>().enabled = false;
            tile.GetComponent<UIAdaptiveButton>().enabled = false;
            UIControlAccent defaultAccent = tile.GetComponent<UIControlAccent>();
            if (defaultAccent != null) defaultAccent.enabled = false;
            tile.transition = Selectable.Transition.None;
            Image masthead = ui.Surface("Stage masthead", tile.transform, Color.black);
            masthead.raycastTarget = false;
            masthead.rectTransform.anchorMin = new Vector2(0, 1);
            masthead.rectTransform.anchorMax = Vector2.one;
            masthead.rectTransform.offsetMin = new Vector2(0, -48);
            masthead.rectTransform.offsetMax = Vector2.zero;
            Text mastheadLabel = ui.Label(tile.transform, "PAO  /  OPERATION  " + (index + 1).ToString("00"),
                17, Color.white, TextAnchor.MiddleLeft);
            Anchor(mastheadLabel.rectTransform, new Vector2(0, 1), new Vector2(20, -46), new Vector2(320, 44), new Vector2(0, 1));
            Text number = tile.transform.Find("Text").GetComponent<Text>();
            number.text = (index + 1).ToString("00"); number.fontStyle = FontStyle.Bold;
            number.GetComponent<UIStyledText>().Configure(122, false, Config);
            number.alignment = TextAnchor.UpperLeft;
            number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0, 1);
            number.rectTransform.pivot = new Vector2(0, 1);
            number.rectTransform.anchoredPosition = new Vector2(22, -69);
            number.rectTransform.sizeDelta = new Vector2(240, 154);
            UIStageMapGraphic chart = ui.Rect("Chapter chart", tile.transform).gameObject.AddComponent<UIStageMapGraphic>();
            chart.Variant = index; chart.color = Color.black; chart.raycastTarget = false;
            chart.rectTransform.anchorMin = new Vector2(0, .29f);
            chart.rectTransform.anchorMax = new Vector2(1, .63f);
            chart.rectTransform.offsetMin = new Vector2(18, 0);
            chart.rectTransform.offsetMax = new Vector2(-18, 0);
            Text title = ui.Label(tile.transform, chapter.title, 32, Color.black);
            title.fontStyle = FontStyle.Bold;
            Anchor(title.rectTransform, Vector2.zero, new Vector2(24, 93), new Vector2(334, 76), Vector2.zero);
            Image foot = ui.Surface("Stage foot", tile.transform, Color.black);
            foot.raycastTarget = false;
            foot.rectTransform.anchorMin = Vector2.zero;
            foot.rectTransform.anchorMax = new Vector2(1, 0);
            foot.rectTransform.offsetMin = Vector2.zero;
            foot.rectTransform.offsetMax = new Vector2(0, 64);
            Text state = ui.Label(tile.transform, "", 18, Color.white);
            state.name = "Stage status";
            Anchor(state.rectTransform, Vector2.zero, new Vector2(24, 10), new Vector2(246, 44), Vector2.zero);
            Image divider = ui.Surface("Stage rule", tile.transform, Color.black);
            divider.raycastTarget = false;
            Anchor(divider.rectTransform, Vector2.zero, new Vector2(24, 92), new Vector2(342, 2), Vector2.zero);
            Image bar = ui.Surface("Selection bar", tile.transform, Color.black);
            bar.raycastTarget = false;
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.offsetMin = Vector2.zero;
            bar.rectTransform.offsetMax = new Vector2(7, 0);
            Text indexMark = ui.Label(tile.transform, "↗", 28, Color.white, TextAnchor.MiddleRight);
            Anchor(indexMark.rectTransform, Vector2.zero, new Vector2(316, 10), new Vector2(50, 44), Vector2.zero);
            Image shutter = ui.Surface("Stage reveal", tile.transform, Color.black);
            shutter.raycastTarget = false;
            shutter.rectTransform.anchorMin = new Vector2(1, 0);
            shutter.rectTransform.anchorMax = Vector2.one;
            shutter.rectTransform.pivot = new Vector2(1, .5f);
            shutter.rectTransform.sizeDelta = new Vector2(390, 0);
            shutter.rectTransform.anchoredPosition = Vector2.zero;
            tile.gameObject.AddComponent<UIStageCardMotion>().Configure(bar, shutter.rectTransform, index);
            return tile;
        }

        private void ShowChapterInfo(int index, RunProgress progress, string currentScene, bool recordPreview)
        {
            ChapterDefinition chapter = Config.chapters[index];
            bool completed = progress.completed.Contains(chapter.scene);
            bool unlocked = index == 0 || progress.completed.Contains(Config.chapters[index - 1].scene);
            bool available = Application.CanStreamedLevelBeLoaded(chapter.scene);
            UIPage page = Push((index + 1).ToString("00") + "  /  " + chapter.title, "", true, UIPageDesign.Confirmation);
            RectTransform body = (RectTransform)page.transform.Find("Page");
            UICompactPageLayout automaticLayout = body.GetComponent<UICompactPageLayout>();
            if (automaticLayout != null) automaticLayout.enabled = false;
            bool narrow = safeRoot.rect.width < 1300;
            body.anchorMin = narrow ? new Vector2(.06f, .07f) : new Vector2(.17f, .11f);
            body.anchorMax = narrow ? new Vector2(.94f, .93f) : new Vector2(.83f, .89f);
            body.offsetMin = body.offsetMax = Vector2.zero;
            Image dossierPaper = body.GetComponent<Image>(); dossierPaper.sprite = null; dossierPaper.color = Color.white;
            Outline frame = body.gameObject.AddComponent<Outline>();
            frame.effectColor = Color.black; frame.effectDistance = new Vector2(2, 2);
            VerticalLayoutGroup dossierLayout = body.GetComponent<VerticalLayoutGroup>();
            dossierLayout.padding = new RectOffset(36, 36, 34, 28);
            dossierLayout.spacing = 14;
            Text heading = body.Find("Page title").GetComponent<Text>();
            heading.fontStyle = FontStyle.Bold;
            heading.GetComponent<UIStyledText>().Configure(50, false, Config);
            ui.Emblem(page.Content, UIGuideSymbol.Goal, T("航线档案", "STAGE DOSSIER"), Color.black);
            RectTransform chartSheet = ui.Rect("Stage dossier chart", page.Content);
            UIWidgets.Size(chartSheet, narrow ? 178 : 234);
            Image chartPaper = chartSheet.gameObject.AddComponent<Image>(); chartPaper.color = Color.white; chartPaper.raycastTarget = false;
            Outline chartFrame = chartSheet.gameObject.AddComponent<Outline>();
            chartFrame.effectColor = Color.black; chartFrame.effectDistance = Vector2.one;
            UIStageMapGraphic preview = ui.Rect("Chapter map drawing", chartSheet).gameObject.AddComponent<UIStageMapGraphic>();
            preview.Variant = index; preview.color = Color.black; preview.raycastTarget = false;
            UIWidgets.Stretch(preview.rectTransform, 10);
            ui.Label(page.Content, chapter.description, 27, Color.black);
            if (!string.IsNullOrWhiteSpace(chapter.objective))
            {
                RectTransform mission = ui.Card("Chapter objective", page.Content);
                ui.Label(mission, T("本章目标", "OBJECTIVE"), 19, Color.black);
                ui.Label(mission, chapter.objective, 24, Color.black);
            }
            string state = completed ? T("已完成", "Completed") : currentScene == chapter.scene ?
                T("当前章节", "Current chapter") : unlocked ? T("已开放", "Available") : T("未解锁", "Locked");
            if (!available) state += T(" · 暂不可进入", " · unavailable");
            ui.Label(page.Content, T("航线状态  /  ", "ROUTE STATUS  /  ") + state, 22, Color.black);
            if (!recordPreview)
                ui.Button(page.Footer, T("进入本章", "Enter stage"), () => Confirm(chapter.title,
                    T("从本章起点进入。当前未保存的探索会重置。", "Enter from the chapter start. Unsaved exploration will reset."),
                    () => { SaveOnNextEntry = true; LoadScene(chapter.scene); }), false, unlocked && available);
            ui.Button(page.Footer, T("返回地图", "Back to map"), Pop);
            body.gameObject.AddComponent<UIStageDossierMotion>().Configure(chartSheet);
            Finish(page);
        }

        public void ShowSaves()
        {
            UIPage page = Push(T("旅程记录", "Journey records"),
                T("按章节浏览旅程，并选择要读取或写入的记录。", "Browse the route and choose a journey record."), false, UIPageDesign.Records);
            page.Rebuild = ShowSaves;
            Action<SaveSlotData> preview = BuildChapterAtlas(page.Content, true);
            RectTransform records = ui.Grid("Record shelf", page.Content, 1, 720);
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
                if (data != null) ui.Label(card, T("游玩时间 ", "Play time ") +
                    TimeSpan.FromSeconds(data.runProgress != null ? data.runProgress.playSeconds : 0).ToString(@"hh\:mm\:ss") +
                    T("  ·  意识碎片 ", "  ·  Memories ") + (data.runProgress != null ? data.runProgress.memories.Count : 0), 21);
                if (data != null && !Application.CanStreamedLevelBeLoaded(data.sceneName))
                    ui.Label(card, T("记录所在的水域暂不可用。记录仍会保留。", "The recorded waters are unavailable. Your record is preserved."), 21);
                if (automatic) ui.Label(card, T("探索中、暂停时及完成章节后自动更新。", "Updated during exploration, on pause and after a chapter."), 18);
                RectTransform actions = ui.Grid("Record actions", card, 2, 140);
                if (data != null && preview != null)
                    ui.Button(actions, T("查看章节进度", "Preview route"), () =>
                    {
                        preview(data);
                        page.Content.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
                    });
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
            if (scene == "cs") return T("机制测试场", "Mechanics test ground");
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
