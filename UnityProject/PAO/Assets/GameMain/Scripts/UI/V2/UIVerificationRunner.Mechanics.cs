#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private IEnumerator MechanicsScenarios()
        {
            UISettings.Apply(new UISettingsData { fullscreen = false, tutorials = false });
            for (int i = 0; i < 8; i++) yield return null;
            UIRuntime runtime = UIRuntime.Instance;
            PlayerController player = FindFirstObjectByType<PlayerController>();
            Check(runtime != null && runtime.IsGameplay && SceneManager.GetActiveScene().name == "cs", "cs bootstraps the gameplay UI.");
            Check(FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length == 1, "The repaired scene contains exactly one player.");
            var missing = new List<string>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0) missing.Add(child.name);
            Check(missing.Count == 0, "The scene has no missing scripts. " + string.Join(", ", missing));
            Check(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1, "Only one EventSystem is active.");
            BubbleLauncher launcher = player.GetComponent<BubbleLauncher>();
            BubbleResourceManager energy = player.GetComponent<BubbleResourceManager>();
            BubbleTypeSwitcher types = player.GetComponent<BubbleTypeSwitcher>();
            Check(launcher != null && energy != null && energy.Current == energy.Max && player.GetComponent<JellyFaceDirector>() != null &&
                player.GetComponentInChildren<JellyFaceSwitcher>() != null && Camera.main != null, "The player has resources, expressions and a main camera.");
            player.enabled = false; launcher.InputBlocked = true;
            Transform panel = runtime.transform.Find("UI Canvas V2/Safe Area/Gameplay HUD/Bubble resource");
            Check(panel != null && panel.gameObject.activeInHierarchy && !UISettings.Current.detailedHints, "Resource feedback remains visible with minimal hints.");
            UISettingsData detailedCosts = UISettings.Current.Copy(); detailedCosts.detailedHints = true; UISettings.Apply(detailedCosts);
            foreach (BubbleType type in new[] { BubbleType.Sticky, BubbleType.Bouncy, BubbleType.Bomb })
            {
                types.SetType(type);
                BubbleSettings settings = launcher.SettingsFor(type);
                Check(settings.prefab != null && settings.minCost > 0 && settings.maxCost >= settings.minCost, "Scene bubble template and cost resolve: " + type);
                typeof(UIRuntime).GetMethod("RefreshHud", Private).Invoke(runtime, null);
                Check(GuideField<RectTransform>(runtime, "abilityCluster").Find("Current ability") == null &&
                    panel.GetComponentsInChildren<Text>(true).Length == 1 &&
                    GuideField<Text>(runtime, "resourceAmount").text == energy.Current + " / " + energy.Max,
                    "Detailed mode keeps the ability card absent and energy focused on remaining quantity: " + type);
            }
            detailedCosts.detailedHints = false; UISettings.Apply(detailedCosts);
            types.SetType(BubbleType.Sticky);
            yield return VerifyNarrationLifecycle(runtime);
            yield return Capture(runtime, "cs-resource-full-1600x900", 1600, 900);
            VerifyResourceTextFits(panel);

            energy.SetCurrent(0);
            typeof(BubbleLauncher).GetMethod("BeginCharge", Private).Invoke(launcher, null);
            Check(!launcher.IsCharging && !types.IsLocked && energy.Current == 0, "Insufficient energy prevents charging without locking input or going negative.");
            yield return Capture(runtime, "cs-resource-empty-960x540", 960, 540);
            VerifyResourceTextFits(panel);

            energy.SetCurrent(3);
            BubbleLoadout testLoadout = Instantiate(launcher.Loadout);
            testLoadout.maxChargeTime = .2f; testLoadout.holdThreshold = 0;
            typeof(BubbleLauncher).GetField("m_Loadout", Private).SetValue(launcher, testLoadout);
            typeof(BubbleLauncher).GetMethod("BeginCharge", Private).Invoke(launcher, null);
            Check(launcher.IsCharging && types.IsLocked && energy.Current == 1, "Starting a sticky bubble pays its base cost and locks type switching.");
            types.SetType(BubbleType.Bomb);
            Check(types.Current == BubbleType.Sticky, "Ability selection respects the charge lock.");
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                typeof(BubbleLauncher).GetMethod("UpdateCharge", Private).Invoke(launcher, null);
            }
            Check(launcher.IsResourceLimited && launcher.PaidCost + energy.Current == 3 && energy.Current >= 0,
                "Growing bubbles stop at the available budget and keep consistent paid cost.");
            yield return Capture(runtime, "cs-charge-limited-1600x900", 1600, 900);
            VerifyResourceTextFits(panel);
            typeof(BubbleLauncher).GetMethod("Release", Private).Invoke(launcher, null);
            Check(!launcher.IsCharging && !types.IsLocked && launcher.HasHeldBubble, "A limited bubble can still be released and held ready.");
            int beforeLaunch = energy.Current;
            yield return Capture(runtime, "cs-bubble-held-960x540", 960, 540);
            VerifyResourceTextFits(panel);
            launcher.LaunchHeldBubble();
            Check(!launcher.HasHeldBubble && energy.Current == beforeLaunch, "Launching a held bubble does not charge twice.");

            var terrainObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            terrainObject.name = "Verification terrain";
            terrainObject.transform.position = new Vector3(2000, 2000, 2000);
            TerrainBubble terrain = terrainObject.AddComponent<TerrainBubble>();
            energy.SetCurrent(10);
            Check(!terrain.Absorb(2) && terrain.CurrentFill == 2, "Terrain stores partial fill before it is full.");
            Vector3 cameraPosition = Camera.main.transform.position;
            Quaternion cameraRotation = Camera.main.transform.rotation;
            Camera.main.transform.position = terrainObject.transform.position + Vector3.back * 4;
            Camera.main.transform.LookAt(terrainObject.transform);
            Physics.SyncTransforms();
            typeof(UIRuntime).GetMethod("RefreshHud", Private).Invoke(runtime, null);
            Check(GuideField<RectTransform>(runtime, "abilityCluster").Find("Current ability") == null && terrain.CurrentFill == 2,
                "Aiming at partly filled terrain keeps the removed information card absent.");
            yield return Capture(runtime, "cs-terrain-fill-1600x900", 1600, 900);
            Camera.main.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            terrain.Absorb(terrain.Capacity);
            Check(energy.Current == 10 + terrain.RewardAmount, "Filling terrain rewards the actual player resource pool.");
            terrain.Break(TerrainBubble.BreakCause.Bouncy);
            Check(energy.Current == 10 + terrain.RewardAmount, "Repeated terrain destruction does not reward twice.");
            Destroy(terrainObject);
            Bubble[] bubbles = FindObjectsByType<Bubble>(FindObjectsSortMode.None);
            Bubble paid = System.Array.Find(bubbles, item => item.PaidCost > 0);
            Check(paid != null, "Launched bubbles retain their paid cost for recovery.");
            int amount = paid.PaidCost, beforeRefund = energy.Current;
            paid.RefundCostToPlayer(); paid.RefundCostToPlayer();
            Check(energy.Current == beforeRefund + amount && paid.PaidCost == 0, "Sticky recovery refunds the paid amount exactly once.");
            yield return Capture(runtime, "cs-recovered-1600x900", 1600, 900);

            player.enabled = true; launcher.InputBlocked = false;
            runtime.ShowPause();
            Check(Time.timeScale == 0 && !panel.gameObject.activeInHierarchy && launcher.InputBlocked, "Pause hides resource HUD and blocks charging.");
            yield return Capture(runtime, "cs-test-tools-960x540", 960, 540);
            FindButton(runtime, "低资源（10%）").onClick.Invoke();
            Check(!runtime.IsBlockingGameplay && energy.Current == energy.Max / 10 && player.enabled, "Low energy test action applies and resumes gameplay.");
            Check(GuideField<Image>(runtime, "resourceFill").color == UIDesignPalette.Coral &&
                GuideField<float>(runtime, "resourceFeedbackUntil") <= Time.unscaledTime,
                "Reducing energy clears recovery feedback from the previous resource increase.");
            yield return Capture(runtime, "cs-resource-low-960x540", 960, 540);
            runtime.ShowPause(); FindButton(runtime, "补满泡泡值").onClick.Invoke();
            Check(energy.Current == energy.Max && !runtime.IsBlockingGameplay, "Refill test action restores the pool and resumes.");
            player.Teleport(new Vector3(20, 8, 20), Quaternion.identity);
            runtime.ShowPause(); FindButton(runtime, "回到出生点").onClick.Invoke();
            Check(Vector3.Distance(player.transform.position, new Vector3(.55f, .59f, -3.37f)) < .001f && !runtime.IsBlockingGameplay,
                "Return to spawn uses the repaired scene's original spawn point.");
            player.enabled = false; launcher.InputBlocked = true;

            UISettingsData detailed = UISettings.Current.Copy(); detailed.detailedHints = true; detailed.textScale = 1.3f;
            UISettings.Apply(detailed);
            yield return Capture(runtime, "cs-detailed-large-text-960x540", 960, 540);
            VerifyResourceTextFits(panel);
            UISettingsData english = UISettings.Current.Copy(); english.language = 1; english.detailedHints = false;
            UISettings.Apply(english);
            yield return Capture(runtime, "cs-english-large-text-960x540", 960, 540);
            VerifyResourceTextFits(panel);
            runtime.ShowSubtitle("水流", "字幕固定在底部中央，与提示自动错开。");
            runtime.Toast("破坏地形或回收泡泡可以恢复泡泡值。");
            yield return Capture(runtime, "cs-hud-bottom-messages-960x540", 960, 540);
            yield return null; yield return null; Canvas.ForceUpdateCanvases();
            VerifyHudPlacement(runtime);
            UISettingsData minimal = UISettings.Current.Copy(); minimal.language = 0; minimal.textScale = 1; UISettings.Apply(minimal);
            yield return CaptureWorld(runtime, "cs-hud-world-annotated-layout");
            yield return null; yield return null; Canvas.ForceUpdateCanvases();
            VerifyHudPlacement(runtime);
            runtime.ShowHelp();
            Check(Top(runtime).GetComponentsInChildren<Text>().Length > 10, "The field guide opens with the added mechanics lessons.");
            runtime.Back();
            runtime.LoadScene("cs");
            for (int i = 0; i < 20; i++) yield return null;
            energy = FindFirstObjectByType<BubbleResourceManager>();
            Check(energy != null && energy.Current == energy.Max && FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length == 1,
                "Reloading the test scene restores a single fully provisioned player.");
            runtime.LoadScene("Map5");
            while (SceneManager.GetActiveScene().name != "Map5") yield return null;
            for (int i = 0; i < 8; i++) yield return null;
            player = FindFirstObjectByType<PlayerController>();
            Check(player != null && player.GetComponents<BubbleResourceManager>().Length == 1 && player.GetComponents<JellyFaceDirector>().Length == 1,
                "Map5 inherits one resource manager and one face director with its original defaults.");
            Check(!player.GetComponent<BubbleResourceHud>().enabled &&
                runtime.transform.Find("UI Canvas V2/Safe Area/Gameplay HUD/Bubble resource").gameObject.activeInHierarchy,
                "Map5 uses the new HUD while suppressing its legacy resource display.");
        }

        private IEnumerator VerifyNarrationLifecycle(UIRuntime runtime)
        {
            RectTransform popup = GuideField<RectTransform>(runtime, "subtitleRoot");
            Check(popup != null && popup.gameObject.activeInHierarchy && float.IsPositiveInfinity(GuideField<float>(runtime, "subtitleUntil")),
                "cs starts with a persistent narration preview rather than a resource recovery notice.");
            runtime.ShowNarration("旁白", "这是一段短暂的旁白。", .12f);
            yield return null;
            Check(popup.gameObject.activeInHierarchy, "A narration event opens the bottom popup.");
            yield return new WaitForSecondsRealtime(.2f);
            Check(!popup.gameObject.activeInHierarchy, "Timed narration hides automatically when it ends.");
            runtime.ShowNarration("旁白", "测试场景常驻旁白，正式关卡只在触发时显示。", -1);
            runtime.ShowPause(); yield return null;
            Check(!popup.gameObject.activeInHierarchy, "Pause hides the narration preview with gameplay UI.");
            runtime.Back(); yield return null; yield return null;
            Check(popup.gameObject.activeInHierarchy, "The persistent preview returns after closing pause.");
            UISettingsData disabled = UISettings.Current.Copy(); disabled.subtitles = false; UISettings.Apply(disabled);
            yield return null;
            Check(!popup.gameObject.activeInHierarchy, "The subtitle preference also hides the narration popup.");
            disabled.subtitles = true; UISettings.Apply(disabled); yield return null;
            runtime.HideNarration(); yield return null;
            Check(!popup.gameObject.activeInHierarchy, "Narration can be closed explicitly without leaving an empty panel.");
            runtime.ShowNarration("旁白", "水流轻轻涌动，泡泡会带你去往新的水域。", -1);
        }

        private void VerifyResourceTextFits(Transform panel)
        {
            UIRuntime runtime = UIRuntime.Instance;
            foreach (Transform area in new[] { panel, GuideField<RectTransform>(runtime, "subtitleRoot") })
                if (area != null) foreach (Text text in area.GetComponentsInChildren<Text>())
                    Check(text.rectTransform.rect.height >= text.preferredHeight - 1,
                        "HUD text fits: " + text.text.Replace('\n', ' '));
        }

        private static Rect HudRect(UIRuntime runtime, RectTransform rect)
        {
            RectTransform hud = GuideFieldStatic<RectTransform>(runtime, "hud");
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector3 min = hud.InverseTransformPoint(corners[0]), max = hud.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        private static T GuideFieldStatic<T>(UIRuntime runtime, string name) => (T)typeof(UIRuntime).GetField(name, Private).GetValue(runtime);

        private void VerifyHudPlacement(UIRuntime runtime)
        {
            RectTransform energy = GuideField<RectTransform>(runtime, "resourcePanel");
            RectTransform cluster = GuideField<RectTransform>(runtime, "abilityCluster");
            RectTransform messages = GuideField<RectTransform>(runtime, "hudMessages");
            Check(energy.anchorMin == new Vector2(0, 1) && energy.GetComponentsInChildren<Text>().Length == 1,
                "Upper-left energy feedback contains only the quantity value and meter.");
            Check(cluster.Find("Current ability") == null && !GuideField<RectTransform>(runtime, "reticleCue").gameObject.activeSelf,
                "Three ability buttons remain without the removed card or duplicate charge badge.");
            Check(messages.anchorMin.y == 0 && !HudRect(runtime, messages).Overlaps(HudRect(runtime, cluster)),
                "Bottom messages reserve space for the ability cluster at large text sizes. Messages=" + HudRect(runtime, messages) + ", abilities=" + HudRect(runtime, cluster));
            RectTransform subtitle = GuideField<RectTransform>(runtime, "subtitleRoot"), notice = GuideField<RectTransform>(runtime, "noticeRoot");
            Check(subtitle.IsChildOf(messages) && notice.IsChildOf(messages) && !HudRect(runtime, subtitle).Overlaps(HudRect(runtime, notice)),
                "Narration and temporary notices share the bottom lane without overlapping.");
        }
    }
}
#endif
