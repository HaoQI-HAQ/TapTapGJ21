using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using PAO.Underwater;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Map1「水域 02」环境渲染（粉色水下）。
    ///
    /// 现状：Map1 只有 Directional Light + takoyaki + Cube + 关卡白盒几何，
    /// RenderSettings 是默认值（雾关、默认天空盒、无后处理），没有任何水面/焦散/体积光。
    ///
    /// 这个工具按 wengjie / Map0 的水下配方做一套 Map1 专用的粉色水下环境（幂等，可重复执行）：
    ///   1. Water_Ceiling（UnderwaterCeiling 水面壳）+ 粉色水面材质副本；
    ///   2. 全局 Volume：ACES + Bloom + Vignette + ColorAdjustments + WhiteBalance（粉色水下调色）；
    ///   3. RenderSettings：粉紫雾 + 低环境光 + 相机深色底；
    ///   4. 天上 3 个 Sunshafts (Area) 光柱 + 4 处 Ambient 浮尘；
    ///   5. 相机前 PAO_CausticsScreen 全屏焦散 + Map1 自己的材质副本（_WaterY 对齐水面）；
    ///   6. 在 T1 珍珠 / 出口等关键点补 3~4 盏点光，保证白盒可读；
    ///   7. 主光调成暖白、软阴影。
    ///
    /// 菜单：
    ///   Tools/PAO/关卡白盒/Map1 环境渲染 · 粉色水下（一键搭建）
    ///   Tools/PAO/关卡白盒/Map1 环境渲染 · 截图
    ///   Tools/PAO/关卡白盒/Map1 环境渲染 · 移除环境
    /// </summary>
    public static class Map1EnvironmentSetup
    {
        private const string kScenePath   = "Assets/GameMain/Scenes/Map1.unity";
        private const string kEnvRoot     = "Map1_Environment";
        private const string kWaterName   = "Water_Ceiling";
        private const string kCausticsObj = "PAO_CausticsScreen";
        private const string kShotDir     = "Docs/Map1-env/shots";

        private const string kWaterMatPath     = "Assets/GameMain/Art/Materials/M_Map1_UnderwaterCeiling.mat";
        private const string kSrcWaterMatPath  = "Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat";
        private const string kCausticsMatPath  = "Assets/GameMain/Art/Materials/M_Map1_CausticsScreen.mat";
        private const string kSrcCausticsMat   = "Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat";
        private const string kProfilePath      = "Assets/GameMain/Art/Profiles/Map1_PinkWaterProfile.asset";

        private const string kSunshaftPrefab = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab";
        private const string kDustPrefab     = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Ambient Particles.prefab";
        private const string kSunshaftMatPath = "Assets/GameMain/Art/Materials/M_Map1_Sunshafts.mat";
        private const string kSrcSunshaftMat  = "Assets/ThirdParty/Thalassophobia/Contents/Materials/Sunshafts.mat";

        // ---------- 调参入口 ----------

        // 粉色水下配色（v8：收曝光——v7 太亮过曝，压回一档，保留粉雾层次）
        private static readonly Color kFogColor        = new Color(0.72f, 0.58f, 0.65f, 1f);
        private const float kFogDensity                = 0.008f;
        private static readonly Color kAmbientColor    = new Color(0.62f, 0.54f, 0.60f, 1f);
        private const float kAmbientIntensity          = 1.05f;
        private static readonly Color kCameraBackground = new Color(0.72f, 0.58f, 0.65f, 1f);

        // 主光 + 冷色补光
        private static readonly Color kSunColor   = new Color(1f, 0.94f, 0.89f, 1f);
        private const float kSunIntensity         = 1.45f;
        private const float kSunShadowStrength    = 0.80f;
        private static readonly Vector3 kSunEuler = new Vector3(58f, 330f, 0f);

        private static readonly Color kFillColor = new Color(0.72f, 0.78f, 0.98f, 1f);
        private const float kFillIntensity       = 0.42f;
        private static readonly Vector3 kFillEuler = new Vector3(65f, 140f, 0f);

        // 水面材质（粉色，比 v7 收一点亮度）
        private static readonly Color kWaterDeep    = new Color(0.45f, 0.30f, 0.37f, 1f);
        private static readonly Color kWaterShallow = new Color(0.92f, 0.68f, 0.74f, 1f);
        private static readonly Color kWaterLight   = new Color(1f, 0.94f, 0.93f, 1f);
        private static readonly Color kWaterRipple  = new Color(1f, 0.93f, 0.93f, 1f);
        private static readonly Color kWaterFoam    = new Color(1f, 0.96f, 0.95f, 1f);

        // 焦散（保持克制）
        private static readonly Color kCausticColor = new Color(1f, 0.94f, 0.95f, 1f);
        private const float kCausticIntensity  = 0.06f;
        private const float kCausticLineWidth  = 0.04f;
        private const float kCausticScale      = 0.30f;
        private const float kCausticSpeed      = 0.60f;
        private const float kCausticFadeStart  = 50f;
        private const float kCausticFadeEnd    = 140f;

        // 后处理（正常曝光，微饱和）
        private const float kPostExposure = 0.15f;
        private const float kContrast     = 10f;
        private const float kSaturation   = 3f;
        private const float kWhiteBalanceTemperature = 3f;
        private const float kWhiteBalanceTint        = 1f;
        private const float kBloomThreshold = 1.00f;
        private const float kBloomIntensity = 0.60f;
        private const float kBloomScatter   = 0.70f;
        private const float kVignetteIntensity  = 0.18f;
        private const float kVignetteSmoothness = 0.45f;

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/Map1 环境渲染 · 粉色水下（一键搭建）", priority = 50)]
        public static void BuildPinkWater()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map1 环境] 退出 Play 再改场景。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath)
            {
                Debug.LogWarning("[Map1 环境] 当前不是 Map1（当前：" + scene.path + "）。");
                return;
            }

            RemoveEnvironment(quiet: true);

            Bounds bounds = ComputeLevelBounds();
            float waterY = bounds.max.y + 12f;
            float halfSize = Mathf.Max(Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.4f, 120f) + 160f;
            float skirtDepth = Mathf.Clamp(bounds.size.y + 60f, 80f, 400f);

            var root = new GameObject(kEnvRoot);
            root.transform.position = Vector3.zero;

            CreateWaterCeiling(root.transform, bounds, waterY, halfSize, skirtDepth);
            CreatePostFx(root.transform);
            CreateSunshafts(root.transform, bounds, waterY);
            CreateDust(root.transform, bounds);
            CreatePointLights(root.transform);
            CreateFillLight(root.transform);

            ApplyRenderSettings();
            ApplySunLight();
            ApplyCamera();
            CreateCausticsScreen(waterY);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, kScenePath);

            Debug.Log(
                "[Map1 环境] 粉色水下环境已搭建并保存。\n" +
                "  关卡包围盒: center=" + bounds.center + " size=" + bounds.size + "\n" +
                "  水面 Y=" + waterY + "  HalfSize=" + halfSize + "  Skirt=" + skirtDepth + "\n" +
                "  雾: " + kFogColor + " density=" + kFogDensity + " (Exp2)\n" +
                "  环境光: " + kAmbientColor + " × " + kAmbientIntensity + "\n" +
                "  主光: intensity=" + kSunIntensity + " euler=" + kSunEuler + "\n" +
                "  Profile: " + kProfilePath + "\n" +
                "  材质副本: " + kWaterMatPath + " / " + kCausticsMatPath);
        }

        [MenuItem("Tools/PAO/关卡白盒/Map1 环境渲染 · 移除环境", priority = 52)]
        public static void RemoveEnvironmentMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map1 环境] 退出 Play 再改场景。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath) { Debug.LogWarning("[Map1 环境] 当前不是 Map1。"); return; }
            RemoveEnvironment(quiet: false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, kScenePath);
        }

        [MenuItem("Tools/PAO/关卡白盒/Map1 环境渲染 · 截图", priority = 51)]
        public static void CaptureViews()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map1 环境] 退出 Play 再截图。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath) { Debug.LogWarning("[Map1 环境] 当前不是 Map1。"); return; }

            Directory.CreateDirectory(kShotDir);
            foreach (string old in Directory.GetFiles(kShotDir, "*.png"))
                File.Delete(old);

            Bounds bounds = ComputeLevelBounds();
            float waterY = bounds.max.y + 12f;

            Vector3 playerPos = Camera.main != null ? Camera.main.transform.position : bounds.center;
            Quaternion playerRot = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

            Capture("01_player", playerPos, playerRot);

            Vector3 center = bounds.center;
            Vector3 ext = bounds.extents;
            Vector3 eye = new Vector3(center.x, bounds.min.y + Mathf.Clamp(bounds.size.y * 0.25f, 2.5f, 8f), center.z);

            // 与 before 完全同机位的 8 张（改前改后可逐张对照）
            Capture("02_center_forward", eye - new Vector3(0f, 0f, Mathf.Max(ext.z * 0.55f, 12f)), Quaternion.Euler(8f, 0f, 0f));
            Capture("03_center_back",    eye + new Vector3(0f, 0f, Mathf.Max(ext.z * 0.55f, 12f)), Quaternion.Euler(8f, 180f, 0f));
            Capture("04_across_x",       eye - new Vector3(Mathf.Max(ext.x * 0.55f, 12f), 0f, 0f), Quaternion.Euler(8f, 90f, 0f));
            Capture("05_overview",       new Vector3(center.x, Mathf.Min(waterY - 4f, bounds.max.y + 8f), center.z - Mathf.Max(ext.z * 0.9f, 25f)), Quaternion.Euler(32f, 0f, 0f));
            Capture("06_look_up_water",  eye, Quaternion.Euler(-55f, 0f, 0f));

            Transform pearl = FindDeep(scene, "T1_Central_Pearl_Terrain_Bubble_Lock");
            if (pearl != null)
                Capture("07_pearl", pearl.position + new Vector3(0f, 2.5f, 10f), LookAt(pearl.position + new Vector3(0f, 1f, 0f), pearl.position + new Vector3(0f, 2.5f, 10f)));

            Transform exit = FindDeep(scene, "Cube");
            if (exit != null)
                Capture("08_exit", exit.position + new Vector3(0f, 3.5f, 12f), LookAt(exit.position + new Vector3(0f, 1f, 0f), exit.position + new Vector3(0f, 3.5f, 12f)));

            // 额外的高机位，用来看环境整体（水面下 6 米俯拍）
            float highY = waterY - 6f;
            Capture("09_high_forward", new Vector3(center.x, highY, center.z - ext.z * 0.35f), Quaternion.Euler(25f, 0f, 0f));
            Capture("10_high_back",    new Vector3(center.x, highY, center.z + ext.z * 0.35f), Quaternion.Euler(25f, 180f, 0f));
            Capture("11_high_side",    new Vector3(center.x - ext.x * 0.35f, highY, center.z), Quaternion.Euler(25f, 90f, 0f));

            Debug.Log("[Map1 环境] 截图完成 -> " + Path.GetFullPath(kShotDir));
        }

        // ==================== 环境内容 ====================

        private static void CreateWaterCeiling(Transform parent, Bounds bounds, float waterY, float halfSize, float skirtDepth)
        {
            Material mat = EnsureWaterMaterial(halfSize);
            var go = new GameObject(kWaterName);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(bounds.center.x, waterY, bounds.center.z);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = null;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            var ceiling = go.AddComponent<UnderwaterCeiling>();
            ceiling.Configure(halfSize, skirtDepth);
        }

        private static Material EnsureWaterMaterial(float halfSize)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(kWaterMatPath);
            if (mat == null)
            {
                AssetDatabase.CopyAsset(kSrcWaterMatPath, kWaterMatPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(kWaterMatPath);
            }
            if (mat == null)
            {
                Debug.LogError("[Map1 环境] 复制水面材质失败：" + kSrcWaterMatPath);
                return null;
            }

            mat.SetFloat("_RippleAlpha", 0.50f);
            mat.SetFloat("_RippleStrength", 0.35f);
            mat.SetFloat("_FoamStrength", 0.12f);
            mat.SetColor("_DeepColor", kWaterDeep);
            mat.SetColor("_ShallowColor", kWaterShallow);
            mat.SetColor("_LightColor", kWaterLight);
            mat.SetColor("_RippleColor", kWaterRipple);
            mat.SetColor("_FoamColor", kWaterFoam);
            mat.SetFloat("_FadeStart", halfSize * 0.28f);
            mat.SetFloat("_FadeEnd", halfSize * 0.95f);
            mat.SetFloat("_RippleFadeStart", halfSize * 0.20f);
            mat.SetFloat("_RippleFadeEnd", halfSize * 0.65f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void CreatePostFx(Transform parent)
        {
            VolumeProfile profile = EnsurePinkProfile();
            var go = new GameObject("Map1_PinkPostFX");
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        private static VolumeProfile EnsurePinkProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(kProfilePath);
            bool usable = profile != null && profile.components != null && profile.components.Count >= 5;
            if (usable)
            {
                for (int i = 0; i < profile.components.Count; i++)
                    if (profile.components[i] == null) usable = false;
            }

            if (!usable)
            {
                if (profile != null) AssetDatabase.DeleteAsset(kProfilePath);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, kProfilePath);
                AddEffect<Tonemapping>(profile);
                AddEffect<Bloom>(profile);
                AddEffect<Vignette>(profile);
                AddEffect<ColorAdjustments>(profile);
                AddEffect<WhiteBalance>(profile);
                AddEffect<ShadowsMidtonesHighlights>(profile);
                AssetDatabase.SaveAssets();
            }

            if (!profile.TryGet<ShadowsMidtonesHighlights>(out _))
                AddEffect<ShadowsMidtonesHighlights>(profile);

            if (profile.TryGet<Tonemapping>(out Tonemapping tonemapping) && tonemapping != null)
                tonemapping.mode.Override(TonemappingMode.ACES);
            if (profile.TryGet<Bloom>(out Bloom bloom) && bloom != null)
            {
                bloom.threshold.Override(kBloomThreshold);
                bloom.intensity.Override(kBloomIntensity);
                bloom.scatter.Override(kBloomScatter);
                bloom.tint.Override(new Color(1f, 0.92f, 0.96f, 1f));
            }
            if (profile.TryGet<Vignette>(out Vignette vignette) && vignette != null)
            {
                vignette.intensity.Override(kVignetteIntensity);
                vignette.smoothness.Override(kVignetteSmoothness);
                vignette.color.Override(new Color(0.05f, 0.01f, 0.03f, 1f));
            }
            if (profile.TryGet<ColorAdjustments>(out ColorAdjustments color) && color != null)
            {
                color.postExposure.Override(kPostExposure);
                color.contrast.Override(kContrast);
                color.saturation.Override(kSaturation);
            }
            if (profile.TryGet<WhiteBalance>(out WhiteBalance whiteBalance) && whiteBalance != null)
            {
                whiteBalance.temperature.Override(kWhiteBalanceTemperature);
                whiteBalance.tint.Override(kWhiteBalanceTint);
            }

            // 暖高光 / 冷阴影：让粉岩和雾拉开色相层次
            if (profile.TryGet<ShadowsMidtonesHighlights>(out ShadowsMidtonesHighlights smh) && smh != null)
            {
                smh.shadows.Override(new Vector4(0.92f, 0.94f, 1.05f, 0f));
                smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
                smh.highlights.Override(new Vector4(1.06f, 1.01f, 0.97f, 0f));
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static T AddEffect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static void CreateSunshafts(Transform parent, Bounds bounds, float waterY)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kSunshaftPrefab);
            if (prefab == null) { Debug.LogWarning("[Map1 环境] 找不到 Sunshafts (Area) prefab。"); return; }

            Material mat = EnsureSunshaftMaterial();
            Camera cam = Camera.main;

            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            var spots = new List<Vector3>
            {
                new Vector3(c.x, waterY - 6f, c.z - e.z * 0.45f),
                new Vector3(c.x + e.x * 0.35f, waterY - 8f, c.z + e.z * 0.10f),
                new Vector3(c.x - e.x * 0.30f, waterY - 4f, c.z + e.z * 0.45f),
            };
            var scales = new List<float> { 28f, 32f, 24f };

            // 玩家正前方补一道大光柱：进 Play 第一眼就能看到体积光。
            if (cam != null)
            {
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
                float shaftY = waterY - 8f;
                spots.Add(cam.transform.position + forward * 26f + Vector3.up * (shaftY - cam.transform.position.y));
                scales.Add(40f);
            }

            for (int i = 0; i < spots.Count; i++)
            {
                GameObject shaft = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                shaft.name = "Map1_Sunshaft_" + i;
                SceneManager.MoveGameObjectToScene(shaft, SceneManager.GetActiveScene());
                shaft.transform.SetParent(parent, true);
                shaft.transform.position = spots[i];
                shaft.transform.rotation = Quaternion.identity;
                shaft.transform.localScale = Vector3.one * scales[i];
                DestroyColliders(shaft);

                if (mat != null)
                {
                    Renderer[] renderers = shaft.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++) renderers[r].sharedMaterial = mat;
                }
            }
        }

        private static Material EnsureSunshaftMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(kSunshaftMatPath);
            if (mat == null)
            {
                AssetDatabase.CopyAsset(kSrcSunshaftMat, kSunshaftMatPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(kSunshaftMatPath);
            }
            if (mat == null) { Debug.LogWarning("[Map1 环境] 复制 Sunshaft 材质失败。"); return null; }

            mat.SetColor("_BaseColor", new Color(1f, 0.86f, 0.90f, 0.30f));
            mat.SetFloat("_Intensity", 5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void CreateDust(Transform parent, Bounds bounds)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kDustPrefab);
            if (prefab == null) { Debug.LogWarning("[Map1 环境] 找不到 Ambient Particles prefab。"); return; }

            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            float y = bounds.min.y + Mathf.Clamp(bounds.size.y * 0.45f, 4f, 16f);
            Vector3[] spots =
            {
                new Vector3(c.x, y, c.z - e.z * 0.45f),
                new Vector3(c.x, y, c.z),
                new Vector3(c.x + e.x * 0.35f, y, c.z + e.z * 0.35f),
                new Vector3(c.x - e.x * 0.35f, y, c.z - e.z * 0.20f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                GameObject dust = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                dust.name = "Map1_Dust_" + i;
                SceneManager.MoveGameObjectToScene(dust, SceneManager.GetActiveScene());
                dust.transform.SetParent(parent, true);
                dust.transform.position = spots[i];
                dust.transform.localScale = Vector3.one * 1.5f;
            }
        }

        private static void CreatePointLights(Transform parent)
        {
            Scene scene = SceneManager.GetActiveScene();
            AddPointLight(parent, "Map1_Light_Pearl", FindDeep(scene, "T1_Central_Pearl_Terrain_Bubble_Lock"), new Color(1f, 0.82f, 0.68f, 1f), 14f, 30f, 3.0f);
            AddPointLight(parent, "Map1_Light_EastGoal", FindDeep(scene, "T1_East_Short_Goal_Pearl"), new Color(0.80f, 0.89f, 1f, 1f), 12f, 26f, 2.5f);
            AddPointLight(parent, "Map1_Light_WestPocket", FindDeep(scene, "T1_West_Optional_Pocket_Lock"), new Color(1f, 0.83f, 0.70f, 1f), 13f, 28f, 3.0f);
            AddPointLight(parent, "Map1_Light_Exit", FindDeep(scene, "Cube"), new Color(1f, 0.78f, 0.64f, 1f), 11f, 24f, 2.5f);
        }

        private static void AddPointLight(Transform parent, string name, Transform anchor, Color color, float intensity, float range, float heightOffset)
        {
            if (anchor == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = anchor.position + new Vector3(0f, heightOffset, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        /// <summary>冷色补光：从反方向把峡谷阴影里的粉岩提亮，避免大片死黑。</summary>
        private static void CreateFillLight(Transform parent)
        {
            var go = new GameObject("Map1_FillLight");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(kFillEuler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = kFillColor;
            light.intensity = kFillIntensity;
            light.shadows = LightShadows.None;
        }

        private static void CreateCausticsScreen(float waterY)
        {
            Camera cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Map1 环境] 找不到 Main Camera，跳过焦散。"); return; }

            Transform old = cam.transform.Find(kCausticsObj);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(kCausticsMatPath);
            if (mat == null)
            {
                AssetDatabase.CopyAsset(kSrcCausticsMat, kCausticsMatPath);
                mat = AssetDatabase.LoadAssetAtPath<Material>(kCausticsMatPath);
            }
            if (mat == null) { Debug.LogError("[Map1 环境] 复制焦散材质失败。"); return; }

            mat.SetFloat("_WaterY", waterY);
            mat.SetColor("_CausticColor", kCausticColor);
            mat.SetFloat("_Intensity", kCausticIntensity);
            mat.SetFloat("_LineWidth", kCausticLineWidth);
            mat.SetFloat("_Scale", kCausticScale);
            mat.SetFloat("_Speed", kCausticSpeed);
            mat.SetFloat("_FadeStart", kCausticFadeStart);
            mat.SetFloat("_FadeEnd", kCausticFadeEnd);
            EditorUtility.SetDirty(mat);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = kCausticsObj;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(cam.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(8f, 8f, 1f);
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static void ApplyRenderSettings()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = kFogColor;
            RenderSettings.fogDensity = kFogDensity;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = kAmbientColor;
            RenderSettings.ambientIntensity = kAmbientIntensity;
            RenderSettings.skybox = null;
        }

        private static void ApplySunLight()
        {
            GameObject sunGo = GameObject.Find("Directional Light");
            if (sunGo == null) { Debug.LogWarning("[Map1 环境] 找不到 Directional Light。"); return; }
            Light sun = sunGo.GetComponent<Light>();
            if (sun == null) return;
            sun.color = kSunColor;
            sun.intensity = kSunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = kSunShadowStrength;
            sun.transform.rotation = Quaternion.Euler(kSunEuler);
        }

        private static void ApplyCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = kCameraBackground;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = true;
        }

        private static void RemoveEnvironment(bool quiet)
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform old = FindDeep(scene, kEnvRoot);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Transform caustics = cam.transform.Find(kCausticsObj);
                if (caustics != null) Object.DestroyImmediate(caustics.gameObject);
            }
            if (!quiet) Debug.Log("[Map1 环境] 已移除环境根节点与焦散面片。");
        }

        // ==================== 工具 ====================

        /// <summary>关卡整体渲染包围盒（排除环境自己、水面、焦散、粒子）。</summary>
        private static Bounds ComputeLevelBounds()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool has = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                string n = root.name;
                if (n == kEnvRoot || n == kWaterName) continue;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled) continue;
                    if (r is ParticleSystemRenderer) continue;
                    if (r.name == kCausticsObj) continue;
                    Bounds b = r.bounds;
                    if (b.size.sqrMagnitude <= 0.0001f) continue;
                    if (!has) { bounds = b; has = true; }
                    else bounds.Encapsulate(b);
                }
            }
            if (!has) bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            return bounds;
        }

        private static Transform FindDeep(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (root.name == name) return root.transform;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name == name) return all[i];
            }
            return null;
        }

        private static void DestroyColliders(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) Object.DestroyImmediate(colliders[i]);
        }

        private static Quaternion LookAt(Vector3 target, Vector3 from)
        {
            Vector3 dir = target - from;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static void Capture(string name, Vector3 pos, Quaternion rot)
        {
            Camera source = Camera.main;
            var go = new GameObject("__Map1EnvCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = kCameraBackground;
            cam.cullingMask = ~0;
            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;

            RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.sRGB);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(kShotDir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
        }
    }
}
