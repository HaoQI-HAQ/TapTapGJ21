using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PAO;
using PAO.BubbleTide;

namespace PAO.EditorTools
{
    /// <summary>
    /// wengjie 场景的中期关卡：枢纽广场 + 断柱廊 + 峡谷裂口。
    ///
    /// 【这一段在 15 分钟流程里的位置】
    ///   教学关（已有）→ **枢纽 → 断柱廊(物理涌现) → 峡谷(潮水追)** → 终局（待做）
    ///
    /// 【设计意图】
    ///   断柱廊教「世界是可以拆的」：四根 8 米泡柱，楔子被填掉就倒，
    ///   倒进沟里就是桥。峡谷教「时间和空间是一回事」：潮水追着你，
    ///   而水本身也是最快的电梯。
    ///
    /// 【幂等】
    ///   重复执行只会重建 Wengjie_MidGame 这个根物体，玩家预制体、灯光、原有的 Cube 都不动。
    ///
    /// 菜单：Tools → PAO → 关卡白盒 → 搭建 wengjie 中期（断柱廊 + 峡谷）
    /// </summary>
    public static class WengjieLevelSetup
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRootName = "Wengjie_MidGame";
        private const string kMatFolder = "Assets/GameMain/Art/WhiteboxMaterials/";

        private static Material m_Ground;
        private static Material m_Edge;
        private static Material m_Platform;
        private static Material m_Landmark;
        private static Material m_Bounce;
        private static Material m_Sticky;
        private static Material m_Pop;
        private static Material m_Destructible;
        private static Material m_Void;

        // ==================== 关卡尺寸（改这里就能整体调） ====================

        /// <summary>枢纽平台的外沿半径（正方形边长的一半）。</summary>
        private const float kHubHalf = 15f;

        /// <summary>井口（正方形）半径。</summary>
        private const float kWellHalf = 5f;

        /// <summary>断柱廊的沟宽。</summary>
        private const float kPitWidth = 6f;

        /// <summary>柱子的高度。</summary>
        private const float kColumnHeight = 8f;

        /// <summary>峡谷总长（z 方向）。</summary>
        private const float kCanyonLength = 50f;

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/搭建 wengjie 中期（断柱廊 + 峡谷）", priority = 71)]
        public static void Build()
        {
            LoadMaterials();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) == null)
            {
                EditorUtility.DisplayDialog("搭建 wengjie", "找不到场景：\n" + kScenePath, "好");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kRootName);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            BuildHub(t);
            BuildColonnade(t);
            BuildCanyon(t);
            BuildSystems(t);
            MovePlayerToSpawn();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log("[Wengjie] 中期关卡已生成：\n" +
                      "  枢纽广场（中间是井口，往下 8 米）\n" +
                      "  断柱廊（西侧）：4 根泡柱 + 6 米沟 + 渡台，填楔子让柱子倒成桥\n" +
                      "  峡谷裂口（北侧）：5 级台阶，踏进去潮水就开始追\n" +
                      "  潮水：Tools → PAO → 泡泡潮 → 安装到 URP Renderer（如果还没装）");
        }

        // ==================== ① 枢纽广场 ====================

        private static void BuildHub(Transform root)
        {
            var group = new GameObject("Hub_Plaza").transform;
            group.SetParent(root, false);

            float outer = kHubHalf;
            float inner = kWellHalf;
            float band = (outer - inner) * 0.5f;
            float mid = (outer + inner) * 0.5f;

            const float y = 0f;
            const float thick = 1f;

            // 四块地板围出中间的井口
            Box(group, "Hub_Floor_N", new Vector3(0f, y - thick * 0.5f, mid), new Vector3(outer * 2f, thick, band), m_Ground);
            Box(group, "Hub_Floor_S", new Vector3(0f, y - thick * 0.5f, -mid), new Vector3(outer * 2f, thick, band), m_Ground);
            Box(group, "Hub_Floor_E", new Vector3(mid, y - thick * 0.5f, 0f), new Vector3(band, thick, inner * 2f), m_Ground);
            Box(group, "Hub_Floor_W", new Vector3(-mid, y - thick * 0.5f, 0f), new Vector3(band, thick, inner * 2f), m_Ground);

            // 井口四面墙（往下 8 米），先封住，终局再打开
            const float wellDepth = 8f;
            float wallH = wellDepth;
            float wallY = -wellDepth * 0.5f;
            Box(group, "Hub_WellWall_N", new Vector3(0f, wallY, inner), new Vector3(inner * 2f + 1f, wallH, 0.6f), m_Edge);
            Box(group, "Hub_WellWall_S", new Vector3(0f, wallY, -inner), new Vector3(inner * 2f + 1f, wallH, 0.6f), m_Edge);
            Box(group, "Hub_WellWall_E", new Vector3(inner, wallY, 0f), new Vector3(0.6f, wallH, inner * 2f), m_Edge);
            Box(group, "Hub_WellWall_W", new Vector3(-inner, wallY, 0f), new Vector3(0.6f, wallH, inner * 2f), m_Edge);
            // 井底不放实体地板：掉下去要能一路落到重生高度，而不是卡在坑底
            Box(group, "Hub_WellVoid_Backdrop", new Vector3(0f, -16f, 0f), new Vector3(inner * 2f, 0.2f, inner * 2f), m_Void, false);

            // 井口的四角柱：给远景一个记忆点，也是「爬到终局」的视觉预告
            float c = inner + 0.8f;
            Box(group, "Hub_MarkerPillar_NW", new Vector3(-c, 3f, c), new Vector3(1.2f, 6f, 1.2f), m_Landmark);
            Box(group, "Hub_MarkerPillar_NE", new Vector3(c, 3f, c), new Vector3(1.2f, 6f, 1.2f), m_Landmark);
            Box(group, "Hub_MarkerPillar_SW", new Vector3(-c, 3f, -c), new Vector3(1.2f, 6f, 1.2f), m_Landmark);
            Box(group, "Hub_MarkerPillar_SE", new Vector3(c, 3f, -c), new Vector3(1.2f, 6f, 1.2f), m_Landmark);

            // 外沿矮栏：防止玩家从边上掉进虚空，但不挡视线
            Box(group, "Hub_Rail_N", new Vector3(0f, 0.4f, outer), new Vector3(outer * 2f, 0.8f, 0.4f), m_Edge);
            Box(group, "Hub_Rail_S", new Vector3(0f, 0.4f, -outer), new Vector3(outer * 2f, 0.8f, 0.4f), m_Edge);
            Box(group, "Hub_Rail_E", new Vector3(outer, 0.4f, 0f), new Vector3(0.4f, 0.8f, outer * 2f), m_Edge);
            Box(group, "Hub_Rail_W", new Vector3(-outer, 0.4f, 0f), new Vector3(0.4f, 0.8f, outer * 2f), m_Edge);

            // 出口缺口（西 → 断柱廊，北 → 峡谷，南 → 教学关方向）
            // 用「补一块地板」的方式把矮栏的缺口做出来，视觉上就是三条路
            Box(group, "Hub_Gate_W_Floor", new Vector3(-outer, -0.2f, 0f), new Vector3(6f, 0.8f, 6f), m_Platform);
            Box(group, "Hub_Gate_N_Floor", new Vector3(0f, -0.2f, outer), new Vector3(6f, 0.8f, 6f), m_Platform);
            Box(group, "Hub_Gate_S_Floor", new Vector3(0f, -0.2f, -outer), new Vector3(6f, 0.8f, 6f), m_Platform);

            DestroyRailAt(group, "Hub_Rail_W", 0f, 6f);
            DestroyRailAt(group, "Hub_Rail_N", 0f, 6f);
            DestroyRailAt(group, "Hub_Rail_S", 0f, 6f);

            // 指路牌：三根不同颜色的柱子，对应三条路的玩法
            Box(group, "Sign_W_Sticky", new Vector3(-10f, 1.5f, 4f), new Vector3(0.6f, 3f, 0.6f), m_Sticky);
            Box(group, "Sign_N_Pop", new Vector3(2f, 1.5f, 10f), new Vector3(0.6f, 3f, 0.6f), m_Pop);
            Box(group, "Sign_S_Platform", new Vector3(-4f, 1.5f, -10f), new Vector3(0.6f, 3f, 0.6f), m_Platform);
        }

        // ==================== ② 断柱廊 ====================

        private static void BuildColonnade(Transform root)
        {
            var group = new GameObject("ZoneA_Colonnade").transform;
            group.SetParent(root, false);

            // 走廊：从枢纽西口 (-15) 往西延伸到 -22
            Box(group, "A_Corridor_Floor", new Vector3(-18.5f, -0.5f, 0f), new Vector3(7f, 1f, 10f), m_Ground);

            // 大厅地面：东岸 -34..-22 / 西岸 -58..-40，中间是沟
            Box(group, "A_Floor_East", new Vector3(-28f, -0.5f, 0f), new Vector3(12f, 1f, 28f), m_Ground);
            Box(group, "A_Floor_West", new Vector3(-49f, -0.5f, 0f), new Vector3(18f, 1f, 28f), m_Ground);

            // 沟：x -40..-34，深 5
            Box(group, "A_Pit_Floor", new Vector3(-37f, -5.5f, 0f), new Vector3(kPitWidth, 1f, 28f), m_Void);
            Box(group, "A_Pit_Wall_S", new Vector3(-37f, -2.5f, -14f), new Vector3(kPitWidth + 2f, 6f, 1f), m_Edge);
            Box(group, "A_Pit_Wall_N", new Vector3(-37f, -2.5f, 14f), new Vector3(kPitWidth + 2f, 6f, 1f), m_Edge);

            // 大厅外墙
            Box(group, "A_Wall_S", new Vector3(-40f, 4f, -14f), new Vector3(40f, 8f, 1f), m_Edge);
            Box(group, "A_Wall_N", new Vector3(-40f, 4f, 14f), new Vector3(40f, 8f, 1f), m_Edge);
            Box(group, "A_Wall_W", new Vector3(-58f, 4f, 0f), new Vector3(1f, 8f, 28f), m_Edge);
            Box(group, "A_Wall_E_S", new Vector3(-22f, 4f, -9f), new Vector3(1f, 8f, 10f), m_Edge);
            Box(group, "A_Wall_E_N", new Vector3(-22f, 4f, 9f), new Vector3(1f, 8f, 10f), m_Edge);

            // 四根泡柱：立在东岸沿（x=-33），往西倒，正好搭到西岸（-40）
            float[] zs = { -9f, -3f, 3f, 9f };
            for (int i = 0; i < zs.Length; i++)
            {
                BuildOneColumn(group, i + 1, new Vector3(-33.0f, 0f, zs[i]));
            }

            // 渡台：沟上往返，慢速。柱子倒下来会被它撞歪 / 挡住
            GameObject ferry = Box(group, "A_Ferry", new Vector3(-40f, 1.2f, 0f), new Vector3(5f, 0.5f, 3f), m_Platform);
            WhiteboxPathPlatform mover = ferry.AddComponent<WhiteboxPathPlatform>();
            mover.Configure(Vector3.right, 6f, 1.2f);

            // 沟底的上生气流：掉进沟里也有免费的上来的路（不然 5 米深的坑是个陷阱）
            GameObject updraft = new GameObject("A_Pit_Updraft");
            updraft.transform.SetParent(group, false);
            updraft.transform.localPosition = new Vector3(-39f, -2f, 0f);
            BoxCollider updraftTrigger = updraft.AddComponent<BoxCollider>();
            updraftTrigger.size = new Vector3(1.6f, 8f, 26f);
            updraftTrigger.isTrigger = true;
            updraft.AddComponent<WhiteboxUpdraft>().Configure(3.4f);

            // 沟壁上的可粘补丁：粘泡封住能买 10 秒（给潮水 / 给后面的终局用）
            MakeSticky(group, "A_Sticky_01", new Vector3(-34.2f, 2.5f, -6f), new Vector3(0.5f, 3f, 2.5f));
            MakeSticky(group, "A_Sticky_02", new Vector3(-34.2f, 2.5f, 6f), new Vector3(0.5f, 3f, 2.5f));

            // 泡泡泉：容量 1 / 奖励 5 / 8 秒重生 = 净赚 +3，防卡死
            Terrain(group, "A_Spring_01", new Vector3(-52f, 1.2f, -10f), 2.4f, 1, 5, 8f);
            Terrain(group, "A_Spring_02", new Vector3(-52f, 1.2f, 10f), 2.4f, 1, 5, 8f);

            // 回程捷径：西岸的高台 + 一条滑回枢纽的斜坡
            Box(group, "A_Return_Platform", new Vector3(-55f, 3.5f, 0f), new Vector3(6f, 0.6f, 6f), m_Landmark);
            Box(group, "A_Return_Ramp", new Vector3(-46f, 1.6f, 0f), new Vector3(14f, 0.5f, 3f), m_Platform)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 12f);

            // 一根「已经倒了一半」的柱子当示范：玩家一进门就看见它斜在那儿
            BuildOneColumn(group, 0, new Vector3(-49f, 0f, 7f), 55f, true);
        }

        /// <summary>
        /// 一根泡柱 = 柱子（kinematic 刚体，倾斜 10°）+ 楔子（TerrainBubble）+ 释放器。
        /// </summary>
        private static void BuildOneColumn(Transform parent, int index, Vector3 basePos, float tilt = 10f, bool preFallen = false)
        {
            string name = "A_Column_" + index.ToString("00");

            // 柱子：绕底端倾斜。中心 = 底 + R(θ)·(0,h/2,0)
            float rad = tilt * Mathf.Deg2Rad;
            Vector3 up = new Vector3(-Mathf.Sin(rad), Mathf.Cos(rad), 0f);
            Vector3 center = basePos + up * (kColumnHeight * 0.5f);

            GameObject column = Box(parent, name, center, new Vector3(1.1f, kColumnHeight, 1.1f), m_Bounce);
            column.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

            Rigidbody body = column.AddComponent<Rigidbody>();
            body.mass = 1.6f;
            body.drag = 0.15f;
            body.angularDrag = 0.4f;
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 柱头 / 柱础：只是观感，不加碰撞体，避免把柱子卡住
            Box(parent, name + "_Cap", center + up * (kColumnHeight * 0.5f), new Vector3(1.6f, 0.5f, 1.6f), m_Landmark, false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Box(parent, name + "_Base", basePos + Vector3.up * 0.25f, new Vector3(1.9f, 0.5f, 1.9f), m_Landmark);

            // 楔子：塞在柱子的西侧脚下。填满它就炸，柱子随即失去支撑
            TerrainBubble chock = Terrain(parent, name + "_Chock",
                basePos + new Vector3(-0.9f, 0.9f, 0f), 1.9f, 3, 5, 0f);

            if (preFallen)
            {
                // 示范柱：歪 55°，不接释放器，纯装饰（也真的会倒，给玩家看物理）
                return;
            }

            WengjieColumnRelease release = column.AddComponent<WengjieColumnRelease>();
            SerializedObject so = new SerializedObject(release);
            so.FindProperty("m_Chock").objectReferenceValue = chock;
            so.FindProperty("m_Column").objectReferenceValue = body;
            so.FindProperty("m_KickTorque").floatValue = 1.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ==================== ③ 峡谷裂口 ====================

        private static void BuildCanyon(Transform root)
        {
            var group = new GameObject("ZoneB_Canyon").transform;
            group.SetParent(root, false);

            // 入口走廊：从枢纽北口 (+15) 到峡谷口 (+24)
            Box(group, "B_Corridor_Floor", new Vector3(0f, -0.5f, 19.5f), new Vector3(10f, 1f, 9f), m_Ground);

            // 五级台阶，每级 3 米高、10 米深
            float z0 = 24f;
            float depth = 10f;
            for (int i = 0; i < 5; i++)
            {
                float y = i * 3f;
                float z = z0 + depth * i + depth * 0.5f;
                Box(group, "B_Terrace_" + i, new Vector3(0f, y - 0.5f, z), new Vector3(28f, 1f, depth), m_Ground);
                // 台阶立面
                Box(group, "B_Riser_" + i, new Vector3(0f, y - 1.5f, z0 + depth * i), new Vector3(28f, 3f, 1f), m_Edge);
            }

            // 两侧崖壁：给「窄」的感觉
            for (int i = 0; i < 5; i++)
            {
                float y = i * 3f;
                Box(group, "B_Cliff_W_" + i, new Vector3(-14.5f, y + 5f, 24f + depth * i + depth * 0.5f), new Vector3(1f, 10f, depth), m_Edge);
                Box(group, "B_Cliff_E_" + i, new Vector3(14.5f, y + 5f, 24f + depth * i + depth * 0.5f), new Vector3(1f, 10f, depth), m_Edge);
            }

            // 侧边小台：给「不花钱的慢路线」，也打断纯粹的直线
            Box(group, "B_Ledge_W_1", new Vector3(-11f, 4.2f, 39f), new Vector3(6f, 0.5f, 3f), m_Platform);
            Box(group, "B_Ledge_E_2", new Vector3(11f, 7.2f, 49f), new Vector3(6f, 0.5f, 3f), m_Platform);
            Box(group, "B_Ledge_W_3", new Vector3(-11f, 10.2f, 59f), new Vector3(6f, 0.5f, 3f), m_Platform);

            // 终点祭坛：最高一级
            Box(group, "B_Altar", new Vector3(0f, 12.4f, 70f), new Vector3(8f, 0.8f, 8f), m_Landmark);
            Box(group, "B_Altar_Core", new Vector3(0f, 13.2f, 72f), new Vector3(2.4f, 1.4f, 2.4f), m_Bounce);

            GameObject goalObject = new GameObject("B_Goal");
            goalObject.transform.SetParent(group, false);
            goalObject.transform.localPosition = new Vector3(0f, 13.4f, 70f);
            BubbleTideGoal goal = goalObject.AddComponent<BubbleTideGoal>();
            SerializedObject goalSo = new SerializedObject(goal);
            goalSo.FindProperty("m_GraceSeconds").floatValue = 8f;
            goalSo.FindProperty("m_ReachDistance").floatValue = 2.5f;
            goalSo.FindProperty("m_Radius").floatValue = 1.5f;
            goalSo.ApplyModifiedPropertiesWithoutUndo();

            // 峡谷尽头封口（防止玩家从顶上走出去）
            Box(group, "B_BackWall", new Vector3(0f, 20f, 74f), new Vector3(30f, 20f, 1f), m_Edge);

            // 潮水开关：踏进峡谷口就开始追
            GameObject gate = new GameObject("B_TideGate");
            gate.transform.SetParent(group, false);
            gate.transform.localPosition = new Vector3(0f, 2f, 23.5f);
            BoxCollider gateCollider = gate.AddComponent<BoxCollider>();
            gateCollider.size = new Vector3(28f, 4f, 2f);
            gateCollider.isTrigger = true;
            gate.AddComponent<WengjieTideGate>();
        }

        // ==================== ④ 系统：潮水 / 重生 / 出生点 ====================

        private static void BuildSystems(Transform root)
        {
            var group = new GameObject("Systems").transform;
            group.SetParent(root, false);

            // ---- 出生点 ----
            GameObject spawn = new GameObject("Spawn");
            spawn.transform.SetParent(group, false);
            spawn.transform.localPosition = new Vector3(0f, 0.6f, -12f);

            // ---- 坠落重生 ----
            GameObject respawn = new GameObject("FallRespawn");
            respawn.transform.SetParent(group, false);
            FallRespawn fall = respawn.AddComponent<FallRespawn>();
            fall.Configure(-30f, spawn.transform);

            // ---- 潮水总控 ----
            GameObject tideObject = new GameObject("BubbleTide");
            tideObject.transform.SetParent(group, false);
            tideObject.transform.localPosition = Vector3.zero;
            BubbleTideDirector director = tideObject.AddComponent<BubbleTideDirector>();

            SerializedObject so = new SerializedObject(director);
            so.FindProperty("m_AutoStart").boolValue = false;
            so.FindProperty("m_StartDelay").floatValue = 0f;
            so.FindProperty("m_BaseSpeed").floatValue = 0.55f;
            so.FindProperty("m_CellSize").floatValue = 0.7f;
            so.FindProperty("m_BubbleRadius").floatValue = 0.34f;
            so.FindProperty("m_BubbleSpacing").floatValue = 0.5f;
            so.FindProperty("m_PushSpeed").floatValue = 3.5f;
            so.FindProperty("m_UseExplicitBounds").boolValue = true;
            so.FindProperty("m_BoundsMin").vector3Value = new Vector3(-14f, 0f, 24f);
            so.FindProperty("m_BoundsMax").vector3Value = new Vector3(14f, 14f, 74f);
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---- 八个潮水源：峡谷入口的低处 ----
            for (int i = 0; i < 8; i++)
            {
                int col = i % 4;
                int row = i / 4;
                float x = -12f + col * 8f;
                float z = 25.5f + row * 2.5f;

                GameObject anchor = new GameObject("TideAnchor_" + i.ToString("00"));
                anchor.transform.SetParent(group, false);
                // 潮水源要浮在地面上方，不能埋进地板里 —— 埋进去会变成「封闭空腔」，
                // 水永远涨不上来（第一次搭建时踩过这个坑）
                anchor.transform.localPosition = new Vector3(x, 0.4f, z);
                anchor.AddComponent<BubbleTideAnchor>();
            }
        }

        // ==================== 玩家 ====================

        private static void MovePlayerToSpawn()
        {
            GameObject player = GameObject.Find("takoyaki");
            if (player == null)
            {
                Debug.LogWarning("[Wengjie] 场景里没找到 takoyaki（玩家预制体），出生点没设置。");
                return;
            }

            player.transform.position = new Vector3(0f, 1.2f, -12f);
            player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        }

        // ==================== 工具 ====================

        private static void LoadMaterials()
        {
            m_Ground = Mat("WB_Ground");
            m_Edge = Mat("WB_Edge");
            if (m_Edge == null) m_Edge = Mat("WB_Ground");   // 这个工程里没有 WB_Edge，退回地面材质
            m_Platform = Mat("WB_Platform");
            m_Landmark = Mat("WB_Landmark");
            m_Bounce = Mat("WB_Bounce");
            m_Sticky = Mat("WB_FloatSticky");
            m_Pop = Mat("WB_Bomb");
            m_Destructible = Mat("WB_Destructible");
            m_Void = Mat("WB_Void");
        }

        private static Material Mat(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(kMatFolder + name + ".mat");
            if (material == null) Debug.LogWarning("[Wengjie] 找不到材质：" + kMatFolder + name + ".mat");
            return material;
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material material, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            if (!collider)
            {
                Collider own = go.GetComponent<Collider>();
                if (own != null) Object.DestroyImmediate(own);
            }

            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 localPos, float diameter, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            return go;
        }

        private static void MakeSticky(Transform parent, string name, Vector3 localPos, Vector3 size)
        {
            GameObject go = Box(parent, name, localPos, size, m_Sticky);
            go.AddComponent<StickySurface>();
        }

        private static TerrainBubble Terrain(Transform parent, string name, Vector3 localPos, float diameter, int capacity, int reward, float respawn)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && m_Destructible != null) renderer.sharedMaterial = m_Destructible;

            TerrainBubble bubble = go.AddComponent<TerrainBubble>();
            SerializedObject so = new SerializedObject(bubble);
            so.FindProperty("m_Capacity").intValue = capacity;
            so.FindProperty("m_RewardAmount").intValue = reward;
            so.FindProperty("m_RespawnDelay").floatValue = respawn;
            so.ApplyModifiedPropertiesWithoutUndo();
            return bubble;
        }

        /// <summary>把某一面矮栏在 |axis| &lt; gap 的范围内删掉，做出一个出口。</summary>
        private static void DestroyRailAt(Transform group, string railName, float center, float gap)
        {
            GameObject rail = group.Find(railName)?.gameObject;
            if (rail == null) return;

            Vector3 scale = rail.transform.localScale;
            Vector3 pos = rail.transform.localPosition;

            // 直接把整根矮栏缩成两段：左右各留一段
            bool alongX = scale.x > scale.z;
            float half = (alongX ? scale.x : scale.z) * 0.5f;
            float seg = Mathf.Max(0.1f, half - gap * 0.5f - Mathf.Abs(center));

            if (alongX)
            {
                rail.transform.localScale = new Vector3(seg, scale.y, scale.z);
                rail.transform.localPosition = new Vector3(-(gap * 0.5f + seg * 0.5f), pos.y, pos.z);

                GameObject right = Box(group, railName + "_R", Vector3.zero, new Vector3(seg, scale.y, scale.z), rail.GetComponent<Renderer>()?.sharedMaterial);
                right.transform.localPosition = new Vector3(gap * 0.5f + seg * 0.5f, pos.y, pos.z);
            }
            else
            {
                rail.transform.localScale = new Vector3(scale.x, scale.y, seg);
                rail.transform.localPosition = new Vector3(pos.x, pos.y, -(gap * 0.5f + seg * 0.5f));

                GameObject right = Box(group, railName + "_R", Vector3.zero, new Vector3(scale.x, scale.y, seg), rail.GetComponent<Renderer>()?.sharedMaterial);
                right.transform.localPosition = new Vector3(pos.x, pos.y, gap * 0.5f + seg * 0.5f);
            }
        }

        // ==================================================================
        //  机关：障碍类（旋转杆 / 尖刺 / 上生气流 / 移动台 / 塌陷板）
        // ==================================================================

        private const string kHazardRoot = "Wengjie_Hazards";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 加机关（障碍）", priority = 73)]
        public static void AddHazards()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kHazardRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kHazardRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            int count = 0;

            // ---------- 枢纽：绕井口扫的两根长杆 ----------
            count += Spinner(t, "Hub_Spin_A", new Vector3(-9f, 1.1f, 0f), 9f, 42f);
            count += Spinner(t, "Hub_Spin_B", new Vector3(9f, 1.1f, 0f), 9f, -36f);

            // ---------- 断柱廊：沟上两道摆动的刺球 + 西岸尖刺 ----------
            count += Swinger(t, "Pit_Swing_A", new Vector3(-37f, 6f, -7f), 6f, 30f, 55f);
            count += Swinger(t, "Pit_Swing_B", new Vector3(-37f, 6f, 7f), 6f, -30f, 62f);
            count += Spikes(t, "Pit_Spike_", new Vector3(-40.6f, 1f, -4f), 3, 2.4f);
            count += Spikes(t, "Gap_Spike_", new Vector3(-33.4f, 1f, 4f), 3, 2.4f);

            // ---------- 峡谷：三层各一根扫杆 + 崖边尖刺 ----------
            count += Spinner(t, "Canyon_Spin_1", new Vector3(0f, 4.3f, 39f), 11f, 48f);
            count += Spinner(t, "Canyon_Spin_2", new Vector3(0f, 7.3f, 49f), 11f, -54f);
            count += Spinner(t, "Canyon_Spin_3", new Vector3(0f, 10.3f, 59f), 10f, 60f);
            count += Spikes(t, "Canyon_Spike_W_", new Vector3(-11f, 1f, 32f), 3, 2.2f);
            count += Spikes(t, "Canyon_Spike_E_", new Vector3(11f, 7f, 52f), 3, 2.2f);

            // ---------- 上生气流：掉进沟里 / 掉下峡谷的免费回程 ----------
            count += Updraft(t, "Canyon_Updraft_W", new Vector3(-12f, 4f, 36f), 2.5f, 10f, 6f, 3.4f);
            count += Updraft(t, "Canyon_Updraft_E", new Vector3(12f, 8f, 56f), 2.5f, 10f, 6f, 3.4f);

            // ---------- 移动台：峡谷第二层的一个横向捷径 ----------
            count += Mover(t, "Canyon_Lift", new Vector3(8f, 3.4f, 46f), new Vector3(4f, 0.5f, 4f), Vector3.up, 5.6f, 1.1f);

            // ---------- 塌陷板：断柱廊西岸的一段，踩上去 4 秒后塌 ----------
            count += Collapse(t, "Colonnade_Collapse", new Vector3(-46f, 0.4f, -8f), new Vector3(6f, 0.5f, 5f), 4f);
            count += Collapse(t, "Canyon_Collapse", new Vector3(0f, 12.4f, 66f), new Vector3(8f, 0.5f, 4f), 3.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 机关已生成：" + count + " 件（扫杆 / 摆锤 / 尖刺 / 气流 / 移动台 / 塌陷板）");
        }

        private static int Spinner(Transform parent, string name, Vector3 pivotPos, float armLength, float speed)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = pivotPos;

            GameObject arm = Box(pivot.transform, "Arm", Vector3.zero, new Vector3(armLength, 0.6f, 0.9f), m_Pop);
            arm.AddComponent<BubblePopObstacle>().Configure(false, false);

            pivot.AddComponent<RotatingHazard>().Configure(Vector3.up, speed);
            return 2;
        }

        /// <summary>摆动障碍：用 RotatingHazard 的 pingPong 模式做「扫过去再扫回来」。</summary>
        private static int Swinger(Transform parent, string name, Vector3 pivotPos, float length, float angle, float speed)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = pivotPos;

            GameObject rope = Box(pivot.transform, "Rope", new Vector3(0f, -length * 0.5f, 0f), new Vector3(0.12f, length, 0.12f), m_Edge, false);
            rope.transform.localPosition = new Vector3(0f, -length * 0.5f, 0f);

            GameObject ball = Sphere(pivot.transform, "SpikeBall", new Vector3(0f, -length, 0f), 1.6f, m_Pop);
            ball.AddComponent<BubblePopObstacle>().Configure(false, false);

            pivot.AddComponent<RotatingHazard>().Configure(Vector3.forward, speed, true, angle);
            return 2;
        }

        private static int Spikes(Transform parent, string namePrefix, Vector3 start, int count, float spacing)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject spike = Sphere(parent, namePrefix + i, start + new Vector3(0f, 0f, i * spacing), 1.4f, m_Pop);
                spike.AddComponent<BubblePopObstacle>().Configure(false, false);
            }

            return count;
        }

        private static int Updraft(Transform parent, string name, Vector3 center, float width, float height, float depth, float speed)
        {
            var zone = new GameObject(name);
            zone.transform.SetParent(parent, false);
            zone.transform.position = center;

            BoxCollider trigger = zone.AddComponent<BoxCollider>();
            trigger.size = new Vector3(width, height, depth);
            trigger.isTrigger = true;

            zone.AddComponent<WhiteboxUpdraft>().Configure(speed);
            return 1;
        }

        private static int Mover(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 axis, float distance, float speed)
        {
            GameObject platform = Box(parent, name, pos, size, m_Platform);
            platform.AddComponent<WhiteboxPathPlatform>().Configure(axis, distance, speed);
            return 1;
        }

        private static int Collapse(Transform parent, string name, Vector3 pos, Vector3 size, float delay)
        {
            GameObject floor = Box(parent, name, pos, size, m_Edge);
            floor.AddComponent<CollapsingFloor>().Configure(delay);
            return 1;
        }

        // ==================================================================
        //  路线引导：地面引导带 + 箭头 + 出口光柱 + 南区收窄成 S 形
        // ==================================================================

        private const string kGuideRoot = "Wengjie_Route";
        private static Material m_Guide;

        [MenuItem("Tools/PAO/关卡白盒/wengjie 路线引导", priority = 75)]
        public static void BuildRouteGuidance()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kGuideRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kGuideRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            m_Guide = AssetDatabase.LoadAssetAtPath<Material>(kMatFolder + "WB_Landmark.mat");

            int strips = 0;
            int arrows = 0;

            // 主路：出生点 → 广场
            strips += GuideLine(t, "G_Spawn", new Vector3(0f, 0.03f, -20f), new Vector3(0f, 0.03f, -7f), ref arrows);
            // 广场 → 西门（断柱廊）
            strips += GuideLine(t, "G_West", new Vector3(-6f, 0.03f, 0f), new Vector3(-21f, 0.03f, 0f), ref arrows);
            // 广场 → 北门（峡谷）
            strips += GuideLine(t, "G_North", new Vector3(0f, 0.03f, 6f), new Vector3(0f, 0.03f, 23f), ref arrows);
            // 广场 → 南门（新庭院）
            strips += GuideLine(t, "G_South", new Vector3(0f, 0.03f, -6f), new Vector3(0f, 0.03f, -26f), ref arrows);
            // 南区：S 形
            strips += GuideLine(t, "G_S1", new Vector3(0f, 0.03f, -30f), new Vector3(-12f, 0.03f, -34f), ref arrows);
            strips += GuideLine(t, "G_S2", new Vector3(-12f, 0.03f, -34f), new Vector3(-12f, 0.03f, -46f), ref arrows);
            strips += GuideLine(t, "G_S3", new Vector3(-12f, 0.03f, -46f), new Vector3(0f, 0.03f, -50f), ref arrows);

            // 三个出口的「光柱」：远远就能看见
            int beacons = 0;
            beacons += Beacon(t, "B_West", new Vector3(-15f, 0f, 0f), new Color(0.55f, 0.85f, 1f));
            beacons += Beacon(t, "B_North", new Vector3(0f, 0f, 15f), new Color(1f, 0.82f, 0.24f));
            beacons += Beacon(t, "B_South", new Vector3(0f, 0f, -15f), new Color(1f, 0.62f, 0.42f));

            // 南区收窄：两道内墙把大空盒子变成 S 形
            Box(t, "S_Funnel_A", new Vector3(7f, 2.5f, -33f), new Vector3(18f, 5f, 1f), m_Edge);
            Box(t, "S_Funnel_B", new Vector3(-7f, 2.5f, -45f), new Vector3(18f, 5f, 1f), m_Edge);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 路线引导已生成：" + strips + " 段引导带、" + arrows + " 个箭头、" + beacons + " 道光柱、2 道收窄墙");
        }

        /// <summary>沿 A→B 铺一条 1.4 米宽的亮色引导带，每 7 米一个箭头。</summary>
        private static int GuideLine(Transform parent, string name, Vector3 a, Vector3 b, ref int arrows)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.5f) return 0;

            Vector3 dir = delta / length;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            GameObject strip = Box(parent, name, (a + b) * 0.5f, new Vector3(1.4f, 0.06f, length), m_Guide, false);
            strip.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            int steps = Mathf.Max(1, Mathf.FloorToInt(length / 7f));
            for (int i = 0; i <= steps; i++)
            {
                float tt = (i + 0.5f) / (steps + 1);
                Vector3 pos = Vector3.Lerp(a, b, tt) + Vector3.up * 0.04f;

                GameObject arrow = Box(parent, name + "_Arrow" + i, pos, new Vector3(0.9f, 0.06f, 0.5f), m_Guide, false);
                arrow.transform.rotation = Quaternion.Euler(0f, yaw + 45f, 0f);
                arrows++;
            }

            return 1;
        }

        /// <summary>出口光柱：一根细长的亮色柱，远处可见。</summary>
        private static int Beacon(Transform parent, string name, Vector3 basePos, Color color)
        {
            GameObject column = Box(parent, name, basePos + Vector3.up * 4f, new Vector3(0.5f, 8f, 0.5f), m_Guide, false);

            Renderer renderer = column.GetComponent<Renderer>();
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }

            return 1;
        }

        // ==================================================================
        //  扩建 + 美术碰撞 + 会动的机关（真摆锤 / 巡逻刺墙）
        // ==================================================================

        private const string kExpandRoot = "Wengjie_South";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 扩建 + 美术碰撞 + 摆锤", priority = 74)]
        public static void ExpandAndHarden()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kExpandRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kExpandRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ---------- ① 往南扩一片新区：40×36 的庭院 + 走廊 ----------
            Box(t, "S_Corridor", new Vector3(0f, -0.5f, -20.5f), new Vector3(10f, 1f, 12f), m_Ground);
            Box(t, "S_Court_Floor", new Vector3(0f, -0.5f, -44f), new Vector3(40f, 1f, 36f), m_Ground);
            Box(t, "S_Wall_W", new Vector3(-20f, 5f, -44f), new Vector3(1f, 10f, 36f), m_Edge);
            Box(t, "S_Wall_E", new Vector3(20f, 5f, -44f), new Vector3(1f, 10f, 36f), m_Edge);
            Box(t, "S_Wall_S", new Vector3(0f, 5f, -62f), new Vector3(40f, 10f, 1f), m_Edge);
            Box(t, "S_Wall_N_W", new Vector3(-15f, 5f, -26f), new Vector3(10f, 10f, 1f), m_Edge);
            Box(t, "S_Wall_N_E", new Vector3(15f, 5f, -26f), new Vector3(10f, 10f, 1f), m_Edge);

            // 中央高台 + 两根泡柱（延续「拆结构」的玩法）
            Box(t, "S_Dais", new Vector3(0f, 2f, -46f), new Vector3(12f, 4f, 10f), m_Landmark);
            Terrain(t, "S_PillarChock_A", new Vector3(-5.6f, 1.1f, -46f), 2.2f, 3, 5, 8f);
            Terrain(t, "S_PillarChock_B", new Vector3(5.6f, 1.1f, -46f), 2.2f, 3, 5, 8f);

            // ---------- ② 会动的机关 ----------
            int hazards = 0;

            // 大摆锤 ×3：庭院两个，走廊一个
            hazards += Pendulum(t, "S_Pendulum_A", new Vector3(-8f, 9f, -38f), 8f, 42f, 3.4f, Vector3.forward);
            hazards += Pendulum(t, "S_Pendulum_B", new Vector3(8f, 9f, -50f), 8f, 46f, 3.9f, Vector3.forward);
            hazards += Pendulum(t, "S_Pendulum_C", new Vector3(0f, 8f, -20f), 7f, 38f, 3.0f, Vector3.right);

            // 巡逻刺墙 ×2：在庭院里来回扫
            hazards += Patrol(t, "S_Patrol_A", new Vector3(-14f, 2f, -36f), new Vector3(1f, 4f, 8f), Vector3.right, 28f, 2.2f);
            hazards += Patrol(t, "S_Patrol_B", new Vector3(14f, 2f, -52f), new Vector3(1f, 4f, 8f), Vector3.left, 28f, 1.7f);

            // 旋转扫杆 ×1（高台上，守奖励）
            var spinPivot = new GameObject("S_Spin");
            spinPivot.transform.SetParent(t, false);
            spinPivot.transform.position = new Vector3(0f, 5.2f, -46f);
            GameObject spinArm = Box(spinPivot.transform, "Arm", Vector3.zero, new Vector3(12f, 0.5f, 0.8f), m_Pop);
            spinArm.AddComponent<BubblePopObstacle>().Configure(false, false);
            spinPivot.AddComponent<RotatingHazard>().Configure(Vector3.up, 55f);
            hazards += 2;

            // ---------- ③ 给建筑补碰撞 ----------
            int colliders = AddArtColliders();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 扩建完成：南区 40×36 + 摆锤3 / 巡逻刺墙2 / 扫杆1，建筑补碰撞 " + colliders + " 件");
        }

        /// <summary>真摆锤：pivot + 绳 + 刺球，用 WhiteboxSwingMotion 摆动。</summary>
        private static int Pendulum(Transform parent, string name, Vector3 pivotPos, float length, float angle, float period, Vector3 axis)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = pivotPos;

            Box(pivot.transform, "Rope", new Vector3(0f, -length * 0.5f, 0f), new Vector3(0.14f, length, 0.14f), m_Edge, false);

            GameObject ball = Sphere(pivot.transform, "SpikeBall", new Vector3(0f, -length, 0f), 1.9f, m_Pop);
            ball.AddComponent<BubblePopObstacle>().Configure(false, false);

            pivot.AddComponent<WhiteboxSwingMotion>().Configure(axis, angle, period, null, new Vector3(0f, -length, 0f));
            return 2;
        }

        /// <summary>巡逻刺墙：一块会来回移动的刺板。</summary>
        private static int Patrol(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 axis, float distance, float speed)
        {
            GameObject wall = Box(parent, name, pos, size, m_Pop);
            wall.AddComponent<BubblePopObstacle>().Configure(false, false);

            var mover = wall.AddComponent<WhiteboxPathPlatform>();
            mover.Configure(axis, distance, speed);
            return 1;
        }

        /// <summary>给换装进来的建筑补 BoxCollider（植被不加，免得挡路）。</summary>
        private static int AddArtColliders()
        {
            GameObject artRoot = GameObject.Find(kArtRoot);
            if (artRoot == null) return 0;

            int added = 0;
            Transform[] all = artRoot.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                Transform child = all[i];
                if (child.name.StartsWith("T_src_", System.StringComparison.Ordinal)) continue;
                if (child.name.IndexOf("Kelp", System.StringComparison.Ordinal) >= 0) continue;

                // 门洞和地砖**不能**有实心碰撞：
                // 门洞的包围盒会把出口整个封死，地砖比地面高 3cm 会造成卡脚。
                bool skipSolid = child.name.IndexOf("Gate", System.StringComparison.Ordinal) >= 0
                              || child.name.IndexOf("Tile", System.StringComparison.Ordinal) >= 0;
                if (skipSolid)
                {
                    Collider[] existingSkip = child.GetComponents<Collider>();
                    for (int k = 0; k < existingSkip.Length; k++)
                    {
                        if (existingSkip[k] != null && existingSkip[k].enabled) existingSkip[k].enabled = false;
                    }
                    continue;
                }

                if (child.GetComponent<Renderer>() == null) continue;

                Collider existing = child.GetComponent<Collider>();
                if (existing != null)
                {
                    if (!existing.enabled) { existing.enabled = true; added++; }
                    continue;
                }

                Bounds b = ArtMeasure(child.gameObject);
                if (b.size.x < 0.2f || b.size.y < 0.2f || b.size.z < 0.2f) continue;

                var box = child.gameObject.AddComponent<BoxCollider>();
                box.center = child.InverseTransformPoint(b.center);

                Vector3 s = child.lossyScale;
                box.size = new Vector3(
                    b.size.x / Mathf.Max(0.001f, Mathf.Abs(s.x)),
                    b.size.y / Mathf.Max(0.001f, Mathf.Abs(s.y)),
                    b.size.z / Mathf.Max(0.001f, Mathf.Abs(s.z)));
                added++;
            }

            return added;
        }

        // ==================================================================
        //  联动陷阱：一套组合打三个目的
        //    ① 把人退回之前的路径  ② 榨干泡泡  ③ 推下平台
        // ==================================================================

        private const string kTrapRoot = "Wengjie_Traps";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 联动陷阱", priority = 76)]
        public static void BuildTrapCombos()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kTrapRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kTrapRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ================= ① 断柱廊：断退路 + 退回下层 ----------
            // 沟边的窄台：踩上去 0.6 秒后开始塌（人已经走到中间），
            // 1.6 秒后扫杆启动，把人往沟的方向赶；掉进沟 = 退回西岸低层。
            GameObject ledgeA = Box(t, "T1_Ledge", new Vector3(-37f, 1.4f, 0f), new Vector3(5f, 0.4f, 3f), m_Edge);
            var ledgeFloor = ledgeA.AddComponent<CollapsingFloor>();
            ledgeFloor.Configure(3.2f);

            var armA = SpinnerRoot(t, "T1_Sweeper", new Vector3(-37f, 2.6f, 0f), 7f, 70f);

            var trapA = new GameObject("Trap_Backtrack");
            trapA.transform.SetParent(t, false);
            trapA.transform.position = new Vector3(-33.5f, 1.5f, 0f);
            AddTrigger(trapA, new Vector3(5f, 3f, 8f));
            var compA = trapA.AddComponent<WengjieTrap>();
            SetTrapSteps(compA, new object[]
            {
                (MonoBehaviour)ledgeFloor, 0.6f, WengjieTrap.TrapAction.ArmFloor,
                (MonoBehaviour)armA.GetComponent<RotatingHazard>(), 1.6f, WengjieTrap.TrapAction.Enable,
            });

            // ================= ② 峡谷：榨干泡泡 ----------
            // 窄道上：先放尖刺（骑泡过来必破），1.2 秒后再启动巡逻刺墙扫过 ——
            // 玩家要么重新吹泡，要么退回去。两次三番，泡泡值就空了。
            var spikeGroup = new GameObject("T2_Spikes");
            spikeGroup.transform.SetParent(t, false);
            for (int i = 0; i < 3; i++)
            {
                GameObject spike = Sphere(spikeGroup.transform, "T2_Spike_" + i,
                    new Vector3(-4f + i * 4f, 7.6f, 47f), 1.5f, m_Pop);
                spike.AddComponent<BubblePopObstacle>().Configure(false, false);
                spike.GetComponent<Collider>().enabled = false;
            }

            var wallB = Box(t, "T2_Patrol", new Vector3(-12f, 7.5f, 44f), new Vector3(1f, 3f, 8f), m_Pop);
            wallB.AddComponent<BubblePopObstacle>().Configure(false, false);
            var moverB = wallB.AddComponent<WhiteboxPathPlatform>();
            moverB.Configure(Vector3.right, 24f, 3.4f);

            var trapB = new GameObject("Trap_Drain");
            trapB.transform.SetParent(t, false);
            trapB.transform.position = new Vector3(0f, 7.5f, 42f);
            AddTrigger(trapB, new Vector3(24f, 4f, 3f));
            var compB = trapB.AddComponent<WengjieTrap>();
            SetTrapSteps(compB, new object[]
            {
                (MonoBehaviour)wallB.GetComponent<BubblePopObstacle>(), 0.4f, WengjieTrap.TrapAction.Enable,
                moverB, 1.2f, WengjieTrap.TrapAction.Enable,
            });

            // ================= ③ 南区：推下平台 ----------
            // S 形转角的高台边缘：先让「活塞」平台弹出来推人，
            // 0.9 秒后脚下的地板开始塌 —— 被推一下正好踩到塌陷区。
            var piston = Box(t, "T3_Piston", new Vector3(4f, 3.2f, -46f), new Vector3(2f, 3f, 10f), m_Pop);
            piston.AddComponent<BubblePopObstacle>().Configure(false, false);
            var pistonMover = piston.AddComponent<WhiteboxPathPlatform>();
            pistonMover.Configure(Vector3.left, 9f, 6f);

            GameObject pedestal = Box(t, "T3_Pedestal", new Vector3(0f, 2.1f, -46f), new Vector3(6f, 0.4f, 6f), m_Edge);
            var pedestalFloor = pedestal.AddComponent<CollapsingFloor>();
            pedestalFloor.Configure(4.5f);

            var trapC = new GameObject("Trap_Shove");
            trapC.transform.SetParent(t, false);
            trapC.transform.position = new Vector3(0f, 4f, -40f);
            AddTrigger(trapC, new Vector3(16f, 4f, 2f));
            var compC = trapC.AddComponent<WengjieTrap>();
            SetTrapSteps(compC, new object[]
            {
                (MonoBehaviour)pistonMover, 0.2f, WengjieTrap.TrapAction.Enable,
                pedestalFloor, 0.9f, WengjieTrap.TrapAction.ArmFloor,
            });

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 联动陷阱已生成：3 套（断退路 / 榨泡泡 / 推下平台）");
        }

        private static GameObject SpinnerRoot(Transform parent, string name, Vector3 pos, float armLength, float speed)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = pos;

            GameObject arm = Box(pivot.transform, "Arm", Vector3.zero, new Vector3(armLength, 0.5f, 0.8f), m_Pop);
            arm.AddComponent<BubblePopObstacle>().Configure(false, false);

            pivot.AddComponent<RotatingHazard>().Configure(Vector3.up, speed);
            return pivot;
        }

        private static void AddTrigger(GameObject host, Vector3 size)
        {
            BoxCollider trigger = host.AddComponent<BoxCollider>();
            trigger.size = size;
            trigger.isTrigger = true;
        }

        private static void SetTrapSteps(WengjieTrap trap, object[] pairs)
        {
            var so = new SerializedObject(trap);
            SerializedProperty steps = so.FindProperty("m_Steps");
            steps.arraySize = pairs.Length / 3;

            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                step.FindPropertyRelative("target").objectReferenceValue = (MonoBehaviour)pairs[i * 3];
                step.FindPropertyRelative("delay").floatValue = (float)pairs[i * 3 + 1];
                step.FindPropertyRelative("action").enumValueIndex = (int)(WengjieTrap.TrapAction)pairs[i * 3 + 2];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ==================================================================
        //  P1 外壳（分三段交付）
        //    第一段：河岸 + 弧形坡 240° + 环形村台
        //    第二段：断柱街市（270° 双层反向）
        //    第三段：峡谷井 360° + 塔顶
        // ==================================================================

        private const string kP1Root = "Wengjie_P1";
        private const string kStage1Root = "Wengjie_P1_A";
        private const float kRingRadius = 45f;      // 弧形坡半径
        private const float kRingY = 12f;           // 村台高度

        [MenuItem("Tools/PAO/关卡白盒/wengjie P1-1 河岸 + 弧形坡 + 村台", priority = 79)]
        public static void BuildStage1()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            string[] oldRoots = { kP1Root, kV2Root, "Wengjie_South", "Wengjie_Hazards", "Wengjie_Traps", "Wengjie_Plan", "Wengjie_Art", kGuideRoot, kStage1Root };
            for (int i = 0; i < oldRoots.Length; i++)
            {
                GameObject old = GameObject.Find(oldRoots[i]);
                if (old != null) Object.DestroyImmediate(old);
            }

            var root = new GameObject(kStage1Root);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ---------------- S 出生河岸（40 × 30，y=0）----------------
            Box(t, "Hub_Floor_S", new Vector3(0f, -0.5f, -15f), new Vector3(40f, 1f, 30f), m_Ground);
            Box(t, "A_Wall_S_W", new Vector3(-20f, 6f, -15f), new Vector3(1f, 12f, 30f), m_Edge);
            Box(t, "A_Wall_S_E", new Vector3(20f, 6f, -15f), new Vector3(1f, 12f, 30f), m_Edge);
            Box(t, "A_Wall_S_S", new Vector3(0f, 6f, -30f), new Vector3(40f, 12f, 1f), m_Edge);

            // ---------------- A 弧形坡：R=45，12 段 × 20°，累计 240° ----------------
            // 圆心放在 (0, 0, 45)，圆弧从 -90°（原点附近）转到 +150°，
            // 每段同时爬升 1 米 —— 走起来是「绕着村子转」。
            Vector3 arcCenter = new Vector3(0f, 0f, 45f);
            const int segCount = 12;
            const float segAngle = 20f;
            float chord = 2f * kRingRadius * Mathf.Sin(segAngle * 0.5f * Mathf.Deg2Rad);

            for (int i = 0; i < segCount; i++)
            {
                float angle = -90f + (i + 0.5f) * segAngle;    // 段中心的极角
                float rad = angle * Mathf.Deg2Rad;

                Vector3 pos = arcCenter + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * kRingRadius;
                pos.y = (i + 0.5f) * (kRingY / segCount);

                GameObject seg = Box(t, "A_Ramp_" + i, pos, new Vector3(4f, 0.6f, chord), m_Platform);
                seg.transform.rotation = Quaternion.Euler(0f, -angle, 0f);

                // 外侧护栏（只做外沿，内侧敞着 —— 掉下去是河岸，不死）
                Vector3 railPos = arcCenter + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (kRingRadius + 2.2f);
                railPos.y = pos.y + 0.8f;
                GameObject rail = Box(t, "A_RampRail_" + i, railPos, new Vector3(0.4f, 1.2f, chord), m_Edge, false);
                rail.transform.rotation = Quaternion.Euler(0f, -angle, 0f);
            }

            // ---------------- A 环形村台：外径 40 / 内径 18，16 楔 ----------------
            const int wedgeCount = 16;
            const float outerR = 40f;
            const float innerR = 18f;
            float wedgeAngle = 360f / wedgeCount;
            float wedgeChord = 2f * outerR * Mathf.Sin(wedgeAngle * 0.5f * Mathf.Deg2Rad);
            float midR = (outerR + innerR) * 0.5f;

            for (int i = 0; i < wedgeCount; i++)
            {
                float angle = i * wedgeAngle;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = arcCenter + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * midR;
                pos.y = kRingY;

                GameObject wedge = Box(t, "A_Ring_" + i, pos, new Vector3(wedgeChord, 1f, outerR - innerR), m_Ground);
                wedge.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);

                // 内圈矮墙：防止直接掉进中央空洞
                Vector3 wallPos = arcCenter + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * (innerR + 0.4f);
                wallPos.y = kRingY + 0.9f;
                GameObject wall = Box(t, "A_RingWall_" + i, wallPos, new Vector3(wedgeChord * 0.95f, 1.2f, 0.5f), m_Edge);
                wall.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);
            }

            // 坡顶与村台之间的 5 米接缝
            float bridgeRad = 150f * Mathf.Deg2Rad;
            Vector3 bridgePos = arcCenter + new Vector3(Mathf.Cos(bridgeRad), 0f, Mathf.Sin(bridgeRad)) * 42f;
            bridgePos.y = kRingY;
            GameObject bridge = Box(t, "A_RingBridge", bridgePos, new Vector3(6f, 1f, 8f), m_Platform);
            bridge.transform.rotation = Quaternion.Euler(0f, 90f - 150f, 0f);

            // ---------------- 系统 ----------------
            GameObject spawn = new GameObject("Spawn");
            spawn.transform.SetParent(t, false);
            spawn.transform.position = new Vector3(0f, 0.6f, -28f);

            GameObject respawn = new GameObject("FallRespawn");
            respawn.transform.SetParent(t, false);
            respawn.AddComponent<FallRespawn>().Configure(-12f, spawn.transform);

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = new Vector3(0f, 1.2f, -28f);
                player.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie P1-1] 河岸 + 弧形坡 + 村台 已搭好：\n" +
                      "  弧形坡 R=45，12 段 × 20°，累计 240°，爬升 12 米\n" +
                      "  村台 外径 40 / 内径 18，16 楔，中央空洞（下面留给街市）\n" +
                      "  坡顶与村台之间留了 5 米接缝。");
        }

        // ---------------- 第二段：断柱街市 ----------------

        private const string kStage2Root = "Wengjie_P1_B";

        [MenuItem("Tools/PAO/关卡白盒/wengjie P1-2 断柱街市", priority = 80)]
        public static void BuildStage2()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kStage2Root);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kStage2Root);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // 街市圆心放在村台北面 65 米，半径 36
            Vector3 c = new Vector3(0f, 0f, 110f);
            const float streetR = 36f;
            const float roofR = 30f;
            const int seg = 12;
            const float step = 22.5f;
            float chordStreet = 2f * streetR * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
            float chordRoof = 2f * roofR * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);

            // ---------- 村里 → 街市的连接桥（z 74 ↔ 85）----------
            Box(t, "B_Bridge", new Vector3(0f, 11.5f, 79.5f), new Vector3(8f, 1f, 13f), m_Platform);

            // ---------- 街市地面：270°，y=12 ----------
            for (int i = 0; i < seg; i++)
            {
                float angle = i * step;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * streetR;
                pos.y = 12f;

                GameObject s = Box(t, "B_Street_" + i, pos, new Vector3(12f, 0.8f, chordStreet), m_Ground);
                s.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);
            }

            // ---------- 中央钟楼：直径 24，高 22 ----------
            Box(t, "B_BellTower", new Vector3(c.x, 23f, c.z), new Vector3(24f, 22f, 24f), m_Landmark);

            // ---------- 屋顶：反向 270°，y=26 ----------
            for (int i = 0; i < seg; i++)
            {
                float angle = 270f - i * step;     // 反向
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * roofR;
                pos.y = 26f;

                GameObject s = Box(t, "B_Roof_" + i, pos, new Vector3(10f, 0.6f, chordRoof), m_Platform);
                s.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);
            }

            // ---------- 四道梯：地面 ↔ 屋顶 ----------
            float[] ladderAngles = { 0f, 90f, 180f, 270f };
            for (int i = 0; i < ladderAngles.Length; i++)
            {
                float rad = ladderAngles[i] * Mathf.Deg2Rad;
                Vector3 pos = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 33f;
                pos.y = 19f;
                Box(t, "B_Ladder_" + i, pos, new Vector3(0.8f, 14f, 0.8f), m_Edge);
            }

            // ---------- 断柱沟：东侧 6 宽 × 24 长 × 12 深 ----------
            Vector3 pitPos = c + new Vector3(36f, 6f, 0f);
            Box(t, "B_Pit", pitPos, new Vector3(6f, 12f, 24f), m_Void);

            // ---------- 四根可倒柱：立在沟内侧 ----------
            for (int i = 0; i < 4; i++)
            {
                Vector3 basePos = c + new Vector3(32.6f, 12f, -9f + i * 6f);
                float rad = 10f * Mathf.Deg2Rad;
                Vector3 up = new Vector3(-Mathf.Sin(rad), Mathf.Cos(rad), 0f);
                Vector3 center = basePos + up * 4f;

                GameObject column = Box(t, "B_Column_" + i, center, new Vector3(1.1f, 8f, 1.1f), m_Bounce);
                column.transform.rotation = Quaternion.Euler(0f, 0f, 10f);
            }

            // ---------- 下水道：沟底 y=6，直道通北 ----------
            Box(t, "B_Sewer", new Vector3(c.x + 30f, 6f, c.z), new Vector3(8f, 3f, 24f), m_Void);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie P1-2] 断柱街市已搭好：\n" +
                      "  街市 12 段 × 22.5° = 270°（y=12），半径 36\n" +
                      "  屋顶 12 段 = 反向 270°（y=26），半径 30\n" +
                      "  钟楼 24×22，四道梯，断柱沟 6×24×12，下水道 y=6");
        }

        // ---------------- 第三段：峡谷井 + 塔顶 ----------------

        private const string kStage3Root = "Wengjie_P1_C";

        [MenuItem("Tools/PAO/关卡白盒/wengjie P1-3 峡谷井 + 塔顶", priority = 81)]
        public static void BuildStage3()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kStage3Root);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kStage3Root);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 c = new Vector3(0f, 0f, 200f);
            const float floorY = 34f;
            const float topY = 64f;
            const float half = 33f;      // 内净 66

            // ---------- 屋顶 → 井底：16 级台阶（+26 → +34）----------
            for (int i = 0; i < 16; i++)
            {
                Box(t, "C_Stair_" + i, new Vector3(0f, 26.25f + i * 0.5f, 148f + i * 1.6f),
                    new Vector3(10f, 0.5f, 1.8f), m_Platform);
            }

            // ---------- 井底 ----------
            Box(t, "Hub_Floor_C", new Vector3(c.x, floorY - 0.5f, c.z), new Vector3(half * 2f, 1f, half * 2f), m_Ground);

            // ---------- 井壁（南墙留门）----------
            float wallH = topY - floorY + 6f;
            float wallMidY = (floorY + topY) * 0.5f;
            Box(t, "C_Wall_N", new Vector3(c.x, wallMidY, c.z + half), new Vector3(half * 2f + 4f, wallH, 2f), m_Edge);
            Box(t, "C_Wall_E", new Vector3(c.x + half, wallMidY, c.z), new Vector3(2f, wallH, half * 2f), m_Edge);
            Box(t, "C_Wall_W", new Vector3(c.x - half, wallMidY, c.z), new Vector3(2f, wallH, half * 2f), m_Edge);
            Box(t, "C_Wall_S_L", new Vector3(c.x - 20f, wallMidY, c.z - half), new Vector3(26f, wallH, 2f), m_Edge);
            Box(t, "C_Wall_S_R", new Vector3(c.x + 20f, wallMidY, c.z - half), new Vector3(26f, wallH, 2f), m_Edge);
            Box(t, "C_Wall_S_Top", new Vector3(c.x, topY + 2f, c.z - half), new Vector3(14f, 10f, 2f), m_Edge);

            // ---------- 六层岩台：每层转 60°，整体 360° ----------
            const int ledgeCount = 6;
            for (int i = 0; i < ledgeCount; i++)
            {
                float angle = i * 60f;
                float rad = angle * Mathf.Deg2Rad;
                float y = 38f + i * 4f;                 // +38 → +58

                Vector3 pos = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 24f;
                pos.y = y;

                Box(t, "C_Ledge_" + i, pos, new Vector3(16f, 1f, 16f), m_Platform);
            }

            // ---------- 塔顶：环形，中间留 20×20 的井口 ----------
            const float topHalf = 20f;
            Box(t, "Hub_Floor_C_N", new Vector3(c.x, topY - 0.5f, c.z + 15f), new Vector3(topHalf * 2f, 1f, 10f), m_Landmark);
            Box(t, "Hub_Floor_C_S", new Vector3(c.x, topY - 0.5f, c.z - 15f), new Vector3(topHalf * 2f, 1f, 10f), m_Landmark);
            Box(t, "Hub_Floor_C_E", new Vector3(c.x + 15f, topY - 0.5f, c.z), new Vector3(10f, 1f, 20f), m_Landmark);
            Box(t, "Hub_Floor_C_W", new Vector3(c.x - 15f, topY - 0.5f, c.z), new Vector3(10f, 1f, 20f), m_Landmark);

            // ---------- 祭坛 ----------
            Box(t, "Altar_Core", new Vector3(c.x, topY + 0.6f, c.z + 13f), new Vector3(4f, 1.2f, 4f), m_Bounce);

            GameObject goal = new GameObject("B_Goal");
            goal.transform.SetParent(t, false);
            goal.transform.position = new Vector3(c.x, topY + 1.6f, c.z + 13f);
            BubbleTideGoal tideGoal = goal.AddComponent<BubbleTideGoal>();
            SerializedObject goalSo = new SerializedObject(tideGoal);
            goalSo.FindProperty("m_GraceSeconds").floatValue = 8f;
            goalSo.FindProperty("m_ReachDistance").floatValue = 3f;
            goalSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie P1-3] 峡谷井 + 塔顶已搭好：\n" +
                      "  井 66×66，y=34→64，六层岩台每层转 60°（累计 360°）\n" +
                      "  塔顶环形平台，中央 20×20 井口，祭坛在 +64.6");
        }

        // ==================================================================
        //  六个障碍机制 × 各 7 次 = 42 处
        //  M1 尺寸门 / M2 粘附点 / M3 填充锁 / M4 引爆点 / M5 水位 / M6 破泡栅栏
        // ==================================================================

        private const string kObstacleRoot = "Wengjie_Obstacles";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 障碍矩阵（6×7）", priority = 82)]
        public static void BuildObstacleMatrix()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kObstacleRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kObstacleRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 cA = new Vector3(0f, 0f, 45f);      // 村台圆心
            Vector3 cB = new Vector3(0f, 0f, 110f);     // 街市圆心
            Vector3 cC = new Vector3(0f, 0f, 200f);     // 井心

            int m1 = 0, m2 = 0, m3 = 0, m4 = 0, m6 = 0;

            // ================= M1 尺寸门（7）=================
            // 门框 + 中间一颗「塞住洞口的泡」。孔越小，射击越难。
            m1 += SizeGate(t, "M1_Ramp_A", ArcPoint(cA, 45f, -30f, 3.5f), 1.0f);
            m1 += SizeGate(t, "M1_Ramp_B", ArcPoint(cA, 45f, 50f, 7.5f), 0.6f);
            m1 += SizeGate(t, "M1_RingOuter", ArcPoint(cA, 34f, 30f, 12f), 1.4f);
            m1 += SizeGate(t, "M1_StreetIn", ArcPoint(cB, 36f, 0f, 12f), 1.0f);
            m1 += SizeGate(t, "M1_StreetTop", ArcPoint(cB, 36f, 90f, 16f), 0.8f);
            m1 += SizeGate(t, "M1_Well2", WellLedge(cC, 1), 1.2f);
            m1 += SizeGate(t, "M1_Well5", WellLedge(cC, 4), 0.5f);

            // ================= M2 粘附点（7）=================
            m2 += Sticky(t, "M2_Ramp_A", ArcPoint(cA, 43f, 10f, 2.5f), new Vector3(0.6f, 4f, 4f));
            m2 += Sticky(t, "M2_Ramp_B", ArcPoint(cA, 43f, 90f, 9.5f), new Vector3(0.6f, 4f, 4f));
            m2 += Sticky(t, "M2_RingInner", ArcPoint(cA, 18.5f, 60f, 15f), new Vector3(0.6f, 5f, 5f));
            m2 += Sticky(t, "M2_RingHole", ArcPoint(cA, 17f, 210f, 8f), new Vector3(0.6f, 6f, 5f));
            m2 += Sticky(t, "M2_RingExit", ArcPoint(cA, 20f, 100f, 18f), new Vector3(0.6f, 5f, 5f));
            m2 += Sticky(t, "M2_TowerFace", cB + new Vector3(13.5f, 20f, 6f), new Vector3(0.6f, 6f, 5f));
            m2 += Sticky(t, "M2_RoofEave", ArcPoint(cB, 30f, 180f, 23f), new Vector3(0.6f, 5f, 5f));

            // ================= M3 填充锁（7）=================
            m3 += FillLock(t, "M3_RingExit", ArcPoint(cA, 30f, 150f, 12.6f), 1);
            m3 += FillLock(t, "M3_RingSide", ArcPoint(cA, 26f, 200f, 12.6f), 3);
            m3 += FillLock(t, "M3_ShopDoor", ArcPoint(cB, 33f, 45f, 12.6f), 2);
            m3 += FillLock(t, "M3_StreetMid", ArcPoint(cB, 36f, 135f, 12.6f), 4);
            m3 += FillLock(t, "M3_ColumnFoot", cB + new Vector3(31.4f, 12.9f, -9f), 3);
            m3 += FillLock(t, "M3_Well3", WellLedge(cC, 2) + Vector3.up * 1.2f, 5);
            m3 += FillLock(t, "M3_Well6", WellLedge(cC, 5) + Vector3.up * 1.2f, 2);

            // ================= M4 引爆点（7）=================
            // 可被炸弹推动的刚体：炸开的路 / 推倒的墙 / 砸下来的石头
            m4 += Blastable(t, "M4_CrackWall", ArcPoint(cB, 33f, 200f, 13f), new Vector3(3f, 4f, 1f), 20f);
            m4 += Blastable(t, "M4_Crates", ArcPoint(cB, 34f, 160f, 13f), new Vector3(2f, 2f, 2f), 8f);
            m4 += Blastable(t, "M4_RoofColumn", ArcPoint(cB, 30f, 240f, 27f), new Vector3(1.2f, 8f, 1.2f), 30f);
            m4 += Blastable(t, "M4_Chandelier", ArcPoint(cB, 26f, 300f, 24f), new Vector3(3f, 3f, 3f), 40f);
            m4 += Blastable(t, "M4_VentSlab", ArcPoint(cB, 30f, 20f, 27f), new Vector3(4f, 0.6f, 4f), 15f);
            m4 += Blastable(t, "M4_WellPillar", WellLedge(cC, 3) + new Vector3(0f, 3f, 0f), new Vector3(1.5f, 6f, 1.5f), 35f);
            m4 += Blastable(t, "M4_Stalactite", WellLedge(cC, 5) + new Vector3(6f, 4f, 0f), new Vector3(2f, 6f, 2f), 25f);

            // ================= M5 水位（1 套 + 6 个观测点）=================
            BuildWellTide(t, cC);

            // ================= M6 破泡栅栏（7）=================
            m6 += Fence(t, "M6_StreetIn", ArcPoint(cB, 36f, 15f, 13f), new Vector3(8f, 3f, 0.5f), 0f);
            m6 += Fence(t, "M6_StreetMid", ArcPoint(cB, 36f, 90f, 13f), new Vector3(8f, 3f, 0.5f), 90f);
            m6 += Fence(t, "M6_ShopInside", ArcPoint(cB, 33f, 45f, 13f), new Vector3(5f, 3f, 0.5f), 45f);
            m6 += Fence(t, "M6_RoofEdge", ArcPoint(cB, 30f, 120f, 27f), new Vector3(8f, 2.5f, 0.5f), 120f);
            m6 += Fence(t, "M6_RoofColumn", ArcPoint(cB, 30f, 210f, 27.5f), new Vector3(8f, 2.5f, 0.5f), 210f);
            m6 += Fence(t, "M6_Attic", ArcPoint(cB, 30f, 330f, 27f), new Vector3(6f, 3f, 0.5f), 330f);
            m6 += Fence(t, "M6_LadderTop", ArcPoint(cB, 33f, 270f, 27f), new Vector3(6f, 2.5f, 0.5f), 270f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log(string.Format(
                "[Wengjie] 障碍矩阵已铺：M1 {0} / M2 {1} / M3 {2} / M4 {3} / M5 1 套 / M6 {4}（合计 {5} 处）",
                m1, m2, m3, m4, m6, m1 + m2 + m3 + m4 + 7 + m6));
        }

        // ---------- 坐标工具 ----------

        private static Vector3 ArcPoint(Vector3 center, float radius, float angleDeg, float y)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Cos(rad) * radius, y, Mathf.Sin(rad) * radius);
        }

        private static Vector3 WellLedge(Vector3 center, int index)
        {
            float angle = index * 60f;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 24f;
            pos.y = 38f + index * 4f + 0.6f;
            return pos;
        }

        // ---------- 六个生成函数 ----------

        /// <summary>M1 尺寸门：门框 + 塞住洞口的泡。孔越小越难瞄。</summary>
        private static int SizeGate(Transform parent, string name, Vector3 pos, float holeDiameter)
        {
            var frame = new GameObject(name);
            frame.transform.SetParent(parent, false);
            frame.transform.position = pos;

            float w = holeDiameter + 1.6f;
            float h = holeDiameter + 1.6f;

            Box(frame.transform, "L", new Vector3(-w * 0.5f, 0f, 0f), new Vector3(0.6f, h + 2f, 0.8f), m_Edge);
            Box(frame.transform, "R", new Vector3(w * 0.5f, 0f, 0f), new Vector3(0.6f, h + 2f, 0.8f), m_Edge);
            Box(frame.transform, "T", new Vector3(0f, h * 0.5f + 0.8f, 0f), new Vector3(w + 1f, 0.6f, 0.8f), m_Edge);
            Box(frame.transform, "B", new Vector3(0f, -h * 0.5f - 0.8f, 0f), new Vector3(w + 1f, 0.6f, 0.8f), m_Edge);

            // 洞口塞一颗泡：填满它就炸开
            GameObject plug = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            plug.name = "Plug";
            plug.transform.SetParent(frame.transform, false);
            plug.transform.localPosition = Vector3.zero;
            plug.transform.localScale = Vector3.one * holeDiameter;
            Renderer r = plug.GetComponent<Renderer>();
            if (r != null && m_Destructible != null) r.sharedMaterial = m_Destructible;
            plug.AddComponent<TerrainBubble>();

            return 1;
        }

        /// <summary>M2 粘附点：一块可粘墙。</summary>
        private static int Sticky(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = Box(parent, name, pos, size, m_Sticky);
            go.AddComponent<StickySurface>();
            return 1;
        }

        /// <summary>M3 填充锁：一颗需要填满的地形泡。</summary>
        private static int FillLock(Transform parent, string name, Vector3 pos, int capacity)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (1.4f + capacity * 0.3f);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && m_Destructible != null) r.sharedMaterial = m_Destructible;

            TerrainBubble tb = go.AddComponent<TerrainBubble>();
            SerializedObject so = new SerializedObject(tb);
            so.FindProperty("m_Capacity").intValue = capacity;
            so.FindProperty("m_RewardAmount").intValue = 5;
            so.FindProperty("m_RespawnDelay").floatValue = 8f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        /// <summary>M4 引爆点：可被炸弹推动的刚体。</summary>
        private static int Blastable(Transform parent, string name, Vector3 pos, Vector3 size, float mass)
        {
            GameObject go = Box(parent, name, pos, size, m_Bounce);
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.drag = 0.3f;
            body.angularDrag = 0.6f;
            return 1;
        }

        /// <summary>M6 破泡栅栏：碰到就打爆载人泡泡。</summary>
        private static int Fence(Transform parent, string name, Vector3 pos, Vector3 size, float yaw)
        {
            GameObject go = Box(parent, name, pos, size, m_Pop);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<BubblePopObstacle>().Configure(false, false);
            return 1;
        }

        /// <summary>M5 水位：井里的潮水（1 套总控 + 8 个源 + 1 个门）。</summary>
        private static void BuildWellTide(Transform parent, Vector3 center)
        {
            GameObject tide = new GameObject("M5_BubbleTide");
            tide.transform.SetParent(parent, false);
            tide.transform.position = center;

            BubbleTideDirector director = tide.AddComponent<BubbleTideDirector>();
            SerializedObject so = new SerializedObject(director);
            so.FindProperty("m_AutoStart").boolValue = false;
            so.FindProperty("m_BaseSpeed").floatValue = 0.55f;
            so.FindProperty("m_CellSize").floatValue = 0.8f;
            so.FindProperty("m_BubbleRadius").floatValue = 0.34f;
            so.FindProperty("m_BubbleSpacing").floatValue = 0.5f;
            so.FindProperty("m_PushSpeed").floatValue = 3.5f;
            so.FindProperty("m_UseExplicitBounds").boolValue = true;
            so.FindProperty("m_BoundsMin").vector3Value = center + new Vector3(-33f, 34f, -33f);
            so.FindProperty("m_BoundsMax").vector3Value = center + new Vector3(33f, 62f, 33f);
            so.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var anchor = new GameObject("M5_Anchor_" + i);
                anchor.transform.SetParent(parent, false);
                anchor.transform.position = center + new Vector3(Mathf.Cos(a) * 28f, 34.6f, Mathf.Sin(a) * 28f);
                anchor.AddComponent<BubbleTideAnchor>();
            }

            var gate = new GameObject("M5_Gate");
            gate.transform.SetParent(parent, false);
            gate.transform.position = center + new Vector3(0f, 35f, -32f);
            BoxCollider trigger = gate.AddComponent<BoxCollider>();
            trigger.size = new Vector3(14f, 4f, 3f);
            trigger.isTrigger = true;
            gate.AddComponent<WengjieTideGate>();
        }

        // ==================================================================
        //  「狠」的一轮：加密、加坑、加吊物、加假地板、潮水提速
        //  原则：不新增机制，只把已有机制摆到「必须一次做对」的位置
        // ==================================================================

        private const string kCruelRoot = "Wengjie_Cruel";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 加狠（加密陷阱）", priority = 83)]
        public static void BuildCruelty()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kCruelRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kCruelRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 cA = new Vector3(0f, 0f, 45f);
            Vector3 cB = new Vector3(0f, 0f, 110f);

            int count = 0;

            // ---------- ① 尖刺阵：5 处，每处 6 根，间距 1.2（必须精确走位）----------
            count += SpikeLine(t, "Spike_Ramp", ArcPoint(cA, 45f, 15f, 2.2f), 6, 1.2f, 20f);
            count += SpikeLine(t, "Spike_RingExit", ArcPoint(cA, 30f, 148f, 13f), 6, 1.2f, 150f);
            count += SpikeLine(t, "Spike_StreetIn", ArcPoint(cB, 36f, 3f, 13f), 6, 1.2f, 90f);
            count += SpikeLine(t, "Spike_StreetMid", ArcPoint(cB, 36f, 100f, 13f), 6, 1.2f, 10f);
            count += SpikeLine(t, "Spike_Roof", ArcPoint(cB, 30f, 160f, 27f), 6, 1.2f, 70f);

            // ---------- ② 悬挂重物：4 处，被打中会掉下来把路堵死 ----------
            count += HangingWeight(t, "Drop_Street", ArcPoint(cB, 34f, 60f, 20f), 12f);
            count += HangingWeight(t, "Drop_Street2", ArcPoint(cB, 34f, 120f, 20f), 12f);
            count += HangingWeight(t, "Drop_Roof", ArcPoint(cB, 28f, 200f, 34f), 10f);
            count += HangingWeight(t, "Drop_Well", new Vector3(0f, 52f, 200f), 14f);

            // ---------- ③ 连续塌陷：4 块，2 秒，在唯一通路上 ----------
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = ArcPoint(cB, 36f, 70f + i * 6f, 12.4f);
                GameObject plate = Box(t, "Fake_Street_" + i, p, new Vector3(6f, 0.4f, 5f), m_Ground);
                plate.AddComponent<CollapsingFloor>().Configure(2f);
                count++;
            }

            // ---------- ④ 假地板：3 块，和普通地板同材质 ----------
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = ArcPoint(cA, 30f, 40f + i * 40f, 12.4f);
                GameObject plate = Box(t, "Fake_Ring_" + i, p, new Vector3(6f, 0.4f, 6f), m_Ground);
                plate.AddComponent<CollapsingFloor>().Configure(1.2f);
                count++;
            }

            // ---------- ⑤ 双栅栏：3 组，间隔 3 米，必须一次越过两道 ----------
            count += FencePair(t, "Pair_Street", ArcPoint(cB, 36f, 40f, 13f), 90f);
            count += FencePair(t, "Pair_Roof", ArcPoint(cB, 30f, 250f, 27f), 30f);
            count += FencePair(t, "Pair_Exit", ArcPoint(cB, 33f, 300f, 27f), 120f);

            // ---------- ⑥ 潮水提速：0.55 → 0.9，宽限 8 → 5 ----------
            BubbleTideDirector director = Object.FindFirstObjectByType<BubbleTideDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                SerializedObject so = new SerializedObject(director);
                so.FindProperty("m_BaseSpeed").floatValue = 0.9f;
                so.FindProperty("m_PushSpeed").floatValue = 4.5f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            BubbleTideGoal goal = Object.FindFirstObjectByType<BubbleTideGoal>(FindObjectsInactive.Include);
            if (goal != null)
            {
                SerializedObject so = new SerializedObject(goal);
                so.FindProperty("m_GraceSeconds").floatValue = 5f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 加狠完成：" + count + " 处（尖刺 5 组 / 吊物 4 / 假地板 7 / 双栅栏 3 组）+ 潮水提速到 0.9");
        }

        private static int SpikeLine(Transform parent, string name, Vector3 center, int count, float spacing, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * spacing;
                Vector3 pos = center + rot * new Vector3(offset, 0f, 0f);

                GameObject spike = Sphere(parent, name + "_" + i, pos, 1.3f, m_Pop);
                spike.AddComponent<BubblePopObstacle>().Configure(false, true);
            }

            return count;
        }

        private static int HangingWeight(Transform parent, string name, Vector3 pos, float mass)
        {
            GameObject weight = Box(parent, name, pos, new Vector3(3.5f, 3.5f, 3.5f), m_Landmark);
            Rigidbody body = weight.AddComponent<Rigidbody>();
            body.mass = mass;
            body.drag = 0.1f;
            body.angularDrag = 0.3f;

            // 吊绳：纯装饰
            Box(parent, name + "_Rope", pos + Vector3.up * 6f, new Vector3(0.2f, 12f, 0.2f), m_Edge, false);
            return 1;
        }

        private static int FencePair(Transform parent, string name, Vector3 pos, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = pos + rot * new Vector3(0f, 0f, (i - 0.5f) * 3f);
                GameObject fence = Box(parent, name + "_" + i, p, new Vector3(8f, 3f, 0.4f), m_Pop);
                fence.transform.rotation = rot;
                fence.AddComponent<BubblePopObstacle>().Configure(false, false);
            }

            return 2;
        }

        // ==================================================================
        //  轮转地形：整体大轮转（村台整环）+ 局部小轮转（街市转盘）
        //
        //  用 RotatingHazard 转**地形本身**，不是转机关：
        //    整体：把 16 块环楔 + 16 段矮墙挂到一个中心 pivot 下，2.5°/s（144 秒一圈）
        //    局部：街市上放 3 块转盘，8~12°/s，反向
        //
        //  【注意】RotatingHazard 不会「带着玩家走」，它只是让地面转。
        //  玩家站在上面会被碰撞体慢慢推着走 —— 这正是要的效果：
        //  站着不动就会被推到边缘，想留在原地必须一直移动。
        // ==================================================================

        private const string kRotateRoot = "Wengjie_Rotate";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 轮转地形", priority = 84)]
        public static void BuildRotatingTerrain()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRotateRoot);
            if (old != null) Object.DestroyImmediate(old);

            GameObject stageA = GameObject.Find("Wengjie_P1_A");
            if (stageA == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_P1_A，先跑 P1-1。");
                return;
            }

            // ---------- ① 整体大轮转：村台整环 ----------
            var root = new GameObject(kRotateRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            var ringPivot = new GameObject("RingPivot");
            ringPivot.transform.SetParent(t, false);
            ringPivot.transform.position = new Vector3(0f, 12f, 45f);

            int moved = 0;
            Transform[] children = stageA.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (!child.name.StartsWith("A_Ring", System.StringComparison.Ordinal)) continue;
                if (child.name == "A_RingBridge") continue;      // 接缝不能跟着转

                child.SetParent(ringPivot.transform, true);       // 保留世界位置
                moved++;
            }

            ringPivot.AddComponent<RotatingHazard>().Configure(Vector3.up, 2.5f);

            // ---------- ② 局部小轮转：街市三块转盘 ----------
            Vector3 cB = new Vector3(0f, 0f, 110f);
            int discs = 0;

            discs += Turntable(t, "Disc_A", ArcPoint(cB, 34f, 30f, 12.3f), 10f, 9f);
            discs += Turntable(t, "Disc_B", ArcPoint(cB, 34f, 150f, 12.3f), 10f, -12f);
            discs += Turntable(t, "Disc_C", ArcPoint(cB, 30f, 240f, 26.3f), 8f, 7f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 轮转地形已启用：村台整环 " + moved + " 块（2.5°/s，144 秒一圈）+ 街市转盘 " + discs + " 块");
        }

        private static int Turntable(Transform parent, string name, Vector3 pos, float diameter, float speed)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.position = pos;

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Surface";
            disc.transform.SetParent(pivot.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localScale = new Vector3(diameter, 0.2f, diameter);

            Renderer r = disc.GetComponent<Renderer>();
            if (r != null && m_Platform != null) r.sharedMaterial = m_Platform;

            pivot.AddComponent<RotatingHazard>().Configure(Vector3.up, speed);
            return 1;
        }

        // ==================================================================
        //  反向咬合：外环顺时针 + 内环逆时针
        //
        //  外环（已有的 16 楔，R=18..40）转 +2.5°/s
        //  内环（新增 8 块扇形带，R=18..24，抬高 0.6 米）转 −4°/s
        //  内圈的矮墙一起交给内环，视觉上才是「两圈咬着转」。
        // ==================================================================

        private const string kCounterRoot = "Wengjie_CounterRing";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 反向咬合（内环逆转）", priority = 85)]
        public static void BuildCounterRing()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kCounterRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kCounterRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 cA = new Vector3(0f, 12f, 45f);

            var inner = new GameObject("RingPivotInner");
            inner.transform.SetParent(t, false);
            inner.transform.position = cA;

            // ---------- 内环带：8 块扇形，R=18..24 ----------
            const int sectors = 8;
            const float innerR = 18f;
            const float outerR = 24f;
            float step = 360f / sectors;
            float chord = 2f * outerR * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
            float midR = (innerR + outerR) * 0.5f;

            for (int i = 0; i < sectors; i++)
            {
                float angle = i * step;
                float rad = angle * Mathf.Deg2Rad;

                // 【上下交错】奇偶叶片差 0.45 米 —— 比 step offset 高、比跳跃低，
                // 所以相邻两片之间必须小跳一下，不能再平着走。
                float lift = (i % 2 == 0) ? 0f : 0.45f;
                Vector3 pos = cA + new Vector3(Mathf.Cos(rad), 0.6f + lift, Mathf.Sin(rad)) * midR;

                GameObject s = Box(inner.transform, "InnerBand_" + i, pos, new Vector3(chord, 0.5f, outerR - innerR), m_Platform);

                // 【交错】每片叶片有自己的初始相位 + 自己的转速：
                //   相位错开半个扇形（11.25°），转速按 14/18/22/26 循环、正反交替。
                //   相邻两片的相对速度因此不是固定值，而是一排「咬合但不齐」的齿轮。
                float phase = (i % 2 == 0) ? 0f : step * 0.5f;
                s.transform.rotation = Quaternion.Euler(0f, 90f - angle + phase + i * 3f, 0f);

                float speed = 14f + (i % 4) * 4f;
                if (i % 2 == 1) speed = -speed;
                s.AddComponent<RotatingHazard>().Configure(Vector3.up, speed);
            }

            inner.AddComponent<RotatingHazard>().Configure(Vector3.up, -4f);

            // ---------- 内圈矮墙：直接建在内环上（和外环反向）----------
            // 注意：不要在别的根物体里「搬」这些墙 —— 那个根一重建，墙就跟着被删了。
            int moved = 0;
            for (int i = 0; i < sectors; i++)
            {
                float angle = i * step;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = cA + new Vector3(Mathf.Cos(rad), 1.5f, Mathf.Sin(rad)) * (innerR + 0.5f);

                GameObject wall = Box(inner.transform, "InnerWall_" + i, pos, new Vector3(chord * 0.92f, 1.2f, 0.6f), m_Edge);
                wall.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);
                moved++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 反向咬合已启用：内环 8 块（−4°/s）+ 内圈矮墙 " + moved + " 段；外环仍为 +2.5°/s");
        }

        // ==================================================================
        //  把 M1 尺寸门的「洞口泡」改成接近即炸 + 弹飞
        //  原来它只是「填满才炸」的死物，玩家贴着站也没事 —— 手感太温。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 尺寸门改成接近引爆", priority = 86)]
        public static void MakeGatesExplosive()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject obstacles = GameObject.Find(kObstacleRoot);
            if (obstacles == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_Obstacles，先跑障碍矩阵。");
                return;
            }

            int converted = 0;
            TerrainBubble[] bubbles = obstacles.GetComponentsInChildren<TerrainBubble>(true);

            for (int i = 0; i < bubbles.Length; i++)
            {
                TerrainBubble bubble = bubbles[i];

                // 只处理尺寸门里那几颗「塞洞口的泡」
                if (bubble.name != "Plug") continue;

                GameObject go = bubble.gameObject;

                SphereCollider trigger = go.GetComponent<SphereCollider>();
                if (trigger == null) trigger = go.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 2.2f;          // 比泡本身大一圈，靠近就炸

                WengjieProximityBurst burst = go.GetComponent<WengjieProximityBurst>();
                if (burst == null) burst = go.AddComponent<WengjieProximityBurst>();

                SerializedObject so = new SerializedObject(burst);
                so.FindProperty("m_Delay").floatValue = 0.16f;
                so.FindProperty("m_LaunchSpeed").floatValue = 20f;
                so.FindProperty("m_LaunchDuration").floatValue = 0.45f;
                so.FindProperty("m_UpBias").floatValue = 0.55f;
                so.FindProperty("m_Target").objectReferenceValue = bubble;
                so.FindProperty("m_DestroySelf").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                converted++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 尺寸门已改成接近引爆：" + converted + " 颗泡（延迟 0.16 秒，弹飞 20 m/s，向上偏置 0.55）");
        }

        // ==================================================================
        //  斜飞巡逻雷：沿斜轴 PingPong 飞出去再飞回来，碰到就炸 + 弹飞
        //  用 WhiteboxPathPlatform 做往返（它本来就是 PingPong），
        //  轴给一个斜向量就得到「斜着飞过去又回来」。
        // ==================================================================

        private const string kMineRoot = "Wengjie_Mines";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 斜飞巡逻雷", priority = 87)]
        public static void BuildPatrolMines()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kMineRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kMineRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            int count = 0;

            // 广场环上：横跨缺口斜飞
            count += Mine(t, "Mine_Ring_A", new Vector3(-8f, 15f, 45f), new Vector3(1f, 0.5f, 1f), 22f, 3.2f);
            count += Mine(t, "Mine_Ring_B", new Vector3(8f, 17f, 45f), new Vector3(-1f, 0.5f, -1f), 22f, 2.7f);

            // 街市：沿环形街斜穿
            count += Mine(t, "Mine_Street_A", new Vector3(0f, 15f, 130f), new Vector3(1f, 0.4f, -1f), 18f, 3.6f);
            count += Mine(t, "Mine_Street_B", new Vector3(0f, 15f, 95f), new Vector3(-1f, 0.4f, 1f), 18f, 3.0f);

            // 屋顶：低空斜扫
            count += Mine(t, "Mine_Roof_A", new Vector3(0f, 28f, 125f), new Vector3(1f, 0.35f, 1f), 14f, 4.2f);

            // 井：两层之间斜飞，堵住上升路线
            count += Mine(t, "Mine_Well_A", new Vector3(0f, 44f, 200f), new Vector3(1f, 0.6f, 0f), 26f, 4.5f);
            count += Mine(t, "Mine_Well_B", new Vector3(0f, 54f, 200f), new Vector3(0f, 0.6f, 1f), 26f, 3.8f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 斜飞巡逻雷已布置：" + count + " 颗（斜轴 PingPong，碰到炸 + 弹飞 20 m/s，3 秒重新上膛）");
        }

        private static int Mine(Transform parent, string name, Vector3 start, Vector3 axis, float distance, float speed)
        {
            var mine = new GameObject(name);
            mine.transform.SetParent(parent, false);
            mine.transform.position = start;

            GameObject ball = Sphere(mine.transform, "Ball", Vector3.zero, 1.6f, m_Pop);
            DestroyColliders(ball);

            SphereCollider trigger = mine.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.6f;

            WhiteboxPathPlatform mover = mine.AddComponent<WhiteboxPathPlatform>();
            mover.Configure(axis, distance, speed);

            WengjieProximityBurst burst = mine.AddComponent<WengjieProximityBurst>();
            SerializedObject so = new SerializedObject(burst);
            so.FindProperty("m_Delay").floatValue = 0.05f;
            so.FindProperty("m_LaunchSpeed").floatValue = 20f;
            so.FindProperty("m_LaunchDuration").floatValue = 0.45f;
            so.FindProperty("m_UpBias").floatValue = 0.5f;
            so.FindProperty("m_DestroySelf").boolValue = false;
            so.FindProperty("m_RearmSeconds").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return 1;
        }

        private static void DestroyColliders(GameObject go)
        {
            Collider[] cs = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cs.Length; i++) Object.DestroyImmediate(cs[i]);
        }

        // ==================================================================
        //  尖刺阵「实体化 + 动起来」
        //  原来：BubblePopObstacle 会把碰撞体设成 Trigger → 玩家直接穿过去
        //  现在：留 Trigger（负责打爆泡泡），另加一个实体子碰撞（负责挡住人），
        //        再挂 WhiteboxPathPlatform 让整排横向扫动。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 尖刺实体化 + 扫动", priority = 88)]
        public static void HardenSpikes()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject hazards = GameObject.Find("Wengjie_Cruel");
            if (hazards == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_Cruel，先跑「加狠」。");
                return;
            }

            int solid = 0;
            int moving = 0;

            Transform[] all = hazards.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform spike = all[i];
                if (!spike.name.StartsWith("Spike_", System.StringComparison.Ordinal)) continue;
                if (spike.childCount > 0) continue;      // 已经处理过

                // ① 实体碰撞：子物体上的 BoxCollider（非 Trigger）
                var solidGo = new GameObject("Solid");
                solidGo.transform.SetParent(spike, false);
                solidGo.transform.localScale = Vector3.one;
                BoxCollider box = solidGo.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.size = Vector3.one * 1.1f;
                solid++;

                // ② 扫动：沿垂直于排布方向的轴往返
                float speed = 2.2f + (spike.GetSiblingIndex() % 3) * 0.6f;
                var mover = spike.gameObject.AddComponent<WhiteboxPathPlatform>();
                mover.Configure(Vector3.forward, 4f, speed);
                moving++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 尖刺已实体化：" + solid + " 个实体碰撞， " + moving + " 个开始扫动（往返 4 米）");
        }

        // ==================================================================
        //  屏障实体化：栅栏 / 门洞塞子 → 挡住人，但炸弹能轰开
        //
        //  BubblePopObstacle 会把碰撞体设成 Trigger（只破泡、不挡人）。
        //  这里给它们**再加一个实体子碰撞**，并且挂上刚体 —— 于是：
        //    · 走不过去（必须绕）
        //    · 炸弹的冲量能把它轰开（必须炸）
        //  Trigger 留着，继续负责「碰到就破泡」。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 屏障实体化（绕或炸）", priority = 90)]
        public static void HardenBarriers()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int fences = 0;
            int plugs = 0;

            // ---------- ① 栅栏：M6_* 与 Pair_* ----------
            string[] roots = { "Wengjie_Obstacles", "Wengjie_Cruel" };
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = GameObject.Find(roots[r]);
                if (root == null) continue;

                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Transform tr = all[i];
                    bool isFence = tr.name.StartsWith("M6_", System.StringComparison.Ordinal)
                                || tr.name.StartsWith("Pair_", System.StringComparison.Ordinal);
                    if (!isFence) continue;
                    if (tr.Find("Solid") != null) continue;      // 已处理

                    AddSolidChild(tr, new Vector3(1f, 1f, 1f));

                    // 刚体：让炸弹能把它轰开（质量大一点，免得被泡泡一碰就飞）
                    if (tr.GetComponent<Rigidbody>() == null)
                    {
                        Rigidbody body = tr.gameObject.AddComponent<Rigidbody>();
                        body.mass = 40f;
                        body.drag = 0.6f;
                        body.angularDrag = 1.2f;
                    }

                    fences++;
                }
            }

            // ---------- ② 门洞塞子：M1_* 下面的 Plug ----------
            GameObject obstacles = GameObject.Find("Wengjie_Obstacles");
            if (obstacles != null)
            {
                TerrainBubble[] bubbles = obstacles.GetComponentsInChildren<TerrainBubble>(true);
                for (int i = 0; i < bubbles.Length; i++)
                {
                    if (bubbles[i].name != "Plug") continue;
                    if (bubbles[i].transform.Find("Solid") != null) continue;

                    AddSolidChild(bubbles[i].transform, Vector3.one * 0.8f);
                    plugs++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 屏障实体化：栅栏 " + fences + " 道（可被炸弹轰开）、门洞塞子 " + plugs + " 颗");
        }

        /// <summary>在物体下加一个实体子碰撞（非 Trigger），scale 由父物体决定。</summary>
        private static void AddSolidChild(Transform parent, Vector3 localScale)
        {
            var solid = new GameObject("Solid");
            solid.transform.SetParent(parent, false);
            solid.transform.localScale = localScale;

            BoxCollider box = solid.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size = Vector3.one;
        }

        // ==================================================================
        //  修「站在叶片上一直下坠」
        //
        //  原因：InnerBand 会绕自己中心自转（±14~26°/s），而 CharacterController
        //  遇到「转动的碰撞体」不会被带着走，只会被边缘往下铲 —— 表现就是一直下沉。
        //
        //  修法：叶片只留**视觉**（碰撞体改成 Trigger），
        //  在它下面垫一圈**不转的静态环带**当真正的落脚面。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 修叶片站不住", priority = 91)]
        public static void FixBladeStanding()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject counter = GameObject.Find(kCounterRoot);
            if (counter == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_CounterRing。");
                return;
            }

            Vector3 cA = new Vector3(0f, 12f, 45f);
            int triggers = 0;

            // ① 叶片：碰撞体改成 Trigger（只留视觉）
            Transform[] all = counter.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].name.StartsWith("InnerBand_", System.StringComparison.Ordinal)) continue;

                Collider col = all[i].GetComponent<Collider>();
                if (col != null) col.isTrigger = true;
                triggers++;
            }

            // ② 静态垫层：不转的环形带，玩家真正站的地方
            var deck = new GameObject("InnerDeck_Static");
            deck.transform.SetParent(counter.transform, false);
            deck.transform.position = cA;

            const int sectors = 8;
            const float innerR = 18f;
            const float outerR = 24f;
            float step = 360f / sectors;
            float chord = 2f * outerR * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
            float midR = (innerR + outerR) * 0.5f;

            for (int i = 0; i < sectors; i++)
            {
                float angle = i * step;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 pos = cA + new Vector3(Mathf.Cos(rad), 0.35f, Mathf.Sin(rad)) * midR;

                GameObject s = Box(deck.transform, "Deck_" + i, pos, new Vector3(chord, 0.3f, outerR - innerR), m_Platform);
                s.transform.rotation = Quaternion.Euler(0f, 90f - angle, 0f);

                // 接缝处留一点空隙会让玩家掉下去，所以稍微放宽
                s.transform.localScale = new Vector3(chord * 1.06f, 0.3f, (outerR - innerR) * 1.06f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 叶片已改为纯视觉：" + triggers + " 片碰撞体转 Trigger，另加 8 块静态环带当落脚面（y=12.35）");
        }

        // ==================================================================
        //  出生点兜底 + 全局接住层
        //  不管下落是哪个环节的问题，先保证「有地方站、掉不死」。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 出生点兜底", priority = 92)]
        public static void FixSpawnSafety()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ① 出生点平台：30×30，顶面 y=0，独立于其它结构
            GameObject spawnPad = GameObject.Find("SpawnPad");
            if (spawnPad == null)
            {
                spawnPad = Box(null, "SpawnPad", new Vector3(0f, -0.5f, -28f), new Vector3(30f, 1f, 30f), m_Ground);
                SceneManager.MoveGameObjectToScene(spawnPad, scene);
            }

            // ② 全局接住层：整张地图下面铺一块，掉下去有地方落
            GameObject catchFloor = GameObject.Find("CatchFloor");
            if (catchFloor == null)
            {
                catchFloor = Box(null, "CatchFloor", new Vector3(0f, -6f, 100f), new Vector3(400f, 1f, 500f), m_Ground);
                SceneManager.MoveGameObjectToScene(catchFloor, scene);
            }

            // ③ 出生点 / 玩家 / 重生 三处对齐
            Vector3 spawnPos = new Vector3(0f, 1.2f, -28f);

            GameObject spawn = GameObject.Find("Wengjie_P1_A/Spawn");
            if (spawn != null) spawn.transform.position = spawnPos;

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = spawnPos;
                player.transform.rotation = Quaternion.identity;
            }

            FallRespawn respawn = Object.FindFirstObjectByType<FallRespawn>(FindObjectsInactive.Include);
            if (respawn != null)
            {
                SerializedObject so = new SerializedObject(respawn);
                so.FindProperty("m_KillHeight").floatValue = -4f;
                if (spawn != null) so.FindProperty("m_SpawnPoint").objectReferenceValue = spawn.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 兜底已加：出生平台 30×30（顶面 y=0）、接住层 y=-6、重生高度 -4、出生点 (0,1.2,-28)");
        }

        // ==================================================================
        //  激进开场：出生点在 12 米深的竖井底部，第一步就是往上爬
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 激进开场（竖井出生）", priority = 93)]
        public static void BuildAggressiveStart()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find("Wengjie_StartShaft");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Wengjie_StartShaft");
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 c = new Vector3(0f, 0f, -28f);

            // ---------- 12 米深的竖井：8×8，四面墙 ----------
            Box(t, "Shaft_Floor", c + new Vector3(0f, -0.5f, 0f), new Vector3(10f, 1f, 10f), m_Ground);
            Box(t, "Shaft_W", c + new Vector3(-4.5f, 6f, 0f), new Vector3(1f, 12f, 10f), m_Edge);
            Box(t, "Shaft_E", c + new Vector3(4.5f, 6f, 0f), new Vector3(1f, 12f, 10f), m_Edge);
            Box(t, "Shaft_S", c + new Vector3(0f, 6f, -4.5f), new Vector3(10f, 12f, 1f), m_Edge);
            // 北面留一半，出去就是村台方向
            Box(t, "Shaft_N", c + new Vector3(0f, 9f, 4.5f), new Vector3(10f, 6f, 1f), m_Edge);

            // ---------- 井内 5 层落脚台：每层 2.4 米，只能靠泡泡 ----------
            for (int i = 0; i < 5; i++)
            {
                float y = 1.2f + i * 2.4f;
                float x = (i % 2 == 0) ? -2.2f : 2.2f;
                Box(t, "Shaft_Ledge_" + i, c + new Vector3(x, y, -2f), new Vector3(4f, 0.5f, 3.5f), m_Platform);
            }

            // ---------- 上升气流：白送的垂直通道，也是第一个教学 ----------
            var updraft = new GameObject("Shaft_Updraft");
            updraft.transform.SetParent(t, false);
            updraft.transform.position = c + new Vector3(0f, 6f, 0f);
            BoxCollider trigger = updraft.AddComponent<BoxCollider>();
            trigger.size = new Vector3(6f, 13f, 6f);
            trigger.isTrigger = true;
            updraft.AddComponent<WhiteboxUpdraft>().Configure(3.4f);

            // ---------- 出生点沉到井底 ----------
            GameObject spawn = GameObject.Find("Wengjie_P1_A/Spawn");
            if (spawn != null) spawn.transform.position = c + new Vector3(0f, 0.6f, 0f);

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = c + new Vector3(0f, 1.2f, 0f);
                player.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 激进开场已建：出生点沉到 12 米深竖井底部，5 层落脚台 + 上升气流，第一步必须往上爬");
        }

        // ==================================================================
        //  障碍重铺：**全部放在玩家真正会走的那条线上**
        //  之前按圆环半径撒点，结果大半落在没人走的区域。
        //  这里只按"路径点"放：河岸直道 → 坡道中段 → 桥 → 环内 → 街市 → 屋顶 → 井
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 障碍重铺到主路", priority = 94)]
        public static void ObstaclesOnPath()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            string[] old = { "Wengjie_Obstacles", "Wengjie_Cruel", "Wengjie_Mines", "Wengjie_Obstacles2" };
            for (int i = 0; i < old.Length; i++)
            {
                GameObject o = GameObject.Find(old[i]);
                if (o != null) Object.DestroyImmediate(o);
            }

            var root = new GameObject("Wengjie_Obstacles2");
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 cA = new Vector3(0f, 0f, 45f);
            Vector3 cB = new Vector3(0f, 0f, 110f);

            int n = 0;

            // ---------- ① 河岸直道：x=0，z 从 -26 到 -2 ----------
            n += PathFence(t, "P_Fence_1", new Vector3(0f, 1.6f, -22f), new Vector3(9f, 3.2f, 0.5f), 0f);
            n += Spikes(t, "P_Spike_1", new Vector3(0f, 0.7f, -16f), 5, 1.6f, 90f);

            // ---------- ② 坡道：只放坡道"中心线"上（半径 45）----------
            // 坡道第 3 段（y≈3.5）放尺寸门
            Vector3 ramp3 = ArcPoint(cA, 45f, -90f + 3.5f * 20f, 3.5f);
            n += Gate(t, "P_Gate_Ramp", ramp3, 1.0f);

            // 坡道第 6 段（y≈6.5）放尖刺
            Vector3 ramp6 = ArcPoint(cA, 45f, -90f + 6.5f * 20f, 6.5f);
            n += Spikes(t, "P_Spike_Ramp", ramp6, 4, 1.5f, 20f);

            // 坡道第 9 段（y≈9.5）放填充锁（必须填满才能过）
            Vector3 ramp9 = ArcPoint(cA, 45f, -90f + 9.5f * 20f, 9.5f);
            n += Fill(t, "P_Fill_Ramp", ramp9, 3);

            // ---------- ③ 桥 → 环：z 74~85 的必经段 ----------
            n += PathFence(t, "P_Fence_Bridge", new Vector3(0f, 13.6f, 79.5f), new Vector3(8f, 3.2f, 0.5f), 0f);
            n += Gate(t, "P_Gate_RingIn", new Vector3(0f, 12.9f, 72f), 1.2f);
            n += Fill(t, "P_Fill_RingOut", new Vector3(0f, 12.9f, 83f), 4);

            // ---------- ④ 街市：只放环形街中心线（半径 34）----------
            for (int k = 0; k < 3; k++)
            {
                float ang = 22.5f + k * 90f;
                Vector3 p = ArcPoint(cB, 34f, ang, 12.6f);
                n += PathFence(t, "P_Fence_Street" + k, p + Vector3.up * 1.6f, new Vector3(8f, 3f, 0.5f), ang);
            }
            n += Gate(t, "P_Gate_Street", ArcPoint(cB, 34f, 67f, 12.9f), 0.8f);

            // ---------- ⑤ 屋顶 → 楼梯口 ----------
            n += Spikes(t, "P_Spike_Roof", new Vector3(0f, 26.7f, 120f), 5, 1.6f, 90f);
            n += PathFence(t, "P_Fence_Stair", new Vector3(0f, 27.6f, 146f), new Vector3(10f, 3f, 0.5f), 0f);

            // ---------- ⑥ 井：每层岩台都放一个 ----------
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f;
                float rad = angle * Mathf.Deg2Rad;
                Vector3 ledge = new Vector3(Mathf.Cos(rad) * 24f, 38f + i * 4f + 0.9f, 200f + Mathf.Sin(rad) * 24f);
                n += Fill(t, "P_Fill_Well" + i, ledge, 2);
            }

            // ---------- ⑦ 潮水：井里 ----------
            n += Tide(t, new Vector3(0f, 0f, 200f));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 障碍已重铺到主路：" + n + " 处，全部落在「河岸直道 → 坡道中线 → 桥 → 环 → 街市中线 → 屋顶 → 井台」这条线上");
        }

        private static int Gate(Transform parent, string name, Vector3 pos, float hole)
        {
            GameObject frame = new GameObject(name);
            frame.transform.SetParent(parent, false);
            frame.transform.position = pos;

            float w = hole + 2.2f;
            Box(frame.transform, "L", new Vector3(-w * 0.5f, 0f, 0f), new Vector3(1f, 6f, 1f), m_Edge);
            Box(frame.transform, "R", new Vector3(w * 0.5f, 0f, 0f), new Vector3(1f, 6f, 1f), m_Edge);
            Box(frame.transform, "T", new Vector3(0f, 3f, 0f), new Vector3(w + 1f, 1f, 1f), m_Edge);
            return 1;
        }

        private static int Fill(Transform parent, string name, Vector3 pos, int capacity)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (1.8f + capacity * 0.25f);
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && m_Destructible != null) r.sharedMaterial = m_Destructible;

            TerrainBubble tb = go.AddComponent<TerrainBubble>();
            SerializedObject so = new SerializedObject(tb);
            so.FindProperty("m_Capacity").intValue = capacity;
            so.FindProperty("m_RewardAmount").intValue = 5;
            so.FindProperty("m_RespawnDelay").floatValue = 8f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        private static int PathFence(Transform parent, string name, Vector3 pos, Vector3 size, float yaw)
        {
            GameObject go = Box(parent, name, pos, size, m_Pop);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<BubblePopObstacle>().Configure(false, false);

            var solid = new GameObject("Solid");
            solid.transform.SetParent(go.transform, false);
            BoxCollider box = solid.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size = Vector3.one;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 40f;
            body.drag = 0.6f;
            return 1;
        }

        private static int Spikes(Transform parent, string name, Vector3 center, int count, float spacing, float yaw)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < count; i++)
            {
                float off = (i - (count - 1) * 0.5f) * spacing;
                Vector3 pos = center + rot * new Vector3(off, 0f, 0f);

                GameObject spike = Sphere(parent, name + "_" + i, pos, 1.3f, m_Pop);
                spike.AddComponent<BubblePopObstacle>().Configure(false, true);

                var solid = new GameObject("Solid");
                solid.transform.SetParent(spike.transform, false);
                solid.AddComponent<BoxCollider>().isTrigger = false;
            }

            return count;
        }

        private static int Tide(Transform parent, Vector3 center)
        {
            var tide = new GameObject("P_Tide");
            tide.transform.SetParent(parent, false);
            tide.transform.position = center;

            BubbleTideDirector director = tide.AddComponent<BubbleTideDirector>();
            SerializedObject so = new SerializedObject(director);
            so.FindProperty("m_AutoStart").boolValue = false;
            so.FindProperty("m_BaseSpeed").floatValue = 0.8f;
            so.FindProperty("m_CellSize").floatValue = 0.8f;
            so.FindProperty("m_UseExplicitBounds").boolValue = true;
            so.FindProperty("m_BoundsMin").vector3Value = center + new Vector3(-33f, 34f, -33f);
            so.FindProperty("m_BoundsMax").vector3Value = center + new Vector3(33f, 62f, 33f);
            so.ApplyModifiedPropertiesWithoutUndo();

            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var anchor = new GameObject("P_Anchor_" + i);
                anchor.transform.SetParent(parent, false);
                anchor.transform.position = center + new Vector3(Mathf.Cos(a) * 28f, 34.6f, Mathf.Sin(a) * 28f);
                anchor.AddComponent<BubbleTideAnchor>();
            }

            var gate = new GameObject("P_TideGate");
            gate.transform.SetParent(parent, false);
            gate.transform.position = center + new Vector3(0f, 35f, -32f);
            BoxCollider trig = gate.AddComponent<BoxCollider>();
            trig.size = new Vector3(14f, 4f, 3f);
            trig.isTrigger = true;
            gate.AddComponent<WengjieTideGate>();
            return 1;
        }

        // ==================================================================
        //  Map4 氛围：密集海草 + 遗迹拱门 + 抄 Map4 的光照
        //
        //  【关键】海草是 Terrain Tree 预制体，包围盒里带 billboard（能到 150 米），
        //  所以**绝对不能按包围盒缩放** —— 必须按美术原尺寸（scale 1）摆，
        //  Map4 里就是这么用的。这一条之前踩过坑。
        // ==================================================================

        private const string kMoodRoot = "Wengjie_Mood";

        [MenuItem("Tools/PAO/关卡白盒/wengjie Map4 氛围", priority = 103)]
        public static void Map4Mood()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kMoodRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kMoodRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // 先清掉旧换装里那些稀疏又难看的海草
            GameObject art = GameObject.Find(kArtRoot);
            if (art != null)
            {
                Transform[] artKids = art.GetComponentsInChildren<Transform>(true);
                for (int i = artKids.Length - 1; i >= 0; i--)
                {
                    if (artKids[i].name.StartsWith("Art_Kelp_", System.StringComparison.Ordinal))
                    {
                        Object.DestroyImmediate(artKids[i].gameObject);
                    }
                }
            }

            // ---------- 从 Map4 抓素材 ----------
            bool opened;
            Scene map4 = ArtOpenOrGet(kMap4Path, out opened);

            var kelpOrange = new List<GameObject>();   // Deep Kelp（橙黄）
            var kelpCyan = new List<GameObject>();     // Kelp 1（青蓝）
            var grass = new List<GameObject>();        // 草皮
            var ruins = new List<GameObject>();        // 遗迹拱门

            ArtCollect(map4, "Deep Kelp", kelpOrange, 6);
            ArtCollect(map4, "Kelp 1", kelpCyan, 10);
            ArtCollect(map4, "Green Sea Grass", grass, 4);
            ArtCollect(map4, "SM_L4_yi_ji", ruins, 3);
            ArtCollect(map4, "SM_L5_yi_ji", ruins, 3);

            ArtCopyLighting(map4, scene);
            // 注意：不能在这里关 Map4 —— 下面还要用它里面的预制体来种海草，
            // 提前关会因为对象被销毁而抛 MissingReferenceException。

            int planted = 0;

            // ---------- 河岸两侧：密排 ----------
            for (int i = 0; i < 16; i++)
            {
                float z = -30f + i * 2f;
                planted += Plant(t, kelpCyan, new Vector3(-16f + (i % 3), 0f, z), i);
                planted += Plant(t, kelpOrange, new Vector3(16f - (i % 3), 0f, z), i + 1);
            }

            // ---------- 村台内缘：一圈 ----------
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * 17f, 12.5f, 45f + Mathf.Sin(a) * 17f);
                planted += Plant(t, i % 2 == 0 ? kelpCyan : kelpOrange, p, i);
            }

            // ---------- 街市外圈：两圈交错 ----------
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * 40f, 12.5f, 110f + Mathf.Sin(a) * 40f);
                planted += Plant(t, i % 2 == 0 ? kelpOrange : kelpCyan, p, i + 2);
            }
            for (int i = 0; i < 12; i++)
            {
                float a = (i * 30f + 15f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * 34f, 27f, 110f + Mathf.Sin(a) * 34f);
                planted += Plant(t, grass, p, i);
            }

            // ---------- 井壁：四面挂草 ----------
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * 32f, 40f + (i % 6) * 4f, 200f + Mathf.Sin(a) * 32f);
                planted += Plant(t, i % 2 == 0 ? kelpCyan : kelpOrange, p, i);
            }

            // ---------- 遗迹拱门：6 座，当远景锚点 ----------
            int arches = 0;
            Vector3[] archSpots =
            {
                new Vector3(-20f, 0f, -8f), new Vector3(20f, 0f, -8f),
                new Vector3(-30f, 12f, 60f), new Vector3(30f, 12f, 60f),
                new Vector3(-40f, 12f, 150f), new Vector3(40f, 12f, 150f),
            };
            for (int i = 0; i < archSpots.Length && i < ruins.Count; i++)
            {
                GameObject arch = Object.Instantiate(ruins[i], t);
                arch.name = "Mood_Arch_" + i;
                arch.transform.position = archSpots[i];
                arch.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
                arch.transform.localScale = Vector3.one;
                ArtRemoveColliders(arch);
                arches++;
            }

            if (opened) EditorSceneManager.CloseScene(map4, true);

EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] Map4 氛围已铺：海草/草 " + planted + " 丛、遗迹拱门 " + arches + " 座、光照已换成 Map4 的亮青色");
        }

        /// <summary>按美术原尺寸（scale 1）种一株，只用随机 yaw + 轻微均匀缩放。</summary>
        private static int Plant(Transform parent, List<GameObject> pool, Vector3 pos, int seed)
        {
            if (pool.Count == 0) return 0;

            GameObject prefab = pool[Mathf.Abs(seed) % pool.Count];
            GameObject go = Object.Instantiate(prefab, parent);
            go.name = "Mood_Plant_" + Mathf.Abs(seed) + "_" + Mathf.Abs(seed * 7 % 100);

            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (seed * 47f) % 360f, 0f);
            go.transform.localScale = Vector3.one * (0.85f + (Mathf.Abs(seed) % 5) * 0.06f);

            ArtRemoveColliders(go);
            return 1;
        }

        // ==================================================================
        //  画面降噪 + 补气氛
        //  问题：407 块 7 米地砖随机旋转，铺满整张地图 → 视觉噪音爆表。
        //  做法：删掉 2/3 的地砖（保留 1/3 做纹理），
        //        再补 Thalassophobia 的光柱和水面（最有性价比的气氛资源）。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 画面降噪 + 气氛", priority = 102)]
        public static void CleanNoiseAndMood()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int removed = 0;
            int kept = 0;

            // ---------- ① 删掉 2/3 的地砖 ----------
            GameObject art = GameObject.Find(kArtRoot);
            if (art != null)
            {
                Transform[] all = art.GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (!all[i].name.StartsWith("Art_Tile_", System.StringComparison.Ordinal)) continue;

                    // 用名字的哈希决定去留：稳定的 1/3 保留
                    int hash = Mathf.Abs(all[i].name.GetHashCode()) % 3;
                    if (hash != 0)
                    {
                        Object.DestroyImmediate(all[i].gameObject);
                        removed++;
                    }
                    else kept++;
                }
            }

            // ---------- ② 补光柱：河岸 / 街市 / 井 ----------
            string sunshaftPath = "Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts.prefab";
            GameObject sunshaftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sunshaftPath);

            int beams = 0;
            if (sunshaftPrefab != null)
            {
                beams += Beam(scene, sunshaftPrefab, new Vector3(0f, 18f, -20f), 14f);
                beams += Beam(scene, sunshaftPrefab, new Vector3(0f, 30f, 110f), 16f);
                beams += Beam(scene, sunshaftPrefab, new Vector3(0f, 62f, 200f), 18f);
            }
            else
            {
                Debug.LogWarning("[Wengjie] 找不到 Sunshafts 预制体：" + sunshaftPath);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 降噪完成：删掉地砖 " + removed + " 块，保留 " + kept + " 块；补光柱 " + beams + " 束");
        }

        private static int Beam(Scene scene, GameObject prefab, Vector3 pos, float scale)
        {
            GameObject beam = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            beam.name = "Art_Sunshaft";
            SceneManager.MoveGameObjectToScene(beam, scene);
            beam.transform.position = pos;
            beam.transform.localScale = Vector3.one * scale;
            return 1;
        }

        // ==================================================================
        //  给换装进来的美术补碰撞（复用 AddArtColliders）
        //  跳过：Kelp（植被，会挡路）、Gate（门洞，会封死出口）、Tile（地砖，比地面高）
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 美术补碰撞", priority = 101)]
        public static void AddArtCollisionPass()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject artRoot = GameObject.Find(kArtRoot);
            if (artRoot == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_Art，先跑美术换装。");
                return;
            }

            int added = AddArtColliders();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 美术补碰撞完成：" + added + " 件获得实体碰撞（植被 / 门洞 / 地砖跳过）");
        }

        // ==================================================================
        //  补回「会爆炸的小球」
        //    A. 尺寸门口各一颗：靠近就炸 + 把人弹飞（一次性）
        //    B. 主路上 4 颗巡逻雷：沿斜轴往返，碰到炸 + 弹飞，3 秒重新上膛
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 补回爆炸球", priority = 100)]
        public static void AddExplosiveOrbs()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find("Wengjie_Orbs");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Wengjie_Orbs");
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            int gates = 0;
            int mines = 0;

            // ---------- A. 门口炸弹 ----------
            GameObject obstacles = GameObject.Find("Wengjie_Obstacles2");
            if (obstacles != null)
            {
                Transform[] all = obstacles.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (!all[i].name.StartsWith("P_Gate", System.StringComparison.Ordinal)) continue;

                    Vector3 pos = all[i].position;
                    GameObject orb = Sphere(t, "Orb_" + all[i].name, pos, 1.5f, m_Pop);

                    SphereCollider trig = orb.GetComponent<SphereCollider>();
                    trig.isTrigger = true;
                    trig.radius = 2.4f;

                    WengjieProximityBurst burst = orb.AddComponent<WengjieProximityBurst>();
                    SerializedObject so = new SerializedObject(burst);
                    so.FindProperty("m_Delay").floatValue = 0.16f;
                    so.FindProperty("m_LaunchSpeed").floatValue = 20f;
                    so.FindProperty("m_LaunchDuration").floatValue = 0.45f;
                    so.FindProperty("m_UpBias").floatValue = 0.55f;
                    so.FindProperty("m_DestroySelf").boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    gates++;
                }
            }

            // ---------- B. 巡逻雷（斜飞往返）----------
            mines += OrbMine(t, "Orb_Mine_Street", new Vector3(0f, 15f, 130f), new Vector3(1f, 0.45f, -1f), 20f, 3.4f);
            mines += OrbMine(t, "Orb_Mine_Roof", new Vector3(0f, 28f, 120f), new Vector3(1f, 0.4f, 1f), 16f, 4.0f);
            mines += OrbMine(t, "Orb_Mine_Well_A", new Vector3(0f, 44f, 200f), new Vector3(1f, 0.6f, 0f), 26f, 4.2f);
            mines += OrbMine(t, "Orb_Mine_Well_B", new Vector3(0f, 54f, 200f), new Vector3(0f, 0.6f, 1f), 26f, 3.6f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 爆炸球已补回：门口 " + gates + " 颗（一次性）+ 巡逻雷 " + mines + " 颗（斜飞往返，3 秒重上膛）");
        }

        private static int OrbMine(Transform parent, string name, Vector3 start, Vector3 axis, float distance, float speed)
        {
            var mine = new GameObject(name);
            mine.transform.SetParent(parent, false);
            mine.transform.position = start;

            GameObject ball = Sphere(mine.transform, "Ball", Vector3.zero, 1.6f, m_Pop);
            Collider[] ballCols = ball.GetComponents<Collider>();
            for (int i = 0; i < ballCols.Length; i++) Object.DestroyImmediate(ballCols[i]);

            SphereCollider trig = mine.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 1.7f;

            mine.AddComponent<WhiteboxPathPlatform>().Configure(axis, distance, speed);

            WengjieProximityBurst burst = mine.AddComponent<WengjieProximityBurst>();
            SerializedObject so = new SerializedObject(burst);
            so.FindProperty("m_Delay").floatValue = 0.05f;
            so.FindProperty("m_LaunchSpeed").floatValue = 20f;
            so.FindProperty("m_LaunchDuration").floatValue = 0.45f;
            so.FindProperty("m_UpBias").floatValue = 0.5f;
            so.FindProperty("m_DestroySelf").boolValue = false;
            so.FindProperty("m_RearmSeconds").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return 1;
        }

        // ==================================================================
        //  删掉河岸那组没有意义的「栅栏 + 尖刺」
        //  它不挡路（40 米宽的河岸只挡 9 米）、不教东西、也不制造选择。
        //  开头 40 秒改成纯走路，第一个障碍留到坡道的尺寸门。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 删掉河岸栅栏", priority = 99)]
        public static void DeleteRiverbankGate()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject root = GameObject.Find("Wengjie_Obstacles2");
            if (root == null)
            {
                Debug.LogWarning("[Wengjie] 找不到 Wengjie_Obstacles2。");
                return;
            }

            int removed = 0;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                string n = all[i].name;
                if (n.StartsWith("P_Fence_1", System.StringComparison.Ordinal) ||
                    n.StartsWith("P_Spike_1_", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(all[i].gameObject);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 河岸栅栏 + 尖刺已删除：" + removed + " 个物体。开头 40 秒现在是纯走路。");
        }

        // ==================================================================
        //  把「一直转」改成「来回循环」
        //  RotatingHazard 的 pingPong 模式：在 ±swingAngle 之间往返，
        //  最终回到原点 —— 玩家可以靠记相位通过，而不是永远面对新地形。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 运动改成循环", priority = 98)]
        public static void MakeMotionCyclic()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int cyclic = 0;

            // ① 大轮转的 pivot：街市 / 屋顶 / 村台
            string[] pivots = { "Wengjie_StreetRotate/StreetPivot", "Wengjie_StreetRotate/RoofPivot", "Wengjie_Rotate/RingPivot" };
            for (int i = 0; i < pivots.Length; i++)
            {
                GameObject pivot = GameObject.Find(pivots[i]);
                if (pivot == null) continue;

                RotatingHazard spin = pivot.GetComponent<RotatingHazard>();
                if (spin == null) continue;

                // 幅度 12°，周期按速度决定；正负交替，三层不同相
                float speed = (i == 0) ? 7f : (i == 1 ? -6f : 4f);
                spin.Configure(Vector3.up, speed, true, 12f);
                cyclic++;
            }

            // ② 小轮转的每一块板：幅度 10°，周期错开
            string[] plateRoots = { "Wengjie_StreetRotate", "Wengjie_CounterRing" };
            for (int r = 0; r < plateRoots.Length; r++)
            {
                GameObject root = GameObject.Find(plateRoots[r]);
                if (root == null) continue;

                RotatingHazard[] spins = root.GetComponentsInChildren<RotatingHazard>(true);
                for (int i = 0; i < spins.Length; i++)
                {
                    if (spins[i].transform.parent != null &&
                        (spins[i].transform.parent.name == "StreetPivot" || spins[i].transform.parent.name == "RoofPivot"))
                    {
                        continue;   // 大轮转已经处理过
                    }

                    float baseSpeed = 8f + (i % 4) * 2f;
                    if (i % 2 == 1) baseSpeed = -baseSpeed;
                    spins[i].Configure(Vector3.up, baseSpeed, true, 10f);
                    cyclic++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 运动已改成循环：" + cyclic + " 个组件切换为 pingPong（大轮转 ±12°，小轮转 ±10°），全部会回到原点");
        }

        // ==================================================================
        //  街市「大轮转 + 小轮转」
        //    大：整圈街市板挂到中心 pivot，+1.5°/s；屋顶挂另一个 pivot，−1.8°/s
        //    小：每块板自己绕中心转，±6°/s，正反交替
        //  说明：绕 Y 轴转的是**平**板，顶面高度不变，不会像叶片那样把人往下铲。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 街市大轮转+小轮转", priority = 97)]
        public static void RotateStreet()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject stageB = GameObject.Find("Wengjie_P1_B");
            if (stageB == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_P1_B。");
                return;
            }

            GameObject old = GameObject.Find("Wengjie_StreetRotate");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("Wengjie_StreetRotate");
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 streetC = new Vector3(0f, 12f, 110f);
            Vector3 roofC = new Vector3(0f, 26f, 110f);

            var streetPivot = new GameObject("StreetPivot");
            streetPivot.transform.SetParent(t, false);
            streetPivot.transform.position = streetC;

            var roofPivot = new GameObject("RoofPivot");
            roofPivot.transform.SetParent(t, false);
            roofPivot.transform.position = roofC;

            int street = 0;
            int roof = 0;

            // 先把上一版的摆动组件摘掉，避免和旋转打架
            Transform[] all = stageB.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                WhiteboxSwingMotion swing = all[i].GetComponent<WhiteboxSwingMotion>();
                if (swing != null) Object.DestroyImmediate(swing);
            }

            for (int i = 0; i < all.Length; i++)
            {
                Transform tr = all[i];
                bool isStreet = tr.name.StartsWith("B_Street_", System.StringComparison.Ordinal);
                bool isRoof = tr.name.StartsWith("B_Roof_", System.StringComparison.Ordinal);
                if (!isStreet && !isRoof) continue;

                // 大轮转：挂到对应 pivot 下
                tr.SetParent(isStreet ? streetPivot.transform : roofPivot.transform, true);

                // 小轮转：每块自己绕中心转，正反交替
                float spin = (isStreet ? 6f : -6f) * ((tr.GetSiblingIndex() % 2 == 0) ? 1f : -1f);
                tr.gameObject.AddComponent<RotatingHazard>().Configure(Vector3.up, spin);

                if (isStreet) street++;
                else roof++;
            }

            // 大轮转速度：慢到不会让人晕，但能看出来在转
            streetPivot.AddComponent<RotatingHazard>().Configure(Vector3.up, 1.5f);
            roofPivot.AddComponent<RotatingHazard>().Configure(Vector3.up, -1.8f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 街市转动已启用：街市 " + street + " 块（大 +1.5°/s，小 ±6°/s）+ 屋顶 " + roof + " 块（大 −1.8°/s，小 ∓6°/s）");
        }

        // ==================================================================
        //  街市板「浮板化」：B_Street_* / B_Roof_* 各自摆动
        //  用 WhiteboxSwingMotion（它会载人，不会像旋转碰撞体那样把人铲下去）
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 街市板运动", priority = 96)]
        public static void MoveStreetPlates()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject stageB = GameObject.Find("Wengjie_P1_B");
            if (stageB == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_P1_B。");
                return;
            }

            int street = 0;
            int roof = 0;

            Transform[] all = stageB.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform tr = all[i];
                bool isStreet = tr.name.StartsWith("B_Street_", System.StringComparison.Ordinal);
                bool isRoof = tr.name.StartsWith("B_Roof_", System.StringComparison.Ordinal);
                if (!isStreet && !isRoof) continue;
                if (tr.GetComponent<WhiteboxSwingMotion>() != null) continue;

                int index = isStreet ? street : roof;
                Collider self = tr.GetComponent<Collider>();

                // 幅度小、周期错开：板与板之间永远不同步，走上去像踩浮木
                float angle = isStreet ? 5f : 7f;
                float period = 2.6f + index * 0.17f;

                WhiteboxSwingMotion swing = tr.gameObject.AddComponent<WhiteboxSwingMotion>();
                swing.Configure(Vector3.forward, angle, period, self, Vector3.zero);

                if (isStreet) street++;
                else roof++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 街市板已动起来：" + street + " 块街面板（±5°）+ " + roof + " 块屋顶板（±7°），周期逐个错开");
        }

        // ==================================================================
        //  把主路上的障碍全部补上实体碰撞
        //  症状：玩家能从尖刺/填充泡中间穿过去。
        //  原因：BubblePopObstacle 把碰撞体设成 Trigger；Solid 子碰撞又只有 1 米，
        //        而球体是 1.3~3 米 —— 边缘全是空的。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie 主路障碍补碰撞", priority = 95)]
        public static void HardenPathObstacles()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject pathRoot = GameObject.Find("Wengjie_Obstacles2");
            if (pathRoot == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_Obstacles2。");
                return;
            }

            int spikes = 0;
            int fills = 0;

            Transform[] all = pathRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform tr = all[i];

                if (tr.name.StartsWith("P_Spike", System.StringComparison.Ordinal))
                {
                    Transform solid = tr.Find("Solid");
                    if (solid == null)
                    {
                        var go = new GameObject("Solid");
                        go.transform.SetParent(tr, false);
                        solid = go.transform;
                    }

                    SphereCollider sphere = solid.GetComponent<SphereCollider>();
                    if (sphere == null) sphere = solid.gameObject.AddComponent<SphereCollider>();
                    sphere.isTrigger = false;
                    sphere.radius = 0.52f;
                    spikes++;
                    continue;
                }

                if (tr.name.StartsWith("P_Fill", System.StringComparison.Ordinal))
                {
                    SphereCollider own = tr.GetComponent<SphereCollider>();
                    if (own != null)
                    {
                        own.isTrigger = false;
                        fills++;
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 主路障碍补碰撞：尖刺 " + spikes + " 个（球 r=0.52）、填充锁 " + fills + " 个");
        }

        // ==================================================================
        //  P1 空壳：大尺度五段主路（无机关 / 无美术）
        //  只为验证「空间够大、15 分钟走得完」。
        // ==================================================================

        [MenuItem("Tools/PAO/关卡白盒/wengjie P1 空壳主路（旧版方块）", priority = 89)]
        public static void BuildShellP1()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            string[] oldRoots = { kRootName, kV2Root, "Wengjie_South", "Wengjie_Hazards", "Wengjie_Traps", "Wengjie_Plan", "Wengjie_Art", kGuideRoot };
            for (int i = 0; i < oldRoots.Length; i++)
            {
                GameObject old = GameObject.Find(oldRoots[i]);
                if (old != null) Object.DestroyImmediate(old);
            }

            var root = new GameObject(kP1Root);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ---------- A 浅滩 160×80，y=0 ----------
            Box(t, "Hub_Floor_A", new Vector3(0f, -0.5f, -170f), new Vector3(160f, 1f, 80f), m_Ground);
            Box(t, "A_Wall_A_N", new Vector3(0f, 12f, -130f), new Vector3(160f, 24f, 2f), m_Edge);
            Box(t, "A_Wall_A_W", new Vector3(-80f, 12f, -170f), new Vector3(2f, 24f, 80f), m_Edge);
            Box(t, "A_Wall_A_E", new Vector3(80f, 12f, -170f), new Vector3(2f, 24f, 80f), m_Edge);
            Box(t, "A_Wall_A_S", new Vector3(0f, 12f, -210f), new Vector3(160f, 24f, 2f), m_Edge);

            // ---------- A→B 坡道（6 米，12 级）----------
            for (int i = 0; i < 12; i++)
            {
                Box(t, "Step_AB_" + i, new Vector3(0f, 0.25f + i * 0.5f, -129f + i * 1.2f), new Vector3(20f, 0.5f, 1.4f), m_Platform);
            }

            // ---------- B 沉没街市 200×120，y=6 ----------
            Box(t, "Hub_Floor_B", new Vector3(0f, 5.5f, -50f), new Vector3(200f, 1f, 120f), m_Ground);
            Box(t, "A_Wall_B_W", new Vector3(-100f, 20f, -50f), new Vector3(2f, 28f, 120f), m_Edge);
            Box(t, "A_Wall_B_E", new Vector3(100f, 20f, -50f), new Vector3(2f, 28f, 120f), m_Edge);
            Box(t, "A_Wall_B_N_L", new Vector3(-55f, 20f, 10f), new Vector3(90f, 28f, 2f), m_Edge);
            Box(t, "A_Wall_B_N_R", new Vector3(55f, 20f, 10f), new Vector3(90f, 28f, 2f), m_Edge);
            // 街市体块：左右各 6 个 20×14 的店铺，中间留 8 米主街
            for (int i = 0; i < 6; i++)
            {
                float z = -100f + i * 20f;
                Box(t, "B_Shop_L_" + i, new Vector3(-30f, 12f, z), new Vector3(40f, 12f, 14f), m_Landmark);
                Box(t, "B_Shop_R_" + i, new Vector3(30f, 12f, z), new Vector3(40f, 12f, 14f), m_Landmark);
            }

            // ---------- B→C 坡道（14 米，20 级）----------
            for (int i = 0; i < 20; i++)
            {
                Box(t, "Step_BC_" + i, new Vector3(0f, 6.25f + i * 0.7f, 11f + i * 1.5f), new Vector3(24f, 0.7f, 1.7f), m_Platform);
            }

            // ---------- C 断柱廊 140×60，y=20 ----------
            Box(t, "Hub_Floor_C", new Vector3(0f, 19.5f, 90f), new Vector3(140f, 1f, 60f), m_Ground);
            Box(t, "A_Wall_C_W", new Vector3(-70f, 36f, 90f), new Vector3(2f, 32f, 60f), m_Edge);
            Box(t, "A_Wall_C_E", new Vector3(70f, 36f, 90f), new Vector3(2f, 32f, 60f), m_Edge);
            Box(t, "A_Pit_C", new Vector3(0f, 12f, 90f), new Vector3(60f, 16f, 12f), m_Void);
            for (int i = 0; i < 4; i++)
            {
                Box(t, "A_Column_P1_" + i, new Vector3(-32f, 24f, 78f + i * 8f), new Vector3(1.2f, 8f, 1.2f), m_Bounce);
            }

            // ---------- D 峡谷竖井 80×80，y=20→70 ----------
            Box(t, "Hub_Floor_D", new Vector3(0f, 19.5f, 140f), new Vector3(80f, 1f, 80f), m_Ground);
            Box(t, "D_Wall_W", new Vector3(-40f, 45f, 140f), new Vector3(2f, 52f, 80f), m_Edge);
            Box(t, "D_Wall_E", new Vector3(40f, 45f, 140f), new Vector3(2f, 52f, 80f), m_Edge);
            Box(t, "D_Wall_N", new Vector3(0f, 45f, 180f), new Vector3(80f, 52f, 2f), m_Edge);
            // 井内螺旋台：每 5 米一层，交替四角
            for (int i = 0; i < 10; i++)
            {
                float y = 25f + i * 5f;
                float x = (i % 2 == 0) ? 24f : -24f;
                float z = (i % 4 < 2) ? 160f : 120f;
                Box(t, "D_Ledge_" + i, new Vector3(x, y, z), new Vector3(18f, 1f, 18f), m_Platform);
            }

            // ---------- E 塔顶 60×60，y=70 ----------
            Box(t, "Hub_Floor_E", new Vector3(0f, 69.5f, 140f), new Vector3(60f, 1f, 60f), m_Landmark);
            Box(t, "Altar_Core", new Vector3(0f, 70.6f, 140f), new Vector3(4f, 1.2f, 4f), m_Bounce);

            GameObject goal = new GameObject("B_Goal");
            goal.transform.SetParent(t, false);
            goal.transform.position = new Vector3(0f, 71.5f, 140f);
            BubbleTideGoal tideGoal = goal.AddComponent<BubbleTideGoal>();
            SerializedObject goalSo = new SerializedObject(tideGoal);
            goalSo.FindProperty("m_GraceSeconds").floatValue = 8f;
            goalSo.FindProperty("m_ReachDistance").floatValue = 3f;
            goalSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 系统 ----------
            GameObject spawn = new GameObject("Spawn");
            spawn.transform.SetParent(t, false);
            spawn.transform.position = new Vector3(0f, 0.6f, -200f);

            GameObject respawn = new GameObject("FallRespawn");
            respawn.transform.SetParent(t, false);
            respawn.AddComponent<FallRespawn>().Configure(-12f, spawn.transform);

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = new Vector3(0f, 1.2f, -200f);
                player.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie P1] 空壳主路已搭好：\n" +
                      "  A 浅滩 160×80 (y=0) → 坡道 6m → B 街市 200×120 (y=6)\n" +
                      "  → 坡道 14m → C 柱廊 140×60 (y=20) → D 竖井 80×80 (y=20→70)\n" +
                      "  → E 塔顶 60×60 (y=70)\n" +
                      "  总平面 260×400m，垂直 70m。请掐表走一遍。");
        }

        // ==================================================================
        //  重建：垂直推进版
        //
        //  结构 = 中央竖井 + 三层横向关卡，每层比下一层高 8 米。
        //  玩家必须**绕井爬升**才能进下一层，所以空间是螺旋上升的：
        //
        //      祭坛 (y=24)
        //        ▲  井顶
        //   北峡谷 (y=16) ── 潮水 / 三级台阶
        //        ▲  井
        //   西柱廊 (y=8)  ── 倒柱 / 窄沟
        //        ▲  井
        //   南庭院 (y=0)  ── 起点 / 认识泡泡
        //
        //  每次回到井里都要再花泡泡往上爬 —— 泡泡值就是「上楼费」。
        // ==================================================================

        private const string kV2Root = "Wengjie_V2";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 重建（垂直推进）", priority = 78)]
        public static void BuildVerticalLevel()
        {
            LoadMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            string[] oldRoots = { kRootName, "Wengjie_South", "Wengjie_Hazards", "Wengjie_Traps", "Wengjie_Plan", "Wengjie_Art", kGuideRoot };
            for (int i = 0; i < oldRoots.Length; i++)
            {
                GameObject old = GameObject.Find(oldRoots[i]);
                if (old != null) Object.DestroyImmediate(old);
            }

            var root = new GameObject(kV2Root);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ---------------- 中央竖井 ----------------
            Box(t, "Hub_WellFloor", new Vector3(0f, -1.2f, 0f), new Vector3(18f, 0.6f, 18f), m_Void);
            Box(t, "Hub_WellWall_N", new Vector3(0f, 13f, 8f), new Vector3(16f, 30f, 1f), m_Edge);
            Box(t, "Hub_WellWall_S", new Vector3(0f, 13f, -8f), new Vector3(16f, 30f, 1f), m_Edge);
            Box(t, "Hub_WellWall_E", new Vector3(8f, 13f, 0f), new Vector3(1f, 30f, 16f), m_Edge);
            Box(t, "Hub_WellWall_W", new Vector3(-8f, 13f, 0f), new Vector3(1f, 30f, 16f), m_Edge);

            // 井壁螺旋落脚台：每 3 米一层，左右交替。3 米 > 跳跃 1.2 米，所以必须用泡泡。
            for (int i = 0; i < 8; i++)
            {
                float y = 3f + i * 3f;
                float side = (i % 2 == 0) ? 5.2f : -5.2f;
                float depth = (i % 4 < 2) ? 5.2f : -5.2f;
                Box(t, "Hub_Ledge_" + i, new Vector3(side, y, depth), new Vector3(4.4f, 0.5f, 4.4f), m_Platform);
            }

            // ---------------- L0 南庭院（起点）----------------
            Box(t, "Hub_Floor_L0", new Vector3(0f, -0.5f, -24f), new Vector3(32f, 1f, 32f), m_Ground);
            Box(t, "A_Wall_L0_S", new Vector3(0f, 5f, -40f), new Vector3(32f, 10f, 1f), m_Edge);
            Box(t, "A_Wall_L0_W", new Vector3(-16f, 5f, -24f), new Vector3(1f, 10f, 32f), m_Edge);
            Box(t, "A_Wall_L0_E", new Vector3(16f, 5f, -24f), new Vector3(1f, 10f, 32f), m_Edge);
            Box(t, "A_Wall_L0_N_L", new Vector3(-9f, 5f, -8f), new Vector3(14f, 10f, 1f), m_Edge);
            Box(t, "A_Wall_L0_N_R", new Vector3(9f, 5f, -8f), new Vector3(14f, 10f, 1f), m_Edge);

            Terrain(t, "L0_Teach_Bubble", new Vector3(-6f, 1.4f, -22f), 2.6f, 3, 5, 8f);
            MakeSticky(t, "L0_Sticky", new Vector3(-15.4f, 2.5f, -18f), new Vector3(0.6f, 4f, 5f));

            // ---------------- L1 西柱廊（y=8）----------------
            Box(t, "Hub_Floor_L1", new Vector3(-30f, 7.5f, 0f), new Vector3(44f, 1f, 24f), m_Ground);
            Box(t, "A_Wall_L1_N", new Vector3(-30f, 13f, 12f), new Vector3(44f, 10f, 1f), m_Edge);
            Box(t, "A_Wall_L1_S", new Vector3(-30f, 13f, -12f), new Vector3(44f, 10f, 1f), m_Edge);
            Box(t, "A_Wall_L1_W", new Vector3(-52f, 13f, 0f), new Vector3(1f, 10f, 24f), m_Edge);
            Box(t, "A_Pit_L1", new Vector3(-33f, 3.5f, 0f), new Vector3(6f, 9f, 24f), m_Void);

            float[] colZ = { -8f, -3f, 3f, 8f };
            for (int i = 0; i < colZ.Length; i++)
            {
                Vector3 basePos = new Vector3(-29f, 8f, colZ[i]);
                float rad = 10f * Mathf.Deg2Rad;
                Vector3 up = new Vector3(-Mathf.Sin(rad), Mathf.Cos(rad), 0f);
                Vector3 center = basePos + up * 4f;

                GameObject column = Box(t, "A_Column_L1_" + i, center, new Vector3(1.1f, 8f, 1.1f), m_Bounce);
                column.transform.rotation = Quaternion.Euler(0f, 0f, 10f);

                Rigidbody body = column.AddComponent<Rigidbody>();
                body.mass = 1.6f;
                body.drag = 0.15f;
                body.angularDrag = 0.4f;
                body.useGravity = false;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                TerrainBubble chock = Terrain(t, "A_Column_L1_" + i + "_Chock",
                    basePos + new Vector3(-0.9f, 0.9f, 0f), 1.9f, 3, 5, 0f);

                WengjieColumnRelease release = column.AddComponent<WengjieColumnRelease>();
                SerializedObject so = new SerializedObject(release);
                so.FindProperty("m_Chock").objectReferenceValue = chock;
                so.FindProperty("m_Column").objectReferenceValue = body;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ---------------- L2 北峡谷（y=16）----------------
            Box(t, "Hub_Floor_L2", new Vector3(0f, 15.5f, 26f), new Vector3(24f, 1f, 36f), m_Ground);
            for (int i = 1; i <= 3; i++)
            {
                float y = 16f + i * 3f;
                Box(t, "B_Terrace_" + i, new Vector3(0f, y - 0.5f, 20f + i * 10f), new Vector3(24f, 1f, 10f), m_Ground);
                Box(t, "B_Riser_" + i, new Vector3(0f, y - 1.5f, 18f + i * 10f), new Vector3(24f, 3f, 1f), m_Edge);
            }
            Box(t, "B_Cliff_W", new Vector3(-12.5f, 22f, 30f), new Vector3(1f, 16f, 36f), m_Edge);
            Box(t, "B_Cliff_E", new Vector3(12.5f, 22f, 30f), new Vector3(1f, 16f, 36f), m_Edge);
            Box(t, "B_BackWall", new Vector3(0f, 26f, 46f), new Vector3(26f, 24f, 1f), m_Edge);

            // ---------------- L3 祭坛（y=24）----------------
            Box(t, "Hub_Floor_L3", new Vector3(0f, 23.5f, 6f), new Vector3(12f, 1f, 12f), m_Landmark);
            Box(t, "Altar_Core", new Vector3(0f, 24.6f, 6f), new Vector3(2.6f, 1.2f, 2.6f), m_Bounce);

            GameObject goal = new GameObject("B_Goal");
            goal.transform.SetParent(t, false);
            goal.transform.position = new Vector3(0f, 25.2f, 6f);
            BubbleTideGoal tideGoal = goal.AddComponent<BubbleTideGoal>();
            SerializedObject goalSo = new SerializedObject(tideGoal);
            goalSo.FindProperty("m_GraceSeconds").floatValue = 8f;
            goalSo.FindProperty("m_ReachDistance").floatValue = 2.5f;
            goalSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------------- 系统 ----------------
            GameObject spawn = new GameObject("Spawn");
            spawn.transform.SetParent(t, false);
            spawn.transform.position = new Vector3(0f, 0.6f, -34f);

            GameObject respawn = new GameObject("FallRespawn");
            respawn.transform.SetParent(t, false);
            respawn.AddComponent<FallRespawn>().Configure(-10f, spawn.transform);

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = new Vector3(0f, 1.2f, -34f);
                player.transform.rotation = Quaternion.identity;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;
            Debug.Log("[Wengjie V2] 垂直推进关卡已重建：L0 南庭院 → 井 → L1 西柱廊 → 井 → L2 北峡谷 → 井 → L3 祭坛");
        }

        // ==================================================================
        //  主线规划：把散开的区块收成「起点 → 终点」的一条路
        //  引导**不靠路牌**，靠三样东西：
        //    ① 光：视线永远被一束暖光牵着走
        //    ② 收窄：主路宽、岔路窄，人会本能走宽的那条
        //    ③ 高低差：主路持续微微上升，脚下在替你决定方向
        // ==================================================================

        private const string kPlanRoot = "Wengjie_Plan";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 主线规划（隐形引导）", priority = 77)]
        public static void PlanRoute()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ---------- ① 撤掉明示引导（箭头/彩带/光柱）----------
            GameObject oldRoute = GameObject.Find(kGuideRoot);
            if (oldRoute != null) Object.DestroyImmediate(oldRoute);

            GameObject previous = GameObject.Find(kPlanRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            var root = new GameObject(kPlanRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            // ---------- ② 起点放到南区庭院最深处 ----------
            // 出生点 = 路的开端，玩家第一眼看到的是走廊尽头的一束光。
            Vector3 spawnPos = new Vector3(0f, 0.6f, -56f);
            GameObject spawnObject = GameObject.Find("Spawn");
            if (spawnObject != null) spawnObject.transform.position = spawnPos;

            GameObject player = GameObject.Find("takoyaki");
            if (player != null)
            {
                player.transform.position = spawnPos + Vector3.up * 0.6f;
                player.transform.rotation = Quaternion.identity;   // 面朝 +Z，正对走廊
            }

            // ---------- ③ 收窄：三个转角的「喉口」----------
            // 主路 10 米宽，到了转角收成 4~6 米 —— 身体会自己跟着宽窄走。
            // 【尺寸必须让出缺口】左右两段的 x 区间不能相交，否则会把路封死（上一版就犯了这个错）。
            Box(t, "Narrow_CourtExit", new Vector3(-12.5f, 2.5f, -27.5f), new Vector3(15f, 5f, 1f), m_Edge);
            Box(t, "Narrow_CourtExit_R", new Vector3(12.5f, 2.5f, -27.5f), new Vector3(15f, 5f, 1f), m_Edge);
            Box(t, "Narrow_HubSouth", new Vector3(-8f, 2.5f, -16f), new Vector3(10f, 5f, 1f), m_Edge);
            Box(t, "Narrow_HubSouth_R", new Vector3(8f, 2.5f, -16f), new Vector3(10f, 5f, 1f), m_Edge);
            Box(t, "Narrow_HubNorth", new Vector3(-8f, 2.5f, 16f), new Vector3(10f, 5f, 1f), m_Edge);
            Box(t, "Narrow_HubNorth_R", new Vector3(8f, 2.5f, 16f), new Vector3(10f, 5f, 1f), m_Edge);

            // ---------- ④ 光：沿路一束束暖光 ----------
            // 只照亮「该走的那条」，暗处自然被当成死路。
            int lights = 0;
            lights += WarmLight(t, "L_Court", new Vector3(0f, 6f, -50f), 26f, 2.4f);
            lights += WarmLight(t, "L_CourtExit", new Vector3(0f, 5f, -28f), 16f, 3.2f);
            lights += WarmLight(t, "L_HubSouth", new Vector3(0f, 6f, -14f), 18f, 2.6f);
            lights += WarmLight(t, "L_HubCentre", new Vector3(0f, 8f, 0f), 24f, 2.0f);
            lights += WarmLight(t, "L_HubWest", new Vector3(-14f, 5f, 0f), 14f, 3.4f);
            lights += WarmLight(t, "L_HubNorth", new Vector3(0f, 5f, 14f), 14f, 3.4f);
            lights += WarmLight(t, "L_CanyonFoot", new Vector3(0f, 6f, 26f), 22f, 2.2f);

            // ---------- ⑤ 一处「看得见到不了」的承诺 ----------
            // 在广场南口就能看见峡谷顶上的祭坛，整条路的终点从第一步就成立。
            GameObject altarBeam = Box(t, "Promise_Beam", new Vector3(0f, 14f, 24f), new Vector3(0.6f, 16f, 0.6f), m_Guide, false);
            Renderer beamRenderer = altarBeam.GetComponent<Renderer>();
            if (beamRenderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(1f, 0.86f, 0.55f));
                block.SetColor("_Color", new Color(1f, 0.86f, 0.55f));
                beamRenderer.SetPropertyBlock(block);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 主线规划完成：起点移到南区 → 走廊 → 广场 → 断柱廊 → 折返 → 峡谷 → 祭坛\n" +
                      "  收窄喉口 7 处、暖光 " + lights + " 处、明示引导已撤除");
        }

        private static int WarmLight(Transform parent, string name, Vector3 pos, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.87f, 0.68f);   // 暖白，和场景的冷蓝形成牵引
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;

            return 1;
        }

        // ==================================================================
        //  美术换装：从 Map4 / Map5 抓真实资源，铺到白盒体块表面
        // ==================================================================

        private const string kArtRoot = "Wengjie_Art";
        private const string kMap5Path = "Assets/GameMain/Scenes/Map5.unity";
        private const string kMap4Path = "Assets/GameMain/Scenes/Map4.unity";
        private const string kHudPath = "Assets/GameMain/UI/Level HUD.prefab";

        private static readonly string[] kWallPrefixes = { "TFD_Wall_01A", "TFD_Wall_04A", "TFD_Wall_Block" };
        private static readonly string[] kCliffPrefixes = { "TFD_Canyon_Cliff_01A", "TFD_Rock_Large_08A", "TFD_Rock_Large_15A" };
        private static readonly string[] kPavePrefixes = { "TFD_Pavement_01A" };

        // 植被：Map4 里的普通网格灌木 + 草皮（不要用 Terrain Tree 那套，包围盒带 billboard）
        private static readonly string[] kPlantPrefixes = { "TFD_Bush_01A", "TFD_Grass_Patch_01A" };

        // 珊瑚：Map4 里是 `..._Coral_Colony_*` 的球簇，用「包含」匹配
        private const string kCoralToken = "Coral_Colony";

        // 门洞：Map4 用了 30 段，做广场三个出口最合适
        private static readonly string[] kGatePrefixes = { "TFD_Gate_01A_Module" };

        [MenuItem("Tools/PAO/关卡白盒/wengjie 美术换装", priority = 72)]
        public static void DressArt()
        {
            Scene wengjie = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(wengjie);

            GameObject previous = GameObject.Find(kArtRoot);
            if (previous != null) Object.DestroyImmediate(previous);

            // 水体雾 / 后期体积是挂在场景根上的，不在 Wengjie_Art 里 ——
            // 不清掉的话每跑一次就会多叠一套（旧的还带着碰撞体）
            Scene active = SceneManager.GetActiveScene();
            GameObject[] rootObjects = active.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                if (rootObjects[i] != null && rootObjects[i].name.StartsWith("Art_", System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(rootObjects[i]);
                }
            }

            var artRoot = new GameObject(kArtRoot);
            SceneManager.MoveGameObjectToScene(artRoot, wengjie);
            Transform art = artRoot.transform;

            var walls = new List<GameObject>();
            var cliffs = new List<GameObject>();
            var paves = new List<GameObject>();
            var kelps = new List<GameObject>();
            var gates = new List<GameObject>();

            // ---- Map5：石构件 / 崖壁 / 铺地 / 后期体积 ----
            bool openedMap5;
            Scene map5 = ArtOpenOrGet(kMap5Path, out openedMap5);
            foreach (string p in kWallPrefixes) ArtCollect(map5, p, walls, 4);
            foreach (string p in kCliffPrefixes) ArtCollect(map5, p, cliffs, 4);
            foreach (string p in kPavePrefixes) ArtCollect(map5, p, paves, 4);
            GameObject volumeSource = ArtFindVolume(map5);

            // 光照 / 雾 / 天空盒：直接抄 Map5 的水下设置
            // （这个 Unity 版本没有 EditorSceneManager.CopySceneSettings，所以手抄）
            ArtCopyLighting(map5, wengjie);

            // ---- Map4：植被 / 珊瑚 / 门洞 ----
            bool openedMap4;
            Scene map4 = ArtOpenOrGet(kMap4Path, out openedMap4);
            foreach (string p in kPlantPrefixes) ArtCollect(map4, p, kelps, 4);
            ArtCollectContaining(map4, kCoralToken, kelps, 4);
            foreach (string p in kGatePrefixes) ArtCollect(map4, p, gates, 3);

            Debug.Log(string.Format(
                "[WengjieArt] 采集：墙{0} 崖{1} 铺{2} ｜ 植被{3} 门{4}",
                walls.Count, cliffs.Count, paves.Count, kelps.Count, gates.Count));

            // 【顺序很重要】必须先把素材克隆进 wengjie，再关素材场景。
            // 反过来（先关再克隆）源物体全部失效，克隆出来就是 0 个。
            var wallT = ArtCloneTemplates(walls, wengjie, art);
            var cliffT = ArtCloneTemplates(cliffs, wengjie, art);
            var paveT = ArtCloneTemplates(paves, wengjie, art);
            var kelpT = ArtCloneTemplates(kelps, wengjie, art);
            var gateT = ArtCloneTemplates(gates, wengjie, art);

            GameObject volumeClone = null;
            if (volumeSource != null)
            {
                volumeClone = Object.Instantiate(volumeSource);
                volumeClone.name = "Art_PostVolume";
                SceneManager.MoveGameObjectToScene(volumeClone, wengjie);
                volumeClone.transform.SetParent(art, true);
            }

            if (openedMap5) EditorSceneManager.CloseScene(map5, true);
            if (openedMap4) EditorSceneManager.CloseScene(map4, true);

            // ---- Map4 的水下氛围：**不要用** ----
            // 那是个 236 米的 WaterFogVolume 立方体，按 Map4 的尺度做的。
            // 搬进 wengjie 之后整块渲染成粉红色天棚 + 右侧一条红带，把画面毁了。
            // 水下感靠 Map5 抄过来的雾 / 环境光就够了。
            GameObject rigClone = null;

            // ---- 铺 ----
            int wallCount = ArtCladAll(ArtFind("Hub_WellWall_"), wallT, art, "WallA", 5f, true);
            wallCount += ArtCladAll(ArtFind("A_Wall_"), wallT, art, "WallB", 8f, true);
            wallCount += ArtCladAll(ArtFind("Hub_MarkerPillar_"), wallT, art, "Pillar", 6f, true);

            int cliffCount = ArtCladAll(ArtFind("B_Cliff_"), cliffT, art, "Cliff", 10f, false);

            int gateCount = ArtPlaceGates(gateT, art);

            int paveCount = ArtTileAll(ArtFind("Hub_Floor_"), paveT, art);
            paveCount += ArtTileAll(ArtFind("A_Floor_"), paveT, art);
            paveCount += ArtTileAll(ArtFind("A_Corridor_Floor"), paveT, art);
            paveCount += ArtTileAll(ArtFind("B_Terrace_"), paveT, art);
            paveCount += ArtTileAll(ArtFind("B_Corridor_Floor"), paveT, art);
            paveCount += ArtTileAll(ArtFind("B_Street_"), paveT, art);
            paveCount += ArtTileAll(ArtFind("B_Roof_"), paveT, art);
            paveCount += ArtTileAll(ArtFind("A_Ring_"), paveT, art);
            wallCount += ArtCladAll(ArtFind("C_Wall_"), wallT, art, "WallC", 12f, true);

            int kelpCount = ArtScatterKelp(kelpT, art);

            Debug.Log(string.Format(
                "[WengjieArt] 换装完成\n" +
                "  墙 {0} / 崖 {1} / 铺地 {2} / 海草 {3}\n" +
                "  模板：墙 {4}、崖 {5}、铺地 {6}、海草 {7}\n" +
                "  后期体积 {8}、水下氛围 {9}",
                wallCount, cliffCount, paveCount, kelpCount,
                wallT.Count, cliffT.Count, paveT.Count, kelpT.Count,
                volumeClone != null ? "有" : "无",
                rigClone != null ? "有" : "无"));

            ArtFixUi(art);

            // 体检：报出最大的一件，尺寸不对（>40 米）就说明定标又飞了
            GameObject biggest = ArtBiggestPiece(art);
            if (biggest != null)
            {
                Bounds b = ArtMeasure(biggest);
                Debug.Log(string.Format("[WengjieArt] 最大的一件：{0}  尺寸 {1:F1} × {2:F1} × {3:F1}",
                    biggest.name, b.size.x, b.size.y, b.size.z));
            }

            EditorSceneManager.MarkSceneDirty(wengjie);
            EditorSceneManager.SaveScene(wengjie);
            Selection.activeGameObject = artRoot;
        }

        // ---------------- 铺装 ----------------

        private static int ArtCladAll(List<GameObject> boxes, List<GameObject> templates, Transform art, string tag, float height, bool hideIfCovered)
        {
            int count = 0;
            if (templates.Count == 0) return 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                count += ArtClad(boxes[i], templates[i % templates.Count], art, tag, height, i, hideIfCovered);
            }

            return count;
        }

        private static int ArtClad(GameObject box, GameObject template, Transform art, string tag, float targetHeight, int seed, bool hideIfCovered)
        {
            Renderer boxRenderer = box != null ? box.GetComponent<Renderer>() : null;
            if (boxRenderer == null) return 0;

            Bounds boxBounds = boxRenderer.bounds;
            bool alongX = boxBounds.size.x >= boxBounds.size.z;
            float span = Mathf.Max(1f, alongX ? boxBounds.size.x : boxBounds.size.z);

            Bounds probe = ArtMeasure(template);
            if (probe.size.y <= 0.001f) return 0;

            // 【定标规则】高度和宽度都不能超过目标：
            //   只按高度定标会让本来就长的墙段变成几十米的巨型板（之前头顶那片粉色天棚就是它）。
            float longest = Mathf.Max(probe.size.x, Mathf.Max(probe.size.y, probe.size.z));
            float fitByHeight = targetHeight / Mathf.Max(0.001f, probe.size.y);
            float fitBySpan = (span * 0.9f) / Mathf.Max(0.001f, longest);
            float fit = Mathf.Min(fitByHeight, fitBySpan);
            float pieceSpan = Mathf.Max(0.8f, (alongX ? probe.size.x : probe.size.z) * fit);
            int count = Mathf.Clamp(Mathf.RoundToInt(span / pieceSpan), 1, 10);

            for (int i = 0; i < count; i++)
            {
                float offset = ((i + 0.5f) / count - 0.5f) * span;
                Vector3 center = boxBounds.center;
                if (alongX) center.x += offset;
                else center.z += offset;

                // 贴到盒子「朝向关卡中心」的那一面，压到几乎齐平，别凸出来变成「看得见踩不进去」的假墙
                float thickness = alongX ? boxBounds.size.z : boxBounds.size.x;
                float sign = alongX
                    ? -Mathf.Sign(center.z - 0f)
                    : -Mathf.Sign(center.x - 0f);
                if (Mathf.Approximately(sign, 0f)) sign = 1f;

                center += alongX
                    ? new Vector3(0f, 0f, sign * (thickness * 0.5f + 0.05f))
                    : new Vector3(sign * (thickness * 0.5f + 0.05f), 0f, 0f);

                GameObject piece = ArtPlace(template, art, center, alongX ? 0f : 90f, fit, (i * 31 + seed) % 3);
                piece.name = "Art_" + tag + "_" + box.name + "_" + i.ToString("00");
            }

            // 铺满了就把白盒墙的渲染关掉 —— **只关 MeshRenderer，碰撞体原样保留**。
            // 这样看到的是石头墙，撞到的还是同一堵墙，不会出现「看得见走不进去」。
            if (hideIfCovered && boxRenderer != null)
            {
                float coverage = count * pieceSpan / Mathf.Max(0.001f, span);
                if (coverage >= 0.85f)
                {
                    boxRenderer.enabled = false;
                }
            }

            return count;
        }

        private static int ArtTileAll(List<GameObject> floors, List<GameObject> templates, Transform art)
        {
            int count = 0;
            if (templates.Count == 0) return 0;

            for (int i = 0; i < floors.Count; i++)
            {
                count += ArtTile(floors[i], templates[i % templates.Count], art);
            }

            return count;
        }

        private static int ArtTile(GameObject floor, GameObject template, Transform art)
        {
            Renderer floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
            if (floorRenderer == null) return 0;

            Bounds bounds = floorRenderer.bounds;
            Bounds probe = ArtMeasure(template);
            if (probe.size.x <= 0.001f || probe.size.z <= 0.001f) return 0;

            const float targetTile = 7f;
            float fit = targetTile / Mathf.Max(probe.size.x, probe.size.z);
            float tileX = Mathf.Max(1f, probe.size.x * fit);
            float tileZ = Mathf.Max(1f, probe.size.z * fit);

            int nx = Mathf.Clamp(Mathf.RoundToInt(bounds.size.x / tileX), 1, 8);
            int nz = Mathf.Clamp(Mathf.RoundToInt(bounds.size.z / tileZ), 1, 8);

            int count = 0;
            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float fx = nx == 1 ? 0f : ((ix + 0.5f) / nx - 0.5f);
                    float fz = nz == 1 ? 0f : ((iz + 0.5f) / nz - 0.5f);

                    Vector3 center = new Vector3(
                        bounds.center.x + fx * bounds.size.x,
                        bounds.max.y + 0.03f,
                        bounds.center.z + fz * bounds.size.z);

                    GameObject tile = ArtPlace(template, art, center, (ix * 37 + iz * 91) % 4 * 90f, fit, (ix + iz) % 3);
                    tile.name = "Art_Tile_" + floor.name + "_" + ix + "_" + iz;
                    count++;
                }
            }

            return count;
        }

        /// <summary>等比缩放到 fitScale，转 yaw，再把包围盒中心对到 center。</summary>
        private static GameObject ArtPlace(GameObject template, Transform parent, Vector3 center, float yaw, float fitScale, int jitterIndex)
        {
            GameObject go = Object.Instantiate(template, parent);
            go.SetActive(true);
            ArtRemoveColliders(go);

            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = template.transform.localScale * fitScale * (1f + (jitterIndex - 1) * 0.04f);

            Bounds b = ArtMeasure(go);
            go.transform.position += center - b.center;
            go.transform.position += Vector3.up * 0.01f;

            // 【保险】任何一件都不许超过 40 米 —— 资源包里有些预制体的包围盒是坏的
            b = ArtMeasure(go);
            float longestPlaced = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (longestPlaced > 40f)
            {
                go.transform.localScale *= 40f / longestPlaced;
                b = ArtMeasure(go);
                go.transform.position += center - b.center;
            }

            return go;
        }

        private static int ArtScatterKelp(List<GameObject> templates, Transform art)
        {
            if (templates.Count == 0) return 0;

            var anchors = new List<Vector3>();
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                anchors.Add(new Vector3(Mathf.Cos(angle) * 13.5f, 0f, Mathf.Sin(angle) * 13.5f));
            }
            for (int i = 0; i < 6; i++)
            {
                anchors.Add(new Vector3(-20.5f, 0f, -11f + i * 4.2f));
                anchors.Add(new Vector3(-55f, 0f, -11f + i * 4.2f));
            }
            for (int i = 0; i < 5; i++)
            {
                anchors.Add(new Vector3(-12.6f, i * 3f, 28f + i * 10f));
                anchors.Add(new Vector3(12.6f, i * 3f, 28f + i * 10f));
            }

            var rng = new System.Random(20261018);
            int count = 0;

            for (int i = 0; i < anchors.Count; i++)
            {
                GameObject go = Object.Instantiate(templates[i % templates.Count], art);
                go.SetActive(true);
                go.name = "Art_Kelp_" + i.ToString("00");
                ArtRemoveColliders(go);

                float scale = 0.9f + (float)rng.NextDouble() * 0.8f;
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale *= scale;

                // 按实际高度归到 2.2 米左右，别信预制体自带的包围盒
                Bounds b0 = ArtMeasure(go);
                if (b0.size.y > 0.001f)
                {
                    float fitKelp = Mathf.Clamp(2.2f / b0.size.y, 0.05f, 3f);
                    go.transform.localScale *= fitKelp;
                }

                Bounds b = ArtMeasure(go);
                Vector3 target = anchors[i] + new Vector3(
                    (float)rng.NextDouble() * 1.6f - 0.8f, 0f, (float)rng.NextDouble() * 1.6f - 0.8f);

                go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
                count++;
            }

            return count;
        }

        // ---------------- UI ----------------

        private static void ArtFixUi(Transform art)
        {
            // 关掉潮水的调试 HUD（左上角那行「距离终点被淹 … 秒」）
            BubbleTideDirector director = Object.FindFirstObjectByType<BubbleTideDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                SerializedObject so = new SerializedObject(director);
                SerializedProperty draw = so.FindProperty("m_DrawHud");
                if (draw != null) draw.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 【重要】不要往场景里塞 HUD 预制体。
            // 成品场景（Map4）里根本没有 Canvas / EventSystem —— UI 是运行时统一起的。
            // 塞进去的那个预制体带一张全屏 Image：既把画面盖成红色，又吃掉鼠标左键（吹不了泡）。
            GameObject strayHud = GameObject.Find("Level HUD");
            if (strayHud != null && strayHud.scene == art.gameObject.scene)
            {
                Object.DestroyImmediate(strayHud);
            }

            // 把泡泡类型标签的引用清空 —— 让切换器自己在运行时建那行小字（默认行为）。
            // 之前把它指到一个隐藏 Text 上，结果左下角什么都不显示。
            BubbleTypeSwitcher switcher = Object.FindFirstObjectByType<BubbleTypeSwitcher>(FindObjectsInactive.Include);
            if (switcher != null)
            {
                SerializedObject so = new SerializedObject(switcher);
                SerializedProperty label = so.FindProperty("m_Label");
                if (label != null && label.objectReferenceValue is UnityEngine.UI.Text text && text.gameObject.name == "BubbleTypeLabel_Hidden")
                {
                    label.objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    GameObject hidden = GameObject.Find("BubbleTypeLabel_Hidden");
                    if (hidden != null) Object.DestroyImmediate(hidden);
                }
            }
        }

        // ---------------- 工具 ----------------

        private static List<GameObject> ArtFind(string prefix)
        {
            var result = new List<GameObject>();
            Scene activeScene = SceneManager.GetActiveScene();
            GameObject[] roots = activeScene.GetRootGameObjects();
            var allList = new List<Transform>();
            for (int r = 0; r < roots.Length; r++) allList.AddRange(roots[r].GetComponentsInChildren<Transform>(true));
            Transform[] all = allList.ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.StartsWith(prefix, System.StringComparison.Ordinal) && all[i].GetComponent<Renderer>() != null)
                {
                    result.Add(all[i].gameObject);
                }
            }

            return result;
        }

        /// <summary>找出换装里体积最大的一件（用来抓「定标飞了」的巨型板）。</summary>
        private static GameObject ArtBiggestPiece(Transform art)
        {
            GameObject biggest = null;
            float best = -1f;

            for (int i = 0; i < art.childCount; i++)
            {
                GameObject child = art.GetChild(i).gameObject;
                if (child.name.StartsWith("T_src_", System.StringComparison.Ordinal)) continue;

                Bounds b = ArtMeasure(child);
                float volume = b.size.x * b.size.y * b.size.z;
                if (volume > best)
                {
                    best = volume;
                    biggest = child;
                }
            }

            return biggest;
        }

        private static Bounds ArtMeasure(GameObject go)
        {            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ArtRemoveColliders(GameObject go)
        {
            // 用「禁用」而不是 DestroyImmediate：
            // 换装进来的都是预制体实例，删组件在实例上不可靠，禁用一定生效。
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        /// <summary>在广场三个出口各立一道门洞。</summary>
        private static int ArtPlaceGates(List<GameObject> templates, Transform art)
        {
            if (templates.Count == 0) return 0;

            Vector3[] spots =
            {
                new Vector3(-15f, 3.5f, 0f),
                new Vector3(0f, 3.5f, 15f),
                new Vector3(0f, 3.5f, -15f),
            };
            float[] yaws = { 90f, 0f, 180f };

            int count = 0;
            for (int i = 0; i < spots.Length && i < templates.Count; i++)
            {
                Bounds b = ArtMeasure(templates[i]);
                if (b.size.y <= 0.001f) continue;

                float fit = Mathf.Min(7f / b.size.y, 40f / Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                GameObject gate = ArtPlace(templates[i], art, spots[i], yaws[i], fit, 1);
                gate.name = "Art_Gate_" + i;
                count++;
            }

            return count;
        }

        private static void ArtCollect(Scene scene, string prefix, List<GameObject> output, int max)
        {
            int found = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && found < max; i++)
            {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length && found < max; j++)
                {
                    if (!all[j].name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                    if (all[j].GetComponent<Renderer>() == null) continue;
                    output.Add(all[j].gameObject);
                    found++;
                }
            }
        }

        /// <summary>
        /// 打开素材场景，但**如果它已经开在编辑器里就复用**。
        /// 直接 Additive 打开已加载的场景会抛异常，整个换装会中断（表现为 Wengjie_Art 是空的）。
        /// </summary>
        private static Scene ArtOpenOrGet(string path, out bool openedByUs)
        {
            Scene existing = SceneManager.GetSceneByPath(path);
            if (existing.IsValid() && existing.isLoaded)
            {
                openedByUs = false;
                return existing;
            }

            openedByUs = true;
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        private static void ArtCollectContaining(Scene scene, string token, List<GameObject> output, int max)
        {
            int found = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && found < max; i++)
            {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length && found < max; j++)
                {
                    if (all[j].name.IndexOf(token, System.StringComparison.Ordinal) < 0) continue;
                    if (all[j].GetComponent<Renderer>() == null) continue;
                    output.Add(all[j].gameObject);
                    found++;
                }
            }
        }

        private static GameObject ArtFindByName(Scene scene, string prefix)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j].name.StartsWith(prefix, System.StringComparison.Ordinal)) return all[j].gameObject;
                }
            }

            return null;
        }

        /// <summary>克隆成模板：保持 active（否则包围盒为 0），但挪到地下 500 米。</summary>
        private static List<GameObject> ArtCloneTemplates(List<GameObject> sources, Scene target, Transform parent)
        {
            var clones = new List<GameObject>();
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] == null) continue;

                GameObject clone = Object.Instantiate(sources[i]);
                clone.name = "T_src_" + sources[i].name;
                SceneManager.MoveGameObjectToScene(clone, target);
                clone.transform.SetParent(parent, true);
                clone.transform.position += Vector3.down * 500f;
                clones.Add(clone);
            }

            return clones;
        }

        private static GameObject ArtFindVolume(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Volume[] volumes = roots[i].GetComponentsInChildren<Volume>(true);
                if (volumes.Length > 0) return volumes[0].gameObject;
            }

            return null;
        }

        /// <summary>
        /// 把一个场景的光照/雾/天空盒抄到另一个场景。
        ///
        /// RenderSettings 读的永远是「当前激活场景」的设置，所以要先切激活场景读值，
        /// 切回来再写值 —— 这是没有 CopySceneSettings 时唯一稳的做法。
        /// </summary>
        private static void ArtCopyLighting(Scene from, Scene to)
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

            SceneManager.SetActiveScene(previous);
        }
    }
}
namespace PAO.EditorTools
{
    /// <summary>
    /// 从 Map5 抓 TFD 建筑构件（拱门 / 柱 / 体块 / 墙），摆成远景。
    /// 单独一个类，追加在文件末尾，不改动原有 WengjieLevelSetup 的结构。
    /// </summary>
    public static class WengjieArchTools
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kMap5Path = "Assets/GameMain/Scenes/Map5.unity";
        private const string kRoot = "Wengjie_Arch";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 建筑（从 Map5 抓）", priority = 105)]
        public static void BuildArchitecture()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);

            Scene map5 = SceneManager.GetSceneByPath(kMap5Path);
            bool opened = false;
            if (!map5.IsValid() || !map5.isLoaded)
            {
                map5 = EditorSceneManager.OpenScene(kMap5Path, OpenSceneMode.Additive);
                opened = true;
            }

            var srcs = new List<GameObject>();
            string[] prefixes = { "TFD_Gate_01A_Module", "TFD_Column_02A_Module", "TFD_Building_Block", "TFD_Wall_04A" };
            for (int pi = 0; pi < prefixes.Length; pi++)
            {
                int found = 0;
                GameObject[] roots = map5.GetRootGameObjects();
                for (int r = 0; r < roots.Length && found < 3; r++)
                {
                    Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < all.Length && found < 3; i++)
                    {
                        if (!all[i].name.StartsWith(prefixes[pi], System.StringComparison.Ordinal)) continue;
                        if (all[i].GetComponent<Renderer>() == null) continue;
                        srcs.Add(all[i].gameObject);
                        found++;
                    }
                }
            }

            Vector3[] spots =
            {
                new Vector3(-24f, 0f, -8f), new Vector3(24f, 0f, -8f),
                new Vector3(-36f, 12f, 64f), new Vector3(36f, 12f, 64f),
                new Vector3(-46f, 12f, 150f), new Vector3(46f, 12f, 150f),
                new Vector3(-30f, 0f, -100f), new Vector3(30f, 0f, -100f),
            };

            int placed = 0;
            for (int i = 0; i < spots.Length && srcs.Count > 0; i++)
            {
                GameObject src = srcs[i % srcs.Count];
                GameObject clone = Object.Instantiate(src);
                clone.name = "Arch_" + i;
                SceneManager.MoveGameObjectToScene(clone, scene);
                clone.transform.SetParent(root.transform, true);

                Vector3 worldScale = clone.transform.lossyScale;
                Bounds b = Measure(clone);
                float fit = 16f / Mathf.Max(0.001f, b.size.y);
                fit = Mathf.Min(fit, 44f / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))));
                clone.transform.localScale *= fit;

                Bounds after = Measure(clone);
                clone.transform.position += spots[i] - new Vector3(after.center.x, after.min.y, after.center.z);

                Collider[] cols = clone.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++) cols[c].enabled = false;

                placed++;
            }

            if (opened) EditorSceneManager.CloseScene(map5, true);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 建筑已从 Map5 抓取并摆放：" + placed + " 件（源 " + srcs.Count + " 个）");
        }

        private static Bounds Measure(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 把大块平地的白盒材质换成 TFD 石材（直接借 Art_Tile 的 sharedMaterial），
    /// 解决「土方块没有质感」的问题。
    /// </summary>
    public static class WengjieMaterialTools
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 地面换石材", priority = 106)]
        public static void SwapFloorMaterial()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject art = GameObject.Find("Wengjie_Art");
            if (art == null)
            {
                Debug.LogError("[Wengjie] 找不到 Wengjie_Art。");
                return;
            }

            // 从任意一块铺地里取石材
            Material stone = null;
            Transform[] artAll = art.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < artAll.Length; i++)
            {
                if (!artAll[i].name.StartsWith("Art_Tile_", System.StringComparison.Ordinal)) continue;
                Renderer r = artAll[i].GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                {
                    stone = r.sharedMaterial;
                    break;
                }
            }

            if (stone == null)
            {
                Debug.LogError("[Wengjie] 铺地里找不到可用材质。");
                return;
            }

            int swapped = 0;
            string[] prefixes = { "Hub_Floor_", "A_Ring_", "B_Street_", "B_Roof_", "A_Ramp_", "Shaft_Floor", "SpawnPad" };
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    bool match = false;
                    for (int pi = 0; pi < prefixes.Length; pi++)
                    {
                        if (all[i].name.StartsWith(prefixes[pi], System.StringComparison.Ordinal)) { match = true; break; }
                    }
                    if (!match) continue;

                    Renderer renderer = all[i].GetComponent<Renderer>();
                    if (renderer == null) continue;
                    renderer.sharedMaterial = stone;
                    swapped++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 地面材质已换成石材：" + swapped + " 块（材质 " + stone.name + "）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 把地面石材染成水下蓝：复制一份 TFD 石材材质，把 BaseColor 乘成青蓝，
    /// 再赋给所有大块平地。保持石头纹理，只改色调。
    /// </summary>
    public static class WengjieBlueStone
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kAssetPath = "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 地面染蓝", priority = 107)]
        public static void TintBlue()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject art = GameObject.Find("Wengjie_Art");
            if (art == null) { Debug.LogError("[Wengjie] 找不到 Wengjie_Art。"); return; }

            Material stone = null;
            Transform[] artAll = art.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < artAll.Length; i++)
            {
                if (!artAll[i].name.StartsWith("Art_Tile_", System.StringComparison.Ordinal)) continue;
                Renderer r = artAll[i].GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null) { stone = r.sharedMaterial; break; }
            }

            if (stone == null) { Debug.LogError("[Wengjie] 找不到石材。"); return; }

            // 复制一份并染蓝
            Material blue = AssetDatabase.LoadAssetAtPath<Material>(kAssetPath);
            if (blue == null)
            {
                blue = new Material(stone);
                blue.name = "M_Wengjie_StoneBlue";
                Color tint = new Color(0.42f, 0.72f, 0.95f, 1f);
                if (blue.HasProperty("_BaseColor")) blue.SetColor("_BaseColor", tint);
                if (blue.HasProperty("_Color")) blue.SetColor("_Color", tint);
                AssetDatabase.CreateAsset(blue, kAssetPath);
            }

            int swapped = 0;
            string[] prefixes = { "Hub_Floor_", "A_Ring_", "B_Street_", "B_Roof_", "A_Ramp_", "Shaft_Floor", "SpawnPad" };
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    bool match = false;
                    for (int pi = 0; pi < prefixes.Length; pi++)
                    {
                        if (all[i].name.StartsWith(prefixes[pi], System.StringComparison.Ordinal)) { match = true; break; }
                    }
                    if (!match) continue;

                    Renderer renderer = all[i].GetComponent<Renderer>();
                    if (renderer == null) continue;
                    renderer.sharedMaterial = blue;
                    swapped++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 地面已染成水下蓝：" + swapped + " 块（材质 " + kAssetPath + "）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 第二份材质：给岩壁 / 石墙用的深一档的青蓝，和大块平地形成层次。
    /// </summary>
    public static class WengjieBlueStoneB
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kAssetPath = "Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue_B.mat";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 岩壁染蓝（第二份材质）", priority = 108)]
        public static void TintCliffs()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject art = GameObject.Find("Wengjie_Art");
            if (art == null) { Debug.LogError("[Wengjie] 找不到 Wengjie_Art。"); return; }

            Material stone = null;
            Transform[] artAll = art.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < artAll.Length; i++)
            {
                if (!artAll[i].name.StartsWith("Art_Wall", System.StringComparison.Ordinal)) continue;
                Renderer r = artAll[i].GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null) { stone = r.sharedMaterial; break; }
            }

            if (stone == null) { Debug.LogError("[Wengjie] 找不到石材。"); return; }

            Material blue = AssetDatabase.LoadAssetAtPath<Material>(kAssetPath);
            if (blue == null)
            {
                blue = new Material(stone);
                blue.name = "M_Wengjie_StoneBlue_B";
                // 比地面深一档、偏青一点 —— 上下两层不会糊成一片
                Color tint = new Color(0.28f, 0.55f, 0.78f, 1f);
                if (blue.HasProperty("_BaseColor")) blue.SetColor("_BaseColor", tint);
                if (blue.HasProperty("_Color")) blue.SetColor("_Color", tint);
                AssetDatabase.CreateAsset(blue, kAssetPath);
            }

            int swapped = 0;
            string[] prefixes = { "Art_Wall", "Art_Cliff", "Art_Arch", "Arch_", "Mood_Arch", "Mood_Column" };
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    bool match = false;
                    for (int pi = 0; pi < prefixes.Length; pi++)
                    {
                        if (all[i].name.StartsWith(prefixes[pi], System.StringComparison.Ordinal)) { match = true; break; }
                    }
                    if (!match) continue;

                    Renderer[] rs = all[i].GetComponentsInChildren<Renderer>(true);
                    for (int k = 0; k < rs.Length; k++) rs[k].sharedMaterial = blue;
                    swapped++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 岩壁已染蓝（第二份材质）：" + swapped + " 组，材质 " + kAssetPath);
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 建筑配色：三档材质轮换，解决「颜色太单薄」。
    ///   A 深板岩蓝 / B 青绿 / C 浅石 —— 交替赋给拱门和柱，远近有层次。
    /// </summary>
    public static class WengjieBuildingPalette
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kDir = "Assets/GameMain/Art/Materials/";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 建筑配色（三档）", priority = 109)]
        public static void Recolor()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject art = GameObject.Find("Wengjie_Art");
            if (art == null) { Debug.LogError("[Wengjie] 找不到 Wengjie_Art。"); return; }

            Material stone = null;
            Transform[] artAll = art.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < artAll.Length; i++)
            {
                if (!artAll[i].name.StartsWith("Art_Wall", System.StringComparison.Ordinal)) continue;
                Renderer r = artAll[i].GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null) { stone = r.sharedMaterial; break; }
            }
            if (stone == null) { Debug.LogError("[Wengjie] 找不到石材。"); return; }

            Color[] tints =
            {
                new Color(0.16f, 0.32f, 0.52f, 1f),   // A 深板岩蓝
                new Color(0.18f, 0.52f, 0.58f, 1f),   // B 青绿
                new Color(0.55f, 0.66f, 0.74f, 1f),   // C 浅石
            };
            string[] names = { "M_Bldg_A", "M_Bldg_B", "M_Bldg_C" };

            Material[] mats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                string path = kDir + names[i] + ".mat";
                mats[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mats[i] == null)
                {
                    mats[i] = new Material(stone);
                    mats[i].name = names[i];
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", tints[i]);
                    if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", tints[i]);
                    AssetDatabase.CreateAsset(mats[i], path);
                }
            }

            string[] prefixes = { "Arch_", "Mood_Arch", "Mood_Column", "Art_Arch" };
            int swapped = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    bool match = false;
                    for (int pi = 0; pi < prefixes.Length; pi++)
                    {
                        if (all[i].name.StartsWith(prefixes[pi], System.StringComparison.Ordinal)) { match = true; break; }
                    }
                    if (!match) continue;

                    Material m = mats[swapped % 3];
                    Renderer[] rs = all[i].GetComponentsInChildren<Renderer>(true);
                    for (int k = 0; k < rs.Length; k++) rs[k].sharedMaterial = m;
                    swapped++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 建筑配色完成：" + swapped + " 组，三档材质交替（A深板岩 / B青绿 / C浅石）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 对比度修正：压雾、提方向光、给材质去饱和。
    /// 「太蓝太单薄」的根因不是材质，是雾把远处的色差全冲平了。
    /// </summary>
    public static class WengjieContrast
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 对比度修正", priority = 110)]
        public static void Fix()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ---------- ① 雾：变暗、变薄，远处的对比才留得住 ----------
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.10f, 0.30f, 0.38f, 1f);   // 深青，不是淡蓝
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.006f;                            // 之前太浓
            RenderSettings.ambientIntensity = 0.65f;                       // 环境光压低，让方向光起作用

            // ---------- ② 方向光：提亮 + 暖色，和青蓝撞开 ----------
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional) continue;
                lights[i].intensity = 1.7f;
                lights[i].color = new Color(1f, 0.93f, 0.80f);
            }

            // ---------- ③ 材质去饱和：从「一片蓝」变成「灰蓝 / 青灰 / 暖灰」 ----------
            Color[] tints =
            {
                new Color(0.34f, 0.40f, 0.47f, 1f),   // A 冷灰
                new Color(0.30f, 0.46f, 0.46f, 1f),   // B 青灰
                new Color(0.58f, 0.55f, 0.49f, 1f),   // C 暖灰
            };
            string[] paths =
            {
                "Assets/GameMain/Art/Materials/M_Bldg_A.mat",
                "Assets/GameMain/Art/Materials/M_Bldg_B.mat",
                "Assets/GameMain/Art/Materials/M_Bldg_C.mat",
            };

            int mats = 0;
            for (int i = 0; i < 3; i++)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (m == null) continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tints[i]);
                if (m.HasProperty("_Color")) m.SetColor("_Color", tints[i]);
                EditorUtility.SetDirty(m);
                mats++;
            }

            // 地面也别那么蓝
            Material floor = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat");
            if (floor != null)
            {
                Color c = new Color(0.40f, 0.46f, 0.50f, 1f);
                if (floor.HasProperty("_BaseColor")) floor.SetColor("_BaseColor", c);
                if (floor.HasProperty("_Color")) floor.SetColor("_Color", c);
                EditorUtility.SetDirty(floor);
                mats++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 对比度已修：雾 0.006/深青、方向光 1.7 暖色、环境光 0.65、材质 " + mats + " 份去饱和");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 井的垂直路线改版：不再用粘泡提拉一层层爬，
    /// 改成三段 3 米宽的「挤压烟囱」——挤墙弹射（弹力泡）才能上，
    /// 再补一道上生气流作为免费备选。
    /// </summary>
    public static class WengjieWellVertical
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_WellShafts";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 井改成弹射上行", priority = 111)]
        public static void Rebuild()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            // 干掉原来 4 米一层的落脚台（那是给粘泡提拉用的）
            GameObject stageC = GameObject.Find("Wengjie_P1_C");
            int removed = 0;
            if (stageC != null)
            {
                Transform[] all = stageC.GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (all[i].name.StartsWith("C_Ledge_", System.StringComparison.Ordinal))
                    {
                        Object.DestroyImmediate(all[i].gameObject);
                        removed++;
                    }
                }
            }

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Vector3 c = new Vector3(0f, 0f, 200f);
            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");

            // 三段烟囱：底部 y=34，每段升 8 米，3 米宽，四面墙只留上下开口
            float[] baseY = { 34f, 42f, 50f };
            float[] angles = { 0f, 120f, 240f };

            for (int i = 0; i < 3; i++)
            {
                float rad = angles[i] * Mathf.Deg2Rad;
                Vector3 center = c + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * 22f;

                var shaft = new GameObject("Shaft_" + i);
                shaft.transform.SetParent(t, false);
                shaft.transform.position = new Vector3(center.x, baseY[i], center.z);

                // 两面「挤压墙」：间距 3 米，高 9 米 —— 挤墙弹射必须靠它们
                GameObject w1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w1.name = "WallA";
                w1.transform.SetParent(shaft.transform, false);
                w1.transform.localPosition = new Vector3(0f, 4.5f, -1.5f);
                w1.transform.localScale = new Vector3(6f, 9f, 0.6f);
                if (edge != null) w1.GetComponent<Renderer>().sharedMaterial = edge;

                GameObject w2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w2.name = "WallB";
                w2.transform.SetParent(shaft.transform, false);
                w2.transform.localPosition = new Vector3(0f, 4.5f, 1.5f);
                w2.transform.localScale = new Vector3(6f, 9f, 0.6f);
                if (edge != null) w2.GetComponent<Renderer>().sharedMaterial = edge;

                // 顶部落脚台
                GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                top.name = "TopLanding";
                top.transform.SetParent(shaft.transform, false);
                top.transform.localPosition = new Vector3(0f, 9f, 0f);
                top.transform.localScale = new Vector3(7f, 0.6f, 6f);
                if (ground != null) top.GetComponent<Renderer>().sharedMaterial = ground;
            }

            // 一道上生气流：最后一段的免费备选（不强迫，但不让弹射失败就卡死）
            var updraft = new GameObject("Well_Updraft");
            updraft.transform.SetParent(t, false);
            updraft.transform.position = new Vector3(0f, 55f, 200f);
            BoxCollider trig = updraft.AddComponent<BoxCollider>();
            trig.size = new Vector3(10f, 14f, 10f);
            trig.isTrigger = true;
            var lift = updraft.AddComponent<PAO.WhiteboxUpdraft>();
            lift.Configure(3.4f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 井的垂直路线已改：删掉 " + removed + " 层粘泡落脚台，换成 3 段 3 米宽挤压烟囱（弹力泡）+ 1 道上生气流");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 三种能力的「独占关卡件」：每一件都只有一种能力能解。
    ///   1 重石墙（质量 120）—— 只有炸弹推得动，泡泡推不动
    ///   2 高位粘附点 —— 只有粘泡跳得上去
    ///   3 弹力泡容器 —— 只有弹力泡填得满
    /// </summary>
    public static class WengjieAbilityPieces
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Ability";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 三能力专属件", priority = 112)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");
            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material pop = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");
            Material stickyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_FloatSticky.mat");

            // ---------- ① 重石墙：只有炸弹推得动 ----------
            var wall = new GameObject("OnlyBomb_Wall");
            wall.transform.SetParent(t, false);
            wall.transform.position = new Vector3(0f, 13.6f, 79.5f);

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.transform.SetParent(wall.transform, false);
            slab.transform.localScale = new Vector3(10f, 5f, 1.2f);
            if (pop != null) slab.GetComponent<Renderer>().sharedMaterial = pop;

            Rigidbody body = slab.AddComponent<Rigidbody>();
            body.mass = 120f;          // 泡泡推不动，只有炸弹冲量推得动
            body.drag = 0.8f;

            // 两侧导轨：墙只能沿 X 滑开，不会翻倒
            for (int i = 0; i < 2; i++)
            {
                GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "Rail_" + i;
                rail.transform.SetParent(wall.transform, false);
                rail.transform.localPosition = new Vector3(i == 0 ? -5.6f : 5.6f, 0f, 0f);
                rail.transform.localScale = new Vector3(0.6f, 6f, 1.6f);
                if (edge != null) rail.GetComponent<Renderer>().sharedMaterial = edge;
            }

            // ---------- ② 高位粘附点：只有粘泡上得去 ----------
            var sticky = new GameObject("OnlySticky_Anchor");
            sticky.transform.SetParent(t, false);
            sticky.transform.position = new Vector3(0f, 13.5f, 120f);

            GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            patch.name = "Patch";
            patch.transform.SetParent(sticky.transform, false);
            patch.transform.localPosition = new Vector3(0f, 7f, 0f);
            patch.transform.localScale = new Vector3(0.5f, 6f, 5f);   // 高处 7 米，跳 1.2 米够不到
            if (stickyMat != null) patch.GetComponent<Renderer>().sharedMaterial = stickyMat;
            patch.AddComponent<PAO.StickySurface>();

            // ---------- ③ 弹力泡容器：只有弹力泡填得满 ----------
            var container = new GameObject("OnlyBouncy_Container");
            container.transform.SetParent(t, false);
            container.transform.position = new Vector3(0f, 26.9f, 146f);

            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Shell";
            shell.transform.SetParent(container.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * 3.2f;
            if (pop != null) shell.GetComponent<Renderer>().sharedMaterial = pop;

            PAO.TerrainBubble tb = shell.AddComponent<PAO.TerrainBubble>();
            SerializedObject so = new SerializedObject(tb);
            so.FindProperty("m_Capacity").intValue = 5;    // 要两发满蓄力弹力泡
            so.FindProperty("m_RewardAmount").intValue = 8;
            so.FindProperty("m_RespawnDelay").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 三能力专属件已建：重石墙(只有炸弹) / 高位粘附点(只有粘) / 弹力泡容器(只有弹)");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 能力组合件：每一件都要**两种能力按顺序用**，单独一种过不去。
    ///   Combo A：粘 → 弹（粘泡造台阶，站上去再挤墙弹射）
    ///   Combo B：炸 → 粘（炸弹推开石板，后面才有可粘面可以爬）
    /// </summary>
    public static class WengjieAbilityCombos
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Combos";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 能力组合件", priority = 113)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");
            Material stickyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_FloatSticky.mat");
            Material pop = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");

            // ============ Combo A：粘 → 弹 ============
            // 14 米竖直井：底部跳不上去；中途 6 米有可粘面；
            // 顶部有挑檐，提拉会被挡；只有「粘成台阶 + 站在台阶上挤墙弹射」能上。
            Vector3 a = new Vector3(20f, 34f, 180f);

            Box(t, "ComboA_Wall_L", a + new Vector3(-2.5f, 7f, 0f), new Vector3(0.8f, 14f, 6f), edge);
            Box(t, "ComboA_Wall_R", a + new Vector3(2.5f, 7f, 0f), new Vector3(0.8f, 14f, 6f), edge);
            Box(t, "ComboA_Eave", a + new Vector3(0f, 11f, 0f), new Vector3(4f, 0.8f, 6f), edge);   // 挑檐：提拉到此为止

            GameObject patch = Box(t, "ComboA_Sticky", a + new Vector3(-2f, 6f, 0f), new Vector3(0.4f, 5f, 4f), stickyMat);
            patch.AddComponent<PAO.StickySurface>();

            Box(t, "ComboA_Top", a + new Vector3(0f, 14.2f, 0f), new Vector3(8f, 0.6f, 6f), ground);

            // ============ Combo B：炸 → 粘 ============
            // 一个竖井被重石板盖住；炸弹推开石板后，里面才露出可粘面，才能爬上去。
            Vector3 b = new Vector3(-20f, 34f, 180f);

            Box(t, "ComboB_Wall_L", b + new Vector3(-2.5f, 7f, 0f), new Vector3(0.8f, 14f, 6f), edge);
            Box(t, "ComboB_Wall_R", b + new Vector3(2.5f, 7f, 0f), new Vector3(0.8f, 14f, 6f), edge);
            Box(t, "ComboB_Top", b + new Vector3(0f, 14.2f, 0f), new Vector3(8f, 0.6f, 6f), ground);

            var lid = new GameObject("ComboB_Lid");
            lid.transform.SetParent(t, false);
            lid.transform.position = b + new Vector3(0f, 6f, 0f);

            GameObject lidSlab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lidSlab.name = "Slab";
            lidSlab.transform.SetParent(lid.transform, false);
            lidSlab.transform.localScale = new Vector3(4.6f, 0.8f, 5.4f);
            if (pop != null) lidSlab.GetComponent<Renderer>().sharedMaterial = pop;

            Rigidbody lidBody = lidSlab.AddComponent<Rigidbody>();
            lidBody.mass = 100f;      // 泡泡推不动，炸弹推得动
            lidBody.drag = 0.6f;

            GameObject patchB = Box(t, "ComboB_Sticky", b + new Vector3(-2f, 10f, 0f), new Vector3(0.4f, 5f, 4f), stickyMat);
            patchB.AddComponent<PAO.StickySurface>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 组合件已建：A 粘→弹（粘台阶+挤墙弹射上 14 米）/ B 炸→粘（炸弹推盖+粘面爬升）");
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 组合 C/D：两把锁串联。
    ///   C 弹 → 炸：先填弹力泡容器，炸开后露出重石板，再用炸弹推开
    ///   D 炸 → 弹：先炸开盖板，露出容器，再用弹力泡填满
    /// </summary>
    public static class WengjieAbilityCombos2
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Combos2";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 能力组合件 C/D", priority = 114)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material pop = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");

            // ---------- C：弹 → 炸 ----------
            Vector3 c = new Vector3(0f, 34f, 240f);   // 井北侧走廊

            Box(t, "ComboC_Wall_L", c + new Vector3(-3f, 4f, 0f), new Vector3(0.8f, 8f, 8f), edge);
            Box(t, "ComboC_Wall_R", c + new Vector3(3f, 4f, 0f), new Vector3(0.8f, 8f, 8f), edge);
            Box(t, "ComboC_Floor", c + new Vector3(0f, -0.4f, 0f), new Vector3(6f, 0.8f, 10f), edge);

            var plug = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            plug.name = "ComboC_Plug";
            plug.transform.SetParent(t, false);
            plug.transform.position = c + new Vector3(0f, 1.8f, -1f);
            plug.transform.localScale = Vector3.one * 3f;
            if (pop != null) plug.GetComponent<Renderer>().sharedMaterial = pop;
            PAO.TerrainBubble plugTb = plug.AddComponent<PAO.TerrainBubble>();
            SerializedObject so1 = new SerializedObject(plugTb);
            so1.FindProperty("m_Capacity").intValue = 3;
            so1.FindProperty("m_RespawnDelay").floatValue = 0f;
            so1.ApplyModifiedPropertiesWithoutUndo();

            var slabC = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slabC.name = "ComboC_Slab";
            slabC.transform.SetParent(t, false);
            slabC.transform.position = c + new Vector3(0f, 2.5f, 3f);
            slabC.transform.localScale = new Vector3(5.4f, 5f, 1.2f);
            if (pop != null) slabC.GetComponent<Renderer>().sharedMaterial = pop;
            Rigidbody bodyC = slabC.AddComponent<Rigidbody>();
            bodyC.mass = 100f;
            bodyC.drag = 0.6f;

            // ---------- D：炸 → 弹 ----------
            Vector3 d = new Vector3(14f, 34f, 240f);

            Box(t, "ComboD_Wall_L", d + new Vector3(-3f, 4f, 0f), new Vector3(0.8f, 8f, 8f), edge);
            Box(t, "ComboD_Wall_R", d + new Vector3(3f, 4f, 0f), new Vector3(0.8f, 8f, 8f), edge);
            Box(t, "ComboD_Floor", d + new Vector3(0f, -0.4f, 0f), new Vector3(6f, 0.8f, 10f), edge);

            var lidD = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lidD.name = "ComboD_Lid";
            lidD.transform.SetParent(t, false);
            lidD.transform.position = d + new Vector3(0f, 3f, -1f);
            lidD.transform.localScale = new Vector3(5.4f, 0.8f, 5.4f);
            if (pop != null) lidD.GetComponent<Renderer>().sharedMaterial = pop;
            Rigidbody bodyD = lidD.AddComponent<Rigidbody>();
            bodyD.mass = 90f;
            bodyD.drag = 0.6f;

            var containD = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            containD.name = "ComboD_Container";
            containD.transform.SetParent(t, false);
            containD.transform.position = d + new Vector3(0f, 1.4f, -1f);
            containD.transform.localScale = Vector3.one * 2.6f;
            if (pop != null) containD.GetComponent<Renderer>().sharedMaterial = pop;
            PAO.TerrainBubble tbD = containD.AddComponent<PAO.TerrainBubble>();
            SerializedObject so2 = new SerializedObject(tbD);
            so2.FindProperty("m_Capacity").intValue = 4;
            so2.FindProperty("m_RespawnDelay").floatValue = 0f;
            so2.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 组合 C/D 已建：C 弹→炸 / D 炸→弹（井北走廊 z=240）");
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 剧情 / 配音点位：6 个，全部放在「玩家刚站稳、抬头看」的停顿处。
    /// 不打断跑动，只在转折点说话。
    /// </summary>
    public static class WengjieStory
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Story";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 剧情配音点位", priority = 115)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);

            // 位置 / 说话人 / 台词
            object[][] nodes =
            {
                new object[] { new Vector3(0f, 1f, -26f), "泡泡", "甜甜圈！" },
                new object[] { new Vector3(0f, 2f, 2f), "泡泡", "往上走，水在涨。" },
                new object[] { new Vector3(0f, 13f, 66f), "回音", "粘住墙，下面是空的。" },
                new object[] { new Vector3(0f, 13f, 79f), "回音", "有人试过，没走完。" },
                new object[] { new Vector3(0f, 35f, 194f), "钟声", "钟响的时候，水到顶。" },
                new object[] { new Vector3(0f, 64.6f, 200f), "泡泡", "到了。然后呢？" },
            };

            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 pos = (Vector3)nodes[i][0];
                var go = new GameObject("Story_" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;

                BoxCollider trig = go.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.size = new Vector3(6f, 4f, 4f);

                PAO.WengjieStoryNode node = go.AddComponent<PAO.WengjieStoryNode>();
                SerializedObject so = new SerializedObject(node);
                so.FindProperty("m_Speaker").stringValue = (string)nodes[i][1];
                so.FindProperty("m_Line").stringValue = (string)nodes[i][2];

                // 旁白配音：命名约定 vo_N.wav，和节点索引一一对应
                string clipPath = "Assets/GameMain/Audio/WengjieVO/vo_" + i + ".wav";
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip != null) so.FindProperty("m_Voice").objectReferenceValue = clip;
                so.FindProperty("m_Delay").floatValue = 0.2f;
                so.FindProperty("m_Once").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 剧情点位已布：" + nodes.Length + " 个（出生点 / 坡道口 / 环桥 / 桥面 / 井口 / 塔顶）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 物理机关：全部靠泡泡的「重量 / 冲量 / 附着」驱动，不用任何 UI 提示。
    ///   P1 压力板  —— 压上足够重量，平台升起（炸弹重、粘泡浮、弹力泡轻，物理表现天然不同）
    ///   P2 平衡梁  —— 一头放重物，另一头翘起，把玩家弹上去
    ///   P3 风车     —— 被泡泡的冲量推动旋转，叶片扫过带动下一组平台
    ///   P4 弹簧板  —— 弹力泡落下时的反作用力把板压下去，玩家借力
    /// 旧的「栅栏 / 尖刺 / 尺寸门」这类纯阻挡物全部删掉。
    /// </summary>
    public static class WengjieMechanisms
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Mechanisms";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 换成物理机关", priority = 116)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ---------- 删掉旧的纯阻挡类障碍 ----------
            string[] obsolete = { "Wengjie_Obstacles2", "Wengjie_Orbs", "Wengjie_Ability", "Wengjie_Combos", "Wengjie_Combos2" };
            int removed = 0;
            for (int i = 0; i < obsolete.Length; i++)
            {
                GameObject o = GameObject.Find(obsolete[i]);
                if (o != null) { Object.DestroyImmediate(o); removed++; }
            }

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");
            Material pop = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");

            int built = 0;

            // ---------- P1 压力板 ×2：压够重量，平台升起 ----------
            Vector3[] plateSpots = { new Vector3(-6f, 12.6f, 66f), new Vector3(6f, 12.6f, 100f) };
            for (int i = 0; i < plateSpots.Length; i++)
                built += PressurePlate(t, "Plate_" + i, plateSpots[i], edge, pop, ground);

            // ---------- P2 平衡梁 ×2：一头重，另一头翘 ----------
            Vector3[] seeSpots = { new Vector3(0f, 12.6f, 120f), new Vector3(0f, 26.6f, 132f) };
            for (int i = 0; i < seeSpots.Length; i++)
                built += Seesaw(t, "Seesaw_" + i, seeSpots[i], edge, pop);

            // ---------- P3 风车 ×2：被冲量推着转 ----------
            Vector3[] wheelSpots = { new Vector3(0f, 14f, 74f), new Vector3(0f, 28f, 118f) };
            for (int i = 0; i < wheelSpots.Length; i++)
                built += Wheel(t, "Wheel_" + i, wheelSpots[i], edge);

            // ---------- P4 弹簧板 ×2 ----------
            Vector3[] springSpots = { new Vector3(0f, 12.6f, 88f), new Vector3(0f, 26.6f, 108f) };
            for (int i = 0; i < springSpots.Length; i++)
                built += Spring(t, "Spring_" + i, springSpots[i], edge, ground);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 已换成物理机关：删掉旧障碍根 " + removed + " 组，新建机关 " + built + " 件（压力板2 / 平衡梁2 / 风车2 / 弹簧板2）");
        }

        private static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider = true)
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

        /// <summary>压力板：上方重量 ≥ 阈值时，底板下沉、配重平台升起。</summary>
        private static int PressurePlate(Transform parent, string name, Vector3 pos, Material edge, Material pop, Material ground)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            // 活动的踏板
            GameObject plate = Cube(root.transform, "Plate", Vector3.zero, new Vector3(5f, 0.4f, 5f), pop);
            Rigidbody body = plate.AddComponent<Rigidbody>();
            body.mass = 20f;
            body.drag = 1.5f;
            body.angularDrag = 3f;

            // 导轨：只允许上下移动
            ConfigurableJoint joint = plate.AddComponent<ConfigurableJoint>();
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Limited;
            SoftJointLimit limit = joint.linearLimit;
            limit.limit = 1.2f;
            joint.linearLimit = limit;
            joint.yDrive = new JointDrive { positionSpring = 900f, positionDamper = 60f, maximumForce = 4000f };

            // 底座
            Cube(root.transform, "Base", new Vector3(0f, -1.6f, 0f), new Vector3(6f, 0.6f, 6f), edge);

            // 连动的闸门（踏板压下去，闸门升起）
            GameObject gate = Cube(root.transform, "LinkedGate", new Vector3(6f, -0.4f, 0f), new Vector3(0.8f, 5f, 6f), ground);
            Rigidbody gateBody = gate.AddComponent<Rigidbody>();
            gateBody.mass = 18f;
            ConfigurableJoint gj = gate.AddComponent<ConfigurableJoint>();
            gj.xMotion = ConfigurableJointMotion.Locked;
            gj.zMotion = ConfigurableJointMotion.Locked;
            gj.angularXMotion = ConfigurableJointMotion.Locked;
            gj.angularYMotion = ConfigurableJointMotion.Locked;
            gj.angularZMotion = ConfigurableJointMotion.Locked;
            gj.yMotion = ConfigurableJointMotion.Limited;
            SoftJointLimit gl = gj.linearLimit;
            gl.limit = 3f;
            gj.linearLimit = gl;
            gj.yDrive = new JointDrive { positionSpring = 400f, positionDamper = 40f, maximumForce = 2000f };

            // 用弹簧把闸门和踏板连起来（重量 vs 升起的物理耦合）
            SpringJoint link = gate.AddComponent<SpringJoint>();
            link.connectedBody = body;
            link.spring = 1200f;
            link.damper = 50f;
            link.minDistance = 0f;
            link.maxDistance = 3f;

            return 1;
        }

        /// <summary>平衡梁：一端放重物会翘起来，把玩家甩上去。</summary>
        private static int Seesaw(Transform parent, string name, Vector3 pos, Material edge, Material pop)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            GameObject beam = Cube(root.transform, "Beam", Vector3.zero, new Vector3(10f, 0.5f, 3f), pop);
            Rigidbody body = beam.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.centerOfMass = Vector3.zero;

            HingeJoint hinge = beam.AddComponent<HingeJoint>();
            hinge.axis = Vector3.forward;
            hinge.useLimits = true;
            JointLimits limits = hinge.limits;
            limits.min = -22f;
            limits.max = 22f;
            hinge.limits = limits;

            Cube(root.transform, "Pivot", new Vector3(0f, -0.9f, 0f), new Vector3(0.6f, 1.8f, 3f), edge);
            return 1;
        }

        /// <summary>风车：被泡泡的冲量推着转，叶片扫过带动连动杆。</summary>
        private static int Wheel(Transform parent, string name, Vector3 pos, Material edge)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            GameObject hub = Cube(root.transform, "Hub", Vector3.zero, new Vector3(0.6f, 4f, 0.6f), edge);
            Rigidbody body = hub.AddComponent<Rigidbody>();
            body.mass = 8f;
            body.drag = 0.2f;
            body.angularDrag = 0.35f;

            HingeJoint hinge = hub.AddComponent<HingeJoint>();
            hinge.axis = Vector3.up;
            hinge.useLimits = false;

            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f;
                GameObject blade = Cube(hub.transform, "Blade_" + i, Vector3.zero, new Vector3(4f, 0.3f, 1.2f), edge);
                blade.transform.localPosition = Quaternion.Euler(0f, ang, 0f) * new Vector3(2f, 0f, 0f);
                blade.transform.localRotation = Quaternion.Euler(0f, ang, 0f);
                Object.DestroyImmediate(blade.GetComponent<Collider>());
                BoxCollider bc = blade.AddComponent<BoxCollider>();
                bc.isTrigger = false;
            }

            return 1;
        }

        /// <summary>弹簧板：被压下后弹回，把人送上去。</summary>
        private static int Spring(Transform parent, string name, Vector3 pos, Material edge, Material ground)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            GameObject pad = Cube(root.transform, "Pad", Vector3.zero, new Vector3(4f, 0.4f, 4f), ground);
            Rigidbody body = pad.AddComponent<Rigidbody>();
            body.mass = 10f;
            body.drag = 0.5f;

            ConfigurableJoint joint = pad.AddComponent<ConfigurableJoint>();
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Limited;
            SoftJointLimit limit = joint.linearLimit;
            limit.limit = 1.5f;
            joint.linearLimit = limit;
            joint.yDrive = new JointDrive { positionSpring = 2600f, positionDamper = 22f, maximumForce = 9000f };

            Cube(root.transform, "Frame", new Vector3(0f, -1.2f, 0f), new Vector3(4.6f, 1.2f, 4.6f), edge);
            return 1;
        }
    }
}
namespace PAO.EditorTools
{
    /// <summary>
    /// 物理机关 第二批：接触类。
    ///   旋转门 —— 被推一下转半圈，粘泡贴上去会被带着转
    ///   摆锤   —— 被冲量打到会荡起来，荡回来能砸开东西
    ///   滚筒   —— 可以被推着滚，滚到位置可以当桥
    ///   柱塞   —— 压下去弹回来，把人/泡弹飞
    /// </summary>
    public static class WengjieMechanisms2
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Mechanisms2";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 物理机关 第二批", priority = 117)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            Transform t = root.transform;

            Material edge = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Platform.mat");
            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");
            Material pop = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");
            Material sticky = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_FloatSticky.mat");

            int n = 0;

            // 旋转门 ×3
            n += Door(t, "Door_0", new Vector3(0f, 12.6f, 70f), edge);
            n += Door(t, "Door_1", new Vector3(0f, 12.6f, 112f), edge);
            n += Door(t, "Door_2", new Vector3(0f, 26.6f, 126f), edge);

            // 摆锤 ×3
            n += Pendulum(t, "Pendulum_0", new Vector3(4f, 20f, 76f), edge, pop);
            n += Pendulum(t, "Pendulum_1", new Vector3(-4f, 20f, 118f), edge, pop);
            n += Pendulum(t, "Pendulum_2", new Vector3(4f, 34f, 140f), edge, pop);

            // 滚筒 ×3
            n += Roller(t, "Roller_0", new Vector3(0f, 13.2f, 84f), ground);
            n += Roller(t, "Roller_1", new Vector3(0f, 13.2f, 96f), ground);
            n += Roller(t, "Roller_2", new Vector3(0f, 27.2f, 114f), ground);

            // 柱塞 ×3
            n += Plunger(t, "Plunger_0", new Vector3(0f, 12.6f, 104f), edge, pop);
            n += Plunger(t, "Plunger_1", new Vector3(0f, 26.6f, 130f), edge, pop);
            n += Plunger(t, "Plunger_2", new Vector3(0f, 34.6f, 178f), edge, pop);

            // 可粘转盘 ×1：粘泡粘上去会被带着转，弹力泡只能推
            n += StickyDisc(t, "StickyDisc", new Vector3(0f, 13.4f, 92f), sticky, edge);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 第二批物理机关已建：" + n + " 件（旋转门3 / 摆锤3 / 滚筒3 / 柱塞3 / 粘转盘1）");
        }

        private static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        private static int Door(Transform parent, string name, Vector3 pos, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Cube(root.transform, "Frame", new Vector3(0f, 0f, 0f), new Vector3(0.4f, 5f, 0.4f), mat);

            GameObject leaf = Cube(root.transform, "Leaf", new Vector3(0f, 2.4f, 1.6f), new Vector3(0.3f, 4.6f, 3.2f), mat);
            Rigidbody body = leaf.AddComponent<Rigidbody>();
            body.mass = 14f;
            body.drag = 0.3f;
            body.angularDrag = 0.45f;

            HingeJoint hinge = leaf.AddComponent<HingeJoint>();
            hinge.axis = Vector3.up;
            hinge.useLimits = false;
            return 1;
        }

        private static int Pendulum(Transform parent, string name, Vector3 pos, Material frameMat, Material weightMat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Cube(root.transform, "Arm", new Vector3(0f, 3.5f, 0f), new Vector3(0.3f, 7f, 0.3f), frameMat);

            GameObject weight = Cube(root.transform, "Weight", new Vector3(0f, 0.4f, 0f), new Vector3(1.8f, 1.8f, 1.8f), weightMat);
            Rigidbody body = weight.AddComponent<Rigidbody>();
            body.mass = 45f;
            body.drag = 0.1f;
            body.angularDrag = 0.1f;

            HingeJoint hinge = weight.AddComponent<HingeJoint>();
            hinge.axis = Vector3.right;
            hinge.useLimits = true;
            JointLimits limits = hinge.limits;
            limits.min = -70f;
            limits.max = 70f;
            hinge.limits = limits;
            return 1;
        }

        private static int Roller(Transform parent, string name, Vector3 pos, Material mat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            GameObject drum = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            drum.name = "Drum";
            drum.transform.SetParent(root.transform, false);
            drum.transform.localPosition = Vector3.zero;
            drum.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            drum.transform.localScale = new Vector3(2.2f, 1.6f, 2.2f);
            Renderer r = drum.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;

            Rigidbody body = drum.AddComponent<Rigidbody>();
            body.mass = 55f;
            body.drag = 0.15f;
            body.angularDrag = 0.12f;
            return 1;
        }

        private static int Plunger(Transform parent, string name, Vector3 pos, Material frameMat, Material capMat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Cube(root.transform, "Cylinder", new Vector3(0f, -1.2f, 0f), new Vector3(2.4f, 2.4f, 2.4f), frameMat);

            GameObject cap = Cube(root.transform, "Cap", new Vector3(0f, 0.2f, 0f), new Vector3(2.6f, 0.4f, 2.6f), capMat);
            Rigidbody body = cap.AddComponent<Rigidbody>();
            body.mass = 12f;
            body.drag = 0.4f;

            ConfigurableJoint joint = cap.AddComponent<ConfigurableJoint>();
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Limited;
            SoftJointLimit limit = joint.linearLimit;
            limit.limit = 2f;
            joint.linearLimit = limit;
            joint.yDrive = new JointDrive { positionSpring = 3500f, positionDamper = 18f, maximumForce = 12000f };
            return 1;
        }

        private static int StickyDisc(Transform parent, string name, Vector3 pos, Material discMat, Material frameMat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Cube(root.transform, "Pivot", new Vector3(0f, -0.8f, 0f), new Vector3(0.5f, 1.6f, 0.5f), frameMat);

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localScale = new Vector3(6f, 0.25f, 6f);
            Renderer r = disc.GetComponent<Renderer>();
            if (r != null && discMat != null) r.sharedMaterial = discMat;

            disc.AddComponent<PAO.StickySurface>();

            Rigidbody body = disc.AddComponent<Rigidbody>();
            body.mass = 30f;
            body.drag = 0.3f;
            body.angularDrag = 0.25f;

            HingeJoint hinge = disc.AddComponent<HingeJoint>();
            hinge.axis = Vector3.up;
            hinge.useLimits = false;
            return 1;
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 机关收尾第一步：删掉没有因果链的纯装饰（风车/旋转门/摆锤），
    /// 第二步：把压力板和它驱动的闸门拉近到 3 米内，并用一根可见连杆连起来。
    /// </summary>
    public static class WengjieMechanismFix
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 机关：删装饰 + 连因果", priority = 118)]
        public static void Fix()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int removed = 0;
            string[] deadPrefixes = { "Wheel_", "Door_", "Pendulum_" };
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    for (int p = 0; p < deadPrefixes.Length; p++)
                    {
                        if (!all[i].name.StartsWith(deadPrefixes[p], System.StringComparison.Ordinal)) continue;
                        Object.DestroyImmediate(all[i].gameObject);
                        removed++;
                        break;
                    }
                }
            }

            // 压力板：闸门拉近到 3 米 + 可见连杆
            Material rodMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");
            int connected = 0;

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (!all[i].name.StartsWith("Plate_", System.StringComparison.Ordinal)) continue;

                    Transform gate = all[i].Find("LinkedGate");
                    if (gate == null) continue;

                    gate.localPosition = new Vector3(3f, -0.4f, 0f);

                    if (all[i].Find("LinkRod") == null)
                    {
                        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        rod.name = "LinkRod";
                        rod.transform.SetParent(all[i], false);
                        rod.transform.localPosition = new Vector3(1.5f, 0.6f, 0f);
                        rod.transform.localScale = new Vector3(3f, 0.25f, 0.25f);
                        DestroyCollider(rod);
                        Renderer rr = rod.GetComponent<Renderer>();
                        if (rr != null && rodMat != null) rr.sharedMaterial = rodMat;
                    }
                    connected++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 机关收尾：删掉纯装饰 " + removed + " 件（风车/旋转门/摆锤），压力板 " + connected + " 处闸门拉到 3 米内并加了可见连杆");
        }

        private static void DestroyCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 第三步：视觉语言。三类机关三种颜色，玩家一眼能分出「这个是干什么的」。
    ///   可压 / 可站  → 深石板 + 橘色边框
    ///   会动 / 可转  → 橘色轴心
    ///   可粘        → 浮粘泡同色
    /// </summary>
    public static class WengjieMechanismVisuals
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kDir = "Assets/GameMain/Art/Materials/";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 机关：视觉语言", priority = 119)]
        public static void Paint()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            Material stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Ground.mat");
            Material accent = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_Bomb.mat");
            Material sticky = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameMain/Art/WhiteboxMaterials/WB_FloatSticky.mat");
            if (stone == null) { Debug.LogError("[Wengjie] 找不到白盒材质。"); return; }

            Material plateMat = MakeVariant(stone, "M_Mech_Plate", new Color(0.24f, 0.28f, 0.33f, 1f));
            Material moveMat = MakeVariant(stone, "M_Mech_Move", new Color(0.95f, 0.45f, 0.18f, 1f));

            int painted = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n = all[i].name;

                    // 可压 / 可站：踏板、板面、底座用深石板
                    if (n == "Plate" || n == "Pad" || n == "Beam")
                    {
                        SetMat(all[i], plateMat);
                        painted++;
                    }
                    // 会动 / 可转：轴心、连杆用橘色
                    else if (n == "Pivot" || n == "LinkRod" || n == "Hub" || n == "Drum")
                    {
                        SetMat(all[i], moveMat);
                        painted++;
                    }
                    // 可粘：粘座用粘泡色
                    else if (n == "Disc" && sticky != null && all[i].GetComponent<PAO.StickySurface>() != null)
                    {
                        SetMat(all[i], sticky);
                        painted++;
                    }
                    // 其余机关件回到石材，不要再用白盒蓝
                    else if (n == "Frame" || n == "Base" || n == "Cylinder" || n == "Cylinder" || n == "Arm")
                    {
                        SetMat(all[i], stone);
                        painted++;
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 机关视觉语言已上：" + painted + " 个部件（深石板=可压 / 橘色=会动 / 粘泡色=可粘）");
        }

        private static void SetMat(Transform tr, Material mat)
        {
            if (mat == null) return;
            Renderer r = tr.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        private static Material MakeVariant(Material src, string name, Color color)
        {
            string path = kDir + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            m = new Material(src);
            m.name = name;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 删掉所有挡视野的建筑体块：
    ///   Wengjie_Arch（Map5 抓的拱门/柱/体块）
    ///   Mood_Arch_* / Mood_Column_*（海草那批里混进来的建筑）
    /// 只保留贴墙的石壁与装饰，不动地形和机关。
    /// </summary>
    public static class WengjieRemoveBlockers
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 删掉挡视野的建筑", priority = 120)]
        public static void Remove()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int removed = 0;

            GameObject archRoot = GameObject.Find("Wengjie_Arch");
            if (archRoot != null)
            {
                removed += archRoot.transform.childCount;
                Object.DestroyImmediate(archRoot);
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    string n = all[i].name;
                    if (!n.StartsWith("Mood_Arch", System.StringComparison.Ordinal)
                        && !n.StartsWith("Mood_Column", System.StringComparison.Ordinal)) continue;

                    Object.DestroyImmediate(all[i].gameObject);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 挡视野建筑已清空：" + removed + " 件（Wengjie_Arch + Mood_Arch/Mood_Column）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 视线检查：沿主路取点，逐段向前打射线，
    /// 报告「挡在玩家和下一段之间」的物体。
    /// 标准：站在主路任一点，必须能看见前方 25 米内的路。
    /// </summary>
    public static class WengjieSightCheck
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 视线检查", priority = 121)]
        public static void Check()
        {
            EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            var path = new List<Vector3>();
            Vector3 cA = new Vector3(0f, 0f, 45f);
            Vector3 cB = new Vector3(0f, 0f, 110f);

            // 河岸
            for (float z = -30f; z <= 0f; z += 5f) path.Add(new Vector3(0f, 0f, z));

            // 弧形坡（半径 45，240°）
            for (int k = 0; k <= 12; k++)
            {
                float ang = (-90f + k * 20f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(ang) * 45f, k * 1f, 45f + Mathf.Sin(ang) * 45f));
            }

            // 桥 → 环 → 街市
            path.Add(new Vector3(0f, 12f, 79f));
            path.Add(new Vector3(0f, 12f, 85f));
            for (int k = 0; k <= 12; k++)
            {
                float ang = (22.5f + k * 22.5f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(ang) * 34f, 12f, 110f + Mathf.Sin(ang) * 34f));
            }

            // 楼梯 → 井台 → 塔顶
            path.Add(new Vector3(0f, 26f, 148f));
            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(ang) * 24f, 38f + i * 4f, 200f + Mathf.Sin(ang) * 24f));
            }
            path.Add(new Vector3(0f, 64.6f, 200f));

            var hits = new Dictionary<string, int>();
            int blocked = 0, total = 0;

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = path[i] + Vector3.up * 1.6f;
                Vector3 b = path[i + 1] + Vector3.up * 1.6f;
                float dist = Vector3.Distance(a, b);
                if (dist < 0.5f) continue;

                total++;
                RaycastHit[] found = Physics.RaycastAll(a, (b - a).normalized, dist, ~0, QueryTriggerInteraction.Ignore);
                if (found.Length == 0) continue;

                blocked++;
                for (int h = 0; h < found.Length; h++)
                {
                    string n = found[h].collider.gameObject.name;
                    if (n == "takoyaki" || found[h].collider.GetComponent<PAO.PlayerController>() != null) continue;

                    if (!hits.ContainsKey(n)) hits[n] = 0;
                    hits[n]++;
                }
            }

            var sorted = new List<KeyValuePair<string, int>>(hits);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            var sb = new System.Text.StringBuilder();
            sb.Append("[视线检查] 路径段 ").Append(total).Append("，被挡 ").Append(blocked).Append(" 段\n");
            for (int i = 0; i < sorted.Count && i < 15; i++)
            {
                sb.Append("  ").Append(sorted[i].Key).Append(" ×").Append(sorted[i].Value).Append("\n");
            }

            Debug.Log(sb.ToString());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 让环境「通透」：
    ///   1 清掉离主路 8 米以内的海草（它们贴在镜头前，把纵深糊死了）
    ///   2 雾再压薄一档，远处建筑才透得出来
    /// </summary>
    public static class WengjieClarity
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 环境通透", priority = 122)]
        public static void Clear()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // 主路采样点（和视线检查同一套）
            var path = new List<Vector3>();
            for (float z = -30f; z <= 0f; z += 5f) path.Add(new Vector3(0f, 0f, z));
            for (int k = 0; k <= 12; k++)
            {
                float a = (-90f + k * 20f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(a) * 45f, k, 45f + Mathf.Sin(a) * 45f));
            }
            path.Add(new Vector3(0f, 12f, 79f));
            for (int k = 0; k <= 12; k++)
            {
                float a = (22.5f + k * 22.5f) * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(a) * 34f, 12f, 110f + Mathf.Sin(a) * 34f));
            }
            path.Add(new Vector3(0f, 26f, 148f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                path.Add(new Vector3(Mathf.Cos(a) * 24f, 38f + i * 4f, 200f + Mathf.Sin(a) * 24f));
            }

            int removed = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (!all[i].name.StartsWith("Mood_Plant", System.StringComparison.Ordinal)) continue;

                    Vector3 pos = all[i].position;
                    float best = float.MaxValue;
                    for (int k = 0; k < path.Count; k++)
                    {
                        float d = Vector3.Distance(new Vector3(pos.x, path[k].y, pos.z), path[k]);
                        if (d < best) best = d;
                    }

                    if (best < 8f)
                    {
                        Object.DestroyImmediate(all[i].gameObject);
                        removed++;
                    }
                }
            }

            RenderSettings.fogDensity = 0.0035f;
            RenderSettings.fogColor = new Color(0.22f, 0.42f, 0.52f, 1f);
            RenderSettings.ambientIntensity = 0.85f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 通透处理：清掉主路 8 米内海草 " + removed + " 丛，雾 0.006→0.0035，环境光 0.65→0.85");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 补上全局后处理 Volume。
    /// 场景里原本一个 Volume 都没有 —— 没有 Color Grading / Bloom / Tonemapping，
    /// 所以画面永远像蒙了一层灰，怎么调雾和光都不通透。
    /// 用 Thalassophobia 的 Shallow Water（比 Deep Sea 亮一档）。
    /// </summary>
    public static class WengjiePostFX
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kProfile = "Assets/ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 补后处理", priority = 123)]
        public static void Add()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find("Wengjie_PostFX");
            if (old != null) Object.DestroyImmediate(old);

            UnityEngine.Rendering.VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(kProfile);
            if (profile == null) { Debug.LogError("[Wengjie] 找不到 Profile: " + kProfile); return; }

            var go = new GameObject("Wengjie_PostFX");
            SceneManager.MoveGameObjectToScene(go, scene);

            UnityEngine.Rendering.Volume volume = go.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = go;

            Debug.Log("[Wengjie] 全局后处理已加：Shallow Water Profile（Color Grading / Bloom / Tonemapping 全部生效）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 清理散乱的摆件：删掉 Art_Wall* 与 Art_Arch* （贴在墙上的零碎石片），
    /// 保留地形、铺地、海草和机关。白色石壁本身还在，读路不受影响。
    /// </summary>
    public static class WengjieTidyProps
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 清理散乱摆件", priority = 124)]
        public static void Tidy()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int removed = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    string n = all[i].name;
                    if (!n.StartsWith("Art_Wall", System.StringComparison.Ordinal)
                        && !n.StartsWith("Art_Arch", System.StringComparison.Ordinal)) continue;

                    Object.DestroyImmediate(all[i].gameObject);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 散乱摆件已清：" + removed + " 件（Art_Wall / Art_Arch），保留地形、铺地、海草、机关");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 只留地形：删掉所有 Art_Tile 铺地碎片（7 米见方、随机旋转，是「乱」的主要来源）。
    /// 地形本身有完整的碰撞面，不需要这些贴片。
    /// </summary>
    public static class WengjieTidyTiles
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 清掉所有地砖", priority = 125)]
        public static void Tidy()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            int removed = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (!all[i].name.StartsWith("Art_Tile", System.StringComparison.Ordinal)) continue;
                    Object.DestroyImmediate(all[i].gameObject);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 地砖已清空：" + removed + " 块。现在只剩地形 + 海草 + 机关。");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 收尾两件事：
    ///   1 删掉全部 Art_Tile 地砖（贴片乱的主因）
    ///   2 把 Shallow Water Profile 的调色降一档（饱和度过头导致纯蓝爆掉）
    /// </summary>
    public static class WengjieFinalPolish
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kProfilePath = "Assets/ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 收尾：清地砖 + 降饱和", priority = 126)]
        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // ---------- 1 清地砖 ----------
            int tiles = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    if (!all[i].name.StartsWith("Art_Tile", System.StringComparison.Ordinal)) continue;
                    Object.DestroyImmediate(all[i].gameObject);
                    tiles++;
                }
            }

            // ---------- 2 降饱和 ----------
            int tuned = 0;
            UnityEngine.Rendering.VolumeProfile profile =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(kProfilePath);

            if (profile != null)
            {
                UnityEngine.Rendering.Universal.ColorAdjustments ca;
                if (profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out ca))
                {
                    ca.saturation.Override(-12f);      // 降饱和
                    ca.postExposure.Override(0.15f);   // 略提亮
                    ca.contrast.Override(8f);          // 保一点对比
                    tuned++;
                }

                UnityEngine.Rendering.Universal.Vignette vg;
                if (profile.TryGet<UnityEngine.Rendering.Universal.Vignette>(out vg))
                {
                    vg.intensity.Override(0.22f);
                    tuned++;
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 收尾完成：清地砖 " + tiles + " 块；Profile 调色 " + tuned + " 项（饱和 -12 / 曝光 +0.15 / 对比 8 / 暗角 0.22）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 第二批旁白点位（Story_6..11）+ 配音 vo_6..vo_11。
    /// 位置覆盖桥头 / 街市 / 屋顶 / 井底 / 井中 / 塔前。
    /// </summary>
    public static class WengjieStory2
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_Story2";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 剧情点位 第二批", priority = 127)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);

            object[][] nodes =
            {
                new object[] { new Vector3(0f, 13f, 82f),  "回音", "别停下，它们会记住你。" },
                new object[] { new Vector3(0f, 13f, 125f), "回音", "这里以前有人住。" },
                new object[] { new Vector3(0f, 27f, 135f), "回音", "抬头，路在上面。" },
                new object[] { new Vector3(0f, 35f, 195f), "钟声", "往下没有底。" },
                new object[] { new Vector3(0f, 50f, 200f), "钟声", "水在等你。" },
                new object[] { new Vector3(0f, 64f, 198f), "泡泡", "只剩最后一段。" },
            };

            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 pos = (Vector3)nodes[i][0];
                var go = new GameObject("Story2_" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;

                BoxCollider trig = go.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.size = new Vector3(6f, 4f, 4f);

                PAO.WengjieStoryNode node = go.AddComponent<PAO.WengjieStoryNode>();
                SerializedObject so = new SerializedObject(node);
                so.FindProperty("m_Speaker").stringValue = (string)nodes[i][1];
                so.FindProperty("m_Line").stringValue = (string)nodes[i][2];
                so.FindProperty("m_Delay").floatValue = 0.2f;
                so.FindProperty("m_Once").boolValue = true;

                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/GameMain/Audio/WengjieVO/vo_" + (i + 6) + ".wav");
                if (clip != null) so.FindProperty("m_Voice").objectReferenceValue = clip;

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 第二批旁白已布：" + nodes.Length + " 句（含配音 vo_6..vo_11）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 给「当前选中的平台」加一个向上的弹射气流：站上去就被吹起来。
    /// 先选中目标平台，再跑这个菜单。
    /// </summary>
    public static class WengjieLaunchPad
    {
        [MenuItem("Tools/PAO/关卡白盒/wengjie 给选中平台加弹射", priority = 128)]
        public static void Add()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogError("[Wengjie] 先在 Hierarchy 里选中目标平台。");
                return;
            }

            // 用平台包围盒算出气流范围
            Bounds bounds = new Bounds(selected.transform.position, Vector3.one * 4f);
            Renderer[] rs = selected.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                bounds = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) bounds.Encapsulate(rs[i].bounds);
            }

            var pad = new GameObject("LaunchPad_" + selected.name);
            pad.transform.position = bounds.center + Vector3.up * (bounds.extents.y + 3f);
            pad.transform.SetParent(selected.transform.parent, true);

            BoxCollider trig = pad.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(
                Mathf.Max(3f, bounds.size.x * 0.8f),
                12f,
                Mathf.Max(3f, bounds.size.z * 0.8f));

            var lift = pad.AddComponent<PAO.WhiteboxUpdraft>();
            lift.Configure(14f);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Wengjie] 弹射已加在「" + selected.name + "」上方：气流 14 m/s，范围 " +
                trig.size.x.ToString("F1") + " × 12 × " + trig.size.z.ToString("F1"));
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 转盘类平台的首次语音：踩上 Disc_A/B/C 第一次时喊「飞起来！」。
    /// 触发器直接放在转盘正上方，跟着转盘的水平位置（转盘只绕轴自转，位置不变）。
    /// </summary>
    public static class WengjieStoryDisc
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_StoryDisc";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 转盘首次语音", priority = 129)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameMain/Audio/WengjieVO/vo_12.wav");
            string[] discs = { "Disc_A", "Disc_B", "Disc_C" };
            int built = 0;

            for (int i = 0; i < discs.Length; i++)
            {
                GameObject disc = GameObject.Find(discs[i]);
                if (disc == null) continue;

                var go = new GameObject("StoryDisc_" + discs[i]);
                go.transform.SetParent(root.transform, false);
                go.transform.position = disc.transform.position + Vector3.up * 2f;

                BoxCollider trig = go.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.size = new Vector3(10f, 5f, 10f);

                PAO.WengjieStoryNode node = go.AddComponent<PAO.WengjieStoryNode>();
                SerializedObject so = new SerializedObject(node);
                so.FindProperty("m_Speaker").stringValue = "泡泡";
                so.FindProperty("m_Line").stringValue = "飞起来！";
                so.FindProperty("m_Delay").floatValue = 0.2f;
                so.FindProperty("m_Once").boolValue = true;
                if (clip != null) so.FindProperty("m_Voice").objectReferenceValue = clip;
                so.ApplyModifiedPropertiesWithoutUndo();
                built++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 转盘语音已加：" + built + " 处（Disc_A/B/C 首次踩上喊「飞起来！」）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 批量跳板：主路每段放一个向上的强气流，站上去直接弹飞。
    /// 速度 18 m/s（约弹起 16 米），比默认的 3.4 快五倍。
    /// </summary>
    public static class WengjieJumpPads
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";
        private const string kRoot = "Wengjie_JumpPads";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 批量强跳板", priority = 130)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject old = GameObject.Find(kRoot);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(kRoot);
            SceneManager.MoveGameObjectToScene(root, scene);

            // 主路每段一个：河岸 / 坡道 / 环 / 桥 / 街市 / 屋顶 / 井 / 塔顶
            Vector3[] spots =
            {
                new Vector3(0f, 1f, -10f),
                new Vector3(38f, 7f, 67f),
                new Vector3(0f, 13f, 60f),
                new Vector3(0f, 13f, 79f),
                new Vector3(0f, 13f, 120f),
                new Vector3(0f, 27f, 130f),
                new Vector3(0f, 35f, 200f),
                new Vector3(0f, 65f, 200f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var pad = new GameObject("JumpPad_" + i);
                pad.transform.SetParent(root.transform, false);
                pad.transform.position = spots[i];

                BoxCollider trig = pad.AddComponent<BoxCollider>();
                trig.isTrigger = true;
                trig.size = new Vector3(8f, 14f, 8f);

                var lift = pad.AddComponent<PAO.WhiteboxUpdraft>();
                lift.Configure(18f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log("[Wengjie] 批量跳板已布：" + spots.Length + " 处，气流 18 m/s（约弹起 16 米）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「超级跳」装到所有黑色跳板（Spring_* / Plate_*）上：
    /// 空手踩没反应，只有在弹力泡泡里踩才会被弹飞。
    /// 同时删掉之前那批无条件生效的强气流跳板。
    /// </summary>
    public static class WengjieSuperJumps
    {
        private const string kScenePath = "Assets/GameMain/Scenes/wengjie.unity";

        [MenuItem("Tools/PAO/关卡白盒/wengjie 超级跳（仅弹力泡）", priority = 131)]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            // 摘掉无条件跳板，以及上一轮装错位置的超级跳（转盘上那些）
            GameObject pads = GameObject.Find("Wengjie_JumpPads");
            if (pads != null) Object.DestroyImmediate(pads);

            PAO.WengjieSuperJump[] wrong = Object.FindObjectsByType<PAO.WengjieSuperJump>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < wrong.Length; i++) Object.DestroyImmediate(wrong[i].gameObject);

            int built = 0;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n = all[i].name;
                    bool isPad = n.StartsWith("Spring_", System.StringComparison.Ordinal)
                              || n.StartsWith("Plate_", System.StringComparison.Ordinal)
                              || n.StartsWith("Seesaw_", System.StringComparison.Ordinal);
                    if (!isPad) continue;
                    if (all[i].GetComponent<PAO.WengjieSuperJump>() != null) continue;

                    // 在跳板上方叠一个触发体积
                    var trig = new GameObject("SuperJumpZone");
                    trig.transform.SetParent(all[i], false);
                    trig.transform.localPosition = new Vector3(0f, 1.6f, 0f);

                    BoxCollider box = trig.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = new Vector3(5f, 3.2f, 5f);

                    trig.AddComponent<PAO.WengjieSuperJump>();
                    built++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Wengjie] 超级跳已装：" + built + " 块跳板（仅当玩家在弹力泡泡里才弹飞 26 m/s）");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>把已有超级跳的力度调大：26 → 48 m/s，时长 1.15 → 1.6。</summary>
    public static class WengjieSuperJumpTune
    {
        [MenuItem("Tools/PAO/关卡白盒/wengjie 超级跳加大力度", priority = 132)]
        public static void Tune()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/GameMain/Scenes/wengjie.unity", OpenSceneMode.Single);

            PAO.WengjieSuperJump[] all = Object.FindObjectsByType<PAO.WengjieSuperJump>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                SerializedObject so = new SerializedObject(all[i]);
                so.FindProperty("m_Speed").floatValue = 28f;
                so.FindProperty("m_Cooldown").floatValue = 1.5f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Wengjie] 超级跳改为抛物线：初速 40 m/s（弹起约 80 米），共 " + all.Length + " 块");
        }
    }
}

namespace PAO.EditorTools
{
    /// <summary>把机关平台调成「超级弹」：弹簧极硬、阻尼极低、碰撞弹性 1.0。</summary>
    public static class WengjieSuperBounce
    {
        [MenuItem("Tools/PAO/关卡白盒/wengjie 机关调成超级弹", priority = 133)]
        public static void Make()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/GameMain/Scenes/wengjie.unity", OpenSceneMode.Single);

            PhysicMaterial bouncy = AssetDatabase.LoadAssetAtPath<PhysicMaterial>("Assets/GameMain/Art/PhysicMaterials/M_SuperBouncy.physicMaterial");
            if (bouncy == null)
            {
                bouncy = new PhysicMaterial("M_SuperBouncy");
                bouncy.bounciness = 1f;
                bouncy.bounceCombine = PhysicMaterialCombine.Maximum;
                bouncy.dynamicFriction = 0f;
                bouncy.staticFriction = 0f;
                AssetDatabase.CreateFolder("Assets/GameMain/Art", "PhysicMaterials");
                AssetDatabase.CreateAsset(bouncy, "Assets/GameMain/Art/PhysicMaterials/M_SuperBouncy.physicMaterial");
            }

            int tuned = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                Transform[] all = roots[r].GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (!all[i].name.StartsWith("Plate_", System.StringComparison.Ordinal)
                        && !all[i].name.StartsWith("Spring_", System.StringComparison.Ordinal)
                        && !all[i].name.StartsWith("Seesaw_", System.StringComparison.Ordinal)) continue;

                    ConfigurableJoint[] joints = all[i].GetComponentsInChildren<ConfigurableJoint>(true);
                    for (int j = 0; j < joints.Length; j++)
                    {
                        JointDrive drive = joints[j].yDrive;
                        drive.positionSpring = 5000f;
                        drive.positionDamper = 4f;
                        drive.maximumForce = 30000f;
                        joints[j].yDrive = drive;
                    }

                    Rigidbody[] bodies = all[i].GetComponentsInChildren<Rigidbody>(true);
                    for (int j = 0; j < bodies.Length; j++)
                    {
                        bodies[j].interpolation = RigidbodyInterpolation.Interpolate;
                        bodies[j].collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    }

                    Collider[] cols = all[i].GetComponentsInChildren<Collider>(true);
                    for (int j = 0; j < cols.Length; j++) cols[j].material = bouncy;

                    tuned++;
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Wengjie] 机关已调成超级弹：" + tuned + " 组（弹簧 5000 / 阻尼 4 / 弹性 1.0 / 连续碰撞）");
        }
    }
}
