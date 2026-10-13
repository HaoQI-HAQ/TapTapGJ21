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
        private readonly List<Image> abilityRings = new List<Image>();
        private readonly Queue<string> notices = new Queue<string>();
        private readonly Queue<string> contextNotices = new Queue<string>();
        private bool noticeIsForeground;
        private RectTransform noticeRoot;
        private Text noticeText;
        private float noticeRemaining;
        private string objectiveOverride;
        private bool resultShown;
        private bool wasCharging;

        private void BuildHud()
        {
            hud = ui.Rect("Gameplay HUD", safeRoot); UIWidgets.Stretch(hud);
            int chapter = Config.IndexOf(lastScene);
            RectTransform objective = ui.Column("Objective", hud, 0, 8);
            Anchor(objective, new Vector2(0, 1), new Vector2(32, -28), new Vector2(760, 100), new Vector2(0, 1));
            ui.Label(objective, ChapterName(lastScene), 21, Config.mist);
            objectiveLabel = ui.Label(objective, objectiveOverride ?? (chapter >= 0 ? Config.chapters[chapter].objective :
                T("探索水域，寻找下一段水流。", "Explore the waters and find the next current.")), 24, Color.white);

            RectTransform type = ui.Column("Current ability", hud, 0, 8);
            Anchor(type, Vector2.zero, new Vector2(32, 32), new Vector2(600, 192), Vector2.zero);
            currentAbility = ui.Label(type, "", 28, Color.white);
            chargeLabel = ui.Label(type, T("按住左键蓄力 · E 与泡泡交互", "Hold left click to charge · E to interact"), 20, Color.white);
            Image chargeBackground = ui.Surface("Charge track", type, Config.ink, true); UIWidgets.Size(chargeBackground.transform, 8);
            chargeFill = ui.Surface("Charge fill", chargeBackground.transform, Config.teal, true);
            chargeFill.rectTransform.anchorMin = Vector2.zero; chargeFill.rectTransform.anchorMax = new Vector2(0, 1);
            chargeFill.rectTransform.offsetMin = chargeFill.rectTransform.offsetMax = Vector2.zero;

            RectTransform abilities = ui.Rect("Three bubble abilities", hud);
            Anchor(abilities, new Vector2(1, 0), new Vector2(-32, 48), new Vector2(350, 340), new Vector2(1, 0));
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
                Image ring = ui.Surface("Selection ring", circle.transform, Config.teal);
                UIWidgets.Stretch(ring.rectTransform, -8); ring.sprite = Config.ring; ring.raycastTarget = false;
                abilityRings.Add(ring);
                if (Config.abilityIcons != null && i < Config.abilityIcons.Length && Config.abilityIcons[i] != null)
                {
                    Image icon = ui.Surface("Ability symbol", circle.transform, Config.ink);
                    UIWidgets.Stretch(icon.rectTransform, 25); icon.sprite = Config.abilityIcons[i]; icon.raycastTarget = false;
                }
                Text name = ui.Label(circle.transform, (i + 1) + " · " + AbilityName(i), 20, Color.white, TextAnchor.MiddleCenter);
                Anchor(name.rectTransform, new Vector2(0.5f, 0), new Vector2(0, -28), new Vector2(180, 48), new Vector2(0.5f, 0.5f));
            }
            RectTransform buttons = ui.Row("HUD actions", hud, 12);
            Anchor(buttons, Vector2.one, new Vector2(-32, -28), new Vector2(340, 64), Vector2.one);
            ui.Button(buttons, T("日志 J", "Journal J"), ShowJournal);
            ui.Button(buttons, T("暂停 Esc", "Pause Esc"), ShowPause);
            RefreshHud();
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        private void ClearHud()
        {
            if (hud != null) { hud.gameObject.SetActive(false); Destroy(hud.gameObject); }
            hud = null; abilityRings.Clear(); currentAbility = null;
            if (noticeRoot != null) Destroy(noticeRoot.gameObject);
            noticeRoot = null; notices.Clear(); contextNotices.Clear(); noticeRemaining = 0;
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
            if (hud == null || currentAbility == null || switcher == null) return;
            int type = (int)switcher.Current;
            string name = AbilityName(type);
            if (currentAbility.text != name) currentAbility.text = name;
            for (int i = 0; i < abilityRings.Count; i++)
            {
                bool selected = i == type;
                abilityRings[i].enabled = selected;
                abilityRings[i].transform.parent.localScale = Vector3.one * (selected && !UISettings.Current.reducedMotion ? 1.08f : 1);
            }
            if (launcher != null && chargeLabel != null)
            {
                string status = launcher.IsCharging ? T("正在蓄力 ", "Charging ") + Mathf.RoundToInt(launcher.ChargeProgress * 100) + "%" :
                    launcher.HasHeldBubble ? T("泡泡已就绪 · 点击左键发射", "Bubble ready · Click to launch") :
                    UISettings.Current.toggleCharge ? T("点击蓄力 · 再次点击释放", "Click to charge · Click again to release") :
                    T("按住左键蓄力 · E 与泡泡交互", "Hold left click to charge · E to interact");
                if (chargeLabel.text != status) chargeLabel.text = status;
                chargeFill.rectTransform.anchorMax = new Vector2(launcher.IsCharging ? launcher.ChargeProgress : 0, 1);
                if (wasCharging && !launcher.IsCharging) ShowTutorial("first-cast", T("让泡泡成为你的路径", "Let bubbles become your path"),
                    T("留意泡泡与地形的关系。靠近泡泡，尝试 E 交互。", "Watch how bubbles interact with the terrain. Approach one and try E."));
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
            if (UISettings.Current.subtitles) Toast(speaker + "\n" + text, false);
        }

        public void Toast(string message, bool showOverModals = true)
        {
            if (string.IsNullOrEmpty(message) || notices.Count + contextNotices.Count >= 8) return;
            if (showOverModals) notices.Enqueue(message); else contextNotices.Enqueue(message);
        }

        private void UpdateTransientUI()
        {
            if (noticeRoot == null)
            {
                Image paper = ui.Surface("Context notice", safeRoot, Config.mist, true);
                noticeRoot = paper.rectTransform;
                Anchor(noticeRoot, new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(840, 72), new Vector2(0.5f, 1));
                paper.raycastTarget = false;
                noticeText = ui.Label(noticeRoot, "", 23, Config.ink, TextAnchor.MiddleCenter);
                UIWidgets.Stretch(noticeText.rectTransform, 20);
            }
            if (!noticeIsForeground && notices.Count > 0) noticeRemaining = 0;
            if (noticeRemaining <= 0 && notices.Count > 0)
            { noticeText.text = notices.Dequeue(); noticeRemaining = 6; noticeIsForeground = true; }
            else if (noticeRemaining <= 0 && pages.Count == 0 && contextNotices.Count > 0)
            { noticeText.text = contextNotices.Dequeue(); noticeRemaining = 6; noticeIsForeground = false; }
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
            if (!UISettings.Current.tutorials || UIRunState.Progress.tutorialsSeen.Contains(id)) return;
            UIRunState.Progress.tutorialsSeen.Add(id); Toast(title + "\n" + body, false);
        }

        public void ShowHelp()
        {
            UIPage page = Push(T("操作与教学", "Controls & tutorials"), T("任何时候都可以回到这里复习。", "Return here at any time for a refresher."));
            page.Rebuild = ShowHelp;
            string[,] topics = {
                { T("探索", "Explore"), T("WASD 移动，空格跳跃。鼠标改变视角，按住 Shift 加速。", "WASD to move, Space to jump. Mouse to look, hold Shift to sprint.") },
                { T("泡泡", "Bubbles"), T("1 / 2 / 3 或滚轮切换泡泡。按住左键蓄力，松开释放。较大的泡泡会停留在发射口，再次点击发射。", "1 / 2 / 3 or wheel to switch. Hold left click to charge and release. Large bubbles wait at the muzzle; click again to launch.") },
                { T("交互", "Interact"), T("靠近泡泡后按 E 交互。根据环境组合黏附、弹跳与爆破。", "Approach a bubble and press E. Combine adhesion, bouncing and explosions with the environment.") },
                { T("记录", "Records"), T("Esc 打开暂停，保存到三个手动槽位。完成章节会留下自动记录。J 打开意识碎片日志。", "Esc to pause and save to three manual slots. Completing a chapter creates an automatic record. J opens the memory journal.") },
                { T("导航", "Navigation"), T("方向键或 Tab 移动焦点，Enter 确认，Esc 返回。手柄 A 确认、B 返回、菜单键暂停。", "Arrows or Tab move focus, Enter submits, Esc returns. Controller A submits, B returns, Menu pauses.") } };
            for (int i = 0; i < topics.GetLength(0); i++)
            { ui.Label(page.Content, topics[i, 0], 30); ui.Label(page.Content, topics[i, 1], 24); }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void CollectMemory(MemoryEntry memory)
        {
            if (!UIRunState.AddMemory(memory)) return;
            Toast(T("获得意识碎片 · ", "Memory found · ") + memory.title);
        }

        public void ShowJournal()
        {
            UIPage page = Push(T("意识碎片", "Memory journal"),
                T("你在水流中拾起的片段，将在这里留下回声。", "Fragments gathered in the current echo here."));
            page.Rebuild = ShowJournal;
            if (UIRunState.Progress.memories.Count == 0)
                ui.Label(page.Content, T("尚未拾起意识碎片。探索时留意水域中可以互动的物体。", "No memories yet. Look for objects you can interact with as you explore."), 26);
            foreach (MemoryEntry memory in UIRunState.Progress.memories)
            {
                if (memory == null) continue;
                ui.Label(page.Content, ChapterName(memory.chapter) + "  ·  " + memory.title, 28);
                ui.Label(page.Content, memory.body, 24);
            }
            ui.Button(page.Footer, T("返回", "Back"), Pop); Finish(page);
        }

        public void ShowDialogue(DialogueAsset dialogue)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Length == 0 || loading) return;
            int line = 0;
            UIPage page = Push(dialogue.title, "", true);
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
            UIPage page = Push(dialogue.title, T("选择你想留下的回声。", "Choose the echo you wish to leave."), true);
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
        private void OnLevelLost()
        {
            if (resultShown) return; resultShown = true;
            UIPage page = Push(T("水流暂时淹没了路径", "The current covered the path"),
                T("换一种泡泡组合，再试一次。", "Try a different combination of bubbles."));
            page.CanClose = false;
            ui.Button(page.Content, T("重试本章", "Retry chapter"), () => LoadScene(lastScene), true);
            ui.Button(page.Content, T("操作与教学", "Controls & tutorials"), ShowHelp);
            ui.Button(page.Footer, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Footer, T("主菜单", "Main menu"), () => LoadScene(Config.menuScene)); Finish(page);
        }

        public bool CompleteChapter(string nextScene = null)
        {
            if (!IsGameplay || resultShown || loading) return false;
            resultShown = true; UIRunState.Complete(lastScene); SaveSnapshot(UIRunState.AutoSlot);
            int index = Config.IndexOf(lastScene);
            bool ending = index >= 0 && index == Config.chapters.Length - 1;
            UIPage page = Push(T("这一段水流，已有回响", "This current now has an echo"), ChapterName(lastScene));
            page.CanClose = false;
            ui.Label(page.Content, T("章节完成", "Chapter completed"), 36);
            ui.Label(page.Content, T("意识碎片 ", "Memories ") + UIRunState.Progress.memories.Count + "  ·  " +
                T("旅程时间 ", "Journey time ") + TimeSpan.FromSeconds(UIRunState.Progress.playSeconds).ToString(@"hh\:mm\:ss"), 26);
            if (string.IsNullOrEmpty(nextScene) && index >= 0 && index + 1 < Config.chapters.Length) nextScene = Config.chapters[index + 1].scene;
            string next = nextScene;
            if (ending) ui.Button(page.Content, T("留下最终的回声", "Leave your final echo"), ShowEnding, true);
            else if (!string.IsNullOrEmpty(next)) ui.Button(page.Content, T("前往下一章", "Next chapter"), () => { SaveOnNextEntry = true; LoadScene(next); }, true);
            ui.Button(page.Content, T("回看意识碎片", "Review memories"), ShowJournal);
            ui.Button(page.Footer, T("章节路径", "Chapter path"), ShowChapters);
            ui.Button(page.Footer, T("主菜单", "Main menu"), () => LoadScene(Config.menuScene)); Finish(page); return true;
        }

        public void ShowEnding()
        {
            UIPage page = Push(T("当水流归于平静", "When the current rests"),
                T("你可以先回看旅程，再决定留下怎样的回声。", "Review your journey before choosing the echo to leave."));
            ui.Button(page.Content, T("回看意识碎片", "Review memories"), ShowJournal);
            if (Config.endings == null || Config.endings.Length == 0)
                ui.Label(page.Content, T("这段旅程的结局尚待写入，你可以继续回看意识碎片。", "The ending of this journey is still unwritten. You can continue reviewing your memories."), 26);
            foreach (EndingDefinition ending in Config.endings)
            {
                EndingDefinition choice = ending;
                bool eligible = UIRunState.Progress.completed.Contains(lastScene) &&
                    UIRunState.Progress.memories.Count >= choice.requiredMemories;
                ui.Label(page.Content, choice.description, 24);
                if (!eligible) ui.Label(page.Content, T("需要完成本章并收集意识碎片：", "Complete this chapter and collect memories: ") + choice.requiredMemories, 22);
                ui.Button(page.Content, choice.title, () => Confirm(choice.title,
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
