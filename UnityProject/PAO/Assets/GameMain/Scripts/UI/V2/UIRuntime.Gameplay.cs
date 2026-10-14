using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private Text currentAbility;
        private Text objectiveLabel;
        private Text chargeLabel;
        private Image chargeFill;
        private UIAbilityMotion abilityMotion;
        private readonly List<Image> abilityRings = new List<Image>();
        private Text journalShortcut;
        private Text pauseShortcut;
        private Text goalLabel;
        private readonly Queue<string> notices = new Queue<string>();
        private readonly Queue<string> contextNotices = new Queue<string>();
        private readonly Queue<string> tutorialNotices = new Queue<string>();
        private bool noticeIsTutorial;
        private bool noticeIsForeground;
        private RectTransform noticeRoot;
        private Text noticeText;
        private float noticeRemaining;
        private RectTransform subtitleRoot;
        private Text subtitleText;
        private float subtitleUntil;
        private string objectiveOverride;
        private bool resultShown;
        private bool wasCharging;

        private void BuildHud()
        {
            hud = ui.Rect("Gameplay HUD", safeRoot); UIWidgets.Stretch(hud);
            int chapter = Config.IndexOf(lastScene);
            RectTransform objective = ui.Column("Objective", hud, 16, 8);
            objectivePanel = objective;
            AddHudBacking(objective);
            Anchor(objective, new Vector2(0, 1), new Vector2(32, -28), new Vector2(760, 156), new Vector2(0, 1));
            ui.Label(objective, ChapterName(lastScene), 21, Config.teal);
            objectiveLabel = ui.Label(objective, objectiveOverride ?? (chapter >= 0 ? Config.chapters[chapter].objective :
                T("探索水域，寻找下一段水流。", "Explore the waters and find the next current.")), 24, Config.ink);
            goalLabel = ui.Label(objective, "", 20, Config.text);

            RectTransform type = ui.Column("Current ability", hud, 16, 8);
            abilityPanel = type;
            AddHudBacking(type);
            Anchor(type, Vector2.zero, new Vector2(32, 32), new Vector2(600, 192), Vector2.zero);
            currentAbility = ui.Label(type, "", 28, Config.ink);
            chargeLabel = ui.Label(type, T("按住左键蓄力 · E 与泡泡交互", "Hold left click to charge · E to interact"), 20, Config.ink);
            Image chargeBackground = ui.Surface("Charge track", type, Config.ink, true); UIWidgets.Size(chargeBackground.transform, 8);
            chargeFill = ui.Surface("Charge fill", chargeBackground.transform, Config.teal, true);
            chargeFill.rectTransform.anchorMin = Vector2.zero; chargeFill.rectTransform.anchorMax = new Vector2(0, 1);
            chargeFill.rectTransform.offsetMin = chargeFill.rectTransform.offsetMax = Vector2.zero;

            RectTransform abilities = ui.Rect("Three bubble abilities", hud);
            Anchor(abilities, new Vector2(1, 0), new Vector2(-32, 48), new Vector2(350, 340), new Vector2(1, 0));
            abilityMotion = abilities.gameObject.AddComponent<UIAbilityMotion>();
            abilityMotion.Configure(ui, chargeFill, type.GetComponent<Image>(), currentAbility);
            Vector2[] positions = { new Vector2(-175, 245), new Vector2(-290, 82), new Vector2(-60, 82) };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Image circle = ui.Rect("Ability " + i, abilities).gameObject.AddComponent<UICircleImage>();
                circle.color = Config.mist;
                Anchor(circle.rectTransform, new Vector2(1, 0), positions[i], new Vector2(96, 96), new Vector2(0.5f, 0.5f));
                Button button = circle.gameObject.AddComponent<Button>(); button.targetGraphic = circle;
                button.onClick.AddListener(() => { if (switcher != null) switcher.SetType((BubbleType)index); });
                circle.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
                Image ring = ui.Rect("Selection ring", circle.transform).gameObject.AddComponent<UILowPolyRingImage>();
                ring.color = Config.teal;
                UIWidgets.Stretch(ring.rectTransform, -8); ring.raycastTarget = false;
                abilityRings.Add(ring);
                UIGuideGraphic icon = ui.Rect("Ability symbol", circle.transform).gameObject.AddComponent<UIGuideGraphic>();
                icon.Symbol = i == 0 ? UIGuideSymbol.Sticky : i == 1 ? UIGuideSymbol.Bouncy : UIGuideSymbol.Bomb;
                icon.color = Config.text; icon.raycastTarget = false; UIWidgets.Stretch(icon.rectTransform, 25);
                abilityMotion.AddNode(ui, circle, ring);
            }
            RectTransform buttons = ui.Row("HUD actions", hud, 12);
            Anchor(buttons, Vector2.one, new Vector2(-32, -28), new Vector2(140, 56), Vector2.one);
            journalShortcut = HudIconButton(buttons, UIGuideSymbol.Journal, ShowJournal);
            pauseShortcut = HudIconButton(buttons, UIGuideSymbol.Pause, ShowPause);
            BuildVisualGuidance();
            BuildResourceHud();
            RefreshHud();
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        private void AddHudBacking(RectTransform rect)
        {
            Image backing = rect.gameObject.AddComponent<UILowPolyImage>();
            backing.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, 0.94f);
            backing.raycastTarget = false;
        }

        private void ClearHud()
        {
            if (subtitleRoot != null) Destroy(subtitleRoot.gameObject);
            subtitleRoot = null; subtitleText = null;
            if (hud != null) { hud.gameObject.SetActive(false); Destroy(hud.gameObject); }
            hud = null; abilityMotion = null; abilityRings.Clear(); currentAbility = null;
            ClearVisualGuidance();
            ClearResourceHud();
            if (noticeRoot != null) Destroy(noticeRoot.gameObject);
            noticeRoot = null; notices.Clear(); contextNotices.Clear(); tutorialNotices.Clear(); noticeRemaining = 0;
            resultShown = false; wasCharging = false;
        }

        private string AbilityName(int index)
        {
            return index == 0 ? T("浮粘泡泡", "Sticky bubble") : index == 1 ? T("弹力泡泡", "Bouncy bubble") : T("炸弹泡泡", "Bomb bubble");
        }

        private void OnAbilityChanged(BubbleType ability)
        {
            UIRunState.Progress.bubbleType = (int)ability; RefreshHud();
            string[] descriptions = {
                T("浮粘泡泡能彼此黏附，帮助你抵达更高的位置。", "Sticky bubbles attach to one another to help you reach higher places."),
                T("弹力泡泡在碰撞中反弹，留意起跳与释放的时机。", "Bouncy bubbles rebound on impact. Watch your takeoff and release timing."),
                T("炸弹泡泡能改变周围环境，施放前留意安全距离。", "Bomb bubbles can change the environment. Keep a safe distance.") };
            ShowTutorial("ability-" + (int)ability, AbilityName((int)ability), descriptions[(int)ability]);
        }

        private void RefreshHud()
        {
            RefreshResourceHud();
            if (hud == null || currentAbility == null || switcher == null) return;
            int type = (int)switcher.Current;
            string name = AbilityName(type);
            if (currentAbility.text != name) currentAbility.text = name;
            if (journalShortcut != null) { journalShortcut.text = GuideInputPrompt(UIInput.Journal); SetInputTypeface(journalShortcut); }
            if (pauseShortcut != null) pauseShortcut.text = UIInput.ControllerActive ? "Menu" : "Esc";
            if (goalLabel != null)
                goalLabel.text = chapterGoal != null && player != null ? T("终点距离 ", "Goal distance ") +
                    Mathf.RoundToInt(Vector3.Distance(player.transform.position, chapterGoal.position)) + " m" :
                    tide != null ? T("潮水抵达终点 ", "Tide reaches goal in ") + Mathf.Max(0, Mathf.CeilToInt(tide.TimeUntilGoalFlooded)) + " s" : "";
            if (abilityMotion != null)
                abilityMotion.SetState(type, launcher != null && launcher.IsCharging ? launcher.ChargeProgress : 0,
                    launcher != null && launcher.IsCharging, launcher != null && launcher.HasHeldBubble);
            if (launcher != null && chargeLabel != null)
            {
                string charge = UIInput.Prompt(UIInput.Charge), interact = UIInput.Prompt(UIInput.Interact);
                string status = launcher.IsCharging ? T("正在蓄力 ", "Charging ") + Mathf.RoundToInt(launcher.ChargeProgress * 100) + "%" :
                    launcher.HasHeldBubble ? T("泡泡已就绪 · ", "Bubble ready · ") + charge + T(" 发射", " to launch") :
                    UIInput.ToggleCharge ? charge + T(" 点击蓄力 · 再次点击释放", " to charge · Press again to release") :
                    T("按住 ", "Hold ") + charge + T(" 蓄力 · ", " to charge · ") + interact + T(" 交互", " to interact");
                if (chargeLabel.text != status) chargeLabel.text = status;
                chargeFill.rectTransform.anchorMax = new Vector2(launcher.HasHeldBubble ? 1 : launcher.IsCharging ? launcher.ChargeProgress : 0, 1);
                if (wasCharging && !launcher.IsCharging) ShowTutorial("first-cast", T("让泡泡成为你的路径", "Let bubbles become your path"),
                    T("留意泡泡与地形的关系。交互按键：", "Watch how bubbles interact with the terrain. Interaction key: ") + interact);
                wasCharging = launcher.IsCharging;
            }
        }

        public void SetObjective(string text)
        {
            objectiveOverride = text;
            if (objectiveLabel != null) objectiveLabel.text = text;
        }

        public void ShowSubtitle(string speaker, string text)
        {
            if (!UISettings.Current.subtitles) return;
            if (subtitleRoot == null)
            {
                Image background = ui.Surface("Subtitles", safeRoot, Config.ink, true);
                subtitleRoot = background.rectTransform;
                Anchor(subtitleRoot, new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(760, 100), new Vector2(0.5f, 0));
                background.raycastTarget = false;
                background.gameObject.AddComponent<UISubtitleBackground>().Configure(Config.ink);
                subtitleText = ui.Label(subtitleRoot, "", 24, Config.paper, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(subtitleText.rectTransform, 16);
            }
            subtitleText.text = string.IsNullOrEmpty(speaker) ? text : speaker + "\n" + text;
            subtitleUntil = Time.unscaledTime + Mathf.Max(5, text.Length * 0.12f);
        }

        public void Toast(string message, bool showOverModals = true)
        {
            if (string.IsNullOrEmpty(message) || notices.Count + contextNotices.Count >= 8) return;
            if (showOverModals) notices.Enqueue(message); else contextNotices.Enqueue(message);
        }

        private void UpdateTransientUI()
        {
            if (!UISettings.Current.detailedHints)
            {
                tutorialNotices.Clear();
                if (noticeIsTutorial) noticeRemaining = 0;
            }
            if (subtitleRoot != null)
            {
                subtitleRoot.gameObject.SetActive(UISettings.Current.subtitles && !IsBlockingGameplay && Time.unscaledTime < subtitleUntil);
                bool narrow = safeRoot.rect.width < 1200;
                subtitleRoot.anchorMin = subtitleRoot.anchorMax = new Vector2(.5f, narrow ? .45f : 0);
                subtitleRoot.anchoredPosition = new Vector2(0, narrow ? 0 : 36);
                subtitleRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(760, safeRoot.rect.width - 96));
                subtitleRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(90, subtitleText.preferredHeight + 40));
            }
            if (noticeRoot == null)
            {
                Image paper = ui.Surface("Context notice", safeRoot, Config.mist, true);
                noticeRoot = paper.rectTransform;
                Anchor(noticeRoot, new Vector2(0.5f, 1), new Vector2(0, -208), new Vector2(840, 72), new Vector2(0.5f, 1));
                paper.raycastTarget = false;
                noticeText = ui.Label(noticeRoot, "", 23, Config.text, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(noticeText.rectTransform, 20);
            }
            if (!noticeIsForeground && notices.Count > 0) noticeRemaining = 0;
            if (noticeRemaining <= 0 && notices.Count > 0)
            { noticeText.text = notices.Dequeue(); noticeRemaining = 6; noticeIsForeground = true; noticeIsTutorial = false; }
            else if (noticeRemaining <= 0 && pages.Count == 0 && tutorialNotices.Count > 0)
            { noticeText.text = tutorialNotices.Dequeue(); noticeRemaining = 6; noticeIsForeground = false; noticeIsTutorial = true; }
            else if (noticeRemaining <= 0 && pages.Count == 0 && contextNotices.Count > 0)
            { noticeText.text = contextNotices.Dequeue(); noticeRemaining = 6; noticeIsForeground = false; noticeIsTutorial = false; }
            bool visible = noticeRemaining > 0 && (noticeIsForeground || pages.Count == 0);
            noticeRoot.gameObject.SetActive(visible && pages.Count == 0);
            if (pages.Count > 0)
            {
                UIPage top = pages[pages.Count - 1];
                top.NoticeArea.gameObject.SetActive(visible);
                if (visible) top.NoticeLabel.text = noticeText.text;
            }
            if (noticeRemaining > 0) noticeRoot.sizeDelta = new Vector2(840, Mathf.Max(72, noticeText.preferredHeight + 40));
            noticeRoot.SetAsLastSibling();
            if (visible) noticeRemaining -= Time.unscaledDeltaTime;
        }

        public void ShowTutorial(string id, string title, string body)
        {
            if (hud == null || !UISettings.Current.tutorials || UIRunState.Progress.tutorialsSeen.Contains(id)) return;
            UIRunState.Progress.tutorialsSeen.Add(id);
            if (UISettings.Current.detailedHints) { if (tutorialNotices.Count < 3) tutorialNotices.Enqueue(title + "\n" + body); }
            else ShowVisualTutorial(id);
        }

        public void ShowHelp()
        {
            UIPage page = Push(T("操作与教学", "Controls & tutorials"), T("任何时候都可以回到这里复习。", "Return here at any time for a refresher."), false, UIPageDesign.Guide);
            page.Rebuild = ShowHelp;
            string[,] topics = {
                { T("当前路径", "Current route"), objectiveOverride ?? (Config.IndexOf(lastScene) >= 0 ? Config.chapters[Config.IndexOf(lastScene)].objective :
                    T("探索水域，寻找下一段水流。", "Explore the waters and find the next current.")) },
                { T("探索", "Explore"), UIInput.MovementHint() + T(" 移动 · 跳跃 ", " Move · Jump ") + UIInput.Prompt(UIInput.Jump) +
                    T(" · 加速 ", " · Sprint ") + UIInput.Prompt(UIInput.Sprint) + T("。鼠标或右摇杆改变视角。", ". Mouse or right stick to look.") },
                { T("泡泡", "Bubbles"), UIInput.Prompt(0) + "/" + UIInput.Prompt(1) + "/" + UIInput.Prompt(2) +
                    T(" 或滚轮切换。蓄力与发射：", " or wheel to switch. Charge and launch: ") + UIInput.Prompt(UIInput.Charge) +
                    (UIInput.ToggleCharge ? T("。点击蓄力，再按一次释放。", ". Press to charge, then press again to release.") :
                    T("。按住蓄力，松开释放。", ". Hold to charge, release to cast.")) + T("较大的泡泡停在发射口，再按一次发射。", " Large bubbles wait at the muzzle; press again to launch.") },
                { T("交互", "Interact"), T("靠近泡泡或互动点。交互：", "Approach a bubble or interaction site. Interact: ") + UIInput.Prompt(UIInput.Interact) +
                    T(" · 乘坐与跳出：", " · Ride and exit: ") + UIInput.Prompt(UIInput.Ride) },
                { T("记录", "Records"), T("Esc 暂停并保存。完成章节会自动记录。日志：", "Esc to pause and save. Chapters save automatically. Journal: ") + UIInput.Prompt(UIInput.Journal) },
                { T("引爆", "Detonate"), UIInput.Prompt(UIInput.Detonate) + T(" 引爆泡泡", " detonates bubbles") },
                { T("能力特性", "Ability behavior"), T("浮粘泡泡彼此黏附，组成向上的路径。弹力泡泡在碰撞中反弹。炸弹泡泡改变周围环境，留意安全距离。",
                    "Sticky bubbles attach to build upward paths. Bouncy bubbles rebound on impact. Bomb bubbles change the environment; keep a safe distance.") },
                { T("泡泡值与回收", "Energy & recovery"), T("三种泡泡共用泡泡值。蓄力越大，消耗越多；不足时停止变大，仍可释放。蓄力中锁定类型。炸掉浮粘泡泡可返还其消耗，破坏泡泡地形也可获得泡泡值。",
                    "All types share energy. Larger bubbles cost more. Low energy stops growth but still allows release. Type switching locks while charging. Bombs reclaim energy from sticky bubbles; breaking bubble terrain also rewards energy.") },
                { T("泡泡地形", "Bubble terrain"), T("弹力泡泡可以填充泡泡地形，填满后自动破坏。普通破坏后地形会重生，炸弹破坏则永久消失。低泡泡值也会影响角色表情。",
                    "Bouncy bubbles fill bubble terrain until it can be broken. Normally it respawns; bomb destruction is permanent. Low energy also changes the character's expression.") },
                { T("导航", "Navigation"), T("方向键或 Tab 移动焦点，Enter 确认，Esc 返回。手柄 A 确认、B 返回、菜单键暂停。", "Arrows or Tab move focus, Enter submits, Esc returns. Controller A submits, B returns, Menu pauses.") } };
            RectTransform lessons = ui.Grid("Illustrated field guide", page.Content, 2, 430);
            UIGuideSymbol[] symbols = { UIGuideSymbol.Goal, UIGuideSymbol.Move, UIGuideSymbol.Charge, UIGuideSymbol.Ride, UIGuideSymbol.Journal, UIGuideSymbol.Bomb, UIGuideSymbol.Sticky, UIGuideSymbol.Charge, UIGuideSymbol.Bouncy, UIGuideSymbol.Arrow };
            for (int i = 0; i < topics.GetLength(0); i++)
            {
                RectTransform lesson = ui.Card("Lesson " + i, lessons);
                ui.Emblem(lesson, symbols[i], (i + 1).ToString("00") + "  /  " + topics[i, 0]);
                ui.Label(lesson, topics[i, 1], 22);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void CollectMemory(MemoryEntry memory)
        {
            if (!UIRunState.AddMemory(memory)) return;
            if (UISettings.Current.detailedHints) Toast(T("获得意识碎片 · ", "Memory found · ") + memory.title);
            else PulseVisualCue(UIGuideSymbol.Memory);
        }

        public void ShowJournal()
        {
            UIPage page = Push(T("意识碎片", "Memory journal"),
                T("你在水流中拾起的片段，将在这里留下回声。", "Fragments gathered in the current echo here."), false, UIPageDesign.Journal);
            page.Rebuild = ShowJournal;
            if (UIRunState.Progress.memories.Count == 0)
            {
                RectTransform empty = ui.Card("Unwritten journal", page.Content);
                ui.Emblem(empty, UIGuideSymbol.Memory, T("等待第一段回声", "THE FIRST ECHO AWAITS"), UIDesignPalette.Coral);
                ui.Label(empty, T("尚未拾起意识碎片。探索时留意水域中可以互动的物体。", "No memories yet. Look for objects you can interact with as you explore."), 24);
            }
            var entries = new List<MemoryEntry>(UIRunState.Progress.memories);
            entries.Sort((a, b) => string.Compare(a != null ? a.chapter : "", b != null ? b.chapter : "", StringComparison.Ordinal));
            string chapter = null;
            RectTransform journal = null;
            foreach (MemoryEntry memory in entries)
            {
                if (memory == null) continue;
                if (journal == null || chapter != memory.chapter)
                {
                    chapter = memory.chapter;
                    ui.Emblem(page.Content, UIGuideSymbol.Journal, ChapterName(chapter), UIDesignPalette.Coral);
                    journal = ui.Grid("Journal chapter " + chapter, page.Content, 2, 440);
                }
                RectTransform entry = ui.Card("Memory " + memory.id, journal);
                ui.Label(entry, memory.title, 30);
                ui.Label(entry, memory.body, 22);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void ShowDialogue(DialogueAsset dialogue)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Length == 0 || loading) return;
            int line = 0;
            UIPage page = Push(dialogue.title, "", true, UIPageDesign.Dialogue);
            Text speaker = ui.Label(page.Content, "", 26, Config.teal);
            Text body = ui.Label(page.Content, "", 28);
            Action render = () => { speaker.text = dialogue.lines[line].speaker; body.text = dialogue.lines[line].text; page.Refresh(); };
            render();
            ui.Button(page.Footer, T("跳过", "Skip"), () => Confirm(T("跳过这段回声？", "Skip this echo?"),
                T("这段内容会保留在日志里。", "This content will remain in your journal."), () => { RecordDialogue(dialogue); Pop(); }));
            ui.Button(page.Footer, T("继续", "Continue"), () =>
            {
                line++;
                if (line < dialogue.lines.Length) render();
                else
                {
                    RecordDialogue(dialogue); Pop();
                    if (dialogue.choices != null && dialogue.choices.Length > 0) ShowDialogueChoices(dialogue);
                }
            }, true); Finish(page);
        }

        private void RecordDialogue(DialogueAsset dialogue)
        {
            string body = "";
            foreach (DialogueLine line in dialogue.lines) body += line.speaker + "：" + line.text + "\n";
            CollectMemory(new MemoryEntry { id = dialogue.id, chapter = lastScene, title = dialogue.title, body = body });
        }

        private void ShowDialogueChoices(DialogueAsset dialogue)
        {
            UIPage page = Push(dialogue.title, T("选择你想留下的回声。", "Choose the echo you wish to leave."), true, UIPageDesign.Dialogue);
            foreach (DialogueChoice choice in dialogue.choices)
            {
                DialogueChoice selected = choice;
                ui.Button(page.Content, choice.text, () => Confirm(choice.text,
                    T("确认后，这个选择将被记入旅程。", "Confirm to record this choice in your journey."), () =>
                    {
                        CollectMemory(new MemoryEntry { id = dialogue.id + ":choice", chapter = lastScene, title = dialogue.title, body = selected.text });
                        if (selected.next != null) { Pop(); ShowDialogue(selected.next); } else Pop();
                    }));
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        private void OnLevelWon() { CompleteChapter(); }
        private void OnLevelLost() { ShowFailure(); }
        public void ShowFailure()
        {
            if (resultShown) return; resultShown = true;
            UIPage page = Push(T("水流暂时淹没了路径", "The current covered the path"),
                T("换一种泡泡组合，再试一次。", "Try a different combination of bubbles."), false, UIPageDesign.Failure);
            page.CanClose = false;
            RectTransform recovery = ui.Card("Another current", page.Content, true);
            ui.Emblem(recovery, UIGuideSymbol.Current, T("新的路径，仍在水中", "ANOTHER PATH AWAITS"));
            ui.IconButton(recovery, T("重试本章", "Retry chapter"), UIGuideSymbol.Arrow, () => LoadScene(lastScene), true);
            ui.IconButton(recovery, T("操作与教学", "Controls & tutorials"), UIGuideSymbol.Move, ShowHelp);
            ui.Button(page.Footer, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Footer, T("主菜单", "Main menu"), () => LoadScene(Config.menuScene)); Finish(page);
        }

        public bool CompleteChapter(string nextScene = null)
        {
            if (!IsGameplay || resultShown || loading) return false;
            resultShown = true; UIRunState.Complete(lastScene); SaveSnapshot(UIRunState.AutoSlot);
            int index = Config.IndexOf(lastScene);
            bool ending = index >= 0 && index == Config.chapters.Length - 1;
            UIPage page = Push(T("这一段水流，已有回响", "This current now has an echo"), ChapterName(lastScene), false, UIPageDesign.Completion);
            page.CanClose = false;
            ui.Emblem(page.Content, UIGuideSymbol.Check, T("章节完成", "CHAPTER COMPLETE"));
            RectTransform statistics = ui.Grid("Journey keepsakes", page.Content, 2, 300);
            RectTransform memories = ui.Card("Memory count", statistics);
            ui.Emblem(memories, UIGuideSymbol.Memory, T("意识碎片", "MEMORIES"));
            ui.Label(memories, UIRunState.Progress.memories.Count.ToString("00"), 56);
            RectTransform time = ui.Card("Journey time", statistics);
            ui.Emblem(time, UIGuideSymbol.Current, T("旅程时间", "JOURNEY TIME"));
            ui.Label(time, TimeSpan.FromSeconds(UIRunState.Progress.playSeconds).ToString(@"hh\:mm\:ss"), 44);
            if (string.IsNullOrEmpty(nextScene) && index >= 0 && index + 1 < Config.chapters.Length) nextScene = Config.chapters[index + 1].scene;
            string next = nextScene;
            if (ending) ui.Button(page.Content, T("留下最终的回声", "Leave your final echo"), ShowEnding, true);
            else if (!string.IsNullOrEmpty(next)) ui.Button(page.Content, T("前往下一章", "Next chapter"), () => { SaveOnNextEntry = true; LoadScene(next); }, true);
            ui.Button(page.Content, T("回看意识碎片", "Review memories"), ShowJournal);
            ui.Button(page.Footer, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Footer, T("主菜单", "Main menu"), () => LoadScene(Config.menuScene)); Finish(page); return true;
        }

        public void ConfirmChapterGoal(string nextScene)
        {
            if (IsBlockingGameplay || resultShown || loading) return;
            Confirm(T("完成这一章？", "Complete this chapter?"),
                T("确认后保存章节进度并打开结算。取消可继续探索。", "Confirm to save chapter progress and open the results. Cancel to keep exploring."),
                () => CompleteChapter(nextScene));
        }

        public void ShowEnding()
        {
            UIPage page = Push(T("当水流归于平静", "When the current rests"),
                T("你可以先回看旅程，再决定留下怎样的回声。", "Review your journey before choosing the echo to leave."), false, UIPageDesign.Ending);
            ui.Button(page.Content, T("回看意识碎片", "Review memories"), ShowJournal);
            RectTransform branches = ui.Grid("Final echoes", page.Content, 2, 440);
            if (Config.endings == null || Config.endings.Length == 0)
            {
                RectTransform unwritten = ui.Card("Unwritten echo", branches);
                ui.Emblem(unwritten, UIGuideSymbol.Current, T("旅程尚在继续", "THE JOURNEY CONTINUES"), UIDesignPalette.Coral);
                ui.Label(unwritten, T("这段旅程的结局尚待写入，你可以继续回看意识碎片。", "The ending of this journey is still unwritten. You can continue reviewing your memories."), 26);
            }
            foreach (EndingDefinition ending in Config.endings ?? new EndingDefinition[0])
            {
                EndingDefinition choice = ending;
                RectTransform branch = ui.Card("Ending " + choice.id, branches);
                ui.Emblem(branch, UIGuideSymbol.Memory, choice.title, UIDesignPalette.Coral);
                bool eligible = UIRunState.Progress.completed.Contains(lastScene) &&
                    UIRunState.Progress.memories.Count >= choice.requiredMemories;
                ui.Label(branch, choice.description, 24);
                if (!eligible) ui.Label(branch, T("需要完成本章并收集意识碎片：", "Complete this chapter and collect memories: ") + choice.requiredMemories, 22);
                ui.Button(branch, choice.title, () => Confirm(choice.title,
                    T("确认后记录这一结局。取消可继续回看。", "Confirm to record this ending. Cancel to continue reviewing."), () =>
                    {
                        string previous = UIRunState.Progress.ending;
                        UIRunState.Progress.ending = choice.id;
                        if (!SaveSnapshot(UIRunState.AutoSlot)) { UIRunState.Progress.ending = previous; return; }
                        Pop(); ShowCredits();
                    }), false, eligible && !string.IsNullOrEmpty(choice.id));
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }
    }
}
