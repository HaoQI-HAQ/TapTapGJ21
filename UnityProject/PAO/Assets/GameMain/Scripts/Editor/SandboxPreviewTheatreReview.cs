#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using PAO.Sandbox;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    // Exercises the real Creat page in a disposable project, using generated world geometry and isolated archives.
    [InitializeOnLoad]
    public static class SandboxPreviewTheatreReview
    {
        private const string Requested = "PAO.SandboxPreviewTheatreReview";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly List<string> passed = new List<string>(), failed = new List<string>(), errors = new List<string>();
        private static readonly List<Object> pipelineObjects = new List<Object>();
        private static Fixture fixture;
        private static IEnumerator work;
        private static string output;
        private static RenderPipelineAsset previousPipeline, previousQuality;

        static SandboxPreviewTheatreReview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Requested, false)) return;
                SessionState.SetBool(Requested, false);
                EditorApplication.delayCall += Start;
            };
        }

        public static void RunCLI()
        {
            RequireIsolatedProject();
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOWorkPreviewTheatreVerification";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Requested, true);
            EditorApplication.isPlaying = true;
        }

        private static void RequireIsolatedProject()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../PAO_UI_VERIFICATION_PROJECT")))
                throw new InvalidOperationException("RunCLI requires a disposable project carrying PAO_UI_VERIFICATION_PROJECT.");
        }

        private static void Start()
        {
            RequireIsolatedProject();
            output = Path.GetFullPath("WorkPreviewTheatreArtifacts"); Directory.CreateDirectory(output);
            passed.Clear(); failed.Clear(); errors.Clear(); pipelineObjects.Clear();
            Application.logMessageReceived += OnLog;
            work = Run(); EditorApplication.update += Step;
        }

        private static void Step()
        {
            try { if (work.MoveNext()) return; }
            catch (Exception exception) { failed.Add(exception.ToString()); }
            EditorApplication.update -= Step; Application.logMessageReceived -= OnLog;
            if (fixture != null) fixture.Dispose(); fixture = null;
            GraphicsSettings.defaultRenderPipeline = previousPipeline; QualitySettings.renderPipeline = previousQuality;
            foreach (Object item in pipelineObjects) if (item != null) Object.DestroyImmediate(item);
            File.WriteAllLines(Path.Combine(output, "report.txt"), new[] { "Creat work preview theatre", "Passed: " + passed.Count,
                "Failed: " + failed.Count, "Errors: " + errors.Count, "" }.Concat(passed.Select(s => "PASS " + s))
                .Concat(failed.Select(s => "FAIL " + s)).Concat(errors.Select(s => "ERROR " + s)));
            EditorApplication.Exit(failed.Count == 0 && errors.Count == 0 ? 0 : 1);
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
        }

        private static IEnumerator Run()
        {
            // Bootstrap may have created a menu before this editor callback. Remove it before constructing the fixture.
            foreach (UIRuntime runtime in Object.FindObjectsByType<UIRuntime>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(runtime.gameObject);
            UISettings.Initialize(); ConfigureSettings(1);
            UIRunState.ResetSession(); UIRunState.NewRun();
            ConfigurePipeline();
            fixture = new Fixture(1600, 900, false);
            string originalWorld = WorldGeometry(); int originalRevision = fixture.world.Revision;
            Time.timeScale = .75f;
            HashSet<int> beforeTextures = TextureIds();
            fixture.runtime.ShowSandboxPreview(); Settle();
            for (int frame = 0; frame < 4; frame++) yield return null;
            SandboxPreviewView preview = Preview(); UIPage previewPage = Pages().Last();
            Check(fixture.runtime.IsSandboxPreview && Pages().Count == 1 && preview != null, "The actual ShowSandboxPreview page opens once.");
            Check(preview.ObjectCount == fixture.world.Count && preview.ObjectCount > 1000, "The preview shows every actual generated bubble cell: " + preview.ObjectCount + ".");
            Check(Time.timeScale == 0 && fixture.runtime.IsBlockingGameplay, "Opening the page pauses gameplay and blocks input.");
            Check(preview.Objects.GetComponentsInChildren<Collider>().Length == 0 && preview.Objects.GetComponentsInChildren<SandboxPiece>().Length == 0,
                "The rendering snapshot contains no gameplay colliders or SandboxPiece components.");
            Check(previewPage.GetComponentsInChildren<UIWorkPreviewTheatre>().Length == 1, "The theatre presentation is attached to the actual work page.");
            Check(ArtworkTitle().text == "Next", "An unnamed current work displays Next in the upper left.");
            CheckExpandedPresentation("initial");
            fixture.runtime.ShowSandboxPreview(); Check(Pages().Count == 1, "A repeated preview request does not duplicate the page.");

            string[] viewNames = { "透视", "正面", "侧面", "顶视" }; float[] yaws = { 35, 0, 90, 0 }, pitches = { 24, 0, 0, 89 };
            var signatures = new List<long>();
            for (int i = 0; i < viewNames.Length; i++)
            {
                Click(viewNames[i]); Settle(); preview.Render(); signatures.Add(Signature(ModelTexture()));
                Check(Mathf.Abs(Mathf.DeltaAngle(preview.Yaw, yaws[i])) < .05f && Mathf.Abs(preview.Pitch - pitches[i]) < .05f,
                    "The " + viewNames[i] + " button updates the real snapshot camera.");
            }
            Check(signatures.Distinct().Count() == 4, "All four view controls produce distinct URP images.");
            Click("透视"); Settle(); preview.Render(); long withGround = Signature(ModelTexture());
            Click("地面"); Settle(); preview.Render();
            Check(!preview.GridVisible && Signature(ModelTexture()) != withGround, "The ground control hides the rendered ground.");
            Click("地面"); Check(preview.GridVisible, "The ground control restores the ground.");
            float yaw = preview.Yaw, distance = preview.Distance;
            preview.OnDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(70, -25) });
            Check(!Mathf.Approximately(yaw, preview.Yaw), "Left dragging rotates the real model view.");
            Vector3 cameraPosition = preview.ViewCamera.transform.position;
            preview.OnDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Middle, delta = new Vector2(30, 12) });
            Check(preview.ViewCamera.transform.position != cameraPosition, "Middle dragging pans the real model view.");
            preview.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = Vector2.up });
            Check(preview.Distance < distance, "Wheel input zooms the real model view.");
            Click("取景全部"); Check(FiniteCamera(preview), "Frame all restores a finite camera with a positive distance.");
            Click("透视"); Settle(); CheckButtons("preview-1600x900"); CheckButtonTint(); Capture("preview-1600x900");
            CheckVisibleModel(ModelTexture());
            MeshRenderer atmosphere = preview.ViewCamera.transform.parent.GetComponentsInChildren<MeshRenderer>()
                .First(renderer => renderer.name == "Preview ground mist and spotlight");
            long atmosphereSignature = Signature(ModelTexture());
            atmosphere.enabled = false; preview.Render();
            Check(Signature(ModelTexture()) != atmosphereSignature, "The low fog and spotlight volume affects the rendered scene.");
            Capture("preview-atmosphere-off"); atmosphere.enabled = true; preview.Render();
            foreach (string shaderName in new[] { "SandboxPreviewSurface", "SandboxPreviewAtmosphere" })
                Check(ShaderUtil.GetShaderMessages(Resources.Load<Shader>(shaderName)).All(message => message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),
                    shaderName + ": material compiles without shader errors.");

            Click("保存作品"); Settle();
            Check(Pages().Count == 2 && Pages().Last().GetComponentInChildren<InputField>() != null,
                "Save opens the existing work naming dialog above the preview.");
            Check(preview != null && ModelTexture() != null && Time.timeScale == 0, "Opening the save dialog keeps the snapshot and pause alive.");
            Capture("preview-save-dialog"); fixture.runtime.Back(); Settle();
            Check(Pages().Count == 1 && Preview() == preview && previewPage.GetComponent<CanvasGroup>().interactable,
                "Cancelling save returns to the same interactive preview.");
            Click("作品存档"); Settle();
            Check(Pages().Count == 2 && Pages().Last().GetComponentsInChildren<Button>().Any(button => button.name == "Load sandbox autosave"),
                "Archives opens the existing work archive page.");
            Capture("preview-archives"); fixture.runtime.Back(); Settle();
            Check(Pages().Count == 1 && Preview() == preview && ModelTexture() != null,
                "Returning from archives preserves the same preview texture and camera.");
            Check(WorldGeometry() == originalWorld && fixture.world.Revision == originalRevision,
                "Opening views, manipulating the camera and visiting save/archive pages never edit the world.");
            Check(!Directory.Exists(fixture.archiveDirectory) || Directory.GetFiles(fixture.archiveDirectory).Length == 0,
                "Cancelling the naming dialog creates no archive file.");
            Click("保存作品"); Settle();
            Pages().Last().GetComponentInChildren<InputField>().text = "隔离验证作品";
            Click("Confirm sandbox work name"); Call(fixture.runtime, "UpdateTransientUI"); Settle();
            Pages().Last().GetComponentInChildren<UIWorkPreviewTheatre>().SendMessage("LateUpdate");
            Check(ArtworkTitle().text == "隔离验证作品" && fixture.world.CurrentWorkName == "隔离验证作品",
                "Saving a named work updates the title on the existing preview without reopening it.");
            Check(Pages().Count == 1 && Directory.Exists(fixture.archiveDirectory) && Directory.GetFiles(fixture.archiveDirectory, "*.json").Length == 1,
                "Confirming a work name writes exactly one record inside the fixture's isolated archive directory.");
            Check(previewPage.NoticeArea.gameObject.activeInHierarchy && previewPage.NoticeLabel.text == "作品已存档。"
                && !previewPage.NoticeArea.IsChildOf(previewPage.transform.Find("Page")),
                "The existing save toast is visible in the theatre after saving.");
            Capture("preview-saved-toast");
            Click("作品存档"); Settle();
            Check(Pages().Last().GetComponentsInChildren<Text>().Any(text => text.text == "隔离验证作品"),
                "The existing archive page lists the newly saved isolated work.");
            Capture("preview-saved-archive"); fixture.runtime.Back(); Settle();
            Check(WorldGeometry() == originalWorld && fixture.world.Revision == originalRevision, "Saving the work archive changes only its identity, leaving geometry and edit history unchanged.");
            RenderTexture[] owned = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .Where(texture => texture.name == "Creation preview" && !beforeTextures.Contains(texture.GetInstanceID())).ToArray();
            Transform model = preview.Objects; Camera modelCamera = preview.ViewCamera;
            Click("返回建造"); Settle();
            for (int frame = 0; frame < 5; frame++) yield return null;
            Check(Pages().Count == 0 && !fixture.runtime.IsSandboxPreview && Mathf.Approximately(Time.timeScale, .75f),
                "Return to build closes the preview and restores the previous gameplay time scale.");
            Check(model == null && modelCamera == null && owned.Length > 0 && owned.All(texture => texture == null),
                "Closing unloads the independent preview scene and releases its camera, objects and render texture.");
            fixture.Dispose(); fixture = null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            int[] widths = { 934, 800, 2100, 934, 1600, 800 };
            int[] heights = { 534, 900, 900, 534, 900, 900 };
            string[] names = { "preview-934x534", "preview-800x900", "preview-2100x900", "preview-large-text", "preview-empty", "preview-long-name" };
            for (int i = 0; i < widths.Length; i++)
            {
                ConfigureSettings(i == 3 ? 1.3f : 1);
                fixture = new Fixture(widths[i], heights[i], i == 4);
                if (i == 5) Call(fixture.world, "SetCurrentWork", null, new string('名', SandboxWorkArchive.NameLimit));
                fixture.runtime.ShowSandboxPreview(); Settle();
                for (int frame = 0; frame < 4; frame++) yield return null;
                CheckButtons(names[i]); CheckExpandedPresentation(names[i]);
                Check(FiniteCamera(Preview()), names[i] + ": frame all is finite."); Capture(names[i]);
                if (i == 4) Check(Preview().ObjectCount == 0 && Pages().Last().GetComponentsInChildren<Text>()
                    .Any(text => text.text.Contains("第一份作品") && text.gameObject.activeInHierarchy), "An empty world displays the first work prompt.");
                fixture.runtime.Back(); Settle();
                for (int frame = 0; frame < 3; frame++) yield return null;
                fixture.Dispose(); fixture = null;
                for (int frame = 0; frame < 2; frame++) yield return null;
            }
            fixture = new Fixture(1600, 900, true);
            SandboxWorkArchive identityArchive = new SandboxWorkArchive(fixture.archiveDirectory);
            Check(identityArchive.SaveNew(fixture.world, "雾光之间", out SandboxWorkInfo named, out string identityError),
                "A named work can be created in the isolated archive.");
            string savedIdentity = WorldJson();
            Check(identityArchive.Rename(named.Id, "云上展台", out identityError) && fixture.world.CurrentWorkName == "云上展台",
                "Renaming the active archive updates the current work identity.");
            Check((bool)Call(fixture.world, "RestoreWorldJson", savedIdentity, false) && fixture.world.CurrentWorkName == "雾光之间",
                "Rolling world JSON preserves the named work identity across a restore.");
            Check(identityArchive.Load(fixture.world, named.Id, out identityError) && fixture.world.CurrentWorkName == "云上展台",
                "Archive loading takes its authoritative name even when the embedded snapshot has an older title.");
            string currentName = fixture.world.CurrentWorkName;
            Check(!identityArchive.SaveNew(fixture.world, "   ", out _, out _) && fixture.world.CurrentWorkName == currentName,
                "A rejected name does not change the current identity.");
            Check((bool)Call(fixture.world, "RestoreWorldJson", "{\"version\":2,\"dimensions\":{\"x\":96,\"y\":72,\"z\":48},\"cellSize\":0.35,\"origin\":{\"x\":-48,\"y\":0,\"z\":-24},\"cells\":[]}", false)
                && string.IsNullOrEmpty(fixture.world.CurrentWorkName), "Legacy world JSON without naming fields restores as unnamed.");
            fixture.Dispose(); fixture = null;
            ConfigureSettings(1); Time.timeScale = 1;
        }

        private static void ConfigureSettings(float textScale) => UISettings.Apply(new UISettingsData
            { reducedMotion = true, tutorials = false, textScale = textScale, uiScale = textScale > 1 ? 1.2f : 1 }, false);
        private static object Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static List<UIPage> Pages() => (List<UIPage>)typeof(UIRuntime).GetField("pages", Private).GetValue(fixture.runtime);
        private static SandboxPreviewView Preview() => (SandboxPreviewView)typeof(UIRuntime).GetField("sandboxPreview", Private).GetValue(fixture.runtime);
        private static string WorldJson() => (string)Call(fixture.world, "CaptureWorldJson");
        [Serializable] private sealed class Geometry
        {
            public int version;
            public Vector3Int dimensions;
            public float cellSize;
            public Vector3 origin;
            public List<SandboxCellData> cells;
        }
        private static string WorldGeometry() => JsonUtility.ToJson(JsonUtility.FromJson<Geometry>(WorldJson()));
        private static Text ArtworkTitle() => Pages().Last().GetComponentsInChildren<Text>()
            .First(text => text.name == "Current artwork name");
        private static void CheckExpandedPresentation(string name)
        {
            RectTransform page = (RectTransform)Pages().Last().transform;
            RectTransform view = Preview().GetComponent<RectTransform>();
            Check(view.rect.width >= page.rect.width * .99f && view.rect.height >= page.rect.height * .99f,
                name + ": model rendering covers the entire page behind the controls.");
            Check(!Pages().Last().GetComponentsInChildren<Text>().Any(text => text.gameObject.activeInHierarchy &&
                (text.text.Contains("件造物") || text.text.Contains("C R E A T I O N   /   P R E V I E W"))),
                name + ": the former count and identity metadata are absent.");
            Check(Preview().ViewCamera.GetUniversalAdditionalCameraData().requiresDepthTexture,
                name + ": the volume can stop at actual model depth.");
        }
        private static RenderTexture ModelTexture() => Preview().GetComponent<RawImage>().texture as RenderTexture;
        private static HashSet<int> TextureIds() => new HashSet<int>(Resources.FindObjectsOfTypeAll<RenderTexture>().Select(texture => texture.GetInstanceID()));
        private static void Check(bool condition, string description) { (condition ? passed : failed).Add(description); }
        private static void Click(string label)
        {
            Button button = Pages().Last().GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.interactable &&
                (candidate.name.Contains(label) || candidate.GetComponentsInChildren<Text>().Any(text => text.text.Contains(label))));
            if (button == null) throw new InvalidOperationException("Missing active button: " + label);
            button.onClick.Invoke();
        }
        private static void Settle()
        {
            DOTween.CompleteAll(true); DOTween.KillAll();
            for (int i = 0; i < 4; i++)
            {
                Canvas.ForceUpdateCanvases();
                foreach (UIWorkPreviewTheatre theatre in fixture.root.GetComponentsInChildren<UIWorkPreviewTheatre>()) theatre.Arrange();
                foreach (UIPage page in Pages()) page.Refresh();
            }
            SandboxPreviewView preview = Preview();
            if (preview != null) preview.SendMessage("LateUpdate");
        }
        private static bool FiniteCamera(SandboxPreviewView preview)
        {
            if (preview == null || preview.ViewCamera == null) return false;
            Vector3 position = preview.ViewCamera.transform.position;
            return !float.IsNaN(position.x + position.y + position.z) && !float.IsInfinity(position.x + position.y + position.z) && preview.Distance > 0;
        }
        private static long Signature(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); long signature = 17;
            unchecked { foreach (Color32 pixel in pixels.GetPixels32().Where((pixel, index) => index % 41 == 0))
                signature = signature * 31 + pixel.r * 65536 + pixel.g * 256 + pixel.b; }
            Object.DestroyImmediate(pixels); RenderTexture.active = previous; return signature;
        }
        private static void CheckVisibleModel(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            int gold = 0, magenta = 0;
            foreach (Color32 pixel in pixels.GetPixels32())
            {
                if (pixel.r > 100 && pixel.g > 60 && pixel.r > pixel.b * 1.4f) gold++;
                if (pixel.r > 180 && pixel.b > 180 && pixel.g < 80) magenta++;
            }
            Check(gold > 1000 && magenta < 100, "The URP snapshot contains visible gold geometry without error shader pixels (gold " + gold + ", magenta " + magenta + ").");
            Object.DestroyImmediate(pixels); RenderTexture.active = previous;
        }
        private static void CheckButtons(string name)
        {
            RectTransform canvas = (RectTransform)fixture.canvas.transform;
            var rectangles = new List<Rect>(); bool inside = true, separate = true;
            foreach (Button button in Pages().Last().GetComponentsInChildren<Button>())
            {
                Vector3[] corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                Vector2 low = canvas.InverseTransformPoint(corners[0]), high = canvas.InverseTransformPoint(corners[2]);
                Rect rect = Rect.MinMaxRect(low.x, low.y, high.x, high.y); rectangles.Add(rect);
                inside &= rect.width > 8 && rect.height > 8 && rect.xMin >= canvas.rect.xMin - 2 && rect.xMax <= canvas.rect.xMax + 2
                    && rect.yMin >= canvas.rect.yMin - 2 && rect.yMax <= canvas.rect.yMax + 2;
            }
            for (int a = 0; a < rectangles.Count; a++) for (int b = a + 1; b < rectangles.Count; b++)
            {
                Rect rect = rectangles[a]; rect.xMin += 1; rect.xMax -= 1; rect.yMin += 1; rect.yMax -= 1;
                separate &= !rect.Overlaps(rectangles[b]);
            }
            Check(inside, name + ": every button is inside the canvas."); Check(separate, name + ": buttons do not overlap.");
            foreach (Text text in Pages().Last().GetComponentsInChildren<Text>())
            {
                if (string.IsNullOrWhiteSpace(text.text) || text.color.a <= 0 || !text.gameObject.activeInHierarchy) continue;
                Vector2 size = text.rectTransform.rect.size;
                TextGenerationSettings settings = text.GetGenerationSettings(new Vector2(size.x, 0));
                if (text.resizeTextForBestFit) { settings.resizeTextForBestFit = false; settings.fontSize = text.cachedTextGenerator.fontSizeUsedForBestFit; }
                float preferred = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
                Check(preferred <= size.y + 2, name + ": text fits: " + text.text.Replace("\n", " / ") + " (" + preferred.ToString("F1") + " / " + size.y.ToString("F1") + ").");
            }
        }
        private static void CheckButtonTint()
        {
            Button button = Pages().Last().GetComponentsInChildren<Button>().First(control => control.name == "透视");
            Graphic surface = button.targetGraphic;
            Color tint = surface.canvasRenderer.GetColor();
            Check(tint == Color.white, "The selected view has a white CanvasRenderer multiplier; the former disabled tint is cleared (" + tint + ").");
        }
        private static void Capture(string name)
        {
            Settle(); if (Preview() != null) Preview().Render(); fixture.camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = fixture.target;
            Texture2D pixels = new Texture2D(fixture.target.width, fixture.target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, fixture.target.width, fixture.target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels); RenderTexture.active = previous; passed.Add("Rendered " + name + ".");
        }

        private static void ConfigurePipeline()
        {
            previousPipeline = GraphicsSettings.defaultRenderPipeline; previousQuality = QualitySettings.renderPipeline;
            if (previousPipeline is UniversalRenderPipelineAsset)
            { passed.Add("Rendering with the isolated project's Universal Render Pipeline."); return; }
            const string shaders = "Assets/VerificationShaderIncludes/com.unity.render-pipelines.universal/";
            if (!AssetDatabase.IsValidFolder(shaders.TrimEnd('/'))) throw new InvalidOperationException("A URP project or verification shader includes are required.");
            UniversalRendererData data = ScriptableObject.CreateInstance<UniversalRendererData>(); pipelineObjects.Add(data);
            data.debugShaders = new ScriptableRendererData.DebugShaderResources
            {
                debugReplacementPS = AssetDatabase.LoadAssetAtPath<Shader>(shaders + "Shaders/Debug/DebugReplacement.shader"),
                hdrDebugViewPS = AssetDatabase.LoadAssetAtPath<Shader>(shaders + "Shaders/Debug/HDRDebugView.shader")
            };
            data.shaders = new UniversalRendererData.ShaderResources();
            var shaderPaths = new Dictionary<string, string> {
                {"blitPS", "Utils/Blit"}, {"copyDepthPS", "Utils/CopyDepth"}, {"screenSpaceShadowPS", "Utils/ScreenSpaceShadows"},
                {"samplingPS", "Utils/Sampling"}, {"stencilDeferredPS", "Utils/StencilDeferred"}, {"fallbackErrorPS", "Utils/FallbackError"},
                {"fallbackLoadingPS", "Utils/FallbackLoading"}, {"materialErrorPS", "Utils/MaterialError"}, {"coreBlitPS", "Utils/CoreBlit"},
                {"coreBlitColorAndDepthPS", "Utils/CoreBlitColorAndDepth"}, {"blitHDROverlay", "Utils/BlitHDROverlay"},
                {"cameraMotionVector", "CameraMotionVectors"}, {"objectMotionVector", "ObjectMotionVectors"},
                {"dataDrivenLensFlare", "PostProcessing/LensFlareDataDriven"} };
            foreach (var pair in shaderPaths) typeof(UniversalRendererData.ShaderResources).GetField(pair.Key, Any)?.SetValue(data.shaders,
                AssetDatabase.LoadAssetAtPath<Shader>(shaders + "Shaders/" + pair.Value + ".shader"));
            FieldInfo xrField = typeof(UniversalRendererData).GetField("xrSystemData", Any);
            if (xrField != null)
            {
                ScriptableObject xr = ScriptableObject.CreateInstance(xrField.FieldType); pipelineObjects.Add(xr);
                FieldInfo xrShaders = xrField.FieldType.GetField("shaders", Any); object resources = Activator.CreateInstance(xrShaders.FieldType);
                foreach (FieldInfo field in resources.GetType().GetFields(Any)) if (field.FieldType == typeof(Shader))
                    field.SetValue(resources, AssetDatabase.LoadAssetAtPath<Shader>(shaders + (field.Name.Contains("Occlusion") ? "Shaders/XR/XROcclusionMesh.shader" : "Shaders/XR/XRMirrorView.shader")));
                xrShaders.SetValue(xr, resources); xrField.SetValue(data, xr);
            }
            UniversalRenderPipelineAsset asset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>(); pipelineObjects.Add(asset);
            typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Any).SetValue(asset, new ScriptableRendererData[] { data });
            typeof(UniversalRenderPipelineAsset).GetField("m_AdditionalLightsRenderingMode", Any).SetValue(asset, LightRenderingMode.Disabled);
            // Offline verification uses compiled package assemblies. Supply these mandatory resources explicitly.
            typeof(UniversalRenderPipelineAsset).GetField("m_Textures", Any).SetValue(asset, new UniversalRenderPipelineAsset.TextureResources
                { blueNoise64LTex = Texture2D.grayTexture, bayerMatrixTex = Texture2D.grayTexture });
            asset.supportsHDR = false; asset.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = asset; QualitySettings.renderPipeline = asset;
            passed.Add("Rendering with a temporary Universal Render Pipeline in the isolated fixture.");
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject root;
            public readonly UIRuntime runtime;
            public readonly BubbleSandbox world;
            public readonly Canvas canvas;
            public readonly Camera camera;
            public readonly RenderTexture target;
            public readonly string archiveDirectory;
            private readonly UIConfig theme;

            public Fixture(int width, int height, bool empty)
            {
                theme = Object.Instantiate(Resources.Load<UIConfig>("PAOUIConfig"));
                root = new GameObject("Work preview theatre verification fixture");
                runtime = root.AddComponent<UIRuntime>(); runtime.enabled = false;
                Set(runtime, "<Config>k__BackingField", theme); Set(runtime, "<IsGameplay>k__BackingField", true);
                typeof(UIRuntime).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, runtime);
                Set(runtime, "gameplayTheme", theme); Set(runtime, "menuTheme", theme); Set(runtime, "lastScene", "Creat");
                UIWidgets ui = new UIWidgets(theme); Set(runtime, "ui", ui);
                archiveDirectory = Path.Combine(output, "IsolatedArchives", Guid.NewGuid().ToString("N"));
                Set(runtime, "sandboxWorkArchive", new SandboxWorkArchive(archiveDirectory));
                camera = new GameObject("Work preview verification UI camera", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 5;
                target = new RenderTexture(width, height, 24); target.Create(); camera.targetTexture = target;
                RectTransform canvasRect = ui.Rect("UI Canvas V2", root.transform);
                canvas = canvasRect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.sortingOrder = 1000;
                canvas.scaleFactor = height / 900f * UISettings.Current.uiScale;
                canvasRect.gameObject.AddComponent<GraphicRaycaster>(); Set(runtime, "canvas", canvas);
                RectTransform safe = ui.Rect("Safe Area", canvasRect); UIWidgets.Stretch(safe); Set(runtime, "safeRoot", safe);
                new GameObject("Work preview verification events", typeof(EventSystem)).transform.SetParent(root.transform);
                GameObject worldRoot = new GameObject("Generated verification world"); worldRoot.transform.SetParent(root.transform);
                world = worldRoot.AddComponent<BubbleSandbox>(); world.restoreSavedWorld = false; world.enabled = false;
                world.cellSize = .35f; world.dimensions = BubbleSandbox.DefaultDimensions * 2;
                if (!empty) BuildGoldCharacter();
            }

            private static bool Inside(Vector3 p)
            {
                return Ellipsoid(p, new Vector3(0, 16, 0), new Vector3(8, 11, 6)) ||
                    Ellipsoid(p, new Vector3(-1, 29, -.7f), new Vector3(7.5f, 8.5f, 6.5f)) ||
                    Ellipsoid(p, new Vector3(-8, 17, -.3f), new Vector3(3.5f, 8, 3.5f)) ||
                    Ellipsoid(p, new Vector3(8, 15.5f, .7f), new Vector3(3.5f, 7.5f, 3.5f)) ||
                    Ellipsoid(p, new Vector3(-4.5f, 4, -.2f), new Vector3(3.5f, 5.5f, 4.5f)) ||
                    Ellipsoid(p, new Vector3(4.5f, 4, .7f), new Vector3(3.5f, 5.5f, 4.5f));
            }
            private static bool Ellipsoid(Vector3 p, Vector3 center, Vector3 radius)
            { Vector3 d = p - center; return d.x * d.x / (radius.x * radius.x) + d.y * d.y / (radius.y * radius.y) + d.z * d.z / (radius.z * radius.z) <= 1; }
            private void BuildGoldCharacter()
            {
                MethodInfo add = typeof(BubbleSandbox).GetMethod("Add", Private);
                Color gold = new Color(1, .64f, .07f), cream = new Color(1, .88f, .61f), dark = new Color(.11f, .065f, .025f);
                for (int y = 0; y <= 38; y++) for (int x = -12; x <= 12; x++) for (int z = -8; z <= 8; z++)
                {
                    Vector3 p = new Vector3(x, y, z); if (!Inside(p)) continue;
                    if (Inside(p + Vector3.left) && Inside(p + Vector3.right) && Inside(p + Vector3.up) && Inside(p + Vector3.down)
                        && Inside(p + Vector3.forward) && Inside(p + Vector3.back)) continue;
                    Color tint = gold;
                    if (z < -3 && y >= 9 && y <= 22 && x * x / 45f + (y - 16) * (y - 16) / 75f < 1) tint = cream;
                    if (z < -4 && y >= 24 && y <= 28 && x >= -5 && x <= 4) tint = dark;
                    if (z < -4 && y == 25 && x >= -3 && x <= 3) tint = new Color(.42f, .13f, .06f);
                    if (z < -4 && y == 28 && x >= -4 && x <= 3) tint = cream;
                    if (z < -4 && y >= 32 && y <= 33 && ((x >= -5 && x <= -3) || (x >= 1 && x <= 3))) tint = dark;
                    if (z < -4 && y == 31 && (x == -4 || x == 2)) tint = new Color(.05f, .03f, .015f);
                    if (z < -3 && y >= 11 && y <= 15 && Mathf.Abs(x) >= 6) tint = dark;
                    SandboxCellData data = new SandboxCellData { position = new Vector3Int(x + 40, y, z + 20), color = 11,
                        customColor = true, tint = tint, shape = SandboxShape.Bubble };
                    add.Invoke(world, new object[] { data });
                }
            }
            public void Dispose()
            {
                if (runtime != null) { Call(runtime, "ClearPages"); Call(runtime, "ClearHud"); Call(runtime, "ReleasePause"); }
                DOTween.KillAll(); if (camera != null) camera.targetTexture = null;
                if (world != null) world.restoreSavedWorld = false;
                if (root != null) Object.DestroyImmediate(root);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (theme != null) Object.DestroyImmediate(theme);
            }
        }
    }
}
#endif
