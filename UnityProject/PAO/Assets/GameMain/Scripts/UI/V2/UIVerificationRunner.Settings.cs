#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIVerificationRunner
    {
        private static Button NamedButton(UIRuntime runtime, string name) => Array.Find(Top(runtime).GetComponentsInChildren<Button>(), b => b.name == name);
        private static object SettingsSessionOf(UIRuntime runtime) => typeof(UIRuntime).GetField("settingsSession", Private).GetValue(runtime);
        private static UISettingsData DraftOf(UIRuntime runtime)
        {
            object session = SettingsSessionOf(runtime);
            return (UISettingsData)session.GetType().GetField("draft").GetValue(session);
        }
        private static void SelectSettingsTab(UIRuntime runtime, string label)
        { Button tab = FindButton(runtime, label); tab.Select(); tab.onClick.Invoke(); }

        private IEnumerator SettingsScenarios(UIRuntime runtime)
        {
            UISettingsData original = UISettings.Current.Copy();
            var repaired = new UISettingsData { controller = new[] { ControllerControl.Menu, ControllerControl.A, ControllerControl.A },
                controllerSensitivity = float.NaN, moveDeadzone = float.PositiveInfinity, textScale = float.NaN };
            repaired.Validate();
            Check(repaired.controller.Length == 17 && repaired.controller[UIInput.Forward] == ControllerControl.None &&
                !Array.Exists(repaired.controller, c => c == ControllerControl.Menu) && !float.IsNaN(repaired.controllerSensitivity),
                "Legacy and malformed settings restore controller defaults, reserved navigation and finite parameters.");
            Check(UIInput.ApplyDeadzone(new Vector2(0.1f, 0), 0.19f) == Vector2.zero &&
                Mathf.Abs(UIInput.ApplyDeadzone(Vector2.right, 0.19f).x - 1) < 0.001f,
                "Stick drift inside the deadzone is suppressed while full movement is preserved.");

            runtime.ShowSettings(); SelectSettingsTab(runtime, "键鼠");
            Check(NamedButton(runtime, "Bind " + UIInput.Charge) != null && NamedButton(runtime, "Bind " + UIInput.NextBubble) != null,
                "The keyboard page exposes every gameplay action, including mouse buttons and bubble cycling.");
            yield return Capture(runtime, "settings-keyboard-1080p", 1920, 1080);
            NamedButton(runtime, "Bind " + UIInput.Charge).Select(); yield return null;
            yield return Capture(runtime, "settings-keyboard-bindings", 1600, 900);
            SelectSettingsTab(runtime, "手柄");
            Check(NamedButton(runtime, "Bind " + UIInput.Forward) == null && NamedButton(runtime, "Bind " + UIInput.Jump) != null,
                "The controller page shows stick assignment separately from button bindings.");
            yield return Capture(runtime, "settings-controller-1080p", 1920, 1080);
            yield return Capture(runtime, "settings-controller-960", 960, 540);
            NamedButton(runtime, "Bind " + UIInput.Charge).Select(); yield return null;
            yield return Capture(runtime, "settings-controller-bindings", 1600, 900);
            FindButton(runtime, "按键示意").onClick.Invoke();
            yield return Capture(runtime, "settings-controller-layout", 1600, 900); runtime.Back();
            FindButton(runtime, "输入参数").onClick.Invoke();
            Check(NamedButton(runtime, "Bind " + UIInput.Charge) == null && Top(runtime).GetComponentsInChildren<Slider>().Length == 4,
                "Controller bindings and independent input parameters have separate subpages.");
            yield return Capture(runtime, "settings-controller-options", 1600, 900);
            Canvas.ForceUpdateCanvases();
            foreach (Slider slider in Top(runtime).GetComponentsInChildren<Slider>())
                Check(slider.handleRect.rect.height <= slider.GetComponent<RectTransform>().rect.height + 1,
                    "A slider handle fits its track without covering the parameter label.");
            FindButton(runtime, "操作绑定").onClick.Invoke();

            var buttons = new HashSet<ControllerControl>(); var axes = new Dictionary<string, float>();
            UIInput.VerificationHeld = c => buttons.Contains(c);
            UIInput.VerificationAxis = name => axes.TryGetValue(name, out float value) ? value : 0;
            yield return null;
            NamedButton(runtime, "Bind " + UIInput.Charge).onClick.Invoke();
            Check(!EventSystem.current.sendNavigationEvents, "Capture blocks menu submit/navigation so a captured button cannot activate another control.");
            yield return null; yield return null;
            yield return Capture(runtime, "settings-controller-capture", 1600, 900);
            axes["PAOTriggerR"] = 0.9f;
            yield return null; yield return null;
            Check(UISettings.Current.controller[UIInput.Charge] == ControllerControl.RT,
                "Pressing a trigger in the capture prompt changes the actual controller action binding.");
            axes.Clear(); yield return null; yield return null; yield return null;
            Check(EventSystem.current.sendNavigationEvents, "Navigation is restored after the captured control is released.");
            ControllerControl charge = UISettings.Current.controller[UIInput.Charge];
            NamedButton(runtime, "Bind " + UIInput.Jump).onClick.Invoke();
            yield return null; yield return null;
            buttons.Add(ControllerControl.X); yield return null; yield return null;
            Check(Top(runtime).name == "绑定已被占用" && UISettings.Current.controller[UIInput.Jump] == ControllerControl.A,
                "Controller conflicts show a confirmation and keep existing bindings until accepted.");
            yield return Capture(runtime, "settings-binding-conflict", 1600, 900);
            buttons.Clear(); FindButton(runtime, "取消").onClick.Invoke(); yield return null; yield return null;
            Check(UISettings.Current.controller[UIInput.Jump] == ControllerControl.A && UISettings.Current.controller[UIInput.Ride] == ControllerControl.X,
                "Cancelling a controller conflict preserves both actions.");
            NamedButton(runtime, "Bind " + UIInput.Jump).onClick.Invoke();
            yield return null; yield return null;
            buttons.Add(ControllerControl.X); yield return null; yield return null;
            buttons.Clear(); FindButton(runtime, "确认").onClick.Invoke(); yield return null;
            Check(UISettings.Current.controller[UIInput.Jump] == ControllerControl.X && UISettings.Current.controller[UIInput.Ride] == ControllerControl.A,
                "Accepting a controller conflict swaps both gameplay bindings.");
            NamedButton(runtime, "Reset " + UIInput.Jump).onClick.Invoke();
            FindButton(runtime, "确认").onClick.Invoke(); yield return null;
            Check(UISettings.Current.controller[UIInput.Jump] == ControllerControl.A && UISettings.Current.controller[UIInput.Ride] == ControllerControl.X &&
                UISettings.Current.controller[UIInput.Charge] == charge,
                "Single-action controller reset resolves conflicts without resetting unrelated bindings.");
            NamedButton(runtime, "Clear 0").onClick.Invoke(); yield return null;
            NamedButton(runtime, "Bind 0").onClick.Invoke(); yield return null; yield return null;
            buttons.Add(ControllerControl.A); yield return null; yield return null;
            Check(Top(runtime).name == "游戏设置" && UISettings.Current.controller[0] == ControllerControl.None &&
                UISettings.Current.controller[UIInput.Jump] == ControllerControl.A,
                "Clearing optional direct selection is allowed, but a conflict cannot unbind mandatory jump.");
            buttons.Clear(); yield return null; yield return null; yield return null;
            FindButton(runtime, "重置手柄绑定").onClick.Invoke(); FindButton(runtime, "确认").onClick.Invoke();
            Check(UISettings.Current.controller[UIInput.Charge] == ControllerControl.Y && UISettings.Current.keys[UIInput.Charge] == original.keys[UIInput.Charge],
                "Device reset restores controller defaults without altering keyboard bindings.");
            SelectSettingsTab(runtime, "键鼠");
            Action<KeyCode> keyboardCapture;
            NamedButton(runtime, "Bind " + UIInput.Jump).onClick.Invoke();
            keyboardCapture = (Action<KeyCode>)typeof(UIRuntime).GetField("rebind", Private).GetValue(runtime);
            runtime.Back(); keyboardCapture(KeyCode.F3); yield return null;
            NamedButton(runtime, "Reset " + UIInput.Jump).onClick.Invoke();
            Check(UISettings.Current.keys[UIInput.Jump] == KeyCode.Space && UISettings.Current.controller[UIInput.Jump] == ControllerControl.A,
                "Single-action keyboard reset restores Space without changing the controller.");
            NamedButton(runtime, "Bind " + UIInput.Charge).onClick.Invoke();
            typeof(UIRuntime).GetField("captureDeadline", Private).SetValue(runtime, Time.unscaledTime - 1);
            yield return null; yield return null;
            Check(Top(runtime).name == "游戏设置" && UISettings.Current.keys[UIInput.Charge] == original.keys[UIInput.Charge],
                "Capture timeout closes cleanly and preserves the original binding.");
            SelectSettingsTab(runtime, "手柄");
            NamedButton(runtime, "Bind " + UIInput.Jump).Select(); GameObject stableFocus = EventSystem.current.currentSelectedGameObject;
            buttons.Add(ControllerControl.Y); yield return null; yield return null;
            Check(UIInput.ControllerActive && EventSystem.current.currentSelectedGameObject == stableFocus,
                "Switching to controller input updates prompts without rebuilding the page or losing selection.");
            buttons.Clear(); yield return null;

            var draft = DraftOf(runtime);
            draft.BindController(UIInput.Charge, ControllerControl.RT);
            draft.controllerSensitivity = 1.7f; draft.lookDeadzone = 0.12f;
            UISettings.Apply(draft, false);
            FindButton(runtime, "应用").onClick.Invoke(); yield return null; yield return null;
            UISettingsData saved = JsonUtility.FromJson<UISettingsData>(PlayerPrefs.GetString("PAO.UI.Settings.V2")); saved.Validate();
            Check(saved.controller[UIInput.Charge] == ControllerControl.RT && saved.controllerSensitivity == 1.7f,
                "Applying settings persists controller mappings and independent look options for restart.");
            axes["PAOTriggerR"] = 0.9f; yield return null;
            Check(UIInput.Held(UIInput.Charge) && UIInput.Prompt(UIInput.Charge) == "RT",
                "The saved trigger activates the gameplay action and updates its on-screen prompt.");
            axes.Clear(); buttons.Add(ControllerControl.Y); yield return null;
            Check(!UIInput.Held(UIInput.Charge), "The previous fixed Y button stops activating charge after remapping.");
            buttons.Clear(); yield return null;

            runtime.ShowSettings(); FindButton(runtime, "手柄").onClick.Invoke();
            NamedButton(runtime, "Bind " + UIInput.Charge).onClick.Invoke();
            yield return null; yield return null;
            buttons.Add(ControllerControl.Menu); yield return null; yield return null;
            Check(Top(runtime).name == "游戏设置" && UISettings.Current.controller[UIInput.Charge] == ControllerControl.RT,
                "Menu cancels controller capture without remapping the reserved pause button.");
            buttons.Clear(); yield return null; yield return null; yield return null;
            runtime.Back(); yield return null; yield return null;

            runtime.ShowSettings(); SelectSettingsTab(runtime, "画面");
            UISettingsData video = DraftOf(runtime); video.resolutionWidth = Screen.width == 800 ? 960 : 800; video.resolutionHeight = 600;
            int oldWidth = Screen.width, oldHeight = Screen.height; FullScreenMode oldMode = Screen.fullScreenMode;
            FindButton(runtime, "应用").onClick.Invoke();
            Check(Top(runtime).name == "保留画面设置？", "Display changes wait for explicit keep/revert confirmation before saving.");
            yield return Capture(runtime, "settings-display-confirmation", 1600, 900);
            FindButton(runtime, "还原").onClick.Invoke(); yield return null; yield return null;
            Check(Top(runtime).name == "游戏设置" && Screen.width == oldWidth && Screen.height == oldHeight && Screen.fullScreenMode == oldMode,
                "Manual display revert restores the actual previous display state and keeps settings open.");
            video = DraftOf(runtime); video.resolutionWidth = oldWidth == 800 ? 960 : 800; video.resolutionHeight = 600;
            FindButton(runtime, "应用").onClick.Invoke(); yield return new WaitForSecondsRealtime(15.2f);
            Check(Top(runtime).name == "游戏设置" && Screen.width == oldWidth && Screen.height == oldHeight,
                "Display confirmation automatically reverts after 15 seconds without input.");
            runtime.Back(); yield return null;

            runtime.ShowSettings(); SelectSettingsTab(runtime, "辅助");
            DraftOf(runtime).subtitleBackground = 0.25f; UISettings.Apply(DraftOf(runtime), false);
            Image sample = Top(runtime).transform.GetComponentInChildren<UISubtitleBackground>().GetComponent<Image>();
            Check(Mathf.Abs(sample.color.a - 0.25f) < 0.01f, "Subtitle opacity changes the rendered subtitle backing immediately.");
            yield return Capture(runtime, "settings-accessibility", 1600, 900);
            FindButton(runtime, "语言").onClick.Invoke(); FindButton(runtime, "English").onClick.Invoke();
            Check(FindButton(runtime, "Controller") != null && FindButton(runtime, "Apply") != null,
                "Language preview updates tabs and the apply/back controls immediately.");
            FindButton(runtime, "Back").onClick.Invoke(); yield return null; yield return null;
            Check(UISettings.Current.language == original.language && UISettings.Current.controller[UIInput.Charge] == ControllerControl.RT,
                "Cancelling settings restores the applied configuration and language, not factory defaults.");

            UISettingsData large = UISettings.Current.Copy(); large.language = 2; large.textScale = 1.3f; large.uiScale = 1.2f; large.reducedMotion = true;
            UISettings.Apply(large, false); runtime.ShowSettings(); SelectSettingsTab(runtime, UISettings.Text("手柄", "Controller"));
            NamedButton(runtime, "Bind " + UIInput.Charge).Select();
            yield return Capture(runtime, "settings-controller-large-pseudo", 960, 540);
            foreach (Button button in Top(runtime).GetComponentsInChildren<Button>())
            {
                Text label = button.GetComponentInChildren<Text>();
                Check(label.rectTransform.rect.height + 1 >= label.preferredHeight,
                    "Large UI and text scaling keep a localized button label fully readable: " + label.text);
            }
            runtime.Back(); UISettings.Apply(saved, false);

            bool connected = true; UIInput.VerificationDevices = () => connected ? new[] { "Verification controller" } : new string[0];
            buttons.Add(ControllerControl.LeftStick); yield return new WaitForSecondsRealtime(0.65f); buttons.Clear();
            connected = false; yield return new WaitForSecondsRealtime(0.65f);
            Check(Top(runtime).name == "手柄已断开" && Time.timeScale == 0 && UIInput.Blocked,
                "Disconnecting the active controller opens a recovery dialog and pauses gameplay.");
            FindButton(runtime, "使用键鼠").onClick.Invoke(); yield return null;
            Check(Top(runtime).name == "在这里，停留片刻" && !UIInput.ControllerActive,
                "Keyboard recovery returns to pause and preserves the player's modal context.");
            runtime.Back(); yield return null; yield return null;
            UIInput.VerificationHeld = null; UIInput.VerificationAxis = null; UIInput.VerificationDevices = null;
            UIInput.UseKeyboard(); UISettings.Commit(original);
        }
    }

}
#endif
