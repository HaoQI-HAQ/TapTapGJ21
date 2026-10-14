#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private T BubbleField<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);

        private IEnumerator BubbleScenarios()
        {
            for (int frame = 0; frame < 8; frame++) yield return null;
            UIRuntime runtime = UIRuntime.Instance;
            UISettings.Apply(new UISettingsData { detailedHints = false, tutorials = false }, false);
            UIInput.VerificationAxis = axis => 0;
            UIInput.VerificationHeld = control => false;
            BubbleLoadout shared = Resources.Load<BubbleLoadout>("CSBubbleLoadout");
            Check(shared != null && shared.maxChargeTime == 2 && shared.holdThreshold == 1,
                "CS loadout resolves with a two-second charge and one-metre hold threshold.");
            string[] prefabs = { "nian_p", "tan_P", "bao_P" };
            float[] minSizes = { .2f, .3f, .2f }, maxSizes = { 1.6f, 1.1f, .6f };
            for (int type = 0; type < 3; type++)
            {
                BubbleSettings settings = shared.SettingsFor((BubbleType)type);
                Check(settings.prefab != null && settings.prefab.name == prefabs[type] &&
                    Mathf.Approximately(settings.minSize, minSizes[type]) && Mathf.Approximately(settings.maxSize, maxSizes[type]) &&
                    settings.minCost == 2 && settings.maxCost == 12, "CS type " + type + " preserves its reference prefab, size range and cost.");
            }
            Check(shared.sticky.lifeTime == 20 && shared.sticky.heldLiftSpeed == 1 && shared.bouncy.riderLaunchDuration == .7f &&
                shared.bomb.blastRadius == .3f && shared.bomb.absorbedBlastMultiplier == 9, "CS behavior tuning is retained in the shared loadout.");

            for (int map = -1; map < 6; map++)
            {
                string scene = map < 0 ? "cs" : "Map" + map;
                if (SceneManager.GetActiveScene().name != scene)
                {
                    runtime.LoadScene(scene);
                    while (SceneManager.GetActiveScene().name != scene) yield return null;
                    for (int frame = 0; frame < 8; frame++) yield return null;
                }
                PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
                Check(players.Length == 1, scene + " has exactly one player.");
                GameObject player = players[0].gameObject;
                BubbleLauncher launcher = player.GetComponent<BubbleLauncher>();
                BubbleResourceManager resource = player.GetComponent<BubbleResourceManager>();
                BubbleTypeSwitcher switcher = player.GetComponent<BubbleTypeSwitcher>();
                Check(launcher.Loadout == shared && player.GetComponents<BubbleResourceManager>().Length == 1 &&
                    player.GetComponents<JellyFaceDirector>().Length == 1, scene + " uses the same loadout and one resource/face manager.");
                int missing = 0;
                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                        if (PrefabUtility.IsPrefabAssetMissing(child.gameObject)) missing++;
                    }
                Check(missing == 0, scene + " has no missing scripts or prefab instances.");
                RectTransform panel = BubbleField<RectTransform>(runtime, "resourcePanel");
                Check(panel != null && panel.gameObject.activeInHierarchy && resource.Current == 100,
                    scene + " shows the full resource pool with detailed hints disabled.");
                if (scene == "cs")
                    Check(Vector3.Distance(player.transform.position, new Vector3(.55f, .59f, -3.37f)) < .6f,
                        "CS retains its authored player spawn after replacing the missing prefab.");

                for (int type = 0; type < 3; type++)
                {
                    switcher.SetType((BubbleType)type); resource.SetCurrent(100);
                    UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
                    yield return new WaitForSecondsRealtime(.25f);
                    GameObject bubble = BubbleField<GameObject>(launcher, "m_CurrentBubble");
                    Check(bubble != null && bubble.GetComponent<Bubble>().Type == (BubbleType)type &&
                        bubble.GetComponent<Renderer>().sharedMaterial == shared.SettingsFor((BubbleType)type).prefab.GetComponent<Renderer>().sharedMaterial,
                        scene + " actually creates the reference prefab for type " + type + ".");
                    Check(launcher.IsCharging && switcher.IsLocked && 100 - resource.Current == launcher.PaidCost &&
                        launcher.PaidCost == launcher.CostFor((BubbleType)type, launcher.ChargeProgress),
                        scene + " incrementally charges the displayed cost for type " + type + ".");
                    UIInput.VerificationHeld = control => false;
                    yield return null; yield return null;
                    Check(!launcher.IsCharging && !switcher.IsLocked, scene + " releases and unlocks type " + type + ".");
                    if (bubble != null) Destroy(bubble);
                    yield return null;
                }
                typeof(UIRuntime).GetMethod("RefreshHud", Private).Invoke(runtime, null);
                Check(panel.GetComponent<UILowPolyImage>() != null && panel.rect.width == 300 &&
                    !BubbleField<Text>(runtime, "resourceCost").gameObject.activeSelf &&
                    BubbleField<RectTransform>(runtime, "reticleCue").IsChildOf(panel),
                    scene + " keeps compact lowpoly energy feedback and charging badge in the lower-left panel.");
                if (map == -1) yield return VerifyBubbleResources(runtime, launcher, resource, switcher);
                resource.SetCurrent(100);
                yield return Capture(runtime, scene + "-resource-hud", 1600, 900);
                VerifyResourceTextFits(panel);
            }
            UIInput.VerificationHeld = null; UIInput.VerificationAxis = null; UIInput.UseKeyboard();
        }

        private IEnumerator VerifyBubbleResources(UIRuntime runtime, BubbleLauncher launcher, BubbleResourceManager resource, BubbleTypeSwitcher switcher)
        {
            switcher.SetType(BubbleType.Sticky); resource.SetCurrent(3);
            int notices = BubbleField<System.Collections.Generic.Queue<string>>(runtime, "notices").Count;
            UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
            yield return new WaitForSecondsRealtime(.6f);
            Check(launcher.ChargeLimited && resource.Current == 0 && launcher.PaidCost == 3,
                "Insufficient resource stops growth after spending only the affordable cost.");
            float progress = launcher.ChargeProgress;
            Vector3 scale = BubbleField<GameObject>(launcher, "m_CurrentBubble").transform.localScale;
            yield return new WaitForSecondsRealtime(.2f);
            Check(launcher.ChargeProgress == progress && BubbleField<GameObject>(launcher, "m_CurrentBubble").transform.localScale == scale,
                "A depleted charge retains its size and progress.");
            switcher.SetType(BubbleType.Bomb);
            Check(switcher.Current == BubbleType.Sticky && BubbleField<Text>(runtime, "resourceStatus").text.Contains("停止变大") &&
                BubbleField<System.Collections.Generic.Queue<string>>(runtime, "notices").Count == notices,
                "Type switching stays locked and repeated insufficient events use one inline hint.");
            yield return CaptureWorld(runtime, "cs-lowpoly-world-charging");
            yield return Capture(runtime, "cs-resource-insufficient-960", 960, 540);
            VerifyResourceTextFits(BubbleField<RectTransform>(runtime, "resourcePanel"));
            resource.Refund(5); yield return new WaitForSecondsRealtime(.2f);
            Check(!launcher.ChargeLimited && launcher.ChargeProgress > progress, "Recovered resource resumes the existing charge.");
            runtime.ShowPause(); float pausedProgress = launcher.ChargeProgress; int pausedValue = resource.Current;
            yield return new WaitForSecondsRealtime(.2f);
            Check(Time.timeScale == 0 && launcher.ChargeProgress == pausedProgress && resource.Current == pausedValue &&
                !BubbleField<RectTransform>(runtime, "resourcePanel").gameObject.activeInHierarchy,
                "Pause stops charge spending and hides the resource HUD.");
            runtime.Back(); UIInput.VerificationHeld = control => false;
            yield return null; yield return null;
            launcher.SendMessage("OnDisable");
            resource.SetCurrent(0);
            UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
            yield return new WaitForSecondsRealtime(.15f);
            Check(!launcher.IsCharging && resource.Current == 0 && BubbleField<Text>(runtime, "resourceStatus").text.Contains("还差 2"),
                "An empty pool refuses a new bubble and shows the exact shortfall.");
            UIInput.VerificationHeld = control => false; yield return null; yield return null;

            resource.SetCurrent(20);
            UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
            yield return new WaitForSecondsRealtime(2.2f);
            UIInput.VerificationHeld = control => false; yield return null; yield return null;
            Check(launcher.HasHeldBubble && launcher.PaidCost == 12 && resource.Current == 8,
                "A fully charged sticky bubble remains at the muzzle with its paid cost.");
            switcher.SetType(BubbleType.Bomb);
            Check(launcher.DisplayType == BubbleType.Sticky && BubbleField<Text>(runtime, "resourceCost").text.Contains("浮粘"),
                "A held bubble retains its original type in the cost preview after switching.");
            GameObject held = BubbleField<GameObject>(launcher, "m_HeldBubble");
            launcher.LaunchHeldBubble();
            Check(!launcher.HasHeldBubble && resource.Current == 8 && held.GetComponent<Bubble>().Type == BubbleType.Sticky,
                "Launching the held bubble uses its original settings without charging twice.");
            Destroy(held); yield return null;

            resource.SetCurrent(40); yield return new WaitForSecondsRealtime(.15f);
            GameObject terrain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            TerrainBubble target = terrain.AddComponent<TerrainBubble>();
            terrain.transform.position = launcher.transform.position + Vector3.right * 20;
            target.Break(TerrainBubble.BreakCause.Bomb); yield return null;
            Check(resource.Current == 45 && BubbleField<Text>(runtime, "resourceStatus").text.Contains("+5"),
                "Breaking actual bubble terrain refunds its reward and updates the HUD.");
            yield return Capture(runtime, "cs-resource-recovered", 1600, 900);
            UISettingsData english = new UISettingsData { language = 1, textScale = 1.3f, uiScale = 1.2f, detailedHints = true, tutorials = false };
            UISettings.Apply(english, false);
            yield return Capture(runtime, "cs-resource-english-large-960", 960, 540);
            VerifyResourceTextFits(BubbleField<RectTransform>(runtime, "resourcePanel"));
            UISettings.Apply(new UISettingsData { tutorials = false }, false);
        }
    }
}
#endif
