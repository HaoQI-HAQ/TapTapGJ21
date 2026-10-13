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
    public sealed class UIVerificationRunner : MonoBehaviour
    {
        private static string output;
        private const string Requested = "PAO.UI.V2.VerificationRequested";
        private readonly List<string> passed = new List<string>();
        private readonly List<string> errors = new List<string>();
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeScene()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../VerificationArtifacts")); Directory.CreateDirectory(output);
            SaveSystem.EditorVerificationDirectory = Path.Combine(output, "Saves-" + Guid.NewGuid().ToString("N"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequested()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            SessionState.SetBool(Requested, false);
            var go = new GameObject("UI verification runner"); DontDestroyOnLoad(go);
            go.AddComponent<UIVerificationRunner>();
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            var stack = new Stack<IEnumerator>(); stack.Push(Scenarios());
            float deadline = Time.realtimeSinceStartup + 150;
            while (stack.Count > 0)
            {
                object current = null; bool moved = false;
                try
                {
                    if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Verification timed out.");
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception error) { errors.Add(error.ToString()); break; }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            Application.logMessageReceived -= OnLog;
            string report = "Passed: " + passed.Count + "\n" + string.Join("\n", passed) +
                "\n\nErrors: " + errors.Count + "\n" + string.Join("\n", errors);
            File.WriteAllText(Path.Combine(output, "report.txt"), report);
            Debug.Log(report);
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }

        private void OnLog(string text, string trace, LogType type)
        { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(text + "\n" + trace); }

        private void Check(bool condition, string description)
        { if (!condition) throw new Exception(description); passed.Add(description); }

        private static UIPage Top(UIRuntime runtime)
        {
            var pages = (List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(runtime);
            return pages.Count == 0 ? null : pages[pages.Count - 1];
        }

        private static Button FindButton(UIRuntime runtime, string label)
        {
            foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
                if (button.GetComponentInChildren<Text>().text == label) return button;
            throw new Exception("Missing button: " + label);
        }

        private IEnumerator Scenarios()
        {
            for (int i = 0; i < 5; i++) yield return null;
            UIRuntime runtime = UIRuntime.Instance;
            Check(runtime != null && runtime.Config.font != null && runtime.Config.round != null && runtime.Config.menuArt != null,
                "Runtime bootstraps with the existing Chinese font and artwork.");
            Check(Top(runtime) != null && !runtime.IsGameplay, "Fresh menu opens with an actionable default focus.");
            Check(EventSystem.current.currentSelectedGameObject != null, "Menu has a default selection.");
            foreach (Vector2Int resolution in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 800), new Vector2Int(1920, 820), new Vector2Int(960, 540) })
                yield return Capture(runtime, "menu-" + resolution.x + "x" + resolution.y, resolution.x, resolution.y);

            Check(!UIRunState.IsUnlocked(runtime.Config, 1), "The next chapter is locked before completing its predecessor.");
            UIRunState.Complete("Map0");
            Check(UIRunState.IsUnlocked(runtime.Config, 1), "Completing a chapter unlocks the next chapter.");
            runtime.ShowChapters(); yield return Capture(runtime, "chapters", 1600, 900); runtime.Back();
            runtime.ShowSaves();
            Check(EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(Top(runtime).transform) &&
                EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>().IsInteractable(),
                "Empty save pages focus the available Back control instead of disabled load buttons.");
            yield return Capture(runtime, "empty-records", 1600, 900); runtime.Back();

            string path = SaveSystem.PathForSlot(0);
            var data = new SaveSlotData { sceneName = "Map0", savedAtUtc = DateTime.UtcNow.ToString("o"), slot = 0,
                posX = 3, posY = 2, posZ = 1, hasPosition = true, runProgress = UIRunState.Progress };
            Check(SaveSystem.SaveSlot(data), "First versioned save succeeds.");
            Check(SaveSystem.LoadSlot(0).posX == 3, "Saved player position round-trips.");
            data.posX = 8; Check(SaveSystem.SaveSlot(data) && SaveSystem.HasBackup(0), "Overwrite retains a valid recovery backup.");
            string bytes = File.ReadAllText(path); File.WriteAllText(path, bytes.Replace("checksum", "brokenChecksum"));
            Check(SaveSystem.LoadSlot(0) == null && SaveSystem.HasBackup(0), "Corrupted checksums are rejected while retaining the backup.");
            runtime.ShowSaves(); yield return Capture(runtime, "damaged-record", 1600, 900); runtime.Back();
            Check(SaveSystem.RestoreBackup(0) && SaveSystem.LoadSlot(0).posX == 3 && File.Exists(path + ".damaged"), "Recovery preserves damaged bytes and restores the previous valid position.");
            data.version = 0; File.WriteAllText(SaveSystem.PathForSlot(1), JsonUtility.ToJson(data));
            Check(SaveSystem.LoadSlot(1) != null && SaveSystem.LoadSlot(1).hasPosition, "Plain V1 JSON saves remain readable.");
            data.version = 99; Check(!SaveSystem.ValidateData(data), "Future save versions are rejected.");
            data.version = 2; data.posX = float.NaN; Check(!SaveSystem.ValidateData(data), "Non-finite player positions are rejected.");

            typeof(UIRuntime).GetMethod("RefreshVisiblePage", Private).Invoke(runtime, null);
            yield return Capture(runtime, "menu-continue-1920x820", 1920, 820);
            Button credits = FindButton(runtime, "制作人员");
            ScrollRect menuScroll = credits.GetComponentInParent<ScrollRect>();
            Bounds creditsBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(menuScroll.viewport, credits.transform);
            Check(FindButton(runtime, "继续游戏").IsInteractable() &&
                creditsBounds.min.y >= menuScroll.viewport.rect.yMin - 1 && creditsBounds.max.y <= menuScroll.viewport.rect.yMax + 1,
                "The continued-run menu keeps every action visible on an ultrawide display.");
            RectTransform artwork = Top(runtime).transform.Find("Page/Scroll/Viewport/Content/Menu and illustration/Jellyfish paper art") as RectTransform;
            Text introduction = Top(runtime).transform.Find("Page/Scroll/Viewport/Content/Menu and illustration/Menu actions/Text").GetComponent<Text>();
            Check(artwork.rect.height >= 200 && introduction.rectTransform.rect.height >= introduction.preferredHeight - 1,
                "The continued-run menu retains its artwork and both lines of introduction.");

            runtime.LoadScene("Map0", SaveSystem.LoadSlot(0));
            while (SceneManager.GetActiveScene().name != "Map0") yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            PlayerController player = FindFirstObjectByType<PlayerController>();
            Check(player != null && Vector3.Distance(player.transform.position, new Vector3(3, 2, 1)) < 0.5f, "Loading enters the saved scene and restores player position.");
            yield return Capture(runtime, "gameplay-hud", 1600, 900);

            Time.timeScale = 0.37f; runtime.ShowPause();
            BubbleLauncher launcher = FindFirstObjectByType<BubbleLauncher>();
            Check(Time.timeScale == 0 && !player.enabled && launcher.enabled && launcher.InputBlocked,
                "Pause blocks gameplay input and preserves the launcher's state.");
            Check(!runtime.transform.Find("UI Canvas V2/Safe Area/Gameplay HUD").gameObject.activeSelf, "Pause hides the whole gameplay HUD.");
            Button settings = FindButton(runtime, "游戏设置"); settings.Select(); settings.onClick.Invoke();
            float master = UISettings.Current.master;
            Top(runtime).GetComponentInChildren<Slider>().value = 0.1f;
            Check(Mathf.Abs(UISettings.Current.master - 0.1f) < 0.01f, "Settings preview applies immediately.");
            yield return Capture(runtime, "settings", 1600, 900);
            Check(!runtime.transform.Find("UI Canvas V2/Safe Area/Context notice").gameObject.activeSelf &&
                Top(runtime).NoticeArea.gameObject.activeSelf,
                "Save feedback occupies the modal status area instead of covering the page header.");
            runtime.Back();
            Check(Mathf.Abs(UISettings.Current.master - master) < 0.01f && Time.timeScale == 0,
                "Cancelling settings rolls back the draft and keeps the game paused.");
            Check(EventSystem.current.currentSelectedGameObject == settings.gameObject, "Returning restores focus to the originating control.");
            runtime.Back();
            Check(Mathf.Abs(Time.timeScale - 0.37f) < 0.001f && player.enabled && !launcher.InputBlocked,
                "Resume restores the exact previous time scale and input state.");
            Time.timeScale = 1;

            for (int i = 0; i < 10; i++) { runtime.ShowPause(); runtime.ShowSettings(); runtime.Back(); runtime.Back(); }
            yield return null;
            Check(!runtime.IsBlockingGameplay && Time.timeScale == 1, "Rapid repeated pause/settings navigation leaves no modal or pause state behind.");
            int quality = QualitySettings.GetQualityLevel(); runtime.ShowSettings();
            FindButton(runtime, "画面").onClick.Invoke();
            foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
                if (button.GetComponentInChildren<Text>().text.StartsWith("画质")) { button.onClick.Invoke(); break; }
            runtime.Back();
            Check(QualitySettings.GetQualityLevel() == quality,
                "Cancelling video settings restores the actual quality level even when the saved setting is Current.");
            UISettingsData accessible = UISettings.Current.Copy();
            accessible.textScale = 1.3f; accessible.language = 2; accessible.reducedMotion = true; accessible.highContrast = true;
            UISettings.Apply(accessible); runtime.ShowSettings();
            yield return Capture(runtime, "settings-pseudo-130percent", 960, 540);
            runtime.Back(); UISettings.Apply(new UISettingsData { fullscreen = false });

            UIPrototypeLab lab = FindFirstObjectByType<UIPrototypeLab>();
            runtime.ShowDialogue(lab.dialogue); yield return Capture(runtime, "dialogue", 1600, 900);
            FindButton(runtime, "继续").onClick.Invoke(); FindButton(runtime, "继续").onClick.Invoke();
            Check(UIRunState.Progress.memories.Exists(m => m.id == lab.dialogue.id), "Read dialogue is archived in the memory journal.");
            runtime.Back(); runtime.ShowJournal(); yield return Capture(runtime, "journal", 1600, 900); runtime.Back();
            runtime.ShowConstruction(lab.site); FindButton(runtime, "预览建构").onClick.Invoke();
            yield return Capture(runtime, "construction-preview", 1600, 900);
            var previewPages = (List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(runtime);
            Check(previewPages[previewPages.Count - 2].GetComponent<CanvasGroup>().alpha == 0,
                "Construction previews expose the world by hiding the opaque recipe page.");
            int tokens = UIRunState.Progress.buildTokens; runtime.Back();
            Check(UIRunState.Progress.buildTokens == tokens, "Cancelling a construction preview consumes no energy.");
            Check(Top(runtime).GetComponent<CanvasGroup>().alpha == 1,
                "Cancelling a construction preview restores the recipe page.");
            FindButton(runtime, "预览建构").onClick.Invoke(); FindButton(runtime, "确认建构").onClick.Invoke();
            Check(UIRunState.Progress.buildTokens == tokens - 2 && UIRunState.Progress.builds.Count == 1,
                "Confirmed construction consumes energy and records its placement.");
            Check(lab.site.Remove() && UIRunState.Progress.buildTokens == tokens, "Removing construction returns its energy.");
            runtime.CompleteChapter(); yield return Capture(runtime, "chapter-complete", 1600, 900);
            Check(UIRunState.Progress.completed.Contains("Map0") && SaveSystem.LoadSlot(UIRunState.AutoSlot) != null,
                "Chapter completion records progress and creates an automatic save.");
            runtime.Config.endings = new[] { new EndingDefinition { id = "verification-ending", title = "示例结局", requiredMemories = 1 } };
            runtime.ShowEnding(); FindButton(runtime, "示例结局").onClick.Invoke(); runtime.Back();
            Check(string.IsNullOrEmpty(UIRunState.Progress.ending), "Cancelling ending confirmation does not commit an ending.");
            FindButton(runtime, "示例结局").onClick.Invoke(); FindButton(runtime, "确认").onClick.Invoke();
            Check(UIRunState.Progress.ending == "verification-ending" && SaveSystem.LoadSlot(UIRunState.AutoSlot).runProgress.ending == "verification-ending",
                "An ending is saved only after final confirmation.");
        }

        private IEnumerator Capture(UIRuntime runtime, string name, int width, int height)
        {
            Canvas canvas = runtime.GetComponentInChildren<Canvas>();
            var cameraGo = new GameObject("UI verification camera", typeof(Camera)); Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = runtime.IsGameplay ? runtime.Config.ink : runtime.Config.paper;
            camera.cullingMask = 1 << 5; camera.orthographic = true;
            var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            yield return new WaitForSecondsRealtime(0.26f);
            yield return null; yield return null; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null; Destroy(image); Destroy(target); Destroy(cameraGo);
            passed.Add("Rendered " + name + ".");
        }
    }
}
#endif
