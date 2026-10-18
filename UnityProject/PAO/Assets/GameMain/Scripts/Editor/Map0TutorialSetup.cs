using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using PAO;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Map0 教程重构（第一章「诞生」｜新手教学）。
    ///
    /// 依据：《泡涌》(2).docx 第一章旁白稿。
    ///   场景 01 苏醒：纯白空间、玩家诞生在一颗半透明泡泡里；
    ///   场景 02 破坏：前方一面「粉色泡泡墙」，打破才能过；
    ///   场景 03 恢复：被打破的粉色泡泡会自己恢复（TerrainBubble 重生）；
    ///   场景 04 资源：破坏消耗泡泡液，散落的液滴可以回收；
    ///   场景 05 分歧：出口开放后，走反方向会有旁白反应；
    ///   场景 08 收束：出口 → 第一章终幕。
    ///
    /// 美术：白空间（白色石构 + 白雾 + 白后处理 + 浮尘）、粉色泡泡墙、发光出口门。
    /// 旁白：Map0NarrationDirector 只播 5 段「关键机制教学」（破坏/恢复/消耗/蓄力/材料），
    ///       开场叙事、出口提示与结尾提问都不播（数据见 Map0NarrationData）。
    ///
    /// 菜单：Tools/PAO/关卡白盒/Map0 教程重构（第一章 诞生）
    /// </summary>
    public static class Map0TutorialSetup
    {
        private const string kScenePath = "Assets/GameMain/Scenes/Map0.unity";
        private const string kRoot      = "Map0_Tutorial";
        private const string kBackupDir = "Docs/Map0-refactor/backup";
        private const string kShotDir   = "Docs/Map0-refactor/tutorial-shots";
        private const string kMatDir    = "Assets/GameMain/Art/Materials";
        private const string kProfileDir = "Assets/GameMain/Art/Profiles";
        private const string kWengjiePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kPortalPrefab = "Assets/GameMain/Art/VFX/TFD_Portal_01A.prefab";
        private const string kDustPrefab = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Ambient Particles.prefab";

        private const string kSpawnAnchor = "Map0_Spawn";
        private const string kGoalAnchor  = "Map0_Goal";

        private static Material m_Floor, m_Wall, m_BubbleSkin, m_BirthBubble, m_Glow, m_Droplet, m_BlackDroplet, m_Scratch, m_Landmark;
        private static readonly List<GameObject> s_ColumnTemplates = new List<GameObject>();
        private static readonly List<GameObject> s_PavementTemplates = new List<GameObject>();
        private static int s_ColumnIndex;
        private static int s_PavementIndex;
        private static readonly List<GameObject> s_PropTemplates = new List<GameObject>();
        private static int s_PropIndex;
        private const string kMap5Path = "Assets/GameMain/Scenes/Map5.unity";
        private static Material[] m_ColorOrbs;
        private static Material m_BubbleReal;

        private static readonly Vector3 SpawnPos = new Vector3(0f, 0.2f, -26f);
        private static readonly Vector3 GoalPos = new Vector3(0f, 3.2f, 74f);

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构（第一章 诞生）", priority = 55)]
        public static void BuildAll()
        {
            if (!Begin()) return;
            BuildWorld();
            BuildAtmosphere();
            BuildNarration();
            WireChapterConfig();
            End();
            CaptureViews();
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构 · 只截图", priority = 56)]
        public static void CaptureViews()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map0] 退出 Play 再截图。"); return; }
            if (SceneManager.GetActiveScene().path != kScenePath) { Debug.LogWarning("[Map0] 当前不是 Map0。"); return; }
            Directory.CreateDirectory(kShotDir);
            Capture("01_spawn", new Vector3(0f, 3f, -37f), Quaternion.Euler(4f, 0f, 0f));
            Capture("02_birth_bubble", new Vector3(0f, 2.6f, -21f), Quaternion.Euler(2f, 180f, 0f));
            Capture("03_pink_wall", new Vector3(0f, 2.6f, -25f), Quaternion.Euler(3f, 0f, 0f));
            Capture("04_reveal", new Vector3(0f, 2.4f, -15f), Quaternion.Euler(6f, 0f, 0f));
            Capture("05_hall", new Vector3(0f, 2.4f, -6f), Quaternion.Euler(4f, 0f, 0f));
            Capture("06_hall_mid", new Vector3(0f, 4f, 20f), Quaternion.Euler(6f, 0f, 0f));
            Capture("07_climb", new Vector3(0f, 2.6f, 50f), Quaternion.Euler(14f, 0f, 0f));
            Capture("08_gallery", new Vector3(0f, 11.5f, 76f), Quaternion.Euler(2f, 0f, 0f));
            Capture("09_exit", new Vector3(0f, 12f, 86f), Quaternion.Euler(8f, 0f, 0f));
            Capture("10_color_corona", new Vector3(0f, 12f, 98f), Quaternion.Euler(16f, 0f, 0f));
            Capture("11_look_back", new Vector3(0f, 4f, 10f), Quaternion.Euler(4f, 180f, 0f));
            Capture("12_overview", new Vector3(0f, 40f, 20f), Quaternion.Euler(38f, 0f, 0f));
            Debug.Log("[Map0 教程] 截图完成 -> " + Path.GetFullPath(kShotDir));
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构 · 场景报告", priority = 57)]
        public static void SceneReport()
        {
            Scene scene = SceneManager.GetActiveScene();
            int renderers = 0;
            Bounds b = default(Bounds); bool has = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (!rs[i].enabled || rs[i] is ParticleSystemRenderer) continue;
                    if (!has) { b = rs[i].bounds; has = true; } else b.Encapsulate(rs[i].bounds);
                    renderers++;
                }
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Map0 教程] 场景报告");
            sb.AppendLine("  Renderer: " + renderers);
            if (has) sb.AppendLine("  Bounds: min=" + b.min + " max=" + b.max);
            foreach (GameObject root in scene.GetRootGameObjects())
                sb.AppendLine("  Root: " + root.name + " hierarchy=" + root.transform.hierarchyCount);
            Debug.Log(sb.ToString());
        }

        // ==================== 场景准备 ====================

        private static bool Begin()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map0] 请先退出 Play 模式。"); return false; }
            Scene active = SceneManager.GetActiveScene();
            if (active.path != kScenePath)
            {
                EditorSceneManager.SaveOpenScenes();
                active = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);
            }
            else EditorSceneManager.SaveScene(active);

            Directory.CreateDirectory(kBackupDir);
            File.Copy(active.path, Path.Combine(kBackupDir, "Map0_before_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity"), true);

            LoadMaterials();
            return true;
        }

        private static void End()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Map0 教程] 第一章「诞生」（新手教学）已重建。根物体：" + kRoot);
        }

        private static void LoadMaterials()
        {
            m_Floor = Tinted("M_Map0_TutorialFloor.mat", "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat", new Color(0.93f, 0.94f, 0.96f));
            m_Wall = Tinted("M_Map0_TutorialWall.mat", "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue_B.mat", new Color(0.86f, 0.88f, 0.91f));
            m_BubbleSkin = Tinted("M_Map0_BubbleSkin.mat", null, new Color(1f, 0.62f, 0.76f));
            m_BirthBubble = Transparent("M_Map0_BirthBubble.mat", new Color(0.82f, 0.9f, 1f, 0.34f));
            m_Glow = EnsureEmissive("M_Map0_TutorialGlow.mat", new Color(1f, 0.97f, 0.9f), new Color(1.6f, 1.5f, 1.3f));
            m_Droplet = EnsureEmissive("M_Map0_DropletGlow.mat", new Color(0.55f, 0.85f, 1f), new Color(0.5f, 1.2f, 1.8f));
            m_BlackDroplet = Tinted("M_Map0_BlackDroplet.mat", null, new Color(0.05f, 0.05f, 0.07f));
            m_Scratch = Tinted("M_Map0_Scratch.mat", null, new Color(0.34f, 0.35f, 0.38f));
            m_Landmark = EnsureEmissive("M_Map0_GiantBubble.mat", new Color(0.82f, 0.9f, 1f), new Color(0.55f, 0.85f, 1.3f));
            m_ColorOrbs = new Material[5];
            m_ColorOrbs[0] = EnsureEmissive("M_Map0_OrbTeal.mat", new Color(0.35f, 0.95f, 0.9f), new Color(0.15f, 0.9f, 0.8f));
            m_ColorOrbs[1] = EnsureEmissive("M_Map0_OrbAmber.mat", new Color(1f, 0.72f, 0.35f), new Color(1f, 0.55f, 0.15f));
            m_ColorOrbs[2] = EnsureEmissive("M_Map0_OrbViolet.mat", new Color(0.72f, 0.55f, 1f), new Color(0.5f, 0.3f, 1f));
            m_ColorOrbs[3] = EnsureEmissive("M_Map0_OrbRose.mat", new Color(1f, 0.5f, 0.65f), new Color(1f, 0.25f, 0.45f));
            m_ColorOrbs[4] = EnsureEmissive("M_Map0_OrbSky.mat", new Color(0.55f, 0.8f, 1f), new Color(0.3f, 0.6f, 1f));

            // 真实泡泡材质：工程现成的 BubbleTideToon（半透、内壁、虹彩、流动）
            m_BubbleReal = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/BubbleTideToon.mat");
            if (m_BubbleReal == null) m_BubbleReal = m_BubbleSkin;

            // 真实建筑材质：工程 Map4/Map5 的石化材质，保留贴图细节
            // 白色底材：颜色完全交给 RampColor 的逐构件渐变（否则贴图自身颜色会盖掉渐变）
        }

        private static Material Tinted(string fileName, string sourcePath, Color color)
        {
            string path = kMatDir + "/" + fileName;
            // 每次都重建：wengjie 的源材质带蓝色贴图，复制过来会整关变蓝
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            Material mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(fileName) };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.SetFloat("_Metallic", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.03f);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Material Transparent(string fileName, Color color)
        {
            string path = kMatDir + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            Material mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(fileName) };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Material EnsureEmissive(string fileName, Color baseColor, Color emission)
        {
            string path = kMatDir + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            Material mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(fileName) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.8f);
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ==================== 世界 ====================

        private static void BuildWorld()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject player = Object.FindFirstObjectByType<PlayerController>()?.gameObject;

            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot == null) continue;
                if (sceneRoot.name == "Directional Light") continue;
                if (player != null && sceneRoot == player) continue;
                Object.DestroyImmediate(sceneRoot);
            }

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            LoadRealTemplates(scene);
            LoadExtraPropTemplates(scene);
            ConsolidateTemplateHolders();
            BuildWhiteSpace(t);
            BuildBirthBubble(t);
            BuildPinkWall(t);
            BuildResourceRoom(t);
            BuildForkAndExit(t);
            BuildHallBeats(t);
            BuildMeaningfulArchitecture(t);
            BuildTutorialGates(t);
            BuildColorProgression(t);
            ApplyColorGradient(t);
            BuildSystems(t, player);
        }

        private static void BuildWhiteSpace(Transform t)
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform a = Group(t, "WhiteSpace");

            // ===== 横向锥形空间：出生端是锥尖（窄、矮、简单），越往前越宽越高（庞大 → 震撼）=====
            const float zApex = -42f;
            const float zBase = 110f;
            const float hwApex = 5.5f;
            const float hwBase = 56f;
            const float hApex = 8f;
            const float hBase = 52f;
            const int segments = 64;

            float segLen = (zBase - zApex) / segments;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                float t1 = (i + 1) / (float)segments;
                float tm = (t0 + t1) * 0.5f;
                float zMid = zApex + tm * (zBase - zApex);

                // 用贝塞尔曲线控制「宽度 / 高度」的展开节奏：
                // 前半段几乎不收放（简单、窄），后半段才迅速张开（庞大 → 震撼）
                float w0 = BezierT(t0, 0.15f, 0.62f);
                float w1 = BezierT(t1, 0.15f, 0.62f);
                float hh0 = BezierT(t0, 0.2f, 0.7f);
                float hh1 = BezierT(t1, 0.2f, 0.7f);
                float hw0 = Mathf.Lerp(hwApex, hwBase, w0);
                float hw1 = Mathf.Lerp(hwApex, hwBase, w1);
                float h0 = Mathf.Lerp(hApex, hBase, hh0);
                float h1 = Mathf.Lerp(hApex, hBase, hh1);
                float hwMid = (hw0 + hw1) * 0.5f;
                float hMid = (h0 + h1) * 0.5f;

                // 中轴线也走一条贝塞尔 S 形，避免整条路是一条直线
                float cx0 = CenterOffset(t0);
                float cx1 = CenterOffset(t1);
                float cxMid = (cx0 + cx1) * 0.5f;

                float slope = Mathf.Atan2(hw1 - hw0, segLen) * Mathf.Rad2Deg;
                float yaw = Mathf.Atan2(cx1 - cx0, segLen) * Mathf.Rad2Deg;

                // 整体环境的颜色渐变：越往锥底，白色里逐渐渗进暖色
                float g = Mathf.Clamp01(t0 * 1.12f);
                Color floorTint = Color.Lerp(new Color(0.95f, 0.95f, 0.97f), new Color(0.95f, 0.87f, 0.78f), g * g);
                Color wallTint = Color.Lerp(new Color(0.90f, 0.91f, 0.94f), new Color(0.72f, 0.66f, 0.93f), Mathf.SmoothStep(0f, 1f, g));

                // 地面：64 段贝塞尔锥面（段与段之间旋转对齐，看起来是连续的曲线）
                GameObject floorSeg = Box(a, "ConeFloor_" + i, new Vector3(cxMid, -0.5f, zMid), new Vector3(hwMid * 2f, 1f, segLen + 0.8f), m_Floor);
                floorSeg.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                Tint(floorSeg.GetComponent<Renderer>(), floorTint);

                // 两侧锥面：沿曲线切线旋转，宽度/高度都由贝塞尔控制
                GameObject left = Box(a, "ConeWall_L_" + i, new Vector3(cxMid - hwMid, hMid * 0.5f, zMid), new Vector3(1.4f, hMid, segLen + 0.8f), m_Wall);
                left.transform.rotation = Quaternion.Euler(0f, yaw + slope, 0f);
                GameObject right = Box(a, "ConeWall_R_" + i, new Vector3(cxMid + hwMid, hMid * 0.5f, zMid), new Vector3(1.4f, hMid, segLen + 0.8f), m_Wall);
                right.transform.rotation = Quaternion.Euler(0f, yaw - slope, 0f);

                Tint(left.GetComponent<Renderer>(), wallTint);
                Tint(right.GetComponent<Renderer>(), wallTint);
            }

            // 锥尖后墙（出生端）
            Box(a, "ConeApexWall", new Vector3(0f, hApex * 0.5f, zApex), new Vector3(hwApex * 2f + 2f, hApex, 1.5f), m_Wall);

            // ===== 锥形内部的结构：巨柱跟着宽度逐渐变多 =====
            for (int i = 0; i < 6; i++)
            {
                float z = 0f + i * 16f;
                float tt = Mathf.InverseLerp(zApex, zBase, z);
                float hw = Mathf.Lerp(hwApex, hwBase, tt);
                float colH = Mathf.Lerp(24f, 46f, tt);
                Pillar(a, "InnerColumn_W_" + i, new Vector3(-hw * 0.45f, 0f, z), 2.4f + i * 0.14f, colH);
                Pillar(a, "InnerColumn_E_" + i, new Vector3(hw * 0.45f, 0f, z), 2.4f + i * 0.14f, colH);
                // 横跨锥体的巨梁：给空间做「间距构造」，也压出体量
                Box(a, "ArchRib_" + i, new Vector3(0f, colH * 0.72f, z), new Vector3(hw * 1.9f, 2.4f + i * 0.2f, 2.6f), m_Wall);
                if (i % 2 == 1)
                    Box(a, "ArchRib_Low_" + i, new Vector3(0f, colH * 0.38f, z), new Vector3(hw * 1.85f, 1.6f, 2.2f), m_Wall);
            }

            // ===== 过渡喉口（在锥尖后段再收一次，制造「先窄后阔」）=====
            Box(a, "Throat_W", new Vector3(-6.5f, 5f, -13f), new Vector3(1f, 10f, 7f), m_Wall);
            Box(a, "Throat_E", new Vector3(6.5f, 5f, -13f), new Vector3(1f, 10f, 7f), m_Wall);
            Box(a, "Throat_Floor", new Vector3(0f, -0.5f, -13f), new Vector3(14f, 1f, 7f), m_Floor);

            // ===== 锥形扩散的入口巨框（从喉口看出去，巨型泡正好被框在正中）=====
            Box(a, "Reveal_L", new Vector3(-17f, 14f, -8f), new Vector3(13f, 28f, 3f), m_Wall);
            Box(a, "Reveal_R", new Vector3(17f, 14f, -8f), new Vector3(13f, 28f, 3f), m_Wall);
            Box(a, "Reveal_Top", new Vector3(0f, 29f, -8f), new Vector3(47f, 2f, 3f), m_Wall);

            // ===== 上升段：宽台阶爬到高廊 =====
            for (int i = 0; i < 10; i++)
            {
                GameObject step = Box(a, "ClimbStair_" + i, new Vector3(0f, 0.5f + i * 1.0f, 58f + i * 1.5f), new Vector3(16f, 1f, 1.6f), m_Floor);
                HideRenderer(step);
                GameObject art = TakeTemplate(s_PavementTemplates, ref s_PavementIndex);
                if (art != null)
                    PlacePiece(art, a, new Vector3(0f, 1.0f + i * 1.0f, 58f + i * 1.5f), 0f, 0.3f, 15f);
            }
            Box(a, "ClimbSide_W", new Vector3(-9f, 5f, 64f), new Vector3(1f, 10f, 15f), m_Wall);
            Box(a, "ClimbSide_E", new Vector3(9f, 5f, 64f), new Vector3(1f, 10f, 15f), m_Wall);

            Box(a, "Gallery", new Vector3(0f, 9.5f, 84f), new Vector3(16f, 1f, 22f), m_Floor);
            Box(a, "Gallery_Rail_W", new Vector3(-8f, 10.5f, 84f), new Vector3(0.6f, 1f, 22f), m_Wall);
            Box(a, "Gallery_Rail_E", new Vector3(8f, 10.5f, 84f), new Vector3(0.6f, 1f, 22f), m_Wall);

            // ===== 锥底（宽阔端）：抬高出口平台 + 巨拱 + 发光巨泡 =====
            Box(a, "ExitPlatform", new Vector3(0f, 9.5f, 100f), new Vector3(52f, 1f, 18f), m_Floor);
            for (int px = -2; px <= 2; px++)
                for (int pz = 0; pz < 3; pz++)
                {
                    GameObject art = TakeTemplate(s_PavementTemplates, ref s_PavementIndex);
                    if (art != null)
                        PlacePiece(art, a, new Vector3(px * 10f, 10f, 94f + pz * 6f), 0f, 0.25f, 10f);
                }
            Box(a, "ExitArch_L", new Vector3(-26f, 25f, 108f), new Vector3(12f, 31f, 6f), m_Wall);
            Box(a, "ExitArch_R", new Vector3(26f, 25f, 108f), new Vector3(12f, 31f, 6f), m_Wall);
            Box(a, "ExitArch_Top", new Vector3(0f, 40f, 108f), new Vector3(64f, 2.6f, 6f), m_Wall);

            // 锥底后墙（只在拱外两侧，形成锥形的「大开口」）
            Box(a, "BaseWall_L", new Vector3(-42f, 17f, 112f), new Vector3(28f, 34f, 2f), m_Wall);
            Box(a, "BaseWall_R", new Vector3(42f, 17f, 112f), new Vector3(28f, 34f, 2f), m_Wall);

            // 错误方向：锥形西侧的侧廊（旁白 B-005/B-006 用）
            Box(a, "WrongCorridor_N", new Vector3(-58f, 4f, 52f), new Vector3(22f, 8f, 1f), m_Wall);
            Box(a, "WrongCorridor_S", new Vector3(-58f, 4f, 40f), new Vector3(22f, 8f, 1f), m_Wall);
            Box(a, "WrongCorridor_Floor", new Vector3(-58f, -0.5f, 46f), new Vector3(22f, 1f, 12f), m_Floor);
            Box(a, "WrongCorridor_DeadEnd", new Vector3(-70f, 4f, 46f), new Vector3(1f, 8f, 12f), m_Wall);
        }

        /// <summary>三次贝塞尔（P0 = 0，P3 = 1）的归一化 y 值，用两个控制点调「展开节奏」。</summary>
        private static float BezierT(float t, float p1, float p2)
        {
            float u = 1f - t;
            return 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t;
        }

        /// <summary>关节中轴线的横向偏移：一条缓慢的 S 形贝塞尔。</summary>
        private static float CenterOffset(float t)
        {
            float u = 1f - t;
            return 10f * (3f * u * u * t * 0.55f + 3f * u * t * t * (-0.45f) + t * t * t * 0.25f);
        }

        /// <summary>给单个 Renderer 打颜色（共享材质不动，用 MaterialPropertyBlock 做渐变）。</summary>
        private static void Tint(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>从真实场景（Map5）收集 TFD 柱/铺地/楼梯模板，放到地下当模板库。</summary>
        private static void LoadRealTemplates(Scene dst)
        {
            if (s_ColumnTemplates.Count > 0) return;
            try
            {
                Scene src = EditorSceneManager.OpenScene(kMap5Path, OpenSceneMode.Additive);
                s_ColumnTemplates.AddRange(CollectTemplates(src, dst, null, new[] { "TFD_Column_02A", "TFD_Column_01A", "TFD_Column_03A" }, 5));
                s_PavementTemplates.AddRange(CollectTemplates(src, dst, null, new[] { "TFD_Pavement", "TFD_Stairs_01A" }, 8));
                EditorSceneManager.CloseScene(src, true);
                Debug.Log("[Map0] 真实模板：柱 " + s_ColumnTemplates.Count + " / 铺地·楼梯 " + s_PavementTemplates.Count);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Map0] 真实模板加载失败（回退白模）：" + e.Message);
            }
        }

        private static GameObject TakeTemplate(List<GameObject> list, ref int index)
        {
            if (list.Count == 0) return null;
            GameObject go = list[index % list.Count];
            index++;
            return go;
        }

        private static void HideRenderer(GameObject go)
        {
            Renderer r = go != null ? go.GetComponent<Renderer>() : null;
            if (r != null) r.enabled = false;
        }

        /// <summary>巨柱：碰撞用圆柱，外观换成真实 TFD 石柱。</summary>
        private static void Pillar(Transform parent, string name, Vector3 basePos, float diameter, float height)
        {
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = name + "_Collider";
            shaft.transform.SetParent(parent, false);
            shaft.transform.position = new Vector3(basePos.x, basePos.y + height * 0.5f, basePos.z);
            shaft.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            HideRenderer(shaft);

            GameObject art = TakeTemplate(s_ColumnTemplates, ref s_ColumnIndex);
            if (art != null)
            {
                try { PlacePiece(art, parent, new Vector3(basePos.x, basePos.y, basePos.z), 0f, height, diameter * 3.4f); }
                catch (System.Exception e) { Debug.LogWarning("[Map0] 石柱真实资产放置失败：" + e.Message); }
            }
            else
            {
                Renderer fallback = shaft.GetComponent<Renderer>();
                if (fallback != null) { fallback.enabled = true; fallback.sharedMaterial = m_Wall; }
            }
        }

        private static void BuildBirthBubble(Transform t)
        {
            Transform a = Group(t, "BirthBubble");
            GameObject bubble = Sphere(a, "BirthBubble", new Vector3(SpawnPos.x, 2.6f, SpawnPos.z), 9f, m_BirthBubble);
            bubble.GetComponent<Collider>().enabled = false;

            Box(a, "BirthPad", new Vector3(SpawnPos.x, 0.01f, SpawnPos.z), new Vector3(5f, 0.015f, 5f), m_Glow, false);

            for (int i = 0; i < 4; i++)
            {
                GameObject mark = Box(a, "BirthBubble_Scratch_" + i,
                    new Vector3(-1.4f + i * 0.9f, 0.02f, -27.6f + (i % 2) * 0.8f),
                    new Vector3(0.035f, 0.01f, 0.45f + (i % 2) * 0.25f), m_Scratch, false);
                mark.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
            }
        }

        /// <summary>粉色泡泡墙：每一颗都是会重生的小地形泡（破坏 → 恢复的教学样本）。</summary>
        private static void Barrier(Transform parent, string name, Vector3 center, int cols, int rows, float spacing, float diameter, bool sideFrames)
        {
            if (sideFrames)
            {
                float halfW = (cols * spacing) * 0.5f + 1.2f;
                Box(parent, name + "_Frame_W", new Vector3(center.x - halfW, center.y + rows * spacing * 0.5f, center.z), new Vector3(2f, rows * spacing + 3f, 4f), m_Wall);
                Box(parent, name + "_Frame_E", new Vector3(center.x + halfW, center.y + rows * spacing * 0.5f, center.z), new Vector3(2f, rows * spacing + 3f, 4f), m_Wall);
                Box(parent, name + "_Frame_Top", new Vector3(center.x, center.y + rows * spacing + 0.6f, center.z), new Vector3(halfW * 2f + 2f, 0.8f, 4f), m_Wall);
            }

            for (int row = 0; row < rows; row++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = center.x + (c - (cols - 1) * 0.5f) * spacing;
                    float y = center.y + row * spacing;
                    GameObject go = Sphere(parent, name + "_" + row + "_" + c, new Vector3(x, y, center.z), diameter, m_BubbleReal);
                    TerrainBubble tb = go.AddComponent<TerrainBubble>();
                    SetSerialized(tb, so =>
                    {
                        so.FindProperty("m_Capacity").intValue = 1;
                        so.FindProperty("m_RewardAmount").intValue = 5;
                        so.FindProperty("m_RespawnDelay").floatValue = 9f;
                        so.FindProperty("m_EmptyColor").colorValue = new Color(1f, 0.72f, 0.82f, 1f);
                        so.FindProperty("m_FullColor").colorValue = new Color(1f, 0.3f, 0.55f, 1f);
                    });
                }
            }
        }

        private static void BuildPinkWall(Transform t)
        {
            Transform a = Group(t, "PinkBubbleWall");
            Barrier(a, "PinkWall", new Vector3(0f, 1.2f, -18f), 8, 4, 2.55f, 2.4f, true);
            Barrier(a, "PinkWall_Mid", new Vector3(0f, 1.2f, 24f), 6, 3, 2.55f, 2.4f, true);
            Barrier(a, "PinkGate", new Vector3(0f, 10.7f, 78f), 6, 3, 2.55f, 2.4f, true);
        }

        private static void BuildResourceRoom(Transform t)
        {
            Transform a = Group(t, "ResourceRoom");

            Vector3[] springs = { new Vector3(-10f, 1f, 8f), new Vector3(10f, 1f, 8f), new Vector3(-10f, 1f, 40f), new Vector3(10f, 1f, 40f) };
            for (int i = 0; i < springs.Length; i++)
            {
                Box(a, "SpringPlinth_" + i, new Vector3(springs[i].x, 0.35f, springs[i].z), new Vector3(2.6f, 0.7f, 2.6f), m_Wall);
                GameObject go = Sphere(a, "BubbleSpring_" + i, springs[i] + Vector3.up * 1.4f, 1.8f, m_BubbleReal);
                TerrainBubble tb = go.AddComponent<TerrainBubble>();
                SetSerialized(tb, so =>
                {
                    so.FindProperty("m_Capacity").intValue = 2;
                    so.FindProperty("m_RewardAmount").intValue = 8;
                    so.FindProperty("m_RespawnDelay").floatValue = 12f;
                    so.FindProperty("m_EmptyColor").colorValue = new Color(0.7f, 0.9f, 1f, 1f);
                    so.FindProperty("m_FullColor").colorValue = new Color(0.4f, 0.85f, 1f, 1f);
                });
            }

            Vector3[] droplets = { new Vector3(-6f, 1.1f, 2f), new Vector3(6f, 1.1f, 16f), new Vector3(-8f, 1.1f, 32f), new Vector3(7f, 1.1f, 46f), new Vector3(-5f, 10.8f, 84f), new Vector3(5f, 10.8f, 92f) };
            for (int i = 0; i < droplets.Length; i++)
            {
                GameObject go = Sphere(a, "Droplet_" + i, droplets[i], 0.8f, m_Droplet);
                go.GetComponent<Collider>().isTrigger = true;
                go.AddComponent<PAO.Map0DropletPickup>().Configure(6);
            }

            Box(a, "Unrecyclable_Plinth", new Vector3(-12f, 0.15f, -2f), new Vector3(1.6f, 0.3f, 1.6f), m_Wall);
            GameObject black = Sphere(a, "Droplet_Unrecyclable", new Vector3(-12f, 0.65f, -2f), 0.7f, m_BlackDroplet);
            black.GetComponent<Collider>().isTrigger = true;
        }

        private static void BuildForkAndExit(Transform t)
        {
            Transform a = Group(t, "ForkAndExit");

            GameObject portalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPortalPrefab);
            if (portalPrefab != null)
            {
                GameObject portal = (GameObject)PrefabUtility.InstantiatePrefab(portalPrefab);
                portal.name = "ExitPortal";
                SceneManager.MoveGameObjectToScene(portal, a.gameObject.scene);
                portal.transform.SetParent(a, true);
                Bounds pb = Measure(portal);
                float scale = 9f / Mathf.Max(0.001f, pb.size.y);
                portal.transform.localScale *= scale;
                Bounds after = Measure(portal);
                portal.transform.position += new Vector3(0f, 10f, 106f) - new Vector3(after.center.x, after.min.y, after.center.z);
                DestroyColliders(portal);
            }

            // 「震撼」：出口巨拱后、雾里的发光巨泡
            GameObject giant = Sphere(a, "GiantBubble", new Vector3(0f, 28f, 132f), 44f, m_BubbleReal);
            giant.GetComponent<Collider>().enabled = false;
            GameObject giantCore = Sphere(a, "GiantBubble_Core", new Vector3(0f, 28f, 132f), 30f, m_Landmark);
            giantCore.GetComponent<Collider>().enabled = false;

            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "GiantBubble_Beam";
            beam.transform.SetParent(a, false);
            beam.transform.position = new Vector3(0f, 24f, 132f);
            beam.transform.localScale = new Vector3(7f, 26f, 7f);
            Object.DestroyImmediate(beam.GetComponent<Collider>());
            Renderer br = beam.GetComponent<Renderer>();
            if (br != null) br.sharedMaterial = m_Glow;

            var goal = new GameObject(kGoalAnchor);
            goal.transform.SetParent(a, false);
            goal.transform.position = new Vector3(0f, 13f, 107f);
            BoxCollider goalBox = goal.AddComponent<BoxCollider>();
            goalBox.isTrigger = true;
            goalBox.size = new Vector3(8f, 6f, 7f);
            var trigger = goal.AddComponent<PAO.UI.V2.ChapterGoalTrigger>();
            trigger.requireInteract = true;
            trigger.nextScene = "Map1";
        }

        private static void BuildHallBeats(Transform t)
        {
            Transform a = Group(t, "HallBeats");
            float[] beats = { -14f, 12f, 62f, 92f, 108f };
            for (int b = 0; b < beats.Length; b++)
            {
                float z = beats[b];
                float tt = Mathf.InverseLerp(-42f, 110f, z);
                float hw = Mathf.Lerp(5.5f, 56f, BezierT(tt, 0.15f, 0.62f));
                float h = Mathf.Lerp(8f, 52f, BezierT(tt, 0.2f, 0.7f));
                float cx = CenterOffset(tt);
                float gateW = (b == beats.Length - 1) ? hw * 0.92f : hw * 1.18f;
                float gateH = Mathf.Min((b == beats.Length - 1) ? h * 0.9f : h * 0.7f, 16f);
                Box(a, "BeatFrame_L_" + b, new Vector3(cx - gateW, gateH * 0.5f, z), new Vector3(7f, gateH, 5f), m_Wall);
                Box(a, "BeatFrame_R_" + b, new Vector3(cx + gateW, gateH * 0.5f, z), new Vector3(7f, gateH, 5f), m_Wall);
                Box(a, "BeatFrame_Top_" + b, new Vector3(cx, gateH, z), new Vector3(gateW * 2f + 7f, 3f, 5f), m_Wall);
                Box(a, "BeatTrim_L_" + b, new Vector3(cx - gateW + 3.6f, gateH * 0.5f, z - 2.2f), new Vector3(0.4f, gateH * 0.84f, 0.4f), m_Glow);
                Box(a, "BeatTrim_R_" + b, new Vector3(cx + gateW - 3.6f, gateH * 0.5f, z - 2.2f), new Vector3(0.4f, gateH * 0.84f, 0.4f), m_Glow);
            }
            for (int i = 0; i < 4; i++)
            {
                float z = 20f + i * 13f;
                float tt = Mathf.InverseLerp(-42f, 110f, z);
                float hw = Mathf.Lerp(5.5f, 56f, BezierT(tt, 0.15f, 0.62f));
                float cx = CenterOffset(tt);
                Pillar(a, "CourtColumn_W_" + i, new Vector3(cx - hw * 0.2f, 0f, z), 1.6f, 30f + i * 3f);
                Pillar(a, "CourtColumn_E_" + i, new Vector3(cx + hw * 0.2f, 0f, z), 1.6f, 30f + i * 3f);
            }
            Debug.Log("[Map0] 五殿门框 + 中庭柱列完成。");
        }

        /// <summary>
        /// 逐个物体改色：整块建筑每一个子项单独取色。
        ///   基底 = 按 z 的虹彩渐变（白 → 粉 → 淡紫 → 青 → 暖桃 → 金），
        ///   再按物体名字/位置做每件微差（色相偏移 + 亮度抖动），避免整面一个色。
        /// 泡泡、光泡、发光门线、粒子、雾、字幕、传送门不参与（保留自身材质）。
        /// </summary>
        private static void ApplyColorGradient(Transform root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            int painted = 0;
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (n == "WhiteTemplates" || n == "ArtTemplates" || n.StartsWith("TPL_")) continue;
                if (all[i].position.y < -100f) continue;
                if (n.Contains("Bubble") || n.Contains("Orb") || n.Contains("Droplet") || n.Contains("Glow")
                    || n.Contains("Trim") || n.Contains("Pad") || n.Contains("Pink") || n.Contains("Portal")
                    || n.Contains("Sunshaft") || n.Contains("Dust") || n.Contains("Fog") || n.Contains("Subtitle")
                    || n.Contains("Main Camera") || n.Contains("takoyaki"))
                    continue;

                Renderer r = all[i].GetComponent<Renderer>();
                if (r == null || !r.enabled) continue;

                // 每件物品的专属色：位置渐变 + 名字决定的小偏移
                int hash = 0;
                for (int k = 0; k < n.Length; k++) hash = (hash * 31 + n[k]) & 0x7fffffff;
                float phase = (hash % 100) / 100f;
                float tt = Mathf.InverseLerp(-42f, 110f, all[i].position.z);
                Color baseColor = RampColor(tt);
                Color item = Color.Lerp(baseColor, RampColor(Mathf.Repeat(tt + 0.45f * phase, 1f)), 0.5f);
                float jitter = 0.94f + (hash % 13) / 100f;              // 0.94 ~ 1.06
                item = new Color(item.r * jitter, item.g * jitter, item.b * jitter, 1f);
                Tint(r, item);
                painted++;
            }
            Debug.Log("[Map0] 逐件改色完成：" + painted + " 个物体（每个子项单独取色）。");
        }

        /// <summary>虹彩渐变：白 → 淡紫 → 天青 → 暖桃 → 玫金（参考图那种主色为白的绚烂）。</summary>
        private static Color RampColor(float t)
        {
            Color white = new Color(0.975f, 0.975f, 0.99f);
            Color lilac = new Color(0.93f, 0.86f, 1.00f);
            Color sky = new Color(0.78f, 0.93f, 1.00f);
            Color peach = new Color(1.00f, 0.90f, 0.82f);
            Color rose = new Color(1.00f, 0.82f, 0.90f);
            if (t < 0.30f) return Color.Lerp(white, lilac, Mathf.SmoothStep(0f, 1f, t / 0.30f));
            if (t < 0.55f) return Color.Lerp(lilac, sky, (t - 0.30f) / 0.25f);
            if (t < 0.78f) return Color.Lerp(sky, peach, (t - 0.55f) / 0.23f);
            return Color.Lerp(peach, rose, (t - 0.78f) / 0.22f);
        }

        /// <summary>
        /// 按殿做"有意义的建筑"：出生殿门框+碑、破坏殿断柱、恢复殿环形柱列+祭台、出口殿巨门内柱。
        /// 同时每个教学点摆一个操作提示区。
        /// </summary>
        private static void BuildMeaningfulArchitecture(Transform t)
        {
            Transform a = Group(t, "MeaningfulArch");

            // 出生殿：门框 + 三座碑
            Box(a, "Birth_Door_L", new Vector3(-5f, 4f, -28f), new Vector3(2f, 8f, 2f), m_Wall);
            Box(a, "Birth_Door_R", new Vector3(5f, 4f, -28f), new Vector3(2f, 8f, 2f), m_Wall);
            Box(a, "Birth_Door_Top", new Vector3(0f, 8.2f, -28f), new Vector3(12f, 1.2f, 2f), m_Wall);
            for (int i = 0; i < 3; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (7f + i);
                float z = -35f + i * 4.5f;
                Box(a, "Birth_Stele_" + i, new Vector3(x, 1.8f, z), new Vector3(1.3f, 3.6f, 0.7f), m_Wall);
                Box(a, "Birth_Rune_" + i, new Vector3(x, 2.3f, z - 0.4f), new Vector3(0.7f, 1.9f, 0.1f), m_Glow);
            }

            // 破坏殿：断柱（倾斜、断口）
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (9f + i * 1.5f);
                float z = -6f + i * 8f;
                float h = 2.4f + (i % 3) * 1.6f;
                GameObject col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                col.name = "BrokenColumn_" + i;
                col.transform.SetParent(a, false);
                col.transform.position = new Vector3(x, h * 0.5f, z);
                col.transform.localScale = new Vector3(1.5f, h * 0.5f, 1.5f);
                col.transform.rotation = Quaternion.Euler((i % 2 == 0 ? 1f : -1f) * (10f + i * 4f), i * 31f, (i % 2 == 0 ? -1f : 1f) * 8f);
                Renderer cr = col.GetComponent<Renderer>();
                if (cr != null) cr.sharedMaterial = m_Wall;
            }

            // 恢复殿：环形柱列 + 中央祭台
            Vector3 center = new Vector3(0f, 0f, 34f);
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * 16f, 0f, Mathf.Sin(ang) * 16f);
                Pillar(a, "RingColumn_" + i, p, 1.8f, 24f);
            }
            Box(a, "Recovery_Altar", new Vector3(0f, 0.8f, 34f), new Vector3(6f, 1.6f, 6f), m_Wall);
            Box(a, "Recovery_Altar_Step", new Vector3(0f, 0.2f, 34f), new Vector3(9f, 0.4f, 9f), m_Wall);
            GameObject orb = Sphere(a, "Recovery_Altar_Orb", new Vector3(0f, 2.8f, 34f), 2f, m_Glow);
            orb.GetComponent<Collider>().enabled = false;

            // 出口殿：巨门内侧两根高柱
            Pillar(a, "ExitInnerColumn_W", new Vector3(-15f, 0f, 94f), 2.6f, 30f);
            Pillar(a, "ExitInnerColumn_E", new Vector3(15f, 0f, 94f), 2.6f, 30f);

            // 每个教学点的操作提示
            HintZone(a, "Hint_Spawn", new Vector3(0f, 3f, -30f), new Vector3(26f, 6f, 10f), "按住左键蓄力，松开就发射泡泡。先往前走。");
            HintZone(a, "Hint_Wall", new Vector3(0f, 3f, -14f), new Vector3(22f, 8f, 8f), "切到弹力泡（2），把泡泡打进粉色泡泡里，填满就会炸开一条路。");
            HintZone(a, "Hint_Sticky", new Vector3(0f, 3f, 2f), new Vector3(22f, 6f, 8f), "按住左键把粘泡粘到发光带上，然后别松手——你会被提上去。");
            HintZone(a, "Hint_Bomb", new Vector3(0f, 3f, 41f), new Vector3(22f, 6f, 8f), "切到炸弹泡（3），朝泡膜发射，靠近后按 E 引爆。");
            HintZone(a, "Hint_Exit", new Vector3(0f, 8f, 96f), new Vector3(24f, 10f, 14f), "走到巨门前，按 E 离开这一章。");
            Debug.Log("[Map0] 有意义的建筑 + 5 个操作提示 已布置。");
        }

        private static void HintZone(Transform parent, string name, Vector3 pos, Vector3 size, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.AddComponent<PAO.Map0HintZone>().Configure(text);
        }

        /// <summary>三种泡泡的必修教学点（缺一种就过不去）：
        ///   ① 粘：2.6 m 高台，必须用浮粘泡滞留提拉上去；
        ///   ② 弹：粉色泡泡墙（已有）；
        ///   ③ 炸：被泡膜封死的窄门，必须用炸弹泡炸开。
        /// </summary>
        private static void BuildTutorialGates(Transform t)
        {
            Transform g = Group(t, "TutorialGates");

            // ① 粘 · 2.6 m 高台（z = 6）
            Box(g, "Sticky_Ledge", new Vector3(0f, 1.3f, 6f), new Vector3(24f, 2.6f, 8f), m_Floor);
            Box(g, "Sticky_Mark_W", new Vector3(-4f, 1.2f, 1.9f), new Vector3(3f, 1.8f, 0.3f), m_Glow);
            GameObject sw = GameObject.Find("Sticky_Mark_W");
            if (sw != null) sw.AddComponent<PAO.StickySurface>();
            Box(g, "Sticky_Mark_E", new Vector3(4f, 1.2f, 1.9f), new Vector3(3f, 1.8f, 0.3f), m_Glow);
            GameObject se = GameObject.Find("Sticky_Mark_E");
            if (se != null) se.AddComponent<PAO.StickySurface>();
            Box(g, "Sticky_Hint", new Vector3(0f, 3.4f, 2.2f), new Vector3(6f, 0.4f, 0.4f), m_Glow);

            // ③ 炸 · 泡膜封死的窄门（z = 46）
            Box(g, "BombGate_W", new Vector3(-7f, 3f, 46f), new Vector3(8f, 6f, 3f), m_Wall);
            Box(g, "BombGate_E", new Vector3(7f, 3f, 46f), new Vector3(8f, 6f, 3f), m_Wall);
            Box(g, "BombGate_Top", new Vector3(0f, 6.2f, 46f), new Vector3(6f, 1.2f, 3f), m_Wall);
            GameObject seal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            seal.name = "BombSeal";
            seal.transform.SetParent(g, false);
            seal.transform.position = new Vector3(0f, 3f, 46f);
            seal.transform.localScale = new Vector3(3.4f, 4.2f, 1.4f);
            Renderer sr = seal.GetComponent<Renderer>();
            if (sr != null) sr.sharedMaterial = m_BubbleReal;
            TerrainBubble tb = seal.AddComponent<TerrainBubble>();
            SetSerialized(tb, so =>
            {
                so.FindProperty("m_Capacity").intValue = 20;   // 只能炸，填不动
                so.FindProperty("m_RewardAmount").intValue = 0;
                so.FindProperty("m_RespawnDelay").floatValue = 0f;
            });
            Debug.Log("[Map0] 教学关已布置：粘高台 / 粉泡墙（弹）/ 炸弹封口。");
        }

        /// <summary>近景 90 + 远景轮廓 40：把真实模型铺满锥形空间。</summary>
        private static void BuildPropScatter(Transform t)
        {
            if (s_PropTemplates.Count == 0) { Debug.LogWarning("[Map0] 没有可铺模型。"); return; }
            Transform a = Group(t, "PropScatter");

            // 近景：只放 24 件，且都贴着两侧墙根（不挡路、不像垃圾堆）
            for (int i = 0; i < 24; i++)
            {
                float tt = (i + 0.5f) / 24f;
                float z = Mathf.Lerp(-36f, 102f, tt);
                float hw = Mathf.Lerp(5.5f, 56f, BezierT(tt, 0.15f, 0.62f));
                float cx = CenterOffset(tt);
                float side = (i % 2 == 0) ? -1f : 1f;
                float spread = 0.93f + ((i * 37) % 5) / 100f;
                float height = 0.9f + ((i * 53) % 12) / 10f;
                PlaceProp(a, i * 7, new Vector3(cx + side * hw * spread, 0f, z), (i * 47) % 360, height, i);
            }

            // 远景：只留 10 件大轮廓，间距拉大，不要堆
            for (int i = 0; i < 10; i++)
            {
                float tt = (i + 0.5f) / 10f;
                float z = Mathf.Lerp(-24f, 106f, tt);
                float hw = Mathf.Lerp(5.5f, 56f, BezierT(tt, 0.15f, 0.62f));
                float cx = CenterOffset(tt);
                float side = (i % 2 == 0) ? -1f : 1f;
                float x = cx + side * (hw + 20f + ((i * 29) % 10));
                float height = 9f + ((i * 41) % 9);
                PlaceProp(a, i * 13, new Vector3(x, 0f, z), (i * 31) % 360, height, i + 50);
            }
            Debug.Log("[Map0] 模型铺满完成：近景 24 / 远景 10。");
        }

        private static void PlaceAndTint(GameObject template, Transform parent, Vector3 pos, float yaw, float fitHeight, float maxWidth, Color color)
        {
            GameObject clone = PlacePiece(template, parent, pos, yaw, fitHeight, maxWidth);
            if (clone == null) return;
            Renderer[] rs = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) Tint(rs[i], color);
        }

        /// <summary>
        /// 真实资产披挂：沿贝塞尔锥面每 6 段贴一段 TFD 石构件（墙段/门/铺地），
        /// 让锥体不是光板白盒，而是"白石建筑"。
        /// </summary>
        private static void BuildRealCladding(Transform t)
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform a = Group(t, "RealCladding");

            List<GameObject> templates = new List<GameObject>();
            try
            {
                Scene wj = EditorSceneManager.OpenScene(kWengjiePath, OpenSceneMode.Additive);
                templates.AddRange(CollectTemplates(wj, scene, a,
                    new[] { "T_src_TFD_Wall_04A", "T_src_TFD_Wall_01A", "T_src_TFD_Gate_01A_Module", "T_src_TFD_Pavement_01A" }, 18));
                EditorSceneManager.CloseScene(wj, true);
            }
            catch (System.Exception e) { Debug.LogWarning("[Map0] 真实披挂模板加载失败：" + e.Message); }
            if (templates.Count == 0) { Debug.LogWarning("[Map0] 没收集到 TFD 模板，跳过真实披挂。"); return; }

            int wi = 0;
            const float segLen = 152f / 64f;
            for (int i = 0; i < 64; i++)
            {
                float tt = i / 64f;
                float z = Mathf.Lerp(-42f, 110f, tt);
                float hw = Mathf.Lerp(5.5f, 56f, BezierT(tt, 0.15f, 0.62f));
                float h = Mathf.Lerp(8f, 34f, BezierT(tt, 0.2f, 0.7f));
                float cx = CenterOffset(tt);

                // 每段墙的底色：白 → 暖金 → 玫红 → 淡紫，越往后越浓
                Color tint = RampColor(tt);

                int rows = h > 15f ? 2 : 1;
                for (int row = 0; row < rows; row++)
                {
                    float rowY = row * 9f;
                    float rowH = Mathf.Min(9f, h - rowY);
                    if (rowH < 2f) continue;
                    PlaceAndTint(templates[wi++ % templates.Count], a, new Vector3(cx - hw + 0.9f, rowY, z), 90f, rowH, segLen * 1.6f, tint);
                    PlaceAndTint(templates[wi++ % templates.Count], a, new Vector3(cx + hw - 0.9f, rowY, z), -90f, rowH, segLen * 1.6f, tint);
                }

                if (i % 3 == 0)
                    PlaceAndTint(templates[wi++ % templates.Count], a, new Vector3(cx, 0.02f, z), (i * 17f) % 360f, 0.3f, 8f, tint);
            }

            // 连续墙面铺上之后，把原来的白方块墙隐藏（碰撞保留）
            if (wi > 40)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Transform[] all = root.GetComponentsInChildren<Transform>(true);
                    for (int k = 0; k < all.Length; k++)
                    {
                        if (!all[k].name.StartsWith("ConeWall_", System.StringComparison.Ordinal)) continue;
                        Renderer rr = all[k].GetComponent<Renderer>();
                        if (rr != null) rr.enabled = false;
                    }
                }
            }
            Debug.Log("[Map0] 真实披挂完成：" + wi + " 件 TFD 石构件（连续墙面）。");
        }

        /// <summary>
        /// 色调的「从单一到多样」：出生区只有白 + 一抹粉；
        /// 进入 Hall 开始出现青、玫、琥珀；越靠近出口颜色越多越亮；
        /// 出口巨拱后是一整圈彩色光泡 —— 主色仍然是被白光打亮的白空间。
        /// </summary>
        private static void BuildColorProgression(Transform t)
        {
            Transform a = Group(t, "ColorProgression");

            Orb(a, new Vector3(-9f, 3.2f, 4f), 1.0f, 0);
            Orb(a, new Vector3(11f, 4.0f, 12f), 1.2f, 4);
            Orb(a, new Vector3(-15f, 3.6f, 18f), 0.9f, 3);

            Orb(a, new Vector3(-22f, 6f, 30f), 1.8f, 2);
            Orb(a, new Vector3(22f, 7f, 34f), 1.6f, 0);
            Orb(a, new Vector3(-10f, 9f, 42f), 2.2f, 4);
            Orb(a, new Vector3(12f, 10f, 48f), 2.0f, 3);
            Orb(a, new Vector3(-28f, 12f, 54f), 2.4f, 1);
            Orb(a, new Vector3(28f, 13f, 58f), 2.2f, 2);

            Orb(a, new Vector3(-8f, 12f, 64f), 1.8f, 0);
            Orb(a, new Vector3(8f, 14f, 68f), 2.0f, 3);
            Orb(a, new Vector3(-10f, 16f, 74f), 2.2f, 4);
            Orb(a, new Vector3(10f, 18f, 82f), 2.4f, 1);
            Orb(a, new Vector3(-9f, 20f, 90f), 2.6f, 2);
            Orb(a, new Vector3(9f, 22f, 96f), 2.8f, 0);

            for (int i = 0; i < 16; i++)
            {
                float ang = i * (360f / 16f) * Mathf.Deg2Rad;
                float radius = 18f + (i % 4) * 6f;
                float y = 16f + (i % 5) * 7f;
                Orb(a, new Vector3(Mathf.Cos(ang) * radius, y, 132f + Mathf.Sin(ang) * radius * 0.55f), 2.6f + (i % 3) * 0.8f, i % 5);
            }
            Orb(a, new Vector3(0f, 16f, 108f), 3.4f, 4);
            Orb(a, new Vector3(-13f, 13f, 104f), 2.8f, 3);
            Orb(a, new Vector3(13f, 14f, 106f), 3.0f, 1);

            ColoredLight(a, new Vector3(0f, 8f, 0f), new Color(1f, 0.6f, 0.72f), 8f, 32f);
            ColoredLight(a, new Vector3(0f, 10f, 20f), new Color(1f, 0.55f, 0.65f), 10f, 36f);
            ColoredLight(a, new Vector3(0f, 11f, 35f), new Color(1f, 0.85f, 0.45f), 12f, 38f);
            ColoredLight(a, new Vector3(0f, 12f, 50f), new Color(0.35f, 0.95f, 0.9f), 14f, 40f);
            ColoredLight(a, new Vector3(0f, 14f, 65f), new Color(0.55f, 0.7f, 1f), 16f, 42f);
            ColoredLight(a, new Vector3(0f, 16f, 80f), new Color(1f, 0.75f, 0.4f), 18f, 44f);
            ColoredLight(a, new Vector3(0f, 30f, 110f), new Color(0.7f, 0.55f, 1f), 22f, 52f);
            ColoredLight(a, new Vector3(0f, 28f, 132f), new Color(0.7f, 0.85f, 1f), 26f, 70f);
        }

        private static void Orb(Transform parent, Vector3 pos, float diameter, int colorIndex)
        {
            GameObject go = Sphere(parent, "ColorOrb_" + pos.z.ToString("F0") + "_" + colorIndex, pos, diameter, m_ColorOrbs[Mathf.Clamp(colorIndex, 0, m_ColorOrbs.Length - 1)]);
            go.GetComponent<Collider>().enabled = false;
        }

        private static void ColoredLight(Transform parent, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject("ColorLight");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void BuildSystems(Transform t, GameObject player)
        {
            Transform s = Group(t, "Systems");

            var spawn = new GameObject(kSpawnAnchor);
            spawn.transform.SetParent(s, false);
            spawn.transform.position = SpawnPos;
            spawn.transform.rotation = Quaternion.identity;

            var fall = new GameObject("FallRespawn");
            fall.transform.SetParent(s, false);
            fall.AddComponent<FallRespawn>().Configure(-10f, spawn.transform);

            // 启动兜底：CharacterController 未启用时自动补上；开场旁白主动请求一次
            var fix = new GameObject("Map0PlayerFix");
            fix.transform.SetParent(s, false);
            fix.AddComponent<PAO.Map0PlayerFix>();

            if (player != null)
            {
                player.transform.position = new Vector3(SpawnPos.x, 1.0f, SpawnPos.z);
                player.transform.rotation = Quaternion.identity;
            }
        }

        // ==================== 白空间气氛 ====================

        private static void BuildAtmosphere()
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform root = GameObject.Find(kRoot)?.transform;
            var atmos = new GameObject("Map0_TutorialAtmosphere");
            SceneManager.MoveGameObjectToScene(atmos, scene);
            if (root != null) atmos.transform.SetParent(root, true);

            // 光照：纯白、柔和
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.94f, 0.95f, 0.97f);
            RenderSettings.fogDensity = 0.008f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.9f, 0.92f, 0.95f);
            RenderSettings.ambientIntensity = 1.15f;
            RenderSettings.skybox = null;

            Light sun = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Light[] lights = go.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++) if (lights[i].type == LightType.Directional) { sun = lights[i]; break; }
                if (sun != null) break;
            }
            if (sun != null)
            {
                sun.color = new Color(1f, 0.985f, 0.96f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(55f, -28f, 0f);
            }

            var fillGo = new GameObject("Tutorial_FillLight");
            fillGo.transform.SetParent(atmos.transform, false);
            fillGo.transform.rotation = Quaternion.Euler(62f, 150f, 0f);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.86f, 0.92f, 1f);
            fill.intensity = 0.45f;
            fill.shadows = LightShadows.None;

            // 丁达尔光柱：从上方落进 Hall，给「庞大」和「震撼」加光
            GameObject shaftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab");
            if (shaftPrefab != null)
            {
                Vector3[] shaftSpots = { new Vector3(-6f, 22f, 6f), new Vector3(6f, 24f, 34f), new Vector3(0f, 30f, 62f), new Vector3(0f, 34f, 90f) };
                for (int i = 0; i < shaftSpots.Length; i++)
                {
                    GameObject shaft = (GameObject)PrefabUtility.InstantiatePrefab(shaftPrefab);
                    shaft.name = "Tutorial_Sunshaft_" + i;
                    SceneManager.MoveGameObjectToScene(shaft, scene);
                    shaft.transform.SetParent(atmos.transform, true);
                    shaft.transform.position = shaftSpots[i];
                    shaft.transform.rotation = Quaternion.identity;
                    shaft.transform.localScale = Vector3.one * (18f + i * 2f);
                }
            }

            // 后处理：白空间需要 Bloom + 暗角（旁白里说的“屏幕边缘不太自然的阴影”）
            GameObject volumeGo = new GameObject("Map0_WhitePostFX");
            volumeGo.transform.SetParent(atmos.transform, false);
            Volume volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = EnsureWhiteProfile();

            // Map4 同款的体积雾（Thalassophobia Fog 预制体）
            GameObject fogPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Fog.prefab");
            if (fogPrefab != null)
            {
                Vector3[] fogSpots = { new Vector3(0f, 5f, 26f), new Vector3(0f, 10f, 66f), new Vector3(0f, 16f, 102f) };
                for (int i = 0; i < fogSpots.Length; i++)
                {
                    GameObject fog = (GameObject)PrefabUtility.InstantiatePrefab(fogPrefab);
                    fog.name = "Map0_VolumeFog_" + i;
                    SceneManager.MoveGameObjectToScene(fog, scene);
                    fog.transform.SetParent(atmos.transform, true);
                    fog.transform.position = fogSpots[i];
                    fog.transform.localScale = Vector3.one * 3.5f;
                    DestroyColliders(fog);
                }
            }

            // 浮尘：从 Thalassophobia 拉环境粒子
            GameObject dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kDustPrefab);
            if (dustPrefab != null)
            {
                Vector3[] spots = { new Vector3(0f, 5f, -26f), new Vector3(0f, 6f, 0f), new Vector3(0f, 7f, 18f), new Vector3(14f, 6f, 40f) };
                for (int i = 0; i < spots.Length; i++)
                {
                    GameObject dust = (GameObject)PrefabUtility.InstantiatePrefab(dustPrefab);
                    dust.name = "Tutorial_Dust_" + i;
                    SceneManager.MoveGameObjectToScene(dust, scene);
                    dust.transform.SetParent(atmos.transform, true);
                    dust.transform.position = spots[i];
                    dust.transform.localScale = Vector3.one * 1.5f;
                }
            }

            // 字幕条：从 wengjie 克隆那套独立 Canvas（不依赖 UIRuntime，直接 Play 也能显示）
            Scene wj = EditorSceneManager.OpenScene(kWengjiePath, OpenSceneMode.Additive);
            GameObject subtitleSource = null;
            foreach (GameObject wjRoot in wj.GetRootGameObjects())
            {
                if (wjRoot != null && wjRoot.name == "Wengjie Subtitle") { subtitleSource = wjRoot; break; }
            }
            if (subtitleSource != null)
            {
                GameObject subtitle = Object.Instantiate(subtitleSource);
                subtitle.name = "Map0 Subtitle";
                SceneManager.MoveGameObjectToScene(subtitle, scene);
                subtitle.transform.SetParent(atmos.transform, true);
            }
            else Debug.LogWarning("[Map0 教程] wengjie 里找不到字幕条。");
            EditorSceneManager.CloseScene(wj, true);

            // 相机：白背景 + 去掉上一版水下残留的焦散面片
            Camera cam = Camera.main;
            if (cam != null)
            {
                Transform oldCaustics = cam.transform.Find("PAO_CausticsScreen");
                if (oldCaustics != null) Object.DestroyImmediate(oldCaustics.gameObject);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.96f, 0.965f, 0.975f);
            }
        }

        private static VolumeProfile EnsureWhiteProfile()
        {
            Directory.CreateDirectory(kProfileDir);
            const string path = "Assets/GameMain/Art/Profiles/Map0_WhiteProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (profile.components.Count == 0)
            {
                Bloom bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(0.9f);
                bloom.intensity.Override(0.85f);
                bloom.scatter.Override(0.8f);
                Vignette vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.28f);
                vignette.smoothness.Override(0.45f);
                ColorAdjustments color = profile.Add<ColorAdjustments>(true);
                color.postExposure.Override(0.35f);
                color.saturation.Override(4f);
                color.contrast.Override(8f);
                Tonemapping tonemapping = profile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.ACES);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            return profile;
        }

        // ==================== 旁白 ====================

        /// <summary>关键机制教学 5 段（数组顺序 = 关卡里的先后顺序）。</summary>
        private static readonly string[] kMechanicBeatIds = { "M-005", "M-007", "M-008", "M-011", "M-013" };

        /// <summary>
        /// 旁白（精简版）：只挂「关键机制教学」5 段的配音 + 触发区。
        ///
        /// 保留：M-005 破坏 / M-007 恢复 / M-008 消耗 / M-011 蓄力 / M-013 工具也是材料。
        /// 不挂：开场叙事（M-001～M-004）、出口提示（M-014）、结尾提问（M-016）以及 B/H/R/X 支线。
        /// 数据在 <see cref="Map0NarrationData"/>；配音在 Assets/GameMain/Audio/Map0VO。
        /// </summary>
        private static void BuildNarration()
        {
            Transform root = GameObject.Find(kRoot)?.transform;
            var go = new GameObject("Map0_Narration");
            if (root != null) go.transform.SetParent(root, true);
            go.transform.position = Vector3.zero;

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            PAO.Map0NarrationDirector director = go.AddComponent<PAO.Map0NarrationDirector>();

            SetSerialized(director, so =>
            {
                SerializedProperty clips = so.FindProperty("m_Clips");
                clips.arraySize = kMechanicBeatIds.Length;
                for (int i = 0; i < kMechanicBeatIds.Length; i++)
                {
                    SerializedProperty entry = clips.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("id").stringValue = kMechanicBeatIds[i];
                    entry.FindPropertyRelative("clip").objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<AudioClip>(VoicePath(kMechanicBeatIds[i]));
                }
                SerializedProperty idle = so.FindProperty("m_IdleBeats");
                idle.arraySize = 0;
            });

            BuildNarrationTriggers(go.transform);
        }

        private static string VoicePath(string beatId)
        {
            return "Assets/GameMain/Audio/Map0VO/" + beatId.ToLowerInvariant().Replace("-", "_") + ".wav";
        }

        /// <summary>
        /// 精简版触发区：只在机制出现的位置铺 5 个。
        /// 位置对齐关卡：粉墙前（破坏）→ 粉墙后（恢复）→ 资源区（消耗）→ 中段（蓄力）→ 爬升前（工具也是材料）。
        /// 原先的 21 个 PY-01 旧触发 + 2 个停留区已删。
        /// </summary>
        private static void BuildNarrationTriggers(Transform parent)
        {
            Trigger(parent, "Mech_M005", "M-005", null, new Vector3(0f, 3f, -21.5f), new Vector3(24f, 8f, 5f));
            Trigger(parent, "Mech_M007", "M-007", null, new Vector3(0f, 3f, -6f), new Vector3(30f, 8f, 10f));
            Trigger(parent, "Mech_M008", "M-008", null, new Vector3(0f, 3f, 10f), new Vector3(34f, 8f, 12f));
            Trigger(parent, "Mech_M011", "M-011", null, new Vector3(0f, 3f, 34f), new Vector3(34f, 8f, 12f));
            Trigger(parent, "Mech_M013", "M-013", null, new Vector3(0f, 3f, 58f), new Vector3(30f, 8f, 10f));
        }

        /// <summary>
        /// 只重建旁白（不动关卡摆放）：删掉场景里现有的 Map0_Narration（连同所有旧触发/停留区），
        /// 按 <see cref="Map0NarrationData"/> 重新生成 5 条配音 + 5 个机制触发区，然后保存场景。
        /// 菜单：Tools/PAO/关卡白盒/Map0 教程重构 · 只重建旁白（精简）
        /// </summary>
        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构 · 只重建旁白（精简）", priority = 59)]
        public static void RebuildNarrationOnly()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map0] 请先退出 Play 模式再重建旁白。"); return; }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath)
            {
                EditorSceneManager.SaveOpenScenes();
                scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);
            }

            Directory.CreateDirectory(kBackupDir);
            File.Copy(kScenePath, Path.Combine(kBackupDir,
                "Map0_before_narration_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity"), true);

            GameObject root = GameObject.Find(kRoot);
            if (root == null) { Debug.LogError("[Map0] 找不到 " + kRoot + "，旁白未重建。"); return; }

            Transform existing = root.transform.Find("Map0_Narration");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // 兜底：清掉不在 Map0_Narration 下面的游离旁白触发 / 停留区
            PAO.Map0NarrationTrigger[] strayTriggers =
                Object.FindObjectsByType<PAO.Map0NarrationTrigger>(FindObjectsSortMode.None);
            for (int i = 0; i < strayTriggers.Length; i++)
                if (strayTriggers[i] != null) Object.DestroyImmediate(strayTriggers[i].gameObject);
            PAO.Map0NarrationDwell[] strayDwells =
                Object.FindObjectsByType<PAO.Map0NarrationDwell>(FindObjectsSortMode.None);
            for (int i = 0; i < strayDwells.Length; i++)
                if (strayDwells[i] != null) Object.DestroyImmediate(strayDwells[i].gameObject);

            BuildNarration();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Map0] 旁白已精简重建：5 段关键机制教学 / 5 个触发区 / 开场不再强播旁白。场景已保存。");
            NarrationCheck();
        }

        private static void Trigger(Transform parent, string name, string beatId, string requires, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            go.AddComponent<PAO.Map0NarrationTrigger>().Configure(beatId, requires, true, 0f);
        }

        // ==================== 章节配置 ====================

        private static void WireChapterConfig()
        {
            PAO.UI.V2.UIConfig config = AssetDatabase.LoadAssetAtPath<PAO.UI.V2.UIConfig>("Assets/GameMain/Resources/PAOUIConfig.asset");
            if (config == null) { Debug.LogError("[Map0] 找不到 PAOUIConfig.asset。"); return; }
            SerializedObject so = new SerializedObject(config);
            SerializedProperty chapters = so.FindProperty("chapters");
            bool found = false;
            for (int i = 0; i < chapters.arraySize; i++)
            {
                SerializedProperty ch = chapters.GetArrayElementAtIndex(i);
                if (ch.FindPropertyRelative("scene").stringValue != "Map0") continue;
                ch.FindPropertyRelative("spawnAnchor").stringValue = kSpawnAnchor;
                ch.FindPropertyRelative("goalAnchor").stringValue = kGoalAnchor;
                ch.FindPropertyRelative("title").stringValue = "第一章 · 诞生";
                ch.FindPropertyRelative("description").stringValue = "一个泡泡在什么都没有的地方醒来。它学会破坏、恢复，也第一次付出代价。";
                ch.FindPropertyRelative("objective").stringValue = "打破前方的泡泡墙，走到出口。离开之前，记住那个问题。";
                found = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log(found ? "[Map0] 章节配置已接线（第一章 · 诞生）。" : "[Map0] PAOUIConfig 里没有 Map0 章节。");
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 教程重构 · 旁白自检", priority = 58)]
        public static void NarrationCheck()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Map0 旁白自检 · 关键机制教学精简版]");

            // 1) 数据
            int beatCount = PAO.Map0NarrationData.All.Length;
            int lineCount = 0;
            string dataIds = "";
            for (int i = 0; i < beatCount; i++)
            {
                lineCount += PAO.Map0NarrationData.All[i].lines.Length;
                dataIds += (i == 0 ? "" : " / ") + PAO.Map0NarrationData.All[i].id;
            }
            sb.AppendLine("  数据段： " + beatCount + " 段 / " + lineCount + " 句（" + dataIds + "）");
            sb.AppendLine("  精简规格：只留 破坏 / 恢复 / 消耗 / 蓄力 / 材料 5 段机制教学；开场叙事、出口提示、结尾提问不播");

            // 2) 播放器与配音
            PAO.Map0NarrationDirector director = Object.FindFirstObjectByType<PAO.Map0NarrationDirector>();
            if (director == null) sb.AppendLine("  ✘ 场景里没有 Map0NarrationDirector");
            else
            {
                SerializedObject so = new SerializedObject(director);
                SerializedProperty clips = so.FindProperty("m_Clips");
                int loaded = 0, missing = 0;
                for (int i = 0; i < clips.arraySize; i++)
                {
                    SerializedProperty entry = clips.GetArrayElementAtIndex(i);
                    string id = entry.FindPropertyRelative("id").stringValue;
                    AudioClip clip = entry.FindPropertyRelative("clip").objectReferenceValue as AudioClip;
                    if (clip != null) loaded++;
                    else { missing++; sb.AppendLine("  ✘ 缺配音：" + id); }
                    if (PAO.Map0NarrationData.Find(id) == null) sb.AppendLine("  ✘ 配音 id 在数据里找不到：" + id);
                }
                sb.AppendLine("  播放器：OK（配音 " + loaded + " 条，缺 " + missing + " 条）");
                if (clips.arraySize != PAO.Map0NarrationData.All.Length)
                    sb.AppendLine("  ✘ 配音条数（" + clips.arraySize + "）与数据段数（" + PAO.Map0NarrationData.All.Length + "）不一致");
            }

            // 3) 触发区
            PAO.Map0NarrationTrigger[] triggers = Object.FindObjectsByType<PAO.Map0NarrationTrigger>(FindObjectsSortMode.None);
            int badTrigger = 0;
            for (int i = 0; i < triggers.Length; i++)
            {
                SerializedObject so = new SerializedObject(triggers[i]);
                string id = so.FindProperty("m_BeatId").stringValue;
                if (PAO.Map0NarrationData.Find(id) == null) { sb.AppendLine("  ✘ 触发器指向不存在的段：" + triggers[i].name + " -> " + id); badTrigger++; }
            }
            sb.AppendLine("  触发区： " + triggers.Length + " 个（异常 " + badTrigger + " 个）");

            PAO.Map0NarrationDwell[] dwells = Object.FindObjectsByType<PAO.Map0NarrationDwell>(FindObjectsSortMode.None);
            sb.AppendLine("  停留区： " + dwells.Length + " 个" + (dwells.Length > 0 ? "（精简版应为 0）" : ""));

            // 4) 字幕条
            bool hasSubtitle = Object.FindFirstObjectByType<PAO.WengjieSubtitleOverlay>(FindObjectsInactive.Include) != null;
            sb.AppendLine("  字幕条： " + (hasSubtitle ? "OK" : "✘ 缺失（旁白会没字幕）"));

            // 5) 音频文件
            int wav = 0;
            foreach (string file in Directory.GetFiles("Assets/GameMain/Audio/Map0VO", "*.wav")) wav++;
            sb.AppendLine("  配音文件： " + wav + " 个（Assets/GameMain/Audio/Map0VO）");

            Debug.Log(sb.ToString());
        }

        // ==================== 构件 ====================

        private static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 pos, float diameter, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * diameter;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        private static Bounds Measure(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        private static void DestroyColliders(GameObject go)
        {
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) if (cols[i] != null) Object.DestroyImmediate(cols[i]);
        }

        private static readonly Material[] s_Pool = new Material[7];

        /// <summary>从 Map4/Map5/shiliuye/wengjie 抓真实模型到道具池。</summary>
        /// <summary>把多次采集产生的多个 WhiteTemplates 根合并成一个，避免 Hierarchy 垃圾。</summary>
        private static void ConsolidateTemplateHolders()
        {
            Transform first = null;
            int moved = 0;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.name.StartsWith("WhiteTemplates")) continue;
                if (first == null) { first = root.transform; continue; }
                root.transform.SetParent(first, true);
                moved++;
            }
            if (first != null) first.name = "WhiteTemplates (模板库)";
            Debug.Log("[Map0] 模板库合并：" + moved + " 个根物体。");
        }

        private static void LoadExtraPropTemplates(Scene dst)
        {
            if (s_PropTemplates.Count > 0) return;
            try
            {
                s_Pool[0] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Bldg_A.mat");
                s_Pool[1] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Bldg_B.mat");
                s_Pool[2] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Bldg_C.mat");
                s_Pool[3] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/Kelp 1.mat");
                s_Pool[4] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/Seagrass 1.mat");
                s_Pool[5] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat");
                s_Pool[6] = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue_B.mat");

                string[] scenes =
                {
                    "Assets/GameMain/Scenes/Map4.unity", "Assets/GameMain/Scenes/Map5.unity",
                    "Assets/GameMain/Scenes/shiliuye map.unity", "Assets/GameMain/Scenes/wengjie.unity"
                };
                string[] prefixes =
                {
                    "TFD_Rock", "TFD_Canyon", "TFD_Bush", "TFD_Grass", "TFD_Cloth", "TFD_Candle", "TFD_Beam",
                    "TFD_Ceiling", "TFD_Column", "TFD_Gate", "TFD_Wall", "TFD_Altar", "TFD_Brazier", "TFD_Web",
                    "TFD_Pavement", "TFD_Building", "Market_", "tou_", "pao_", "West_Coral", "Kelp", "Seagrass", "Coral"
                };
                for (int s = 0; s < scenes.Length; s++)
                {
                    try
                    {
                        Scene extra = EditorSceneManager.OpenScene(scenes[s], OpenSceneMode.Additive);
                        s_PropTemplates.AddRange(CollectTemplates(extra, dst, null, prefixes, 45));
                        EditorSceneManager.CloseScene(extra, true);
                    }
                    catch (System.Exception e) { Debug.LogWarning("[Map0] 采集失败 " + scenes[s] + "：" + e.Message); }
                }
                Debug.Log("[Map0] 道具·植被·建筑模型池：" + s_PropTemplates.Count + " 件");
            }
            catch (System.Exception e) { Debug.LogWarning("[Map0] 模型池加载失败：" + e.Message); }
        }

        private static void PlaceProp(Transform parent, int templateIndex, Vector3 pos, float yaw, float height, int seed)
        {
            if (s_PropTemplates.Count == 0) return;
            GameObject template = s_PropTemplates[Mathf.Abs(templateIndex) % s_PropTemplates.Count];
            GameObject clone = PlacePiece(template, parent, pos, yaw, height, height * 2f);
            if (clone == null) return;
            Material mat = s_Pool[Mathf.Abs(seed) % s_Pool.Length];
            if (mat != null)
            {
                Renderer[] rs = clone.GetComponentsInChildren<Renderer>(true);
                for (int k = 0; k < rs.Length; k++) rs[k].sharedMaterial = mat;
            }
            TintAll(clone, RampColor(Mathf.Repeat(Mathf.InverseLerp(-42f, 110f, pos.z) + (seed % 7) * 0.05f, 1f)));
        }

        private static void TintAll(GameObject go, Color color)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            for (int k = 0; k < rs.Length; k++) Tint(rs[k], color);
        }

        private static List<GameObject> CollectTemplates(Scene src, Scene dst, Transform parent, string[] prefixes, int max)
        {
            var templates = new List<GameObject>();
            var holder = new GameObject("WhiteTemplates");
            SceneManager.MoveGameObjectToScene(holder, dst);
            holder.transform.SetParent(parent, true);
            foreach (GameObject root in src.GetRootGameObjects())
            {
                if (root == null) continue;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length && templates.Count < max; i++)
                {
                    string n = all[i].name;
                    bool match = false;
                    for (int p = 0; p < prefixes.Length; p++) if (n.StartsWith(prefixes[p], System.StringComparison.Ordinal)) { match = true; break; }
                    if (!match || all[i].GetComponent<Renderer>() == null) continue;
                    GameObject clone = Object.Instantiate(all[i].gameObject);
                    clone.name = "TPL_" + n;
                    SceneManager.MoveGameObjectToScene(clone, dst);
                    clone.transform.SetParent(holder.transform, true);
                    clone.transform.position += Vector3.down * 500f;
                    DestroyColliders(clone);
                    templates.Add(clone);
                }
            }
            return templates;
        }

        private static GameObject PlacePiece(GameObject template, Transform parent, Vector3 pos, float yaw, float fitHeight, float maxWidth)
        {
            if (template == null) return null;
            GameObject clone = Object.Instantiate(template);
            clone.name = template.name.Replace("TPL_", "White_");
            SceneManager.MoveGameObjectToScene(clone, parent.gameObject.scene);
            clone.transform.SetParent(parent, true);
            clone.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Bounds b = Measure(clone);
            float scale = fitHeight > 0f ? fitHeight / Mathf.Max(0.001f, b.size.y) : 1f;
            if (maxWidth > 0f && b.size.x > 0f) scale = Mathf.Min(scale, maxWidth / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z)));
            clone.transform.localScale *= scale;
            Bounds after = Measure(clone);
            clone.transform.position += pos - new Vector3(after.center.x, after.min.y, after.center.z);

            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = m_Wall;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }
            return clone;
        }

        // ==================== 截图 ====================

        private static void Capture(string name, Vector3 pos, Quaternion rot)
        {
            Camera source = Camera.main;
            var go = new GameObject("__Map0TutorialCapture");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.96f, 0.965f, 0.975f);
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

        private static void SetSerialized(Object target, System.Action<SerializedObject> apply)
        {
            SerializedObject so = new SerializedObject(target);
            apply(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}