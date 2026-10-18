using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Map0 教程关（第一章「诞生」）光感调整。
    ///
    /// 背景：Map0TutorialSetup.EnsureWhiteProfile() 只调用了 VolumeProfile.Add&lt;T&gt;()，
    /// 没有 AssetDatabase.AddObjectToAsset()，保存后 profile 的组件全部丢失
    /// （Map0_WhiteProfile.asset 里 4 个组件都是 fileID:0），Volume.sharedProfile 也是 null。
    /// 结果 ACES / Bloom / Vignette / ColorAdjustments 实际都没生效，白空间被 HDR 高光裁成一片灰白。
    ///
    /// 这个菜单做三件事（幂等，可重复执行）：
    /// 1. 重建 / 修复 Map0_WhiteProfile.asset（Tonemapping + Bloom + Vignette + ColorAdjustments + WhiteBalance），
    ///    并挂回 Map0_TutorialAtmosphere/Map0_WhitePostFX 的 Volume；
    /// 2. 调 RenderSettings（环境光 / 雾 / 相机底色）：把「糊白」降下来，恢复方向光与阴影层次；
    /// 3. 调灯光：主光 / 冷色补光 / 8 盏彩色点光按 z 渐强，让「简单 → 庞大 → 震撼」的彩色渐变更可见。
    ///
    /// 菜单：Tools/PAO/关卡白盒/Map0 教程重构 · 光感调整
    /// </summary>
    public static class Map0LightingPass
    {
        private const string kScenePath   = "Assets/GameMain/Scenes/Map0.unity";
        private const string kProfilePath = "Assets/GameMain/Art/Profiles/Map0_WhiteProfile.asset";
        private const string kAtmosPath   = "Map0_Tutorial/Map0_TutorialAtmosphere";
        private const string kVolumeName  = "Map0_WhitePostFX";

        // ---------- 调参入口：想改光感，优先改这里的数字 ----------

        // 环境：比原来（0.90 白 × 1.15）暗一档、略偏冷，给方向光和彩光留出对比空间，
        // 但不要压得太死——白空间仍要读得出「白」。
        private static readonly Color kAmbientColor = new Color(0.78f, 0.82f, 0.90f, 1f);
        private const float kAmbientIntensity = 1.10f;
        private static readonly Color kFogColor = new Color(0.90f, 0.93f, 0.97f, 1f);
        private const float kFogDensity = 0.0055f;
        private static readonly Color kCameraBackground = new Color(0.93f, 0.95f, 0.98f, 1f);

        // 主光：暖白，给白墙明确的受光面 / 投影
        private static readonly Color kSunColor = new Color(1f, 0.97f, 0.93f, 1f);
        private const float kSunIntensity = 1.8f;
        private const float kSunShadowStrength = 0.85f;
        private static readonly Vector3 kSunEuler = new Vector3(52f, -30f, 0f);

        // 补光：冷色，只在阴影里提一点蓝，不投影
        private static readonly Color kFillColor = new Color(0.72f, 0.84f, 1f, 1f);
        private const float kFillIntensity = 0.40f;
        private static readonly Vector3 kFillEuler = new Vector3(60f, 150f, 0f);

        // 8 盏彩色点光：按 z（0 → 132）从弱到强；原强度 8~26，这里压到 5~16，
        // 环境光降下来之后彩色已经足够可见，压一点避免整段变色。
        private static readonly float[] kColoredLightIntensity = { 5f, 6f, 7f, 8f, 9.5f, 11f, 13.5f, 16f };

        // 后处理
        private const float kPostExposure = 0.22f;
        private const float kContrast = 12f;
        private const float kSaturation = 8f;
        private const float kWhiteBalanceTemperature = 5f;
        private const float kWhiteBalanceTint = 2f;
        private const float kBloomThreshold = 1.05f;
        private const float kBloomIntensity = 0.55f;
        private const float kBloomScatter = 0.70f;
        private const float kVignetteIntensity = 0.26f;
        private const float kVignetteSmoothness = 0.45f;

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构 · 光感调整", priority = 63)]
        public static void ApplyLighting()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Map0 光感] 退出 Play 再调光。");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath)
            {
                Debug.LogWarning("[Map0 光感] 当前不是 Map0（当前：" + scene.path + "），先打开 Map0 再执行。");
                return;
            }

            GameObject atmos = GameObject.Find(kAtmosPath);
            if (atmos == null)
            {
                Debug.LogError("[Map0 光感] 找不到 " + kAtmosPath + "，场景结构不对。");
                return;
            }

            VolumeProfile profile = EnsureProfile();
            Volume volume = EnsureVolume(atmos.transform);
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            ApplySceneLighting();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, kScenePath);

            Debug.Log(
                "[Map0 光感] 完成并保存。\n" +
                "  Volume: " + kAtmosPath + "/" + kVolumeName + "  profile=" + profile.name + "\n" +
                "  环境光: " + kAmbientColor + " × " + kAmbientIntensity + "\n" +
                "  雾: " + kFogColor + " density=" + kFogDensity + " (Exp2)\n" +
                "  主光: intensity=" + kSunIntensity + " euler=" + kSunEuler + " shadow=" + kSunShadowStrength + "\n" +
                "  补光: intensity=" + kFillIntensity + " color=" + kFillColor + "\n" +
                "  后处理: ACES / Bloom(" + kBloomThreshold + ", " + kBloomIntensity + ") / Vignette(" + kVignetteIntensity + ")" +
                " / ColorAdjust(exposure " + kPostExposure + ", contrast " + kContrast + ", saturation " + kSaturation + ")");
        }

        /// <summary>
        /// 只应用场景光感（RenderSettings / 相机底色 / 主光 / 补光 / 彩色点光），不保存场景。
        /// 供「光感调整」菜单和 Map0TutorialSetup.BuildAtmosphere() 复用，保证重建后光感一致。
        /// </summary>
        public static void ApplySceneLighting()
        {
            ApplyRenderSettings();
            ApplyCamera();
            ApplyLights();
        }

        // ==================== Profile ====================

        /// <summary>保证 Map0_WhiteProfile 是「有真实子资产、组件非空」的可用 profile。</summary>
        public static VolumeProfile EnsureProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(kProfilePath);
            if (!IsUsable(profile))
            {
                if (profile != null) AssetDatabase.DeleteAsset(kProfilePath);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, kProfilePath);

                AddEffect<Tonemapping>(profile);
                AddEffect<Bloom>(profile);
                AddEffect<Vignette>(profile);
                AddEffect<ColorAdjustments>(profile);
                AddEffect<WhiteBalance>(profile);
                AssetDatabase.SaveAssets();
            }
            else
            {
                bool added = false;
                added |= EnsureEffect<Tonemapping>(profile);
                added |= EnsureEffect<Bloom>(profile);
                added |= EnsureEffect<Vignette>(profile);
                added |= EnsureEffect<ColorAdjustments>(profile);
                added |= EnsureEffect<WhiteBalance>(profile);
                if (added)
                {
                    EditorUtility.SetDirty(profile);
                    AssetDatabase.SaveAssets();
                }
            }

            ConfigureProfile(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static bool IsUsable(VolumeProfile profile)
        {
            if (profile == null || profile.components == null || profile.components.Count < 5) return false;
            for (int i = 0; i < profile.components.Count; i++)
                if (profile.components[i] == null) return false;
            return true;
        }

        private static T AddEffect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static bool EnsureEffect<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out T existing) && existing != null) return false;
            AddEffect<T>(profile);
            return true;
        }

        private static void ConfigureProfile(VolumeProfile profile)
        {
            if (profile.TryGet<Tonemapping>(out Tonemapping tonemapping) && tonemapping != null)
                tonemapping.mode.Override(TonemappingMode.ACES);

            if (profile.TryGet<Bloom>(out Bloom bloom) && bloom != null)
            {
                bloom.threshold.Override(kBloomThreshold);
                bloom.intensity.Override(kBloomIntensity);
                bloom.scatter.Override(kBloomScatter);
                bloom.tint.Override(new Color(1f, 0.96f, 0.92f, 1f));
            }

            if (profile.TryGet<Vignette>(out Vignette vignette) && vignette != null)
            {
                vignette.intensity.Override(kVignetteIntensity);
                vignette.smoothness.Override(kVignetteSmoothness);
                vignette.color.Override(Color.black);
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
        }

        private static Volume EnsureVolume(Transform atmos)
        {
            Transform t = atmos.Find(kVolumeName);
            if (t == null)
            {
                var go = new GameObject(kVolumeName);
                go.transform.SetParent(atmos, false);
                t = go.transform;
            }

            Volume volume = t.GetComponent<Volume>();
            if (volume == null) volume = t.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            return volume;
        }

        // ==================== RenderSettings / Camera ====================

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

        private static void ApplyCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = kCameraBackground;
        }

        // ==================== Lights ====================

        private static void ApplyLights()
        {
            GameObject sunGo = GameObject.Find("Directional Light");
            if (sunGo != null)
            {
                Light sun = sunGo.GetComponent<Light>();
                if (sun != null)
                {
                    sun.type = LightType.Directional;
                    sun.color = kSunColor;
                    sun.intensity = kSunIntensity;
                    sun.shadows = LightShadows.Soft;
                    sun.shadowStrength = kSunShadowStrength;
                    sun.transform.rotation = Quaternion.Euler(kSunEuler);
                }
            }
            else Debug.LogWarning("[Map0 光感] 找不到 Directional Light。");

            GameObject fillGo = GameObject.Find(kAtmosPath + "/Tutorial_FillLight");
            if (fillGo != null)
            {
                Light fill = fillGo.GetComponent<Light>();
                if (fill != null)
                {
                    fill.type = LightType.Directional;
                    fill.color = kFillColor;
                    fill.intensity = kFillIntensity;
                    fill.shadows = LightShadows.None;
                    fill.transform.rotation = Quaternion.Euler(kFillEuler);
                }
            }

            Transform root = GameObject.Find("Map0_Tutorial")?.transform;
            Transform group = null;
            if (root != null)
            {
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].name == "ColorProgression") { group = all[i]; break; }
                }
            }
            if (group == null)
            {
                Debug.LogWarning("[Map0 光感] 找不到 ColorProgression，彩色点光未调整。");
                return;
            }

            var lights = new List<Light>();
            Light[] children = group.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < children.Length; i++)
                if (children[i].type == LightType.Point) lights.Add(children[i]);
            lights.Sort((a, b) => a.transform.position.z.CompareTo(b.transform.position.z));

            for (int i = 0; i < lights.Count; i++)
            {
                int idx = Mathf.Clamp(i, 0, kColoredLightIntensity.Length - 1);
                lights[i].intensity = kColoredLightIntensity[idx];
                lights[i].shadows = LightShadows.None;
            }
            Debug.Log("[Map0 光感] 彩色点光 " + lights.Count + " 盏，按 z 设置强度。");
        }
    }
}
