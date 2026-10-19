using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using PAO.UI.V2;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「洛琪希」关卡接入游戏的章节/UI 体系（与其他关卡表现一致）。
    ///
    /// 做的事（幂等）：
    ///   1. 在场景里补 洛琪希_Spawn / 洛琪希_Goal 两个锚点（已有则复用）；
    ///   2. 把场景加入 Build Settings；
    ///   3. 在 PAOUIConfig.chapters 末尾追加章节（标题/描述/目标/锚点），
    ///      章节地图、HUD 目标、暂停、章节完成/下一关流程都直接复用现有 UI；
    ///   4. 同时把场景加入 additionalGameplayScenes，这样直接 Play 该场景也能看到 HUD/暂停 UI。
    ///
    /// 菜单：
    ///   Tools/PAO/关卡白盒/洛琪希关卡 · 接入章节 UI
    ///   Tools/PAO/关卡白盒/洛琪希关卡 · 截图
    /// </summary>
    public static class LuoqixiChapterSetup
    {
        private const string kScenePath   = "Assets/GameMain/Scenes/洛琪希.unity";
        private const string kSceneName   = "洛琪希";
        private const string kConfigPath  = "Assets/GameMain/Resources/PAOUIConfig.asset";
        private const string kSystemsRoot = "洛琪希_Systems";
        private const string kSpawnName   = "洛琪希_Spawn";
        private const string kGoalName    = "洛琪希_Goal";
        private const string kShotDir     = "Docs/洛琪希-chapter/shots";

        // 章节文案（与其他章节的“水域 NN”口径一致）
        private const string kTitle       = "水域 11 · 洛琪希";
        private const string kDescription = "巨兽的骸骨沉在岩浆与深渊之间，最后一段水流从这里经过。";
        private const string kObjective   = "穿过岩浆与深渊，抵达巨兽骸骨的出口。";

        // 终点备用坐标：拉杆/石碑广场附近（场景里没有现成出口，先放一个可到达的地标）
        private static readonly Vector3 kFallbackGoal = new Vector3(-228.65f, 22.4f, -276.1f);

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/洛琪希关卡 · 接入章节 UI", priority = 48)]
        public static void SetupChapter()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[洛琪希] 退出 Play 再改场景。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath)
            {
                Debug.LogWarning("[洛琪希] 当前不是洛琪希场景（当前：" + scene.path + "）。");
                return;
            }

            Transform systems = EnsureSystemsRoot(scene);
            Transform spawn = EnsureAnchor(scene, systems, kSpawnName, FindPlayerPosition());
            Transform goal = EnsureAnchor(scene, systems, kGoalName, FindGoalPosition());

            EnsureInBuildSettings();
            AddChapterToConfig();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, kScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[洛琪希] 章节 UI 接入完成。\n" +
                "  scene=" + kSceneName + "\n" +
                "  spawn=" + spawn.position + "\n" +
                "  goal=" + goal.position + "\n" +
                "  title=" + kTitle + "\n" +
                "  已加入 Build Settings 与 PAOUIConfig（章节 + additionalGameplayScenes）。");
        }

        [MenuItem("Tools/PAO/关卡白盒/洛琪希关卡 · 截图", priority = 49)]
        public static void CaptureViews()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[洛琪希] 退出 Play 再截图。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath) { Debug.LogWarning("[洛琪希] 当前不是洛琪希场景。"); return; }

            Directory.CreateDirectory(kShotDir);
            foreach (string old in Directory.GetFiles(kShotDir, "*.png")) File.Delete(old);

            Bounds bounds = ComputeBounds(scene);
            Transform player = FindDeep(scene, "takoyaki");
            Camera cam = Camera.main;

            if (cam != null)
                Capture("01_player", cam.transform.position, cam.transform.rotation);
            else
                Capture("01_player", player != null ? player.position + new Vector3(0, 3f, -6f) : bounds.center, Quaternion.identity);

            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            float y = Mathf.Clamp(bounds.max.y + 10f, c.y + 12f, c.y + 60f);

            Capture("02_overview", new Vector3(c.x, y, c.z - Mathf.Max(e.z * 0.8f, 30f)), LookAt(c, new Vector3(c.x, y, c.z - Mathf.Max(e.z * 0.8f, 30f))));
            Capture("03_overview_back", new Vector3(c.x, y, c.z + Mathf.Max(e.z * 0.8f, 30f)), LookAt(c, new Vector3(c.x, y, c.z + Mathf.Max(e.z * 0.8f, 30f))));
            Capture("04_canyon_forward", new Vector3(c.x, c.y, c.z - Mathf.Max(e.z * 0.5f, 20f)), Quaternion.Euler(10f, 0f, 0f));

            if (player != null)
            {
                Vector3 from = player.position + new Vector3(0f, 6f, -16f);
                Capture("05_player_back", from, LookAt(player.position + new Vector3(0, 1.2f, 0f), from));
            }

            Debug.Log("[洛琪希] 截图完成 -> " + Path.GetFullPath(kShotDir) + "  bounds=" + bounds);
        }

        // ==================== 场景锚点 ====================

        private static Transform EnsureSystemsRoot(Scene scene)
        {
            Transform root = FindDeep(scene, kSystemsRoot);
            if (root != null) return root;
            var go = new GameObject(kSystemsRoot);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go.transform;
        }

        private static Transform EnsureAnchor(Scene scene, Transform parent, string name, Vector3 position)
        {
            Transform anchor = FindDeep(scene, name);
            if (anchor == null)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetParent(parent, false);
                anchor = go.transform;
            }
            anchor.position = position;
            return anchor;
        }

        private static Vector3 FindPlayerPosition()
        {
            Transform player = FindDeep(SceneManager.GetActiveScene(), "takoyaki");
            return player != null ? player.position : new Vector3(-222.6f, 23.45f, -245.29f);
        }

        private static Vector3 FindGoalPosition()
        {
            Scene scene = SceneManager.GetActiveScene();
            Vector3 player = FindPlayerPosition();

            // 优先找离玩家最近的“石碑 / 拉杆”地标，避免命中 Preset4 里远在 (-187, -568) 的副本。
            string[] landmarks = { "TFD_Wall_Writing_Tablet_07A_LOD0", "TFD_Wall_Lever_01A" };
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n = all[i].name;
                    bool match = false;
                    for (int k = 0; k < landmarks.Length; k++)
                        if (n == landmarks[k]) { match = true; break; }
                    if (!match) continue;
                    float d = (all[i].position - player).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = all[i]; }
                }
            }

            if (best != null) return best.position + new Vector3(0f, 0.4f, 0f);
            return kFallbackGoal;
        }

        // ==================== Build Settings ====================

        private static void EnsureInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
                if (scenes[i].path == kScenePath) return;

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
            list.Add(new EditorBuildSettingsScene(kScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[洛琪希] 已加入 Build Settings：" + kScenePath);
        }

        // ==================== PAOUIConfig ====================

        private static void AddChapterToConfig()
        {
            UIConfig config = AssetDatabase.LoadAssetAtPath<UIConfig>(kConfigPath);
            if (config == null) { Debug.LogError("[洛琪希] 找不到 PAOUIConfig.asset。"); return; }

            var so = new SerializedObject(config);

            // 1) additionalGameplayScenes：直接 Play 场景也能有 HUD
            SerializedProperty extra = so.FindProperty("additionalGameplayScenes");
            bool hasExtra = false;
            for (int i = 0; i < extra.arraySize; i++)
                if (extra.GetArrayElementAtIndex(i).stringValue == kSceneName) { hasExtra = true; break; }
            if (!hasExtra)
            {
                extra.arraySize++;
                extra.GetArrayElementAtIndex(extra.arraySize - 1).stringValue = kSceneName;
            }

            // 2) chapters：末尾追加正式章节
            SerializedProperty chapters = so.FindProperty("chapters");
            int existing = -1;
            for (int i = 0; i < chapters.arraySize; i++)
            {
                SerializedProperty item = chapters.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("scene").stringValue == kSceneName) { existing = i; break; }
            }

            int index = existing >= 0 ? existing : chapters.arraySize;
            if (existing < 0) chapters.arraySize = chapters.arraySize + 1;
            SerializedProperty chapter = chapters.GetArrayElementAtIndex(index);
            chapter.FindPropertyRelative("scene").stringValue = kSceneName;
            chapter.FindPropertyRelative("title").stringValue = kTitle;
            chapter.FindPropertyRelative("description").stringValue = kDescription;
            chapter.FindPropertyRelative("objective").stringValue = kObjective;
            chapter.FindPropertyRelative("spawnAnchor").stringValue = kSpawnName;
            chapter.FindPropertyRelative("goalAnchor").stringValue = kGoalName;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            UIConfig verify = AssetDatabase.LoadAssetAtPath<UIConfig>(kConfigPath);
            Debug.Log("[洛琪希] PAOUIConfig 已更新：chapters[" + index + "] + additionalGameplayScenes。" +
                      " chapters=" + (verify != null ? verify.chapters.Length : -1) +
                      " IndexOf=" + (verify != null ? verify.IndexOf(kSceneName) : -2) +
                      " Supports=" + (verify != null && verify.Supports(kSceneName)));
        }

        // ==================== 工具 ====================

        private static Bounds ComputeBounds(Scene scene)
        {
            bool has = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (root.name == kSystemsRoot || root.name == "Water_Ceiling") continue;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer r = renderers[i];
                    if (r == null || !r.enabled) continue;
                    if (r is ParticleSystemRenderer) continue;
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

        private static Quaternion LookAt(Vector3 target, Vector3 from)
        {
            Vector3 dir = target - from;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static void Capture(string name, Vector3 pos, Quaternion rot)
        {
            Camera source = Camera.main;
            var go = new GameObject("__LuoqixiCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.03f, 0.05f);
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
