using UnityEngine;

namespace PAO.UI.V2
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class UIAudioChannel : MonoBehaviour
    {
        public bool music;
        public string soundCaption;
        private AudioSource source;
        private float baseVolume;
        private bool previousPlaying;
        private void Awake() { source = GetComponent<AudioSource>(); baseVolume = source.volume; }
        private void OnEnable() { UISettings.Changed += Apply; Apply(); }
        private void OnDisable() { UISettings.Changed -= Apply; }
        private void Apply()
        {
            if (source != null && UISettings.Current != null)
                source.volume = baseVolume * (music ? UISettings.Current.music : UISettings.Current.effects);
        }
        private void Update()
        {
            bool playing = source != null && source.isPlaying;
            if (playing && !previousPlaying && UISettings.Current.soundCaptions && UIRuntime.Instance != null && !string.IsNullOrEmpty(soundCaption))
                UIRuntime.Instance.Toast(soundCaption, false);
            previousPlaying = playing;
        }
    }
}
