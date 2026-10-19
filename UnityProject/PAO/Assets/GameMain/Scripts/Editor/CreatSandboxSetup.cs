using System;
using System.Collections.Generic;
using System.IO;
using PAO.Sandbox;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    [InitializeOnLoad]
    public static class CreatSandboxSetup
    {
        public const string ScenePath = "Assets/GameMain/Scenes/Creat.unity";
        private const string Materials = "Assets/GameMain/Art/Materials/Sandbox";
        private const string RequestPath = "Library/CreatSandbox.request";
        private const string VerifyKey = "PAO.Creat.Verify";
        private const string RestorePlaySceneKey = "PAO.Creat.RestorePlayScene";
        private const string PreviousPlaySceneKey = "PAO.Creat.PreviousPlayScene";
        private const string PreviewKey = "PAO.Creat.Preview";
        private const string ExpansionKey = "PAO.Creat.Expansion";
        private const string UnderfootKey = "PAO.Creat.Underfoot";
        private const string TargetingKey = "PAO.Creat.Targeting";
        private const string DestructionKey = "PAO.Creat.Destruction";
        private const string ModelPreviewKey = "PAO.Creat.ModelPreview";
        private const string ArchivesKey = "PAO.Creat.Archives";
        private static double nextRequestPoll;
        private static bool processing;
        static CreatSandboxSetup()
        {
            EditorApplication.update += ProcessRequest;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RestorePlaySceneKey, false))
                {
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousPlaySceneKey, ""));
                    SessionState.SetBool(RestorePlaySceneKey, false);
                }
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(VerifyKey, false))
                {
                    SessionState.SetBool(VerifyKey, false);
                    Application.runInBackground = true;
                    EditorApplication.isPaused = false;
                    var world = UnityEngine.Object.FindFirstObjectByType<BubbleSandbox>();
                    if (world != null) world.restoreSavedWorld = false;
                    var runner = new GameObject("Creat verification").AddComponent<SandboxVerificationRunner>();
                    runner.PreviewOnly = SessionState.GetBool(PreviewKey, false);
                    runner.ExpansionOnly = SessionState.GetBool(ExpansionKey, false);
                    runner.UnderfootOnly = SessionState.GetBool(UnderfootKey, false);
                    runner.TargetingOnly = SessionState.GetBool(TargetingKey, false);
                    runner.DestructionOnly = SessionState.GetBool(DestructionKey, false);
                    runner.ModelPreviewOnly = SessionState.GetBool(ModelPreviewKey, false);
                    runner.ArchivesOnly = SessionState.GetBool(ArchivesKey, false);
                }
            };
        }
        private static void ProcessRequest()
        {
            if (EditorApplication.timeSinceStartup < nextRequestPoll) return;
            nextRequestPoll = EditorApplication.timeSinceStartup + .5;
            if (processing || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(RequestPath)) return;
            string request = File.ReadAllText(RequestPath).Trim();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (request == "verify" || request == "preview" || request == "verify-archives" || request == "verify-model-preview" || request == "verify-expansion" || request == "verify-underfoot" || request == "verify-targeting" || request == "verify-destruction" || request == "resize" || request == "build")
                {
                    EditorApplication.isPlaying = false;
                }
                return;
            }
            DateTime runtimeBuild = File.GetLastWriteTimeUtc(typeof(BubbleSandbox).Assembly.Location);
            foreach (string source in Directory.GetFiles("Assets/GameMain/Scripts/Gameplay/Sandbox", "*.cs"))
                if (File.GetLastWriteTimeUtc(source) > runtimeBuild) { AssetDatabase.Refresh(); return; }
            foreach (string source in new[] { "UIRuntime.Sandbox.cs", "UIRuntime.SandboxArchives.cs", "UIRuntime.SandboxPreview.cs", "UIRuntime.Menu.cs", "UIMenuPresentation.cs", "UIButtonFeedback.cs" })
                if (File.GetLastWriteTimeUtc("Assets/GameMain/Scripts/UI/V2/" + source) > runtimeBuild) { AssetDatabase.Refresh(); return; }
            if (File.GetLastWriteTimeUtc("Assets/GameMain/Scripts/Editor/CreatSandboxSetup.cs") > File.GetLastWriteTimeUtc(typeof(CreatSandboxSetup).Assembly.Location))
            { AssetDatabase.Refresh(); return; }
            processing = true;
            File.Delete(RequestPath);
            try
            {
                if (request == "verify" || request == "preview" || request == "verify-archives" || request == "verify-model-preview" || request == "verify-expansion" || request == "verify-underfoot" || request == "verify-targeting" || request == "verify-destruction")
                {
                    if (!File.Exists(ScenePath)) BuildScene();
                    if (request == "verify-expansion") ExpandScene();
                    // Unity restores the open edit scenes, including unsaved edits, when play ends.
                    SessionState.SetString(PreviousPlaySceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                    SessionState.SetBool(RestorePlaySceneKey, true);
                    EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                    SessionState.SetBool(VerifyKey, true);
                    SessionState.SetBool(PreviewKey, request == "preview");
                    SessionState.SetBool(ExpansionKey, request == "verify-expansion");
                    SessionState.SetBool(UnderfootKey, request == "verify-underfoot");
                    SessionState.SetBool(TargetingKey, request == "verify-targeting");
                    SessionState.SetBool(DestructionKey, request == "verify-destruction");
                    SessionState.SetBool(ModelPreviewKey, request == "verify-model-preview");
                    SessionState.SetBool(ArchivesKey, request == "verify-archives");
                    EditorApplication.isPlaying = true;
                }
                else if (request == "resize") ExpandScene();
                else BuildScene();
                File.WriteAllText("Library/CreatSandbox.setup-result", "OK");
            }
            catch (Exception e)
            { File.WriteAllText("Library/CreatSandbox.setup-result", e.ToString()); Debug.LogException(e); }
            finally { processing = false; }
        }
        [MenuItem("PAO/Creat/打开泡泡沙盒", false, 10)]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("PAO/Creat/验证泡泡沙盒", false, 12)]
        public static void Verify()
        { File.WriteAllText(RequestPath, "verify"); }
        [MenuItem("PAO/Creat/扩大建造区域", false, 13)]
        public static void ExpandScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before resizing Creat.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                BubbleSandbox world = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                { world = root.GetComponentInChildren<BubbleSandbox>(true); if (world != null) break; }
                if (world == null) throw new InvalidOperationException("Creat has no sandbox controller.");
                world.origin = BubbleSandbox.DefaultOrigin; world.dimensions = BubbleSandbox.DefaultDimensions;
                world.cellSize = 2;
                Bounds bounds = world.WorldBounds; Vector3 size = bounds.size;
                var rings = new List<LineRenderer>();
                foreach (Transform child in world.transform)
                {
                    if (child.name.StartsWith("Creation floor"))
                    {
                        child.name = "Creation floor · 96m × 48m";
                        child.position = new Vector3(bounds.center.x, bounds.min.y - .2f, bounds.center.z);
                        child.localScale = new Vector3(size.x, .4f, size.z);
                    }
                    else if (child.name == "Floating foundation")
                    {
                        child.position = new Vector3(bounds.center.x, bounds.min.y - .8f, bounds.center.z);
                        child.localScale = new Vector3(size.x + 1.6f, 1, size.z + 1.6f);
                    }
                    else if (child.name == "East / west boundary")
                    {
                        float side = child.position.x < bounds.center.x ? -1 : 1;
                        child.position = new Vector3(bounds.center.x + side * (size.x * .5f + .3f), bounds.center.y, bounds.center.z);
                        child.GetComponent<BoxCollider>().size = new Vector3(.6f, size.y, size.z + .6f);
                    }
                    else if (child.name == "North / south boundary")
                    {
                        float side = child.position.z < bounds.center.z ? -1 : 1;
                        child.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z + side * (size.z * .5f + .3f));
                        child.GetComponent<BoxCollider>().size = new Vector3(size.x + .6f, size.y, .6f);
                    }
                    else if (child.name.StartsWith("Ceiling"))
                    {
                        child.name = "Ceiling · 72m";
                        child.position = new Vector3(bounds.center.x, bounds.max.y + .3f, bounds.center.z);
                        child.GetComponent<BoxCollider>().size = new Vector3(size.x + .6f, .6f, size.z + .6f);
                    }
                    else if (child.name.StartsWith("Boundary /")) rings.Add(child.GetComponent<LineRenderer>());
                    else if (child.name == "Boundary corner")
                    {
                        LineRenderer line = child.GetComponent<LineRenderer>(); Vector3 old = line.GetPosition(0);
                        float x = old.x < bounds.center.x ? bounds.min.x : bounds.max.x;
                        float z = old.z < bounds.center.z ? bounds.min.z : bounds.max.z;
                        line.SetPositions(new[] { new Vector3(x, bounds.min.y, z), new Vector3(x, bounds.max.y, z) });
                    }
                    else if (child.name == "Corner beacon")
                        child.position = new Vector3(child.position.x < bounds.center.x ? bounds.min.x : bounds.max.x,
                            bounds.min.y + .12f, child.position.z < bounds.center.z ? bounds.min.z : bounds.max.z);
                }
                rings.Sort((a, b) => a.GetPosition(0).y.CompareTo(b.GetPosition(0).y));
                for (int i = 0; i < rings.Count; i++)
                {
                    float y = Mathf.Lerp(bounds.min.y, bounds.max.y, i / (float)Mathf.Max(1, rings.Count - 1));
                    rings[i].name = "Boundary / " + y.ToString("0") + "m";
                    rings[i].SetPositions(new[] { new Vector3(bounds.min.x,y,bounds.min.z), new Vector3(bounds.max.x,y,bounds.min.z),
                        new Vector3(bounds.max.x,y,bounds.max.z), new Vector3(bounds.min.x,y,bounds.max.z), new Vector3(bounds.min.x,y,bounds.min.z) });
                }
                foreach (Camera camera in world.GetComponentsInChildren<Camera>(true)) camera.farClipPlane = 240;
                RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 180;
                EditorUtility.SetDirty(world); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save expanded Creat scene.");
                EnsureBuildScene(); Debug.Log("[Creat] Expanded arena: 96 × 48 m floor, 72 m ceiling, 41472 cells.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
        [MenuItem("PAO/Creat/搭建当前 Creat 沙盒", false, 11)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Creat must be built in edit mode.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the current scene before building Creat.");
                scene = File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            }
            if (GameObject.Find("Creat Sandbox") != null)
            {
                Debug.Log("[Creat] Existing sandbox preserved. Use the scene as authored.");
                EditorSceneManager.SaveScene(scene, ScenePath); EnsureBuildScene(); return;
            }
            Directory.CreateDirectory(Materials);
            Material bubble = MakeMaterial("BubblePearl", Shader.Find("Universal Render Pipeline/Lit"), new Color(.45f, .85f, .8f));
            bubble.SetFloat("_Smoothness", .85f); bubble.SetFloat("_Metallic", .18f);
            Material floor = MakeMaterial("CreationGrid", Shader.Find("PAO/SandboxGrid"), new Color(.16f, .33f, .36f));
            floor.SetColor("_LineColor", new Color(.38f, .64f, .62f)); floor.SetFloat("_CellSize", 2);
            Material frame = MakeMaterial("BoundaryLight", Shader.Find("Universal Render Pipeline/Unlit"), new Color(.56f, .92f, .85f));
            Material platform = MakeMaterial("PlatformEdge", Shader.Find("Universal Render Pipeline/Lit"), new Color(.065f, .17f, .21f));
            GameObject root = new GameObject("Creat Sandbox");
            BubbleSandbox world = root.AddComponent<BubbleSandbox>(); world.creationMaterial = bubble;
            world.spawn = new Vector3(0, .15f, -18);
            Primitive("Creation floor · 96m × 48m", root.transform, new Vector3(0, -.2f, 0), new Vector3(96, .4f, 48), floor);
            Primitive("Floating foundation", root.transform, new Vector3(0, -.8f, 0), new Vector3(97.6f, 1, 49.6f), platform);
            for (int side = -1; side <= 1; side += 2)
            {
                Wall("East / west boundary", root.transform, new Vector3(side * 48.3f, 36, 0), new Vector3(.6f, 72, 48.6f));
                Wall("North / south boundary", root.transform, new Vector3(0, 36, side * 24.3f), new Vector3(96.6f, 72, .6f));
            }
            Wall("Ceiling · 72m", root.transform, new Vector3(0, 72.3f, 0), new Vector3(96.6f, .6f, 48.6f));
            for (int y = 0; y <= 72; y += 24)
            {
                var points = new[] { new Vector3(-48,y,-24), new Vector3(48,y,-24), new Vector3(48,y,24), new Vector3(-48,y,24), new Vector3(-48,y,-24) };
                Line("Boundary / " + y + "m", root.transform, points, frame, y == 0 || y == 72 ? .09f : .035f);
            }
            foreach (float x in new[] { -48f, 48f }) foreach (float z in new[] { -24f, 24f })
            {
                Line("Boundary corner", root.transform, new[] { new Vector3(x,0,z), new Vector3(x,72,z) }, frame, .075f);
                Primitive("Corner beacon", root.transform, new Vector3(x, .12f, z), new Vector3(.65f, .24f, .65f), frame);
            }
            // Keep the original scene's camera and lighting objects in place.
            foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                camera.enabled = false;
                AudioListener listener = camera.GetComponent<AudioListener>(); if (listener != null) listener.enabled = false;
            }
            UIConfig config = Resources.Load<UIConfig>("PAOUIConfig");
            if (config == null || config.defaultPlayer == null) throw new InvalidOperationException("PAOUIConfig.defaultPlayer is required.");
            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(config.defaultPlayer, scene);
            PrefabUtility.UnpackPrefabInstance(player, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            player.name = "Sandbox Player"; player.transform.SetParent(root.transform, false);
            foreach (BubbleRideInteractor component in player.GetComponentsInChildren<BubbleRideInteractor>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (BombBubbleManager component in player.GetComponentsInChildren<BombBubbleManager>(true)) UnityEngine.Object.DestroyImmediate(component);
            foreach (BubbleLauncher component in player.GetComponentsInChildren<BubbleLauncher>(true)) UnityEngine.Object.DestroyImmediate(component);
            player.transform.localScale = Vector3.one * 2;
            CharacterController controller = player.GetComponent<CharacterController>();
            player.transform.position = world.spawn + Vector3.up * ((controller.height * .5f - controller.center.y) * 2);
            PlayerController movement = player.GetComponent<PlayerController>();
            var settings = new SerializedObject(movement);
            SetFloat(settings, "m_MoveSpeed", 8); SetFloat(settings, "m_JumpHeight", 2.8f);
            SetFloat(settings, "m_PivotHeight", 1.3f); SetFloat(settings, "m_CameraDistance", 5.5f);
            settings.ApplyModifiedPropertiesWithoutUndo();
            Camera view = player.GetComponentInChildren<Camera>(true);
            view.enabled = true; view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.035f, .11f, .16f); view.farClipPlane = 240;
            view.GetComponent<AudioListener>().enabled = true;
            if (view.transform.parent != null) view.transform.parent.rotation = Quaternion.Euler(17, 0, 0);
            player.AddComponent<SandboxBrush>();
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.58f, .68f, .75f);
            RenderSettings.fog = true; RenderSettings.fogColor = view.backgroundColor;
            RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 180;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional)
                { light.transform.rotation = Quaternion.Euler(42, -35, 0); light.color = new Color(1, .94f, .84f); light.intensity = 1.25f; }
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath); EnsureBuildScene();
            Debug.Log("[Creat] Bubble sandbox ready: 96 × 72 × 48 m, unlimited brush bubbles.");
        }
        private static void SetFloat(SerializedObject settings, string name, float value)
        { SerializedProperty property = settings.FindProperty(name); if (property != null) property.floatValue = value; }
        private static Material MakeMaterial(string name, Shader shader, Color color)
        {
            if (shader == null) throw new InvalidOperationException("Missing sandbox shader: " + name);
            string path = Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.color = color; EditorUtility.SetDirty(material); return material;
        }
        private static void Primitive(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static void Wall(string name, Transform parent, Vector3 position, Vector3 size)
        {
            GameObject go = new GameObject(name, typeof(BoxCollider)); go.transform.SetParent(parent, false);
            go.transform.position = position; go.GetComponent<BoxCollider>().size = size;
        }
        private static void Line(string name, Transform parent, Vector3[] points, Material material, float width)
        {
            var go = new GameObject(name, typeof(LineRenderer)); go.transform.SetParent(parent, false);
            LineRenderer line = go.GetComponent<LineRenderer>(); line.sharedMaterial = material;
            line.positionCount = points.Length; line.SetPositions(points); line.widthMultiplier = width;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        }
        private static void EnsureBuildScene()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == ScenePath);
            if (index < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); else scenes[index].enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
