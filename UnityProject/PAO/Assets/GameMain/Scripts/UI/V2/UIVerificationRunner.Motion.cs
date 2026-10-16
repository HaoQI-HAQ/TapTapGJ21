#if UNITY_EDITOR
using System.Collections;
using System.IO;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private IEnumerator MotionScenarios(UIRuntime runtime)
        {
            UISettingsData original = UISettings.Current.Copy();
            UISettingsData animated = original.Copy(); animated.reducedMotion = false; animated.highContrast = false;
            animated.tutorials = false; UISettings.Apply(animated, false);
            BubbleTypeSwitcher switcher = FindFirstObjectByType<BubbleTypeSwitcher>();
            UIAbilityMotion motion = runtime.GetComponentInChildren<UIAbilityMotion>(true);
            Check(motion != null && motion.MovingOrbitCount == 1, "Exactly one selected ability owns a moving orbit.");
            for (int i = 0; i < 60; i++) switcher.SetType((BubbleType)(i % 3));
            switcher.SetType(BubbleType.Sticky);
            yield return new WaitForSecondsRealtime(.31f);
            int enabled = 0;
            foreach (Transform child in motion.transform)
            {
                if (child.GetComponent<Button>() == null) continue;
                Transform selection = child.Find("Selection ring");
                Check(selection != null && selection.GetComponent<Image>() != null,
                    "Ability control " + child.name + " retains its selection boundary.");
                if (selection.GetComponent<Image>().enabled) enabled++;
            }
            Check(motion.Selected == (int)switcher.Current && enabled == 1 && motion.MovingOrbitCount == 1,
                "Sixty rapid ability changes settle to the actual ability with one selection and one orbit.");
            RectTransform chosen = motion.transform.Find("Ability 0") as RectTransform;
            Check(Mathf.Abs(chosen.localScale.x - 1.03f) < .001f,
                "The selected scale reaches its final state within 300 ms and is not overwritten by HUD polling.");
            UIPearlGraphic orbit = chosen.Find("Orbit arcs").GetComponent<UIPearlGraphic>();
            float phase = orbit.Phase; float timeScale = Time.timeScale;
            Time.timeScale = 0; yield return new WaitForSecondsRealtime(.12f);
            Check(Mathf.Abs(orbit.Phase - phase) > .1f, "Independent UI time advances the orbit when game time is frozen.");
            Time.timeScale = timeScale;
            runtime.ShowPause(); yield return null;
            Check(motion.MovingOrbitCount == 0, "Hiding the gameplay HUD kills its ambient orbit immediately.");
            Button back = FindButton(runtime, "继续探索");
            UIButtonFeedback feedback = back.GetComponent<UIButtonFeedback>();
            UIFluidGradient gradient = back.GetComponent<UIFluidGradient>();
            feedback.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.14f);
            Check(gradient.Warmth > .95f && Time.timeScale == 0, "Buttons show a clear focus tint while the pause menu freezes gameplay.");
            runtime.Back(); yield return new WaitForSecondsRealtime(.14f);
            Check(motion.MovingOrbitCount == 1, "Returning to gameplay restarts exactly one orbit.");

            Canvas canvas = runtime.GetComponentInChildren<Canvas>();
            Camera camera = new GameObject("Motion capture camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true; camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = runtime.Config.ink;
            var target = new RenderTexture(960, 540, 24); target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Directory.CreateDirectory(Path.Combine(output, "motion-frames"));
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            for (int frame = 0; frame < 60; frame++)
            {
                if (frame % 16 == 0) switcher.SetType((BubbleType)((frame / 16) % 3));
                yield return new WaitForSecondsRealtime(1f / 30);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, "motion-frames", frame.ToString("D3") + ".png"), pixels.EncodeToPNG());
                RenderTexture.active = previous;
            }
            canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = null; target.Release(); Destroy(target); Destroy(pixels); Destroy(camera.gameObject);
            passed.Add("Captured sixty live DOTween frames of ability switching and its travelling pearl.");

            BubbleLauncher launcher = FindFirstObjectByType<BubbleLauncher>();
            UIInput.VerificationAxis = axis => 0;
            UIInput.VerificationHeld = control => control == UIInput.PadBinding(UIInput.Charge);
            yield return new WaitForSecondsRealtime(.22f);
            Check(launcher.IsCharging && Mathf.Abs(motion.ChargeProgress - launcher.ChargeProgress) < .12f,
                "The selected ability charge arc follows the gameplay launcher's actual charge.");
            yield return Capture(runtime, "motion-charging", 1600, 900);
            launcher.SendMessage("OnDisable"); UIInput.VerificationHeld = control => false; yield return null;
            UIInput.VerificationHeld = null; UIInput.VerificationAxis = null; UIInput.UseKeyboard();

            animated.reducedMotion = true; UISettings.Apply(animated, false); switcher.SetType(BubbleType.Bomb);
            yield return new WaitForSecondsRealtime(.16f);
            Check(motion.MovingOrbitCount == 0, "Reduced motion stops continuous orbital movement.");
            foreach (Transform child in motion.transform)
                if (child.GetComponent<Button>() != null) Check(child.localScale == Vector3.one, "Reduced motion keeps " + child.name + " at a stable size.");
            yield return Capture(runtime, "motion-reduced", 1600, 900);
            UISettings.Apply(original, false); yield return new WaitForSecondsRealtime(.5f);
            int baseline = DOTween.TotalActiveTweens();
            for (int i = 0; i < 12; i++) { runtime.ShowPause(); runtime.Back(); }
            yield return new WaitForSecondsRealtime(.6f);
            Check(DOTween.TotalActiveTweens() <= baseline + 2,
                "Twelve open/close cycles leave no accumulated page, button or orbital tweens.");
        }
    }
}
#endif
