using System;
using UnityEngine;

namespace PAO.UI.V2
{
    [Serializable]
    public sealed class UISettingsData
    {
        public float master = 0.8f;
        public float music = 0.8f;
        public float effects = 0.8f;
        public float sensitivity = 1f;
        public float textScale = 1f;
        public bool fullscreen = true;
        public bool reducedMotion;
        public bool highContrast;
        public bool subtitles = true;
        public bool soundCaptions;
        public bool tutorials = true;
        public bool detailedHints;
        public bool toggleCharge;
        public bool invertLook;
        public int version = 3;
        public ControllerControl[] controller = DefaultController();
        public float controllerSensitivity = 1f;
        public float moveDeadzone = 0.19f, lookDeadzone = 0.19f, triggerThreshold = 0.5f;
        public bool controllerInvertY, swapSticks, controllerToggleCharge;
        public bool muteUnfocused = true;
        public int resolutionWidth, resolutionHeight, windowMode = -1, vSync = 1, frameLimit = 60;
        public float uiScale = 1f, subtitleBackground = 0.8f;
        public int colorVision;
        public int quality = -1;
        public int language; // 0: 简体中文; 1: English; 2: pseudo-localization.
        public KeyCode[] keys = DefaultKeys();

        public static KeyCode[] DefaultKeys()
        {
            return new[] { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.E, KeyCode.J, KeyCode.F1,
                KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.LeftShift, KeyCode.F, KeyCode.Mouse0, KeyCode.Mouse1,
                KeyCode.Q, KeyCode.R };
        }

        public static ControllerControl[] DefaultController()
        {
            return new[] { ControllerControl.DpadLeft, ControllerControl.DpadDown, ControllerControl.DpadRight,
                ControllerControl.B, ControllerControl.View, ControllerControl.DpadUp,
                ControllerControl.None, ControllerControl.None, ControllerControl.None, ControllerControl.None,
                ControllerControl.A, ControllerControl.LeftStick, ControllerControl.X, ControllerControl.Y,
                ControllerControl.RightStick, ControllerControl.LB, ControllerControl.RB };
        }

        public void ResetKeyboard()
        { keys = DefaultKeys(); sensitivity = 1; invertLook = false; toggleCharge = false; }
        public void ResetController()
        {
            controller = DefaultController(); controllerSensitivity = 1;
            moveDeadzone = lookDeadzone = 0.19f; triggerThreshold = 0.5f;
            controllerInvertY = swapSticks = controllerToggleCharge = false;
        }

        public int BindKeyboard(int action, KeyCode key)
        {
            if (action < 0 || action >= keys.Length || !UIInput.IsBindable(key)) return -1;
            int conflict = Array.IndexOf(keys, key);
            if (conflict >= 0 && conflict != action) keys[conflict] = keys[action];
            keys[action] = key; return conflict == action ? -1 : conflict;
        }

        public int BindController(int action, ControllerControl control)
        {
            if (!UIInput.CanBindController(action, control)) return -1;
            int conflict = control == ControllerControl.None ? -1 : Array.IndexOf(controller, control);
            if (conflict >= 0 && conflict != action && !UIInput.CanBindController(conflict, controller[action])) return -2;
            if (conflict >= 0 && conflict != action) controller[conflict] = controller[action];
            controller[action] = control; return conflict == action ? -1 : conflict;
        }

        public UISettingsData Copy() { return JsonUtility.FromJson<UISettingsData>(JsonUtility.ToJson(this)); }
        public void Validate()
        {
            master = FiniteClamp(master, 0, 1, 0.8f); music = FiniteClamp(music, 0, 1, 0.8f); effects = FiniteClamp(effects, 0, 1, 0.8f);
            sensitivity = FiniteClamp(sensitivity, 0.3f, 2f, 1);
            textScale = FiniteClamp(textScale, 1f, 1.3f, 1);
            controllerSensitivity = FiniteClamp(controllerSensitivity, 0.3f, 3, 1);
            moveDeadzone = FiniteClamp(moveDeadzone, 0.05f, 0.5f, 0.19f);
            lookDeadzone = FiniteClamp(lookDeadzone, 0.05f, 0.5f, 0.19f);
            triggerThreshold = FiniteClamp(triggerThreshold, 0.15f, 0.95f, 0.5f);
            uiScale = FiniteClamp(uiScale, 0.85f, 1.2f, 1);
            subtitleBackground = FiniteClamp(subtitleBackground, 0, 1, 0.8f);
            colorVision = Mathf.Clamp(colorVision, 0, 3);
            windowMode = Mathf.Clamp(windowMode, -1, 2); vSync = Mathf.Clamp(vSync, 0, 1);
            if (frameLimit != 30 && frameLimit != 60 && frameLimit != 120 && frameLimit != 144 && frameLimit != -1) frameLimit = 60;
            if (resolutionWidth < 640 || resolutionHeight < 360 || resolutionWidth > 7680 || resolutionHeight > 4320)
                resolutionWidth = resolutionHeight = 0;
            quality = Mathf.Clamp(quality, -1, QualitySettings.names.Length - 1);
            language = Mathf.Clamp(language, 0, 2);
            KeyCode[] defaults = DefaultKeys();
            KeyCode[] previous = keys;
            keys = new KeyCode[defaults.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                KeyCode candidate = previous != null && i < previous.Length ? previous[i] : defaults[i];
                if (!UIInput.IsBindable(candidate) || Array.IndexOf(keys, candidate, 0, i) >= 0)
                {
                    candidate = defaults[i];
                    if (Array.IndexOf(keys, candidate, 0, i) >= 0)
                        for (KeyCode replacement = KeyCode.F3; replacement <= KeyCode.F15; replacement++)
                            if (Array.IndexOf(keys, replacement, 0, i) < 0) { candidate = replacement; break; }
                }
                keys[i] = candidate;
            }
            ControllerControl[] defaultController = DefaultController(), oldController = controller;
            controller = new ControllerControl[defaultController.Length];
            var used = new System.Collections.Generic.HashSet<ControllerControl>();
            for (int i = 0; i < controller.Length; i++)
            {
                ControllerControl value = oldController != null && i < oldController.Length ? oldController[i] : defaultController[i];
                if (!UIInput.CanBindController(i, value) || (value != ControllerControl.None && used.Contains(value)))
                {
                    value = defaultController[i];
                    if (value != ControllerControl.None && used.Contains(value))
                    {
                        value = ControllerControl.None;
                        foreach (ControllerControl candidate in Enum.GetValues(typeof(ControllerControl)))
                            if (candidate != ControllerControl.None && UIInput.CanBindController(i, candidate) && !used.Contains(candidate))
                            { value = candidate; break; }
                    }
                }
                controller[i] = value;
                if (value != ControllerControl.None) used.Add(value);
            }
            version = 3;
        }

        private static float FiniteClamp(float value, float min, float max, float fallback)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }
    }

    public static class UISettings
    {
        private const string StorageKey = "PAO.UI.Settings.V2";
        public static UISettingsData Current { get; private set; }
        public static event Action Changed;

        public static void Initialize()
        {
            Changed = null;
            try { Current = JsonUtility.FromJson<UISettingsData>(PlayerPrefs.GetString(StorageKey, "")); }
            catch (Exception) { Current = null; }
            if (Current == null) Current = new UISettingsData { fullscreen = Screen.fullScreen };
            Current.Validate();
            Apply(Current);
        }

        public static void Apply(UISettingsData settings, bool applyDisplay = true)
        {
            settings.Validate();
            Current = settings.Copy();
            AudioListener.volume = Current.master;
            AudioListener.pause = Current.muteUnfocused && !Application.isFocused;
            if (Current.quality >= 0 && QualitySettings.GetQualityLevel() != Current.quality)
                QualitySettings.SetQualityLevel(Current.quality);
            QualitySettings.vSyncCount = Current.vSync; Application.targetFrameRate = Current.frameLimit;
            if (applyDisplay) ApplyDisplay(Current);
            if (Changed != null) Changed();
        }

        public static FullScreenMode DisplayMode(UISettingsData data)
        {
            if (data.windowMode < 0) return data.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            return data.windowMode == 0 ? FullScreenMode.Windowed : data.windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen;
        }

        public static void ApplyDisplay(UISettingsData data)
        {
            FullScreenMode mode = DisplayMode(data);
            int width = data.resolutionWidth > 0 ? data.resolutionWidth : Screen.width;
            int height = data.resolutionHeight > 0 ? data.resolutionHeight : Screen.height;
            if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                Screen.SetResolution(width, height, mode);
        }

        public static void Commit(UISettingsData settings)
        {
            Apply(settings);
            PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
        }

        public static string Text(string chinese, string english)
        {
            if (Current == null || Current.language == 0) return chinese;
            if (Current.language == 1) return english;
            return "［" + english + " ········］";
        }

        public static KeyCode Key(int action, KeyCode fallback)
        {
            return UIRuntime.Instance != null && UIRuntime.Instance.IsGameplay && Current != null && Current.keys != null &&
                action >= 0 && action < Current.keys.Length ? Current.keys[action] : fallback;
        }
    }
}
