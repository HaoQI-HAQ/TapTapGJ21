using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PAO;
using PAO.BubbleTide;
using PAO.Underwater;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Map0「第一章 · 醒来」重构生成器。
    ///
    /// 【世界观 → 空间】《泡涌：意识之海》第一卷「醒来」：
    ///   A 醒来浅滩（开阔、空、只有一个泡泡）→ 学「粘」：造出第一条路
    ///   B 沉没回廊（断柱相碰成拱）→ 学「弹」（填充）与「炸」（破墙），第一次选分支
    ///   C 涨潮竖井（相变/潮水）→ 在潮水淹到祭坛前爬上去，日出收束
    ///
    /// 【幂等】只重建 Map0_Refactor 根；Directional Light 与玩家实例保留（玩家只挪位置）。
    ///
    /// 菜单：Tools/PAO/关卡白盒/Map0 重构（第一章 醒来）
    /// </summary>
    public static class Map0ChapterSetup
    {
        private const string kScenePath = "Assets/GameMain/Scenes/Map0.unity";
        private const string kRoot      = "Map0_Refactor";
        private const string kBackupDir = "Docs/Map0-refactor/backup";
        private const string kShotDir   = "Docs/Map0-refactor/shots";
        private const string kMatFolder = "Assets/GameMain/Art/WhiteboxMaterials/";

        private const string kSpawnAnchor = "Map0_Spawn";
        private const string kGoalAnchor  = "Map0_Goal";

        private static Material m_Ground, m_Edge, m_Platform, m_Landmark, m_Bounce, m_Sticky, m_Bomb, m_Void, m_Pop;

        // ==================== 尺寸常量 ====================
        private const float A_Floor   = 0f;
        private const float B_Floor   = 2.8f;     // 回廊地面（A 断墙的顶）
        private const float BranchY   = 12f;      // 分支上层道
        private const float C_EnterY  = 8f;       // 竖井入口平台
        private const float C_Bottom  = 4f;       // 井底
        private const float C_Top     = 36.5f;    // 塔顶
        private const float ShaftMinX = -26f, ShaftMaxX = 26f;
        private const float ShaftMinZ = 114f, ShaftMaxZ = 166f;

        private static readonly Vector3 ShaftCenter = new Vector3(0f, 0f, 140f);

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 旧版水下（已废弃）", priority = 69)]
        public static void BuildAll()
        {
            if (!Begin()) return;
            BuildWorld();
            DecorateScene(SceneManager.GetActiveScene());
            WireChapterConfig();
            End("第一章「醒来」空间 + 机制 + 系统 + 美术/光/水 已重建");
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 只重建空间", priority = 61)]
        public static void BuildShellOnly()
        {
            if (!Begin()) return;
            BuildWorld();
            End("Map0 空间壳 已重建");
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 接线章节配置", priority = 64)]
        public static void WireChapterConfig()
        {
            PAO.UI.V2.UIConfig config = AssetDatabase.LoadAssetAtPath<PAO.UI.V2.UIConfig>("Assets/GameMain/Resources/PAOUIConfig.asset");
            if (config == null)
            {
                Debug.LogError("[Map0] 找不到 PAOUIConfig.asset。");
                return;
            }

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
                ch.FindPropertyRelative("objective").stringValue = "走到出口。离开之前，记住那个问题。";
                found = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            if (found)
                Debug.Log("[Map0] 章节配置已接线：spawnAnchor=" + kSpawnAnchor + "，goalAnchor=" + kGoalAnchor);
            else
                Debug.LogWarning("[Map0] PAOUIConfig 里没有 scene=Map0 的章节。");
        }

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 场景报告", priority = 65)]
        public static void SceneReport()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath)
            {
                Debug.LogWarning("[Map0] 当前场景不是 Map0： " + scene.name);
                return;
            }

            Bounds b = default(Bounds);
            bool has = false;
            int renderers = 0;
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

            var sb = new StringBuilder();
            sb.AppendLine("[Map0] 场景报告 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("  Renderer: " + renderers);
            if (has)
                sb.AppendLine("  Bounds: center=" + b.center + " size=" + b.size + " min=" + b.min + " max=" + b.max);
            foreach (GameObject root in scene.GetRootGameObjects())
                sb.AppendLine("  Root: " + root.name + "  children=" + root.transform.hierarchyCount);
            Debug.Log(sb.ToString());
        }

        // ==================== 场景准备 ====================

        private static bool Begin()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[Map0] 请先退出 Play 模式再重建。");
                return false;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.path != kScenePath)
            {
                EditorSceneManager.SaveOpenScenes();
                active = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);
            }
            else
            {
                EditorSceneManager.SaveScene(active);
            }

            Directory.CreateDirectory(kBackupDir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(active.path, Path.Combine(kBackupDir, "Map0_before_" + stamp + ".unity"), true);

            LoadMaterials();
            return true;
        }

        private static void End(string message)
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Map0 重构] " + message + "\n  根物体：" + kRoot + "  备份：Docs/Map0-refactor/backup/");
        }

        private static void LoadMaterials()
        {
            m_Ground   = Mat("WB_Ground.mat");
            m_Edge     = Mat("WB_Edge.mat");
            m_Platform = Mat("WB_Platform.mat");
            m_Landmark = Mat("WB_Landmark.mat");
            m_Bounce   = Mat("WB_Bounce.mat");
            m_Sticky   = Mat("WB_FloatSticky.mat");
            m_Bomb     = Mat("WB_Bomb.mat");
            m_Void     = Mat("WB_Void.mat");
            m_Pop      = Mat("WB_Destructible.mat");
        }

        private static Material Mat(string file)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(kMatFolder + file);
        }

        // ==================== 世界 ====================

        private static void BuildWorld()
        {
            Scene scene = SceneManager.GetActiveScene();

            // 1) 清掉旧内容：保留灯与玩家
            GameObject player = null;
            PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc != null) player = pc.gameObject;

            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot == null) continue;
                if (sceneRoot.name == "Directional Light") continue;
                if (player != null && sceneRoot == player) continue;
                Object.DestroyImmediate(sceneRoot);
            }

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            BuildShallows(t);
            BuildCorridor(t);
            BuildBranch(t);
            BuildWell(t);
            BuildSystems(t, player);
        }

        // ---------------- A 醒来浅滩 ----------------

        private static void BuildShallows(Transform t)
        {
            Transform a = Group(t, "A_Shallows");

            // 大地面：x -32..32，z -28..8
            Box(a, "A_Ground", new Vector3(0f, -0.5f, -10f), new Vector3(64f, 1f, 36f), m_Ground);

            // 出生点前方的一道 2.8 m 断墙（必须用粘泡提拉上去）
            Box(a, "A_Terrace", new Vector3(0f, 1.4f, 12f), new Vector3(30f, 2.8f, 8f), m_Ground);

            // 可粘带：断墙正面两块发亮的可粘面
            Sticky(a, "A_Sticky_Left",  new Vector3(-5.5f, 1.6f, 7.85f), new Vector3(2.6f, 1.8f, 0.3f));
            Sticky(a, "A_Sticky_Right", new Vector3(5.5f, 1.6f, 7.85f), new Vector3(2.6f, 1.8f, 0.3f));

            // 断墙顶的泡泡泉（填满给值）
            Spring(a, "A_Terrace_Spring", new Vector3(9f, 3.7f, 12f), 2);

            // 两侧的低矮残墙，防止误掉
            Box(a, "A_Ruin_W", new Vector3(-31.5f, 0.6f, -10f), new Vector3(1f, 1.2f, 36f), m_Edge, false);
            Box(a, "A_Ruin_E", new Vector3(31.5f, 0.6f, -10f), new Vector3(1f, 1.2f, 36f), m_Edge, false);

            // 散落的断柱（A 段只作为「废墟」的暗示，不挡路）
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (18f + (i / 2) * 4f);
                float z = -22f + i * 3.4f;
                float h = 1.2f + (i % 3) * 0.7f;
                Box(a, "A_Rubble_" + i, new Vector3(x, h * 0.5f, z), new Vector3(1.1f, h, 1.1f), m_Landmark);
            }

            // 两截倒下的梁
            Box(a, "A_FallenBeam_0", new Vector3(-14f, 0.4f, -2f), new Vector3(8f, 0.7f, 0.9f), m_Landmark).transform.rotation = Quaternion.Euler(0f, 24f, 0f);
            Box(a, "A_FallenBeam_1", new Vector3(15f, 0.4f, -6f), new Vector3(7f, 0.7f, 0.9f), m_Landmark).transform.rotation = Quaternion.Euler(0f, -18f, 0f);

            // 「第一个泡泡」：悬在井口上方的大泡，出生点抬头就能看见（长视距锚点）
            GameObject firstBubble = Sphere(a, "A_FirstBubble_Landmark", new Vector3(0f, 60f, 150f), 16f, m_Bounce);
            firstBubble.GetComponent<Collider>().enabled = false;

            // 回廊入口的两根门柱（只收窄、不封顶，保证出生点的长视距能看到井口的「第一个泡泡」）
            Box(a, "A_Gate_L", new Vector3(-11f, B_Floor + 4f, 16f), new Vector3(2f, 8f, 3f), m_Landmark);
            Box(a, "A_Gate_R", new Vector3(11f, B_Floor + 4f, 16f), new Vector3(2f, 8f, 3f), m_Landmark);

            // 出生点这一侧的两段矮矮的引导石（把视线夹向门洞）
            Box(a, "A_Sight_Guide_L", new Vector3(-6.5f, A_Floor + 0.35f, -12f), new Vector3(7f, 0.7f, 1.2f), m_Landmark, false);
            Box(a, "A_Sight_Guide_R", new Vector3(6.5f, A_Floor + 0.35f, -12f), new Vector3(7f, 0.7f, 1.2f), m_Landmark, false);
        }

        // ---------------- B 沉没回廊 ----------------

        private static void BuildCorridor(Transform t)
        {
            Transform b = Group(t, "B_Colonnade");

            // 回廊地面：z 16..114，x -12..12
            Box(b, "B_Ground", new Vector3(0f, B_Floor - 0.5f, 65f), new Vector3(24f, 1f, 98f), m_Ground);

            // 两侧矮护栏（残墙）
            Box(b, "B_Rail_W", new Vector3(-12f, B_Floor + 0.6f, 65f), new Vector3(1f, 1.2f, 98f), m_Edge, false);
            Box(b, "B_Rail_E", new Vector3(12f, B_Floor + 0.6f, 65f), new Vector3(1f, 1.2f, 98f), m_Edge, false);

            // 9 对石柱（x=±9，每 10 m 一对）
            for (int i = 0; i < 9; i++)
            {
                float z = 22f + i * 10f;
                Box(b, "B_Column_W_" + i, new Vector3(-9f, B_Floor + 5f, z), new Vector3(1.5f, 10f, 1.5f), m_Landmark);
                Box(b, "B_Column_E_" + i, new Vector3(9f, B_Floor + 5f, z), new Vector3(1.5f, 10f, 1.5f), m_Landmark);

                // 隔一根挂一道断拱（视觉上「两两相碰」）
                if (i % 2 == 0)
                    Box(b, "B_Arch_" + i, new Vector3(0f, B_Floor + 10.3f, z), new Vector3(18f, 0.9f, 1.2f), m_Landmark);
            }

            // ---- B1 弹 · 填充锁（z = 26）：通道收窄到 5 m，一颗大泡塞死 ----
            Box(b, "B1_Narrow_W", new Vector3(-7.25f, B_Floor + 3f, 26f), new Vector3(9.5f, 6f, 4f), m_Edge);
            Box(b, "B1_Narrow_E", new Vector3(7.25f, B_Floor + 3f, 26f), new Vector3(9.5f, 6f, 4f), m_Edge);
            Box(b, "B1_Lintel", new Vector3(0f, B_Floor + 6.6f, 26f), new Vector3(5f, 1.4f, 4f), m_Edge);
            FillLock(b, "B1_Plug", new Vector3(0f, B_Floor + 2.3f, 26f), 4.2f, 3, 6);

            // ---- B2 粘 · 高台（z = 42）：4.5 m 高的断台，必须提拉 ----
            Box(b, "B2_Wall", new Vector3(0f, B_Floor + 2.25f, 42f), new Vector3(24f, 4.5f, 5f), m_Edge);
            Sticky(b, "B2_Sticky_W", new Vector3(-3.5f, B_Floor + 2.1f, 39.4f), new Vector3(3f, 2.2f, 0.3f));
            Sticky(b, "B2_Sticky_E", new Vector3(3.5f, B_Floor + 2.1f, 39.4f), new Vector3(3f, 2.2f, 0.3f));

            // 高台顶：z 44..54，y = B_Floor + 4.5
            float highY = B_Floor + 4.5f;
            Box(b, "B2_HighWalk", new Vector3(0f, highY - 0.5f, 49f), new Vector3(20f, 1f, 10f), m_Platform);
            Spring(b, "B2_High_Spring", new Vector3(-6f, highY + 0.8f, 49f), 2);

            // 从高台下来的斜坡（z 54..62 降到回廊地面）
            Box(b, "B2_Descend", new Vector3(0f, highY - 1.25f, 58.5f), new Vector3(10f, 0.6f, 9f), m_Platform)
                .transform.rotation = Quaternion.Euler(-14f, 0f, 0f);

            // ---- B3 炸 · 裂墙（z = 64）：泡膜封住 5 m 宽的裂口 ----
            Box(b, "B3_Jamb_W", new Vector3(-7.25f, B_Floor + 3f, 64f), new Vector3(9.5f, 6f, 3f), m_Edge);
            Box(b, "B3_Jamb_E", new Vector3(7.25f, B_Floor + 3f, 64f), new Vector3(9.5f, 6f, 3f), m_Edge);
            Box(b, "B3_Lintel", new Vector3(0f, B_Floor + 6.8f, 64f), new Vector3(6f, 1.6f, 3f), m_Edge);
            Breakable(b, "B3_CrackedSeal", new Vector3(0f, B_Floor + 2.9f, 64f), new Vector3(5.6f, 5.4f, 1.6f), 12);

            // ---- B4 断桥 + 扫杆（z 78..92）----
            Box(b, "B4_GapEdge_S", new Vector3(0f, B_Floor - 0.5f, 77f), new Vector3(24f, 1f, 4f), m_Ground);
            Box(b, "B4_GapEdge_N", new Vector3(0f, B_Floor - 0.5f, 93f), new Vector3(24f, 1f, 4f), m_Ground);
            Box(b, "B4_Beam", new Vector3(0f, B_Floor - 0.25f, 85f), new Vector3(1.6f, 0.5f, 14f), m_Platform);

            // 扫杆：pivot 在桥上方，横杆绕 Y 轴来回扫
            var pivot = new GameObject("B4_SweepPivot");
            pivot.transform.SetParent(b, false);
            pivot.transform.position = new Vector3(0f, B_Floor + 1.2f, 85f);
            RotatingHazard sweep = pivot.AddComponent<RotatingHazard>();
            sweep.Configure(Vector3.up, 42f, true, 78f);
            GameObject bar = Box(pivot.transform, "B4_SweepBar", Vector3.zero, new Vector3(11f, 0.5f, 0.6f), m_Bomb);
            bar.transform.localPosition = new Vector3(5.5f, 0f, 0f);
            bar.AddComponent<BubblePopObstacle>().Configure(false, false);

            // ---- B 尾段：上到 C 入口平台的台阶（z 100..108）----
            for (int i = 0; i < 6; i++)
            {
                float y = B_Floor + (i + 1) * 0.87f;
                Box(b, "B_Stair_C_" + i, new Vector3(0f, y - 0.435f, 100.5f + i * 1.4f), new Vector3(10f, 0.87f, 1.5f), m_Platform);
            }
        }

        // ---------------- 分支 上层道 ----------------

        private static void BuildBranch(Transform t)
        {
            Transform br = Group(t, "Branch_UpperWalk");

            // 入口梯：从回廊地面 x=±10 处上到 y=12（10 级，每级 0.92）
            for (int i = 0; i < 10; i++)
            {
                float y = B_Floor + (i + 1) * 0.92f;
                Box(br, "Branch_Stair_" + i, new Vector3(10.8f, y - 0.46f, 52f + i * 1.3f), new Vector3(3.4f, 0.92f, 1.4f), m_Platform);
            }

            // 上层道：x 12.5..19.5，z 64..104，y = 12
            Box(br, "Branch_Walk", new Vector3(16f, BranchY - 0.5f, 84f), new Vector3(7f, 1f, 40f), m_Platform);
            // 外侧护栏
            Box(br, "Branch_Rail", new Vector3(19.5f, BranchY + 0.6f, 84f), new Vector3(0.5f, 1.2f, 40f), m_Edge, false);
            // 内侧矮沿
            Box(br, "Branch_Kerb", new Vector3(12.6f, BranchY + 0.3f, 84f), new Vector3(0.4f, 0.6f, 40f), m_Edge, false);

            // 道上的 2 颗泡泡泉
            Spring(br, "Branch_Spring_0", new Vector3(16f, BranchY + 0.8f, 74f), 3);
            Spring(br, "Branch_Spring_1", new Vector3(16f, BranchY + 0.8f, 96f), 3);

            // 下方的“接住层”：从上层道掉下来会落在回廊外的低台上，走回入口梯即可，不会整关重来
            Box(br, "Branch_CatchFloor", new Vector3(17f, B_Floor - 0.5f, 79f), new Vector3(11f, 1f, 56f), m_Ground);
            Box(br, "Branch_CatchRail", new Vector3(22.5f, B_Floor + 0.6f, 79f), new Vector3(0.6f, 1.2f, 56f), m_Edge, false);

            // 尽头下到 C 入口平台（y=8）的台阶
            for (int i = 0; i < 5; i++)
            {
                float y = BranchY - (i + 1) * 0.8f;
                Box(br, "Branch_Drop_" + i, new Vector3(16f, y - 0.4f, 104f + i * 1.4f), new Vector3(6f, 0.8f, 1.5f), m_Platform);
            }
        }

        // ---------------- C 涨潮竖井 ----------------

        private static void BuildWell(Transform t)
        {
            Transform c = Group(t, "C_RisingWell");

            // 入口平台：z 104..112，y=8
            Box(c, "C_EntryTerrace", new Vector3(0f, C_EnterY - 0.5f, 108f), new Vector3(26f, 1f, 8f), m_Platform);

            // 井底
            Box(c, "C_Bottom", new Vector3(0f, C_Bottom - 0.5f, 140f), new Vector3(52f, 1f, 52f), m_Ground);

            // 井壁（南墙留门）
            float wallH = C_Top + 3f - C_Bottom;
            float wallMidY = (C_Bottom + C_Top + 3f) * 0.5f;
            Box(c, "C_Wall_N", new Vector3(0f, wallMidY, ShaftMaxZ), new Vector3(54f, wallH, 2f), m_Edge);
            Box(c, "C_Wall_E", new Vector3(ShaftMaxX, wallMidY, 140f), new Vector3(2f, wallH, 52f), m_Edge);
            Box(c, "C_Wall_W", new Vector3(ShaftMinX, wallMidY, 140f), new Vector3(2f, wallH, 52f), m_Edge);
            Box(c, "C_Wall_S_L", new Vector3(-17f, wallMidY, ShaftMinZ), new Vector3(18f, wallH, 2f), m_Edge);
            Box(c, "C_Wall_S_R", new Vector3(17f, wallMidY, ShaftMinZ), new Vector3(18f, wallH, 2f), m_Edge);
            Box(c, "C_Wall_S_Top", new Vector3(0f, wallMidY + 8f, ShaftMinZ), new Vector3(16f, wallH - 16f, 2f), m_Edge);

            // 五层螺旋岩台（12 x 12），每层转 72°
            object[][] ledges =
            {
                new object[] { new Vector3(0f, 12.0f, 120f) },
                new object[] { new Vector3(-7f, 16.5f, 126f) },
                new object[] { new Vector3(6f, 21.0f, 132f) },
                new object[] { new Vector3(-5f, 25.5f, 138f) },
                new object[] { new Vector3(7f, 30.0f, 144f) },
                new object[] { new Vector3(0f, 35.6f, 149f) },
            };
            for (int i = 0; i < ledges.Length; i++)
            {
                Vector3 p = (Vector3)ledges[i][0];
                Box(c, "C_Ledge_" + i, new Vector3(p.x, p.y - 0.5f, p.z), new Vector3(13f, 1f, 13f), m_Platform);
                // 岩台边上一块矮的发光“苔”（提示这里可以落脚；垂直上升靠滞留提拉）
                if (i % 2 == 0)
                    Sticky(c, "C_LedgeMoss_" + i, p + new Vector3(0f, 0.18f, 0f), new Vector3(3f, 0.2f, 3f));
            }

            // 塔顶平台（南侧留井口）
            float topY = C_Top;
            Box(c, "C_Top_S", new Vector3(0f, topY - 0.5f, 160f), new Vector3(40f, 1f, 14f), m_Landmark);
            Box(c, "C_Top_W", new Vector3(-17f, topY - 0.5f, 149f), new Vector3(6f, 1f, 12f), m_Landmark);
            Box(c, "C_Top_E", new Vector3(17f, topY - 0.5f, 149f), new Vector3(6f, 1f, 12f), m_Landmark);

            // 祭坛
            Box(c, "C_Altar_Base", new Vector3(0f, topY + 0.4f, 160f), new Vector3(5f, 0.8f, 5f), m_Platform);
            Box(c, "C_Altar_Core", new Vector3(0f, topY + 1.2f, 160f), new Vector3(2.4f, 1.6f, 2.4f), m_Bounce);
            // 祭坛上那颗「光」：终点在塔顶收束成一颗发光的泡泡
            GameObject altarOrb = Sphere(c, "C_Altar_LightBubble", new Vector3(0f, topY + 2.9f, 160f), 2.4f, m_Bounce);
            altarOrb.GetComponent<Collider>().enabled = false;

            // 井底上升气流：掉下去不死，被送回第一层附近
            var updraft = new GameObject("C_Updraft");
            updraft.transform.SetParent(c, false);
            updraft.transform.position = new Vector3(0f, C_Bottom + 6f, 140f);
            BoxCollider ud = updraft.AddComponent<BoxCollider>();
            ud.isTrigger = true;
            ud.size = new Vector3(12f, 12f, 12f);
            updraft.AddComponent<WhiteboxUpdraft>().Configure(11f);

            // 潮水：总控 + 8 源 + 终点 + 开关
            var tide = new GameObject("C_BubbleTide");
            tide.transform.SetParent(c, false);
            tide.transform.position = new Vector3(0f, 0f, 140f);
            BubbleTideDirector director = tide.AddComponent<BubbleTideDirector>();
            SetSerialized(director, so =>
            {
                so.FindProperty("m_AutoStart").boolValue = false;
                so.FindProperty("m_BaseSpeed").floatValue = 0.8f;
                so.FindProperty("m_CellSize").floatValue = 0.8f;
                so.FindProperty("m_BubbleRadius").floatValue = 0.34f;
                so.FindProperty("m_BubbleSpacing").floatValue = 0.5f;
                so.FindProperty("m_PushSpeed").floatValue = 3.5f;
                so.FindProperty("m_UseExplicitBounds").boolValue = true;
                so.FindProperty("m_BoundsMin").vector3Value = new Vector3(ShaftMinX, C_Bottom, ShaftMinZ - 2f);
                so.FindProperty("m_BoundsMax").vector3Value = new Vector3(ShaftMaxX, C_Top + 2f, ShaftMaxZ);
                so.FindProperty("m_StartDelay").floatValue = 1.5f;
            });

            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                var anchor = new GameObject("C_TideAnchor_" + i);
                anchor.transform.SetParent(c, false);
                anchor.transform.position = new Vector3(Mathf.Cos(ang) * 21f, C_Bottom + 0.6f, 140f + Mathf.Sin(ang) * 20f);
                anchor.AddComponent<BubbleTideAnchor>();
            }

            // 潮水可视面（不依赖渲染 Feature）：一张朝上、一张朝下的半透明水面
            Material tideMat = EnsureTideWaterMaterial();
            var surfaceRoot = new GameObject("C_TideSurface");
            surfaceRoot.transform.SetParent(c, false);
            surfaceRoot.transform.position = new Vector3(0f, C_Bottom + 0.2f, 140f);
            for (int i = 0; i < 2; i++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = i == 0 ? "Surface_Up" : "Surface_Down";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(surfaceRoot.transform, false);
                quad.transform.localPosition = new Vector3(0f, i * 0.03f, 0f);
                quad.transform.localRotation = Quaternion.Euler(i == 0 ? -90f : 90f, 0f, 0f);
                quad.transform.localScale = new Vector3(52f, 52f, 1f);
                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = tideMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            PAO.Map0TideSurface surface = surfaceRoot.AddComponent<PAO.Map0TideSurface>();
            surface.Configure(C_Bottom + 0.2f, C_Top + 1f, new Vector2(0f, 140f));

            // 终点标记（潮水判定）
            var goal = new GameObject(kGoalAnchor);
            goal.transform.SetParent(c, false);
            goal.transform.position = new Vector3(0f, topY + 2.1f, 160f);
            BubbleTideGoal tideGoal = goal.AddComponent<BubbleTideGoal>();
            SetSerialized(tideGoal, so =>
            {
                so.FindProperty("m_Radius").floatValue = 0.8f;
                so.FindProperty("m_GraceSeconds").floatValue = 8f;
                so.FindProperty("m_ReachDistance").floatValue = 3f;
                so.FindProperty("m_RequireConfirmKey").boolValue = false;
            });
            BoxCollider goalCollider = goal.AddComponent<BoxCollider>();
            goalCollider.size = new Vector3(3f, 3f, 3f);

            // 潮水开关：走进竖井入口开始涨潮
            var gate = new GameObject("C_TideGate");
            gate.transform.SetParent(c, false);
            gate.transform.position = new Vector3(0f, C_EnterY + 2f, 113f);
            BoxCollider gateTrigger = gate.AddComponent<BoxCollider>();
            gateTrigger.isTrigger = true;
            gateTrigger.size = new Vector3(22f, 6f, 2f);
            WengjieTideGate tideGate = gate.AddComponent<WengjieTideGate>();
            SetSerialized(tideGate, so => so.FindProperty("m_Delay").floatValue = 1.2f);
        }

        // ---------------- 系统 ----------------

        private static void BuildSystems(Transform t, GameObject player)
        {
            Transform s = Group(t, "Systems");

            var spawn = new GameObject(kSpawnAnchor);
            spawn.transform.SetParent(s, false);
            spawn.transform.position = new Vector3(0f, A_Floor + 0.2f, -20f);
            spawn.transform.rotation = Quaternion.identity;

            var fall = new GameObject("FallRespawn");
            fall.transform.SetParent(s, false);
            fall.AddComponent<FallRespawn>().Configure(-8f, spawn.transform);

            if (player != null)
            {
                player.transform.position = new Vector3(0f, A_Floor + 1.2f, -20f);
                player.transform.rotation = Quaternion.identity;
            }

            BuildNarration(s);
        }

        /// <summary>
        /// 第一章旁白（《泡涌》(2).docx 第一章「诞生」）：
        ///   · 一个 Map0NarrationDirector 管队列与音频；
        ///   · 14 个主线 M 段按空间顺序摆放触发区；
        ///   · 若干反应段（走反 / 回头 / 长时间静止）挂在同一套触发区或 idle 表上。
        /// 隐藏段（H）与需要计数器的反应段（R-001/003/004/005、X-002/003）本轮只进数据，不摆触发。
        /// </summary>
        private static void BuildNarration(Transform parent)
        {
            var go = new GameObject("Map0_Narration");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;

            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;

            PAO.Map0NarrationDirector director = go.AddComponent<PAO.Map0NarrationDirector>();

            // ---- 配音表：有 wav 的段挂上，没有的段只走字幕 ----
            string[] voiced =
            {
                "PY-01-M-001", "PY-01-M-002", "PY-01-M-003", "PY-01-M-004", "PY-01-M-005",
                "PY-01-M-006", "PY-01-M-007", "PY-01-M-008", "PY-01-M-009", "PY-01-M-010",
                "PY-01-M-011", "PY-01-M-012", "PY-01-M-013", "PY-01-M-014",
                "PY-01-B-001", "PY-01-B-002", "PY-01-B-005", "PY-01-B-007",
                "PY-01-R-006", "PY-01-X-001",
            };

            SetSerialized(director, so =>
            {
                SerializedProperty clips = so.FindProperty("m_Clips");
                clips.arraySize = voiced.Length;
                for (int i = 0; i < voiced.Length; i++)
                {
                    SerializedProperty entry = clips.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("id").stringValue = voiced[i];
                    string file = voiced[i].Replace("PY-01-", "").ToLowerInvariant().Replace("-", "_") + ".wav";
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameMain/Audio/Map0VO/" + file);
                    entry.FindPropertyRelative("clip").objectReferenceValue = clip;
                }

                SerializedProperty idle = so.FindProperty("m_IdleBeats");
                idle.arraySize = 3;
                SetIdle(idle.GetArrayElementAtIndex(0), 15f, "PY-01-B-001");
                SetIdle(idle.GetArrayElementAtIndex(1), 35f, "PY-01-B-002");
                SetIdle(idle.GetArrayElementAtIndex(2), 70f, "PY-01-R-006");
            });

            // ---- 主线段（按空间推进）----
            NarrationTrigger(parent, "N_M001", "PY-01-M-001", null, new Vector3(0f, 2.5f, -20f), new Vector3(26f, 8f, 18f));
            NarrationTrigger(parent, "N_M002", "PY-01-M-002", null, new Vector3(0f, 2.5f, -12f), new Vector3(24f, 8f, 8f));
            NarrationTrigger(parent, "N_M003", "PY-01-M-003", null, new Vector3(0f, 3.5f, -5f), new Vector3(24f, 8f, 8f));
            NarrationTrigger(parent, "N_M004", "PY-01-M-004", null, new Vector3(0f, 3.5f, 2f), new Vector3(24f, 8f, 8f));
            NarrationTrigger(parent, "N_M005", "PY-01-M-005", null, new Vector3(0f, 4.5f, 7f), new Vector3(24f, 8f, 6f));
            NarrationTrigger(parent, "N_M006", "PY-01-M-006", "PY-01-M-005", new Vector3(0f, 4.4f, 11f), new Vector3(26f, 6f, 8f));
            NarrationTrigger(parent, "N_M007", "PY-01-M-007", null, new Vector3(0f, 5.5f, 26f), new Vector3(20f, 6f, 8f));
            NarrationTrigger(parent, "N_M008", "PY-01-M-008", null, new Vector3(0f, 5.5f, 38f), new Vector3(20f, 6f, 8f));
            NarrationTrigger(parent, "N_M009", "PY-01-M-009", null, new Vector3(0f, 8.5f, 48f), new Vector3(20f, 6f, 8f));
            NarrationTrigger(parent, "N_M010", "PY-01-M-010", null, new Vector3(0f, 6.5f, 56f), new Vector3(20f, 6f, 8f));
            NarrationTrigger(parent, "N_M011", "PY-01-M-011", null, new Vector3(0f, 5.5f, 96f), new Vector3(20f, 6f, 8f));
            NarrationTrigger(parent, "N_M012", "PY-01-M-012", null, new Vector3(0f, 11f, 108f), new Vector3(24f, 6f, 8f));
            NarrationTrigger(parent, "N_M013", "PY-01-M-013", null, new Vector3(0f, 14f, 126f), new Vector3(20f, 8f, 10f));
            NarrationTrigger(parent, "N_M014", "PY-01-M-014", null, new Vector3(0f, 39f, 154f), new Vector3(20f, 6f, 10f));

            // ---- 反应段（本轮能挂的）----
            // 出口露台回头（B-007）：M-012 播过之后，从井口往回走
            NarrationTrigger(parent, "N_B007", "PY-01-B-007", "PY-01-M-012", new Vector3(0f, 11f, 100f), new Vector3(20f, 6f, 6f));
            // 在井里往反方向走（B-005）：M-012 之后回到井口边缘
            NarrationTrigger(parent, "N_B005", "PY-01-B-005", "PY-01-M-012", new Vector3(0f, 12f, 114f), new Vector3(18f, 6f, 6f));
            // 回到出生地（X-001）：M-013 之后
            NarrationTrigger(parent, "N_X001", "PY-01-X-001", "PY-01-M-013", new Vector3(0f, 2.5f, -16f), new Vector3(26f, 8f, 14f));
        }

        private static void SetIdle(SerializedProperty entry, float seconds, string id)
        {
            entry.FindPropertyRelative("seconds").floatValue = seconds;
            entry.FindPropertyRelative("id").stringValue = id;
        }

        private static void NarrationTrigger(Transform parent, string name, string beatId, string requires, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            PAO.Map0NarrationTrigger trigger = go.AddComponent<PAO.Map0NarrationTrigger>();
            trigger.Configure(beatId, requires, true, 0f);
        }



        // ==================== P2 美术 + P3 气氛 ====================

        private const string kWengjiePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kAtmosRoot   = "Map0_Atmosphere";
        private const string kStonePath   = "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat";
        private const string kStoneBPath  = "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue_B.mat";
        private const string kCausticsMat = "Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat";
        private const string kShaftAreaPrefab = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab";
        private const string kShaftPrefab     = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts.prefab";

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 美术/光/水/字幕", priority = 62)]
        public static void DecorateMenu()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map0] 请先退出 Play 模式。"); return; }
            Scene map0 = SceneManager.GetActiveScene();
            if (map0.path != kScenePath) { Debug.LogWarning("[Map0] 当前场景不是 Map0。"); return; }
            if (GameObject.Find(kRoot) == null) { Debug.LogError("[Map0] 先跑「Map0 重构（第一章 醒来）」搭空间。"); return; }
            DecorateScene(map0);
            EditorSceneManager.MarkSceneDirty(map0);
            EditorSceneManager.SaveScene(map0);
            Debug.Log("[Map0 重构] 美术/光/水/字幕 已完成。");
        }

        private static void DecorateScene(Scene map0)
        {
            GameObject world = GameObject.Find(kRoot);
            if (world == null) return;

            GameObject oldAtmos = GameObject.Find(kAtmosRoot);
            if (oldAtmos != null) Object.DestroyImmediate(oldAtmos);

            var atmosGo = new GameObject(kAtmosRoot);
            SceneManager.MoveGameObjectToScene(atmosGo, map0);
            Transform atmos = atmosGo.transform;

            // 1) 石材：地面一档、墙体一档
            Material stone = AssetDatabase.LoadAssetAtPath<Material>(kStonePath);
            Material stoneB = AssetDatabase.LoadAssetAtPath<Material>(kStoneBPath);
            ApplyStoneMaterials(world, stone, stoneB);
            AddGlowMaterials(world);

            // 2) 从 wengjie 抄光照 + 后处理 + 字幕 + 水面 + 植物/建筑模板
            Scene wj = EditorSceneManager.OpenScene(kWengjiePath, OpenSceneMode.Additive);
            CopyLighting(wj, map0);
            CopyDirectionalLight(wj, map0);
            AddFillLight(map0, atmos);

            CloneSceneRoot(wj, map0, "Wengjie_PostFX", atmos, "Map0_PostFX");
            CloneSceneRoot(wj, map0, "Wengjie Subtitle", atmos, "Map0 Subtitle");

            GameObject water = CloneSceneRoot(wj, map0, "Water_Ceiling", atmos, "Water_Ceiling");
            ConfigureWater(water, world, atmos);

            var plants = CollectTemplates(wj, map0, atmos, new[] { "Mood_Plant_" }, 12);
            var gates = CollectTemplates(wj, map0, atmos, new[] { "Art_Gate_" }, 3);
            var tfd = CollectTemplates(wj, map0, atmos, new[] { "T_src_TFD_", "T_src_West_Coral_" }, 36);

            EditorSceneManager.CloseScene(wj, true);

            // 3) 建筑 / 植物 / 光柱 / 点光
            PlaceArchitecture(atmos, gates, tfd);
            PlacePlants(atmos, plants);
            PlaceSunshafts(atmos);
            AddPointLights(atmos);

            // 4) 相机前景的地面焦散
            AttachCausticsScreen(water);
        }

        private static void ApplyStoneMaterials(GameObject world, Material stone, Material stoneB)
        {
            if (stone == null || stoneB == null) return;
            Transform[] all = world.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i].GetComponent<Renderer>();
                if (r == null) continue;
                string n = all[i].name;
                if (n.Contains("Sticky")) continue;                 // 可粘带保留高亮色
                if (n.Contains("Altar_Core") || n.Contains("Landmark") || n.Contains("FirstBubble")) continue;
                if (n.Contains("SweepBar") || n.Contains("CrackedSeal") || n.Contains("Plug") || n.Contains("Spring")) continue;

                bool wall = n.Contains("Wall") || n.Contains("Rail") || n.Contains("Jamb") || n.Contains("Narrow")
                         || n.Contains("Lintel") || n.Contains("Gate_") || n.Contains("Column") || n.Contains("Kerb")
                         || n.Contains("Ruin") || n.Contains("Arch") || n.Contains("Rubble") || n.Contains("Beam_")
                         || n.Contains("Pivot");
                r.sharedMaterial = wall ? stoneB : stone;
            }
        }

        private static Material EnsureEmissiveMaterial(string path, Color baseColor, Color emission)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Material EnsureTideWaterMaterial()
        {
            const string path = "Assets/GameMain/Art/Materials/M_Map0_TideWater.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            mat = new Material(shader) { name = "M_Map0_TideWater" };
            mat.SetFloat("_Surface", 1f);          // Transparent
            mat.SetFloat("_Blend", 0f);            // Alpha
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetFloat("_Metallic", 0f);
            mat.renderQueue = 3000;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetColor("_BaseColor", new Color(0.07f, 0.32f, 0.52f, 0.62f));
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void AddGlowMaterials(GameObject world)
        {
            Material bubbleGlow = EnsureEmissiveMaterial("Assets/GameMain/Art/Materials/M_Map0_FirstBubble.mat",
                new Color(0.35f, 0.68f, 0.95f), new Color(0.55f, 1.1f, 1.9f));
            Material stickyGlow = EnsureEmissiveMaterial("Assets/GameMain/Art/Materials/M_Map0_StickyGlow.mat",
                new Color(0.18f, 0.85f, 0.72f), new Color(0.1f, 0.55f, 0.45f));
            Material altarGlow = EnsureEmissiveMaterial("Assets/GameMain/Art/Materials/M_Map0_AltarGlow.mat",
                new Color(0.95f, 0.85f, 0.6f), new Color(1.3f, 0.95f, 0.55f));

            Transform[] all = world.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i].GetComponent<Renderer>();
                if (r == null) continue;
                string n = all[i].name;
                if (n.Contains("FirstBubble") && bubbleGlow != null) r.sharedMaterial = bubbleGlow;
                else if (n.Contains("Sticky") && stickyGlow != null) r.sharedMaterial = stickyGlow;
                else if (n.Contains("Altar_Light") && bubbleGlow != null) r.sharedMaterial = bubbleGlow;
                else if (n.Contains("Altar_Core") && altarGlow != null) r.sharedMaterial = altarGlow;
            }
        }

        private static void CopyLighting(Scene from, Scene to)
        {
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(from);

            bool fog = RenderSettings.fog;
            Color fogColor = RenderSettings.fogColor;
            FogMode fogMode = RenderSettings.fogMode;
            float fogDensity = RenderSettings.fogDensity;
            float fogStart = RenderSettings.fogStartDistance;
            float fogEnd = RenderSettings.fogEndDistance;
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientSky = RenderSettings.ambientSkyColor;
            Color ambientEquator = RenderSettings.ambientEquatorColor;
            Color ambientGround = RenderSettings.ambientGroundColor;
            float ambientIntensity = RenderSettings.ambientIntensity;
            Color ambientLight = RenderSettings.ambientLight;
            Material skybox = RenderSettings.skybox;

            SceneManager.SetActiveScene(to);

            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.skybox = skybox;
            // 第一章是“浅滩/醒来”，比 wengjie 的深水段亮一档：整体环境光抬起来
            RenderSettings.ambientIntensity = 1.25f;

            SceneManager.SetActiveScene(previous);
        }

        /// <summary>补一盏从侧后方来的冷色“水面散射”方向光，把回廊/井里的暗面提起来。</summary>
        private static void AddFillLight(Scene map0, Transform parent)
        {
            var go = new GameObject("Map0_FillLight");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(64f, 150f, 0f);
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.55f, 0.75f, 0.95f);
            light.intensity = 0.55f;
            light.shadows = LightShadows.None;
        }

        private static void CopyDirectionalLight(Scene from, Scene to)
        {
            Light src = null;
            foreach (GameObject root in from.GetRootGameObjects())
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional) { src = lights[i]; break; }
                }
                if (src != null) break;
            }
            if (src == null) return;

            Light dst = null;
            foreach (GameObject root in to.GetRootGameObjects())
            {
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional) { dst = lights[i]; break; }
                }
                if (dst != null) break;
            }
            if (dst == null) return;

            dst.color = src.color;
            dst.intensity = src.intensity;
            dst.shadows = src.shadows;
            dst.shadowStrength = src.shadowStrength;
            dst.transform.rotation = src.transform.rotation;
        }

        private static GameObject FindSceneRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root != null && root.name == name) return root;
            return null;
        }

        private static GameObject CloneSceneRoot(Scene src, Scene dst, string rootName, Transform parent, string newName)
        {
            GameObject source = FindSceneRoot(src, rootName);
            if (source == null)
            {
                Debug.LogWarning("[Map0] wengjie 里找不到根物体：" + rootName);
                return null;
            }
            GameObject clone = Object.Instantiate(source);
            clone.name = newName;
            SceneManager.MoveGameObjectToScene(clone, dst);
            if (parent != null) clone.transform.SetParent(parent, true);
            return clone;
        }

        private static List<GameObject> CollectTemplates(Scene src, Scene dst, Transform parent, string[] prefixes, int max)
        {
            var templates = new List<GameObject>();
            var holder = new GameObject("ArtTemplates");
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
                    for (int p = 0; p < prefixes.Length; p++)
                        if (n.StartsWith(prefixes[p], System.StringComparison.Ordinal)) { match = true; break; }
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
            Debug.Log("[Map0] 美术模板收集：" + templates.Count + " 个");
            return templates;
        }

        private static void DestroyColliders(GameObject go)
        {
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) Object.DestroyImmediate(cols[i]);
        }

        private static Bounds Measure(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>把一个美术模板按目标高度缩放、底面贴到 pos、朝向 yaw 放好。</summary>
        private static GameObject PlacePiece(GameObject template, Transform parent, Vector3 pos, float yaw, float fitHeight, float maxWidth)
        {
            if (template == null) return null;
            GameObject clone = Object.Instantiate(template);
            clone.name = template.name.Replace("TPL_", "Art_");
            SceneManager.MoveGameObjectToScene(clone, parent.gameObject.scene);
            clone.transform.SetParent(parent, true);
            clone.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            Bounds b = Measure(clone);
            float scale = fitHeight > 0f ? fitHeight / Mathf.Max(0.001f, b.size.y) : 1f;
            if (maxWidth > 0f && b.size.x > 0f)
                scale = Mathf.Min(scale, maxWidth / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z)));
            clone.transform.localScale *= scale;

            Bounds after = Measure(clone);
            clone.transform.position += pos - new Vector3(after.center.x, after.min.y, after.center.z);
            return clone;
        }

        private static void PlaceArchitecture(Transform atmos, List<GameObject> gates, List<GameObject> tfd)
        {
            Transform g = Group(atmos, "Map0_Arch");

            // 废墟门：放在主路两侧当“入口的框”，不压在路面上（避免遮住相机）
            if (gates.Count > 0)
            {
                PlacePiece(gates[0], g, new Vector3(-17f, B_Floor, 15f), 90f, 14f, 8f);
                PlacePiece(gates[1 % gates.Count], g, new Vector3(17f, B_Floor, 15f), -90f, 14f, 8f);
                PlacePiece(gates[2 % gates.Count], g, new Vector3(0f, C_Top, 165f), 180f, 11f, 16f);
            }

            if (tfd.Count == 0) return;
            var walls = new List<GameObject>();
            var rocks = new List<GameObject>();
            var grounds = new List<GameObject>();
            for (int i = 0; i < tfd.Count; i++)
            {
                string n = tfd[i].name;
                if (n.Contains("Wall") || n.Contains("Gate")) walls.Add(tfd[i]);
                else if (n.Contains("Rock") || n.Contains("Canyon") || n.Contains("Coral")) rocks.Add(tfd[i]);
                else if (n.Contains("Pavement") || n.Contains("Grass") || n.Contains("Bush")) grounds.Add(tfd[i]);
            }

            // 回廊两侧的废墟墙段：退到 x=±17，只做“外面还有一座城”的暗示，不挡主路
            int wi = 0;
            for (int i = 0; i < 8 && walls.Count > 0; i++)
            {
                float z = 24f + i * 11f;
                for (int side = -1; side <= 1; side += 2)
                {
                    PlacePiece(walls[wi % walls.Count], g, new Vector3(side * 17f, B_Floor, z), side > 0 ? -90f : 90f, 8.5f, 9f);
                    wi++;
                }
            }

            // 竖井内壁的墙段：北/东/西三面各 3 段；南面只放两侧（中间是门洞）
            for (int i = 0; i < 3 && walls.Count > 0; i++)
            {
                float x = -16f + i * 16f;
                PlacePiece(walls[wi % walls.Count], g, new Vector3(x, C_Bottom, ShaftMaxZ + 1f), 180f, 13f, 11f); wi++;
                PlacePiece(walls[wi % walls.Count], g, new Vector3(ShaftMaxX + 1f, C_Bottom, 126f + i * 14f), -90f, 13f, 11f); wi++;
                PlacePiece(walls[wi % walls.Count], g, new Vector3(ShaftMinX - 1f, C_Bottom, 126f + i * 14f), 90f, 13f, 11f); wi++;
            }
            PlacePiece(walls[wi % walls.Count], g, new Vector3(-19f, C_Bottom, ShaftMinZ - 1f), 0f, 13f, 11f);
            PlacePiece(walls[(wi + 1) % walls.Count], g, new Vector3(19f, C_Bottom, ShaftMinZ - 1f), 0f, 13f, 11f);

            // 竖井的“塔身”：南面外墙上再叠两排墙段 + 四角高柱，让从浅滩看过去是一座塔
            for (int row = 0; row < 3 && walls.Count > 0; row++)
            {
                float y = C_Bottom + row * 10f;
                for (int k = 0; k < 4; k++)
                {
                    float x = -18f + k * 12f;
                    PlacePiece(walls[wi % walls.Count], g, new Vector3(x, y, ShaftMinZ - 2.2f), 0f, 9.5f, 11f);
                    wi++;
                }
            }
            for (int k = 0; k < 4; k++)
            {
                float x = (k % 2 == 0 ? -1f : 1f) * 24f;
                float z = (k < 2 ? ShaftMinZ - 2f : ShaftMaxZ + 2f);
                PlacePiece(walls[wi % walls.Count], g, new Vector3(x, C_Bottom, z), 0f, 30f, 7f);
                wi++;
            }

            // 浅滩的崖壁与巨石（把开阔空间“框”起来）
            if (rocks.Count > 0)
            {
                PlacePiece(rocks[0], g, new Vector3(-30f, 0f, -6f), 24f, 9f, 26f);
                PlacePiece(rocks[1 % rocks.Count], g, new Vector3(30f, 0f, -12f), -32f, 8f, 24f);
                PlacePiece(rocks[2 % rocks.Count], g, new Vector3(-22f, 0f, 10f), 60f, 5f, 16f);
                PlacePiece(rocks[3 % rocks.Count], g, new Vector3(22f, 0f, 8f), -60f, 5f, 16f);
                PlacePiece(rocks[4 % rocks.Count], g, new Vector3(-24f, C_Bottom, 120f), 90f, 14f, 20f);
                PlacePiece(rocks[5 % rocks.Count], g, new Vector3(24f, C_Bottom, 150f), -90f, 14f, 20f);
            }

            // 地面铺石（沿回廊两侧铺，不挡路）
            for (int i = 0; i < 8 && grounds.Count > 0; i++)
            {
                float z = 20f + i * 11f;
                PlacePiece(grounds[i % grounds.Count], g, new Vector3(-10.5f, B_Floor + 0.02f, z), 20f * i, 0.5f, 6f);
                PlacePiece(grounds[(i + 4) % grounds.Count], g, new Vector3(10.5f, B_Floor + 0.02f, z + 5f), -20f * i, 0.5f, 6f);
            }
        }

        private static void PlacePlants(Transform atmos, List<GameObject> plants)
        {
            if (plants.Count == 0) return;
            Transform p = Group(atmos, "Map0_Plants");

            Vector3[] spots =
            {
                new Vector3(-18f, 0f, -18f), new Vector3(-25f, 0f, -8f), new Vector3(18f, 0f, -20f), new Vector3(26f, 0f, -4f),
                new Vector3(-8f, 2.8f, 9f), new Vector3(8f, 2.8f, 9f),
                new Vector3(-11f, 2.8f, 20f), new Vector3(11f, 2.8f, 20f), new Vector3(-11f, 2.8f, 32f), new Vector3(11f, 2.8f, 34f),
                new Vector3(-11f, 2.8f, 46f), new Vector3(11f, 2.8f, 48f), new Vector3(-11f, 2.8f, 60f), new Vector3(11f, 2.8f, 58f),
                new Vector3(-11f, 2.8f, 72f), new Vector3(11f, 2.8f, 74f), new Vector3(-11f, 2.8f, 88f), new Vector3(11f, 2.8f, 90f),
                new Vector3(-11f, 2.8f, 100f), new Vector3(11f, 2.8f, 102f),
                new Vector3(-6f, 7.3f, 49f), new Vector3(7f, 7.3f, 50f),
                new Vector3(15f, 12f, 66f), new Vector3(18f, 12f, 78f), new Vector3(14f, 12f, 88f), new Vector3(18f, 12f, 100f),
                new Vector3(-20f, 4f, 120f), new Vector3(20f, 4f, 126f), new Vector3(-20f, 4f, 150f), new Vector3(20f, 4f, 156f),
                new Vector3(-14f, 12f, 120f), new Vector3(14f, 12f, 120f),
                new Vector3(-17f, 17.5f, 130f), new Vector3(16f, 22f, 136f), new Vector3(-13f, 26.5f, 140f),
                new Vector3(15f, 31f, 146f), new Vector3(-11f, 36f, 150f),
                new Vector3(-18f, 36.5f, 158f), new Vector3(18f, 36.5f, 158f), new Vector3(-8f, 36.5f, 164f), new Vector3(8f, 36.5f, 164f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                GameObject t = plants[i % plants.Count];
                float scale = 0.8f + ((i * 37) % 10) * 0.09f;
                GameObject clone = PlacePiece(t, p, spots[i], (i * 53) % 360, 0f, 0f);
                if (clone == null) continue;
                Vector3 s = clone.transform.localScale;
                clone.transform.localScale = new Vector3(s.x * scale, s.y * scale, s.z * scale);
            }
        }

        private static void PlaceSunshafts(Transform atmos)
        {
            Transform s = Group(atmos, "Map0_Sunshafts");
            GameObject areaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kShaftAreaPrefab);
            GameObject singlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(kShaftPrefab);

            Vector3[] spots =
            {
                new Vector3(0f, 11f, -20f), new Vector3(-14f, 12f, -6f), new Vector3(14f, 12f, 2f),
                new Vector3(0f, 16f, -18f), new Vector3(-14f, 18f, -6f), new Vector3(14f, 18f, 2f),
                new Vector3(0f, 22f, 30f), new Vector3(0f, 24f, 62f), new Vector3(0f, 26f, 92f),
                new Vector3(0f, 34f, 140f), new Vector3(0f, 40f, 160f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                GameObject prefab = (i % 2 == 0) ? areaPrefab : singlePrefab;
                if (prefab == null) continue;
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = "Art_Sunshaft_" + i;
                SceneManager.MoveGameObjectToScene(inst, s.gameObject.scene);
                inst.transform.SetParent(s, true);
                inst.transform.position = spots[i];
                inst.transform.rotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one * 16f;
            }
        }

        private static void AddPointLights(Transform atmos)
        {
            Transform l = Group(atmos, "Map0_Lights");
            object[][] spots =
            {
                new object[] { new Vector3(0f, A_Floor + 5f, -8f),   new Vector3(0.75f, 0.88f, 1f), 2.6f, 26f },
                new object[] { new Vector3(0f, B_Floor + 5f, 20f),  new Vector3(1f, 0.86f, 0.66f), 5.5f, 30f },
                new object[] { new Vector3(0f, B_Floor + 5f, 38f),  new Vector3(1f, 0.86f, 0.66f), 5f, 28f },
                new object[] { new Vector3(0f, B_Floor + 5f, 58f),  new Vector3(1f, 0.86f, 0.66f), 5f, 28f },
                new object[] { new Vector3(0f, B_Floor + 5f, 78f),  new Vector3(1f, 0.86f, 0.66f), 5f, 28f },
                new object[] { new Vector3(0f, B_Floor + 5f, 98f),  new Vector3(1f, 0.86f, 0.66f), 5f, 28f },
                new object[] { new Vector3(16f, BranchY + 4f, 70f), new Vector3(0.8f, 0.9f, 1f), 2.4f, 18f },
                new object[] { new Vector3(16f, BranchY + 4f, 96f), new Vector3(0.8f, 0.9f, 1f), 2.4f, 18f },
                new object[] { new Vector3(0f, C_EnterY + 4f, 110f), new Vector3(0.8f, 0.9f, 1f), 4.5f, 24f },
                new object[] { new Vector3(0f, 10f, 140f),          new Vector3(0.7f, 0.88f, 1f), 5f, 28f },
                new object[] { new Vector3(0f, 18f, 128f),          new Vector3(0.7f, 0.88f, 1f), 5f, 26f },
                new object[] { new Vector3(0f, 26f, 146f),          new Vector3(0.7f, 0.88f, 1f), 5f, 26f },
                new object[] { new Vector3(0f, C_Top + 3f, 160f),  new Vector3(1f, 0.92f, 0.78f), 6f, 32f },
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject("Map0_PointLight_" + i);
                go.transform.SetParent(l, false);
                go.transform.position = (Vector3)spots[i][0];
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                Vector3 tint = (Vector3)spots[i][1];
                light.color = new Color(tint.x, tint.y, tint.z);
                light.intensity = (float)spots[i][2];
                light.range = (float)spots[i][3];
                light.shadows = LightShadows.None;
            }
        }

        private static void ConfigureWater(GameObject water, GameObject world, Transform atmos)
        {
            if (water == null) return;
            UnderwaterCeiling ceiling = water.GetComponent<UnderwaterCeiling>();
            if (ceiling == null) ceiling = water.AddComponent<UnderwaterCeiling>();

            Bounds bounds = Measure(world);
            float waterY = bounds.max.y + 12f;
            float halfSize = Mathf.Max(Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.4f, 120f) + 160f;
            float skirt = Mathf.Clamp(bounds.size.y + 60f, 80f, 400f);

            water.transform.SetPositionAndRotation(new Vector3(bounds.center.x, waterY, bounds.center.z), Quaternion.identity);
            water.transform.localScale = Vector3.one;
            ceiling.Configure(halfSize, skirt);

            MeshRenderer renderer = water.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat");
                if (mat != null) renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Collider col = water.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            EditorUtility.SetDirty(ceiling);
            Debug.Log(string.Format("[Map0] 水面：y={0:F1}（关卡最高 {1:F1}），半边 {2:F1} m，裙边 {3:F1} m。",
                waterY, bounds.max.y, halfSize, skirt));
        }

        private static void AttachCausticsScreen(GameObject water)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(kCausticsMat);
            if (source == null) return;

            // 用 Map0 自己的材质副本：_WaterY 是全局参数，直接改共享材质会把 wengjie 的水深也改掉
            const string map0MatPath = "Assets/GameMain/Art/Materials/M_Map0_CausticsScreen.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(map0MatPath);
            if (mat == null)
            {
                mat = new Material(source) { name = "M_Map0_CausticsScreen" };
                AssetDatabase.CreateAsset(mat, map0MatPath);
            }

            float waterY = water != null ? water.transform.position.y : 60f;
            mat.SetFloat("_WaterY", waterY);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                if (cams.Length > 0) cam = cams[0];
            }
            if (cam == null) return;

            Transform old = cam.transform.Find("PAO_CausticsScreen");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "PAO_CausticsScreen";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(cam.transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(8f, 8f, 1f);
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ==================== 截图（验证用） ====================

        [MenuItem("Tools/PAO/关卡白盒/Map0 重构 · 截图", priority = 66)]
        public static void CaptureViews()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[Map0] 退出 Play 再截图。"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != kScenePath) { Debug.LogWarning("[Map0] 当前场景不是 Map0。"); return; }

            Directory.CreateDirectory(kShotDir);
            Capture("01_spawn",       new Vector3(0f, 3f, -22f),   Quaternion.Euler(8f, 0f, 0f));
            Capture("02_sticky_wall", new Vector3(0f, 4f, -2f),    Quaternion.Euler(14f, 0f, 0f));
            Capture("03_corridor",    new Vector3(0f, 5.5f, 18f),  Quaternion.Euler(6f, 0f, 0f));
            Capture("04_fill_lock",   new Vector3(0f, 5.2f, 18f),  Quaternion.Euler(6f, 0f, 0f));
            Capture("05_b2_high",     new Vector3(0f, 8.5f, 34f),  Quaternion.Euler(2f, 0f, 0f));
            Capture("06_b3_crack",    new Vector3(0f, 5.5f, 56f),  Quaternion.Euler(4f, 0f, 0f));
            Capture("07_branch",      new Vector3(16f, 14.5f, 70f), Quaternion.Euler(4f, 0f, 0f));
            Capture("08_well_entry",  new Vector3(0f, 10.6f, 106f), Quaternion.Euler(12f, 0f, 0f));
            Capture("09_well_top",    new Vector3(0f, 38.6f, 148f), Quaternion.Euler(6f, 0f, 0f));
            Capture("10_overview",    new Vector3(0f, 62f, 30f),   Quaternion.Euler(28f, 0f, 0f));
            Capture("11_look_up",     new Vector3(0f, 3f, -10f),   Quaternion.Euler(-42f, 0f, 0f));
            Capture("12_eye_level",   new Vector3(0f, 3.4f, -20f), Quaternion.Euler(2f, 0f, 0f));
            Capture("13_well_inside", new Vector3(0f, 10f, 118f),  Quaternion.Euler(-6f, 0f, 0f));
            Capture("14_altar",       new Vector3(0f, 38.2f, 152f), Quaternion.Euler(2f, 180f, 0f));
            Capture("15_tower_from_A", new Vector3(0f, 6f, -14f),  Quaternion.Euler(14f, 0f, 0f));

            Debug.Log("[Map0 重构] 截图完成 -> " + Path.GetFullPath(kShotDir));
        }

        private static void Capture(string name, Vector3 pos, Quaternion rot)
        {
            Camera source = Camera.main;
            var go = new GameObject("__Map0CaptureCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            if (source != null) cam.CopyFrom(source);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.cullingMask = ~0;

            var data = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;

            GameObject quad = null;
            Camera playerCam = Camera.main;
            if (playerCam != null)
            {
                Transform q = playerCam.transform.Find("PAO_CausticsScreen");
                if (q != null) quad = q.gameObject;
            }
            Transform quadParent = null;
            Vector3 quadPos = Vector3.zero;
            Quaternion quadRot = Quaternion.identity;
            Vector3 quadScale = Vector3.one;
            if (quad != null)
            {
                quadParent = quad.transform.parent;
                quadPos = quad.transform.localPosition;
                quadRot = quad.transform.localRotation;
                quadScale = quad.transform.localScale;
                quad.transform.SetParent(cam.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 1.5f);
                quad.transform.localRotation = Quaternion.identity;
                quad.transform.localScale = new Vector3(8f, 8f, 1f);
            }

            RenderTexture rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR, RenderTextureReadWrite.sRGB);
            rt.name = "__Map0CaptureRT";
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

            if (quad != null)
            {
                quad.transform.SetParent(quadParent, false);
                quad.transform.localPosition = quadPos;
                quad.transform.localRotation = quadRot;
                quad.transform.localScale = quadScale;
            }

            Object.DestroyImmediate(go);
        }

        // ==================== 构件帮助函数 ====================

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

        private static void Sticky(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = Box(parent, name, pos, size, m_Sticky);
            go.AddComponent<StickySurface>();
        }

        /// <summary>可填充的地形泡（弹力泡填满后炸开，奖励泡泡值）。</summary>
        private static TerrainBubble FillLock(Transform parent, string name, Vector3 pos, float diameter, int capacity, int reward)
        {
            GameObject go = Sphere(parent, name, pos, diameter, m_Pop);
            TerrainBubble tb = go.AddComponent<TerrainBubble>();
            SetSerialized(tb, so =>
            {
                so.FindProperty("m_Capacity").intValue = capacity;
                so.FindProperty("m_RewardAmount").intValue = reward;
                so.FindProperty("m_RespawnDelay").floatValue = 8f;
            });
            return tb;
        }

        /// <summary>泡泡泉：填满后炸开、给出泡泡值。</summary>
        private static TerrainBubble Spring(Transform parent, string name, Vector3 pos, int capacity)
        {
            return FillLock(parent, name, pos, 1.4f + capacity * 0.3f, capacity, 8);
        }

        /// <summary>被炸弹炸开的地形泡「裂墙」：椭圆泡膜封住裂口。</summary>
        private static TerrainBubble Breakable(Transform parent, string name, Vector3 pos, Vector3 size, int capacity)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m_Pop;
            TerrainBubble tb = go.AddComponent<TerrainBubble>();
            SetSerialized(tb, so =>
            {
                so.FindProperty("m_Capacity").intValue = capacity;
                so.FindProperty("m_RewardAmount").intValue = 0;
                so.FindProperty("m_RespawnDelay").floatValue = 0f;
            });
            return tb;
        }

        private static void SetSerialized(Object target, System.Action<SerializedObject> apply)
        {
            SerializedObject so = new SerializedObject(target);
            apply(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
