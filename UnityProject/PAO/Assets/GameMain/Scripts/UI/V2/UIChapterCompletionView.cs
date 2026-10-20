using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    /// <summary>
    /// Chapter results use a reference-sized artboard. Decorative artwork is separate
    /// from every live value and control; narrow displays get a deliberately reflowed page.
    /// </summary>
    public sealed class UIChapterCompletionView : MonoBehaviour
    {
        public RectTransform Artboard { get; private set; }
        public bool IsPortrait { get; private set; }
        public Button PrimaryButton { get; private set; }
        public Button ReviewButton { get; private set; }
        public Button ChaptersButton { get; private set; }
        public Button MenuButton { get; private set; }
        public Text MemoryValue { get; private set; }
        public Text TimeValue { get; private set; }
        public Text SaveStatusLabel { get; private set; }
        public Text ChapterLabel { get; private set; }

        private UIPage page;
        private UIConfig config;
        private RectTransform content, footer, numberRect;
        private Image heading, hero, frame, badge, memoryIcon, currentIcon, reviewIcon;
        private RectTransform memoryRule, timeRule;
        private RectTransform saveLeftRule, saveRightRule;
        private RectTransform chapterSeparator, primaryRule;
        private Text kicker, headingText, chapterState, memoryLabel, timeLabel, primaryText, reviewText, chaptersText, menuText;
        private Text number;
        private UIChapterCompletionNarrowRules narrowRules;
        private UIChapterCompletionStatusMark statusMark;
        private Vector2 previousSize;
        private float previousScale = -1;
        private float previousCanvasScale = -1;
        private int language = -1;
        private bool saved;
        private static Font sansFont, serifFont, figureFont;

        public void Build(UIPage target, UIConfig configuration, string chapterLabel, int chapterNumber,
            int memories, double seconds, bool didSave, string primaryLabel, Action primary,
            Action journal, Action chapters, Action menu)
        {
            page = target;
            config = configuration;
            saved = didSave;
            // Push has already begun its entrance tween. Keep those objects alive for
            // its callbacks, but remove them from presentation and future Page lookups.
            foreach (Transform child in page.transform)
            {
                child.name = "Previous completion layout - " + child.name;
                child.gameObject.SetActive(false);
            }

            Image background = page.GetComponent<Image>();
            if (background != null) background.color = Color.white;
            Image paper = Artwork("Completion paper", page.transform, "Paper");
            UIWidgets.Stretch(paper.rectTransform);
            paper.type = Image.Type.Tiled;
            paper.raycastTarget = true;

            Artboard = Rect("Chapter completion artboard", page.transform);
            Artboard.anchorMin = Artboard.anchorMax = new Vector2(.5f, .5f);
            Artboard.pivot = new Vector2(.5f, .5f);
            content = Rect("Chapter completion content", Artboard);
            UIWidgets.Stretch(content);
            page.Content = content;

            frame = Artwork("Reference technical frame", content, "Frame");
            narrowRules = Rect("Narrow completion drafting rules", content).gameObject.AddComponent<UIChapterCompletionNarrowRules>();
            narrowRules.raycastTarget = false;
            number = Label("Chapter number outline", content, Mathf.Max(0, chapterNumber).ToString("00"), 402,
                TextAnchor.MiddleCenter, Color.white, FigureFont);
            numberRect = number.rectTransform;
            number.verticalOverflow = VerticalWrapMode.Overflow;
            Outline numberEdge = number.gameObject.AddComponent<Outline>();
            numberEdge.effectColor = new Color(.58f, .58f, .58f, .63f);
            numberEdge.effectDistance = new Vector2(.7f, -.7f);
            numberEdge.useGraphicAlpha = true;
            hero = Artwork("Bubble and current illustration", content, "Hero");
            kicker = Label("Chapter complete eyebrow", content, "C H A P T E R   C O M P L E T E", 15);
            heading = Artwork("Chinese completion heading", content, "Heading");
            headingText = Label("Localized completion heading", content, "", 65, TextAnchor.MiddleLeft, null, SerifFont);

            ChapterLabel = Label("Chapter label", content, chapterLabel ?? "", 34, TextAnchor.MiddleLeft, null, SerifFont);
            ChapterLabel.fontStyle = FontStyle.Bold;
            badge = Artwork("Chapter complete badge", content, "Badge");
            badge.type = Image.Type.Sliced;
            chapterState = Label("Chapter completion state", content, "", 19, TextAnchor.MiddleCenter, Color.white);
            chapterSeparator = Rule("Chapter state separator", content);

            memoryIcon = Artwork("Memory crystal illustration", content, "Memory");
            currentIcon = Artwork("Journey current illustration", content, "Current");
            memoryLabel = Label("Memory count label", content, "", 23);
            timeLabel = Label("Journey time label", content, "", 23);
            memoryRule = Rule("Memory label rule", content);
            timeRule = Rule("Journey time label rule", content);
            MemoryValue = Label("Memory count value", content, Mathf.Max(0, memories).ToString("00"), 70,
                TextAnchor.MiddleLeft, null, FigureFont);
            MemoryValue.fontStyle = FontStyle.Bold;
            double safeSeconds = double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0 : Math.Max(0, seconds);
            TimeValue = Label("Journey time value", content,
                TimeSpan.FromSeconds(Math.Min(safeSeconds, TimeSpan.MaxValue.TotalSeconds - 1)).ToString(@"hh\:mm\:ss"),
                70, TextAnchor.MiddleLeft, null, FigureFont);
            TimeValue.fontStyle = FontStyle.Bold;

            PrimaryButton = ActionButton("Completion primary action", content, "Primary", primary,
                out primaryText, Color.white);
            primaryText.text = primaryLabel ?? "";
            primaryRule = Rule("Primary action white rule", PrimaryButton.transform);
            primaryRule.GetComponent<Image>().color = Color.white;
            PrimaryButton.gameObject.SetActive(primary != null);
            ReviewButton = ActionButton("Review memories", content, "Secondary", journal, out reviewText, Color.black);
            reviewIcon = Artwork("Review memory crystal", ReviewButton.transform, "Memory");
            reviewIcon.preserveAspect = true;
            statusMark = Rect("Save status mark", content).gameObject.AddComponent<UIChapterCompletionStatusMark>();
            statusMark.Saved = saved;
            statusMark.raycastTarget = false;
            SaveStatusLabel = Label("Save status", content, "", 20);
            saveLeftRule = Rule("Save status left rule", content);
            saveRightRule = Rule("Save status right rule", content);

            footer = Rect("Chapter completion footer", Artboard);
            UIWidgets.Stretch(footer);
            page.Footer = footer;
            ChaptersButton = ActionButton("Chapter path", footer, null, chapters, out chaptersText, Color.black);
            MenuButton = ActionButton("Main menu", footer, null, menu, out menuText, Color.black);
            SetNavigation();

            page.NoticeArea = Rect("Completion notice", Artboard);
            Image notice = page.NoticeArea.gameObject.AddComponent<Image>();
            notice.color = new Color(1, 1, 1, .98f);
            notice.raycastTarget = false;
            Outline noticeEdge = page.NoticeArea.gameObject.AddComponent<Outline>();
            noticeEdge.effectColor = new Color(0, 0, 0, .65f);
            noticeEdge.effectDistance = new Vector2(1, -1);
            page.NoticeLabel = Label("Completion notice message", page.NoticeArea, "", 21, TextAnchor.MiddleCenter);
            UIWidgets.Stretch(page.NoticeLabel.rectTransform, 8);
            page.NoticeArea.gameObject.SetActive(false);
            previousSize = Vector2.zero;
            RefreshLayout();
        }

        private Font SansFont => sansFont != null ? sansFont : sansFont = OSFont(
            new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "PingFang SC", "Arial" }, 32);
        private Font SerifFont => serifFont != null ? serifFont : serifFont = OSFont(
            new[] { "SimSun", "Noto Serif CJK SC", "Songti SC", "Times New Roman" }, 48);
        private Font FigureFont => figureFont != null ? figureFont : figureFont = OSFont(
            new[] { "Bahnschrift Condensed", "Bahnschrift", "Arial Narrow", "Arial" }, 72);

        private Font OSFont(string[] candidates, int size)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            foreach (string candidate in candidates)
                if (Array.Exists(installed, name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
                    return Font.CreateDynamicFontFromOSFont(candidate, size);
            return config != null && config.font != null ? config.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Image Artwork(string name, Transform parent, string resource)
        {
            Image result = Rect(name, parent).gameObject.AddComponent<Image>();
            result.sprite = Resources.Load<Sprite>("UIChapterCompletion/" + resource);
            result.color = Color.white;
            result.raycastTarget = false;
            return result;
        }

        private static RectTransform Rule(string name, Transform parent)
        {
            Image line = Rect(name, parent).gameObject.AddComponent<Image>();
            line.color = new Color(0, 0, 0, .75f);
            line.raycastTarget = false;
            return line.rectTransform;
        }

        private Text Label(string name, Transform parent, string value, int size,
            TextAnchor alignment = TextAnchor.MiddleLeft, Color? color = null, Font font = null)
        {
            Text result = Rect(name, parent).gameObject.AddComponent<Text>();
            result.font = font != null ? font : SansFont;
            result.fontSize = size;
            result.text = value;
            result.color = color ?? Color.black;
            result.alignment = alignment;
            result.horizontalOverflow = HorizontalWrapMode.Overflow;
            result.verticalOverflow = VerticalWrapMode.Truncate;
            result.supportRichText = false;
            result.raycastTarget = false;
            return result;
        }

        private Button ActionButton(string name, Transform parent, string resource, Action action,
            out Text label, Color foreground)
        {
            Image surface = resource != null ? Artwork(name, parent, resource) :
                Rect(name, parent).gameObject.AddComponent<Image>();
            if (resource == null) surface.color = Color.clear;
            surface.raycastTarget = true;
            surface.type = resource != null ? Image.Type.Sliced : Image.Type.Simple;
            Button button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            button.transition = Selectable.Transition.None;
            button.interactable = action != null;
            // Button.OnEnable may have applied its default disabled tint while the
            // page's entrance CanvasGroup blocks input. The ink artwork owns its
            // colours; clear that initial renderer tint after disabling tinting.
            surface.canvasRenderer.SetColor(Color.white);
            surface.canvasRenderer.SetAlpha(1);
            button.onClick.AddListener(() =>
            {
                PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UIConfirm, Vector3.zero, .5f);
                action?.Invoke();
            });
            label = Label(name + " label", surface.transform, "", 30, TextAnchor.MiddleCenter, foreground);
            var interaction = surface.gameObject.AddComponent<UIChapterCompletionInteraction>();
            interaction.Configure(foreground);
            return button;
        }

        private void SetNavigation()
        {
            Button first = PrimaryButton.gameObject.activeSelf ? PrimaryButton : ReviewButton;
            Navigation navigation = new Navigation { mode = Navigation.Mode.Explicit };
            navigation.selectOnDown = ReviewButton;
            navigation.selectOnUp = ChaptersButton;
            PrimaryButton.navigation = navigation;
            navigation.selectOnUp = first == ReviewButton ? ChaptersButton : PrimaryButton;
            navigation.selectOnDown = ChaptersButton;
            ReviewButton.navigation = navigation;
            navigation.selectOnUp = ReviewButton;
            navigation.selectOnDown = first;
            navigation.selectOnLeft = MenuButton;
            navigation.selectOnRight = MenuButton;
            ChaptersButton.navigation = navigation;
            navigation.selectOnLeft = ChaptersButton;
            navigation.selectOnRight = ChaptersButton;
            MenuButton.navigation = navigation;
        }

        private void LateUpdate()
        {
            if (Artboard != null) RefreshLayout();
        }

        /// <summary>Also used by editor captures after changing their viewport.</summary>
        public void RefreshLayout()
        {
            if (Artboard == null || page == null) return;
            Vector2 size = ((RectTransform)page.transform).rect.size;
            UISettingsData settings = UISettings.Current;
            float textScale = settings != null ? Mathf.Clamp(settings.textScale, 1, 1.3f) : 1;
            int nextLanguage = settings != null ? settings.language : 0;
            Canvas canvas = page.GetComponentInParent<Canvas>();
            float canvasScale = canvas != null ? canvas.scaleFactor : 1;
            if (size == previousSize && Mathf.Approximately(textScale, previousScale) && language == nextLanguage &&
                Mathf.Approximately(canvasScale, previousCanvasScale)) return;
            previousSize = size;
            previousScale = textScale;
            previousCanvasScale = canvasScale;
            language = nextLanguage;
            IsPortrait = size.x / Mathf.Max(1, size.y) < 1.15f;
            Vector2 reference = IsPortrait ? new Vector2(800, 1420) : new Vector2(1672, 941);
            float fit = Mathf.Min(size.x / reference.x, size.y / reference.y);
            Artboard.sizeDelta = reference;
            Artboard.anchoredPosition = Vector2.zero;
            Artboard.localScale = Vector3.one * Mathf.Max(.01f, fit);
            frame.gameObject.SetActive(!IsPortrait);
            narrowRules.gameObject.SetActive(IsPortrait);
            kicker.gameObject.SetActive(IsPortrait);
            bool useChineseArtwork = language == 0;
            heading.gameObject.SetActive(useChineseArtwork);
            headingText.gameObject.SetActive(!useChineseArtwork);
            headingText.text = UISettings.Text("这一段水流，已有回响", "This current now has an echo");
            chapterState.text = UISettings.Text("章节完成", "COMPLETE");
            memoryLabel.text = UISettings.Text("意识碎片", "MEMORIES");
            timeLabel.text = UISettings.Text("旅程时间", "JOURNEY TIME");
            reviewText.text = UISettings.Text("回看意识碎片", "Review memories");
            chaptersText.text = UISettings.Text("章节路径", "Chapter path");
            menuText.text = UISettings.Text("主菜单", "Main menu");
            SaveStatusLabel.text = saved ? UISettings.Text("已留下旅程记录", "Journey recorded") :
                UISettings.Text("旅程记录未能保存，请检查存储空间", "Could not save the journey. Check available storage.");
            if (IsPortrait) ArrangeNarrow(textScale); else ArrangeWide(textScale);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private static void FitText(Text text, float x, float y, float width, float height,
            int fontSize, float scale = 1, bool fit = false)
        {
            Place(text.rectTransform, x, y, width, height);
            text.fontSize = Mathf.RoundToInt(fontSize * scale);
            text.resizeTextForBestFit = fit;
            text.resizeTextMinSize = Mathf.Max(12, Mathf.RoundToInt(fontSize * .76f));
            text.resizeTextMaxSize = text.fontSize;
            text.horizontalOverflow = fit ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        }

        private float FitChapterLabel(float x, float y, float minWidth, float maxWidth, float height, int fontSize, float scale)
        {
            FitText(ChapterLabel, x, y, maxWidth, height, fontSize, scale);
            // A chapter's real title may be much longer than "水域 02". Give it
            // horizontal room first, then reduce its type only when the row is full.
            float preferred = ChapterLabel.preferredWidth;
            if (preferred > maxWidth)
                ChapterLabel.fontSize = Mathf.Max(19, Mathf.FloorToInt(ChapterLabel.fontSize * maxWidth / preferred));
            float width = Mathf.Clamp(ChapterLabel.preferredWidth + 4, minWidth, maxWidth);
            ChapterLabel.rectTransform.sizeDelta = new Vector2(width, height);
            return width;
        }

        private void ArrangeWide(float scale)
        {
            Place(frame.rectTransform, 0, 0, 1672, 941);
            FitText(number, 920, -29, 390, 410, 418);
            // Reference figures are taller and narrower than a default sans face.
            numberRect.localScale = new Vector3(.88f, 1, 1);
            Place(hero.rectTransform, 975, 0, 697, 600);
            Place(heading.rectTransform, 62, 143, 898, 156);
            FitText(headingText, 70, 143, 875, 154, 65, scale, true);
            float chapterWidth = FitChapterLabel(72, 300, 133, 600, 49, 34, scale);
            float badgeX = Mathf.Max(226, 72 + chapterWidth + 21);
            Place(badge.rectTransform, badgeX, 304, 172, 43);
            Place(chapterSeparator, badgeX - 22, 309, 1, 32);
            FitText(chapterState, badgeX + 16, 309, 108, 31, 19, scale, true);
            Place(memoryIcon.rectTransform, 93, 406, 79, 120);
            Place(currentIcon.rectTransform, 661, 410, 95, 53);
            FitText(memoryLabel, 200, 407, 290, 39, 23, scale);
            FitText(timeLabel, 775, 407, 285, 39, 23, scale);
            float memoryRuleX = Mathf.Max(309, 200 + memoryLabel.preferredWidth + 17);
            float timeRuleX = Mathf.Max(894, 775 + timeLabel.preferredWidth + 17);
            Place(memoryRule, memoryRuleX, 436, 74, 1);
            Place(timeRule, timeRuleX, 438, Mathf.Max(32, 957 - timeRuleX), 1);
            FitText(MemoryValue, 200, 441, 360, 97, 83, scale, true);
            MemoryValue.rectTransform.localScale = new Vector3(.74f, 1, 1);
            FitText(TimeValue, 775, 441, 335, 97, 79, scale, true);
            TimeValue.rectTransform.localScale = new Vector3(.87f, 1, 1);
            Place(PrimaryButton.GetComponent<RectTransform>(), 144, 600, 1386, 91);
            FitText(primaryText, 320, 11, 746, 69, 36, scale, true);
            primaryText.fontStyle = FontStyle.Bold;
            Place(primaryRule, Mathf.Max(830, 693 + primaryText.preferredWidth * .5f + 30), 49, 104, 1);
            Place(ReviewButton.GetComponent<RectTransform>(), 144, 702, 1387, 94);
            FitText(reviewText, 636, 12, 360, 64, 25, scale, true);
            reviewText.alignment = TextAnchor.MiddleLeft;
            Place(reviewIcon.rectTransform, 584, 17, 37, 55);
            FitText(SaveStatusLabel, saved ? 795 : 703, 805, saved ? 320 : 612, 40, 20, scale, true);
            Place(statusMark.rectTransform, saved ? 749 : 658, 809, 27, 29);
            Place(saveLeftRule, saved ? 698 : 610, 814, 1, 22);
            Place(saveRightRule, Mathf.Max(saved ? 973 : 1330,
                SaveStatusLabel.rectTransform.anchoredPosition.x + SaveStatusLabel.preferredWidth + 20), 814, 1, 22);
            Place(ChaptersButton.GetComponent<RectTransform>(), 264, 851, 120, 66);
            FitText(chaptersText, 0, 0, 120, 66, 22, scale, true);
            Place(MenuButton.GetComponent<RectTransform>(), 1291, 851, 88, 66);
            FitText(menuText, 0, 0, 88, 66, 22, scale, true);
            Place(page.NoticeArea, 470, 56, 665, 58);
            page.NoticeLabel.fontSize = Mathf.RoundToInt(21 * scale);
        }

        private void ArrangeNarrow(float scale)
        {
            Canvas canvas = page.GetComponentInParent<Canvas>();
            float pixels = Mathf.Max(.01f, Artboard.localScale.x * (canvas != null ? canvas.scaleFactor : 1));
            float buttonHeight = Mathf.Max(94, 48 / pixels);
            float actionScale = Mathf.Max(scale, 16 / (29 * pixels));
            UIWidgets.Stretch(narrowRules.rectTransform);
            FitText(kicker, 48, 67, 704, 32, 18, scale, true);
            Place(heading.rectTransform, 38, 124, 724, 126);
            FitText(headingText, 48, 120, 704, 136, 58, scale, true);
            FitChapterLabel(50, 269, 180, 480, 58, 36, scale);
            float badgeX = Mathf.Max(240, 50 + ChapterLabel.preferredWidth + 30);
            Place(badge.rectTransform, badgeX, 274, 200, 50);
            Place(chapterSeparator, badgeX - 22, 280, 1, 37);
            FitText(chapterState, badgeX + 15, 281, 142, 34, 22, scale, true);
            FitText(number, 388, 302, 305, 360, 335);
            numberRect.localScale = new Vector3(.9f, 1, 1);
            Place(hero.rectTransform, 132, 319, 568, 489);
            Place(memoryIcon.rectTransform, 62, 799, 60, 91);
            Place(currentIcon.rectTransform, 436, 803, 72, 40);
            FitText(memoryLabel, 141, 797, 224, 40, 25, scale, true);
            FitText(timeLabel, 529, 797, 232, 40, 25, scale, true);
            Place(memoryRule, 0, 0, 0, 0);
            Place(timeRule, 0, 0, 0, 0);
            FitText(MemoryValue, 141, 838, 224, 85, 67, scale, true);
            FitText(TimeValue, 436, 845, 323, 75, 62, scale, true);
            TimeValue.rectTransform.localScale = new Vector3(.95f, 1, 1);
            Place(PrimaryButton.GetComponent<RectTransform>(), 48, 1062 - buttonHeight, 704, buttonHeight);
            FitText(primaryText, 126, (buttonHeight - 69) / 2, 452, 69, 34, actionScale, true);
            primaryText.fontStyle = FontStyle.Bold;
            // The arrow belongs to the fixed right end of the sliced ink strip.
            // Keep translated / enlarged action text clear of that end cap.
            primaryText.resizeTextForBestFit = false;
            primaryText.horizontalOverflow = HorizontalWrapMode.Overflow;
            float actionWidth = primaryText.preferredWidth;
            if (actionWidth > 360)
            {
                primaryText.fontSize = Mathf.Max(19, Mathf.FloorToInt(primaryText.fontSize * 360 / actionWidth));
                actionWidth = primaryText.preferredWidth;
            }
            float actionCenter = Mathf.Min(352, 434 - actionWidth * .5f);
            Place(primaryText.rectTransform, actionCenter - 226, (buttonHeight - 69) / 2, 452, 69);
            Place(primaryRule, 0, 0, 0, 0);
            Place(ReviewButton.GetComponent<RectTransform>(), 48, 1082, 704, buttonHeight);
            FitText(reviewText, 266, (buttonHeight - 64) / 2, 358, 64, 29, actionScale, true);
            reviewText.alignment = TextAnchor.MiddleLeft;
            Place(reviewIcon.rectTransform, 210, (buttonHeight - 59) / 2, 39, 59);
            float statusY = Mathf.Max(1201, 1094 + buttonHeight);
            FitText(SaveStatusLabel, saved ? 303 : 130, statusY, saved ? 420 : 602, 49, 23, scale, true);
            Place(statusMark.rectTransform, saved ? 254 : 84, statusY + 9, 30, 30);
            Place(saveLeftRule, saved ? 216 : 52, statusY + 14, 1, 22);
            Place(saveRightRule, Mathf.Min(748, Mathf.Max(saved ? 586 : 748,
                SaveStatusLabel.rectTransform.anchoredPosition.x + SaveStatusLabel.preferredWidth + 20)), statusY + 14, 1, 22);
            float footerHeight = Mathf.Max(82, 48 / pixels);
            Place(ChaptersButton.GetComponent<RectTransform>(), 73, 1392 - footerHeight, 256, footerHeight);
            FitText(chaptersText, 0, 0, 256, footerHeight, 29, actionScale, true);
            Place(MenuButton.GetComponent<RectTransform>(), 471, 1392 - footerHeight, 256, footerHeight);
            FitText(menuText, 0, 0, 256, footerHeight, 29, actionScale, true);
            Place(page.NoticeArea, 80, 1253, 640, 58);
            page.NoticeLabel.fontSize = Mathf.RoundToInt(23 * scale);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIChapterCompletionStatusMark : MaskableGraphic
    {
        private bool saved = true;
        public bool Saved { get => saved; set { saved = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (saved)
            {
                UIChapterCompletionNarrowRules.Line(vh, new Vector2(r.xMin + r.width * .12f, r.yMin + r.height * .48f),
                    new Vector2(r.xMin + r.width * .36f, r.yMin + r.height * .26f), 2.7f, Color.black);
                UIChapterCompletionNarrowRules.Line(vh, new Vector2(r.xMin + r.width * .36f, r.yMin + r.height * .26f),
                    new Vector2(r.xMin + r.width * .88f, r.yMin + r.height * .82f), 2.7f, Color.black);
            }
            else
            {
                UIChapterCompletionNarrowRules.Line(vh, r.min + r.size * .2f, r.max - r.size * .2f, 2.7f, Color.black);
                UIChapterCompletionNarrowRules.Line(vh, new Vector2(r.xMin + r.width * .2f, r.yMax - r.height * .2f),
                    new Vector2(r.xMax - r.width * .2f, r.yMin + r.height * .2f), 2.7f, Color.black);
            }
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIChapterCompletionNarrowRules : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Draw(vh, 48, 34, 48, 56, 1); Draw(vh, 37, 45, 59, 45, 1);
            Draw(vh, 746, 50, 746, 82, 1); Draw(vh, 730, 66, 762, 66, 1);
            Draw(vh, 400, 794, 400, 930, 1);
            for (int block = 0; block < 2; block++)
                for (int i = 0; i < 38; i++)
                {
                    float x = (block == 0 ? 60 : 435) + i * 7.4f;
                    float h = i > 17 && i < 29 ? 12 : 4 + i % 3;
                    Draw(vh, x, 938, x, 938 - h, i > 17 && i < 29 ? 3 : 1);
                }
            Draw(vh, 36, 1303, 36, 1329, 1); Draw(vh, 23, 1316, 49, 1316, 1);
            Draw(vh, 764, 1303, 764, 1329, 1); Draw(vh, 751, 1316, 777, 1316, 1);
            Draw(vh, 48, 1385, 338, 1385, 1); Draw(vh, 462, 1385, 752, 1385, 1);
            Draw(vh, 396, 1331, 396, 1371, 1.5f); Draw(vh, 403, 1331, 403, 1371, 1.5f);
        }
        private void Draw(VertexHelper vh, float x1, float y1, float x2, float y2, float thickness)
        {
            Rect r = rectTransform.rect;
            Line(vh, new Vector2(r.xMin + x1, r.yMax - y1), new Vector2(r.xMin + x2, r.yMax - y2), thickness, Color.black);
        }
        internal static void Line(VertexHelper vh, Vector2 from, Vector2 to, float thickness, Color32 color)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < .001f) return;
            Vector2 edge = new Vector2(-direction.y, direction.x).normalized * thickness * .5f;
            int start = vh.currentVertCount;
            vh.AddVert(from - edge, color, Vector2.zero); vh.AddVert(from + edge, color, Vector2.zero);
            vh.AddVert(to + edge, color, Vector2.zero); vh.AddVert(to - edge, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    public sealed class UIChapterCompletionInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        private UIChapterCompletionFocusGraphic focus;
        private bool hovered, selected;
        public void Configure(Color ink)
        {
            var go = new GameObject("Action focus outline", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIChapterCompletionFocusGraphic));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)go.transform;
            UIWidgets.Stretch(rect, 9);
            focus = go.GetComponent<UIChapterCompletionFocusGraphic>();
            focus.color = ink;
            focus.raycastTarget = false;
            go.SetActive(false);
        }
        private void Refresh() { if (focus != null) focus.gameObject.SetActive(hovered || selected); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
        public void OnPointerDown(PointerEventData data) { if (focus != null) { focus.Pressed = true; focus.SetVerticesDirty(); } }
        public void OnPointerUp(PointerEventData data) { if (focus != null) { focus.Pressed = false; focus.SetVerticesDirty(); } }
        private void OnDisable() { hovered = selected = false; Refresh(); }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIChapterCompletionFocusGraphic : MaskableGraphic
    {
        public bool Pressed;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float weight = Pressed ? 2.5f : 1.25f;
            float tick = Mathf.Min(22, r.height * .26f);
            foreach (Vector2 corner in new[] { r.min, new Vector2(r.xMin, r.yMax), r.max, new Vector2(r.xMax, r.yMin) })
            {
                float dx = corner.x > r.center.x ? -tick : tick;
                float dy = corner.y > r.center.y ? -tick : tick;
                UIChapterCompletionNarrowRules.Line(vh, corner, corner + new Vector2(dx, 0), weight, color);
                UIChapterCompletionNarrowRules.Line(vh, corner, corner + new Vector2(0, dy), weight, color);
            }
        }
    }
}
