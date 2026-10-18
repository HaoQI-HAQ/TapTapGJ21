using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PAO.UI;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PAO.AV.Editor
{
    /// <summary>
    /// 背景音乐接入验收：目录 JSON → 导入参数 → 关卡选曲 → 暂停菜单暂停/恢复 → 设置里的音乐滑条。
    /// 不保存场景、不写正式存档。
    /// 批处理：-executeMethod PAO.AV.Editor.PAOMusicVerification.RunCurrentCLI
    /// </summary>
    public static class PAOMusicVerification
    {
        private const string Requested = "PAO.Music.VerificationRequested";
        private const string SavedChecks = "PAO.Music.VerificationEditorChecks";
        private const string ModeKey = "PAO.Music.VerificationMode";
        private const string ReportKey = "PAO.Music.VerificationReport";
        private const string ScenePath = "Assets/GameMain/Scenes/Map1.unity";
        private const string Map4Path = "Assets/GameMain/Scenes/Map4.unity";
        private const string CatalogPath = "Assets/GameMain/Resources/PAOAudio/MusicCatalog.json";
        private const string CustomRoot = "Assets/GameMain/Resources/PAOAudio/Music/Custom/";
        private const string Pipa = CustomRoot + "Pipa_Solo.mp3";
        private const string Electronic = CustomRoot + "Guzheng_Pipa_Electronic.mp3";
        private const string PipaResource = "PAOAudio/Music/Custom/Pipa_Solo";
        private const string ElectronicResource = "PAOAudio/Music/Custom/Guzheng_Pipa_Electronic";
        private const double ExpectedSeconds = 360d;

        internal static readonly List<string> Checks = new List<string>();
        internal static readonly List<string> Failures = new List<string>();
        private static string reportPath = "music-report.txt";

        [MenuItem("Tools/PAO/Audio/Verify Background Music")]
        public static void RunCurrentCLI()
        {
            Run("level");
        }

        /// <summary>
        /// Map4 专项：新曲目和原有的多轨（Pulse / Tension / 三个环境层）同时存在时不该互相打架。
        /// 只打开场景，不保存、不调用会改场景的安装脚本。
        /// </summary>
        [MenuItem("Tools/PAO/Audio/Verify Map4 Stems")]
        public static void RunMap4CLI()
        {
            Run("map4");
        }

        private static void Run(string mode)
        {
            Checks.Clear();
            Failures.Clear();
            reportPath = mode == "map4" ? "music-map4-report.txt" : "music-report.txt";
            SessionState.SetString(ReportKey, reportPath);
            SessionState.SetString(ModeKey, mode);
            try
            {
                CheckCatalog();
                CheckImport(Pipa, PipaResource);
                CheckImport(Electronic, ElectronicResource);
                EditorSceneManager.OpenScene(mode == "map4" ? Map4Path : ScenePath, OpenSceneMode.Single);
            }
            catch (Exception error)
            {
                Failures.Add("FAIL editor exception: " + error);
            }
            if (Failures.Count > 0) { Finish(); return; }
            SessionState.SetString(SavedChecks, string.Join("\n", Checks));
            SessionState.SetBool(Requested, true);
            EditorApplication.isPlaying = true;
        }

        private static void CheckCatalog()
        {
            Check(File.Exists(CatalogPath), "music catalog json exists");
            PAOMusicCatalog catalog = PAOMusicCatalog.Load();
            Check(catalog != null && catalog.entries != null && catalog.entries.Length >= 2,
                "music catalog parses with at least two entries");
            if (catalog == null) return;

            PAOMusicCatalog.Entry menu = catalog.Resolve("Menu");
            Check(menu != null && menu.clip == ElectronicResource,
                "main menu resolves to the guzheng + pipa electronic track");
            Check(menu != null && menu.pauseWithGameplay, "main menu track is pause-aware");

            PAOMusicCatalog.Entry level = catalog.Resolve("Map1");
            Check(level != null && level.clip == PipaResource, "Map1 resolves to the pipa track");
            PAOMusicCatalog.Entry late = catalog.Resolve("Map5");
            Check(late != null && late.clip == PipaResource, "Map5 resolves to the pipa track");
            PAOMusicCatalog.Entry sub = catalog.Resolve("Map3_5");
            Check(sub != null && sub.clip == PipaResource, "unlisted scene falls back to the pipa track");
            Check(level != null && level.gain > 0f && level.gain <= 1f, "pipa entry has a usable gain");
            Check(level != null && level.muteSynthLayers, "authored track mutes the synthesized pulse layers");
        }

        private static void CheckImport(string path, string resource)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            Check(importer != null, "audio importer present: " + Path.GetFileName(path));
            if (importer != null)
            {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                Check(settings.loadType == AudioClipLoadType.Streaming,
                    "featured music streams from disk: " + Path.GetFileName(path));
                Check(settings.compressionFormat == AudioCompressionFormat.Vorbis,
                    "featured music stays compressed: " + Path.GetFileName(path));
                Check(settings.preloadAudioData == false, "featured music does not preload: " + Path.GetFileName(path));
                Check(importer.loadInBackground, "featured music loads in background: " + Path.GetFileName(path));
            }
            AudioClip clip = Resources.Load<AudioClip>(resource);
            Check(clip != null, "audio loads from Resources: " + resource);
            if (clip == null) return;
            Check(clip.channels == 2 && clip.frequency == 44100,
                "audio keeps stereo 44.1 kHz: " + resource);
            Check(Math.Abs(clip.length - ExpectedSeconds) < 1d, "audio is six minutes long: " + resource);
            long expected = (long)Math.Round(ExpectedSeconds * 44100d);
            Check(Math.Abs(clip.samples - expected) <= 4410,
                "encoder padding is trimmed for a clean loop: " + resource);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void IsolateVerificationSaves()
        {
            if (SessionState.GetBool(Requested, false))
                SaveSystem.EditorVerificationDirectory = Path.GetFullPath("AVVerification/MusicSaves");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BeginRuntimeCheck()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            SessionState.SetBool(Requested, false);
            Checks.Clear();
            string saved = SessionState.GetString(SavedChecks, string.Empty);
            if (!string.IsNullOrEmpty(saved)) Checks.AddRange(saved.Split('\n'));
            SessionState.SetString(SavedChecks, string.Empty);
            var runner = new GameObject("PAO music verification");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            if (SessionState.GetString(ModeKey, "level") == "map4") runner.AddComponent<PAOMusicMap4Check>();
            else runner.AddComponent<PAOMusicRuntimeCheck>();
            SessionState.SetString(ModeKey, string.Empty);
        }

        internal static void Check(bool condition, string description)
        {
            if (condition) Checks.Add("PASS " + description);
            else Failures.Add("FAIL " + description);
        }

        internal static void Finish()
        {
            // 批处理的工作目录不一定是工程根目录，报告固定写到 <工程>/AVVerification。
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "AVVerification");
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine("Passed: " + Checks.Count);
            report.AppendLine("Failures: " + Failures.Count);
            foreach (string line in Checks) report.AppendLine(line);
            foreach (string line in Failures) report.AppendLine(line);
            // 进 Play 模式时域会重载，静态字段回到初始值，所以文件名走 SessionState。
            string path = Path.Combine(folder, SessionState.GetString(ReportKey, reportPath));
            File.WriteAllText(path, report.ToString());
            Debug.Log("[PAO Music Verification] report -> " + path);
            Debug.Log("[PAO Music Verification] " + report);
            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }
    }

    public sealed class PAOMusicRuntimeCheck : MonoBehaviour
    {
        private readonly List<string> runtimeErrors = new List<string>();
        private bool finished;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors.Add(message + "\n" + stack);
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            StartCoroutine(Watchdog());
            // 批处理没有前台窗口，muteUnfocused 会把 AudioListener 整体暂停，
            // 那样所有音源的 isPlaying 都是 false —— 先关掉再验。
            if (UISettings.Current != null)
            {
                UISettingsData settings = UISettings.Current.Copy();
                settings.muteUnfocused = false;
                UISettings.Apply(settings, false);
            }
            AudioListener.pause = false;
            yield return new WaitForSecondsRealtime(5f);
            PAOAudioDirector audio = null;
            AudioClip pipa = null;
            try
            {
                AudioListener.pause = false;
                audio = PAOAudioDirector.Instance;
                pipa = Resources.Load<AudioClip>("PAOAudio/Music/Custom/Pipa_Solo");
                PAOMusicVerification.Check(audio != null, "audio director bootstraps in Map1");
                if (audio != null)
                {
                    PAOMusicVerification.Check(pipa != null && audio.CurrentMusic == pipa,
                        "Map1 plays the pipa track chosen by the catalog");
                    PAOMusicVerification.Check(!audio.MusicPaused, "music starts unpaused");
                    PAOMusicVerification.Check(MusicPlaying(audio, pipa), "pipa track is audible after the scene fade");
                }
            }
            catch (Exception error)
            {
                PAOMusicVerification.Failures.Add("FAIL runtime exception: " + error);
            }
            if (audio != null)
            {
                yield return CheckVolumeSlider(audio, pipa);
                yield return CheckPauseMenu(audio, pipa);
                yield return null;
            }
            yield return CheckMenuSwitch();
            Application.logMessageReceived -= OnLog;
            foreach (string error in runtimeErrors) PAOMusicVerification.Failures.Add("FAIL Unity runtime log: " + error);
            Finish();
        }

        private IEnumerator CheckVolumeSlider(PAOAudioDirector audio, AudioClip pipa)
        {
            UISettingsData original = UISettings.Current.Copy();
            UISettingsData loud = original.Copy();
            loud.music = 1f;
            UISettings.Apply(loud, false);
            yield return null;
            float full = MusicVolume(audio, pipa);
            UISettingsData quiet = original.Copy();
            quiet.music = .25f;
            UISettings.Apply(quiet, false);
            yield return null;
            float quarter = MusicVolume(audio, pipa);
            PAOMusicVerification.Check(full > .05f, "music gain is above silence at full slider");
            PAOMusicVerification.Check(quarter > 0f && Mathf.Abs(quarter / Mathf.Max(full, .0001f) - .25f) < .12f,
                "the settings music slider scales the background track");
            UISettings.Apply(original, false);
            yield return null;
        }

        private IEnumerator CheckPauseMenu(PAOAudioDirector audio, AudioClip pipa)
        {
            UIRuntime ui = UIRuntime.Instance;
            PAOMusicVerification.Check(ui != null && ui.IsGameplay, "Map1 brings up the gameplay UI runtime");
            if (ui == null) yield break;
            for (int guard = 0; guard < 8 && ui.IsBlockingGameplay; guard++) { ui.Back(); yield return null; }
            ui.ShowPause();
            PAOMusicVerification.Check(Time.timeScale == 0f, "the pause menu freezes gameplay time");
            PAOMusicVerification.Check(audio.MusicPaused, "opening the pause menu pauses the background music");
            PAOMusicVerification.Check(!MusicPlaying(audio, pipa), "the paused track stops advancing");
            ui.Back();
            // 暂停页是带关闭动画的，ReleasePause 在动画结束的回调里才执行。
            yield return new WaitForSecondsRealtime(1.2f);
            AudioListener.pause = false;
            PAOMusicVerification.Check(!audio.MusicPaused, "closing the pause menu resumes the background music");
            PAOMusicVerification.Check(MusicPlaying(audio, pipa), "the track plays again from where it stopped");
            PAOMusicVerification.Check(Mathf.Approximately(Time.timeScale, 1f), "gameplay time resumes after the pause menu");
        }

        private IEnumerator CheckMenuSwitch()
        {
            UIRuntime ui = UIRuntime.Instance;
            if (ui == null) yield break;
            for (int guard = 0; guard < 8 && ui.IsBlockingGameplay; guard++) { ui.Back(); yield return null; }
            try { ui.LoadScene("Menu"); }
            catch (Exception error) { PAOMusicVerification.Failures.Add("FAIL menu load exception: " + error); yield break; }
            yield return new WaitForSecondsRealtime(8f);
            AudioListener.pause = false;
            PAOAudioDirector audio = PAOAudioDirector.Instance;
            AudioClip electronic = Resources.Load<AudioClip>("PAOAudio/Music/Custom/Guzheng_Pipa_Electronic");
            PAOMusicVerification.Check(audio != null && electronic != null && audio.CurrentMusic == electronic,
                "the main menu switches to the guzheng + pipa electronic track");
            PAOMusicVerification.Check(audio != null && MusicPlaying(audio, electronic), "the menu track plays");
        }

        private IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(90f);
            if (finished) yield break;
            PAOMusicVerification.Failures.Add("FAIL verification timed out");
            Finish();
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            PAOMusicVerification.Finish();
        }

        private static AudioSource Find(PAOAudioDirector audio, AudioClip clip)
        {
            foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>(true))
                if (source.loop && source.clip == clip) return source;
            return null;
        }

        private static bool MusicPlaying(PAOAudioDirector audio, AudioClip clip)
        {
            AudioSource source = Find(audio, clip);
            return source != null && source.isPlaying;
        }

        private static float MusicVolume(PAOAudioDirector audio, AudioClip clip)
        {
            AudioSource source = Find(audio, clip);
            return source != null ? source.volume : 0f;
        }
    }

    /// <summary>Map4 专项：整曲 + 原有多轨是否共存。只读检查，不动场景。</summary>
    public sealed class PAOMusicMap4Check : MonoBehaviour
    {
        private readonly List<string> runtimeErrors = new List<string>();
        private bool finished;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors.Add(message + "\n" + stack);
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            if (UISettings.Current != null)
            {
                UISettingsData settings = UISettings.Current.Copy();
                settings.muteUnfocused = false;
                UISettings.Apply(settings, false);
            }
            AudioListener.pause = false;
            yield return new WaitForSecondsRealtime(6f);
            AudioListener.pause = false;

            PAOAudioDirector audio = PAOAudioDirector.Instance;
            AudioClip pipa = Resources.Load<AudioClip>("PAOAudio/Music/Custom/Pipa_Solo");
            PAOMusicVerification.Check(audio != null, "audio director bootstraps in Map4");
            if (audio != null)
            {
                PAOMusicVerification.Check(pipa != null && audio.CurrentMusic == pipa,
                    "Map4 plays the pipa track chosen by the catalog");
                int stems = 0, playing = 0;
                foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>(true))
                {
                    if (!source.loop || source.clip == null) continue;
                    stems++;
                    if (source.isPlaying) playing++;
                }
                PAOMusicVerification.Check(stems >= 6, "Map4 still schedules six music and ambience stems (" + stems + ")");
                PAOMusicVerification.Check(playing >= 6, "the Map4 stems all play (" + playing + ")");
                // 成品曲目下，合成的 Pulse / Tension 只保留音源、音量压到 0。
                foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>(true))
                {
                    if (!source.loop || source.clip == pipa) continue;
                    if (source.name != "Music Pulse" && source.name != "Music Tension") continue;
                    PAOMusicVerification.Check(source.volume < .01f,
                        "synthesized layer stays muted under the authored track: " + source.name);
                }
            }
            Application.logMessageReceived -= OnLog;
            foreach (string error in runtimeErrors) PAOMusicVerification.Failures.Add("FAIL Unity runtime log: " + error);
            if (finished) yield break;
            finished = true;
            PAOMusicVerification.Finish();
        }
    }
}
