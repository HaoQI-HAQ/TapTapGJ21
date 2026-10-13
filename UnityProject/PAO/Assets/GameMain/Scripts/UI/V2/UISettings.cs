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
        public bool toggleCharge;
        public bool invertLook;
        public int quality = -1;
        public int language; // 0: 简体中文; 1: English; 2: pseudo-localization.
        public KeyCode[] keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.E, KeyCode.J, KeyCode.F1 };

        public UISettingsData Copy() { return JsonUtility.FromJson<UISettingsData>(JsonUtility.ToJson(this)); }
        public void Validate()
        {
            master = Mathf.Clamp01(master); music = Mathf.Clamp01(music); effects = Mathf.Clamp01(effects);
            sensitivity = Mathf.Clamp(sensitivity, 0.3f, 2f);
            textScale = Mathf.Clamp(textScale, 1f, 1.3f);
            quality = Mathf.Clamp(quality, -1, QualitySettings.names.Length - 1);
            language = Mathf.Clamp(language, 0, 2);
            if (keys == null || keys.Length != 6)
                keys = new[] { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.E, KeyCode.J, KeyCode.F1 };
        }
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

        public static void Apply(UISettingsData settings)
        {
            settings.Validate();
            Current = settings.Copy();
            AudioListener.volume = Current.master;
            if (Current.quality >= 0 && QualitySettings.GetQualityLevel() != Current.quality)
                QualitySettings.SetQualityLevel(Current.quality);
            if (Screen.fullScreen != Current.fullscreen) Screen.fullScreen = Current.fullscreen;
            if (Changed != null) Changed();
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
            return UIRuntime.Instance != null && Current != null && Current.keys != null ? Current.keys[action] : fallback;
        }
    }
}
