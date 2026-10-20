using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private RectTransform abilityCluster, hudMessages;
        private Text objectiveLabel;
        private UIAbilityMotion abilityMotion;
        private readonly List<Image> abilityRings = new List<Image>();
        private readonly List<Button> abilityButtons = new List<Button>();
        private readonly List<CanvasGroup> abilityAvailabilityGroups = new List<CanvasGroup>();
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
        private Text narrationSpeaker;
        private UICinematicSubtitleMotion subtitleMotion;
        private bool subtitleExitImmediately;
        private string objectiveOverride;
        private bool resultShown;
        private bool wasCharging;

        private void BuildHud()
        {
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { BuildSandboxHud(); return; }
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

            hudMessages = ui.Column("Gameplay messages", hud, 0, 8);
            hudMessages.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            if (noticeRoot != null) noticeRoot.SetParent(hudMessages, false);
            abilityCluster = ui.Rect("Three bubble abilities", hud);
            RectTransform abilities = abilityCluster;
            Anchor(abilities, new Vector2(1, 0), new Vector2(-32, 32), new Vector2(380, 400), new Vector2(1, 0));
            abilityMotion = abilities.gameObject.AddComponent<UIAbilityMotion>();
            abilityMotion.Configure(ui, null, null, null);
            Vector2[] positions = { new Vector2(-190, 245), new Vector2(-324, 56), new Vector2(-56, 56) };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                Image circle = ui.Rect("Ability " + i, abilities).gameObject.AddComponent<UICircleImage>();
                circle.color = Config.mist;
                Anchor(circle.rectTransform, new Vector2(1, 0), positions[i], new Vector2(96, 96), new Vector2(0.5f, 0.5f));
                Button button = circle.gameObject.AddComponent<Button>(); button.targetGraphic = circle;
                button.onClick.AddListener(() => { if (switcher != null) switcher.SetType((BubbleType)index); });
                abilityButtons.Add(button);
                abilityAvailabilityGroups.Add(circle.gameObject.AddComponent<CanvasGroup>());
                circle.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
                Image ring = ui.Surface("Selection ring", circle.transform, Config.teal);
                UIWidgets.Stretch(ring.rectTransform, -8); ring.sprite = Config.ring; ring.raycastTarget = false;
                abilityRings.Add(ring);
                UIGuideGraphic icon = ui.Rect("Ability symbol", circle.transform).gameObject.AddComponent<UIGuideGraphic>();
                icon.Symbol = i == 0 ? UIGuideSymbol.Sticky : i == 1 ? UIGuideSymbol.Bouncy : UIGuideSymbol.Bomb;
                icon.color = Config.text; icon.raycastTarget = false; UIWidgets.Stretch(icon.rectTransform, 25);
                abilityMotion.AddNode(ui, circle, ring);
            }
            RectTransform buttons = ui.Row("HUD actions", hud, 12);
            Anchor(buttons, Vector2.one, new Vector2(-32, -28), new Vector2(140, 56), Vector2.one);
            journalShortcut = HudIconButton(buttons, UIGuideSymbol.Restart, () => Confirm(
                T("重新开始本章？", "Restart this chapter?"),
                T("本章尚未保存的变化会重置。", "Unsaved changes in this chapter will be reset."), () => LoadScene(lastScene)));
            pauseShortcut = HudIconButton(buttons, UIGuideSymbol.Pause, ShowPause);
            journalShortcut.transform.parent.gameObject.SetActive(true);
            pauseShortcut.transform.parent.gameObject.SetActive(true);
            BuildVisualGuidance();
            BuildResourceHud();
            RefreshHud();
            RevealHudElement(resourcePanel, 0);
            RevealHudElement(abilityCluster, .12f);
            RevealHudElement(buttons, .2f);
            MechanicsTestScene test = FindFirstObjectByType<MechanicsTestScene>();
            if (test != null && test.NarrationPreview)
                ShowNarration(T("旁白", "Narrator"), T("水流轻轻涌动，泡泡会带你去往新的水域。", "The current stirs softly. Let the bubbles carry you into new waters."), -1);
        }

        private static void RevealHudElement(RectTransform element, float delay)
        {
            if (element == null || UISettings.Current.reducedMotion) return;
            CanvasGroup layer = element.gameObject.AddComponent<CanvasGroup>();
            layer.alpha = 0;
            layer.DOFade(1, .38f).SetDelay(delay).SetEase(Ease.OutCubic)
                .SetUpdate(true).SetLink(element.gameObject);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        private void AddHudBacking(RectTransform rect)
        {
            Image backing = rect.gameObject.AddComponent<Image>();
            backing.sprite = Config.round; backing.type = Image.Type.Sliced;
            backing.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, 0.94f);
            backing.raycastTarget = false;
        }

        private void ClearHud()
        {
            if (subtitleRoot != null) Destroy(subtitleRoot.gameObject);
            subtitleRoot = null; subtitleText = narrationSpeaker = null; subtitleMotion = null;
            subtitleUntil = 0; subtitleExitImmediately = false;
            if (hud != null) { hud.gameObject.SetActive(false); Destroy(hud.gameObject); }
            hud = null; hudMessages = abilityCluster = null;
            abilityMotion = null; abilityRings.Clear();
            abilityButtons.Clear(); abilityAvailabilityGroups.Clear();
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
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { RefreshSandboxHud(); return; }
            UIRunState.Progress.bubbleType = (int)ability; RefreshHud();
            string[] descriptions = {
                T("浮粘泡泡能彼此黏附，帮助你抵达更高的位置。", "Sticky bubbles attach to one another to help you reach higher places."),
                T("弹力泡泡在碰撞中反弹，留意起跳与释放的时机。", "Bouncy bubbles rebound on impact. Watch your takeoff and release timing."),
                T("炸弹泡泡能改变周围环境，施放前留意安全距离。", "Bomb bubbles can change the environment. Keep a safe distance.") };
            ShowTutorial("ability-" + (int)ability, AbilityName((int)ability), descriptions[(int)ability]);
        }

        private void RefreshHud()
        {
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { RefreshSandboxHud(); return; }
            RefreshResourceHud();
            if (hud == null || switcher == null) return;
            int type = launcher != null ? (int)launcher.DisplayType : (int)switcher.Current;
            if (!switcher.IsAvailable((BubbleType)type)) type = (int)switcher.Current;
            for (int i = 0; i < abilityButtons.Count; i++)
            {
                bool available = switcher.IsAvailable((BubbleType)i);
                abilityButtons[i].interactable = available;
                abilityAvailabilityGroups[i].alpha = available ? 1f : .25f;
                abilityAvailabilityGroups[i].interactable = available;
            }
            if (goalLabel != null)
                goalLabel.text = chapterGoal != null && player != null ? T("终点距离 ", "Goal distance ") +
                    Mathf.RoundToInt(Vector3.Distance(player.transform.position, chapterGoal.position)) + " m" :
                    tide != null ? T("潮水抵达终点 ", "Tide reaches goal in ") + Mathf.Max(0, Mathf.CeilToInt(tide.TimeUntilGoalFlooded)) + " s" : "";
            if (abilityMotion != null)
                abilityMotion.SetState(type, launcher != null && launcher.IsCharging ? launcher.ChargeProgress : 0,
                    launcher != null && launcher.IsCharging, launcher != null && launcher.HasHeldBubble);
            if (launcher != null)
            {
                if (wasCharging && !launcher.IsCharging) ShowTutorial("first-cast", T("让泡泡成为你的路径", "Let bubbles become your path"),
                    T("留意泡泡与地形的关系。交互按键：", "Watch how bubbles interact with the terrain. Interaction key: ") + UIInput.Prompt(UIInput.Interact));
                wasCharging = launcher.IsCharging;
            }
        }

        public void SetObjective(string text)
        {
            objectiveOverride = text;
            if (objectiveLabel != null) objectiveLabel.text = text;
        }

        public void ShowSubtitle(string speaker, string text) => ShowNarration(speaker, text);

        // Negative duration keeps a line visible until HideNarration; zero uses reading time.
        public void ShowNarration(string speaker, string text, float duration = 0)
        {
            if (string.IsNullOrWhiteSpace(text)) { HideNarration(); return; }
            if (hudMessages == null) return;
            EnsureNarrationRoot();
            narrationSpeaker.text = speaker;
            // A generic narrator tag adds no information to a cinematic subtitle.
            bool namedSpeaker = !string.IsNullOrWhiteSpace(speaker) && speaker != "旁白" &&
                !string.Equals(speaker, "Narrator", StringComparison.OrdinalIgnoreCase);
            narrationSpeaker.gameObject.SetActive(namedSpeaker);
            float revealDuration = subtitleMotion != null ? subtitleMotion.Present(text) : 0;
            subtitleExitImmediately = duration > 0 && duration <= .2f;
            subtitleUntil = duration < 0 ? float.PositiveInfinity : Time.unscaledTime +
                (duration > 0 ? duration : revealDuration + Mathf.Max(5, text.Length * .12f));
            RefreshNarration();
            PositionGameplayHud();
        }

        /// <summary>
        /// 字幕控件（含 CanvasGroup / 动画组件）可能被 UI 重建或场景切换销毁。
        /// 每次显示前检查：缺了哪个就整组重建，避免“字幕不见了”。
        /// </summary>
        private void EnsureNarrationRoot()
        {
            if (subtitleRoot != null && subtitleMotion != null && subtitleText != null && narrationSpeaker != null)
            {
                subtitleMotion.Configure(subtitleText, narrationSpeaker);
                return;
            }

            if (subtitleRoot != null) Destroy(subtitleRoot.gameObject);

            subtitleRoot = ui.Column("Narration popup", hudMessages, 18, 6);
            // 电影字幕：不铺底板，只留文字 —— 可读性交给 Outline + Shadow。
            // 底板会把字幕从画面里「浮起来」变成对话框，破坏沉浸感。
            narrationSpeaker = ui.Label(subtitleRoot, "", 20, Color.white, TextAnchor.MiddleCenter);
            narrationSpeaker.fontStyle = FontStyle.Bold;
            subtitleText = ui.Label(subtitleRoot, "", 30, Color.white, TextAnchor.MiddleCenter);
            subtitleText.lineSpacing = 1.12f;
            subtitleMotion = subtitleRoot.gameObject.AddComponent<UICinematicSubtitleMotion>();
            subtitleMotion.Configure(subtitleText, narrationSpeaker);
            Outline textEdge = subtitleText.gameObject.AddComponent<Outline>();
            textEdge.effectColor = new Color(0, 0, 0, .92f);
            textEdge.effectDistance = new Vector2(1, -1);
            Shadow textShadow = subtitleText.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0, 0, 0, .72f);
            textShadow.effectDistance = new Vector2(0, -2);
            subtitleRoot.SetAsLastSibling();
        }

        public void HideNarration()
        {
            subtitleUntil = 0;
            if (subtitleMotion != null) subtitleMotion.Dismiss(true);
            else if (subtitleRoot != null) subtitleRoot.gameObject.SetActive(false);
        }

        private void RefreshNarration()
        {
            if (subtitleRoot == null) return;
            UISettingsData settings = UISettings.Current;
            bool enabled = (settings == null || settings.subtitles) && !IsBlockingGameplay;
            bool withinTime = Time.unscaledTime < subtitleUntil;
            if (!enabled)
            {
                if (subtitleMotion != null && subtitleRoot.gameObject.activeSelf) subtitleMotion.Suspend();
                else subtitleRoot.gameObject.SetActive(false);
            }
            else if (withinTime)
            {
                if (!subtitleRoot.gameObject.activeSelf && subtitleMotion != null) subtitleMotion.Restore();
                else if (!subtitleRoot.gameObject.activeSelf) subtitleRoot.gameObject.SetActive(true);
            }
            else if (subtitleMotion != null)
            {
                if (subtitleRoot.gameObject.activeSelf && !subtitleMotion.IsDismissing)
                    subtitleMotion.Dismiss(subtitleExitImmediately || (settings != null && settings.reducedMotion));
            }
            else subtitleRoot.gameObject.SetActive(false);
            float height = 36 + ResourceTextHeight(subtitleText);
            if (narrationSpeaker != null && narrationSpeaker.gameObject.activeSelf) height += ResourceTextHeight(narrationSpeaker) + 6;
            UIWidgets.Size(subtitleRoot, Mathf.Max(90, height));
        }

        public void Toast(string message, bool showOverModals = true)
        {
            if (string.IsNullOrEmpty(message) || notices.Count + contextNotices.Count >= 8) return;
            if (showOverModals) notices.Enqueue(message); else contextNotices.Enqueue(message);
        }

        private void UpdateTransientUI()
        {
            UISettingsData settings = UISettings.Current;
            if (settings != null && !settings.detailedHints)
            {
                tutorialNotices.Clear();
                if (noticeIsTutorial) noticeRemaining = 0;
            }
            RefreshNarration();
            if (noticeRoot == null)
            {
                Image paper = ui.Surface("Context notice", hudMessages != null ? hudMessages : safeRoot, Config.mist, true);
                noticeRoot = paper.rectTransform;
                UIWidgets.Size(noticeRoot, 72);
                paper.raycastTarget = false;
                noticeText = ui.Label(noticeRoot, "", 23, Config.text, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(noticeText.rectTransform, 20);
            }
            if (hudMessages != null && noticeRoot.parent != hudMessages) noticeRoot.SetParent(hudMessages, false);
            if (!noticeIsForeground && notices.Count > 0) noticeRemaining = 0;
            if (noticeRemaining <= 0 && notices.Count > 0)
            { noticeText.text = notices.Dequeue(); noticeRemaining = 6; noticeIsForeground = true; noticeIsTutorial = false; }
            else if (noticeRemaining <= 0 && pages.Count == 0 && tutorialNotices.Count > 0)
            { noticeText.text = tutorialNotices.Dequeue(); noticeRemaining = 6; noticeIsForeground = false; noticeIsTutorial = true; }
            else if (noticeRemaining <= 0 && pages.Count == 0 && contextNotices.Count > 0)
            { noticeText.text = contextNotices.Dequeue(); noticeRemaining = 6; noticeIsForeground = false; noticeIsTutorial = false; }
            bool visible = noticeRemaining > 0 && (noticeIsForeground || pages.Count == 0);
            noticeRoot.gameObject.SetActive(false);
            if (pages.Count > 0)
            {
                UIPage top = pages[pages.Count - 1];
                top.NoticeArea.gameObject.SetActive(visible);
                if (visible)
                {
                    // 只在文字变化时重算尺寸，避免暂停期间每帧触发一次布局重建。
                    bool textChanged = top.NoticeLabel.text != noticeText.text;
                    top.NoticeLabel.text = noticeText.text;
                    if (textChanged)
                    {
                        // 提示框高度跟随文字：长提示（例如「设置已保存」这种两段文字）
                        // 在固定高度里会被裁掉尾巴，看起来像只有半句话。
                        Vector2 areaSize = top.NoticeArea.sizeDelta;
                        top.NoticeArea.sizeDelta = new Vector2(areaSize.x,
                            Mathf.Max(64f, top.NoticeLabel.preferredHeight + 24f));
                    }
                }
            }
            if (noticeRemaining > 0) UIWidgets.Size(noticeRoot, Mathf.Max(64, noticeText.preferredHeight + 40));
            if (subtitleRoot != null) subtitleRoot.SetAsLastSibling();
            PositionGameplayHud();
            if (visible) noticeRemaining -= Time.unscaledDeltaTime;
        }

        public void ShowTutorial(string id, string title, string body)
        {
            if (hud == null || (UISettings.Current != null && !UISettings.Current.tutorials) ||
                UIRunState.Progress.tutorialsSeen.Contains(id)) return;
            UIRunState.Progress.tutorialsSeen.Add(id);
            ShowVisualTutorial(id);
        }

        public void ShowHelp()
        {
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { ShowSandboxWorkshop(); return; }
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
            if (UISettings.Current != null && UISettings.Current.detailedHints) Toast(T("获得意识碎片 · ", "Memory found · ") + memory.title);
            else PulseVisualCue(UIGuideSymbol.Memory);
        }

        public void ShowJournal()
        {
            if (PAO.Sandbox.BubbleSandbox.Instance != null) { ShowSandboxWorkshop(); return; }
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
            if (tide == null) PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.ChapterFailure, Vector3.zero);
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
            resultShown = true; UIRunState.Complete(lastScene); bool saved = SaveSnapshot(UIRunState.AutoSlot);
            if (tide == null) PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.ChapterComplete, Vector3.zero);
            int index = Config.IndexOf(lastScene);

            // 【结局判定不能只看"是不是数组最后一个"】
            // 章节表末尾可能有**未完待续的占位关卡**（scene 为空、没有真实场景）。
            // 那些占位永远进不去，如果用"最后一个下标"判断结局，
            // 真正能通关的最后一关（比如第八关）完成时就永远看不到结局了。
            //
            // 所以改成往后找**下一个有真实场景的章节**：找不到，才说明这是终章。
            string followingScene = NextPlayableScene(index);
            bool ending = index >= 0 && string.IsNullOrEmpty(followingScene);

            UIPage page = Push(T("这一段水流，已有回响", "This current now has an echo"), ChapterName(lastScene), false, UIPageDesign.Completion);
            page.CanClose = false;
            if (string.IsNullOrEmpty(nextScene) && !string.IsNullOrEmpty(followingScene)) nextScene = followingScene;
            string next = nextScene;
            string primaryLabel = ending ? T("留下最终的回声", "Leave your final echo") :
                !string.IsNullOrEmpty(next) ? T("前往下一章", "Next chapter") : T("章节路径", "Chapter path");
            Action primary = ending ? (Action)ShowEnding : !string.IsNullOrEmpty(next) ?
                () => { SaveOnNextEntry = true; LoadScene(next); } : (Action)ShowChapters;
            page.gameObject.AddComponent<UIChapterCompletionView>().Build(page, Config, ChapterName(lastScene),
                index >= 0 ? index + 1 : 0, UIRunState.Progress.memories.Count, UIRunState.Progress.playSeconds,
                saved, primaryLabel, primary, ShowJournal, ShowChapters, () => LoadScene(Config.menuScene));
            Finish(page); return true;
        }

        /// <summary>
        /// 从 <paramref name="index"/> 之后找**第一个有真实场景**的章节。
        /// 找不到（或 index 无效）返回空串，表示这是终章。
        ///
        /// 跳过 scene 为空的"未完待续"占位关卡 —— 它们只用来在章节地图上占位展示。
        /// </summary>
        private string NextPlayableScene(int index)
        {
            if (index < 0 || Config.chapters == null) return "";
            for (int i = index + 1; i < Config.chapters.Length; i++)
            {
                string scene = Config.chapters[i].scene;
                if (!string.IsNullOrEmpty(scene)) return scene;
            }
            return "";
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
