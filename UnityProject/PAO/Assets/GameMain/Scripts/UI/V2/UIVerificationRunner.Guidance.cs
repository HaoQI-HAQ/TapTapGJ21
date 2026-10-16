#if UNITY_EDITOR
using System.Collections;
using PAO.BubbleTide;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private T GuideField<T>(UIRuntime runtime, string name) => (T)typeof(UIRuntime).GetField(name, Private).GetValue(runtime);
        private IEnumerator GuidanceScenarios(UIRuntime runtime)
        {
            UISettingsData original = UISettings.Current.Copy();
            Transform originalGoal = GuideField<Transform>(runtime, "chapterGoal");
            UISettingsData quiet = original.Copy(); quiet.detailedHints = false; quiet.keys[UIInput.Interact] = KeyCode.K;
            UISettings.Apply(quiet, false); UIInput.UseKeyboard(); yield return null;
            Check(!GuideField<Transform>(runtime, "objectivePanel").gameObject.activeSelf &&
                GuideField<Transform>(runtime, "abilityPanel") == null,
                "Default guidance has no ability information card and hides the persistent objective paragraph.");
            runtime.ShowHelp();
            string help = ""; foreach (Text label in Top(runtime).GetComponentsInChildren<Text>()) help += label.text;
            Check(help.Contains("当前路径") && help.Contains("浮粘泡泡") && help.Contains("K"),
                "Players can deliberately open Help to review route, ability behavior and current controls.");
            runtime.Back(); yield return null; yield return null;

            Camera camera = Camera.main;
            GameObject memory = GameObject.CreatePrimitive(PrimitiveType.Sphere); memory.name = "Verification memory cue";
            memory.GetComponent<SphereCollider>().enabled = false; memory.transform.localScale = Vector3.one * .45f;
            memory.transform.position = camera.transform.position + camera.transform.forward * 7 - camera.transform.right;
            MemoryTrigger trigger = memory.AddComponent<MemoryTrigger>();
            trigger.memory.id = "visual-guide-verification"; trigger.memory.title = "Verification memory";
            trigger.SendMessage("OnTriggerEnter", FindFirstObjectByType<PlayerController>().GetComponent<CharacterController>());
            GameObject goal = new GameObject("Verification destination");
            goal.transform.position = camera.transform.position + camera.transform.forward * 18 + camera.transform.right * 3 - Vector3.up * 2;
            typeof(UIRuntime).GetField("chapterGoal", Private).SetValue(runtime, goal.transform);
            yield return null; yield return null;
            Check(GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf &&
                GuideField<UIGuideGraphic>(runtime, "interactionSymbol").Symbol == UIGuideSymbol.Memory,
                "An available memory displays its shape and mapped input beside the object.");
            Text input = GuideField<RectTransform>(runtime, "interactionCue").GetComponentInChildren<Text>();
            Check(input.text == "K", "Visual interaction keycaps use the remapped action rather than a fixed E.");
            yield return CaptureWorld(runtime, "guidance-world-interaction");
            yield return Capture(runtime, "guidance-minimal-960", 960, 540);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Verification occlusion";
            wall.transform.position = (camera.transform.position + memory.transform.position + Vector3.up * .6f) * .5f;
            wall.transform.localScale = new Vector3(2, 3, .5f);
            Physics.SyncTransforms();
            yield return null; yield return null;
            Check(!GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf,
                "A solid wall suppresses interaction guidance instead of showing it through geometry.");
            Destroy(wall); yield return null; yield return null;
            goal.transform.position = camera.transform.position - camera.transform.forward * 20;
            yield return null; yield return null;
            RectTransform marker = GuideField<RectTransform>(runtime, "goalCue");
            Check(marker.gameObject.activeSelf && GuideField<UIGuideGraphic>(runtime, "goalSymbol").Symbol == UIGuideSymbol.Arrow &&
                Mathf.Abs(marker.anchoredPosition.x) <= marker.parent.GetComponent<RectTransform>().rect.width * .5f - 90,
                "A destination behind the camera becomes a direction arrow inside the safe area.");
            goal.transform.position = camera.transform.position + camera.transform.right * 80 - camera.transform.up * 32 + camera.transform.forward * 2;
            yield return null; yield return null;
            Check(!HudRect(runtime, marker).Overlaps(HudRect(runtime, GuideField<RectTransform>(runtime, "abilityCluster"))),
                "An offscreen destination cue avoids the actual bounds of the three ability controls.");
            goal.transform.position = camera.transform.position - camera.transform.forward * 20;

            BubbleLauncher launcher = FindFirstObjectByType<BubbleLauncher>();
            UIInput.VerificationAxis = axis => 0;
            UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
            yield return new WaitForSecondsRealtime(.25f);
            Check(launcher.IsCharging && !GuideField<RectTransform>(runtime, "reticleCue").gameObject.activeSelf &&
                Mathf.Abs(GuideField<UIAbilityMotion>(runtime, "abilityMotion").ChargeProgress - launcher.ChargeProgress) < .05f,
                "The selected ability ring shows actual charge without adding another badge or card.");
            yield return CaptureWorld(runtime, "guidance-world-charge");
            runtime.ShowPause(); yield return null;
            Check(!GuideField<RectTransform>(runtime, "visualHud").gameObject.activeInHierarchy,
                "Pause hides destination, charge and interaction graphics together with the HUD.");
            runtime.Back(); UIInput.VerificationHeld = control => false; yield return null;
            launcher.SendMessage("OnDisable"); UIInput.VerificationHeld = null; UIInput.VerificationAxis = null; UIInput.UseKeyboard();

            quiet.detailedHints = true; UISettings.Apply(quiet, false); yield return null;
            Check(GuideField<Transform>(runtime, "objectivePanel").gameObject.activeSelf &&
                GuideField<Transform>(runtime, "abilityPanel") == null && !GuideField<RectTransform>(runtime, "visualHud").gameObject.activeSelf,
                "Detailed text hints remain available as an explicit accessibility preference.");
            yield return Capture(runtime, "guidance-detailed-opt-in", 1600, 900);
            quiet.tutorials = true; UISettings.Apply(quiet, false);
            runtime.ShowTutorial("guidance-mode-transition", "验证教学", "这段教学不应在关闭文字提示后重新出现。");
            yield return null;
            quiet.detailedHints = false; quiet.reducedMotion = true; quiet.highContrast = true; UISettings.Apply(quiet, false);
            yield return null;
            Check(GuideField<System.Collections.Generic.Queue<string>>(runtime, "tutorialNotices").Count == 0 &&
                (!GuideField<bool>(runtime, "noticeIsTutorial") || GuideField<float>(runtime, "noticeRemaining") <= 0),
                "Switching to graphical guidance clears active and queued teaching paragraphs.");
            runtime.PulseVisualCue(UIGuideSymbol.Move, UIInput.Forward, 1); yield return null;
            Check(GuideField<RectTransform>(runtime, "tutorialCue").localScale == Vector3.one,
                "Reduced motion retains static shape and key cues without scaling or shaking.");
            yield return CaptureWorld(runtime, "guidance-world-reduced-motion");
            quiet.tutorials = false; UISettings.Apply(quiet, false);
            runtime.PulseVisualCue(UIGuideSymbol.Check); yield return null;
            Check(GuideField<RectTransform>(runtime, "tutorialCue").gameObject.activeSelf,
                "Disabling tutorials keeps short collection, build and restore feedback visible.");
            float pulseTime = GuideField<float>(runtime, "tutorialRemaining");
            runtime.ShowPause(); yield return new WaitForSecondsRealtime(.2f);
            Check(Mathf.Abs(GuideField<float>(runtime, "tutorialRemaining") - pulseTime) < .06f,
                "Graphical feedback waits while a modal hides the gameplay HUD.");
            runtime.Back(); yield return null;
            trigger.SendMessage("OnTriggerExit", FindFirstObjectByType<PlayerController>().GetComponent<CharacterController>());
            yield return null; yield return null;
            Check(!GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf,
                "Leaving an interaction area immediately clears the object's cue.");
            trigger.SendMessage("OnTriggerEnter", FindFirstObjectByType<PlayerController>().GetComponent<CharacterController>());
            yield return null; yield return null;
            trigger.enabled = false; yield return null;
            Check(!GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf,
                "Disabling an interaction removes its marker without leaving a stale request.");
            trigger.enabled = true; yield return null; yield return null;
            Check(!GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf,
                "Re-enabling an object does not reuse an old nearby flag.");
            runtime.RequestInteractionCue(trigger, camera.transform.position - camera.transform.forward * 5,
                UIGuideSymbol.Exit, UIInput.Ride, -1, memory.transform, true);
            runtime.RequestInteractionCue(goal.transform, camera.transform.position + camera.transform.forward * 7,
                UIGuideSymbol.Memory, UIInput.Interact);
            yield return null;
            Check(GuideField<RectTransform>(runtime, "interactionCue").gameObject.activeSelf &&
                GuideField<UIGuideGraphic>(runtime, "interactionSymbol").Symbol == UIGuideSymbol.Exit,
                "A pinned ride-exit cue stays reachable outside the camera view and takes priority over nearby objects.");
            runtime.RemoveInteractionCue(trigger);
            runtime.RemoveInteractionCue(goal.transform);
            BubbleTideDirector originalTide = GuideField<BubbleTideDirector>(runtime, "tide");
            GameObject tideObject = new GameObject("Verification tide clock");
            BubbleTideDirector clock = tideObject.AddComponent<BubbleTideDirector>(); clock.enabled = false;
            typeof(UIRuntime).GetField("tide", Private).SetValue(runtime, clock);
            yield return null;
            Check(!GuideField<RectTransform>(runtime, "tideCue").gameObject.activeSelf,
                "An unbaked tide does not display a misleading zero-second countdown.");
            typeof(BubbleTideDirector).GetProperty("Deadline").SetValue(clock, 120f);
            typeof(BubbleTideDirector).GetField("m_Elapsed", Private).SetValue(clock, 30f);
            typeof(BubbleTideDirector).GetProperty("State").SetValue(clock, BubbleTideState.Flooding);
            yield return null;
            Check(GuideField<Text>(runtime, "tideTime").text == "90 s" &&
                Mathf.Abs(GuideField<UIChargeArc>(runtime, "tideArc").Progress - .75f) < .01f,
                "The tide ring reads its real deadline even when baking finishes after HUD creation.");
            typeof(BubbleTideDirector).GetProperty("State").SetValue(clock, BubbleTideState.GoalFlooded);
            typeof(BubbleTideDirector).GetField("m_GraceRemaining", Private).SetValue(clock, 7f);
            yield return null;
            typeof(BubbleTideDirector).GetField("m_GraceRemaining", Private).SetValue(clock, 3f);
            yield return null;
            Check(GuideField<Text>(runtime, "tideTime").text == "3 s" &&
                Mathf.Abs(GuideField<UIChargeArc>(runtime, "tideArc").Progress - 3f / 7) < .01f,
                "After the goal floods, the clock shows the remaining grace period rather than zero.");
            yield return CaptureWorld(runtime, "guidance-world-tide-grace");
            typeof(UIRuntime).GetField("tide", Private).SetValue(runtime, originalTide); Destroy(tideObject);
            Destroy(memory); Destroy(goal); typeof(UIRuntime).GetField("chapterGoal", Private).SetValue(runtime, originalGoal);
            UISettings.Apply(original, false); yield return null;
        }
    }
}
#endif
