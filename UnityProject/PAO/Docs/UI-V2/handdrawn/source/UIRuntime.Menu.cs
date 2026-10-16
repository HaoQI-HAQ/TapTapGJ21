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
            UIPage page = Push(T("泡涌", "BUBBLE TIDE")); page.Rebuild = ShowMenu;
            Image background = page.GetComponent<Image>();
            background.sprite = Config.menuArt; background.type = Image.Type.Simple; background.color = Color.white;
            background.gameObject.AddComponent<UIMenuAmbience>();

            RectTransform body = (RectTransform)page.transform.Find("Page");
            VerticalLayoutGroup layout = body.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 28); layout.spacing = 12;
            Text heading = body.GetChild(0).GetComponent<Text>();
            heading.name = "Menu title"; heading.alignment = TextAnchor.MiddleCenter;
            heading.GetComponent<UIStyledText>().Configure(72, true, Config);
            UIWidgets.Size(heading.transform, 116 * UISettings.Current.textScale);
            Text subtitle = ui.Label(body, T("意识之海", "THE SEA OF CONSCIOUSNESS"), 21, null, TextAnchor.MiddleCenter);
            subtitle.name = "Menu subtitle"; subtitle.transform.SetSiblingIndex(1);

            VerticalLayoutGroup contentLayout = page.Content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(12, 12, 12, 12);
            contentLayout.childForceExpandWidth = false; contentLayout.childAlignment = TextAnchor.UpperCenter;
            RectTransform actions = ui.Column("Menu actions", page.Content, 4, 10);
            LayoutElement actionSize = UIWidgets.Size(actions, -1, 420);
            actions.gameObject.AddComponent<UIMenuLayout>().Configure(actionSize);
            if (state.latest != null)
                MenuButton(actions, T("继续游戏", "Continue"), () => ContinueJourney(state.latest.slot), true);
            MenuButton(actions, T("新的旅程", "New journey"), RequestNewRun, state.latest == null);
            MenuButton(actions, T("章节路径", "Chapter path"), ShowChapters);
            MenuButton(actions, T("读取记录", "Load record"), ShowSaves);
            MenuButton(actions, T("游戏设置", "Settings"), ShowSettings);
            MenuButton(actions, T("制作人员", "Credits"), ShowCredits);
            MenuButton(actions, T("离开意识之海", "Quit"), ConfirmQuit);

            if (state.latest == null && (state.damaged > 0 || state.unavailable > 0))
            {
                Text status = ui.Label(page.Content, state.damaged > 0 ?
                    T("记录无法读取，可在「读取记录」恢复备份。", "Record unreadable. Restore a backup in Load record.") :
                    T("记录对应的章节暂不可用。", "The recorded chapter is currently unavailable."),
                    18, null, TextAnchor.MiddleCenter);
                status.name = "Record status";
                UIWidgets.Size(status.transform, -1, 560).layoutPriority = 3;
            }

            // Secondary help stays outside the scroll viewport; no panel can draw over it.
            RectTransform secondary = ui.Row("Menu help", page.Footer);
            UIWidgets.Size(secondary, -1, 300);
            Button help = ui.Button(secondary, T("操作与教学", "Controls & tutorials"), ShowHelp);
            help.gameObject.AddComponent<UIMenuButtonLayout>().Configure(help.GetComponentInChildren<Text>(), null);
            page.Footer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            page.Footer.GetComponent<LayoutElement>().minHeight = 46;
            Finish(page);
        }

        private Button MenuButton(Transform parent, string title, Action click, bool primary = false)
        {
            Button button = ui.Button(parent, title, click, primary);
            Text label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.offsetMin = new Vector2(24, 6); label.rectTransform.offsetMax = new Vector2(-24, -6);
            button.gameObject.AddComponent<UIMenuButtonLayout>().Configure(label, null);
            return button;
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
            if (parent.GetComponentInChildren<UIMenuLayout>() != null && ReadMenuRecords().Signature != menuSignature)
                RefreshVisiblePage();
        }
    }
}
