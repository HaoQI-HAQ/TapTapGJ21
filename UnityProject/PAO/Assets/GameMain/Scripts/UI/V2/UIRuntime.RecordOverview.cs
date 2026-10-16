using System;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        // A short, selectable route summary leaves the record actions in view.
        // The full chapter dossier is still reachable from every stage marker.
        private Action<SaveSlotData> BuildRecordRouteSummary(Transform parent)
        {
            if (Config.chapters == null || Config.chapters.Length == 0)
            {
                Text empty = ui.Label(parent, T("暂无章节进度", "No route progress"), 19);
                UIWidgets.Size(empty.transform, 60);
                return null;
            }

            SaveSlotData latest = IsGameplay ? null : ReadMenuRecords().latest;
            SaveSlotData shownRecord = latest;
            RunProgress shown = latest != null && latest.runProgress != null ?
                latest.runProgress : UIRunState.Progress;
            if (shown == null) shown = new RunProgress();
            string scene = IsGameplay ? lastScene : latest != null ? latest.sceneName : "";
            int selected = Mathf.Max(0, Config.IndexOf(scene));

            RectTransform panel = ui.Rect("Record route summary", parent);
            UIWidgets.Size(panel, 104);
            Image paper = panel.gameObject.AddComponent<Image>();
            paper.color = Color.white; paper.raycastTarget = false;
            Outline edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = Color.black; edge.effectDistance = Vector2.one;

            Text heading = ui.Label(panel, T("章节进度  /  ROUTE", "ROUTE PROGRESS"),
                18, Color.black, TextAnchor.MiddleLeft);
            heading.name = "Record route heading"; heading.fontStyle = FontStyle.Bold;
            heading.rectTransform.anchorMin = new Vector2(0, 1);
            heading.rectTransform.anchorMax = new Vector2(.42f, 1);
            heading.rectTransform.offsetMin = new Vector2(14, -34);
            heading.rectTransform.offsetMax = new Vector2(0, -3);
            Text caption = ui.Label(panel, "", 17, Color.black, TextAnchor.MiddleRight);
            caption.name = "Previewed record";
            caption.rectTransform.anchorMin = new Vector2(.42f, 1);
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = new Vector2(0, -34);
            caption.rectTransform.offsetMax = new Vector2(-14, -3);
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 13;
            caption.resizeTextMaxSize = 20;

            RectTransform viewport = ui.Rect("Compact route viewport", panel);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12, 10);
            viewport.offsetMax = new Vector2(-12, -41);
            Image hitArea = viewport.gameObject.AddComponent<Image>();
            hitArea.color = Color.white;
            viewport.gameObject.AddComponent<RectMask2D>();
            UIHorizontalScrollRect scroll = viewport.gameObject.AddComponent<UIHorizontalScrollRect>();
            scroll.horizontal = true; scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32; scroll.viewport = viewport;

            RectTransform strip = ui.Row("Compact route stages", viewport, 8);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, .5f); strip.anchoredPosition = Vector2.zero;
            strip.sizeDelta = new Vector2(Config.chapters.Length * 116 +
                (Config.chapters.Length - 1) * 8, 0);
            scroll.content = strip;
            Button[] stops = new Button[Config.chapters.Length];
            Text[] stopLabels = new Text[stops.Length];
            Outline[] stopEdges = new Outline[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                int index = i;
                Button stop = ui.Button(strip, "", () => ShowChapterInfo(index, shown, scene, true));
                stop.name = "Record chapter " + (i + 1).ToString("00");
                UIWidgets.Size(stop.transform, 51, 116, 1);
                UIFluidGradient gradient = stop.GetComponent<UIFluidGradient>();
                if (gradient != null) gradient.enabled = false;
                UIAdaptiveButton adaptive = stop.GetComponent<UIAdaptiveButton>();
                if (adaptive != null) adaptive.enabled = false;
                UIButtonFeedback feedback = stop.GetComponent<UIButtonFeedback>();
                if (feedback != null) feedback.enabled = false;
                UIControlAccent accent = stop.GetComponent<UIControlAccent>();
                if (accent != null) accent.enabled = false;
                Image surface = stop.GetComponent<Image>();
                surface.sprite = null; surface.color = Color.white;
                stop.transition = Selectable.Transition.ColorTint;
                Text label = stop.GetComponentInChildren<Text>();
                label.GetComponent<UIStyledText>().Configure(17, false, Config);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 13; label.resizeTextMaxSize = 22;
                stopLabels[i] = label;
                stopEdges[i] = stop.GetComponent<Outline>();
                stops[i] = stop;
            }

            Action redraw = () =>
            {
                selected = Mathf.Max(0, Config.IndexOf(scene));
                caption.text = shownRecord != null ?
                    T("正在预览记录 ", "PREVIEWING RECORD ") +
                    (shownRecord.slot == UIRunState.AutoSlot ? "AUTO" :
                        (shownRecord.slot + 1).ToString("00")) + "  /  " + ChapterName(scene) :
                    IsGameplay ? T("当前旅程  /  ", "CURRENT RUN  /  ") + ChapterName(scene) :
                    T("尚未选择记录", "NO RECORD SELECTED");
                for (int i = 0; i < stops.Length; i++)
                {
                    ChapterDefinition chapter = Config.chapters[i];
                    bool completed = shown.completed != null && shown.completed.Contains(chapter.scene);
                    bool unlocked = i == 0 || shown.completed != null &&
                        shown.completed.Contains(Config.chapters[i - 1].scene);
                    bool available = Application.CanStreamedLevelBeLoaded(chapter.scene);
                    string state = !available ? T("不可用", "OFFLINE") : completed ?
                        T("完成", "DONE") : scene == chapter.scene ? T("当前", "HERE") :
                        unlocked ? T("开放", "OPEN") : T("锁定", "LOCKED");
                    stopLabels[i].text = (i + 1).ToString("00") + "  /  " + state;
                    stopEdges[i].effectColor = i == selected ? Color.black :
                        new Color(.58f, .58f, .58f);
                    stopEdges[i].effectDistance = i == selected ? new Vector2(2, 2) : Vector2.one;
                    stops[i].GetComponent<Image>().color = i == selected ?
                        new Color(.94f, .94f, .94f) : Color.white;
                }
                Canvas.ForceUpdateCanvases();
                scroll.horizontalNormalizedPosition = stops.Length <= 1 ? 0 :
                    selected / (float)(stops.Length - 1);
            };

            Canvas.ForceUpdateCanvases();
            strip.sizeDelta = new Vector2(Mathf.Max(viewport.rect.width,
                Config.chapters.Length * 116 + (Config.chapters.Length - 1) * 8), 0);
            redraw();
            return data =>
            {
                if (data == null) return;
                shownRecord = data;
                shown = data.runProgress ?? new RunProgress();
                scene = data.sceneName;
                redraw();
            };
        }

        private Text RecordCardLabel(Transform parent, string name, string value, int size,
            float top, float height, float left, float right, TextAnchor alignment,
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

        private Text RecordCardRightLabel(Transform parent, string name, string value,
            int size, float top, float height)
        {
            Text label = RecordCardLabel(parent, name, value, size, top, height,
                0, 20, TextAnchor.MiddleRight);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-20, -top);
            rect.sizeDelta = new Vector2(170, height);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12; label.resizeTextMaxSize = 19;
            return label;
        }

        private void CompactRecordAction(Button button)
        {
            UIAdaptiveButton adaptive = button.GetComponent<UIAdaptiveButton>();
            if (adaptive != null) adaptive.enabled = false;
            UIWidgets.Size(button.transform, 48);
            Text label = button.GetComponentInChildren<Text>();
            if (label == null) return;
            label.GetComponent<UIStyledText>().Configure(18, false, Config);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14; label.resizeTextMaxSize = 24;
        }
    }
}
