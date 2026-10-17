#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using DG.Tweening;
using PAO.UI;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    // Render actual page builders with sample data, in a disposable project only.
    // This tool never runs game commands, commits settings, or reads a player's save directory.
    [InitializeOnLoad]
    public static class UIVisualDesignReview
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string output;
        private static int captures;

        static UIVisualDesignReview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("PAO.UI.VisualReview", false)) return;
                SessionState.SetBool("PAO.UI.VisualReview", false);
                EditorApplication.delayCall += Generate;
            };
        }
        public static void RunCLI()
        {
            if (!File.Exists("PAO_UI_VERIFICATION_PROJECT")) throw new InvalidOperationException("Use an isolated visual-review project.");
            PlayerSettings.companyName = "CodexVisualReview"; PlayerSettings.productName = "PAOUIDesignReview";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var scenes = new EditorBuildSettingsScene[7]; scenes[0] = new EditorBuildSettingsScene("Assets/GameMain/UI/Menu.unity", true);
            for (int i = 0; i < 6; i++) scenes[i + 1] = new EditorBuildSettingsScene("Assets/GameMain/Scenes/Map" + i + ".unity", true);
            EditorBuildSettings.scenes = scenes;
            SessionState.SetBool("PAO.UI.VisualReview", true);
            EditorApplication.isPlaying = true;
        }
        private static void Generate()
        {
            if (!File.Exists("PAO_UI_VERIFICATION_PROJECT")) throw new InvalidOperationException("Use an isolated visual-review project.");
            PlayerSettings.companyName = "CodexVisualReview"; PlayerSettings.productName = "PAOUIDesignReview";
            output = Path.Combine(Directory.GetCurrentDirectory(), "VisualReview"); Directory.CreateDirectory(output);
            SaveSystem.EditorVerificationDirectory = Path.Combine(output, "SampleRecords", Guid.NewGuid().ToString("N"));
            UISettings.Initialize(); UISettings.Apply(new UISettingsData { reducedMotion = true, tutorials = false }, false);
            UIRunState.NewRun();
            UIRunState.Progress.memories.Add(new MemoryEntry { id = "saved-sample", chapter = "Map0", title = "第一段回声", body = "来自示例记录的意识碎片。" });
            SaveSystem.SaveSlot(new SaveSlotData { slot = 0, sceneName = "Map0", savedAtUtc = "2026-10-13T10:24:00Z", runProgress = UIRunState.Progress.Copy() });
            Render("menu", false, r => Call(r, "ShowMenu"));
            Render("pause", true, r => r.ShowPause());
            Render("chapters", true, r => r.ShowChapters());
            Render("records", true, r => r.ShowSaves());
            for (int tab = 0; tab < 6; tab++)
            {
                int selected = tab;
                Render("settings-" + tab, true, r => SettingsTab(r, selected));
            }
            Render("settings-keyboard-options", true, r => InputOptions(r, false));
            Render("settings-controller-options", true, r => InputOptions(r, true));
            Render("settings-controller-portrait", true, r => SettingsTab(r, 3), 800, 900, false, 1.3f);
            Render("settings-controller-portrait-binding", true, r => SettingsTab(r, 3), 800, 900, false, 1.3f, 0,
                r => { foreach (Button button in r.GetComponentsInChildren<Button>()) if (button.name == "Bind " + UIInput.Jump) { button.Select(); break; } });
            Render("keyboard-capture", true, r => { SettingsTab(r, 2); r.GetComponentInChildren<UIPage>().transform.Find("Page").GetComponentsInChildren<Button>().FirstBinding().onClick.Invoke(); });
            Render("controller-capture", true, r => { SettingsTab(r, 3); r.GetComponentInChildren<UIPage>().transform.Find("Page").GetComponentsInChildren<Button>().FirstBinding().onClick.Invoke(); });
            Render("binding-conflict", true, r =>
            {
                SettingsTab(r, 2); object session = typeof(UIRuntime).GetField("settingsSession", Private).GetValue(r);
                Call(r, "ChangeBinding", session, UIInput.Forward, false, KeyCode.S, ControllerControl.None);
            });
            Render("guide", true, r => r.ShowHelp());
            Render("journal", true, r => r.ShowJournal());
            Render("journal-empty", true, r => { UIRunState.Progress.memories.Clear(); r.ShowJournal(); });
            Render("dialogue", true, r =>
            {
                DialogueAsset dialogue = ScriptableObject.CreateInstance<DialogueAsset>(); dialogue.id = "visual-review"; dialogue.title = "水流中的回声";
                dialogue.lines = new[] { new DialogueLine { speaker = "来自水流", text = "有些回声不会消散。它们会随着泡泡，陪你去往下一片水域。" } };
                r.ShowDialogue(dialogue);
            });
            Render("dialogue-choices", true, r =>
            {
                DialogueAsset dialogue = ScriptableObject.CreateInstance<DialogueAsset>(); dialogue.title = "回声的方向";
                dialogue.choices = new[] { new DialogueChoice { text = "继续沿着水流探索" }, new DialogueChoice { text = "在这里留下一片意识碎片" } };
                Call(r, "ShowDialogueChoices", dialogue);
            });
            Render("construction", true, r => r.ShowConstruction(BuildSample()));
            Render("construction-preview", true, r => { BuildSite site = BuildSample(); r.ShowConstruction(site); Call(r, "ShowBuildPreview", site, site.recipes[0]); });
            Render("construction-preview-narrow", true, r => { BuildSite site = BuildSample(); r.ShowConstruction(site); Call(r, "ShowBuildPreview", site, site.recipes[0]); }, 800, 900, false, 1.3f);
            Render("completion", true, r => r.CompleteChapter());
            Render("ending", true, r => { UIRunState.Progress.completed.Add("Map0"); r.ShowEnding(); });
            Render("ending-sample-branches", true, r =>
            {
                r.Config.endings = new[] {
                    new EndingDefinition { id = "sample-a", title = "随水而行", description = "示例分支：回望已经抵达的水域，选择继续前行。", requiredMemories = 0 },
                    new EndingDefinition { id = "sample-b", title = "留住回声", description = "示例分支：收集更多意识碎片，留下另一种回响。", requiredMemories = 3 } };
                UIRunState.Progress.completed.Add("Map0"); r.ShowEnding();
            });
            Render("failure", true, r => r.ShowFailure());
            Render("credits", false, r => Call(r, "ShowCredits"));
            Render("confirmation", true, r => Call(r, "Confirm", "重新开始本章？", "本章尚未保存的变化会重置。", (Action)(() => { }), true));
            Render("controller-layout", true, r => Call(r, "ShowInputDiagram", true));
            Render("keyboard-layout", true, r => Call(r, "ShowInputDiagram", false));
            Render("controller-layout-narrow", true, r => Call(r, "ShowInputDiagram", true), 800, 900, false, 1.3f);
            Render("keyboard-layout-narrow", true, r => Call(r, "ShowInputDiagram", false), 800, 900, false, 1.3f);
            Render("hud", true, r => BuildSampleHud(r));
            Render("hud-detailed", true, r => BuildSampleHud(r, true));
            Render("hud-guidance", true, r =>
            {
                BuildSampleHud(r); r.PulseVisualCue(UIGuideSymbol.Move, UIInput.Forward, 5); Call(r, "UpdateVisualGuidance");
            });
            Render("subtitle-narrow", true, r =>
            {
                BuildSampleHud(r);
                r.ShowSubtitle("来自水流", "有些回声不会消散。它们会随着泡泡，陪你去往下一片水域。");
            }, 800, 900, false, 1.3f, 0, r => Call(r, "UpdateTransientUI"));
            Render("loading", true, r =>
            {
                Canvas capture = r.GetComponentInChildren<Canvas>();
                UIBubbleTransition transition = UIBubbleTransition.Create(r.transform, r.Config);
                transition.Begin(true); transition.SetProgress(.58f);
                Canvas curtain = transition.GetComponent<Canvas>();
                curtain.renderMode = RenderMode.ScreenSpaceCamera;
                curtain.worldCamera = capture.worldCamera; curtain.planeDistance = 1;
            });
            Render("menu-contrast", false, r => Call(r, "ShowMenu"), 1600, 900, true);
            Render("pause-contrast", true, r => r.ShowPause(), 1600, 900, true);
            Render("menu-wide", false, r => Call(r, "ShowMenu"), 2100, 900);
            Render("pause-narrow-large-text", true, r => r.ShowPause(), 960, 720, false, 1.3f);
            Render("settings-narrow-large-text", true, r => r.ShowSettings(), 960, 720, false, 1.3f);
            Render("menu-english", false, r => Call(r, "ShowMenu"), 1600, 900, false, 1, 1);
            Render("settings-pseudo", true, r => r.ShowSettings(), 1600, 900, false, 1.3f, 2);
            File.WriteAllText(Path.Combine(output, "capture-summary.txt"), captures + " actual Unity page captures. Sample data and isolated sample records only. No player records or persistent settings were modified.");
            EditorApplication.Exit(0);
        }

        private static object Call(UIRuntime runtime, string name, params object[] args) => typeof(UIRuntime).GetMethod(name, Private).Invoke(runtime, args);
        private static void Set(UIRuntime runtime, string name, object value) => typeof(UIRuntime).GetField(name, Private).SetValue(runtime, value);
        private static void SettingsTab(UIRuntime runtime, int index)
        {
            runtime.ShowSettings();
            Transform tabs = runtime.GetComponentInChildren<UIPage>().transform.Find("Page/Settings tabs");
            Button button = tabs.GetChild(index).GetComponent<Button>(); button.Select(); button.onClick.Invoke();
        }
        private static void InputOptions(UIRuntime runtime, bool controller)
        {
            SettingsTab(runtime, controller ? 3 : 2);
            Transform content = runtime.GetComponentInChildren<UIPage>().Content.Find("Settings workbench/Input sections");
            Button button = content.GetChild(1).GetComponent<Button>(); button.Select(); button.onClick.Invoke();
        }
        private static Button FirstBinding(this Button[] buttons)
        {
            foreach (Button button in buttons) if (button.name.StartsWith("Bind ")) return button;
            throw new InvalidOperationException("No binding button was created.");
        }
        private static void BuildSampleHud(UIRuntime runtime, bool detailed = false)
        {
            UISettingsData settings = UISettings.Current.Copy(); settings.detailedHints = detailed;
            UISettings.Apply(settings, false);
            GameObject fixture = new GameObject("Selected ability sample"); fixture.transform.SetParent(runtime.transform);
            fixture.SetActive(false); Set(runtime, "switcher", fixture.AddComponent<PAO.BubbleTypeSwitcher>());
            Call(runtime, "BuildHud"); Call(runtime, "UpdateVisualGuidance");
        }
        private static BuildSite BuildSample()
        {
            GameObject root = new GameObject("Visual review site"); root.SetActive(false);
            BuildSite site = root.AddComponent<BuildSite>(); site.id = "sample-site"; site.placement = root.transform;
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube); source.name = "Sample platform"; source.SetActive(false);
            BuildRecipe recipe = ScriptableObject.CreateInstance<BuildRecipe>(); recipe.id = "sample-platform"; recipe.title = "水流阶台";
            recipe.description = "让浮粘泡泡与阶台相接，建立通向高处的路径。"; recipe.cost = 2; recipe.prefab = source;
            site.recipes = new[] { recipe }; root.SetActive(true); return site;
        }

        private static void Render(string name, bool gameplay, Action<UIRuntime> build, int width = 1600, int height = 900, bool contrast = false, float textScale = 1, int language = 0, Action<UIRuntime> afterLayout = null)
        {
            UISettings.Apply(new UISettingsData { reducedMotion = true, tutorials = false, highContrast = contrast, textScale = textScale, language = language }, false);
            UIRunState.ResetSession(); UIRunState.NewRun();
            UIRunState.Progress.playSeconds = 823; UIRunState.Progress.buildTokens = 8;
            UIRunState.Progress.memories.Add(new MemoryEntry { id = "sample-1", chapter = "Map0", title = "第一片水域", body = "沿着浮粘泡泡组成的路径，水流将你带向高处。" });
            UIRunState.Progress.memories.Add(new MemoryEntry { id = "sample-2", chapter = "Map0", title = "留下的涟漪", body = "每一次相遇，都在意识之海留下了新的回声。" });
            UIConfig theme = Object.Instantiate(Resources.Load<UIConfig>("PAOUIConfig")); UIHandDrawnForPreview(theme);
            theme.paperStyle = !gameplay; if (!gameplay) theme.menuArt = theme.menuPaperArt;
            var stage = new GameObject("Visual review stage");
            UIRuntime runtime = stage.AddComponent<UIRuntime>();
            Set(runtime, "<Config>k__BackingField", theme); Set(runtime, "<IsGameplay>k__BackingField", gameplay);
            Set(runtime, "gameplayTheme", theme); Set(runtime, "menuTheme", theme); Set(runtime, "lastScene", "Map0");
            UIWidgets ui = new UIWidgets(theme); Set(runtime, "ui", ui);
            Camera camera = new GameObject("Visual review camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(stage.transform);
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = UIDesignPalette.Paper;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            RectTransform canvasRoot = ui.Rect("UI Canvas V2", stage.transform);
            Canvas canvas = canvasRoot.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.sortingOrder = 1000;
            canvas.scaleFactor = height / 900f;
            Set(runtime, "canvas", canvas);
            RectTransform safe = ui.Rect("Safe Area", canvasRoot); UIWidgets.Stretch(safe); Set(runtime, "safeRoot", safe);
            GameObject events = new GameObject("Visual review events", typeof(EventSystem)); events.transform.SetParent(stage.transform);
            build(runtime);
            // Finish entrance fades while preserving layers intentionally hidden by the page builder.
            DOTween.CompleteAll(true); DOTween.KillAll();
            for (int i = 0; i < 6; i++)
            {
                Canvas.ForceUpdateCanvases();
                foreach (UIMenuResponsiveDesign layout in stage.GetComponentsInChildren<UIMenuResponsiveDesign>()) layout.SendMessage("LateUpdate");
                foreach (UIMenuLayout layout in stage.GetComponentsInChildren<UIMenuLayout>()) layout.SendMessage("LateUpdate");
                foreach (UICompactPageLayout layout in stage.GetComponentsInChildren<UICompactPageLayout>()) layout.SendMessage("LateUpdate");
                foreach (UIConstructionPreviewLayout layout in stage.GetComponentsInChildren<UIConstructionPreviewLayout>()) layout.SendMessage("LateUpdate");
                foreach (UIPage page in stage.GetComponentsInChildren<UIPage>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)page.transform.Find("Page"));
            }
            foreach (UIDeviceDiagramFit fit in stage.GetComponentsInChildren<UIDeviceDiagramFit>()) fit.SendMessage("LateUpdate");
            afterLayout?.Invoke(runtime); Canvas.ForceUpdateCanvases();
            foreach (ScrollRect scroll in stage.GetComponentsInChildren<ScrollRect>()) scroll.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases();
            // Builders use deferred Destroy during tab changes; omit old disabled rows from this still frame.
            camera.Render(); RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG()); captures++;
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            Call(runtime, "ClearPages"); Call(runtime, "ClearHud"); DOTween.KillAll();
            Object.DestroyImmediate(stage); Object.DestroyImmediate(image); Object.DestroyImmediate(target);
            foreach (BuildSite site in Object.FindObjectsByType<BuildSite>(FindObjectsSortMode.None)) Object.DestroyImmediate(site.gameObject);
            foreach (GameObject obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)) if (obj.name == "Sample platform" || obj.name == "Construction placement preview") Object.DestroyImmediate(obj);
        }
        private static void UIHandDrawnForPreview(UIConfig theme)
        {
            Type type = typeof(UIConfig).Assembly.GetType("PAO.UI.V2.UIHandDrawn"); type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { theme });
        }
    }
}
#endif
