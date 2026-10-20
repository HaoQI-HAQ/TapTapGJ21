#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DG.Tweening;
using PAO.UI;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    // This entry point can only run in a disposable copy carrying the verification marker.
    [InitializeOnLoad]
    public static class UIChapterCompletionVerification
    {
        internal const string Requested = "PAO.UI.ChapterCompletionVerification";
        internal const string FixtureOnly = "PAO.UI.ChapterCompletionFixtureOnly";
        static UIChapterCompletionVerification()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Requested, false)) return;
                SessionState.SetBool(Requested, false);
                EditorApplication.delayCall += () => new GameObject("Chapter completion verification").AddComponent<UIChapterCompletionVerificationRunner>();
            };
        }

        public static void RunFixtureCLI() { SessionState.SetBool(FixtureOnly, true); RunCLI(); }
        public static void RunCLI()
        {
            if (!File.Exists("PAO_UI_VERIFICATION_PROJECT"))
                throw new InvalidOperationException("Chapter completion verification requires an isolated project, never the source project.");
            PlayerSettings.companyName = "CodexChapterCompletionVerification";
            PlayerSettings.productName = "PAOChapterCompletionVerification";
            typeof(UIV2Verification).GetMethod("RemapCompiledPackageScripts", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            const string folder = "Assets/ChapterCompletionVerification";
            Directory.CreateDirectory(folder);
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string name in new[] { "Menu", "Map4", "Map3_5" })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                string path = folder + "/" + name + ".unity";
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path);
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            SessionState.SetBool(Requested, true);
            EditorApplication.isPlaying = true;
        }
    }

    public sealed class UIChapterCompletionVerificationRunner : MonoBehaviour
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<string> passed = new List<string>(), errors = new List<string>();
        private string output;
        private Fixture fixture;

        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            output = Path.GetFullPath("ChapterCompletionArtifacts"); Directory.CreateDirectory(output);
            SaveSystem.EditorVerificationDirectory = Path.Combine(output, "IsolatedSaves-" + Guid.NewGuid().ToString("N"));
            Application.logMessageReceived += OnLog;
            DOTween.logBehaviour = LogBehaviour.Verbose;
            DOTween.KillAll();
            if (UIRuntime.Instance != null) Object.DestroyImmediate(UIRuntime.Instance.gameObject);
            foreach (EventSystem existing in Object.FindObjectsOfType<EventSystem>()) Object.DestroyImmediate(existing.gameObject);
            UISettings.Initialize();
            var stack = new Stack<IEnumerator>(); stack.Push(Scenarios());
            float deadline = Time.realtimeSinceStartup + 100;
            while (stack.Count > 0)
            {
                bool moved = false; object current = null;
                try
                {
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Completion verification timed out.");
                    moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current;
                }
                catch (Exception error) { errors.Add(error.ToString()); break; }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            fixture?.Dispose(); fixture = null;
            Application.logMessageReceived -= OnLog;
            string report = "Passed: " + passed.Count + "\n" + string.Join("\n", passed) + "\n\nErrors: " + errors.Count + "\n" + string.Join("\n", errors) +
                "\n\nSource: D:/TapTap21/TapTapGJ21-clone/UnityProject/PAO\n" +
                "Screenshots use real uGUI components and Unity rendering. Map3_5 integration uses its actual chapter configuration with an isolated player fixture and isolated saves; the complex map geometry is not rendered.\n";
            File.WriteAllText(Path.Combine(output, "report.txt"), report); Debug.Log(report);
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }

        private IEnumerator Scenarios()
        {
            foreach (string resource in new[] { "Badge", "Current", "Frame", "Heading", "Hero", "Memory", "Paper", "Primary", "Secondary" })
                Check(Resources.Load<Sprite>("UIChapterCompletion/" + resource) != null, "Completion artwork imports: " + resource + ".");
            yield return CaptureFixture("reference-1600x900", 1600, 900);
            if (SessionState.GetBool(UIChapterCompletionVerification.FixtureOnly, false))
            { SessionState.SetBool(UIChapterCompletionVerification.FixtureOnly, false); yield break; }
            yield return CaptureFixture("reference-1672x941", 1672, 941);
            yield return CaptureFixture("reference-2560x1080", 2560, 1080);
            yield return CaptureFixture("reference-1024x768", 1024, 768);
            yield return CaptureFixture("reference-800x900", 800, 900);
            yield return CaptureFixture("reference-english-large-1600x900", 1600, 900, 1, 1.3f);
            yield return CaptureFixture("reference-english-large-800x900", 800, 900, 1, 1.3f);
            yield return CaptureFixture("reference-phone-390x844", 390, 844);
            yield return CaptureFixture("reference-phone-320x568", 320, 568);
            yield return CaptureLongTitle();

            ApplySettings(0, 1); fixture = new Fixture(1600, 900, true); UIRunState.NewRun();
            UIRunState.Progress.playSeconds = 1238;
            for (int i = 0; i < 3; i++) UIRunState.AddMemory(new MemoryEntry { id = "completion-" + i, chapter = "Map3_5", title = "验证碎片 " + i, body = "Isolated verification memory." });
            Check(fixture.Runtime.Config.IndexOf("Map3_5") == 5, "The clone config retains Map3_5 as chapter six.");
            Check(fixture.Runtime.CompleteChapter(), "Actual CompleteChapter accepts the isolated Map3_5 player state.");
            fixture.Settle(); UIChapterCompletionView actual = fixture.View;
            Check(actual != null && actual.MemoryValue.text == "03" && actual.TimeValue.text == "00:20:38" && actual.ChapterLabel.text.Contains("06"), "Actual completion displays chapter six and live memory/time values.");
            SaveSlotData saved = SaveSystem.LoadSlot(UIRunState.AutoSlot);
            Check(saved != null && saved.sceneName == "Map3_5" && saved.runProgress.completed.Contains("Map3_5") && saved.runProgress.memories.Count == 3,
                "Actual CompleteChapter persists chapter progress and all three memories to the isolated automatic slot.");
            Check(saved.hasPosition && Math.Abs(saved.posX - 4) < .01 && Math.Abs(saved.posY - 2) < .01 && Math.Abs(saved.posZ - 7) < .01,
                "The automatic record retains the actual fixture player coordinates.");
            Check(actual.SaveStatusLabel.text == "已留下旅程记录", "The completion page displays the real successful save result.");
            Check(!fixture.Runtime.CompleteChapter() && fixture.Runtime.GetComponentsInChildren<UIChapterCompletionView>().Length == 1,
                "Repeated completion cannot duplicate the page or commit progress twice.");
            ValidateControls(actual, "actual-map3_5"); fixture.Capture(Path.Combine(output, "actual-map3_5-completed.png"));
            actual.ReviewButton.Select(); actual.ReviewButton.onClick.Invoke(); fixture.Settle();
            Check(fixture.Top.Design == UIPageDesign.Journal, "Review memories opens the real journal page.");
            fixture.Runtime.Back(); fixture.Settle();
            Check(EventSystem.current.currentSelectedGameObject == actual.ReviewButton.gameObject, "Closing the journal restores focus to Review memories.");
            actual.ChaptersButton.Select(); actual.ChaptersButton.onClick.Invoke(); fixture.Settle();
            Check(fixture.Top.Design == UIPageDesign.Chapters, "Chapter path opens the real chapter list.");
            fixture.Runtime.Back(); fixture.Settle();
            Check(EventSystem.current.currentSelectedGameObject == actual.ChaptersButton.gameObject, "Closing the chapter list restores the originating focus.");
            actual.PrimaryButton.onClick.Invoke();
            Check(fixture.Runtime.SaveOnNextEntry && (bool)Get(fixture.Runtime, "loading"), "Next chapter requests the configured route and saving on entry.");
            yield return WaitForScene("Map4");
            Check(SceneManager.GetActiveScene().name == "Map4", "The actual primary callback enters Map4 after Map3_5.");
            fixture.Dispose(); fixture = null;
            SceneManager.LoadScene("Map3_5"); yield return null;
            ApplySettings(0, 1); fixture = new Fixture(1600, 900, false); UIRunState.NewRun();
            Check(fixture.Runtime.CompleteChapter(), "Completion remains available when there is no player state to save.");
            fixture.Settle();
            Check(fixture.View.SaveStatusLabel.text.Contains("未能保存"), "Save failure is shown honestly on the completion page.");
            fixture.View.MenuButton.onClick.Invoke(); yield return WaitForScene("Menu");
            Check(SceneManager.GetActiveScene().name == "Menu", "The actual Main menu callback enters the configured menu scene.");
            fixture.Dispose(); fixture = null;
        }

        private IEnumerator CaptureFixture(string name, int width, int height, int language = 0, float scale = 1)
        {
            ApplySettings(language, scale); fixture = new Fixture(width, height, false); UIRunState.NewRun();
            string saveRoot = SaveSystem.EditorVerificationDirectory;
            int before = Directory.Exists(saveRoot) ? Directory.GetFiles(saveRoot, "*", SearchOption.AllDirectories).Length : 0;
            fixture.Runtime.ShowChapterCompletionStylePreview(); fixture.Settle();
            UIChapterCompletionView view = fixture.View;
            Check(view != null && view.MemoryValue.text == "00" && view.TimeValue.text == "00:20:38", name + " displays the reference fixture values.");
            Check(before == (Directory.Exists(saveRoot) ? Directory.GetFiles(saveRoot, "*", SearchOption.AllDirectories).Length : 0) && UIRunState.Progress.completed.Count == 0,
                name + " preview neither completes a chapter nor writes a save.");
            ValidateControls(view, name); ValidateLayout(view, width, height, name);
            fixture.Capture(Path.Combine(output, name + ".png"));
            passed.Add("Rendered " + name + ".png.");
            fixture.Dispose(); fixture = null; yield return null;
        }

        private void ValidateControls(UIChapterCompletionView view, string name)
        {
            Button[] buttons = { view.PrimaryButton, view.ReviewButton, view.ChaptersButton, view.MenuButton };
            Check(Array.TrueForAll(buttons, button => button != null && button.IsActive() && button.IsInteractable()), name + " exposes four usable actions.");
            Check(EventSystem.current.currentSelectedGameObject == view.PrimaryButton.gameObject, name + " focuses the primary action by default.");
            Check(view.PrimaryButton.navigation.selectOnDown == view.ReviewButton && view.ReviewButton.navigation.selectOnDown == view.ChaptersButton &&
                view.ChaptersButton.navigation.selectOnRight == view.MenuButton && view.MenuButton.navigation.selectOnLeft == view.ChaptersButton,
                name + " has connected directional navigation across all four actions.");
        }

        private IEnumerator CaptureLongTitle()
        {
            ApplySettings(0, 1); fixture = new Fixture(390, 844, false); UIRunState.NewRun();
            int index = fixture.Runtime.Config.chapters.Length - 1;
            ChapterDefinition chapter = fixture.Runtime.Config.chapters[index];
            Set(fixture.Runtime, "lastScene", chapter.scene); UIRunState.Progress.playSeconds = 1238;
            Check(fixture.Runtime.CompleteChapter(), "Actual completion accepts the clone's final chapter.");
            fixture.Settle();
            Check(fixture.View.ChapterLabel.text == chapter.title, "The completion page keeps the real long final chapter title from the clone config.");
            ValidateControls(fixture.View, "actual-long-title-390x844");
            ValidateLayout(fixture.View, 390, 844, "actual-long-title-390x844");
            fixture.Capture(Path.Combine(output, "actual-long-title-390x844.png"));
            passed.Add("Rendered actual-long-title-390x844.png.");
            fixture.View.PrimaryButton.onClick.Invoke(); fixture.Settle();
            Check(fixture.Top.Design == UIPageDesign.Ending, "The final chapter primary callback opens the real ending page.");
            fixture.Dispose(); fixture = null; yield return null;
        }

        private void ValidateLayout(UIChapterCompletionView view, int width, int height, string name)
        {
            var corners = new Vector3[4];
            foreach (Button button in new[] { view.PrimaryButton, view.ReviewButton, view.ChaptersButton, view.MenuButton })
            {
                button.GetComponent<RectTransform>().GetWorldCorners(corners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(fixture.Camera, corners[0]);
                Vector2 high = RectTransformUtility.WorldToScreenPoint(fixture.Camera, corners[2]);
                Check(low.x >= -1 && low.y >= -1 && high.x <= width + 1 && high.y <= height + 1 && high.x > low.x && high.y > low.y,
                    name + " keeps " + button.name + " inside the viewport.");
                float minimumHeight = width <= 400 ? 48 : 40;
                Check(high.y - low.y >= minimumHeight - .01f, name + " gives " + button.name + " at least " + minimumHeight + "px screen height.");
            }
            foreach (Text text in view.GetComponentsInChildren<Text>())
            {
                if (!text.gameObject.activeInHierarchy || text.text.Length == 0 || text.name == "Chapter number outline") continue;
                Check(text.rectTransform.rect.height + 2 >= text.preferredHeight || text.resizeTextForBestFit,
                    name + " reserves a fitting text box for " + text.name + ".");
            }
        }

        private static void ApplySettings(int language, float scale)
        { UISettings.Apply(new UISettingsData { language = language, textScale = scale, reducedMotion = true, tutorials = false, uiScale = 1 }, false); }
        private IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline) yield return null;
        }
        private void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed.Add(message); }
        private void OnLog(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
        internal static void Set(object value, string name, object data) { value.GetType().GetField(name, Private).SetValue(value, data); }
        internal static object Get(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
        internal static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Private).Invoke(value, args);

        private sealed class Fixture : IDisposable
        {
            internal UIRuntime Runtime;
            internal Camera Camera;
            private GameObject root;
            private Canvas canvas;
            private RenderTexture target;
            internal UIPage Top { get { var pages = (List<UIPage>)Get(Runtime, "pages"); return pages.Count == 0 ? null : pages[pages.Count - 1]; } }
            internal UIChapterCompletionView View => Top != null ? Top.GetComponent<UIChapterCompletionView>() : null;

            internal Fixture(int width, int height, bool withPlayer)
            {
                root = new GameObject("Isolated completion fixture"); Object.DontDestroyOnLoad(root);
                Runtime = root.AddComponent<UIRuntime>(); Runtime.enabled = false;
                var config = Object.Instantiate(Resources.Load<UIConfig>("PAOUIConfig"));
                Set(Runtime, "<Config>k__BackingField", config); Set(Runtime, "<IsGameplay>k__BackingField", true);
                Set(Runtime, "gameplayTheme", config); Set(Runtime, "menuTheme", Object.Instantiate(config)); Set(Runtime, "lastScene", "Map3_5");
                typeof(UIRuntime).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Runtime);
                var ui = new UIWidgets(config); Set(Runtime, "ui", ui);
                Camera = new GameObject("Completion capture camera", typeof(Camera)).GetComponent<Camera>(); Camera.transform.SetParent(root.transform);
                Camera.gameObject.AddComponent<AudioListener>();
                Camera.transform.position = new Vector3(0, 0, -10); Camera.orthographic = true; Camera.clearFlags = CameraClearFlags.SolidColor; Camera.backgroundColor = Color.white;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create(); Camera.targetTexture = target;
                var canvasRoot = ui.Rect("UI Canvas V2", root.transform); canvas = canvasRoot.gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = Camera; canvas.planeDistance = 1; canvas.scaleFactor = height / 900f;
                canvasRoot.gameObject.AddComponent<CanvasScaler>(); canvasRoot.gameObject.AddComponent<GraphicRaycaster>(); Set(Runtime, "canvas", canvas);
                var safe = ui.Rect("Safe Area", canvasRoot); UIWidgets.Stretch(safe); Set(Runtime, "safeRoot", safe);
                var events = new GameObject("Completion verification events", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(root.transform);
                EventSystem.current = events.GetComponent<EventSystem>();
                Set(Runtime, "loadingTransition", UIBubbleTransition.Create(root.transform, config));
                if (withPlayer)
                {
                    var actor = new GameObject("Isolated completion player"); actor.SetActive(false); actor.transform.SetParent(root.transform); actor.transform.position = new Vector3(4, 2, 7);
                    actor.AddComponent<CharacterController>(); var player = actor.AddComponent<PlayerController>(); player.enabled = false; actor.SetActive(true); Set(Runtime, "player", player);
                }
                Canvas.ForceUpdateCanvases();
            }
            internal void Settle()
            {
                DOTween.CompleteAll(true); DOTween.KillAll();
                for (int i = 0; i < 3; i++) { Canvas.ForceUpdateCanvases(); View?.RefreshLayout(); }
                Canvas.ForceUpdateCanvases();
            }
            internal void Capture(string path)
            {
                Camera.Render(); RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
                var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG()); RenderTexture.active = previous; Object.DestroyImmediate(pixels);
            }
            public void Dispose()
            {
                DOTween.KillAll(); if (Runtime != null) Call(Runtime, "ReleasePause");
                if (root != null) Object.DestroyImmediate(root);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Time.timeScale = 1;
            }
        }
    }
}
#endif
