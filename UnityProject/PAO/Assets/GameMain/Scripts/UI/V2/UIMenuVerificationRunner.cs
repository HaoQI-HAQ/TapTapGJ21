#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIMenuVerificationRunner : MonoBehaviour
    {
        private const string Requested = "PAO.UI.MenuVerification";
        private static string output;
        private readonly List<string> passed = new List<string>(), errors = new List<string>();
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Prepare()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            output = Path.GetFullPath("MenuVerificationArtifacts"); Directory.CreateDirectory(output);
            SaveSystem.EditorVerificationDirectory = Path.Combine(output, "Saves-" + Guid.NewGuid().ToString("N"));
            UISettings.Apply(new UISettingsData { fullscreen = false, windowMode = 0 }, false);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            SessionState.SetBool(Requested, false);
            var go = new GameObject("Menu verification runner"); DontDestroyOnLoad(go); go.AddComponent<UIMenuVerificationRunner>();
        }
        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            var stack = new Stack<IEnumerator>(); stack.Push(Scenarios());
            float deadline = Time.realtimeSinceStartup + 180;
            while (stack.Count > 0)
            {
                object current = null; bool moved = false;
                try
                {
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Menu verification timed out.");
                    moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current;
                }
                catch (Exception error) { errors.Add(error.ToString()); break; }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            Application.logMessageReceived -= OnLog;
            string report = "Passed: " + passed.Count + "\n" + string.Join("\n", passed) + "\n\nErrors: " + errors.Count + "\n" + string.Join("\n", errors);
            File.WriteAllText(Path.Combine(output, "report.txt"), report); Debug.Log(report);
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
        private void OnLog(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
        private void Check(bool condition, string description)
        { if (!condition) throw new Exception(description); passed.Add(description); }
        private static UIPage Top(UIRuntime runtime)
        {
            var pages = (List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(runtime);
            return pages.Count > 0 ? pages[pages.Count - 1] : null;
        }
        private static Button Button(UIRuntime runtime, string title, bool required = true)
        {
            foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
                if (button.GetComponentInChildren<Text>().text == title) return button;
            if (required) throw new Exception("Missing button: " + title); return null;
        }
        private static void Rebuild(UIRuntime runtime) { typeof(UIRuntime).GetMethod("RefreshVisiblePage", Private).Invoke(runtime, null); }
        private static bool HasText(UIRuntime runtime, string part)
        {
            foreach (Text text in Top(runtime).GetComponentsInChildren<Text>()) if (text.text.Contains(part)) return true;
            return false;
        }

        private IEnumerator Scenarios()
        {
            for (int i = 0; i < 8; i++) yield return null;
            UIRuntime runtime = UIRuntime.Instance;
            Check(runtime != null && SceneManager.GetActiveScene().path == "Assets/GameMain/UI/Menu.unity", "The real Menu scene bootstraps the new UI.");
            Check(runtime.Config.paperStyle && runtime.Config.paper.r > .9f && runtime.Config.text.r < .2f,
                "The document menu uses warm paper, dark blue text and a dedicated light theme.");
            Text title = Top(runtime).transform.Find("Page/Menu title").GetComponent<Text>();
            Check(title.alignment == TextAnchor.MiddleCenter && title.fontSize >= 72 &&
                Top(runtime).GetComponentInChildren<UIMenuResonance>() == null,
                "The title is the visual centre and the menu background has no rotating character or orbit.");
            Check(Button(runtime, "继续游戏", false) == null && EventSystem.current.currentSelectedGameObject == Button(runtime, "新的旅程").gameObject,
                "An empty journey hides Continue and focuses New journey.");
            foreach (string label in new[] { "新的旅程", "章节路径", "读取记录", "游戏设置", "制作人员", "操作与教学", "离开意识之海" })
                Check(Button(runtime, label).IsInteractable(), "Main menu entry is available: " + label + ".");
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (canvas.GetComponentInChildren<MenuButton>(true) != null) Check(!canvas.gameObject.activeSelf, "Legacy menu canvas is hidden in the real scene.");
            foreach (Vector2Int size in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 800), new Vector2Int(1920, 820), new Vector2Int(960, 540) })
                yield return Capture(runtime, "menu-new-" + size.x + "x" + size.y, size.x, size.y, true);
            Button(runtime, "新的旅程").Select();
            var move = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Down, moveVector = Vector2.down };
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, move, ExecuteEvents.moveHandler);
            Check(EventSystem.current.currentSelectedGameObject == Button(runtime, "章节路径").gameObject,
                "Directional navigation advances through the single column to Chapter path.");
            Button feedbackButton = Button(runtime, "章节路径");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            EventSystem inputSystem = EventSystem.current;
            inputSystem.enabled = false; // Keep physical pointer events out of this injected press.
            feedbackButton.GetComponent<UIButtonFeedback>().OnPointerDown(pointer); yield return new WaitForSecondsRealtime(0.09f);
            Check(feedbackButton.transform.localScale.x < 1, "Press feedback responds while the menu is open. Scale=" + feedbackButton.transform.localScale.x + ", reduced=" + UISettings.Current.reducedMotion);
            feedbackButton.GetComponent<UIButtonFeedback>().OnPointerUp(pointer);
            inputSystem.enabled = true;

            Button credits = Button(runtime, "制作人员"); credits.Select(); credits.onClick.Invoke();
            Check(HasText(runtime, "TapTap Game Jam 21"), "Credits display the configured production information.");
            yield return Capture(runtime, "credits", 1600, 900); runtime.Back();
            Check(EventSystem.current.currentSelectedGameObject == credits.gameObject, "Credits return restores the originating focus.");
            foreach (string label in new[] { "章节路径", "读取记录", "游戏设置", "操作与教学" })
            {
                Button entry = Button(runtime, label); entry.Select(); entry.onClick.Invoke(); yield return null; runtime.Back();
                Check(EventSystem.current.currentSelectedGameObject == entry.gameObject, "Nested page returns to its menu entry: " + label + ".");
            }
            Button quit = Button(runtime, "离开意识之海"); quit.Select(); quit.onClick.Invoke();
            Check(EventSystem.current.currentSelectedGameObject == Button(runtime, "取消").gameObject, "Quit opens a confirmation focused on Cancel.");
            Button(runtime, "取消").onClick.Invoke(); Check(EventSystem.current.currentSelectedGameObject == quit.gameObject, "Cancelling Quit preserves the menu focus.");

            var progress = new RunProgress { playSeconds = 3960 }; progress.completed.Add("Map0");
            progress.memories.Add(new MemoryEntry { id = "menu-memory", title = "Test memory" });
            var data = new SaveSlotData { slot = 0, sceneName = "Map1", savedAtUtc = DateTime.UtcNow.ToString("o"), runProgress = progress };
            Check(SaveSystem.SaveSlot(data), "A returning journey is stored in the isolated directory.");
            Rebuild(runtime); yield return null;
            Check(Button(runtime, "继续游戏").IsInteractable() && !HasText(runtime, "意识碎片 1") && !HasText(runtime, "1 小时 6 分钟") && !HasText(runtime, data.SavedAtText),
                "A valid record enables Continue while statistics and duplicate save details stay off the main menu.");
            Button(runtime, "章节路径").onClick.Invoke(); yield return Capture(runtime, "chapter-cards", 1600, 900); runtime.Back();
            Button(runtime, "读取记录").onClick.Invoke(); yield return Capture(runtime, "record-cards", 1600, 900); runtime.Back();
            Button(runtime, "继续游戏").Select();
            foreach (Vector2Int size in new[] { new Vector2Int(1600, 900), new Vector2Int(1920, 820), new Vector2Int(934, 534), new Vector2Int(800, 900) })
                yield return Capture(runtime, "menu-returning-" + size.x + "x" + size.y, size.x, size.y, true);

            UISettingsData readable = UISettings.Current.Copy(); readable.highContrast = true; UISettings.Apply(readable, false); Rebuild(runtime);
            yield return Capture(runtime, "menu-high-contrast-934x534", 934, 534, true);
            Button contrastContinue = Button(runtime, "继续游戏");
            Color contrastSurface = contrastContinue.GetComponent<UIFluidGradient>().top;
            Color contrastLabel = contrastContinue.GetComponentInChildren<Text>().color;
            Check((contrastSurface == Color.black && contrastLabel == Color.white ||
                contrastSurface == Color.white && contrastLabel == Color.black) &&
                EventSystem.current.currentSelectedGameObject == contrastContinue.gameObject &&
                Array.Exists(contrastContinue.GetComponents<Outline>(), edge => edge.enabled && edge.effectDistance.x >= 3),
                "High contrast gives Continue opposing black/white text and a visible default focus.");
            readable.highContrast = false; UISettings.Apply(readable, false); Rebuild(runtime);

            Button(runtime, "新的旅程").onClick.Invoke();
            Check(EventSystem.current.currentSelectedGameObject == Button(runtime, "取消").gameObject, "New journey confirms replacing the automatic record.");
            runtime.Back(); Check(UIRunState.Progress.memories.Count == 1 && SaveSystem.LoadSlot(0) != null, "Cancelling a new journey retains progress and records.");
            string firstScene = runtime.Config.chapters[0].scene; runtime.Config.chapters[0].scene = "UnavailableFirstChapter";
            Button(runtime, "新的旅程").onClick.Invoke(); Button(runtime, "确认").onClick.Invoke();
            Check(UIRunState.Progress.memories.Count == 1 && !runtime.SaveOnNextEntry && !runtime.IsGameplay,
                "An unavailable first chapter cannot erase the current journey."); runtime.Config.chapters[0].scene = firstScene;

            Button staleContinue = Button(runtime, "继续游戏");
            Check(SaveSystem.SaveSlot(data), "Overwriting a record creates a recoverable backup.");
            File.WriteAllText(SaveSystem.PathForSlot(0), "damaged"); staleContinue.onClick.Invoke();
            Check(!runtime.IsGameplay && Button(runtime, "继续游戏", false) == null && HasText(runtime, "恢复备份"),
                "Continue revalidates changed records and offers recovery without loading damaged data.");
            yield return Capture(runtime, "menu-damaged-record", 1600, 900, true);
            Button(runtime, "读取记录").Select(); Button(runtime, "读取记录").onClick.Invoke();
            Button(runtime, "恢复备份").onClick.Invoke(); Button(runtime, "确认").onClick.Invoke(); runtime.Back();
            Check(Button(runtime, "继续游戏").IsInteractable() && EventSystem.current.currentSelectedGameObject == Button(runtime, "读取记录").gameObject,
                "Recovering a backup refreshes Continue and retains the originating menu focus.");
            Check(SaveSystem.DeleteSlot(0), "A recovered record can be removed in the isolated test.");
            data.sceneName = "MissingWater"; Check(SaveSystem.SaveSlot(data), "An unavailable scene record is prepared."); Rebuild(runtime);
            Check(Button(runtime, "继续游戏", false) == null && HasText(runtime, "暂不可用"), "Unavailable scenes are excluded from Continue with a readable explanation.");
            Check(UIRunState.Progress.completed.Count == 0 && UIRunState.Progress.memories.Count == 0,
                "Without a valid journey, chapter selection cannot inherit stale unlocks or memories.");
            yield return Capture(runtime, "menu-unavailable-scene", 1600, 900, true);

            UISettingsData access = UISettings.Current.Copy(); access.textScale = 1.3f; access.uiScale = 1.2f;
            access.language = 2; access.highContrast = access.reducedMotion = true; UISettings.Apply(access, false); Rebuild(runtime);
            yield return Capture(runtime, "menu-accessible-pseudo", 960, 540, true);
            Selectable selected = EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>();
            selected.GetComponent<UIButtonFeedback>().OnSubmit(new BaseEventData(EventSystem.current));
            Check(selected.transform.localScale == Vector3.one, "Reduced motion disables button scaling, including submit feedback.");
            UISettings.Apply(new UISettingsData { fullscreen = false, windowMode = 0 }, false); Rebuild(runtime);
            data.sceneName = "Map0"; data.hasPosition = true; data.posX = 3; data.posY = 4; data.posZ = 1;
            data.savedAtUtc = DateTime.UtcNow.AddMinutes(1).ToString("o"); Check(SaveSystem.SaveSlot(data), "Final continuation record is ready."); Rebuild(runtime);
            Button(runtime, "继续游戏").onClick.Invoke();
            while (SceneManager.GetActiveScene().name != "Map0") yield return null;
            for (int i = 0; i < 6; i++) yield return null;
            PlayerController player = FindFirstObjectByType<PlayerController>();
            Check(runtime.IsGameplay && player != null && Vector3.Distance(player.transform.position, new Vector3(3, 4, 1)) < 0.8f && UIRunState.Progress.memories.Count == 1,
                "Continue enters the actual saved Map0 scene and restores position and memories.");
            runtime.ShowPause(); Check(Time.timeScale == 0, "The shared button changes preserve gameplay pause behavior.");
        }

        private IEnumerator Capture(UIRuntime runtime, string name, int width, int height, bool menu = false)
        {
            Canvas canvas = runtime.GetComponentInChildren<Canvas>();
            var go = new GameObject("Menu verification camera", typeof(Camera)); Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = runtime.Config.paper;
            camera.cullingMask = 1 << 5; camera.orthographic = true;
            var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            yield return new WaitForSecondsRealtime(0.3f); yield return null; Canvas.ForceUpdateCanvases();
            if (menu && EventSystem.current.currentSelectedGameObject != null)
            {
                ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, new BaseEventData(EventSystem.current), ExecuteEvents.selectHandler);
                yield return null; yield return null; Canvas.ForceUpdateCanvases();
            }
            if (menu)
            {
                foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
                {
                    Text label = button.GetComponentInChildren<Text>();
                    Check(label.rectTransform.rect.height + 1 >= label.preferredHeight, "Readable button label at " + width + "x" + height + ": " + label.text + ".");
                }
                RectTransform art = Top(runtime).GetComponentInChildren<UIMenuAmbience>().transform as RectTransform;
                Check(art.rect.height >= 200, "The water illustration remains visible at " + width + "x" + height + ".");
                if (width == 800) Check(Top(runtime).GetComponentInChildren<UIMenuLayout>().Stacked, "The single menu column adapts to a narrow window.");
                if (!Top(runtime).GetComponentInChildren<UIMenuLayout>().Stacked && UISettings.Current.textScale == 1 && UISettings.Current.uiScale == 1)
                {
                    ScrollRect scroll = Top(runtime).Content.GetComponentInParent<ScrollRect>();
                    foreach (Button entry in Top(runtime).Content.GetComponentsInChildren<Button>())
                    {
                        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, entry.transform);
                        Check(bounds.min.y >= scroll.viewport.rect.yMin - 1 && bounds.max.y <= scroll.viewport.rect.yMax + 1,
                            "Main entry is visible without scrolling at " + width + "x" + height + ": " + entry.GetComponentInChildren<Text>().text + ".");
                    }
                }
            }
            camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); RenderTexture.active = previous;
            passed.Add("Rendered " + name + ".");
            if (menu)
            {
                CheckMenuSeparation(runtime, width, height);
                // Check focus visibility in the capture's dimensions before returning to the real screen dimensions.
                GameObject originalFocus = EventSystem.current.currentSelectedGameObject;
                foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
                {
                    button.Select(); yield return null; yield return null; Canvas.ForceUpdateCanvases();
                    CheckMenuSeparation(runtime, width, height);
                    ScrollRect scroll = button.GetComponentInParent<ScrollRect>();
                    if (scroll == null) continue;
                    Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, button.transform);
                    Check(bounds.min.y >= scroll.viewport.rect.yMin - 2 && bounds.max.y <= scroll.viewport.rect.yMax + 2,
                        "Focused control is visible at " + width + "x" + height + ": " + button.GetComponentInChildren<Text>().text + ".");
                }
                if (originalFocus != null) originalFocus.GetComponent<Selectable>().Select();
            }
            canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay; camera.targetTexture = null;
            Destroy(image); Destroy(target); Destroy(go);
        }

        private void CheckMenuSeparation(UIRuntime runtime, int width, int height)
        {
            UIPage page = Top(runtime);
            var controls = new List<RectTransform>();
            foreach (Button button in page.GetComponentsInChildren<Button>()) controls.Add((RectTransform)button.transform);
            controls.Add((RectTransform)page.transform.Find("Page/Menu title"));
            controls.Add((RectTransform)page.transform.Find("Page/Menu subtitle"));
            Transform recordStatus = page.Content.Find("Record status");
            if (recordStatus != null) controls.Add((RectTransform)recordStatus);
            if (page.NoticeArea.gameObject.activeInHierarchy) controls.Add(page.NoticeArea);
            ScrollRect scroll = page.Content.GetComponentInParent<ScrollRect>();
            for (int i = 0; i < controls.Count; i++)
                for (int j = i + 1; j < controls.Count; j++)
                {
                    Bounds a = RectBounds(page.transform, controls[i]);
                    Bounds b = RectBounds(page.transform, controls[j]);
                    // Compare visible rectangles: clipped content cannot cover the fixed header or footer.
                    if (controls[i].IsChildOf(scroll.content)) a = ClipToViewport(a, page, scroll);
                    if (controls[j].IsChildOf(scroll.content)) b = ClipToViewport(b, page, scroll);
                    bool overlap = a.size.x > 0 && a.size.y > 0 && b.size.x > 0 && b.size.y > 0 &&
                        Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > 1 &&
                        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > 1;
                    if (overlap) throw new Exception("Menu overlap at " + width + "x" + height + ": " + controls[i].name + " " + a + " / " + controls[j].name + " " + b + "; viewport=" + scroll.viewport.rect);
                }
            passed.Add("Menu controls and title do not overlap at " + width + "x" + height + ".");
        }
        private static Bounds ClipToViewport(Bounds bounds, UIPage page, ScrollRect scroll)
        {
            Bounds viewport = RectBounds(page.transform, scroll.viewport);
            Vector3 min = Vector3.Max(bounds.min, viewport.min), max = Vector3.Min(bounds.max, viewport.max);
            return new Bounds((min + max) * .5f, Vector3.Max(Vector3.zero, max - min));
        }
        private static Bounds RectBounds(Transform relativeTo, RectTransform rect)
        {
            Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Bounds bounds = new Bounds(relativeTo.InverseTransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; i++) bounds.Encapsulate(relativeTo.InverseTransformPoint(corners[i]));
            return bounds;
        }
    }
}
#endif
