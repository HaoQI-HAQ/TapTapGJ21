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
            UIPage page = Push(T("泡涌", "BUBBLE TIDE"), "", false, UIPageDesign.Menu); page.Rebuild = ShowMenu;
            UILowPolyOcean art = ui.Rect("Jellyfish illustration", page.transform).gameObject.AddComponent<UILowPolyOcean>();
            art.color = Color.white; art.raycastTarget = false;
            UIWidgets.Stretch(art.rectTransform); art.transform.SetSiblingIndex(1);
            art.gameObject.AddComponent<UIMenuAmbience>();

            RectTransform body = (RectTransform)page.transform.Find("Page");
            VerticalLayoutGroup layout = body.GetComponent<VerticalLayoutGroup>();
            body.anchorMin = new Vector2(.055f, .06f); body.anchorMax = new Vector2(.53f, .95f);
            layout.padding = new RectOffset(24, 24, 24, 16); layout.spacing = 12;
            Text heading = body.GetChild(0).GetComponent<Text>();
            heading.name = "Menu title"; heading.alignment = TextAnchor.MiddleLeft;
            heading.GetComponent<UIStyledText>().Configure(72, true, Config);
            UIWidgets.Size(heading.transform, -1);
            Text subtitle = ui.Label(body, T("意识之海", "THE SEA OF CONSCIOUSNESS"), 21, null, TextAnchor.MiddleLeft);
            subtitle.name = "Menu subtitle"; subtitle.transform.SetSiblingIndex(1);

            VerticalLayoutGroup contentLayout = page.Content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 8, 8);
            contentLayout.childForceExpandWidth = false; contentLayout.childAlignment = TextAnchor.UpperLeft;
            RectTransform actions = ui.Column("Menu actions", page.Content, 0, 10);
            LayoutElement actionSize = UIWidgets.Size(actions, -1, 560);
            actions.gameObject.AddComponent<UIMenuLayout>().Configure(actionSize);
            if (state.latest != null)
                MenuButton(actions, T("继续游戏", "Continue"), () => ContinueJourney(state.latest.slot), true);
            MenuButton(actions, T("新的旅程", "New journey"), RequestNewRun, state.latest == null);
            MenuButton(actions, T("章节路径", "Chapter path"), ShowChapters);
            MenuButton(actions, T("读取记录", "Load record"), ShowSaves);
            MenuButton(actions, T("游戏设置", "Settings"), ShowSettings);

            if (state.latest == null && (state.damaged > 0 || state.unavailable > 0))
            {
                Text status = ui.Label(page.Content, state.damaged > 0 ?
                    T("记录无法读取，可在「读取记录」恢复备份。", "Record unreadable. Restore a backup in Load record.") :
                    T("记录对应的章节暂不可用。", "The recorded chapter is currently unavailable."),
                    18, null, TextAnchor.MiddleCenter);
                status.name = "Record status";
                UIWidgets.Size(status.transform, -1, 560).layoutPriority = 3;
            }

            // Secondary actions reflow independently of the clipped main list.
            RectTransform secondary = ui.Rect("Secondary menu actions", page.Footer);
            UIWidgets.Size(secondary, -1, -1, 1);
            UIFlowLayout footerFlow = secondary.gameObject.AddComponent<UIFlowLayout>();
            footerFlow.maxColumns = 3; footerFlow.spacing = 12;
            UIWidgets.Size(page.Footer, -1);
            MenuSecondaryButton(secondary, T("操作图鉴", "Guide"), ShowHelp);
            MenuSecondaryButton(secondary, T("制作人员", "Credits"), ShowCredits);
            MenuSecondaryButton(secondary, T("离开", "Quit"), ConfirmQuit);
            body.gameObject.AddComponent<UIMenuResponsiveDesign>().Configure(heading, subtitle, contentLayout, footerFlow);
            AddMenuScrollbar(page.Content.GetComponentInParent<ScrollRect>());
            Finish(page);
        }

        private Button MenuButton(Transform parent, string title, Action click, bool primary = false)
        {
            UIGuideSymbol symbol = click == (Action)ShowChapters ? UIGuideSymbol.Goal : click == (Action)ShowSaves ? UIGuideSymbol.Journal :
                click == (Action)ShowSettings ? UIGuideSymbol.Steer : UIGuideSymbol.Arrow;
            Button button = ui.IconButton(parent, title, symbol, click, primary);
            Text label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft;
            if (primary) label.GetComponent<UIStyledText>().Configure(26, false, Config);
            button.gameObject.AddComponent<UIMenuButtonLayout>().Configure(label, null, primary ? 76 : 60);
            button.gameObject.AddComponent<UIMenuEntryMotion>().Configure(button.transform.Find("Action symbol").GetComponent<UIGuideGraphic>(), Config);
            return button;
        }

        private void MenuSecondaryButton(Transform parent, string title, Action click)
        {
            Button button = ui.Button(parent, title, click);
            button.GetComponent<Image>().color = new Color(1, 1, 1, .55f);
            Text label = button.GetComponentInChildren<Text>();
            label.GetComponent<UIStyledText>().Configure(20, false, Config);
            button.gameObject.AddComponent<UIMenuButtonLayout>().Configure(label, null, 48);
        }

        private void AddMenuScrollbar(ScrollRect scroll)
        {
            if (scroll.verticalScrollbar != null)
            {
                Navigation existing = scroll.verticalScrollbar.navigation; existing.mode = Navigation.Mode.None;
                scroll.verticalScrollbar.navigation = existing;
                return;
            }
            // Reserve a gutter so the handle never draws over a label or focus ring.
            scroll.viewport.offsetMax = new Vector2(-14, scroll.viewport.offsetMax.y);
            Image track = ui.Surface("Menu scroll track", scroll.transform, new Color(Config.ink.r, Config.ink.g, Config.ink.b, .08f), true);
            track.rectTransform.anchorMin = new Vector2(1, 0); track.rectTransform.anchorMax = Vector2.one;
            track.rectTransform.offsetMin = new Vector2(-5, 4); track.rectTransform.offsetMax = new Vector2(-1, -4);
            RectTransform area = ui.Rect("Sliding area", track.transform); UIWidgets.Stretch(area);
            Image handle = ui.Surface("Scroll handle", area, Config.teal, true); UIWidgets.Stretch(handle.rectTransform);
            Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform; bar.targetGraphic = handle; bar.direction = Scrollbar.Direction.BottomToTop;
            Navigation navigation = bar.navigation; navigation.mode = Navigation.Mode.None; bar.navigation = navigation;
            scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
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
            if (parent.Design == UIPageDesign.Menu && ReadMenuRecords().Signature != menuSignature)
                RefreshVisiblePage();
        }
    }
}
