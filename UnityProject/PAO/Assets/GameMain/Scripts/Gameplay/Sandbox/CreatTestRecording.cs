#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using PAO.AV;
using PAO.UI.V2;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

namespace PAO.Sandbox
{
    /// <summary>Opt-in recording of the real Play-mode regression run. Never included in builds.</summary>
    [InitializeOnLoad]
    internal static class CreatTestRecordingRequest
    {
        private const string Request = "Library/CreatTestRecording.request";
        internal const string Pending = "PAO.Creat.RecordTests";
        static CreatTestRecordingRequest()
        {
            EditorApplication.update += Poll;
        }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
            {
                File.Delete(Request);
                SessionState.SetBool(Pending, true);
                File.WriteAllText("Library/CreatSandbox.request", "verify");
            }
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Pending, false)) return;
            SandboxVerificationRunner runner = UnityEngine.Object.FindFirstObjectByType<SandboxVerificationRunner>();
            if (runner == null) return;
            SessionState.SetBool(Pending, false);
            runner.gameObject.AddComponent<CreatTestRecording>();
        }
    }

    internal sealed class CreatTestRecording : MonoBehaviour
    {
        private const int Width = 1920, Height = 1080, Fps = 30;
        private const string VideoName = "Creat-test-1080p-BGM.mp4";
        private MediaEncoder encoder;
        private RenderTexture render;
        private Texture2D pixels;
        private Canvas overlay;
        private Text caption;
        private string folder, phase = "启动沙盒";
        private int passed, failed, frames;
        private double started, nextFrame;
        private bool stopped;
        private float[] musicSamples;
        private int musicRate, musicChannels;
        private string musicPath;
        private float musicGain;
        private double audioRms;
        private long audioFrames;
        private double audioSquares;

        private void Awake()
        {
            folder = Path.GetFullPath("output/CreatRecording/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnPlayMode;
            StartCoroutine(BeginRecording());
        }

        private IEnumerator BeginRecording()
        {
            // Decode a separate copy: the game's imported music remains streamed and unchanged.
            PAOMusicCatalog.Entry entry = PAOMusicCatalog.Load()?.Resolve("Creat");
            musicPath = entry != null ? entry.clip : null;
            AudioClip original = !string.IsNullOrEmpty(musicPath) ? Resources.Load<AudioClip>(musicPath) : null;
            if (original == null) throw new InvalidOperationException("Creat's configured soundtrack is unavailable.");
            musicGain = entry != null ? Mathf.Clamp01(entry.gain) : .4f;
            string source = new Uri(Path.GetFullPath(AssetDatabase.GetAssetPath(original))).AbsoluteUri;
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(source, AudioType.MPEG))
            {
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error);
                AudioClip decoded = DownloadHandlerAudioClip.GetContent(request);
                musicRate = decoded.frequency; musicChannels = decoded.channels;
                musicSamples = new float[decoded.samples * decoded.channels];
                if (!decoded.GetData(musicSamples, 0)) throw new IOException("Could not decode Creat's music samples.");
                Destroy(decoded);
            }
            var video = new VideoTrackEncoderAttributes(new H264EncoderAttributes {
                profile = VideoEncodingProfile.H264High, gopSize = Fps * 2, numConsecutiveBFrames = 2 }) {
                frameRate = new MediaRational(Fps), width = Width, height = Height,
                includeAlpha = false, bitRateMode = VideoBitrateMode.High, targetBitRate = 20000000 };
            var audio = new AudioTrackAttributes { channelCount = (ushort)musicChannels, sampleRate = new MediaRational(musicRate) };
            encoder = new MediaEncoder(Path.Combine(folder, VideoName), video, audio);
            render = new RenderTexture(Width, Height, 24); render.Create();
            pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var root = new GameObject("Recording captions", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            overlay = root.GetComponent<Canvas>(); overlay.renderMode = RenderMode.ScreenSpaceCamera;
            overlay.sortingOrder = 30000; overlay.planeDistance = .1f;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var bar = new GameObject("Recording stage", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(root.transform, false);
            RectTransform rect = (RectTransform)bar.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.right;
            rect.pivot = Vector2.zero; rect.sizeDelta = new Vector2(0, 36);
            rect.anchoredPosition = Vector2.zero;
            bar.GetComponent<Image>().color = new Color(.025f, .035f, .055f, .94f);
            var label = new GameObject("Recording caption", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(bar.transform, false);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18, 0); labelRect.offsetMax = new Vector2(-18, 0);
            caption = label.GetComponent<Text>(); caption.font = UIRuntime.Instance.Config.font;
            caption.fontSize = 18; caption.alignment = TextAnchor.MiddleLeft; caption.color = Color.white;
            caption.raycastTarget = false;
            started = nextFrame = Time.realtimeSinceStartupAsDouble;
            StartCoroutine(Record());
            File.WriteAllText("Library/CreatTestRecording.status", "RECORDING\n" + folder);
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (message.StartsWith("[Creat verify] PASS") || message.StartsWith("[Camera particles] PASS")) passed++;
            if (message.StartsWith("[Creat verify] FAIL") || message.StartsWith("[Camera particles] FAIL")) failed++;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed++;
            if (message.StartsWith("[Camera particles]")) phase = "镜头遮挡 · 粒子虚化";
            else if (trace.Contains("VerifyUnderfoot")) phase = "脚下建造 · 占位与落点";
            else if (trace.Contains("VerifyUpwardTargeting")) phase = "仰视瞄准 · 高处造物";
            else if (trace.Contains("VerifyBrushVolume")) phase = "笔刷体积 · 区域建造与破坏";
            else if (trace.Contains("VerifySelectedDestruction")) phase = "多选 · 批量破坏与撤销";
            else if (trace.Contains("VerifyBubbleMerging")) phase = "重叠泡泡 · 自动合并";
            else if (trace.Contains("VerifySelectionAndLower")) phase = "选中模式 · 红框 · 上浮与下浮";
            else if (trace.Contains("VerifyModelPreview")) phase = "独立预览 · 材质 · 自动取景";
            else if (trace.Contains("VerifyWorkArchives")) phase = "作品管理 · 保存与读取";
            else if (message.StartsWith("[Creat verify]")) phase = "建造 · 调色 · 存档与界面";
        }

        private IEnumerator Record()
        {
            var end = new WaitForEndOfFrame();
            while (!stopped)
            {
                yield return end;
                if (Time.realtimeSinceStartupAsDouble < nextFrame) continue;
                Capture();
                nextFrame = Time.realtimeSinceStartupAsDouble + 1.0 / Fps;
            }
        }
        private void Capture()
        {
            Camera camera = Camera.main;
            UIRuntime runtime = UIRuntime.Instance;
            if (camera == null || runtime == null || encoder == null) return;
            Canvas canvas = runtime.GetComponentInChildren<Canvas>();
            if (canvas == null) return;
            RenderTexture oldTarget = camera.targetTexture, active = RenderTexture.active;
            RenderMode mode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            float plane = canvas.planeDistance;
            try
            {
                camera.targetTexture = render;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                overlay.worldCamera = camera;
                overlay.planeDistance = Mathf.Max(camera.nearClipPlane + .1f, .5f);
                caption.text = "CREAT 运行测试  |  " + phase + "  |  通过 " + passed + "  失败 " + failed;
                Canvas.ForceUpdateCanvases();
                SandboxPreviewView preview = runtime.GetComponentInChildren<SandboxPreviewView>();
                if (preview != null) preview.Render();
                camera.Render(); RenderTexture.active = render;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(false);
                int expected = Mathf.Max(frames + 1, (int)((Time.realtimeSinceStartupAsDouble - started) * Fps));
                int copies = Mathf.Min(30, expected - frames);
                for (int i = 0; i < copies; i++)
                {
                    if (!encoder.AddFrame(pixels)) throw new IOException("Could not encode Creat video frame.");
                    frames++;
                    WriteMusicUntil((long)Math.Round(frames * (double)musicRate / Fps));
                }
                if (frames < Fps || frames % (Fps * 5) < copies)
                    File.WriteAllBytes(Path.Combine(folder, "frame-" + frames.ToString("D6") + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = active;
                canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = plane;
            }
        }
        private void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) StopRecording();
        }
        private void WriteMusicUntil(long total, long fadeOutStart = -1)
        {
            if (encoder == null || musicSamples == null || total <= audioFrames) return;
            long sourceFrames = musicSamples.Length / musicChannels;
            const int chunk = 4096;
            while (audioFrames < total)
            {
                int count = (int)Math.Min(chunk, total - audioFrames);
                var samples = new NativeArray<float>(count * musicChannels, Allocator.Temp);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        long index = audioFrames + i;
                        float fade = Mathf.Min(1, (float)index / musicRate);
                        if (fadeOutStart >= 0) fade *= Mathf.Clamp01(1 - (float)(index - fadeOutStart) / (musicRate * 1.5f));
                        int source = (int)(index % sourceFrames) * musicChannels;
                        for (int channel = 0; channel < musicChannels; channel++)
                        {
                            float sample = musicSamples[source + channel] * musicGain * fade;
                            samples[i * musicChannels + channel] = sample; audioSquares += sample * sample;
                        }
                    }
                    if (!encoder.AddSamples(samples)) throw new IOException("Could not encode Creat's soundtrack.");
                }
                finally { samples.Dispose(); }
                audioFrames += count;
            }
            audioRms = Math.Sqrt(audioSquares / (audioFrames * musicChannels));
        }
        private void StopRecording()
        {
            if (stopped) return; stopped = true;
            Application.logMessageReceived -= OnLog;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            try
            {
                // Feed both tracks together; the platform encoder can block if one track is starved.
                long fadeOutStart = audioFrames;
                for (int i = 0; encoder != null && pixels != null && frames > 0 && i < Fps * 1.5f; i++)
                {
                    if (!encoder.AddFrame(pixels)) throw new IOException("Could not encode Creat's final frame.");
                    frames++;
                    WriteMusicUntil((long)Math.Round(frames * (double)musicRate / Fps), fadeOutStart);
                }
            }
            finally { encoder?.Dispose(); encoder = null; }
            if (render != null) { render.Release(); Destroy(render); }
            if (pixels != null) Destroy(pixels);
            string report = "Video: " + Path.Combine(folder, VideoName) +
                "\nResolution: " + Width + "x" + Height + "\nFPS: 30\nH264 target bitrate: 20 Mbps\nFrames: " + frames +
                "\nDuration: " + (frames / (double)Fps).ToString("F2") +
                " s\nPassed: " + passed + "\nFailed: " + failed +
                "\nMusic: " + musicPath + "\nMusic gain: " + musicGain +
                "\nAudio sample rate: " + musicRate + "\nAudio channels: " + musicChannels +
                "\nAudio RMS: " + audioRms.ToString("F6") +
                "\nSource: Actual Creat Play-mode automated regression with the game's configured soundtrack.\n";
            File.WriteAllText(Path.Combine(folder, "recording.txt"), report);
            File.WriteAllText("Library/CreatTestRecording.status", "DONE\n" + report);
        }
        private void OnDestroy() { StopRecording(); }
    }
}
#endif
