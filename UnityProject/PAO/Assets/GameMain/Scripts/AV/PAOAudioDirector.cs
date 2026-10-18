using System.Collections.Generic;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.AV
{
    /// <summary>Persistent native Unity audio mixer with synchronized score stems.</summary>
    [DefaultExecutionOrder(-80)]
    public sealed class PAOAudioDirector : MonoBehaviour
    {
        public static PAOAudioDirector Instance { get; private set; }

        private const string Root = "PAOAudio/";
        private readonly Dictionary<FeedbackCue, AudioClip> effects = new Dictionary<FeedbackCue, AudioClip>();
        private readonly Dictionary<FeedbackCue, float> lastCue = new Dictionary<FeedbackCue, float>();
        private readonly AudioSource[] voices = new AudioSource[12];
        private AudioSource uiVoice;
        private AudioSource narrationVoice;
        private AudioSource baseCurrent;
        private AudioSource baseNext;
        private AudioSource pulse;
        private AudioSource tension;
        private AudioSource reef;
        private AudioSource vent;
        private AudioSource trench;
        private PAONarrationLibrary narrationLibrary;
        private PAOMusicCatalog catalog;
        private string sceneName;
        private float musicTrim = .46f;
        private bool pauseWithGameplay = true;
        private bool synthLayersMuted;
        private bool musicPaused;
        private bool transitioning;
        private float transition;
        private float pulseMix;
        private float tensionMix;
        private float reefMix;
        private float ventMix;
        private float trenchMix;
        private int voiceCursor;
        private float worldWindowStart;
        private int worldWindowCount;
        private bool tideSubscribed;
        private PAO.BubbleTide.BubbleTideDirector tide;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var root = new GameObject("PAO Audiovisual Runtime");
            DontDestroyOnLoad(root);
            root.AddComponent<PAOSceneAVState>();
            root.AddComponent<PAOAudioDirector>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            baseCurrent = MakeSource("Music A", false, true);
            baseNext = MakeSource("Music B", false, true);
            pulse = MakeSource("Music Pulse", false, true);
            tension = MakeSource("Music Tension", false, true);
            reef = MakeSource("Reef ambience", false, true);
            vent = MakeSource("Vent ambience", false, true);
            trench = MakeSource("Trench ambience", false, true);
            uiVoice = MakeSource("UI sound", false, false);
            narrationVoice = MakeSource("Narration voice", false, false);
            for (int i = 0; i < voices.Length; i++) voices[i] = MakeSource("World voice " + i, true, false);
            narrationLibrary = Resources.Load<PAONarrationLibrary>(Root + "NarrationLibrary");
            SceneManager.sceneLoaded += OnSceneLoaded;
            FeedbackBus.Raised += OnFeedback;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FeedbackBus.Raised -= OnFeedback;
            UnbindTide();
            Instance = null;
        }

        private AudioSource MakeSource(string label, bool spatial, bool loop)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = spatial ? 1f : 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 28f;
            source.priority = spatial ? 140 : 30;
            return source;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            sceneName = scene.name;
            UnbindTide();
            if (catalog == null) catalog = PAOMusicCatalog.Load();
            PAOMusicCatalog.Entry entry = catalog != null ? catalog.Resolve(scene.name) : null;
            AudioClip score = null;
            if (entry != null && !string.IsNullOrEmpty(entry.clip))
                score = Resources.Load<AudioClip>(entry.clip);
            if (score == null) score = Resources.Load<AudioClip>(Root + "Music/" + sceneName + "_Base");
            musicTrim = entry != null ? Mathf.Clamp01(entry.gain) : .46f;
            pauseWithGameplay = entry == null || entry.pauseWithGameplay;
            synthLayersMuted = entry != null && entry.muteSynthLayers;
            // 暂停菜单里的「主菜单 / 重开」是先暂停再换场的：新场景必须无条件恢复播放，
            // 否则音乐就停在上一关的暂停状态里，永远不响。
            SetMusicPaused(false);
            AudioClip map4Pulse = sceneName == "Map4" ? Resources.Load<AudioClip>(Root + "Music/Map4_Pulse") : null;
            AudioClip map4Tension = sceneName == "Map4" ? Resources.Load<AudioClip>(Root + "Music/Map4_Tension") : null;
            AudioClip map4Reef = sceneName == "Map4" ? Resources.Load<AudioClip>(Root + "Ambience/Map4_Reef") : null;
            AudioClip map4Vent = sceneName == "Map4" ? Resources.Load<AudioClip>(Root + "Ambience/Map4_Vent") : null;
            AudioClip map4Trench = sceneName == "Map4" ? Resources.Load<AudioClip>(Root + "Ambience/Map4_Trench") : null;
            double scheduled = AudioSettings.dspTime + .3d;
            if (score != baseCurrent.clip || !baseCurrent.isPlaying)
            {
                baseNext.Stop();
                baseNext.clip = score;
                baseNext.volume = 0f;
                if (score != null) baseNext.PlayScheduled(scheduled);
                transition = 0f;
                transitioning = true;
            }
            if (sceneName == "Map4")
            {
                ConfigureStem(pulse, map4Pulse, scheduled);
                ConfigureStem(tension, map4Tension, scheduled);
                ConfigureStem(reef, map4Reef, scheduled);
                ConfigureStem(vent, map4Vent, scheduled);
                ConfigureStem(trench, map4Trench, scheduled);
            }
            TryBindTide();
        }

        private static void ConfigureStem(AudioSource source, AudioClip clip, double scheduled)
        {
            source.Stop();
            source.clip = clip;
            source.volume = 0f;
            if (clip != null) source.PlayScheduled(scheduled);
        }

        private void TryBindTide()
        {
            PAO.BubbleTide.BubbleTideDirector found = PAO.BubbleTide.BubbleTideDirector.Instance;
            if (found == null || found == tide) return;
            UnbindTide();
            tide = found;
            tide.GoalFlooded += OnGoalFlooded;
            tide.Won += OnTideWon;
            tide.Lost += OnTideLost;
            tideSubscribed = true;
        }

        private void UnbindTide()
        {
            if (!tideSubscribed || tide == null) { tide = null; tideSubscribed = false; return; }
            tide.GoalFlooded -= OnGoalFlooded;
            tide.Won -= OnTideWon;
            tide.Lost -= OnTideLost;
            tide = null;
            tideSubscribed = false;
        }

        private void OnGoalFlooded() { FeedbackBus.Emit(FeedbackCue.TideRising, Vector3.zero); }
        private void OnTideWon() { FeedbackBus.Emit(FeedbackCue.TideWin, Vector3.zero); }
        private void OnTideLost() { FeedbackBus.Emit(FeedbackCue.TideLose, Vector3.zero); }

        private void Update()
        {
            if (tide == null) TryBindTide();
            UISettingsData settings = UISettings.Current;
            narrationVoice.volume = settings != null ? settings.effects : .8f;
            float musicGain = settings != null ? settings.music : .8f;
            float voiceDuck = narrationVoice.isPlaying ? .42f : 1f;
            float gain = musicGain * voiceDuck;
            if (transitioning)
            {
                transition = Mathf.MoveTowards(transition, 1f, Time.unscaledDeltaTime / 2.8f);
                baseCurrent.volume = gain * musicTrim * (1f - transition);
                baseNext.volume = gain * musicTrim * transition;
                if (transition >= 1f)
                {
                    baseCurrent.Stop();
                    baseCurrent.clip = null;
                    AudioSource old = baseCurrent;
                    baseCurrent = baseNext;
                    baseNext = old;
                    transitioning = false;
                }
            }
            else baseCurrent.volume = gain * musicTrim;

            PAOSceneAVState state = PAOSceneAVState.Instance;
            float energy = state != null ? state.Energy : 0f;
            SceneMood mood = state != null ? state.Mood : SceneMood.Explore;
            // 成品曲目自带编曲，再叠原来的 Pulse / Tension 会串调，所以默认把它们压到 0；
            // 这里只是把音量收掉，音源仍然在播（关掉叠层不是一个场景开关就能回滚的事）。
            bool layers = !synthLayersMuted && sceneName == "Map4";
            float wantPulse = layers ? Mathf.Clamp01(.12f + energy * .86f + (mood == SceneMood.Resonance ? .32f : 0f)) : 0f;
            float wantTension = layers ? (mood == SceneMood.Danger ? .85f : energy * .15f) : 0f;
            if (layers && mood == SceneMood.Resolution) { wantPulse = .75f; wantTension = 0f; }
            pulseMix = Mathf.MoveTowards(pulseMix, wantPulse, Time.unscaledDeltaTime * .37f);
            tensionMix = Mathf.MoveTowards(tensionMix, wantTension, Time.unscaledDeltaTime * .3f);
            pulse.volume = gain * .29f * pulseMix;
            tension.volume = gain * .34f * tensionMix;

            SceneRegion region = state != null ? state.Region : SceneRegion.OpenWater;
            float ambienceGain = (settings != null ? settings.effects : .8f) *
                (narrationVoice.isPlaying ? .65f : 1f);
            reefMix = Mathf.MoveTowards(reefMix, sceneName == "Map4" && region == SceneRegion.Reef ? 1f : 0f,
                Time.unscaledDeltaTime * .4f);
            ventMix = Mathf.MoveTowards(ventMix, sceneName == "Map4" && region == SceneRegion.Vent ? 1f : 0f,
                Time.unscaledDeltaTime * .4f);
            trenchMix = Mathf.MoveTowards(trenchMix, sceneName == "Map4" && region == SceneRegion.Trench ? 1f : 0f,
                Time.unscaledDeltaTime * .4f);
            reef.volume = ambienceGain * .16f * reefMix;
            vent.volume = ambienceGain * .2f * ventMix;
            trench.volume = ambienceGain * .22f * trenchMix;
            // 暂停菜单打开后，换场淡入可能还会把新音源排上队；这里兜一下，
            // 保证「暂停」期间不会有音乐从淡入里冒出来。
            if (musicPaused)
            {
                PauseIfPlaying(baseCurrent);
                PauseIfPlaying(baseNext);
                PauseIfPlaying(pulse);
                PauseIfPlaying(tension);
            }
            if (sceneName != "Map4")
            {
                StopFadedStem(pulse, pulseMix);
                StopFadedStem(tension, tensionMix);
                StopFadedStem(reef, reefMix);
                StopFadedStem(vent, ventMix);
                StopFadedStem(trench, trenchMix);
            }
        }

        private static void StopFadedStem(AudioSource source, float mix)
        {
            if (mix > .0001f || source.clip == null) return;
            source.Stop();
            source.clip = null;
        }

        private void OnFeedback(FeedbackEvent e)
        {
            float gap = e.Cue == FeedbackCue.UIHover ? .09f :
                e.Cue == FeedbackCue.PlayerStep ? .21f :
                e.Cue == FeedbackCue.BubbleCollision || e.Cue == FeedbackCue.BubbleBounce ? .065f :
                e.Cue == FeedbackCue.BubbleBurst ? .09f :
                e.Cue == FeedbackCue.BubbleMerge ? .08f : .015f;
            float last;
            if (lastCue.TryGetValue(e.Cue, out last) && Time.unscaledTime - last < gap) return;
            lastCue[e.Cue] = Time.unscaledTime;

            AudioClip clip;
            if (!effects.TryGetValue(e.Cue, out clip))
            {
                string effectName = e.Cue == FeedbackCue.ChapterComplete ? "TideWin" :
                    e.Cue == FeedbackCue.ChapterFailure ? "TideLose" : e.Cue.ToString();
                clip = Resources.Load<AudioClip>(Root + "SFX/" + effectName);
                effects[e.Cue] = clip;
            }
            if (clip == null) return;
            UISettingsData settings = UISettings.Current;
            float gain = (settings != null ? settings.effects : .8f) * Mathf.Lerp(.55f, 1f, e.Intensity);
            bool ui = e.Cue == FeedbackCue.UIHover || e.Cue == FeedbackCue.UIConfirm ||
                e.Cue == FeedbackCue.UIBack || e.Cue == FeedbackCue.UISoftOpen ||
                e.Cue == FeedbackCue.Narration || e.Cue == FeedbackCue.TideWin || e.Cue == FeedbackCue.TideLose ||
                e.Cue == FeedbackCue.ChapterComplete || e.Cue == FeedbackCue.ChapterFailure ||
                e.Cue == FeedbackCue.TideRising;
            if (!ui)
            {
                if (Time.unscaledTime - worldWindowStart >= .1f)
                {
                    worldWindowStart = Time.unscaledTime;
                    worldWindowCount = 0;
                }
                if (worldWindowCount >= 5) return;
                worldWindowCount++;
            }
            AudioSource source = ui ? uiVoice : voices[voiceCursor++ % voices.Length];
            if (!ui)
            {
                if (source.isPlaying) source.Stop();
                source.transform.position = e.Position;
            }
            source.pitch = ui ? 1f : Random.Range(.96f, 1.045f);
            source.volume = gain;
            source.PlayOneShot(clip);
        }

        /// <summary>Resolves an optional voice clip by stable ID, then plays it under existing subtitles.</summary>
        public float PlayNarration(string id, AudioClip overrideClip = null)
        {
            AudioClip clip = overrideClip != null ? overrideClip :
                narrationLibrary != null ? narrationLibrary.Find(id) : null;
            if (clip == null && !string.IsNullOrEmpty(id)) clip = Resources.Load<AudioClip>(Root + "Narration/" + id);
            narrationVoice.Stop();
            if (clip == null) return 0f;
            UISettingsData settings = UISettings.Current;
            narrationVoice.clip = clip;
            narrationVoice.volume = settings != null ? settings.effects : .8f;
            narrationVoice.Play();
            return clip.length;
        }

        public void StopNarration()
        {
            if (narrationVoice.isPlaying) narrationVoice.Stop();
        }

        /// <summary>背景音乐当前是否处于暂停状态。</summary>
        public bool MusicPaused
        {
            get { return musicPaused; }
        }

        /// <summary>当前场景曲目的相对增益（JSON 目录里的 gain）。</summary>
        public float MusicTrim
        {
            get { return musicTrim; }
        }

        /// <summary>当前场景实际在用的背景音乐。</summary>
        public AudioClip CurrentMusic
        {
            get { return baseCurrent != null ? baseCurrent.clip : null; }
        }

        /// <summary>
        /// 暂停 / 恢复背景音乐，播放位置会保留（走 AudioSource.Pause）。
        /// 只动音乐：音效、旁白、环境声照常播。
        /// 暂停菜单打开时由 <see cref="PAO.UI.V2.UIRuntime"/> 调用，
        /// 也可以在别处直接调 <see cref="PauseMusic"/> / <see cref="ResumeMusic"/>。
        /// </summary>
        public void SetMusicPaused(bool paused)
        {
            if (paused && !pauseWithGameplay) return;
            if (musicPaused == paused) return;
            musicPaused = paused;
            if (paused)
            {
                PauseIfPlaying(baseCurrent);
                PauseIfPlaying(baseNext);
                PauseIfPlaying(pulse);
                PauseIfPlaying(tension);
            }
            else
            {
                baseCurrent.UnPause();
                baseNext.UnPause();
                pulse.UnPause();
                tension.UnPause();
            }
        }

        public void PauseMusic() { SetMusicPaused(true); }
        public void ResumeMusic() { SetMusicPaused(false); }

        private static void PauseIfPlaying(AudioSource source)
        {
            if (source != null && source.isPlaying) source.Pause();
        }
    }
}
