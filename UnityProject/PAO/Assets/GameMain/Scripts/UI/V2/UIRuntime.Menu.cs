using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
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
            RectTransform body = (RectTransform)page.transform.Find("Page");
            body.GetComponent<VerticalLayoutGroup>().enabled = false;
            Text heading = body.GetChild(0).GetComponent<Text>();
            heading.name = "Menu title"; heading.alignment = TextAnchor.MiddleCenter;
            heading.GetComponent<UIStyledText>().Configure(112, true, Config);
            heading.resizeTextForBestFit = true; heading.resizeTextMinSize = 42; heading.resizeTextMaxSize = 112;
            Anchor(heading.rectTransform, new Vector2(.5f, .64f), Vector2.zero, new Vector2(760, 190), new Vector2(.5f, .5f));
            heading.gameObject.AddComponent<UIMenuHeroFit>().maximumWidth = 760;
            page.Content.GetComponentInParent<ScrollRect>().gameObject.SetActive(false);
            page.Footer.gameObject.SetActive(false);
            Image hit = ui.Surface("开始游戏", body, new Color(1, 1, 1, 0));
            Anchor(hit.rectTransform, new Vector2(.5f, .38f), Vector2.zero, new Vector2(300, 76), new Vector2(.5f, .5f));
            Button start = hit.gameObject.AddComponent<Button>(); start.targetGraphic = hit;
            Text startLabel = ui.Label(hit.transform, T("开始游戏", "Start game"), 34, Config.ink, TextAnchor.MiddleCenter);
            UIWidgets.Stretch(startLabel.rectTransform);
            bool opening = false;
            start.onClick.AddListener(() =>
            {
                if (opening) return;
                opening = true;
                if (UISettings.Current.reducedMotion) { ShowMenuActions(); return; }
                startLabel.DOColor(new Color(0, 0, 0, .25f), .24f).SetEase(Ease.InOutSine)
                    .SetUpdate(true).SetLink(hit.gameObject).OnComplete(ShowMenuActions);
            });
            Finish(page);
        }

        private void ShowMenuActions()
        {
            MenuRecordState state = ReadMenuRecords(); menuSignature = state.Signature;
            UIPage page = Push(T("泡涌", "BUBBLE TIDE"), "", false, UIPageDesign.Menu); page.Rebuild = ShowMenuActions;
            RectTransform body = (RectTransform)page.transform.Find("Page");
            body.anchorMin = new Vector2(.25f, .06f); body.anchorMax = new Vector2(.75f, .95f);
            body.offsetMin = body.offsetMax = Vector2.zero;
            body.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Text heading = body.GetChild(0).GetComponent<Text>();
            heading.alignment = TextAnchor.MiddleCenter;
            heading.GetComponent<UIStyledText>().Configure(92, true, Config);
            heading.resizeTextForBestFit = true; heading.resizeTextMinSize = 42; heading.resizeTextMaxSize = 92;
            UIMenuHeroFit fit = heading.gameObject.AddComponent<UIMenuHeroFit>();
            fit.maximumWidth = 760; fit.baseFontSize = 92;
            UIWidgets.Size(heading.transform, 140);
            VerticalLayoutGroup contentLayout = page.Content.GetComponent<VerticalLayoutGroup>();
            contentLayout.childForceExpandWidth = false; contentLayout.childAlignment = TextAnchor.UpperCenter;
            RectTransform actions = ui.Column("Menu actions", page.Content, 0, 10);
            LayoutElement actionSize = UIWidgets.Size(actions, -1, 480);
            actions.gameObject.AddComponent<UIMenuLayout>().Configure(actionSize);
            Button resume = MenuButton(actions, T("继续游戏", "Continue"), () => { if (state.latest != null) ContinueJourney(state.latest.slot); }, true);
            resume.interactable = state.latest != null;
            MenuButton(actions, T("新游戏", "New game"), RequestNewRun);
            MenuButton(actions, T("读档", "Load game"), ShowSaves);
            MenuButton(actions, T("设置", "Settings"), ShowSettings);
            page.Footer.gameObject.SetActive(false);
            RectTransform lowerEdge = ui.Row("Menu lower edge", page.transform, 12);
            lowerEdge.anchorMin = new Vector2(0, 0); lowerEdge.anchorMax = new Vector2(1, 0);
            lowerEdge.pivot = new Vector2(.5f, 0); lowerEdge.anchoredPosition = new Vector2(0, 20);
            lowerEdge.sizeDelta = new Vector2(-80, 50);
            HorizontalLayoutGroup footer = lowerEdge.GetComponent<HorizontalLayoutGroup>();
            footer.childForceExpandWidth = false; footer.childAlignment = TextAnchor.MiddleCenter;
            Text version = ui.Label(lowerEdge, T("版本 ", "Version ") + Application.version, 18, Config.ink);
            UIWidgets.Size(version.transform, 42, 240);
            RectTransform spacer = ui.Rect("Footer spacer", lowerEdge); UIWidgets.Size(spacer, 42, 0, 1);
            Button credits = ui.Button(lowerEdge, T("开发人员致谢", "Credits"), ShowCredits);
            UIWidgets.Size(credits.transform, 48, 240);
            AddMenuScrollbar(page.Content.GetComponentInParent<ScrollRect>());
            Finish(page);
            if (!UISettings.Current.reducedMotion)
            {
                Sequence arrival = DOTween.Sequence().SetUpdate(true).SetLink(actions.gameObject);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                for (int i = 0; i < actions.childCount; i++)
                {
                    Transform item = actions.GetChild(i);
                    CanvasGroup layer = item.gameObject.AddComponent<CanvasGroup>();
                    layer.alpha = 0; layer.interactable = false; layer.blocksRaycasts = false;
                    float at = .12f + i * .1f;
                    arrival.Insert(at, layer.DOFade(1, .32f));
                    bool first = i == 0;
                    arrival.InsertCallback(at + .32f, () =>
                    {
                        if (layer == null) return;
                        layer.interactable = true; layer.blocksRaycasts = true;
                        if (first) page.RestoreFocus();
                    });
                }
            }
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
            if (parent.Design != UIPageDesign.Menu) return;
            MenuRecordState state = ReadMenuRecords();
            if (state.Signature == menuSignature) return;
            menuSignature = state.Signature;
            if (state.latest != null) UIRunState.Apply(state.latest);
            else UIRunState.NewRun();
            RefreshVisiblePage();
        }
    }
}
