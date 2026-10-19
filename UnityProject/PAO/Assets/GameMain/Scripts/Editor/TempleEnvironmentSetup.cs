using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PAO.Underwater;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>Scene-authored underwater atmosphere; does not replace level art or gameplay.</summary>
    public static class TempleEnvironmentSetup
    {
        const string AssetFolder = "Assets/GameMain/Art/Environment/TempleAtmosphere";
        const string OutputFolder = "Docs/TempleAtmosphere";
        const string CausticsName = "Temple_CausticsScreen";
        const string HazeName = "Temple_WaterHaze";
        const string WaterSource = "Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat";
        const string ShaftsPrefab = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab";
        const string DustPrefab = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Ambient Particles.prefab";
        const string ShaftsSource = "Assets/ThirdParty/Thalassophobia/Contents/Materials/Sunshafts.mat";
        // Map4's original animated light cookie. Reuse it without editing its shared settings.
        const string CausticsCookie = "Assets/ThirdParty/Thalassophobia/Contents/Materials/Textures/Caustics.asset";
        const float KeyLightIntensity = 2.1f;

        sealed class Palette
        {
            public string map, mood;
            public Color fog, ambient, shallow, accent;
            public float density, exposure, temperature;
            public Color keyColor = new Color(.98f,.99f,1f), blueFillColor = new Color(.97f,.99f,1f);
            public float keyIntensity = KeyLightIntensity, keyShadowStrength = .25f, blueFillIntensity = 1.1f;
            public float underFillIntensity = .65f, contrast = -12f, bloomIntensity = .12f;
            public bool underFillEnabled = true;
            public Vector3 entry, goal;
        }

        static readonly Palette[] Palettes = {
            new Palette { map = "Map2", mood = "水下遗迹 · 清晰水光",
                fog = new Color(.65f,.81f,.86f), ambient = new Color(1.10f,1.08f,1.02f),
                shallow = new Color(.77f,.91f,.92f), accent = new Color(1f,.93f,.80f),
                density = 0f, exposure = -.10f, temperature = 0f,
                entry = new Vector3(-18,2,25), goal = new Vector3(8,8,-111.5f) },
            new Palette { map = "Map2_5", mood = "水光回廊 · 明亮柔光",
                fog = new Color(.68f,.82f,.88f), ambient = new Color(1.10f,1.08f,1.04f),
                shallow = new Color(.79f,.92f,.93f), accent = new Color(1f,.94f,.82f),
                density = 0f, exposure = -.15f, temperature = 0f,
                entry = new Vector3(0,0,84.5f), goal = new Vector3(12,11.5f,-23.5f) },
            new Palette { map = "Map3", mood = "水下峡谷 · 清透浅水",
                fog = new Color(.65f,.80f,.89f), ambient = new Color(.71f,.81f,.93f),
                shallow = new Color(.77f,.89f,.95f), accent = new Color(.94f,.97f,1f),
                density = 0f, exposure = -.10f, temperature = -6f, contrast = -6f,
                keyColor = new Color(.91f,.96f,1f), keyShadowStrength = .5f,
                blueFillColor = new Color(.49f,.77f,.91f), blueFillIntensity = .45f,
                underFillIntensity = 0f, underFillEnabled = false,
                entry = new Vector3(0,-16,60), goal = new Vector3(0,0,-97.5f) }
        };

        [Serializable] sealed class Receipt
        {
            public string scene, mood, backup, utc;
            public Vector3 boundsCenter, boundsSize;
            public float waterHeight, fogDensity;
            public int collidersBefore, collidersAfter, gameplayBehavioursBefore, gameplayBehavioursAfter;
            public int cameras, lights, particleSystems, missingScripts;
            public string[] screenshots;
        }

        [MenuItem("Tools/PAO/环境氛围/搭建 Map2 + Map2_5 + Map3")]
        public static void BuildAll() { ProcessAll(true); }

        [MenuItem("Tools/PAO/环境氛围/导出三个地图预览")]
        public static void CaptureAll() { PrepareCausticsPreview(() => ProcessAll(false)); }

        static void PrepareCausticsPreview(Action capture)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before capturing the atmosphere.");
            var cookie = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>(CausticsCookie);
            if (cookie == null) throw new FileNotFoundException("Missing Map4 caustics cookie",CausticsCookie);
            if (!cookie.IsCreated()) cookie.Create();
            cookie.Initialize();
            cookie.Update();
            // CustomRenderTexture updates run on the next editor frame, not inside Camera.Render.
            // Keep the asset loaded and allow that frame before rendering additive scene previews.
            double readyAt = EditorApplication.timeSinceStartup + .2;
            EditorApplication.CallbackFunction ready = null;
            ready = () =>
            {
                if (EditorApplication.timeSinceStartup < readyAt) return;
                EditorApplication.update -= ready;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                try { capture(); }
                catch (Exception e) { Debug.LogException(e); }
                finally { GC.KeepAlive(cookie); }
            };
            EditorApplication.update += ready;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/PAO/环境氛围/移除三个地图水雾")]
        public static void RemoveHaze() { AdjustAtmosphere(false); }

        [MenuItem("Tools/PAO/环境氛围/调整为明亮柔和浅水")]
        public static void BrightenAtmosphere() { AdjustAtmosphere(true); }

        [MenuItem("Tools/PAO/环境氛围/修复当前 Map3 光照")]
        public static void ApplyMap3LightingRepair()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before repairing Map3 lighting.");
            const string path = "Assets/GameMain/Scenes/Map3.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Map3 must already be loaded before repairing its lighting.");
            // RenderSettings belongs to the active scene. Never switch scenes or rebuild the environment here.
            if (SceneManager.GetActiveScene() != scene)
                throw new InvalidOperationException("Map3 must be the active scene before repairing its lighting.");

            Palette p = Palettes.Single(palette => palette.map == "Map3");
            GameObject environment = Environment(scene,p);
            if (environment == null) throw new InvalidOperationException("Map3_Environment was not found.");
            var lights = Components<Light>(scene);
            Light sun = lights.SingleOrDefault(light => light.type == LightType.Directional && light.cookie != null);
            Light fill = environment.GetComponentsInChildren<Light>(true).SingleOrDefault(light => light.name == "Temple_BlueFill");
            Light underFill = environment.GetComponentsInChildren<Light>(true).SingleOrDefault(light => light.name == "Temple_UnderFill");
            Volume volume = environment.GetComponentsInChildren<Volume>(true).SingleOrDefault(v => v.name == "Atmosphere_PostFX");
            VolumeProfile profile = volume != null ? volume.sharedProfile : null;
            if (sun == null || fill == null || profile == null)
                throw new InvalidOperationException("Map3 key light, blue fill or atmosphere profile was not found.");
            if (AssetDatabase.GetAssetPath(profile) != AssetFolder + "/Map3_AtmosphereProfile.asset")
                throw new InvalidOperationException("Map3 must use its own atmosphere profile before repairing lighting.");
            if (!profile.TryGet<ColorAdjustments>(out var color) || !profile.TryGet<WhiteBalance>(out var balance) ||
                !profile.TryGet<Bloom>(out var bloom))
                throw new InvalidOperationException("Map3 atmosphere profile is missing its color, white balance or bloom settings.");

            // Repair only the excessive lighting; preserve fog, water, geometry, gameplay, cookies and transforms.
            RenderSettings.ambientLight = p.ambient;
            RenderSettings.ambientSkyColor = p.ambient;
            RenderSettings.ambientEquatorColor = p.ambient * .9f;
            RenderSettings.ambientGroundColor = p.ambient * .75f;
            SetAmbientProbe(p);
            sun.color = p.keyColor; sun.intensity = p.keyIntensity; sun.shadowStrength = p.keyShadowStrength;
            fill.color = p.blueFillColor; fill.intensity = p.blueFillIntensity;
            if (underFill != null)
            {
                underFill.intensity = p.underFillIntensity;
                underFill.enabled = p.underFillEnabled;
            }
            color.postExposure.Override(p.exposure); color.contrast.Override(p.contrast);
            balance.temperature.Override(p.temperature);
            bloom.intensity.Override(p.bloomIntensity);
            EditorUtility.SetDirty(color); EditorUtility.SetDirty(balance); EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Debug.Log("[Temple Atmosphere] Repaired Map3 lighting. Scene and profile are dirty; save after reviewing.");
        }

        static void AdjustAtmosphere(bool brighten)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before editing the atmosphere.");
            Scene previous = SceneManager.GetActiveScene();
            string backupDir = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../PAO-EnvironmentBackups"));
            Directory.CreateDirectory(backupDir);
            try
            {
                foreach (Palette p in Palettes)
                {
                    string path = "Assets/GameMain/Scenes/" + p.map + ".unity";
                    Scene scene = SceneManager.GetSceneByPath(path);
                    bool opened = !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    try
                    {
                        SceneManager.SetActiveScene(scene);
                        string backup = Path.Combine(backupDir,p.map + (brighten ? "_before_brighten_" : "_before_remove_haze_") + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
                        if (scene.isDirty)
                        {
                            if (!EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Could not back up " + p.map);
                        }
                        else File.Copy(path,backup);
                        RenderSettings.fog = false;
                        foreach (var haze in Components<TempleAtmosphereVolume>(scene)) haze.enabled = false;
                        if (brighten)
                        {
                            var root = Environment(scene,p).transform;
                            ApplyRenderSettings(p);
                            Light sun = Components<Light>(scene).First(l => l.type == LightType.Directional && l.cookie != null);
                            sun.color = p.keyColor; sun.intensity = p.keyIntensity; sun.shadowStrength = p.keyShadowStrength;
                            var fill = root.Find("Temple_BlueFill").GetComponent<Light>();
                            fill.color = p.blueFillColor; fill.intensity = p.blueFillIntensity;
                            CreateUnderFill(root,p);
                            foreach (var camera in Components<Camera>(scene)) camera.backgroundColor = p.fog;
                            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                                if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "PAO/UnderwaterCeiling")
                                    ApplyWaterPalette(renderer.sharedMaterial,p);
                            var profile = root.GetComponentInChildren<Volume>().sharedProfile;
                            ConfigurePostFX(profile,p);
                        }
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + p.map);
                    }
                    finally
                    {
                        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                        if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene,true);
                    }
                }
                if (brighten) AssetDatabase.SaveAssets();
                Debug.Log("[Temple Atmosphere] " + (brighten ? "Brightened ambient light, shadow fill and water background; haze remains off." : "Removed water haze and distance fog from Map2, Map2_5 and Map3."));
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                SceneView.RepaintAll();
            }
        }

        static void ConfigureCausticLight(Light sun)
        {
            var cookie = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>(CausticsCookie);
            if (cookie == null) throw new FileNotFoundException("Missing Map4 caustics cookie",CausticsCookie);
            sun.color = new Color(.91f,.96f,1f);
            sun.intensity = KeyLightIntensity;
            sun.transform.rotation = Quaternion.Euler(82.97f,-79.067f,7.603f);
            sun.cookie = cookie;
            sun.cookieSize = 10f;
            var data = sun.GetUniversalAdditionalLightData();
            data.lightCookieSize = new Vector2(15,15);
            data.lightCookieOffset = Vector2.zero;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .5f;
            RenderSettings.sun = sun;
        }

        [MenuItem("Tools/PAO/环境氛围/修正 wengjie 焦散")]
        public static void RepairWengjieCaustics()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before editing the atmosphere.");
            const string path = "Assets/GameMain/Scenes/wengjie.unity";
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                EnsureFolder(AssetFolder);
                Directory.CreateDirectory(OutputFolder);
                string backupDir = Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../PAO-EnvironmentBackups"));
                Directory.CreateDirectory(backupDir);
                string backup = Path.Combine(backupDir,"wengjie_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
                if (scene.isDirty) EditorSceneManager.SaveScene(scene,backup,true);
                else File.Copy(path,backup);
                var colliders = Components<Collider>(scene).ToDictionary(c => c,c => c.enabled);
                var gameplay = Components<MonoBehaviour>(scene).Where(b => b != null && !(b is Volume) && !(b is UniversalAdditionalLightData))
                    .Select(b => (behaviour: b,b.enabled,b.transform.position,b.transform.rotation,b.transform.lossyScale)).ToArray();
                int audioSources = Components<AudioSource>(scene).Length;
                Camera camera = Components<Camera>(scene).First(c => c.enabled);
                var palette = new Palette { map = "wengjie", fog = RenderSettings.fogColor };
                Vector3 position = camera.transform.position;
                Vector3 target = position + camera.transform.forward * 20f;
                string beforePath = Path.Combine(OutputFolder,"wengjie_before_gameplay.png");
                if (!File.Exists(beforePath)) Capture(scene,palette,camera,position,target,beforePath,true);

                var old = Components<Transform>(scene).Where(t => t.name == "PAO_CausticsScreen" || t.name == "Wengjie_GroundCaustics")
                    .Select(t => t.gameObject).ToArray();
                foreach (var go in old) Object.DestroyImmediate(go);
                Light sun = Components<Light>(scene).First(l => l.type == LightType.Directional);
                ConfigureCausticLight(sun);
                // The old stone material is fully metallic. Give this scene diffuse receivers for the cookie.
                foreach (Renderer renderer in Components<MeshRenderer>(scene))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i=0;i<materials.Length;i++)
                    {
                        Material original = materials[i];
                        if (original == null || !(original.name.StartsWith("M_Wengjie_StoneBlue") || original.name.StartsWith("wengjie_Stone"))) continue;
                        string suffix = original.name.EndsWith("_B") ? "Stone_B" : "Stone";
                        Material stone = CopyMaterial(AssetDatabase.GetAssetPath(original),"wengjie",suffix);
                        stone.SetFloat("metallicFactor",0f);
                        stone.SetFloat("roughnessFactor",.9f);
                        stone.SetFloat("normalTexture_scale",.65f);
                        materials[i] = stone;
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.48f,.62f,.68f);
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.reflectionIntensity = .25f;
                var ambientPalette = new Palette { ambient = RenderSettings.ambientLight };
                SetAmbientProbe(ambientPalette);
                var fill = Components<Light>(scene).FirstOrDefault(l => l.name == "Wengjie_CausticsFill");
                if (fill == null)
                {
                    var go = new GameObject("Wengjie_CausticsFill");
                    SceneManager.MoveGameObjectToScene(go,scene);
                    fill = go.AddComponent<Light>();
                }
                fill.type = LightType.Directional; fill.intensity = .38f; fill.color = new Color(.54f,.76f,.84f);
                fill.shadows = LightShadows.None; fill.transform.rotation = Quaternion.Euler(35,135,0);

                // Give this scene its own bloom settings; the shared third-party profile stays intact.
                var post = Components<Volume>(scene).First(v => v.name == "Wengjie_PostFX");
                string profilePath = AssetFolder + "/wengjie_SoftPostFX.asset";
                if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) == null &&
                    !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(post.sharedProfile),profilePath))
                    throw new IOException("Could not copy the Wengjie post-processing profile.");
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
                post.sharedProfile = profile;
                var bloom = Effect<Bloom>(profile);
                bloom.intensity.Override(.15f); bloom.threshold.Override(1.3f); bloom.scatter.Override(.72f);
                var color = Effect<ColorAdjustments>(profile);
                color.postExposure.Override(-.15f); color.contrast.Override(-4f);
                foreach (var effect in profile.components) EditorUtility.SetDirty(effect);
                EditorUtility.SetDirty(profile);
                foreach (Camera c in Components<Camera>(scene)) c.GetUniversalAdditionalCameraData().renderPostProcessing = true;

                foreach (var pair in colliders)
                    if (pair.Key == null || pair.Key.enabled != pair.Value) throw new InvalidOperationException("Wengjie collision changed.");
                foreach (var pose in gameplay)
                    if (pose.behaviour == null || pose.behaviour.enabled != pose.enabled || pose.behaviour.transform.position != pose.position ||
                        pose.behaviour.transform.rotation != pose.rotation || pose.behaviour.transform.lossyScale != pose.lossyScale)
                        throw new InvalidOperationException("Wengjie gameplay changed.");
                if (Components<AudioSource>(scene).Length != audioSources) throw new InvalidOperationException("Wengjie audio changed.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save Wengjie caustics.");
                AssetDatabase.SaveAssets();
                SetAmbientProbe(ambientPalette);
                Capture(scene,palette,camera,position,target,Path.Combine(OutputFolder,"wengjie_after_gameplay.png"),true);
                Capture(scene,palette,camera,position + new Vector3(-3,7,8),position + new Vector3(0,-3,15),
                    Path.Combine(OutputFolder,"wengjie_after_detail.png"),true);
                File.WriteAllLines(Path.Combine(OutputFolder,"wengjie-verification.txt"),new[] {
                    "PASS: legacy fullscreen/ground caustics removed; Map4 animated cookie (15x15) assigned to the main light.",
                    "PASS: preserved " + colliders.Count + " colliders, " + gameplay.Length + " gameplay behaviours and " + audioSources + " audio sources.",
                    "PASS: bloom uses a scene-specific profile. Backup: " + backup });
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene,true);
            }
        }

        [MenuItem("Tools/PAO/环境氛围/检查焦散渲染")]
        public static void VerifyCausticRendering()
        {
            PrepareCausticsPreview(VerifyCausticRenderingNow);
        }

        static void VerifyCausticRenderingNow()
        {
            Scene previous = SceneManager.GetActiveScene();
            var lines = new List<string>();
            try
            {
                foreach (Palette p in Palettes.Concat(new[] { new Palette { map = "wengjie", ambient = new Color(.48f,.62f,.68f) } }))
                {
                    string path = "Assets/GameMain/Scenes/" + p.map + ".unity";
                    Scene scene = SceneManager.GetSceneByPath(path);
                    bool opened = !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(scene);
                    var sun = Components<Light>(scene).First(l => l.type == LightType.Directional && l.cookie != null);
                    Texture originalCookie = sun.cookie;
                    try
                    {
                        SetAmbientProbe(p);
                        Vector3 position = p.entry + new Vector3(-4,12,-7);
                        Vector3 target = p.entry + new Vector3(0,0,-18);
                        if (p.map == "wengjie")
                        {
                            Camera camera = Components<Camera>(scene).First(c => c.enabled);
                            p.fog = RenderSettings.fogColor;
                            Vector3 cameraPosition = camera.transform.position;
                            Capture(scene,p,camera,cameraPosition,cameraPosition + camera.transform.forward * 20f,
                                Path.Combine(OutputFolder,"wengjie_after_gameplay.png"),true);
                            position = cameraPosition + new Vector3(-3,7,8);
                            target = cameraPosition + new Vector3(0,-3,15);
                            Capture(scene,p,camera,position,target,Path.Combine(OutputFolder,"wengjie_after_detail.png"),true);
                        }
                        string livePath = "Temp/" + p.map + "_CausticsLive.png";
                        string darkPath = "Temp/" + p.map + "_CausticsDark.png";
                        Capture(scene,p,null,position,target,livePath,true);
                        sun.cookie = Texture2D.blackTexture;
                        Capture(scene,p,null,position,target,darkPath,true);
                        var live = new Texture2D(2,2); var dark = new Texture2D(2,2);
                        try
                        {
                            live.LoadImage(File.ReadAllBytes(livePath)); dark.LoadImage(File.ReadAllBytes(darkPath));
                            Color32[] a = live.GetPixels32(), b = dark.GetPixels32();
                            int changed = 0;
                            for (int i=0;i<a.Length;i++)
                            {
                                int delta = Math.Max(Math.Abs(a[i].r-b[i].r),Math.Max(Math.Abs(a[i].g-b[i].g),Math.Abs(a[i].b-b[i].b)));
                                if (delta > 3) changed++;
                                a[i] = new Color32((byte)Math.Min(255,Math.Abs(a[i].r-b[i].r)*4),
                                    (byte)Math.Min(255,Math.Abs(a[i].g-b[i].g)*4),(byte)Math.Min(255,Math.Abs(a[i].b-b[i].b)*4),255);
                            }
                            live.SetPixels32(a); live.Apply();
                            File.WriteAllBytes("Temp/" + p.map + "_CausticsDifference.png",live.EncodeToPNG());
                            if (changed < 64) throw new InvalidDataException(p.map + " light cookie has no visible contribution.");
                            lines.Add(p.map + ": PASS; animated light cookie affects " + changed + "/" + a.Length + " pixels in the detail view.");
                        }
                        finally { Object.DestroyImmediate(live); Object.DestroyImmediate(dark); }
                    }
                    finally
                    {
                        sun.cookie = originalCookie;
                        if (opened) EditorSceneManager.CloseScene(scene,true);
                    }
                }
                File.WriteAllLines(Path.Combine(OutputFolder,"caustics-render-check.txt"),lines);
            }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        [MenuItem("Tools/PAO/环境氛围/检查保存结果")]
        public static void VerifySaved()
        {
            Scene previous = SceneManager.GetActiveScene();
            var lines = new List<string>();
            try
            {
                foreach (Palette p in Palettes)
                {
                    Scene scene = SceneManager.GetSceneByPath("Assets/GameMain/Scenes/" + p.map + ".unity");
                    bool opened = !scene.isLoaded;
                    if (opened) scene = EditorSceneManager.OpenScene("Assets/GameMain/Scenes/" + p.map + ".unity",OpenSceneMode.Additive);
                    try
                    {
                        SceneManager.SetActiveScene(scene);
                        var volumes = Components<TempleAtmosphereVolume>(scene);
                        if (volumes.Length != 1 || volumes[0].material == null || ShaderUtil.ShaderHasError(volumes[0].material.shader))
                            throw new InvalidDataException(p.map + " atmosphere or shader is invalid.");
                        if (RenderSettings.fog || volumes[0].enabled)
                            throw new InvalidDataException(p.map + " water haze must remain disabled.");
                        var post = Environment(scene,p).GetComponentInChildren<Volume>();
                        if (post == null || post.sharedProfile.components.Count != 5 || post.sharedProfile.components.Any(c => c == null))
                            throw new InvalidDataException(p.map + " profile is invalid.");
                        if (!post.sharedProfile.TryGet<ColorAdjustments>(out var colour) || colour.postExposure.value > 0 ||
                            !post.sharedProfile.TryGet<Bloom>(out var bloom) || bloom.intensity.value > .15f)
                            throw new InvalidDataException(p.map + " exposure is too strong.");
                        var sun = Components<Light>(scene).FirstOrDefault(l => l.type == LightType.Directional && l.cookie != null);
                        var cookie = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>(CausticsCookie);
                        if (sun == null || sun.cookie != cookie || RenderSettings.sun != sun || !Mathf.Approximately(sun.intensity,KeyLightIntensity) ||
                            cookie.updateMode != CustomRenderTextureUpdateMode.Realtime ||
                            sun.GetUniversalAdditionalLightData().lightCookieSize != new Vector2(15,15) ||
                            ShaderUtil.ShaderHasError(cookie.material.shader))
                            throw new InvalidDataException(p.map + " Map4 caustics cookie is invalid.");
                        foreach (Camera camera in Components<Camera>(scene))
                            if ((camera.isActiveAndEnabled && !camera.GetUniversalAdditionalCameraData().renderPostProcessing) ||
                                TempleAtmosphereVolume.TryGet(camera,out _)) throw new InvalidDataException(p.map + " camera is not configured.");
                        foreach (Transform t in Components<Transform>(scene))
                            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                                throw new InvalidDataException(p.map + " contains a missing script.");
                        lines.Add(p.map + ": PASS; water haze and distance fog disabled; Map4 animated light cookie (15x15), profile, camera, shader and scripts preserved. Colliders=" + Components<Collider>(scene).Length);
                    }
                    finally { if (opened) EditorSceneManager.CloseScene(scene,true); }
                }
                Scene wengjie = SceneManager.GetSceneByPath("Assets/GameMain/Scenes/wengjie.unity");
                bool openedWengjie = !wengjie.isLoaded;
                if (openedWengjie) wengjie = EditorSceneManager.OpenScene("Assets/GameMain/Scenes/wengjie.unity",OpenSceneMode.Additive);
                try
                {
                    SceneManager.SetActiveScene(wengjie);
                    var sun = Components<Light>(wengjie).FirstOrDefault(l => l.type == LightType.Directional && l.cookie != null);
                    if (sun == null || RenderSettings.sun != sun || !Mathf.Approximately(sun.intensity,KeyLightIntensity) ||
                        AssetDatabase.GetAssetPath(sun.cookie) != CausticsCookie ||
                        sun.GetUniversalAdditionalLightData().lightCookieSize != new Vector2(15,15) ||
                        Components<Transform>(wengjie).Any(t => t.name == "PAO_CausticsScreen" || t.name == "Wengjie_GroundCaustics"))
                        throw new InvalidDataException("Wengjie caustics are invalid.");
                    var post = Components<Volume>(wengjie).First(v => v.name == "Wengjie_PostFX");
                    if (AssetDatabase.GetAssetPath(post.sharedProfile) != AssetFolder + "/wengjie_SoftPostFX.asset" ||
                        !post.sharedProfile.TryGet<Bloom>(out var bloom) || bloom.intensity.value > .15f)
                        throw new InvalidDataException("Wengjie post-processing is invalid.");
                    if (Components<Collider>(wengjie).Length != 200 || Components<MonoBehaviour>(wengjie).Any(b => b == null))
                        throw new InvalidDataException("Wengjie collision/scripts are invalid.");
                    lines.Add("wengjie: PASS; reloaded Map4 cookie, scene-specific soft bloom, diffuse stone; legacy overlays removed. Colliders=200.");
                }
                finally { if (openedWengjie) EditorSceneManager.CloseScene(wengjie,true); }
                foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData",new[]{"Assets/Settings"}))
                {
                    var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (data.rendererFeatures.Count(f => f is TempleAtmosphereRenderFeature && f.isActive) != 1)
                        throw new InvalidDataException(data.name + " must contain one active atmosphere feature.");
                    lines.Add(data.name + ": PASS; exactly one active atmosphere feature.");
                }
                if (previous.IsValid() && previous.isLoaded && !Palettes.Any(p => p.map == previous.name))
                    foreach (Camera camera in Components<Camera>(previous))
                        if (TempleAtmosphereVolume.TryGet(camera,out _)) throw new InvalidDataException("Atmosphere leaked into " + previous.name);
                lines.Add("PASS: camera scope excludes scenes without a TempleAtmosphereVolume.");
                if (!PAOAudioIconVisibility.IconsHidden()) throw new InvalidDataException("Audio debug icons are visible.");
                lines.Add("PASS: AudioSource and AudioListener debug icons hidden; audio components are preserved.");
                Directory.CreateDirectory(OutputFolder);
                File.WriteAllLines(Path.Combine(OutputFolder,"verification.txt"),lines);
                Debug.Log("[Temple Atmosphere] Saved scene verification passed.");
            }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        [MenuItem("Tools/PAO/环境氛围/检查场景数据")]
        public static void Inspect()
        {
            Scene previous = SceneManager.GetActiveScene();
            var lines = new List<string>();
            foreach (Palette p in Palettes)
            {
                string path = "Assets/GameMain/Scenes/" + p.map + ".unity";
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                lines.Add(p.map);
                lines.Add("SUN " + (RenderSettings.sun != null ? RenderSettings.sun.name + " scene=" + RenderSettings.sun.gameObject.scene.name : "none"));
                foreach (Light light in Components<Light>(scene))
                    lines.Add("LIGHT " + light.name + " type=" + light.type + " intensity=" + light.intensity + " cookie=" + light.cookie);
                foreach (Camera c in Components<Camera>(scene)) lines.Add("CAMERA " + c.name + " position=" + c.transform.position + " forward=" + c.transform.forward + " player=" + c.transform.root.position);
                foreach (var b in Components<MonoBehaviour>(scene).Where(b => b != null && (b.GetType().Name.Contains("Goal") || b.GetType().Name.Contains("PlayerController") || b.GetType().Name == "tiaozhuan")))
                    lines.Add(b.GetType().Name + " " + b.name + " " + b.transform.position);
                foreach (Renderer r in Components<Renderer>(scene).Where(r => r.enabled && r is MeshRenderer).Take(12))
                    lines.Add("MATERIAL " + r.name + " " + string.Join(",", r.sharedMaterials.Where(m => m != null).Select(m => m.name + ":" + m.shader.name)));
                if (opened) EditorSceneManager.CloseScene(scene,true);
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            var cookie = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>(CausticsCookie);
            lines.Add("COOKIE created=" + cookie.IsCreated() + " realtime=" + cookie.updateMode + " shaderErrors=" + ShaderUtil.ShaderHasError(cookie.material.shader));
            if (cookie.IsCreated())
            {
                var active = RenderTexture.active;
                var texture = new Texture2D(cookie.width,cookie.height,TextureFormat.RGB24,false);
                try
                {
                    RenderTexture.active = cookie;
                    texture.ReadPixels(new Rect(0,0,cookie.width,cookie.height),0,0); texture.Apply();
                    File.WriteAllBytes("Temp/Map4CausticsCookie.png",texture.EncodeToPNG());
                }
                finally { RenderTexture.active = active; Object.DestroyImmediate(texture); }
            }
            File.WriteAllLines("Temp/TempleAtmosphereInspect.txt",lines);
        }

        static void ProcessAll(bool build)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before editing the atmosphere.");
            Scene previous = SceneManager.GetActiveScene();
            var opened = new List<Scene>();
            try
            {
                EnsureFolder(AssetFolder);
                if (build) InstallRenderFeature();
                Directory.CreateDirectory(OutputFolder);
                foreach (Palette palette in Palettes)
                {
                    string path = "Assets/GameMain/Scenes/" + palette.map + ".unity";
                    Scene scene = SceneManager.GetSceneByPath(path);
                    if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(scene); }
                    SceneManager.SetActiveScene(scene);
                    if (build) Build(scene, palette);
                    else CaptureViews(scene, palette, "after");
                    if (opened.Contains(scene) && !scene.isDirty)
                    {
                        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                        EditorSceneManager.CloseScene(scene,true);
                        opened.Remove(scene);
                    }
                }
                if (build) AssetDatabase.SaveAssets();
                Debug.Log("[Temple Atmosphere] All three scenes completed. Previews: " + Path.GetFullPath(OutputFolder));
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                foreach (Scene scene in opened) if (scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                if (Palettes.Any(p => p.map == previous.name) && SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.sceneLighting = true;
                    SceneView.lastActiveSceneView.sceneViewState.showFog = true;
                    SceneView.lastActiveSceneView.sceneViewState.showImageEffects = true;
                    var environment = previous.GetRootGameObjects().FirstOrDefault(g => g.name == previous.name + "_Environment");
                    if (environment != null) SceneVisibilityManager.instance.Show(environment,true);
                }
                SceneView.RepaintAll();
            }
        }

        static T[] Components<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        static GameObject Environment(Scene scene, Palette p)
        {
            return scene.GetRootGameObjects().FirstOrDefault(g => g.name == p.map + "_Environment");
        }

        static void Build(Scene scene, Palette p)
        {
            string backupDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../PAO-EnvironmentBackups"));
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir, p.map + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
            if (scene.isDirty) { if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up " + p.map); }
            else File.Copy(scene.path, backup);

            if (Environment(scene, p) == null) CaptureViews(scene, p, "before");
            var colliderState = Components<Collider>(scene).ToDictionary(c => c, c => c.enabled);
            var gameplay = Components<MonoBehaviour>(scene).Where(b => IsGameplay(b, p)).ToArray();
            var gameplayPoses = gameplay.Select(b => (behaviour: b, b.transform.position, b.transform.rotation, b.transform.lossyScale, b.enabled)).ToArray();
            GameObject old = Environment(scene, p);
            if (old != null) Object.DestroyImmediate(old);
            foreach (Camera camera in Components<Camera>(scene))
            {
                Transform existing = camera.transform.Find(CausticsName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                existing = camera.transform.Find(HazeName);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
            }

            var level = scene.GetRootGameObjects().FirstOrDefault(g => g.name == p.map + "_Whitebox");
            var renderers = (level != null ? level.GetComponentsInChildren<Renderer>(true) : Components<Renderer>(scene))
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) &&
                    !InEnvironment(r.transform,p) && r.name != "Bounds_proxy" && !r.transform.root.name.Contains("takoyaki")).ToArray();
            if (renderers.Length == 0) throw new InvalidDataException("No visible level geometry in " + p.map);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float waterY = bounds.max.y + 14f;
            float halfSize = Mathf.Max(bounds.extents.x, bounds.extents.z) + 180f;

            GameObject root = new GameObject(p.map + "_Environment");
            SceneManager.MoveGameObjectToScene(root, scene);
            CreateWater(root.transform, p, bounds, waterY, halfSize);
            CreatePostFX(root.transform, p);
            CreateLighting(root.transform, scene, p);
            CreateParticles(root.transform, p);
            ApplyRenderSettings(p);
            var atmosphere = root.AddComponent<TempleAtmosphereVolume>();
            atmosphere.material = CreateHazeMaterial(p);
            atmosphere.enabled = false;
            Camera[] cameras = Components<Camera>(scene).Where(c => c.cameraType == CameraType.Game).ToArray();
            foreach (Camera camera in cameras)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = p.fog;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.requiresDepthTexture = true;
                data.volumeLayerMask |= 1;
            }
            if (cameras.Length == 0) throw new InvalidDataException("No gameplay camera in " + p.map);

            // Visual dressing must leave every authored collision and gameplay pose intact.
            foreach (var pair in colliderState)
                if (pair.Key == null || pair.Key.enabled != pair.Value) throw new InvalidOperationException("Collision changed during atmosphere build.");
            foreach (var pose in gameplayPoses)
                if (pose.behaviour == null || pose.behaviour.transform.position != pose.position || pose.behaviour.transform.rotation != pose.rotation ||
                    pose.behaviour.transform.lossyScale != pose.lossyScale || pose.behaviour.enabled != pose.enabled)
                    throw new InvalidOperationException("Gameplay transform changed during atmosphere build: " + pose.behaviour);
            int collidersAfter = Components<Collider>(scene).Length;
            if (collidersAfter != colliderState.Count) throw new InvalidOperationException("Atmosphere added a collider.");

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
            string[] screenshots = CaptureViews(scene, p, "after");
            int missing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var receipt = new Receipt {
                scene = scene.path, mood = p.mood, backup = backup, utc = DateTime.UtcNow.ToString("O"),
                boundsCenter = bounds.center, boundsSize = bounds.size, waterHeight = waterY, fogDensity = p.density,
                collidersBefore = colliderState.Count, collidersAfter = collidersAfter,
                gameplayBehavioursBefore = gameplay.Length,
                gameplayBehavioursAfter = Components<MonoBehaviour>(scene).Count(b => IsGameplay(b, p)),
                cameras = cameras.Length, lights = root.GetComponentsInChildren<Light>().Length,
                particleSystems = root.GetComponentsInChildren<ParticleSystem>().Length, missingScripts = missing, screenshots = screenshots
            };
            File.WriteAllText(Path.Combine(OutputFolder, p.map + "_receipt.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("[Temple Atmosphere] Saved " + p.map + ": " + p.mood + ". Colliders preserved: " + collidersAfter);
        }

        static bool InEnvironment(Transform t, Palette p)
        {
            return t.root.name == p.map + "_Environment" || t.name == CausticsName || t.name == HazeName;
        }

        static bool IsGameplay(MonoBehaviour behaviour, Palette p)
        {
            return behaviour != null && !InEnvironment(behaviour.transform, p) &&
                behaviour.GetComponent<Light>() == null && behaviour.GetComponent<Camera>() == null;
        }

        static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            return go;
        }

        static Material CopyMaterial(string source, string map, string suffix)
        {
            string path = AssetFolder + "/" + map + "_" + suffix + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Material original = AssetDatabase.LoadAssetAtPath<Material>(source);
                if (original == null) throw new FileNotFoundException("Missing atmosphere material", source);
                mat = new Material(original) { name = map + "_" + suffix };
                AssetDatabase.CreateAsset(mat, path);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void CreateWater(Transform root, Palette p, Bounds bounds, float waterY, float halfSize)
        {
            Material mat = CopyMaterial(WaterSource, p.map, "WaterCeiling");
            ApplyWaterPalette(mat,p);
            mat.SetFloat("_FadeStart", halfSize * .3f);
            mat.SetFloat("_FadeEnd", halfSize * .92f);
            var water = Child(root, "Water_Ceiling");
            water.transform.position = new Vector3(bounds.center.x, waterY, bounds.center.z);
            water.AddComponent<MeshFilter>();
            water.AddComponent<MeshRenderer>().sharedMaterial = mat;
            water.AddComponent<UnderwaterCeiling>().Configure(halfSize, bounds.size.y + 220f);
        }

        static void ApplyWaterPalette(Material mat, Palette p)
        {
            mat.SetColor("_DeepColor", p.fog);
            mat.SetColor("_ShallowColor", p.shallow);
            mat.SetColor("_HorizonColor", p.fog);
            mat.SetColor("_SkirtBottomColor", p.fog);
            mat.SetColor("_LightColor", new Color(.91f,.97f,1f));
            mat.SetColor("_RippleColor", new Color(.93f,.98f,1f));
            mat.SetColor("_SurfaceColor", p.shallow);
            mat.SetFloat("_RippleAlpha", .28f);
            mat.SetFloat("_RippleStrength", .32f);
            mat.SetFloat("_RippleSpeed", .55f);
            mat.SetFloat("_FoamStrength", .08f);
            EditorUtility.SetDirty(mat);
        }

        static T Effect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out T existing)) return existing;
            T effect = profile.Add<T>(true);
            effect.name = typeof(T).Name;
            effect.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            AssetDatabase.AddObjectToAsset(effect, profile);
            return effect;
        }

        static void CreatePostFX(Transform root, Palette p)
        {
            string path = AssetFolder + "/" + p.map + "_AtmosphereProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            ConfigurePostFX(profile,p);
            var volume = Child(root, "Atmosphere_PostFX").AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 20; volume.sharedProfile = profile;
        }

        static void ConfigurePostFX(VolumeProfile profile, Palette p)
        {
            Effect<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = Effect<Bloom>(profile);
            bloom.threshold.Override(1.35f); bloom.intensity.Override(p.bloomIntensity); bloom.scatter.Override(.72f);
            var color = Effect<ColorAdjustments>(profile);
            color.postExposure.Override(p.exposure); color.contrast.Override(p.contrast); color.saturation.Override(-2f);
            var balance = Effect<WhiteBalance>(profile);
            balance.temperature.Override(p.temperature); balance.tint.Override(1f);
            var vignette = Effect<Vignette>(profile);
            vignette.intensity.Override(0f); vignette.smoothness.Override(.48f);
            vignette.color.Override(new Color(.025f,.05f,.10f));
            foreach (VolumeComponent effect in profile.components) EditorUtility.SetDirty(effect);
            EditorUtility.SetDirty(profile);
        }

        static void ApplyRenderSettings(Palette p)
        {
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fog;
            RenderSettings.fogDensity = p.density;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            RenderSettings.ambientSkyColor = p.ambient;
            RenderSettings.ambientEquatorColor = p.ambient * .9f;
            RenderSettings.ambientGroundColor = p.ambient * .75f;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = .30f;
            RenderSettings.skybox = null;
            SetAmbientProbe(p);
        }

        static void SetAmbientProbe(Palette p)
        {
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(p.ambient.linear);
            RenderSettings.ambientProbe = probe;
        }

        static Light LightAt(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = Child(parent, name).AddComponent<Light>();
            light.transform.position = position;
            light.type = LightType.Point; light.color = color; light.intensity = intensity;
            light.range = range; light.shadows = LightShadows.None;
            return light;
        }

        static void CreateLighting(Transform root, Scene scene, Palette p)
        {
            Light sun = Components<Light>(scene).FirstOrDefault(l => l.type == LightType.Directional && !l.transform.IsChildOf(root));
            if (sun == null) { sun = Child(root, "Temple_KeyLight").AddComponent<Light>(); sun.type = LightType.Directional; }
            ConfigureCausticLight(sun);
            sun.color = p.keyColor; sun.intensity = p.keyIntensity; sun.shadowStrength = p.keyShadowStrength;
            var fill = Child(root, "Temple_BlueFill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = p.blueFillColor;
            fill.intensity = p.blueFillIntensity; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(30,140,0);
            CreateUnderFill(root,p);
            LightAt(root, "Entrance_SoftLight", p.entry + new Vector3(0,5,-3), p.shallow, 4f, 30f);
            LightAt(root, "Goal_AccentLight", p.goal + new Vector3(0,5,2), p.accent, 5.5f, 28f);
            for (int i = 1; i <= 3; i++)
                LightAt(root, "Hall_IndirectLight_" + i, Vector3.Lerp(p.entry,p.goal,i/4f) + new Vector3(i%2==0?-5:5,7,0),
                    Color.Lerp(p.shallow,Color.white,.3f), 2.5f, 26f);
        }

        static void CreateUnderFill(Transform root, Palette p)
        {
            var child = root.Find("Temple_UnderFill");
            Light fill = child != null ? child.GetComponent<Light>() : Child(root,"Temple_UnderFill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = p.underFillIntensity; fill.color = new Color(1f,.97f,.90f);
            fill.enabled = p.underFillEnabled;
            fill.shadows = LightShadows.None; fill.transform.rotation = Quaternion.Euler(-35,-40,0);
        }

        static void CreateParticles(Transform root, Palette p)
        {
            GameObject dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DustPrefab);
            GameObject shaftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShaftsPrefab);
            if (dustPrefab == null || shaftPrefab == null) throw new FileNotFoundException("Missing underwater particle prefabs.");
            Material shaftMaterial = CopyMaterial(ShaftsSource, p.map, "Sunshafts");
            shaftMaterial.SetColor("_BaseColor", new Color(p.shallow.r,p.shallow.g,p.shallow.b,.10f));
            shaftMaterial.SetFloat("_Intensity", 1.0f);
            shaftMaterial.SetFloat("_FarFadeStart", 65f); shaftMaterial.SetFloat("_FarFadeEnd", 125f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 point = Vector3.Lerp(p.entry,p.goal,i/4f);
                var dust = (GameObject)PrefabUtility.InstantiatePrefab(dustPrefab, root.gameObject.scene);
                dust.name = "Water_Motes_" + i; dust.transform.SetParent(root,false);
                dust.transform.position = point + Vector3.up * 4f;
                foreach (ParticleSystem ps in dust.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.maxParticles = 100; main.startSize = new ParticleSystem.MinMaxCurve(.025f,.075f);
                    main.startColor = new Color(.72f,.89f,.94f,.24f);
                    main.startSpeed = .06f; main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.cullingMode = ParticleSystemCullingMode.Automatic;
                    var emission = ps.emission; emission.rateOverTime = 2f;
                    var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(20,12,30);
                }
                if (i == 0 || i == 2 || i == 4)
                {
                    var shaft = (GameObject)PrefabUtility.InstantiatePrefab(shaftPrefab, root.gameObject.scene);
                    shaft.name = "Water_LightShaft_" + i; shaft.transform.SetParent(root,false);
                    shaft.transform.position = point + new Vector3(3,14,-9);
                    shaft.transform.rotation = Quaternion.Euler(0,0,-14);
                    foreach (ParticleSystem ps in shaft.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main; main.maxParticles = 20;
                        var shape = ps.shape; shape.scale = new Vector3(12,1,14);
                        var emission = ps.emission; emission.rateOverTime = .6f;
                    }
                    foreach (Renderer renderer in shaft.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = shaftMaterial;
                }
            }
        }

        static Material CreateHazeMaterial(Palette p)
        {
            string path = AssetFolder + "/" + p.map + "_WaterHaze.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("PAO/TempleWaterHaze");
                if (shader == null) throw new InvalidDataException("Water haze shader was not imported.");
                mat = new Material(shader); AssetDatabase.CreateAsset(mat,path);
            }
            mat.SetColor("_HazeColor",p.fog);
            mat.SetColor("_SkyColor",Color.Lerp(p.fog,new Color(.60f,.71f,.82f),.42f));
            mat.SetFloat("_Density",p.map == "Map2_5" ? .014f : .017f);
            mat.SetFloat("_NearClear",2.5f); mat.SetFloat("_MaxOpacity",.88f);
            // Remove settings from the superseded procedural caustics pass.
            var saved = new SerializedObject(mat);
            var floats = saved.FindProperty("m_SavedProperties.m_Floats");
            for (int i=floats.arraySize-1;i>=0;i--)
            {
                string key = floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                if (key == "_Caustics" || key == "_WaterY") floats.DeleteArrayElementAtIndex(i);
            }
            saved.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static string[] CaptureViews(Scene scene, Palette p, string stage)
        {
            if (stage == "after") SetAmbientProbe(p);
            var result = new List<string>();
            Camera source = Components<Camera>(scene).FirstOrDefault(c => c.enabled);
            Vector3 middle = Vector3.Lerp(p.entry,p.goal,.45f);
            Vector3[] positions = { p.entry + new Vector3(0,3,2), middle + new Vector3(-3,6,9), p.goal + new Vector3(-3,5,17), p.entry + new Vector3(-4,12,-7) };
            Vector3[] targets = { p.entry + new Vector3(0,2,-24), middle + new Vector3(2,2,-20), p.goal + Vector3.up*2, p.entry + new Vector3(0,0,-18) };
            if (p.map == "Map2")
            {
                positions[1] = new Vector3(-12,9,-59); targets[1] = new Vector3(5,3,-82);
            }
            if (p.map == "Map3")
            {
                positions[2] = new Vector3(-7,8,-50); targets[2] = new Vector3(0,-3,-75);
            }
            string[] names = { "entrance", "hall", "goal", "detail" };
            foreach (ParticleSystem ps in Components<ParticleSystem>(scene).Where(ps => ps.transform.root.name == p.map + "_Environment"))
            { ps.useAutoRandomSeed = false; ps.randomSeed = 71; ps.Simulate(14f,true,true,true); }
            for (int i=0;i<positions.Length;i++)
            {
                string path = Path.Combine(OutputFolder,p.map + "_" + stage + "_" + names[i] + ".png");
                Capture(scene,p,source,positions[i],targets[i],path,stage == "after"); result.Add(path);
            }
            if (source != null && p.map != "Map2")
            {
                string path = Path.Combine(OutputFolder,p.map + "_" + stage + "_gameplay.png");
                Capture(scene,p,source,source.transform.position,source.transform.position + source.transform.forward * 20f,path,stage == "after");
                result.Add(path);
            }
            return result.ToArray();
        }

        static void Capture(Scene scene, Palette p, Camera source, Vector3 position, Vector3 target, string path, bool after)
        {
            Directory.CreateDirectory(OutputFolder);
            var go = new GameObject("__TempleAtmosphereCapture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go,scene);
            RenderTexture rt = null; Texture2D texture = null; RenderTexture previous = RenderTexture.active;
            // The existing SDF bubble registry spans additive scenes and bypasses camera layer culling.
            // Exclude other loaded maps from this offscreen preview without changing their objects.
            var outsideBubbles = Object.FindObjectsOfType<PAO.BubbleFX.SDFBubble>()
                .Where(b => b.gameObject.scene != scene && b.isActiveAndEnabled).ToArray();
            var registry = typeof(PAO.BubbleFX.BubbleSDFManager);
            var unregister = registry.GetMethod("Unregister",System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var register = registry.GetMethod("Register",System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var outsideLights = Object.FindObjectsOfType<Light>().Where(l => l.gameObject.scene != scene && l.enabled).ToArray();
            var outsideVolumes = Object.FindObjectsOfType<Volume>().Where(v => v.gameObject.scene != scene && v.enabled).ToArray();
            var outsideRenderers = Object.FindObjectsOfType<Renderer>().Where(r => r.gameObject.scene != scene && r.enabled).ToArray();
            foreach (var bubble in outsideBubbles) unregister.Invoke(null,new object[]{bubble});
            try
            {
                // Additive editor scenes also contribute lights and global post-processing volumes.
                foreach (var light in outsideLights) light.enabled = false;
                foreach (var volume in outsideVolumes) volume.enabled = false;
                foreach (var renderer in outsideRenderers) renderer.enabled = false;
                Light sun = Components<Light>(scene).FirstOrDefault(l => l.type == LightType.Directional && l.cookie != null);
                if (after && sun != null) RenderSettings.sun = sun;
                var camera = go.AddComponent<Camera>();
                if (source != null) camera.CopyFrom(source);
                camera.enabled = false;
                camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position));
                camera.fieldOfView = 65f; camera.nearClipPlane = .15f; camera.farClipPlane = 450f;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.scene = scene;
                camera.allowHDR = true;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = after; data.requiresDepthTexture = true;
                data.volumeLayerMask = 1;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                if (after) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = p.fog; }
                rt = new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture = new Texture2D(1600,900,TextureFormat.RGB24,false,false);
                texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply();
                File.WriteAllBytes(path,texture.EncodeToPNG());
                camera.targetTexture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (texture != null) Object.DestroyImmediate(texture);
                Object.DestroyImmediate(go);
                foreach (var light in outsideLights) if (light != null) light.enabled = true;
                foreach (var volume in outsideVolumes) if (volume != null) volume.enabled = true;
                foreach (var renderer in outsideRenderers) if (renderer != null) renderer.enabled = true;
                foreach (var bubble in outsideBubbles) if (bubble != null && bubble.isActiveAndEnabled) register.Invoke(null,new object[]{bubble});
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }

        static void InstallRenderFeature()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData",new[]{"Assets/Settings"}))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data.rendererFeatures.Any(f => f is TempleAtmosphereRenderFeature)) continue;
                var feature = ScriptableObject.CreateInstance<TempleAtmosphereRenderFeature>();
                feature.name = "Temple Atmosphere";
                AssetDatabase.AddObjectToAsset(feature,data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature,out string _,out long localId);
                var serialized = new SerializedObject(data);
                var features = serialized.FindProperty("m_RendererFeatures");
                var map = serialized.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize-1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize-1).longValue = localId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                data.SetDirty(); EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
