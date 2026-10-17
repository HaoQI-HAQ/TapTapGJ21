using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PAO.UI;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.AV.Editor
{
    /// <summary>Map4 audiovisual acceptance in a complete disposable Unity copy.</summary>
    public static class PAOAVVerification
    {
        private const string Marker = "PAO_AV_VERIFICATION_PROJECT";
        private const string ScenePath = "Assets/GameMain/Scenes/Map4.unity";
        private const string AudioRoot = "Assets/GameMain/Resources/PAOAudio/";
        private const string Requested = "PAO.AV.VerificationRequested";
        private const string SavedChecks = "PAO.AV.VerificationEditorChecks";

        [Serializable] private sealed class AudioEntry
        {
            public string resource;
            public int samples;
            public int channels;
            public int sample_rate;
        }

        [Serializable] private sealed class AudioManifest
        {
            public AudioEntry[] assets;
        }

        internal static readonly List<string> Checks = new List<string>();
        internal static readonly List<string> Failures = new List<string>();

        public static void RunCLI()
        {
            if (!File.Exists(Path.GetFullPath(Marker)))
                throw new InvalidOperationException("Run PAOAVVerification only in a Tools/AV isolated copy.");
            Run();
        }

        /// <summary>Install and verify directly in the requested current project.</summary>
        public static void RunCurrentCLI()
        {
            Run();
        }

        private static void Run()
        {
            Checks.Clear();
            Failures.Clear();
            try
            {
                PAOAudioInstaller.InstallMap4();
                PAOVisualInstaller.InstallMap4();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                CheckAudioAssets();
                CheckMap4Scene();
                CheckShader();
                CheckGltfImports();
            }
            catch (Exception ex)
            {
                Failures.Add("Editor verification exception: " + ex);
            }
            if (Failures.Count > 0)
            {
                Finish();
                return;
            }
            SessionState.SetString(SavedChecks, string.Join("\n", Checks));
            SessionState.SetBool(Requested, true);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void IsolateVerificationSaves()
        {
            if (SessionState.GetBool(Requested, false))
                SaveSystem.EditorVerificationDirectory = Path.GetFullPath("AVVerification/IsolatedSaves");
        }

        internal static void Check(bool condition, string description)
        {
            if (condition) Checks.Add("PASS " + description);
            else Failures.Add("FAIL " + description);
        }

        private static void CheckAudioAssets()
        {
            string json = File.ReadAllText(AudioRoot + "audio_manifest.json", Encoding.UTF8);
            AudioManifest manifest = JsonUtility.FromJson<AudioManifest>(json);
            Check(manifest != null && manifest.assets != null && manifest.assets.Length == 32,
                "audio manifest has 32 resources");
            if (manifest == null || manifest.assets == null) return;

            var clips = new Dictionary<string, AudioClip>();
            foreach (AudioEntry item in manifest.assets)
            {
                if (item == null || string.IsNullOrEmpty(item.resource))
                {
                    Failures.Add("FAIL audio manifest has an empty resource path");
                    continue;
                }
                string path = AudioRoot + item.resource + ".wav";
                AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
                AudioClipLoadType expectedLoadType = item.resource.StartsWith("SFX/", StringComparison.Ordinal)
                    ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
                Check(importer != null &&
                    importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.PCM &&
                    importer.defaultSampleSettings.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate &&
                    importer.defaultSampleSettings.loadType == expectedLoadType,
                    "import settings preserve PCM and sample rate: " + item.resource);
                AudioClip asset = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                AudioClip resource = Resources.Load<AudioClip>("PAOAudio/" + item.resource);
                Check(asset != null && resource != null, "audio imports and loads: " + item.resource);
                if (resource == null) continue;
                Check(resource.samples > 0 && resource.channels == item.channels && resource.frequency == item.sample_rate,
                    "audio format: " + item.resource);
                if (item.samples > 0)
                    Check(Math.Abs(resource.samples - item.samples) <= 1,
                        "sample count: " + item.resource);
                clips[item.resource] = resource;
            }
            string[] stems = { "Music/Map4_Base", "Music/Map4_Pulse", "Music/Map4_Tension",
                               "Ambience/Map4_Reef", "Ambience/Map4_Vent", "Ambience/Map4_Trench" };
            bool aligned = clips.ContainsKey(stems[0]);
            for (int i = 1; i < stems.Length; i++)
                aligned &= clips.ContainsKey(stems[i]) &&
                    clips[stems[i]].samples == clips[stems[0]].samples &&
                    clips[stems[i]].frequency == clips[stems[0]].frequency;
            Check(aligned, "six Map4 music and ambience stems have sample-aligned length");
        }

        private static void CheckMap4Scene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Check(scene.IsValid() && scene.name == "Map4", "Map4 scene opens");
            int visualCount = 0, triggerCount = 0;
            PAOVisualDirector visual = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PAOVisualDirector found in root.GetComponentsInChildren<PAOVisualDirector>(true))
                {
                    visualCount++;
                    visual = found;
                }
                triggerCount += root.GetComponentsInChildren<NarrationTrigger>(true).Length;
            }
            Check(visualCount == 1, "Map4 contains exactly one VisualDirector");
            Check(triggerCount == 3, "Map4 contains three narration triggers");
            if (visual != null)
            {
                SerializedObject serialized = new SerializedObject(visual);
                Material ring = serialized.FindProperty("m_RingMaterial").objectReferenceValue as Material;
                Material pearl = serialized.FindProperty("m_PearlMaterial").objectReferenceValue as Material;
                Check(ring != null && pearl != null && ring.shader != null && pearl.shader != null &&
                    ring.shader.name == "PAO/FeedbackVFX" && pearl.shader.name == "PAO/FeedbackVFX",
                    "VisualDirector retains both PAO FeedbackVFX materials");
            }
            PAONarrationLibrary library = Resources.Load<PAONarrationLibrary>("PAOAudio/NarrationLibrary");
            Check(library != null && library.entries != null && library.entries.Length >= 3,
                "Map4 narration catalog loads with three transcript entries");
        }

        private static void CheckShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/GameMain/Art/Shaders/PAOFeedbackVFX.shader");
            Check(shader != null && shader.name == "PAO/FeedbackVFX" && shader.isSupported,
                "PAO FeedbackVFX shader imports and is supported");
            if (shader == null) return;
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString().IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    Failures.Add("FAIL shader compilation: " + message.message);
        }

        private static void CheckGltfImports()
        {
            string yaml = File.ReadAllText(ScenePath, Encoding.UTF8);
            var paths = new HashSet<string>();
            foreach (Match match in Regex.Matches(yaml, @"guid: ([a-f0-9]{32})"))
            {
                string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) paths.Add(path);
            }
            Check(paths.Count >= 3, "Map4 references at least three glTF model assets");
            foreach (string path in paths)
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(path) != null,
                    "glTF model imports: " + path);
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
            var runner = new GameObject("PAO AV isolated verification");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<PAOAVRuntimeCheck>();
        }

        internal static void Finish()
        {
            string folder = Path.GetFullPath("AVVerification");
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine("Passed: " + Checks.Count);
            report.AppendLine("Failures: " + Failures.Count);
            foreach (string line in Checks) report.AppendLine(line);
            foreach (string line in Failures) report.AppendLine(line);
            File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
            Debug.Log("[PAO AV Verification] " + report);
            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
        }
    }

    public sealed class PAOAVRuntimeCheck : MonoBehaviour
    {
        private readonly List<string> runtimeErrors = new List<string>();

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                runtimeErrors.Add(message + "\n" + stack);
        }

        private IEnumerator Start()
        {
            Application.logMessageReceived += OnLog;
            yield return new WaitForSecondsRealtime(3f);
            try
            {
                if (UISettings.Current != null)
                {
                    UISettingsData settings = UISettings.Current.Copy();
                    settings.muteUnfocused = false;
                    UISettings.Apply(settings, false);
                }
                PAOAVVerification.Check(PAOAudioDirector.Instance != null, "runtime audio director bootstraps");
                PAOAVVerification.Check(PAOSceneAVState.Instance != null, "runtime scene state bootstraps");
                PAOAVVerification.Check(PAOVisualDirector.Instance != null, "runtime visual director enables in Map4");
                PAOAVVerification.Check(UIRuntime.Instance != null, "existing gameplay HUD bootstraps");
                PAOAVVerification.Check(FindFirstObjectByType<PlayerController>() != null, "Map4 default player spawns");
                PAOAudioDirector audio = PAOAudioDirector.Instance;
                if (audio != null)
                {
                    AudioSource[] sources = audio.GetComponentsInChildren<AudioSource>(true);
                    int stems = 0;
                    int playingStems = 0;
                    foreach (AudioSource source in sources)
                        if (source.loop && source.clip != null)
                        {
                            stems++;
                            if (source.isPlaying) playingStems++;
                        }
                    PAOAVVerification.Check(stems >= 6, "Map4 runtime schedules six music and ambience stems");
                    PAOAVVerification.Check(playingStems >= 6, "all six Map4 stems play after DSP scheduling");
                    AudioClip voice = Resources.Load<AudioClip>("PAOAudio/SFX/Narration");
                    PAOAVVerification.Check(voice != null && audio.PlayNarration("Verification/Voice", voice) > 0f,
                        "narration playback API accepts the imported synthesized test cue");
                    audio.StopNarration();
                }
                PAOVisualDirector visual = PAOVisualDirector.Instance;
                if (visual != null)
                {
                    FeedbackBus.Emit(FeedbackCue.BubbleBurst, transform.position, 1f);
                    if (audio != null)
                    {
                        bool worldVoicePlaying = false;
                        foreach (AudioSource source in audio.GetComponentsInChildren<AudioSource>(true))
                            if (source.name.StartsWith("World voice ", StringComparison.Ordinal) && source.isPlaying)
                                worldVoicePlaying = true;
                        PAOAVVerification.Check(worldVoicePlaying, "BubbleBurst starts a world sound voice");
                    }
                    int particles = 0;
                    foreach (ParticleSystem system in visual.GetComponentsInChildren<ParticleSystem>(true))
                        particles += system.particleCount;
                    PAOAVVerification.Check(particles > 0, "bubble burst event produces visible particles");
                }
            }
            catch (Exception ex)
            {
                PAOAVVerification.Failures.Add("FAIL runtime verification exception: " + ex);
            }
            Application.logMessageReceived -= OnLog;
            foreach (string error in runtimeErrors) PAOAVVerification.Failures.Add("FAIL Unity runtime log: " + error);
            PAOAVVerification.Finish();
        }
    }
}
