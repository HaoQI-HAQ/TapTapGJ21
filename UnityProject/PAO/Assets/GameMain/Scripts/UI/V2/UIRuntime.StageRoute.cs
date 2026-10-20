using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        // The chapter page is a compact route. The larger atlas remains available to
        // the save screen, where a record needs its own preview.
        private void BuildStageRoute(Transform parent)
        {
            if (Config.chapters == null || Config.chapters.Length == 0)
            {
                ui.Label(parent, T("暂无可浏览的章节", "No chapters available"));
                return;
            }

            SaveSlotData latest = IsGameplay ? null : ReadMenuRecords().latest;
            RunProgress progress = IsGameplay ? UIRunState.Progress :
                latest != null && latest.runProgress != null ? latest.runProgress : UIRunState.Progress;
            if (progress == null) progress = new RunProgress();
            string currentScene = IsGameplay ? lastScene : latest != null ? latest.sceneName : "";
            int selected = Mathf.Max(0, Config.IndexOf(currentScene));
            bool narrow = safeRoot.rect.width < 1320 ||
                UISettings.Current.textScale > 1.15f && safeRoot.rect.width < 1500;
            float shellHeight = narrow ? safeRoot.rect.width < 600 ? 1230 : 1110 : 552;
            float mapSpace = narrow ? 528 : shellHeight;
            float split = narrow ? 1f - mapSpace / shellHeight : 0f;

            RectTransform shell = ui.Rect("Stage route", parent);
            UIWidgets.Size(shell, shellHeight);
            Image sheet = shell.gameObject.AddComponent<Image>();
            sheet.color = Color.white; sheet.raycastTarget = false;
            Outline border = shell.gameObject.AddComponent<Outline>();
            border.effectColor = Color.black; border.effectDistance = Vector2.one;

            RectTransform map = ui.Rect("Route map", shell);
            RectTransform detail = ui.Rect("Selected stage details", shell);
            if (narrow)
            {
                map.anchorMin = new Vector2(0, split); map.anchorMax = Vector2.one;
                map.offsetMin = new Vector2(16, 12); map.offsetMax = new Vector2(-16, -16);
                detail.anchorMin = Vector2.zero; detail.anchorMax = new Vector2(1, split);
                detail.offsetMin = new Vector2(20, 16); detail.offsetMax = new Vector2(-20, -16);
                RouteRule(shell, new Vector2(0, split), new Vector2(1, split),
                    new Vector2(18, -1), new Vector2(-18, 1));
            }
            else
            {
                map.anchorMin = Vector2.zero; map.anchorMax = new Vector2(.75f, 1);
                map.offsetMin = new Vector2(18, 16); map.offsetMax = new Vector2(-12, -16);
                detail.anchorMin = new Vector2(.75f, 0); detail.anchorMax = Vector2.one;
                detail.offsetMin = new Vector2(16, 16); detail.offsetMax = new Vector2(-18, -16);
                RouteRule(shell, new Vector2(.75f, 0), new Vector2(.75f, 1),
                    new Vector2(-1, 16), new Vector2(1, -16));
            }

            RouteText(map, "Route heading", narrow ? T("路线", "ROUTE") :
                T("章节路线  /  STAGE ROUTE", "STAGE ROUTE"),
                19, 4, 222, 4, 43, TextAnchor.MiddleLeft, true);
            Text counter = RouteText(map, "Route counter", "", 17,
                0, 148, 4, 43, TextAnchor.MiddleRight);
            Text hint = RouteText(map, "Route hint", narrow ? T("拖动或滚轮浏览", "Drag or scroll") :
                T("拖动、滚轮或方向键浏览", "Drag, scroll or use direction keys"),
                16, 4, 4, 0, 30, TextAnchor.MiddleLeft);
            hint.rectTransform.anchorMin = Vector2.zero;
            hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.offsetMin = new Vector2(4, 0);
            hint.rectTransform.offsetMax = new Vector2(-4, 30);

            RectTransform viewport = ui.Rect("Route viewport", map);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(0, 38); viewport.offsetMax = new Vector2(0, -56);
            Image viewPaper = viewport.gameObject.AddComponent<Image>();
            viewPaper.color = Color.white;
            viewport.gameObject.AddComponent<RectMask2D>();
            UIHorizontalScrollRect scroll = viewport.gameObject.AddComponent<UIHorizontalScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42; scroll.viewport = viewport;

            RectTransform strip = ui.Rect("Connected stages", viewport);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, .5f); strip.anchoredPosition = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            float nodeWidth = narrow ? 156 : 134;
            float firstCenter = narrow ? 96 : 82;
            float spacing = narrow ? 218 : Mathf.Clamp(
                (viewport.rect.width - 164) / Mathf.Max(1, Config.chapters.Length - 1), 162, 190);
            strip.sizeDelta = new Vector2(Mathf.Max(viewport.rect.width,
                firstCenter + (Config.chapters.Length - 1) * spacing + nodeWidth * .5f + 15), 0);
            scroll.content = strip;
            UIStageRouteLine route = ui.Rect("Right angle route", strip).gameObject.AddComponent<UIStageRouteLine>();
            UIWidgets.Stretch(route.rectTransform);
            route.color = Color.black; route.raycastTarget = false;

            Text detailNumber = RouteText(detail, "Selected number", "", narrow ? 54 : 68,
                0, 0, 42, 82, TextAnchor.MiddleLeft, true);
            detailNumber.rectTransform.anchorMin = detailNumber.rectTransform.anchorMax = new Vector2(0, 1);
            detailNumber.rectTransform.pivot = new Vector2(0, 1);
            detailNumber.rectTransform.anchoredPosition = new Vector2(0, -42);
            detailNumber.rectTransform.sizeDelta = new Vector2(98, 82);
            // The display font has no Latin digit glyphs on some systems.
            detailNumber.font = counter.font;
            detailNumber.resizeTextForBestFit = true;
            detailNumber.resizeTextMinSize = 40;
            detailNumber.resizeTextMaxSize = narrow ? 72 : 88;
            Text detailTitle = RouteText(detail, "Selected title", "", 31,
                102, 0, 44, 80, TextAnchor.MiddleLeft, true);
            detailTitle.resizeTextForBestFit = true;
            detailTitle.resizeTextMinSize = 23; detailTitle.resizeTextMaxSize = 40;
            RouteText(detail, "Dossier label", T("关卡档案  /  STAGE FILE", "STAGE FILE"),
                16, 0, narrow ? 118 : 0, 4, 30, TextAnchor.MiddleLeft, true);
            RouteRule(detail, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -136), new Vector2(0, -134));
            Text detailStatus = RouteText(detail, "Selected status", "", 20,
                0, 0, 144, 36, TextAnchor.MiddleLeft, true);
            Text detailScene = RouteText(detail, "Selected scene", "", 16,
                0, 0, 184, 28, TextAnchor.MiddleLeft);
            RouteText(detail, "Description label", T("区域概述", "DESCRIPTION"), 16,
                0, 0, 219, 22, TextAnchor.MiddleLeft, true);
            Text description = RouteText(detail, "Selected description", "", 22,
                0, 0, 245, 76, TextAnchor.UpperLeft);
            description.resizeTextForBestFit = true;
            description.resizeTextMinSize = 16; description.resizeTextMaxSize = 29;
            RouteText(detail, "Objective label", T("本章目标", "OBJECTIVE"), 16,
                0, 0, 330, 22, TextAnchor.MiddleLeft, true);
            Text objective = RouteText(detail, "Selected objective", "", 20,
                0, 0, 358, 59, TextAnchor.UpperLeft);
            objective.resizeTextForBestFit = true;
            objective.resizeTextMinSize = 15; objective.resizeTextMaxSize = 26;
            Text reason = RouteText(detail, "Entry reason", "", 16,
                0, 0, 404, 36, TextAnchor.MiddleLeft);
            reason.resizeTextForBestFit = true;
            reason.resizeTextMinSize = 13; reason.resizeTextMaxSize = 21;

            Button enter = RouteButton(detail, "Enter stage", T("进入关卡", "ENTER STAGE"), 24);
            RectTransform enterRect = (RectTransform)enter.transform;
            enterRect.anchorMin = new Vector2(0, 0); enterRect.anchorMax = new Vector2(1, 0);
            enterRect.offsetMin = new Vector2(0, 8); enterRect.offsetMax = new Vector2(0, 65);

            Button[] nodes = new Button[Config.chapters.Length];
            Text[] nodeStates = new Text[nodes.Length];
            Image[] selectionBars = new Image[nodes.Length];
            Outline[] frames = new Outline[nodes.Length];
            bool[] reachable = new bool[nodes.Length];
            UIStageRouteNodeMotion[] motions = new UIStageRouteNodeMotion[nodes.Length];
            Button previous = null, next = null;
            Button backToRoute = null;
            Action<int, bool> choose = null;
            Action redraw = () =>
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    ChapterDefinition chapter = Config.chapters[i];
                    bool done = progress.completed != null && progress.completed.Contains(chapter.scene);
                    bool unlocked = i == 0 || progress.completed != null &&
                        progress.completed.Contains(Config.chapters[i - 1].scene);

                    // 【scene 为空 = 未完待续的占位关卡】
                    // 这类节点没有真实场景可加载，但也不该显示成"不可用"（那读起来像"构建缺失"）。
                    // 小标签仍用"未解锁"，因为节点右上角那块地方很窄，
                    // "未完待续"四个字会溢出；大标题（chapter.title）里已经写了"未完待续"。
                    bool missing = string.IsNullOrEmpty(chapter.scene);
                    bool available = !missing && Application.CanStreamedLevelBeLoaded(chapter.scene);
                    reachable[i] = unlocked && available;
                    nodeStates[i].text = missing ? T("未解锁", "LOCKED") :
                        !available ? T("不可用", "OFFLINE") : done ?
                        T("已完成", "CLEARED") : currentScene == chapter.scene ? T("当前", "CURRENT") :
                        unlocked ? T("已开放", "OPEN") : T("未解锁", "LOCKED");
                    selectionBars[i].gameObject.SetActive(i == selected);
                    frames[i].effectColor = i == selected ? Color.black :
                        new Color(.48f, .48f, .48f);
                    frames[i].effectDistance = i == selected ? new Vector2(2, 2) : Vector2.one;
                }
                route.SetReachable(reachable);
                ChapterDefinition chosen = Config.chapters[selected];
                bool completed = progress.completed != null && progress.completed.Contains(chosen.scene);
                bool canUnlock = selected == 0 || progress.completed != null &&
                    progress.completed.Contains(Config.chapters[selected - 1].scene);
                bool canLoad = Application.CanStreamedLevelBeLoaded(chosen.scene);
                bool chosenMissing = string.IsNullOrEmpty(chosen.scene);
                string state = chosenMissing ? T("未完待续", "TO BE CONTINUED") :
                    !canLoad ? T("暂不可进入", "Unavailable") : completed ?
                    T("已完成", "Completed") : currentScene == chosen.scene ?
                    T("当前章节", "Current stage") : canUnlock ? T("已开放", "Available") :
                    T("未解锁", "Locked");
                counter.text = (selected + 1).ToString("00") + " / " + nodes.Length.ToString("00");
                detailNumber.text = (selected + 1).ToString("00");
                detailTitle.text = chosen.title;
                detailStatus.text = T("航线状态  /  ", "STATUS  /  ") + state;
                detailScene.text = T("场景  /  ", "SCENE  /  ") +
                    (chosenMissing ? "—" : chosen.scene);
                description.text = string.IsNullOrWhiteSpace(chosen.description) ? "—" : chosen.description;
                objective.text = string.IsNullOrWhiteSpace(chosen.objective) ? "—" : chosen.objective;
                reason.text = chosenMissing ? T("此关卡尚在制作中。", "This stage is still in production.") :
                    !canLoad ? T("此关卡暂未包含在当前版本中。", "This stage is not in this build.") :
                    !canUnlock ? T("完成前一关卡后开放。", "Complete the previous stage to unlock.") :
                    T("从本章起点进入。", "Enter from the stage start.");
                enter.interactable = canUnlock && canLoad;
                Text enterText = enter.GetComponentInChildren<Text>();
                if (enterText != null) enterText.color = enter.interactable ? Color.black :
                    new Color(.5f, .5f, .5f);
                Navigation entryNav = enter.navigation;
                entryNav.mode = Navigation.Mode.Explicit;
                entryNav.selectOnUp = nodes[selected];
                enter.navigation = entryNav;
                if (previous != null && next != null)
                {
                    previous.interactable = selected > 0;
                    next.interactable = selected + 1 < nodes.Length;
                    Navigation prevNav = previous.navigation;
                    prevNav.mode = Navigation.Mode.Explicit;
                    prevNav.selectOnRight = nodes[selected];
                    previous.navigation = prevNav;
                    Navigation nextNav = next.navigation;
                    nextNav.mode = Navigation.Mode.Explicit;
                    nextNav.selectOnLeft = nodes[selected];
                    next.navigation = nextNav;
                }
            };

            choose = (index, revealDetail) =>
            {
                selected = Mathf.Clamp(index, 0, nodes.Length - 1);
                redraw();
                Canvas.ForceUpdateCanvases();
                float range = Mathf.Max(0, strip.rect.width - viewport.rect.width);
                float center = firstCenter + selected * spacing;
                scroll.horizontalNormalizedPosition = range > 1 ?
                    Mathf.Clamp01((center - viewport.rect.width * .5f) / range) : 0;
                if (revealDetail && narrow)
                {
                    RevealRouteDetail(parent, detail);
                    if (enter.IsInteractable()) enter.Select();
                    else if (backToRoute != null) backToRoute.Select();
                }
            };

            // Create the current node first so the page's default keyboard focus
            // agrees with the dossier shown on opening.
            for (int order = 0; order < nodes.Length; order++)
            {
                int index = (selected + order) % nodes.Length;
                ChapterDefinition chapter = Config.chapters[index];
                Button node = RouteButton(strip, "Stage " + (index + 1).ToString("00"), "", 20);
                RectTransform rect = (RectTransform)node.transform;
                float[] heights = { .43f, .67f, .38f, .73f, .47f, .62f };
                rect.anchorMin = rect.anchorMax = new Vector2(0, heights[index % heights.Length]);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(firstCenter + index * spacing, 0);
                rect.sizeDelta = new Vector2(nodeWidth, 92);
                Text defaultLabel = node.GetComponentInChildren<Text>();
                if (defaultLabel != null) defaultLabel.gameObject.SetActive(false);
                frames[index] = node.GetComponent<Outline>();
                Image bar = ui.Surface("Selected edge", node.transform, Color.black);
                bar.raycastTarget = false;
                bar.rectTransform.anchorMin = Vector2.zero;
                bar.rectTransform.anchorMax = new Vector2(0, 1);
                bar.rectTransform.offsetMin = Vector2.zero;
                bar.rectTransform.offsetMax = new Vector2(4, 0);
                selectionBars[index] = bar;
                Text id = RouteText(node.transform, "Stage index", (index + 1).ToString("00"),
                    17, 12, 89, 7, 27, TextAnchor.MiddleLeft, true);
                id.font = counter.font;
                nodeStates[index] = RouteText(node.transform, "Stage state", "", 14,
                    67, 10, 7, 27, TextAnchor.MiddleRight);
                Text title = RouteText(node.transform, "Stage title", chapter.title, 19,
                    12, 9, 37, 45, TextAnchor.MiddleLeft, true);
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 14; title.resizeTextMaxSize = 25;
                motions[index] = node.gameObject.AddComponent<UIStageRouteNodeMotion>();
                motions[index].Configure(route, index * 1.27f, 7,
                    () => choose(index, false));
                node.onClick.AddListener(() => choose(index, true));
                nodes[index] = node;
            }
            route.SetNodes(Array.ConvertAll(nodes, node => (RectTransform)node.transform));

            previous = RouteButton(map, "Previous stage", "", 20);
            next = RouteButton(map, "Next stage", "", 20);
            RouteArrowGlyph(previous, true);
            RouteArrowGlyph(next, false);
            Anchor((RectTransform)previous.transform, new Vector2(1, 1),
                new Vector2(-88, -8), new Vector2(42, 40), new Vector2(1, 1));
            Anchor((RectTransform)next.transform, new Vector2(1, 1),
                new Vector2(-39, -8), new Vector2(42, 40), new Vector2(1, 1));
            previous.onClick.AddListener(() =>
            {
                if (selected <= 0) return;
                nodes[selected - 1].Select(); choose(selected - 1, false);
            });
            next.onClick.AddListener(() =>
            {
                if (selected + 1 >= nodes.Length) return;
                nodes[selected + 1].Select(); choose(selected + 1, false);
            });
            enter.onClick.AddListener(() =>
            {
                int index = selected;
                ChapterDefinition chapter = Config.chapters[index];
                bool unlocked = index == 0 || progress.completed != null &&
                    progress.completed.Contains(Config.chapters[index - 1].scene);
                if (!unlocked || !Application.CanStreamedLevelBeLoaded(chapter.scene)) return;
                Confirm(chapter.title,
                    T("从本章起点进入。当前未保存的探索会重置。", "Enter from the chapter start. Unsaved exploration will reset."),
                    () => { SaveOnNextEntry = true; LoadScene(chapter.scene); });
            });

            for (int i = 0; i < nodes.Length; i++)
            {
                Navigation navigation = nodes[i].navigation;
                navigation.mode = Navigation.Mode.Explicit;
                navigation.selectOnLeft = i > 0 ? nodes[i - 1] : previous;
                navigation.selectOnRight = i + 1 < nodes.Length ? nodes[i + 1] : next;
                navigation.selectOnUp = i > 0 ? nodes[i - 1] : previous;
                navigation.selectOnDown = enter;
                nodes[i].navigation = navigation;
            }
            if (narrow)
            {
                backToRoute = RouteButton(detail, "Back to route", T("↑ 返回路线", "↑ ROUTE"), 15);
                Anchor((RectTransform)backToRoute.transform, new Vector2(1, 1),
                    new Vector2(0, -3), new Vector2(108, 30), new Vector2(1, 1));
                backToRoute.onClick.AddListener(() =>
                {
                    ScrollRect outer = parent.GetComponentInParent<ScrollRect>();
                    if (outer != null) outer.verticalNormalizedPosition = 1;
                    nodes[selected].Select();
                });
            }

            redraw();
            Canvas.ForceUpdateCanvases();
            choose(selected, false);
        }

        private void RevealRouteDetail(Transform parent, RectTransform detail)
        {
            ScrollRect outer = parent.GetComponentInParent<ScrollRect>();
            if (outer == null || outer.content == null || outer.viewport == null) return;
            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(outer.content, detail);
            float fromTop = outer.content.rect.yMax - bounds.max.y;
            float range = Mathf.Max(0, outer.content.rect.height - outer.viewport.rect.height);
            outer.StopMovement();
            outer.verticalNormalizedPosition = range > 1 ?
                1 - Mathf.Clamp01((fromTop - 8) / range) : 1;
        }

        private Text RouteText(Transform parent, string name, string value, int size,
            float left, float right, float top, float height, TextAnchor alignment,
            bool bold = false)
        {
            Text label = ui.Label(parent, value, size, Color.black, alignment);
            label.name = name;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            LayoutElement layout = label.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            return label;
        }

        private Image RouteRule(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            Image rule = ui.Surface("Route rule", parent, Color.black);
            rule.raycastTarget = false;
            RectTransform rect = rule.rectTransform;
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            return rule;
        }

        private Button RouteButton(Transform parent, string name, string caption, int fontSize)
        {
            Image paper = ui.Surface(name, parent, Color.white);
            Outline frame = paper.gameObject.AddComponent<Outline>();
            frame.effectColor = Color.black; frame.effectDistance = Vector2.one;
            Button button = paper.gameObject.AddComponent<Button>();
            button.targetGraphic = paper;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = colors.selectedColor = new Color(.92f, .92f, .92f);
            colors.pressedColor = new Color(.82f, .82f, .82f);
            colors.disabledColor = new Color(.93f, .93f, .93f);
            button.colors = colors;
            Text label = ui.Label(button.transform, caption, fontSize, Color.black, TextAnchor.MiddleCenter);
            label.name = "Button label"; UIWidgets.Stretch(label.rectTransform, 4);
            LayoutElement layout = label.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            button.gameObject.AddComponent<UIFocusRing>().Configure(Color.black);
            return button;
        }

        private void RouteArrowGlyph(Button button, bool pointsLeft)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.gameObject.SetActive(false);
            for (int part = 0; part < 2; part++)
            {
                Image stroke = ui.Surface(part == 0 ? "Arrow upper" : "Arrow lower",
                    button.transform, Color.black);
                stroke.raycastTarget = false;
                RectTransform rect = stroke.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(12, 2);
                rect.anchoredPosition = new Vector2(pointsLeft ? -1 : 1, part == 0 ? 4 : -4);
                rect.localRotation = Quaternion.Euler(0, 0,
                    pointsLeft ? part == 0 ? 45 : -45 : part == 0 ? -45 : 45);
            }
        }
    }
}
