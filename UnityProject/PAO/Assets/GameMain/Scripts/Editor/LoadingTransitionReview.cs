#if UNITY_EDITOR
// RunCLI is restricted to a disposable project prepared by Tools/UIV2/prepare_validation.py.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PAO.UI;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class LoadingTransitionReview
{
    public const string Requested = "PAO.Loading.Review";
    public static void RunCLI()
    {
        if (!File.Exists("PAO_UI_VERIFICATION_PROJECT")) throw new InvalidOperationException("Use an isolated verification project.");
        typeof(PAO.EditorTools.UIV2Verification).GetMethod("RemapCompiledPackageScripts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        PlayerSettings.companyName = "CodexVerification"; PlayerSettings.productName = "PAOLoadingReview";
        PAO.EditorTools.UIV2Tools.CreatePreview();
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/GameMain/UI/Menu.unity", true),
            new EditorBuildSettingsScene(PAO.EditorTools.UIV2Tools.PreviewPath, true) };
        EditorSceneManager.OpenScene("Assets/GameMain/UI/Menu.unity");
        SessionState.SetBool(Requested, true); EditorApplication.isPlaying = true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Prepare()
    {
        if (!SessionState.GetBool(Requested, false)) return;
        SaveSystem.EditorVerificationDirectory = Path.GetFullPath("LoadingReview/IsolatedSaves");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Start()
    {
        if (!SessionState.GetBool(Requested, false)) return;
        SessionState.SetBool(Requested, false);
        var root = new GameObject("Loading review runner"); Object.DontDestroyOnLoad(root);
        root.AddComponent<LoadingReviewRunner>();
    }
}

public sealed class LoadingReviewRunner : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> passed = new List<string>(), errors = new List<string>();
    private string output;
    private void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed.Add(message); }
    private void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }

    private IEnumerator Start()
    {
        output = Path.GetFullPath("LoadingReview"); Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        var stack = new Stack<IEnumerator>(); stack.Push(Scenarios());
        float deadline = Time.realtimeSinceStartup + 100;
        while (stack.Count > 0)
        {
            bool moved = false; object current = null;
            try
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Loading review timeout");
                moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current;
            }
            catch (Exception e) { errors.Add(e.ToString()); break; }
            if (!moved) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Application.logMessageReceived -= OnLog;
        File.WriteAllText(Path.Combine(output, "report.txt"), "Passed: " + passed.Count + "\n" + string.Join("\n", passed) +
            "\nErrors: " + errors.Count + "\n" + string.Join("\n", errors));
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    private IEnumerator Scenarios()
    {
        UISettings.Apply(new UISettingsData { tutorials = false, reducedMotion = false, fullscreen = false, windowMode = 0 }, false);
        yield return null;
        UIRuntime runtime = UIRuntime.Instance;
        Check(runtime != null, "Real Menu scene starts UIRuntime.");
        UIBubbleTransition transition = runtime.GetComponentInChildren<UIBubbleTransition>(true);
        Check(transition != null && transition.IsVisible, "Startup is covered by the bubble curtain.");
        Check(Camera.main.clearFlags == CameraClearFlags.SolidColor && Camera.main.backgroundColor == Color.black, "Menu camera clears to black.");
        var legacy = Array.Find(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None), c => c.name == "Canvas");
        Check(legacy != null && !legacy.gameObject.activeSelf, "Restored legacy objects are preserved and hidden by runtime.");
        Image backdrop = legacy.transform.Find("Black Loading Backdrop").GetComponent<Image>();
        Check(backdrop.color == Color.black && backdrop.sprite == null && backdrop.transform.GetSiblingIndex() == legacy.transform.childCount - 1,
            "Editor backdrop is opaque black and covers all old controls.");
        yield return WaitReady(runtime, transition);

        // Render the real transition mesh, including a reproducible four-second motion strip.
        transition.Begin(true); transition.SetProgress(.42f);
        UILoadingBubbleField field = transition.GetComponentInChildren<UILoadingBubbleField>();
        Capture(transition, 1920, 1080, "loading-1920x1080.png", true);
        Capture(transition, 960, 540, "loading-960x540.png", false);
        Capture(transition, 800, 900, "loading-800x900.png", false);
        CheckField(field);
        Time.timeScale = 0;
        float before = (float)typeof(UILoadingBubbleField).GetField("elapsed", Private).GetValue(field);
        yield return new WaitForSecondsRealtime(.15f);
        Check((float)typeof(UILoadingBubbleField).GetField("elapsed", Private).GetValue(field) > before, "Bubbles move with timeScale zero.");
        UISettings.Current.reducedMotion = true;
        before = (float)typeof(UILoadingBubbleField).GetField("elapsed", Private).GetValue(field);
        yield return new WaitForSecondsRealtime(.15f);
        Check((float)typeof(UILoadingBubbleField).GetField("elapsed", Private).GetValue(field) == before, "Reduced motion freezes bubble movement.");
        transition.HideImmediately(); Time.timeScale = 1; UISettings.Current.reducedMotion = false;

        runtime.LoadScene("UI Preview");
        Check(runtime.IsBlockingGameplay && transition.IsVisible, "Scene request immediately blocks input and starts curtain.");
        runtime.Back();
        Check(transition.IsVisible, "Escape cannot dismiss an active load.");
        yield return WaitReady(runtime, transition);
        Check(SceneManager.GetActiveScene().name == "UI Preview" && runtime.IsGameplay, "Async transition reaches gameplay.");
        Check(runtime.transform.Find("UI Canvas V2/Safe Area/Gameplay HUD") != null && Time.timeScale == 1, "HUD is ready and gameplay time resumes after reveal.");
        runtime.ShowPause();
        Check(Time.timeScale == 0, "Gameplay pause is active before return load.");
        runtime.LoadScene("Menu");
        yield return WaitReady(runtime, transition);
        Check(SceneManager.GetActiveScene().name == "Menu" && Time.timeScale == 1, "Return from pause reaches Menu without freezing time.");
        Check(Object.FindObjectsByType<UIBubbleTransition>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "Repeated loads reuse exactly one transition canvas.");
        runtime.LoadScene("Missing-Review-Scene"); yield return null;
        Check(!transition.IsVisible && !(bool)typeof(UIRuntime).GetField("loading", Private).GetValue(runtime), "Unavailable scene request leaves the menu usable.");
        UISettings.Current.reducedMotion = true;
        runtime.LoadScene("UI Preview"); yield return WaitReady(runtime, transition);
        Check(Time.timeScale == 1, "Reduced-motion load completes and releases gameplay.");
    }

    private IEnumerator WaitReady(UIRuntime runtime, UIBubbleTransition transition)
    {
        float deadline = Time.realtimeSinceStartup + 20;
        while ((bool)typeof(UIRuntime).GetField("loading", Private).GetValue(runtime) || transition.IsVisible)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Curtain did not finish");
            yield return null;
        }
    }

    private void CheckField(UILoadingBubbleField field)
    {
        Array bubbles = (Array)typeof(UILoadingBubbleField).GetField("bubbles", Private).GetValue(field);
        Check(bubbles.Length == 24, "Twenty-four bubbles use the same bounded simulation.");
        MethodInfo step = typeof(UILoadingBubbleField).GetMethod("Step", Private);
        for (int i = 0; i < 7200; i++) step.Invoke(field, new object[] { 1f / 120 });
        foreach (object bubble in bubbles)
        {
            Type type = bubble.GetType(); Vector2 p = (Vector2)type.GetField("position").GetValue(bubble);
            Vector2 shape = (Vector2)type.GetField("shape").GetValue(bubble);
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || Mathf.Abs(p.y) > 700 || shape.magnitude > .161f)
                throw new Exception("Bubble simulation became unstable.");
        }
        Check(true, "Sixty simulated seconds remain finite with bounded membrane deformation and recycling.");
        object a = bubbles.GetValue(0), b = bubbles.GetValue(1); Type t = a.GetType();
        t.GetField("radius").SetValue(a, 30f); t.GetField("radius").SetValue(b, 30f);
        t.GetField("position").SetValue(a, Vector2.zero); t.GetField("position").SetValue(b, new Vector2(45, 0));
        t.GetField("velocity").SetValue(a, new Vector2(10, 0)); t.GetField("velocity").SetValue(b, new Vector2(-10, 0));
        typeof(UILoadingBubbleField).GetMethod("Contact", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { a, b, (object)(1f / 120) });
        Check(Vector2.Distance((Vector2)t.GetField("position").GetValue(a), (Vector2)t.GetField("position").GetValue(b)) > 45,
            "Overlapping membranes separate with a compliant contact response.");
        Check(((Vector2)t.GetField("shapeVelocity").GetValue(a)).sqrMagnitude > 0, "Contact excites spring deformation.");
    }

    private void Capture(UIBubbleTransition transition, int width, int height, string filename, bool motion)
    {
        Canvas canvas = transition.GetComponent<Canvas>();
        Camera camera = new GameObject("Capture Camera", typeof(Camera)).GetComponent<Camera>();
        camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0, 0, -1000); camera.farClipPlane = 20;
        var target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        Canvas.ForceUpdateCanvases();
        UILoadingBubbleField field = transition.GetComponentInChildren<UILoadingBubbleField>();
        field.ResetField();
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        Action<string> save = name => {
            field.SetVerticesDirty(); Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
        };
        save(filename);
        int visibleBubblePixels = 0;
        Color32[] pixels = texture.GetPixels32();
        for (int y = height / 5; y < height * 4 / 5; y++)
            for (int x = 0; x < width; x++)
                if (pixels[y * width + x].r > 24) visibleBubblePixels++;
        Check(visibleBubblePixels > 300, "Bubble membranes render visibly in " + filename);
        if (motion)
        {
            Directory.CreateDirectory(Path.Combine(output, "frames"));
            MethodInfo step = typeof(UILoadingBubbleField).GetMethod("Step", Private);
            for (int i = 0; i < 80; i++)
            {
                for (int j = 0; j < 6; j++) step.Invoke(field, new object[] { 1f / 120 });
                save("frames/" + i.ToString("D3") + ".png");
            }
        }
        RenderTexture.active = null; camera.targetTexture = null;
        canvas.worldCamera = null; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Object.Destroy(texture); target.Release(); Object.Destroy(target); Object.Destroy(camera.gameObject);
        Canvas.ForceUpdateCanvases(); passed.Add("Rendered " + filename);
    }
}
#endif
