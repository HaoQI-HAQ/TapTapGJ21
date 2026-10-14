using System;
using System.Collections;
using System.Collections.Generic;
using PAO.BubbleTide;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    [DefaultExecutionOrder(-500)]
    public sealed partial class UIRuntime : MonoBehaviour
    {
        public static UIRuntime Instance { get; private set; }
        public UIConfig Config { get; private set; }
        public bool IsBlockingGameplay { get { return pages.Count > 0 || loading; } }
        public bool BlocksInputThisFrame { get { return IsBlockingGameplay || Time.frameCount <= resumeInputFrame; } }
        private int resumeInputFrame = -1;
        public bool IsGameplay { get; private set; }
        private UIWidgets ui;
        private UIConfig gameplayTheme, menuTheme;
        private RectTransform safeRoot;
        private RectTransform hud;
        private Canvas canvas;
        private readonly List<UIPage> pages = new List<UIPage>();
        private readonly List<Behaviour> frozenInputs = new List<Behaviour>();
        private bool paused;
        private bool loading;
        private float previousTime;
        private CursorLockMode previousCursor;
        private bool previousCursorVisible;
        private bool previousLauncherBlocked;
        private BubbleTypeSwitcher switcher;
        private BubbleLauncher launcher;
        private PlayerController player;
        private BubbleTideDirector tide;
        private float nextHudUpdate;
        private string lastScene;
        private Action<KeyCode> rebind;
        private int rebindOpenedFrame;
        private readonly KeyCode[] keyboardKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null; UIRunState.ResetSession(); UISettings.Initialize();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            UIConfig config = Resources.Load<UIConfig>("PAOUIConfig");
            if (config == null || !config.enabledInBuild || Instance != null) return;
            var root = new GameObject("PAO UI V2");
            DontDestroyOnLoad(root);
            root.AddComponent<UIRuntime>().Initialize(config);
        }

        public void Initialize(UIConfig config)
        {
            UIHandDrawn.Apply(config);
            Instance = this; gameplayTheme = config;
            menuTheme = Instantiate(config); menuTheme.name = "Document menu theme";
            menuTheme.paperStyle = true;
            menuTheme.paper = config.paper;
            menuTheme.ink = menuTheme.text = config.text;
            menuTheme.mist = config.mist;
            menuTheme.teal = config.teal;
            menuTheme.menuArt = config.menuPaperArt != null ? config.menuPaperArt : config.menuArt;
            menuTheme.chapters = config.chapters; menuTheme.endings = config.endings;
            Config = config; ui = new UIWidgets(config);
            RectTransform canvasRect = ui.Rect("UI Canvas V2", transform);
            canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 1f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            safeRoot = ui.Rect("Safe Area", canvasRect); UIWidgets.Stretch(safeRoot);
            safeRoot.gameObject.AddComponent<UISafeArea>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveSystem.SlotLoaded += OnLegacyLoad;
            UISettings.Changed += ApplyGameplaySettings;
            SaveSlotData latest = UIRunState.Latest();
            if (latest != null) UIRunState.Apply(latest);
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            ReleasePause(); UnbindGameplay(); ClearPages(); ClearHud();
            lastScene = scene.name; loading = false;
            canvas.gameObject.SetActive(Config.Supports(scene.name));
            if (!Config.Supports(scene.name)) { IsGameplay = false; return; }
            IsGameplay = scene.name != Config.menuScene;
            Config = IsGameplay ? gameplayTheme : menuTheme; ui = new UIWidgets(Config);
            objectiveOverride = null;
            if (UIRunState.PendingLoad != null) UIRunState.Apply(UIRunState.PendingLoad);
            EnsureEventSystem(scene);
            StartCoroutine(InitializeScene(scene));
        }

        private IEnumerator InitializeScene(Scene scene)
        {
            if (IsGameplay) PrepareChapter(scene);
            // Let existing gameplay Start methods initialize their camera and dynamically created labels.
            yield return null;
            if (scene != SceneManager.GetActiveScene()) yield break;
            HideLegacyUI();
            EnsureEventSystem(scene);
            player = FindFirstObjectByType<PlayerController>();
            launcher = FindFirstObjectByType<BubbleLauncher>();
            BindBubbleResource();
            switcher = BubbleTypeSwitcher.Instance;
            if (IsGameplay && switcher == null) switcher = new GameObject("Bubble Type State").AddComponent<BubbleTypeSwitcher>();
            if (switcher != null)
            {
                switcher.TypeChanged += OnAbilityChanged;
                switcher.SetLabelVisible(false);
                switcher.SetType((BubbleType)UIRunState.Progress.bubbleType);
            }
            tide = BubbleTideDirector.Instance;
            if (tide != null) { tide.Won += OnLevelWon; tide.Lost += OnLevelLost; }
            ApplyGameplaySettings();
            if (!IsGameplay)
            {
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true; ShowMenu(); yield break;
            }
            SaveSlotData pending = UIRunState.PendingLoad;
            UIRunState.PendingLoad = null;
            if (pending != null)
            {
                UIRunState.Apply(pending);
                if (pending.hasPosition && player != null)
                {
                    CharacterController controller = player.GetComponent<CharacterController>();
                    bool wasEnabled = controller != null && controller.enabled;
                    if (controller != null) controller.enabled = false;
                    player.transform.position = new Vector3(pending.posX, pending.posY, pending.posZ);
                    if (controller != null) controller.enabled = wasEnabled;
                }
                if (switcher != null) switcher.SetType((BubbleType)UIRunState.Progress.bubbleType);
            }
            BuildHud();
            foreach (AudioSource audio in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (audio.GetComponent<UIAudioChannel>() == null) audio.gameObject.AddComponent<UIAudioChannel>().InferChannel();
            if (pending != null)
            {
                if (UISettings.Current.detailedHints) Toast(T("记录已恢复", "Record restored"));
                else PulseVisualCue(UIGuideSymbol.Check);
            }
            else ShowTutorial("movement", T("向水流深处", "Follow the current"),
                UIInput.MovementHint() + T(" 移动 · 跳跃 ", " Move · Jump ") + UIInput.Prompt(UIInput.Jump) +
                T("\n蓄力 ", "\nCharge ") + UIInput.Prompt(UIInput.Charge) + T(" · Esc / Menu 暂停 · ", " · Esc / Menu Pause · ") + UIInput.Prompt(UIInput.Help) + T(" 查看教学", " for help"));
            if (SaveOnNextEntry) { SaveOnNextEntry = false; SaveSnapshot(UIRunState.AutoSlot); }
        }

        private static void EnsureEventSystem(Scene scene)
        {
            if (EventSystem.current != null && EventSystem.current.isActiveAndEnabled) return;
            var events = new GameObject("UI EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            SceneManager.MoveGameObjectToScene(events, scene);
        }

        private void HideLegacyUI()
        {
            foreach (Canvas oldCanvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (oldCanvas == canvas || oldCanvas.transform.IsChildOf(transform)) continue;
                bool ownsLegacyUI = oldCanvas.GetComponentInChildren<MenuButton>(true) != null ||
                    oldCanvas.GetComponentInChildren<PauseMenu>(true) != null ||
                    oldCanvas.GetComponentInChildren<SettingsPanel>(true) != null ||
                    oldCanvas.GetComponentInChildren<LoadPanel>(true) != null ||
                    oldCanvas.GetComponentInChildren<BubbleTypeHud>(true) != null;
                if (ownsLegacyUI) oldCanvas.gameObject.SetActive(false);
            }
            foreach (BubbleTypeHud oldHud in FindObjectsByType<BubbleTypeHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                oldHud.enabled = false;
            foreach (BubbleResourceHud oldHud in FindObjectsByType<BubbleResourceHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                oldHud.enabled = false;
        }

        private void Update()
        {
            if (Config == null || !canvas.gameObject.activeSelf) return;
            UIInput.Poll();
            HandleDeviceDisconnect();
            if (UpdateBindingCapture()) return;
            UpdateControllerTabs();
            if (Input.GetKeyDown(KeyCode.Escape) || (pages.Count > 0 && UIInput.ControllerDown(ControllerControl.B)) || UIInput.ControllerDown(ControllerControl.Menu)) Back();
            if (Input.GetKeyDown(KeyCode.Tab)) MoveFocus(Input.GetKey(KeyCode.LeftShift) ? -1 : 1);
            if (IsGameplay && !BlocksInputThisFrame)
            {
                UIRunState.Progress.playSeconds += Time.deltaTime;
                if (UIInput.Down(UIInput.Journal)) ShowJournal();
                else if (UIInput.Down(UIInput.Help)) ShowHelp();
            }
            if (IsGameplay && Time.unscaledTime >= nextHudUpdate)
            {
                nextHudUpdate = Time.unscaledTime + 0.1f; RefreshHud();
            }
            UpdateTransientUI();
        }

        private void LateUpdate()
        {
            if (IsBlockingGameplay || !IsGameplay) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            UpdateVisualGuidance();
        }

        public void Back()
        {
            if (loading) return;
            if (pages.Count == 0) { if (IsGameplay) ShowPause(); return; }
            if (pages.Count == 1 && !IsGameplay) { ConfirmQuit(); return; }
            if (!pages[pages.Count - 1].CanClose) { ShowChapters(); return; }
            Pop();
        }

        private UIPage Push(string title, string subtitle = "", bool compact = false, UIPageDesign design = UIPageDesign.Plain)
        {
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (pages.Count > 0)
            {
                pages[pages.Count - 1].SetInteractable(false);
                pages[pages.Count - 1].NoticeArea.gameObject.SetActive(false);
            }
            Image background = ui.Surface(title, safeRoot, compact ? new Color(Config.ink.r, Config.ink.g, Config.ink.b, 0.35f) :
                Config.paper);
            if (!compact) background.color = Color.white;
            UIWidgets.Stretch(background.rectTransform);
            background.gameObject.AddComponent<CanvasGroup>();
            UIPage page = background.gameObject.AddComponent<UIPage>(); page.ReturnFocus = focus;
            page.Design = design;
            page.Configure(Config.motion);
            if (!compact)
            {
                var gradient = background.gameObject.AddComponent<UIFluidGradient>();
                gradient.top = UIDesignPalette.Paper;
                gradient.bottom = Color.Lerp(UIDesignPalette.Paper, UIDesignPalette.Accent(design), .09f);
                UIPageArtwork artwork = ui.Rect("Page illustration · " + design, background.transform).gameObject.AddComponent<UIPageArtwork>();
                artwork.design = design; artwork.color = new Color(UIDesignPalette.Accent(design).r, UIDesignPalette.Accent(design).g, UIDesignPalette.Accent(design).b, .24f);
                artwork.raycastTarget = false; UIWidgets.Stretch(artwork.rectTransform);
            }
            UIHandDrawn.Backdrop(ui, background.transform);
            RectTransform body = ui.Column("Page", background.transform, compact ? 32 : 24, 16);
            UIWidgets.Stretch(body);
            if (!compact)
            {
                body.anchorMin = new Vector2(.05f, .04f); body.anchorMax = new Vector2(.95f, .96f);
                body.offsetMin = body.offsetMax = Vector2.zero;
            }
            if (compact)
            {
                Image card = body.gameObject.AddComponent<Image>(); card.color = Config.paper;
                card.sprite = Config.round; card.type = Image.Type.Sliced;
                body.anchorMin = new Vector2(0.2f, 0.15f); body.anchorMax = new Vector2(0.8f, 0.85f);
                body.offsetMin = body.offsetMax = Vector2.zero;
                if (design == UIPageDesign.Dialogue)
                {
                    body.anchorMin = new Vector2(.08f, .055f); body.anchorMax = new Vector2(.92f, .58f);
                }
                else if (design != UIPageDesign.Construction) body.gameObject.AddComponent<UICompactPageLayout>();
                RectTransform artMask = ui.Rect("Panel artwork", body); UIWidgets.Stretch(artMask);
                artMask.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                artMask.gameObject.AddComponent<RectMask2D>();
                UIPageArtwork artwork = ui.Rect("Panel illustration · " + design, artMask).gameObject.AddComponent<UIPageArtwork>();
                artwork.design = design; artwork.color = UIDesignPalette.Accent(design) * new Color(1, 1, 1, .12f);
                artwork.raycastTarget = false; UIWidgets.Stretch(artwork.rectTransform);
            }
            Color? headingColor = Config.paperStyle ? (Color?)null : Config.ink;
            Text heading = ui.Label(body, title, 44, headingColor); heading.name = "Page title";
            UIWidgets.Size(heading.transform, 72).preferredHeight = -1;
            if (!string.IsNullOrEmpty(subtitle)) ui.Label(body, subtitle, 21, headingColor);
            page.Content = ui.Scroll(body);
            page.NoticeArea = ui.Column("Page notice", body, 12, 0);
            Image notice = page.NoticeArea.gameObject.AddComponent<Image>();
            notice.color = Config.mist; notice.sprite = Config.round; notice.type = Image.Type.Sliced;
            notice.raycastTarget = false;
            page.NoticeLabel = ui.Label(page.NoticeArea, "", 23, null, TextAnchor.MiddleCenter);
            page.NoticeArea.gameObject.SetActive(false);
            page.Footer = ui.Row("Actions", body); UIWidgets.Size(page.Footer, 72);
            page.Footer.GetComponent<LayoutElement>().preferredHeight = -1;
            pages.Add(page); page.Show();
            SetPause(true);
            if (hud != null) hud.gameObject.SetActive(false);
            return page;
        }

        private void Finish(UIPage page)
        {
            page.Refresh(); page.RestoreFocus();
        }

        private void Pop()
        {
            if (pages.Count == 0) return;
            UIPage page = pages[pages.Count - 1]; pages.RemoveAt(pages.Count - 1);
            if (page.BeforeClose != null) page.BeforeClose();
            page.Hide(); Destroy(page.gameObject);
            if (pages.Count > 0)
            {
                UIPage parent = pages[pages.Count - 1]; parent.SetInteractable(true);
                if (EventSystem.current != null && page.ReturnFocus != null && page.ReturnFocus.activeInHierarchy)
                    EventSystem.current.SetSelectedGameObject(page.ReturnFocus);
                else parent.RestoreFocus();
                RefreshMenuOnReturn(parent);
            }
            else
            {
                resumeInputFrame = Time.frameCount;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                if (hud != null) hud.gameObject.SetActive(true);
                ReleasePause();
            }
        }

        private void ClearPages()
        {
            foreach (UIPage page in pages)
            {
                if (page != null)
                {
                    if (page.BeforeClose != null) page.BeforeClose();
                    page.Hide(); Destroy(page.gameObject);
                }
            }
            pages.Clear();
        }

        private void SetPause(bool value)
        {
            if (!value || paused || !IsGameplay) return;
            paused = true; previousTime = Time.timeScale;
            previousCursor = Cursor.lockState; previousCursorVisible = Cursor.visible;
            Time.timeScale = 0;
            if (launcher != null) { previousLauncherBlocked = launcher.InputBlocked; launcher.InputBlocked = true; }
            foreach (Behaviour input in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                // Bomb labels use OnGUI, so pausing time or hiding the HUD cannot hide them.
                if (input.enabled && (input is PlayerController ||
                    input is BubbleTypeSwitcher || input is BubbleRideInteractor ||
                    input is BombBubbleManager || input is tiaozhuan))
                { input.enabled = false; frozenInputs.Add(input); }
            }
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        private void ReleasePause()
        {
            if (!paused) return;
            foreach (Behaviour input in frozenInputs) if (input != null) input.enabled = true;
            frozenInputs.Clear(); Time.timeScale = previousTime;
            if (launcher != null) launcher.InputBlocked = previousLauncherBlocked;
            Cursor.lockState = previousCursor; Cursor.visible = previousCursorVisible; paused = false;
        }

        private void MoveFocus(int direction)
        {
            if (pages.Count == 0) return;
            var targets = new List<Selectable>();
            foreach (Selectable item in pages[pages.Count - 1].GetComponentsInChildren<Selectable>())
                if (item.IsInteractable() && item.IsActive()) targets.Add(item);
            if (targets.Count == 0 || EventSystem.current == null) return;
            int index = targets.FindIndex(s => s.gameObject == EventSystem.current.currentSelectedGameObject);
            targets[(index + direction + targets.Count) % targets.Count].Select();
        }

        private void Confirm(string title, string explanation, Action action, bool danger = false)
        {
            UIPage page = Push(title, "", true, UIPageDesign.Confirmation);
            ui.Emblem(page.Content, danger ? UIGuideSymbol.Bomb : UIGuideSymbol.Current,
                danger ? T("确认后执行", "CONFIRM TO PROCEED") : T("请确认选择", "REVIEW YOUR CHOICE"), danger ? UIDesignPalette.Coral : Config.teal);
            ui.Label(page.Content, explanation, 26);
            ui.Button(page.Footer, T("取消", "Cancel"), Pop);
            ui.Button(page.Footer, T("确认", "Confirm"), () => { Pop(); action(); }, true, true, danger);
            Finish(page); // The safe option receives initial focus.
        }

        public void LoadScene(string scene, SaveSlotData save = null)
        {
            if (loading) return;
            if (string.IsNullOrEmpty(scene) || !Application.CanStreamedLevelBeLoaded(scene))
            { Toast(T("场景尚未加入构建列表，请稍后再试。", "This scene is unavailable in the build.")); return; }
            loading = true; UIRunState.PendingLoad = save;
            StartCoroutine(LoadSceneRoutine(scene));
        }

        private UIPage CreateLoadingPage(out Text progress, out Image fill)
        {
            UIPage page = Push(T("正在进入意识之海", "Entering the sea"), T("请稍候", "Please wait"), true, UIPageDesign.Loading);
            ui.Emblem(page.Content, UIGuideSymbol.Current, T("循着水流", "FOLLOWING THE CURRENT"));
            Image track = ui.Surface("Loading track", page.Content, Config.mist, true); UIWidgets.Size(track.transform, 12);
            fill = ui.Surface("Loading current", track.transform, Config.teal, true);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            progress = ui.Label(page.Content, "0%", 40, null, TextAnchor.MiddleCenter);
            return page;
        }
        private IEnumerator LoadSceneRoutine(string scene)
        {
            Text progress; Image fill; CreateLoadingPage(out progress, out fill);
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(scene); }
            catch (Exception error) { Debug.LogException(error); }
            if (operation == null)
            {
                SaveSlotData retrySave = UIRunState.PendingLoad;
                loading = false; UIRunState.PendingLoad = null; Pop();
                Confirm(T("无法进入", "Unable to enter"), T("加载失败。是否重试？", "Loading failed. Retry?"), () => LoadScene(scene, retrySave));
                yield break;
            }
            while (!operation.isDone)
            {
                if (progress != null) progress.text = Mathf.RoundToInt(operation.progress / 0.9f * 100) + "%";
                if (fill != null) fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(operation.progress / .9f), 1);
                yield return null;
            }
        }

        private void OnLegacyLoad(SaveSlotData data) { LoadScene(data.sceneName, data); }
        private void ApplyGameplaySettings()
        {
            if (player != null)
            { player.LookSensitivityMultiplier = UISettings.Current.sensitivity; player.InvertVerticalLook = UISettings.Current.invertLook; }
            if (launcher != null) launcher.ToggleChargeInput = UISettings.Current.toggleCharge;
            if (canvas != null) canvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1600, 900) / UISettings.Current.uiScale;
            ApplyHintPresentation();
        }
        private void UnbindGameplay()
        {
            UnbindBubbleResource();
            if (switcher != null) switcher.TypeChanged -= OnAbilityChanged;
            if (tide != null) { tide.Won -= OnLevelWon; tide.Lost -= OnLevelLost; }
            switcher = null; tide = null; player = null; launcher = null;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SaveSystem.SlotLoaded -= OnLegacyLoad;
            UISettings.Changed -= ApplyGameplaySettings;
            ReleasePause(); UnbindGameplay(); if (Instance == this) Instance = null;
            if (menuTheme != null) Destroy(menuTheme);
        }
        private static string T(string chinese, string english) { return UISettings.Text(chinese, english); }
    }
}
