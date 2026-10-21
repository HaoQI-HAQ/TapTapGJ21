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
        private UIBubbleTransition loadingTransition;
        private int sceneInitialization;
        private float previousTime;
        private bool previousLauncherBlocked;
        private BubbleTypeSwitcher switcher;
        private BubbleLauncher launcher;
        private PlayerController player;
        private BubbleTideDirector tide;
        private float nextHudUpdate;
        private float nextAutoSave;
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
            // 直接从编辑器 Play 某个关卡时（不经过主菜单），UISettings 可能还没初始化。
            // 所有 UI 代码都默认它存在，这里兜底，避免字幕/引导一读就被空引用打断。
            if (UISettings.Current == null) UISettings.Initialize();

            Instance = this; gameplayTheme = Instantiate(config); gameplayTheme.name = "Monochrome gameplay theme";
            menuTheme = Instantiate(config); menuTheme.name = "Document menu theme";
            foreach (UIConfig theme in new[] { gameplayTheme, menuTheme })
            {
                theme.paperStyle = false;
                theme.paper = theme.mist = Color.white;
                theme.ink = theme.text = theme.teal = Color.black;
                theme.round = null;
            }
            // The main menu uses the dedicated painted backdrop; gameplay pages keep the white document stock.
            menuTheme.menuArt = config.menuBackdrop != null ? config.menuBackdrop :
                config.menuPaperArt != null ? config.menuPaperArt : config.menuArt;
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
            loadingTransition = UIBubbleTransition.Create(transform, menuTheme);
            loadingTransition.Begin(true);
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
            RefreshEnvironmentLighting();
            ReleasePause(); UnbindGameplay(); ClearPages(); ClearHud();
            int initialization = ++sceneInitialization;
            lastScene = scene.name; loading = true;
            loadingTransition.HoldOpaque();
            nextAutoSave = Time.unscaledTime + 60f;
            canvas.gameObject.SetActive(Config.Supports(scene.name));
            if (!Config.Supports(scene.name))
            {
                IsGameplay = false;
                StartCoroutine(FinishSceneTransition(initialization));
                return;
            }
            IsGameplay = scene.name != Config.menuScene;
            Config = IsGameplay ? gameplayTheme : menuTheme; ui = new UIWidgets(Config);
            objectiveOverride = null;
            if (UIRunState.PendingLoad != null) UIRunState.Apply(UIRunState.PendingLoad);
            EnsureEventSystem(scene);
            HideLegacyUI();
            StartCoroutine(InitializeScene(scene, initialization));
        }

        /// <summary>
        /// 重建天空盒环境探针（ambient probe）。
        ///
        /// 【为什么必须有这一步】
        /// Unity 用 SceneManager 切场景后**不会**自动重算天空盒环境光：
        /// 新场景的 RenderSettings 生效了（主光、雾、环境光来源都换成了新场景的），
        /// 但"拿天空盒渲染出来的环境探针"还留在上一个场景算出的旧值。
        /// 于是凡是把环境光来源设成 Skybox 的关卡（洛琪希 / shiliuye map / Map4 / Map5），
        /// 在编辑器里直接 Play 是正常的，一旦用跳关进来就只剩主光 ——
        /// 暗部纯黑、亮部发黄，正是"阴影黑的离谱"那张图的样子。
        /// 官方 IssueTracker 有记录：
        ///   Skybox Environment Lighting is not visible after loading Scene using SceneManager
        ///   Skybox lighting doesn't update when switching Scenes using SceneManager
        ///
        /// 这里挂在 sceneLoaded 上（此刻新场景已激活、RenderSettings 已应用），
        /// 一处覆盖所有入口：菜单选章、tiaozhuan 跳关、暂停菜单重开、读档、验证脚本。
        ///
        /// 【为什么不影响别的关卡】
        /// 只在"环境光来源 = Skybox"时才动手。Map0/Map1/Map2/Map3 这些用的是
        /// Flat 颜色环境光，颜色存在场景文件里、加载即生效，本来就没坏，不去碰它们，
        /// 也就不会顺手改掉别人的反光表现。
        ///
        /// 【开销】
        /// 每次加载渲染一次 128px 立方图，不是每帧，可以忽略；
        /// 而且这时候过场黑幕还盖着（要等 FinishSceneTransition 才揭幕），看不到跳变。
        /// </summary>
        private static void RefreshEnvironmentLighting()
        {
            // 必须写全限定名：本类里有个同名私有方法 RenderSettings(SettingsSession, int)
            // （设置页的面板渲染），会遮住引擎的 UnityEngine.RenderSettings 类型，少写前缀就是 CS0119。
            if (UnityEngine.RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox)
            {
                return;
            }

            DynamicGI.UpdateEnvironment();
        }

        private IEnumerator InitializeScene(Scene scene, int initialization)
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
                switcher.SetType(PAO.Sandbox.BubbleSandbox.Instance != null ? BubbleType.Bouncy : (BubbleType)UIRunState.Progress.bubbleType);
                UIRunState.Progress.bubbleType = (int)switcher.Current;
            }
            tide = BubbleTideDirector.Instance;
            if (tide != null) { tide.Won += OnLevelWon; tide.Lost += OnLevelLost; }
            ApplyGameplaySettings();
            if (!IsGameplay)
            {
                ApplyCursorLock(false); ShowMenu();
                yield return FinishSceneTransition(initialization);
                yield break;
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
                if (switcher != null)
                {
                    switcher.SetType((BubbleType)UIRunState.Progress.bubbleType);
                    UIRunState.Progress.bubbleType = (int)switcher.Current;
                }
            }
            BuildHud();
            foreach (AudioSource audio in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (audio.GetComponentInParent<PAO.AV.PAOAudioDirector>() == null &&
                    audio.GetComponent<UIAudioChannel>() == null) audio.gameObject.AddComponent<UIAudioChannel>().InferChannel();
            if (pending != null)
            {
                if (UISettings.Current.detailedHints) Toast(T("记录已恢复", "Record restored"));
                else PulseVisualCue(UIGuideSymbol.Check);
            }
            else if (PAO.Sandbox.BubbleSandbox.Instance == null) ShowTutorial("movement", T("向水流深处", "Follow the current"),
                UIInput.MovementHint() + T(" 移动 · 跳跃 ", " Move · Jump ") + UIInput.Prompt(UIInput.Jump) +
                T("\n蓄力 ", "\nCharge ") + UIInput.Prompt(UIInput.Charge) + T(" · Esc / Menu 暂停 · ", " · Esc / Menu Pause · ") + UIInput.Prompt(UIInput.Help) + T(" 查看教学", " for help"));
            if (SaveOnNextEntry) { SaveOnNextEntry = false; SaveSnapshot(UIRunState.AutoSlot); }
            yield return FinishSceneTransition(initialization);
        }

        private IEnumerator FinishSceneTransition(int initialization)
        {
            if (initialization != sceneInitialization) yield break;
            SetPause(true);
            if (pages.Count > 0) pages[pages.Count - 1].SetInteractable(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            // Keep an opaque curtain across scene activation, Start(), save restoration and the first HUD frame.
            yield return null;
            if (initialization != sceneInitialization) yield break;
            yield return loadingTransition.Reveal();
            if (initialization != sceneInitialization) yield break;
            loading = false; resumeInputFrame = Time.frameCount;
            if (pages.Count == 0) ReleasePause();
            else
            {
                pages[pages.Count - 1].SetInteractable(true);
                pages[pages.Count - 1].RestoreFocus();
            }
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
            if (loading) return;
            HandleDeviceDisconnect();
            if (UpdateBindingCapture()) return;
            UpdateControllerTabs();
            if (Input.GetKeyDown(KeyCode.Escape) || (pages.Count > 0 && UIInput.ControllerDown(ControllerControl.B)) || UIInput.ControllerDown(ControllerControl.Menu)) Back();
            if (Input.GetKeyDown(KeyCode.Tab)) MoveFocus(Input.GetKey(KeyCode.LeftShift) ? -1 : 1);
            if (IsGameplay && !BlocksInputThisFrame)
            {
                UIRunState.Progress.playSeconds += Time.deltaTime;
                if (Time.unscaledTime >= nextAutoSave)
                {
                    nextAutoSave = Time.unscaledTime + 60f;
                    AutoSaveQuietly();
                }
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
            // 光标状态每帧从「是不是游戏内 + 有没有界面开着」重新算出来，
            // 而不是沿用某个时刻的快照。
            //
            // 【为什么不能沿用快照】
            // 以前 SetPause 把当时的 Cursor.lockState 记下来、ReleasePause 原样写回。
            // 可暂停、结算、教学这些页面在游戏一开局就会打开，那一刻光标本来就是解锁的，
            // 于是「暂停 → 继续」就把解锁状态写了回去；而 PlayerController 只在 OnEnable
            // 锁一次，之后没有任何代码补锁 —— 玩家按一次 Esc 再继续、或者从结算页回到
            // 游戏，光标就永久留在外面自由漂移，视角却还跟着鼠标转，
            // 看起来就是「鼠标脱离了游戏」。
            // 失焦（切到别的窗口）时不写 Cursor：Windows 上反复锁定会让系统光标被拽住，
            // 玩家反而更难切回游戏。聚焦时 OnApplicationFocus + 这里的每帧补锁会立刻接手。
            if (Application.isFocused)
            {
                bool locked = IsGameplay && pages.Count == 0 && !loading;
                ApplyCursorLock(locked);
                if (player != null) player.ReleaseCursorForMenus = !locked;
            }
            UpdateVisualGuidance();
        }

        public void Back()
        {
            if (loading) return;
            if (pages.Count == 0) { if (IsGameplay) ShowPause(); return; }
            if (pages.Count == 1 && !IsGameplay) { ConfirmQuit(); return; }
            if (!pages[pages.Count - 1].CanClose) { ShowChapters(); return; }
            if (pages[pages.Count - 1].Design == UIPageDesign.Pause) { ClosePause(); return; }
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
            if (!compact) background.color = design == UIPageDesign.Pause ? new Color(1, 1, 1, .08f) : Color.white;
            UIWidgets.Stretch(background.rectTransform);
            background.gameObject.AddComponent<CanvasGroup>();
            UIPage page = background.gameObject.AddComponent<UIPage>(); page.ReturnFocus = focus;
            page.Design = design;
            page.Configure(Config.motion);
            if (!compact && design != UIPageDesign.Pause)
            {
                var gradient = background.gameObject.AddComponent<UIFluidGradient>();
                gradient.top = UIDesignPalette.Paper;
                gradient.bottom = UIDesignPalette.Paper;
                if (design == UIPageDesign.Menu && Config.menuArt != null)
                {
                    // Full-bleed painted backdrop: the wordmark and entries sit straight on the illustration.
                    Image backdrop = ui.Surface("Menu backdrop", background.transform, Color.white);
                    backdrop.sprite = Config.menuArt; backdrop.type = Image.Type.Simple;
                    backdrop.preserveAspect = false; backdrop.raycastTarget = false;
                    UIWidgets.Stretch(backdrop.rectTransform);
                    backdrop.gameObject.AddComponent<UIBackdropCover>();
                }
                else
                {
                    UIPageArtwork artwork = ui.Rect("Page illustration · " + design, background.transform).gameObject.AddComponent<UIPageArtwork>();
                    artwork.design = design; artwork.color = new Color(UIDesignPalette.Accent(design).r, UIDesignPalette.Accent(design).g, UIDesignPalette.Accent(design).b, .24f);
                    artwork.raycastTarget = false; UIWidgets.Stretch(artwork.rectTransform);
                }
            }
            // The atlas design uses clean white stock and black drafting lines.
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
            PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UISoftOpen, Vector3.zero, .6f);
            SetPause(true);
            if (hud != null) hud.gameObject.SetActive(false);
            return page;
        }

        private void Finish(UIPage page)
        {
            page.Refresh(); page.RevealContents(); page.RestoreFocus();
        }

        private void Pop()
        {
            if (pages.Count == 0) return;
            PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.UIBack, Vector3.zero, .55f);
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
            // 暂停菜单（以及它上面叠的设置/地图页）打开时，背景音乐跟着停，位置保留。
            if (PAO.AV.PAOAudioDirector.Instance != null) PAO.AV.PAOAudioDirector.Instance.SetMusicPaused(true);
            // 不再记 Cursor.lockState 快照：界面打开时一律解锁，关掉时由 ReleasePause / LateUpdate 重算。
            Time.timeScale = 0;
            if (launcher != null) { previousLauncherBlocked = launcher.InputBlocked; launcher.InputBlocked = true; }
            foreach (Behaviour input in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                // Bomb labels use OnGUI, so pausing time or hiding the HUD cannot hide them.
                // 玩家控制器禁用期间不能再抢光标，否则刚一恢复就又被它锁回去。
                if (input.enabled && (input is PlayerController ||
                    input is BubbleTypeSwitcher || input is BubbleRideInteractor ||
                    input is BombBubbleManager || input is tiaozhuan))
                {
                    input.enabled = false;
                    PlayerController playerInput = input as PlayerController;
                    if (playerInput != null) playerInput.ReleaseCursorForMenus = true;
                    frozenInputs.Add(input);
                }
            }
            ApplyCursorLock(false);
        }

        private void ReleasePause()
        {
            if (!paused) return;
            foreach (Behaviour input in frozenInputs) if (input != null) input.enabled = true;
            frozenInputs.Clear(); Time.timeScale = previousTime;
            if (launcher != null) launcher.InputBlocked = previousLauncherBlocked;
            paused = false;
            if (PAO.AV.PAOAudioDirector.Instance != null) PAO.AV.PAOAudioDirector.Instance.SetMusicPaused(false);
            // 界面全部关掉之后立刻把光标收回游戏，不再回写打开界面时记下的旧状态。
            bool locked = IsGameplay && pages.Count == 0 && !loading;
            ApplyCursorLock(locked);
            if (player != null)
            {
                player.ReleaseCursorForMenus = !locked;
                if (locked) player.LockCursor(); else player.UnlockCursor();
            }
        }

        /// <summary>
        /// 统一的光标开关，唯一真正写 Cursor.lockState 的地方（菜单场景除外）。
        /// 锁定时顺带隐藏光标；解锁时恢复显示。
        /// </summary>
        private static void ApplyCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
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

        private IEnumerator LoadSceneRoutine(string scene)
        {
            SetPause(true);
            if (pages.Count > 0) pages[pages.Count - 1].SetInteractable(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            loadingTransition.Begin(false);
            yield return loadingTransition.Cover();
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(scene); }
            catch (Exception error) { Debug.LogException(error); }
            if (operation == null)
            {
                SaveSlotData retrySave = UIRunState.PendingLoad;
                loading = false; UIRunState.PendingLoad = null;
                loadingTransition.HideImmediately();
                if (pages.Count > 0) pages[pages.Count - 1].SetInteractable(true);
                Confirm(T("无法进入", "Unable to enter"), T("加载失败。是否重试？", "Loading failed. Retry?"), () => LoadScene(scene, retrySave));
                yield break;
            }
            while (!operation.isDone)
            {
                loadingTransition.SetProgress(Mathf.Clamp01(operation.progress / .9f) * .9f);
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
            if (PAO.Sandbox.BubbleSandbox.Instance != null && hud != null) RefreshSandboxHud();
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
            if (gameplayTheme != null) Destroy(gameplayTheme);
        }
        private static string T(string chinese, string english) { return UISettings.Text(chinese, english); }
        private void OnApplicationPause(bool suspended) { if (suspended) AutoSaveQuietly(); }
        private void OnApplicationQuit() { AutoSaveQuietly(); }
    }
}
