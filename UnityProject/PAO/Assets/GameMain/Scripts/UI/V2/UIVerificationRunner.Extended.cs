#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private void VerifyProgressAndBindings()
        {
            var old = new UISettingsData { keys = new[] { KeyCode.Q, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.E, KeyCode.J, KeyCode.F1 } };
            old.Validate();
            Check(old.keys.Length == 17 && old.keys[0] == KeyCode.Q && old.keys[UIInput.Jump] == KeyCode.Space,
                "Six-action settings migrate to seventeen actions while retaining custom bindings.");
            var invalid = new UISettingsData { keys = new[] { KeyCode.Escape, KeyCode.E, KeyCode.E, (KeyCode)999 } };
            invalid.Validate(); var unique = new System.Collections.Generic.HashSet<KeyCode>(invalid.keys);
            Check(unique.Count == 17 && Array.TrueForAll(invalid.keys, UIInput.IsBindable),
                "Invalid and duplicate bindings are repaired without stealing UI navigation keys.");
            Check(UIInput.IsBindable(KeyCode.Mouse0) && !UIInput.IsBindable(KeyCode.Return) && !UIInput.IsBindable(KeyCode.JoystickButton0),
                "Mouse bindings are accepted and menu/controller navigation stays reserved.");
            var malformed = new RunProgress { playSeconds = float.PositiveInfinity, buildTokens = -8, bubbleType = 90 };
            malformed.completed.AddRange(new[] { "Map0", null, "Map0" });
            malformed.memories.Add(null); malformed.memories.Add(new MemoryEntry { id = "fragment" }); malformed.memories.Add(new MemoryEntry { id = "fragment" });
            malformed.builds.Add(null); malformed.builds.Add(new BuiltItem { scene = "Map0", site = "s", recipe = "r", cost = -2 });
            malformed.Validate();
            Check(malformed.completed.Count == 1 && malformed.memories.Count == 1 && malformed.builds.Count == 0 &&
                malformed.playSeconds == 0 && malformed.buildTokens == 0 && malformed.bubbleType == 2,
                "Malformed progress normalizes null entries, duplicate IDs, negative costs and non-finite time.");
            var snapshot = new SaveSlotData { runProgress = malformed };
            UIRunState.Apply(snapshot); UIRunState.Progress.memories[0].title = "Changed"; UIRunState.Complete("Map1");
            Check(snapshot.runProgress.completed.Count == 1 && snapshot.runProgress.memories[0].title == null,
                "Loaded progress is a deep copy; later gameplay cannot change the read snapshot.");
            UIRunState.NewRun();
            var authored = new MemoryEntry { id = "copy-check", title = "Original" };
            UIRunState.AddMemory(authored); authored.title = "Edited";
            Check(UIRunState.Progress.memories[0].title == "Original", "Collected memory content is independent of its authored source.");
            UIRunState.NewRun();
        }

        private IEnumerator ExtendedScenarios(UIRuntime runtime, UIPrototypeLab lab)
        {
            UISettingsData original = UISettings.Current.Copy();
            runtime.ShowSettings(); FindButton(runtime, "键鼠").onClick.Invoke();
            Array.Find(Top(runtime).GetComponentsInChildren<Button>(), b => b.name == "Bind " + UIInput.Forward).onClick.Invoke();
            Action<KeyCode> capture = (Action<KeyCode>)typeof(UIRuntime).GetField("rebind", Private).GetValue(runtime);
            Check(capture != null, "The movement binding opens the shared capture prompt.");
            runtime.Back(); capture(KeyCode.E);
            Check(Top(runtime).name == "绑定已被占用", "Conflicting keyboard bindings require an explicit swap confirmation.");
            FindButton(runtime, "确认").onClick.Invoke(); yield return null;
            Check(UIInput.Binding(UIInput.Forward) == KeyCode.E && UIInput.Binding(UIInput.Interact) == KeyCode.W,
                "Binding movement to an occupied key swaps the two actions.");
            yield return null; yield return null;
            GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
            ScrollRect scroll = selected.GetComponentInParent<ScrollRect>();
            Bounds selectedBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, selected.transform);
            Check(selectedBounds.min.y >= scroll.viewport.rect.yMin - 1 && selectedBounds.max.y <= scroll.viewport.rect.yMax + 1,
                "A rebuilt binding control remains selected and fully visible inside its scroll viewport.");
            yield return Capture(runtime, "controls-all-actions", 1600, 900);
            runtime.Back(); yield return null;
            Check(UIInput.Binding(UIInput.Forward) == original.keys[UIInput.Forward] && UIInput.Binding(UIInput.Interact) == original.keys[UIInput.Interact],
                "Leaving settings rolls back both actions in a key conflict swap.");
            UISettingsData custom = original.Copy(); custom.keys[UIInput.Charge] = KeyCode.Q; custom.keys[UIInput.Journal] = KeyCode.K;
            UISettings.Apply(custom);
            typeof(UIRuntime).GetMethod("RefreshHud", Private).Invoke(runtime, null);
            Check(UIInput.Binding(UIInput.Charge) == KeyCode.Q &&
                ((Text)typeof(UIRuntime).GetField("journalShortcut", Private).GetValue(runtime)).text.Contains("K"),
                "The charge action and journal shortcut respect the current bindings.");
            runtime.ShowHelp();
            string help = ""; foreach (Text label in Top(runtime).GetComponentsInChildren<Text>()) help += label.text;
            Check(help.Contains("Q") && help.Contains("K") && help.Contains("引爆"), "Tutorial help reflects custom keys and the detonation action.");
            runtime.Back(); yield return null; UISettings.Apply(original);

            var memoryObject = new GameObject("Saved memory trigger");
            MemoryTrigger trigger = memoryObject.AddComponent<MemoryTrigger>(); trigger.memory.id = "restored-fragment";
            UIRunState.AddMemory(new MemoryEntry { id = trigger.memory.id }); int count = UIRunState.Progress.memories.Count;
            trigger.Collect();
            Check(UIRunState.Progress.memories.Count == count && Top(runtime) == null, "A restored memory trigger does not collect its fragment twice.");
            trigger.dialogue = lab.dialogue;
            UIRunState.AddMemory(new MemoryEntry { id = lab.dialogue.id });
            trigger.Collect();
            Check(Top(runtime) != null && Top(runtime).name == lab.dialogue.title,
                "An archived dialogue with an unfinished choice remains available from its trigger.");
            runtime.Back(); yield return null; Destroy(memoryObject);

            runtime.ShowPrototypeCatalog(lab); int pageCount = ((System.Collections.Generic.List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(runtime)).Count;
            runtime.ShowPrototypeCatalog(lab);
            Check(((System.Collections.Generic.List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(runtime)).Count == pageCount,
                "Repeated preview hotkeys cannot stack duplicate catalog pages.");
            Check(FindButton(runtime, "失败与重试") != null && FindButton(runtime, "结局与回顾") != null,
                "The preview catalog exposes failure and ending modules.");
            runtime.Back(); yield return null;
        }

        private IEnumerator FormalScenarios()
        {
            for (int frame = 0; frame < 6; frame++) yield return null;
            UIRuntime runtime = UIRuntime.Instance;
            VerifyProgressAndBindings();
            Check(runtime.Config.defaultPlayer != null, "Formal maps resolve the configured player prefab.");
            for (int map = 0; map < 6; map++)
            {
                string scene = "Map" + map;
                runtime.LoadScene(scene);
                while (SceneManager.GetActiveScene().name != scene) yield return null;
                for (int frame = 0; frame < 8; frame++) yield return null;
                PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                Check(players.Length == 1, scene + " has exactly one active player.");
                PlayerController player = players[0]; BubbleLauncher launcher = player.GetComponent<BubbleLauncher>();
                Check(launcher != null && player.GetComponent<CharacterController>() != null && Camera.main != null,
                    scene + " has movement, launch and camera components.");
                Check(runtime.transform.Find("UI Canvas V2/Safe Area/Gameplay HUD").gameObject.activeSelf,
                    scene + " opens the gameplay HUD.");
                Transform goal = (Transform)typeof(UIRuntime).GetField("chapterGoal", Private).GetValue(runtime);
                Check(goal != null, scene + " resolves its configured or authored chapter goal.");
                int missing = 0;
                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        missing += UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                Check(missing == 0, scene + " imports with no missing script components.");
                Vector3 position = player.transform.position;
                runtime.ShowPause();
                Check(Time.timeScale == 0 && !player.enabled && launcher.InputBlocked && UIInput.Blocked,
                    scene + " pauses its real gameplay components.");
                yield return new WaitForSecondsRealtime(0.08f);
                Check(Vector3.Distance(player.transform.position, position) < 0.01f, scene + " remains stationary while paused.");
                runtime.Back();
                Check(UIInput.Blocked, scene + " blocks gameplay for the frame that closes the page.");
                yield return null;
                Check(player.enabled && !launcher.InputBlocked && !UIInput.Blocked, scene + " restores gameplay after closing pause.");
                // Diagnostic captures show actual geometry using a built-in shader in this DLL-only test project.
                yield return CaptureWorld(runtime, scene + "-entry");
                runtime.ShowPause();
                Check(SaveSystem.SaveCurrentState(0, player.transform), scene + " saves real player coordinates.");
                SaveSlotData saved = SaveSystem.LoadSlot(0);
                runtime.LoadScene(scene, saved);
                yield return null;
                while (SceneManager.GetActiveScene().name != scene || Top(runtime) != null) yield return null;
                for (int frame = 0; frame < 6; frame++) yield return null;
                player = FindFirstObjectByType<PlayerController>();
                Check(Vector3.Distance(player.transform.position, new Vector3(saved.posX, saved.posY, saved.posZ)) < 0.6f,
                    scene + " restores saved coordinates after reloading the real map.");
                ChapterGoalTrigger trigger = FindFirstObjectByType<ChapterGoalTrigger>();
                if (trigger != null)
                {
                    CharacterController controller = player.GetComponent<CharacterController>();
                    controller.enabled = false; player.transform.position = trigger.transform.position + Vector3.up * 0.6f; controller.enabled = true;
                    // Trigger callback is invoked explicitly so batch frame timing cannot skip a physics crossing.
                    trigger.SendMessage("OnTriggerEnter", controller);
                    Check(trigger.TryComplete() && Top(runtime) != null && !UIRunState.Progress.completed.Contains(scene),
                        scene + " presents a confirmation without prematurely completing the chapter.");
                    runtime.Back(); yield return null;
                    Check(!UIRunState.Progress.completed.Contains(scene), scene + " cancels goal confirmation and keeps exploration available.");
                    trigger.TryComplete(); FindButton(runtime, "确认").onClick.Invoke();
                }
                else
                {
                    tiaozhuan exit = FindFirstObjectByType<tiaozhuan>();
                    exit.SendMessage("OnTriggerEnter", player.GetComponent<CharacterController>());
                }
                Check(UIRunState.Progress.completed.Contains(scene) && SaveSystem.LoadSlot(UIRunState.AutoSlot).runProgress.completed.Contains(scene),
                    scene + " completes and persists progress in the automatic slot.");
                if (map == 0) yield return Capture(runtime, "formal-goal-complete", 1600, 900);
            }
        }

        private IEnumerator CaptureWorld(UIRuntime runtime, string name)
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var replacements = new System.Collections.Generic.List<Material>();
            var renderers = new System.Collections.Generic.List<Renderer>();
            var originals = new System.Collections.Generic.List<Material[]>();
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Material[] materials = renderer.sharedMaterials;
                renderers.Add(renderer); originals.Add((Material[])materials.Clone());
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i]; var diagnostic = new Material(shader);
                    diagnostic.color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                        source != null && source.HasProperty("_Color") ? source.color : Color.gray;
                    if (diagnostic.HasProperty("_Glossiness")) diagnostic.SetFloat("_Glossiness", 0);
                    materials[i] = diagnostic; replacements.Add(diagnostic);
                }
                renderer.sharedMaterials = materials;
            }
            Camera camera = Camera.main; Canvas canvas = runtime.GetComponentInChildren<Canvas>();
            var target = new RenderTexture(1600, 900, 24); target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 0.5f;
            yield return new WaitForSecondsRealtime(0.15f); Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var screenshot = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); screenshot.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, name + ".png"), screenshot.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Destroy(target); Destroy(screenshot);
            passed.Add("Rendered diagnostic world and HUD: " + name + ".");
            for (int i = 0; i < renderers.Count; i++) if (renderers[i] != null) renderers[i].sharedMaterials = originals[i];
            foreach (Material material in replacements) Destroy(material);
        }
    }
}
#endif
