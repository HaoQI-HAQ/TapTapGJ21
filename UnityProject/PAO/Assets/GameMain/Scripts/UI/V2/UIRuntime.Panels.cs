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
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { ShowSandboxPause(); return; }
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
            // 标题框高度是写死的 138，但字号会乘上设置里的「文字大小」（textScale）。
            // textScale 一调大（比如 1.2），94 号字变成 114 号，行高约 140 就超过 138，
            // 而 Label 默认 verticalOverflow = Truncate —— 整行会被裁掉，标题直接消失。
            // 中文比拉丁字母更容易撞上这条线，看起来就像「中文标题没字体」。
            // 所以标题必须允许溢出绘制，高度也不写死。
            title.verticalOverflow = VerticalWrapMode.Overflow;
            Anchor(title.rectTransform, new Vector2(.5f, .91f), Vector2.zero, new Vector2(720, 180), new Vector2(.5f, .5f));
            title.transform.SetAsLastSibling();
            // 提示条（Toast）显式摆到左下角。
            // 暂停页把 Page 的 VerticalLayoutGroup 关掉了，凡是靠它排版的子物体都不会
            // 再被摆放 —— NoticeArea 会停在 RectTransform 默认的 100×100、屏幕正中央，
            // 提示文字被这个尺寸挤成两行还截断，看起来就是一个压在角色身上的怪弹窗。
            // 其余页面仍然交给布局组排版，这里只处理暂停页。
            RectTransform noticeArea = page.NoticeArea;
            if (noticeArea != null)
            {
                noticeArea.anchorMin = noticeArea.anchorMax = Vector2.zero;
                noticeArea.pivot = Vector2.zero;
                noticeArea.anchoredPosition = new Vector2(32f, 32f);
                noticeArea.sizeDelta = new Vector2(Mathf.Min(560f, Mathf.Max(240f, safeRoot.rect.width - 64f)), 72f);
            }
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
            if (PAO.Sandbox.BubbleSandbox.Instance != null)
            {
                if (PAO.Sandbox.BubbleSandbox.Instance.restoreSavedWorld) PAO.Sandbox.BubbleSandbox.Instance.SaveWorld();
                return;
            }
            if (!IsGameplay || player == null || loading) return;
            if (switcher != null) UIRunState.Progress.bubbleType = (int)switcher.Current;
            SaveSystem.SaveCurrentState(UIRunState.AutoSlot, player.transform, ChapterName(lastScene));
        }

        public void ShowChapters()
        {
            UIPage page = Push(T("章节地图", "Chapter map"), "", false, UIPageDesign.Chapters);
            page.Rebuild = ShowChapters;
            BuildStageRoute(page.Content);
            ui.Button(page.Footer, T("返回", "Back"), Pop);
            Finish(page);
            if (UISettings.Current.reducedMotion && Config.chapters != null && Config.chapters.Length > 0)
            {
                SaveSlotData latest = IsGameplay ? null : ReadMenuRecords().latest;
                string scene = IsGameplay ? lastScene : latest != null ? latest.sceneName : "";
                string target = "Stage " + (Mathf.Max(0, Config.IndexOf(scene)) + 1).ToString("00");
                foreach (Button button in page.Content.GetComponentsInChildren<Button>())
                    if (button.name == target) { button.Select(); break; }
            }
        }
        private Action<SaveSlotData> BuildChapterAtlas(Transform parent, bool recordPreview = false)
        {
            if (Config.chapters == null || Config.chapters.Length == 0)
            {
                ui.Label(parent, T("暂无可浏览的章节", "No chapters available"));
                return null;
            }
            RectTransform atlas = ui.Card("Chapter atlas", parent);
            atlas.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(16, 16, 24, 24);
            RectTransform navigation = ui.Row("Route navigation", atlas, 12);
            UIWidgets.Size(navigation, 48);
            Text routeTitle = ui.Label(navigation, T("航线图册", "ROUTE ATLAS"), 22, Color.black);
            routeTitle.fontStyle = FontStyle.Bold;
            UIWidgets.Size(routeTitle.transform, 44, 170);
            Text routeHint = ui.Label(navigation, "", 19, Color.black);
            routeHint.resizeTextForBestFit = true;
            routeHint.resizeTextMinSize = 15;
            routeHint.resizeTextMaxSize = 19;
            UIWidgets.Size(routeHint.transform, 44, -1, 1);
            Text routeCounter = ui.Label(navigation, "", 20, Color.black, TextAnchor.MiddleRight);
            routeCounter.resizeTextForBestFit = true;
            routeCounter.resizeTextMinSize = 15;
            routeCounter.resizeTextMaxSize = 20;
            UIWidgets.Size(routeCounter.transform, 44, 112);
            RectTransform viewport = ui.Rect("Horizontal chapter map", atlas);
            // Full atlas leaves remain large enough to read at 1920 x 1080.
            // Further stops remain reachable by dragging, the wheel, or arrows.
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
            Button previous = null, next = null;
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
                    state.text = !available ? T("暂不可进入", "UNAVAILABLE") : completed ? T("已完成", "COMPLETED") : currentScene == chapter.scene ?
                        T("当前章节", "CURRENT") : unlocked && available ? T("已开放", "AVAILABLE") :
                        T("未解锁", "LOCKED");
                    tiles[i].GetComponent<Outline>().effectDistance = i == selected ? new Vector2(3, 3) : Vector2.one;
                    tiles[i].GetComponent<UIStageCardMotion>().SetCurrent(i == selected);
                }
                ChapterDefinition chosen = Config.chapters[selected];
                bool chosenCompleted = previewProgress.completed.Contains(chosen.scene);
                bool chosenUnlocked = selected == 0 || previewProgress.completed.Contains(Config.chapters[selected - 1].scene);
                bool chosenAvailable = Application.CanStreamedLevelBeLoaded(chosen.scene);
                string chosenStatus = chosenCompleted ? T("已完成", "Completed") : currentScene == chosen.scene ?
                    T("当前章节", "Current") : chosenUnlocked && chosenAvailable ? T("已开放", "Available") :
                    T("未解锁", "Locked");
                if (!chosenAvailable) chosenStatus = T("暂不可进入", "Unavailable");
                routeHint.text = safeRoot.rect.width < 1000 ? "" : chosen.title + "  /  " + chosenStatus;
                routeCounter.text = (selected + 1).ToString("00") + " / " + tiles.Length.ToString("00");
                if (previous != null) previous.interactable = selected > 0;
                if (next != null) next.interactable = selected + 1 < tiles.Length;
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
            // Small junction marks keep the route legible between atlas leaves.
            for (int i = 0; i + 1 < tiles.Length; i++)
            {
                Image junction = ui.Surface("Route junction " + (i + 1), strip, Color.black);
                junction.raycastTarget = false;
                junction.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                junction.rectTransform.anchorMin = junction.rectTransform.anchorMax = new Vector2(0, .5f);
                junction.rectTransform.pivot = new Vector2(.5f, .5f);
                junction.rectTransform.anchoredPosition = new Vector2(401 + i * 412, 0);
                junction.rectTransform.sizeDelta = new Vector2(9, 9);
                junction.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            }
            Action<int> locate = index =>
            {
                selected = Mathf.Clamp(index, 0, tiles.Length - 1);
                redraw();
                Canvas.ForceUpdateCanvases();
                scroll.horizontalNormalizedPosition = tiles.Length <= 1 ? 0 : selected / (float)(tiles.Length - 1);
                if (tiles[selected] != null && tiles[selected].IsInteractable()) tiles[selected].Select();
            };
            previous = ui.Button(navigation, "‹", () => locate(selected - 1));
            next = ui.Button(navigation, "›", () => locate(selected + 1));
            foreach (Button arrow in new[] { previous, next })
            {
                UIAdaptiveButton adaptive = arrow.GetComponent<UIAdaptiveButton>();
                if (adaptive != null) adaptive.enabled = false;
                UIButtonFeedback feedback = arrow.GetComponent<UIButtonFeedback>();
                if (feedback != null) feedback.enabled = false;
                UIControlAccent accent = arrow.GetComponent<UIControlAccent>();
                if (accent != null) accent.enabled = false;
                UIFluidGradient gradient = arrow.GetComponent<UIFluidGradient>();
                if (gradient != null) gradient.enabled = false;
                ColorBlock colors = arrow.colors;
                colors.highlightedColor = colors.selectedColor = new Color(.92f, .92f, .92f);
                colors.pressedColor = new Color(.78f, .78f, .78f);
                colors.disabledColor = new Color(.86f, .86f, .86f);
                arrow.colors = colors;
                UIWidgets.Size(arrow.transform, 44, 56);
                Text glyph = arrow.GetComponentInChildren<Text>();
                UIWidgets.Stretch(glyph.rectTransform);
                glyph.fontSize = 28;
            }
            redraw();
            Canvas.ForceUpdateCanvases();
            scroll.horizontalNormalizedPosition = tiles.Length <= 1 ? 0 : selected / (float)(tiles.Length - 1);
            if (!recordPreview) return null;
            return data =>
            {
                if (data == null) return;
                previewProgress = data.runProgress ?? new RunProgress();
                currentScene = data.sceneName;
                selected = Mathf.Max(0, Config.IndexOf(currentScene));
                redraw();
                Canvas.ForceUpdateCanvases();
                scroll.horizontalNormalizedPosition = tiles.Length <= 1 ? 0 : selected / (float)(tiles.Length - 1);
                if (tiles[selected] != null && tiles[selected].IsInteractable())
                    tiles[selected].Select();
            };
        }

        private Button CreateChapterTile(Transform strip, int index, ChapterDefinition chapter, Action open)
        {
            Button tile = ui.Button(strip, "", open);
            tile.name = "Stage " + (index + 1).ToString("00");
            // A full atlas leaf keeps its map drawing, title, and status readable.
            // Details live in the separate dossier popup.
            UIWidgets.Size(tile.transform, 540, 390);
            Image surface = tile.GetComponent<Image>(); surface.color = Color.white; surface.sprite = null;
            tile.GetComponent<UIFluidGradient>().enabled = false;
            tile.GetComponent<UIButtonFeedback>().enabled = false;
            tile.GetComponent<UIAdaptiveButton>().enabled = false;
            UIControlAccent defaultAccent = tile.GetComponent<UIControlAccent>();
            if (defaultAccent != null) defaultAccent.enabled = false;
            tile.transition = Selectable.Transition.None;
            // The page opens non-interactable; clear the Button's initial disabled tint
            // after switching to the card's own focus and hover treatment.
            surface.canvasRenderer.SetColor(Color.white);
            surface.canvasRenderer.SetAlpha(1f);
            Image masthead = ui.Surface("Stage masthead", tile.transform, Color.black);
            masthead.raycastTarget = false;
            masthead.rectTransform.anchorMin = new Vector2(0, 1);
            masthead.rectTransform.anchorMax = Vector2.one;
            masthead.rectTransform.offsetMin = new Vector2(0, -48);
            masthead.rectTransform.offsetMax = Vector2.zero;
            Text mastheadLabel = ui.Label(tile.transform, "PAO  /  OPERATION  " + (index + 1).ToString("00"),
                17, Color.white, TextAnchor.MiddleLeft);
            Anchor(mastheadLabel.rectTransform, new Vector2(0, 1), new Vector2(20, -2), new Vector2(320, 44), new Vector2(0, 1));
            Text number = tile.transform.Find("Text").GetComponent<Text>();
            number.text = (index + 1).ToString("00"); number.fontStyle = FontStyle.Bold;
            number.GetComponent<UIStyledText>().Configure(122, false, Config);
            number.resizeTextForBestFit = true;
            number.resizeTextMinSize = 50;
            number.resizeTextMaxSize = 122;
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
                T("选择要读取或写入的记录；上方可查看章节进度。", "Choose a record to load or save. The route summary is above."), false, UIPageDesign.Records);
            page.Rebuild = ShowSaves;
            Action<SaveSlotData> preview = BuildRecordRouteSummary(page.Content);
            RectTransform records = ui.Grid("Record shelf", page.Content, 2, 520);
            for (int i = 0; i <= UIRunState.AutoSlot; i++)
            {
                int slot = i; SaveSlotData data = SaveSystem.LoadSlot(i);
                bool exists = SaveSystem.HasSlot(i), automatic = i == UIRunState.AutoSlot;
                string name = automatic ? T("自动记录", "Automatic record") : T("记录 ", "Record ") + (i + 1);
                int actionColumns = safeRoot.rect.width < 520 ? 1 : safeRoot.rect.width < 820 ? 2 : 3;
                float cardHeight = actionColumns == 1 ? 420 : actionColumns == 2 ? 300 : 240;
                float actionHeight = actionColumns == 1 ? 264 : actionColumns == 2 ? 156 : 102;
                RectTransform card = ui.Rect(name, records);
                UIWidgets.Size(card, cardHeight);
                StyleRecordCard(card, automatic);
                RecordCardLabel(card, "Record name", name, 23, 10, 34, 20, 170, TextAnchor.MiddleLeft, true);
                RecordCardRightLabel(card, "Record index", automatic ? T("自动保存  /  AUTO", "AUTO SAVE") :
                    T("航行日志  /  ", "VOYAGE LOG  /  ") + (i + 1).ToString("00"),
                    15, 10, 34);
                Text summary = RecordCardLabel(card, "Record summary",
                    data == null ? (exists ? T("记录无法读取，可尝试恢复备份。", "This record is unreadable. Try restoring its backup.") :
                        T("这里还没有留下记录。", "No record here yet.")) :
                        ChapterName(data.sceneName) + "  ·  " + data.SavedAtText,
                    20, 48, 32, 20, 20, TextAnchor.MiddleLeft, true);
                summary.resizeTextForBestFit = true;
                summary.resizeTextMinSize = 15; summary.resizeTextMaxSize = 26;
                if (data != null) RecordCardLabel(card, "Record details", T("游玩时间 ", "Play time ") +
                    TimeSpan.FromSeconds(data.runProgress != null ? data.runProgress.playSeconds : 0).ToString(@"hh\:mm\:ss") +
                    T("  ·  意识碎片 ", "  ·  Memories ") + (data.runProgress != null && data.runProgress.memories != null ? data.runProgress.memories.Count : 0),
                    16, 82, 25, 20, 20, TextAnchor.MiddleLeft);
                string note = data != null && !Application.CanStreamedLevelBeLoaded(data.sceneName) ?
                    T("所在水域暂不可用，记录仍会保留。", "Stage unavailable; the record is preserved.") :
                    automatic ? T("探索、暂停及完成章节后自动更新。", "Updated during play, pause and completion.") : "";
                if (!string.IsNullOrEmpty(note))
                    RecordCardLabel(card, "Record note", note, 14, 108, 18, 20, 20, TextAnchor.MiddleLeft);
                RectTransform actions = ui.Grid("Record actions", card, actionColumns, 130);
                actions.GetComponent<UIFlowLayout>().spacing = 6;
                actions.anchorMin = Vector2.zero; actions.anchorMax = new Vector2(1, 0);
                actions.offsetMin = new Vector2(16, 12);
                actions.offsetMax = new Vector2(-16, 12 + actionHeight);
                CompactRecordAction(ui.Button(actions, T("读取", "Load"), () => Confirm(T("读取这份记录？", "Load this record?"),
                    T("当前尚未保存的探索会重置。", "Your unsaved exploration will be reset."), () => LoadScene(data.sceneName, data)),
                    false, data != null && Application.CanStreamedLevelBeLoaded(data.sceneName)));
                if (IsGameplay && !automatic)
                    CompactRecordAction(ui.Button(actions, T("保存", "Save"), () =>
                    {
                        Action save = () => { if (SaveSnapshot(slot)) RefreshSaves(page); };
                        if (exists) Confirm(T("覆盖这份记录？", "Overwrite this record?"),
                            T("原记录会保留为一份恢复备份。", "The previous record will be kept as a recovery backup."), save, true);
                        else save();
                    }));
                if (data != null && preview != null)
                    CompactRecordAction(ui.Button(actions, T("查看章节进度", "Preview route"), () =>
                    {
                        preview(data);
                        page.Content.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
                    }));
                if (SaveSystem.HasBackup(i)) CompactRecordAction(ui.Button(actions, T("恢复备份", "Restore backup"), () => Confirm(
                    T("恢复上一份记录？", "Restore the previous record?"),
                    T("使用备份替换当前记录。损坏的内容会被保留。", "Replace this record with its backup. Damaged contents are preserved."),
                    () => { if (SaveSystem.RestoreBackup(slot)) { RefreshSaves(page); Toast(T("备份已恢复", "Backup restored")); } else Toast(T("恢复失败，请稍后再试。", "Recovery failed. Please retry.")); })));
                if (exists && !automatic) CompactRecordAction(ui.Button(actions, T("删除", "Delete"), () => Confirm(
                    T("删除这份记录？", "Delete this record?"),
                    T("这份记录及其备份将被删除，此操作无法撤销。", "This record and its backup will be deleted. This cannot be undone."),
                    () => { if (SaveSystem.DeleteSlot(slot)) RefreshSaves(page); else Toast(T("删除失败，请稍后再试。", "Deletion failed. Please retry.")); }, true), false, true, true));
                card.gameObject.AddComponent<UIRecordCardMotion>().Configure(ui, i);
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
            Image accent = ui.Surface("Record spine", card, Config.ink);
            accent.raycastTarget = false; accent.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            accent.rectTransform.anchorMin = Vector2.zero; accent.rectTransform.anchorMax = Vector2.up;
            accent.rectTransform.offsetMin = new Vector2(0, highlighted ? 12 : 20);
            accent.rectTransform.offsetMax = new Vector2(highlighted ? 6 : 3, highlighted ? -12 : -20);
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
            RectTransform spread = ui.Grid("Credits spread", page.Content, 2, 440);
            RectTransform index = ui.Column("Credits index", spread, 32, 20);
            Image indexSurface = index.gameObject.AddComponent<Image>(); indexSurface.color = Color.black;
            indexSurface.raycastTarget = false;
            UIWidgets.Size(index, 430).preferredHeight = -1;
            ui.Label(index, "PAO   /   01", 21, Color.white);
            Text indexTitle = ui.Label(index, T("泡涌", "BUBBLE TIDE"), 58, Color.white);
            UIWidgets.Size(indexTitle.transform, 110).preferredHeight = -1;
            Image indexRule = ui.Surface("Credits white rule", index, Color.white);
            indexRule.raycastTarget = false; UIWidgets.Size(indexRule.transform, 3);
            ui.Label(index, T("制作档案", "CREDITS / ARCHIVE"), 25, Color.white);
            ui.Label(index, T("每个名字，都是这段旅程的一部分。", "Every name is part of this journey."), 21, Color.white);

            RectTransform names = ui.Column("Credits names", spread, 32, 22);
            Image namesSurface = names.gameObject.AddComponent<Image>(); namesSurface.color = Color.white;
            namesSurface.raycastTarget = false;
            Outline namesEdge = names.gameObject.AddComponent<Outline>(); namesEdge.effectColor = Color.black;
            namesEdge.effectDistance = new Vector2(2, 2);
            UIWidgets.Size(names, 430).preferredHeight = -1;
            ui.Emblem(names, UIGuideSymbol.Current, T("共同留下的回响", "CREATED TOGETHER"), Color.black);
            Image namesRule = ui.Surface("Credits black rule", names, Color.black);
            namesRule.raycastTarget = false; UIWidgets.Size(namesRule.transform, 2);
            Text namesText = ui.Label(names, string.IsNullOrWhiteSpace(Config.credits) ?
                T("感谢每一位参与创作与体验的朋友。", "Thank you to everyone who created and explored this world.") : Config.credits, 26);
            namesText.verticalOverflow = VerticalWrapMode.Overflow;
            ui.Label(page.Content, T("感谢你在意识之海留下涟漪。", "Thank you for leaving a ripple in the sea of consciousness."), 22);
            UICreditsFolioMotion motion = spread.gameObject.AddComponent<UICreditsFolioMotion>();
            motion.Configure(index, names);
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
            motion.Play();
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
