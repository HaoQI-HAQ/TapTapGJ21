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
        public bool IsGameplay { get; private set; }
        private UIWidgets ui;
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
            Instance = this; Config = config; ui = new UIWidgets(config);
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
            objectiveOverride = null;
            if (UIRunState.PendingLoad != null) UIRunState.Apply(UIRunState.PendingLoad);
            if (EventSystem.current == null)
            {
                var events = new GameObject("UI EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(events, scene);
            }
            StartCoroutine(InitializeScene(scene));
        }

        private IEnumerator InitializeScene(Scene scene)
        {
            // Let existing gameplay Start methods initialize their camera and dynamically created labels.
            yield return null;
            if (scene != SceneManager.GetActiveScene()) yield break;
            HideLegacyUI();
            player = FindFirstObjectByType<PlayerController>();
            launcher = FindFirstObjectByType<BubbleLauncher>();
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
                if (audio.GetComponent<UIAudioChannel>() == null) audio.gameObject.AddComponent<UIAudioChannel>();
            if (pending != null) Toast(T("记录已恢复", "Record restored"));
            else ShowTutorial("movement", T("向水流深处", "Follow the current"),
                T("WASD 移动 · 鼠标观察 · 空格跳跃\n1 / 2 / 3 或滚轮切换泡泡，按住左键蓄力。Esc 暂停。",
                  "WASD to move · Mouse to look · Space to jump\n1 / 2 / 3 or wheel to switch. Hold left click to charge. Esc to pause."));
            if (SaveOnNextEntry) { SaveOnNextEntry = false; SaveSnapshot(UIRunState.AutoSlot); }
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
        }

        private void Update()
        {
            if (Config == null || !canvas.gameObject.activeSelf) return;
            if (rebind != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { rebind = null; Pop(); return; }
                foreach (KeyCode key in keyboardKeys)
                {
                    if (key <= KeyCode.None || key >= KeyCode.Mouse0 || key == KeyCode.Return || key == KeyCode.Tab ||
                        key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow) continue;
                    if (Input.GetKeyDown(key)) { Action<KeyCode> callback = rebind; rebind = null; Pop(); callback(key); return; }
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.JoystickButton7)) Back();
            if (Input.GetKeyDown(KeyCode.Tab)) MoveFocus(Input.GetKey(KeyCode.LeftShift) ? -1 : 1);
            if (IsGameplay && !IsBlockingGameplay)
            {
                UIRunState.Progress.playSeconds += Time.deltaTime;
                if (Input.GetKeyDown(UISettings.Key(4, KeyCode.J))) ShowJournal();
                else if (Input.GetKeyDown(UISettings.Key(5, KeyCode.F1))) ShowHelp();
                if (Input.GetKeyDown(KeyCode.JoystickButton4) && switcher != null) switcher.Cycle(-1);
                if (Input.GetKeyDown(KeyCode.JoystickButton5) && switcher != null) switcher.Cycle(1);
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
        }

        public void Back()
        {
            if (loading) return;
            if (pages.Count == 0) { if (IsGameplay) ShowPause(); return; }
            if (pages.Count == 1 && !IsGameplay) { ConfirmQuit(); return; }
            if (!pages[pages.Count - 1].CanClose) { ShowChapters(); return; }
            Pop();
        }

        private UIPage Push(string title, string subtitle = "", bool compact = false)
        {
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (pages.Count > 0)
            {
                pages[pages.Count - 1].SetInteractable(false);
                pages[pages.Count - 1].NoticeArea.gameObject.SetActive(false);
            }
            Image background = ui.Surface(title, safeRoot, compact ? new Color(Config.ink.r, Config.ink.g, Config.ink.b, 0.35f) :
                Config.paper);
            UIWidgets.Stretch(background.rectTransform);
            background.gameObject.AddComponent<CanvasGroup>();
            UIPage page = background.gameObject.AddComponent<UIPage>(); page.ReturnFocus = focus;
            RectTransform body = ui.Column("Page", background.transform, compact ? 40 : 56, 20);
            UIWidgets.Stretch(body);
            if (compact)
            {
                Image card = body.gameObject.AddComponent<Image>(); card.color = Config.paper;
                card.sprite = Config.round; card.type = Image.Type.Sliced;
                body.anchorMin = new Vector2(0.2f, 0.15f); body.anchorMax = new Vector2(0.8f, 0.85f);
                body.offsetMin = body.offsetMax = Vector2.zero;
            }
            Text heading = ui.Label(body, title, 44); UIWidgets.Size(heading.transform, 72);
            if (!string.IsNullOrEmpty(subtitle)) ui.Label(body, subtitle, 21);
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
            }
            else
            {
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
                if (input.enabled && (input is PlayerController ||
                    input is BubbleTypeSwitcher || input is BubbleRideInteractor || input is tiaozhuan))
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

        private void Confirm(string title, string explanation, Action action)
        {
            UIPage page = Push(title, "", true);
            ui.Label(page.Content, explanation, 26);
            ui.Button(page.Footer, T("取消", "Cancel"), Pop);
            ui.Button(page.Footer, T("确认", "Confirm"), () => { Pop(); action(); }, true);
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
            UIPage page = Push(T("正在进入意识之海", "Entering the sea"), T("请稍候", "Please wait"), true);
            Text progress = ui.Label(page.Content, "0%", 40, null, TextAnchor.MiddleCenter);
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
                yield return null;
            }
        }

        private void OnLegacyLoad(SaveSlotData data) { LoadScene(data.sceneName, data); }
        private void ApplyGameplaySettings()
        {
            if (player != null)
            { player.LookSensitivityMultiplier = UISettings.Current.sensitivity; player.InvertVerticalLook = UISettings.Current.invertLook; }
            if (launcher != null) launcher.ToggleChargeInput = UISettings.Current.toggleCharge;
        }
        private void UnbindGameplay()
        {
            if (switcher != null) switcher.TypeChanged -= OnAbilityChanged;
            if (tide != null) { tide.Won -= OnLevelWon; tide.Lost -= OnLevelLost; }
            switcher = null; tide = null; player = null; launcher = null;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SaveSystem.SlotLoaded -= OnLegacyLoad;
            UISettings.Changed -= ApplyGameplaySettings;
            ReleasePause(); UnbindGameplay(); if (Instance == this) Instance = null;
        }
        private static string T(string chinese, string english) { return UISettings.Text(chinese, english); }
    }
}
