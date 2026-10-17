using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 必须显式引入 PAO —— 本文件在 namespace PAO.EditorTools 里，
// C# 的子命名空间**不会**自动带入父命名空间下的类型，
// 所以 NarrationTrigger / LayoutVersionStamp / BubblePopObstacle / CollapsingFloor
// 这些 PAO 下的类在这里是找不到的（会报 CS0246）。
using PAO;

// 【注意】这里曾经有一行 using PAO.Underwater; —— 已经删掉。
// 队友在提交 54d66f0「泡泡逻辑的修改，水下效果先删除（要重新做）」里
// 把整个水下系统（WaterVolume / UnderwaterSceneSetup / 后处理）都删了，
// 所以 PAO.Underwater 命名空间现在不存在，留着这行会导致
//     error CS0234: The type or namespace name 'Underwater' does not exist
// 整个游戏程序集编译失败 —— 表现为**所有脚本都不注册**（Add Component 里看不到）。

namespace PAO.EditorTools
{
    /// <summary>
    /// 给「shiliuye map」搭一座**泡泡漂浮爬塔**。
    ///
    /// ============================ 玩法 ============================
    ///   1. 朝上吹一颗【浮粘泡泡】，按 F 钻进去
    ///   2. 泡泡有浮力，带着人一路往上飘
    ///   3. 塔里塞满障碍物 —— **泡泡碰到障碍就破裂**
    ///   4. 泡泡破了，人掉到下面的楼层上
    ///   5. 那个楼层踩上 5 秒后塌掉 —— 必须在它消失前重新起飞
    ///   6. 掉出塔底（y &lt; -12）自动送回出生点
    ///
    /// 【为什么塔是"窄井"而不是之前那种宽塔】
    /// 上一版是 24m 见方的宽塔，靠跳跃/弹力在层间移动。
    /// 这一版是靠泡泡飘升，玩家在泡泡里只能小幅横移 ——
    /// 井道太宽就变成"飘上去了但碰不到墙、也躲不开东西"，
    /// 障碍和路线都失去意义。所以收窄到 12m 见方：
    /// 有横向余量可以闪避，又不至于失控。
    ///
    /// 【和 Falcom 的关系】
    /// 结构母题（垂直塔 + 逐层阻碍 + 越往上越难）致敬伊苏的达姆之塔，
    /// 但**地形与障碍全部是原创白盒**，不复制任何具体游戏的地图或美术资源。
    ///
    /// 菜单：Tools → PAO → 关卡白盒 → 搭建 shiliuye map（爬塔）
    /// 幂等：重复执行会先删掉上次生成的根物体再重建。
    /// </summary>
    public static class ShiliuyeMapBuilder
    {
        private const string kScenePath = "Assets/GameMain/Scenes/shiliuye map.unity";
        private const string kRootName = "Shiliuye_Whitebox";
        private const string kMaterialFolder = "Assets/GameMain/Art/WhiteboxMaterials";

        /// <summary>
        /// 地形版本。**每次改动地形布局都要 +1** ——
        /// 自动钩子靠它判断场景里的是不是旧版，是旧版就重搭。
        /// </summary>
        private const int kLayoutVersion = 10;

        private const string kVersionMarkerName = "_LayoutVersion";

        // ==================== 塔的参数 ====================

        /// <summary>井道内净空的一半（12m 见方）。窄井是这一版的核心决定，见类注释。</summary>
        private const float kShaftHalf = 6f;

        /// <summary>
        /// 层高。
        ///
        /// 【为什么从 9 降到 8】
        /// 9m 时每层要飘好一会儿才到顶，节奏拖；
        /// 8m 更接近"一颗泡泡一次的上升距离"，失误后重爬也快。
        /// </summary>
        private const float kFloorHeight = 8f;

        /// <summary>
        /// 层数。
        ///
        /// 【为什么从 12 降到 8】
        /// 原来 12 层 × 9m = 108m，后半段就是同一套障碍换个方向重来，很重复。
        /// 现在 8 层 × 8m = 64m，而且**每层的障碍组合都不一样**
        /// （见 BuildFloorPattern）—— 短而每层有记忆点，比长而重复好。
        /// </summary>
        private const int kFloorCount = 8;

        /// <summary>地板踩上后多久塌（秒）。玩法要求 5 秒。</summary>
        private const float kFloorCollapseDelay = 5f;

        /// <summary>掉到这个高度以下就重生。</summary>
        private const float kKillHeight = -12f;

        // ==================== 材质 ====================

        private static Material m_Ground;
        private static Material m_Edge;
        private static Material m_Void;
        private static Material m_Bounce;
        private static Material m_Sticky;
        private static Material m_Bomb;
        private static Material m_PinkWall;
        private static Material m_Landmark;

        // ==================== 菜单 ====================

        [MenuItem("Tools/PAO/关卡白盒/搭建 shiliuye map（爬塔）", priority = 82)]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) == null)
            {
                EditorUtility.DisplayDialog("搭建爬塔",
                    "找不到场景：\n" + kScenePath, "好");
                return;
            }

            EnsureMaterials();

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject previous = GameObject.Find(kRootName);

            // 先读出"场景里现在是第几版" —— 必须在删掉根物体之前读
            LayoutVersionStamp previousStamp = previous != null
                ? previous.GetComponentInChildren<LayoutVersionStamp>(true)
                : null;
            int previousVersion = previousStamp != null ? previousStamp.Version : 0;

            if (previous != null)
            {
                Object.DestroyImmediate(previous);
            }

            // ---- 一次性清理：把早先那套"水下光照"从场景里拿掉 ----
            //
            // 【为什么需要这一步】
            // 版本 5 曾经把环境光染成深青蓝、开了指数雾、方向光也染蓝，
            // 而且是**写进场景**的。现在这一段代码删掉了，所以光靠重搭清不掉 ——
            // 场景里会一直留着那套发闷的光。
            //
            // 【为什么条件写 < 8 而不是 < 6】
            // 版本 6 / 7 只是我改了常量，**场景里不一定真的搭过那两版**
            // （自动重搭只在"Unity 重新编译 + 打开该场景"时才跑）。
            // 如果场景里还停在版本 5，写 < 6 是能匹配的；但万一它已经是 6 或 7、
            // 而光照仍是旧的那套，< 6 就漏过去了，画面会一直发闷。
            // 所以阈值放到 8（当前版本），本次一定会清一次；下一次就是 9 了，
            // 条件自然不再成立，不会再碰光照。
            if (previousVersion > 0 && previousVersion < 8)
            {
                RestoreNeutralLighting();
                RemoveUnderwaterObject();
            }

            var root = new GameObject(kRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            BuildTower(root.transform);

            // 记下这次是哪个版本搭的
            var marker = new GameObject(kVersionMarkerName);
            marker.transform.SetParent(root.transform, false);
            var stamp = marker.AddComponent<LayoutVersionStamp>();
            stamp.Version = kLayoutVersion;

            // 坠落重生器挂在场景根上（不属于地形，重搭地形也不该把它删掉）
            EnsureFallRespawn(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EnsureInBuildSettings();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            Debug.Log("[ShiliuyeMap] 爬塔已搭建并保存。玩法：吹浮粘泡泡 → 按 F 钻进去 → " +
                      "飘升躲障碍 → 泡泡一碰就破 → 掉到楼层上 → 5 秒内必须重新起飞。");
        }

        // ==================== 塔体 ====================

        private static void BuildTower(Transform root)
        {
            BuildBase(root);
            BuildShaft(root);
            BuildClimb(root);

            // BuildDecor(root);   ← 装饰层撤回（原因见 BuildDecor 的注释）。
            //                       想恢复就把这行注释去掉、并把 kLayoutVersion +1。

            BuildNarration(root);
            BuildSpawnPoint(root);
            BuildUnderwater();
        }

        // ==================== 装饰 ====================
        //
        // 【当前状态：已撤回，没有被调用】
        //
        // 铺过一版海草（80 丛），整体缩放 1.6 是**估的** ——
        // FBX 的真实尺寸要靠 Mesh.bounds 读，那是运行时数据，搭场景时拿不到。
        // 结果就是尺寸很可能不对，而我没法在本地验证。
        //
        // 所以这一层先撤掉。方法体留着（路径、去碰撞、随机缩放这些都写好了），
        // 想恢复时：
        //   1. 把 BuildTower 里那行 BuildDecor(root); 的注释去掉
        //   2. kLayoutVersion + 1（不然自动钩子不会重搭）
        //   3. **先在 Scene 视图里确认 kDecorScale 合不合适**
        //
        // 【素材来源】
        // 用的是工程里 L4 关卡那套现成 prefab，不是我新建的：
        //   L4海草1/Deep Kelp 3.prefab      深海带（最高）
        //   L4海草2/Kelp 1.prefab           海带
        //   L4草地/Green Sea Grass 1/2.prefab  海草（矮丛）
        // 它们自带材质和摆动着色器（Stylized Kelp / Stylized Grass）。

        /// <summary>装饰根物体的名字。单独一层，方便一键隐藏/删掉。</summary>
        private const string kDecorName = "Shiliuye_Decor";

        /// <summary>
        /// 植物的整体缩放基准。
        ///
        /// 【为什么要单独一个常量】
        /// 这几个 FBX 的导入缩放是 1，但真实尺寸我没法从工程里直接读出来
        /// （要走 Mesh.bounds，而那是运行时数据）。所以先给一个保守值，
        /// **你在 Scene 视图里看一眼，觉得太大/太小就改这一个数**。
        /// </summary>
        private const float kDecorScale = 1.6f;

        /// <summary>每层放几丛植物。</summary>
        private const int kDecorPerFloor = 7;

        private static readonly string[] kDecorPrefabPaths =
        {
            "Assets/GameMain/Art/Materials/L4海草1/Deep Kelp 3.prefab",
            "Assets/GameMain/Art/Materials/L4海草2/Kelp 1.prefab",
            "Assets/GameMain/Art/Materials/L4草地/Green Sea Grass 1.prefab",
            "Assets/GameMain/Art/Materials/L4草地/Green Sea Grass 2.prefab",
        };

        private static void BuildDecor(Transform root)
        {
            // 先清掉上次生成的 —— 单独一层，重搭地形时不会顺带删掉它
            GameObject previous = GameObject.Find(kDecorName);

            if (previous != null)
            {
                Object.DestroyImmediate(previous);
            }

            // 把 prefab 读出来。缺任何一个都只跳过它，不整个失败 ——
            // 美术以后挪了目录也不至于让地形搭不出来。
            var prefabs = new System.Collections.Generic.List<GameObject>();

            for (int i = 0; i < kDecorPrefabPaths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kDecorPrefabPaths[i]);

                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            if (prefabs.Count == 0)
            {
                Debug.LogWarning("[ShiliuyeMap] 一个装饰 prefab 都没找到，跳过装饰层。" +
                                 "路径可能被挪过，看一下 kDecorPrefabPaths。");
                return;
            }

            var decorRoot = new GameObject(kDecorName);
            decorRoot.transform.SetParent(root, false);

            int placed = 0;

            // 沿塔高一路铺上去
            for (int floor = 1; floor <= kFloorCount; floor++)
            {
                float y = floor * kFloorHeight;

                for (int i = 0; i < kDecorPerFloor; i++)
                {
                    GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];

                    // 贴在墙上、贴着楼板 —— 这样才不挡泡泡的上升通道。
                    // 用一个略小于半宽的半径，让根部埋进墙里一点，看着是长出来的。
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float radius = kShaftHalf - Random.Range(0.3f, 1.6f);

                    Vector3 position = new Vector3(
                        Mathf.Cos(angle) * radius,
                        // 略低于楼板顶面，根部埋进去
                        y - Random.Range(0f, 0.5f),
                        Mathf.Sin(angle) * radius);

                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab, decorRoot.transform) as GameObject;

                    if (instance == null)
                    {
                        continue;
                    }

                    instance.transform.position = position;
                    instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                    float s = kDecorScale * Random.Range(0.8f, 1.45f);
                    instance.transform.localScale = new Vector3(s, s, s);

                    // 植物不该挡住泡泡 —— 把碰撞体全去掉。
                    // 不然玩家会撞在看不见的植物上，比白盒更糟。
                    Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);

                    for (int c = 0; c < colliders.Length; c++)
                    {
                        Object.DestroyImmediate(colliders[c]);
                    }

                    placed++;
                }
            }

            // 塔底外围也铺一圈，让"从外面看"不空
            for (int i = 0; i < 24; i++)
            {
                GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = kShaftHalf + Random.Range(1.5f, 5f);

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, decorRoot.transform) as GameObject;

                if (instance == null)
                {
                    continue;
                }

                instance.transform.position = new Vector3(
                    Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                instance.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                float s = kDecorScale * Random.Range(0.8f, 1.45f);
                instance.transform.localScale = new Vector3(s, s, s);

                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);

                for (int c = 0; c < colliders.Length; c++)
                {
                    Object.DestroyImmediate(colliders[c]);
                }

                placed++;
            }

            Debug.Log("[ShiliuyeMap] 装饰层已铺好：" + placed + " 丛植物（" + kDecorName + "）。" +
                      "整体缩放 " + kDecorScale + " —— 觉得太大/太小就改 kDecorScale。");
        }

        // ==================== 海底氛围（已移除）====================

        /// <summary>
        /// 这里曾经做过一套水下氛围：环境光染青蓝 + 指数雾 + WaterVolume 后处理滤镜。
        ///
        /// **两件事让它彻底删掉了：**
        ///
        ///   1. 观感不对。三层叠加之后画面发暗发闷，障碍物看不清 ——
        ///      爬塔本来就要靠眼睛找横梁和转杆的缝，把光压下去等于和玩法作对。
        ///      所以先退掉了光照改动，后来连滤镜也撤了。
        ///
        ///   2. 队友把整个水下系统删了（提交 54d66f0
        ///      「泡泡逻辑的修改，水下效果先删除（要重新做）」）。
        ///      `WaterVolume` / `UnderwaterSceneSetup` / 后处理机关全没了，
        ///      脚本里再引用 `PAO.Underwater` 会直接编译失败。
        ///
        /// 【以后想加水下效果】
        /// 等队友把新的水下系统做完，再用他们的方案 —— 不要再自己搭一套。
        /// 这一关**只保留光照的清理动作**（见 Build() 里那句一次性恢复），
        /// 不主动配置任何环境光 / 雾 / 后处理。
        /// </summary>
        private static void BuildUnderwater()
        {
            // 空实现。保留这个方法和它的调用，是为了让 BuildTower() 的流程
            // 读起来仍然完整（基座 → 井道 → 攀爬 → 旁白 → 出生点 → 氛围），
            // 以后要加水下效果时直接往这里填就行。
        }

        /// <summary>
        /// 把光照恢复成 Unity 的默认值，清掉早先那套"水下环境光 + 雾"。
        ///
        /// **只在 Build() 里"从版本 5 及更早升上来"时自动跑一次**（见那边的说明）。
        /// 不要在别处顺手调它 —— 它会把美术手调的环境光和雾一起清掉。
        /// </summary>
        private static void RestoreNeutralLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientSkyColor = new Color(0.212f, 0.227f, 0.259f);
            RenderSettings.ambientEquatorColor = new Color(0.114f, 0.125f, 0.133f);
            RenderSettings.ambientGroundColor = new Color(0.047f, 0.043f, 0.035f);

            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.fogDensity = 0.01f;

            Light sun = Object.FindFirstObjectByType<Light>();

            if (sun != null && sun.type == LightType.Directional)
            {
                sun.color = Color.white;
                sun.intensity = 1f;
            }

            Debug.Log("[ShiliuyeMap] 已把环境光 / 雾 / 方向光恢复成默认值。");
        }

        /// <summary>
        /// 删掉场景里那个 <c>Underwater</c> 物体（水面 + WaterVolume）。
        ///
        /// 和水下效果一起撤掉。它**不属于地形根物体**，所以重搭地形不会顺带删它 ——
        /// 得单独处理，否则会留一个孤零零的 WaterVolume 在场景里继续推全局变量，
        /// 画面还是蒙着一层青。
        /// </summary>
        private static void RemoveUnderwaterObject()
        {
            GameObject underwater = GameObject.Find("Underwater");

            if (underwater == null)
            {
                return;
            }

            Object.DestroyImmediate(underwater);
            Debug.Log("[ShiliuyeMap] 已移除场景里的 Underwater 物体（水下滤镜 / 水面）。");
        }

        // 【这里原来有 ApplyWaterSettings(WaterVolume, float) —— 已删除】
        //
        // 它是配合 WaterVolume 用的（水面高度、水下滤镜强度、雾浓度、焦散、体积光
        // 那一整套参数）。队友删掉水下系统之后 WaterVolume 类型不存在了，
        // 留着这个方法会让整个程序集编译失败。
        //
        // 参数本身记在这里，免得以后重做时要重新调一遍（那轮调到最后的值）：
        //   水面高度   塔顶 + 12m
        //   水面尺寸   90m（比 12m 的井道大得多，抬头看不到边缘）
        //   滤镜强度   0.38（默认 0.65 —— 压淡过，否则画面蒙一层青、障碍看不清）
        //   浅处水色   (0.50, 0.78, 0.84)
        //   深处水色   (0.12, 0.32, 0.44)
        //   雾浓度     0.022（从 0.045 砍半 —— 12m 井道里 0.045 对面墙就发灰）
        //   深度变暗   1.15
        //   焦散       开，强度 0.95，缩放 3.6，速度 0.5，颜色 (0.80, 1.00, 0.97)
        //   体积光     开，强度 1.5，方向 (-0.2, 1, -0.3)

        /// <summary>
        /// 出生点。塔底正中央 —— 玩家一睁眼就是在井道里往上看。
        /// 装玩家的工具会读这个位置，装完会把它删掉。
        /// </summary>
        private static void BuildSpawnPoint(Transform root)
        {
            var spawn = new GameObject("SpawnPoint");
            spawn.transform.SetParent(root, false);
            spawn.transform.position = new Vector3(0f, 1.2f, 0f);
            spawn.transform.rotation = Quaternion.identity;
        }

        // ==================== 旁白 ====================
        //
        // 语调照着剧本里「编织者」那套写：他知道自己在"编织"这个世界，
        // 却记不清编了多少遍；对玩家是半哄半坦白，时不时说漏嘴。
        // 说话人留空（HUD 上不显示名字），符合"旁白是画外音"的定位。

        private static void BuildNarration(Transform root)
        {
            Trigger(root, "Narration_Start", new Vector3(0f, 3f, 0f),
                "往上吹一颗浮粘泡泡，然后钻进去。\n它会带你上去——它天生就想往上走。",
                6.5f);

            Trigger(root, "Narration_FirstHazard", new Vector3(0f, kFloorHeight + 3f, 0f),
                "塔里的东西别去碰。\n泡泡一碰就破，你就下来了。",
                6f);

            Trigger(root, "Narration_FirstLanding", new Vector3(0f, kFloorHeight * 2f + 3f, 0f),
                "摔下来的话，脚下那块板能接住你。\n但只接五秒——五秒之后它自己也要走。",
                7f);

            Trigger(root, "Narration_Midway", new Vector3(0f, kFloorHeight * 6f + 3f, 0f),
                "爬到一半了。\n从这里开始，那些杆子会转。等它转开再上。",
                6.5f);

            Trigger(root, "Narration_High", new Vector3(0f, kFloorHeight * 9f + 3f, 0f),
                "越往上越挤。\n我不是故意刁难你——是塔本身就这么修上来的。",
                6.5f);

            Trigger(root, "Narration_Top", new Vector3(0f, kFloorHeight * kFloorCount + 4f, 0f),
                "到顶了。\n这一次你是一路飘上来的，我知道。",
                6f);
        }

        /// <summary>
        /// 建一个旁白触发区。用扁平的 Trigger 而不是大立方体 ——
        /// 井道只有 12m 见方，触发区太大会几层一起触发。
        /// </summary>
        private static void Trigger(Transform root, string name, Vector3 position,
                                    string text, float delay)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(kShaftHalf * 2f, 3f, kShaftHalf * 2f);
            box.center = Vector3.zero;

            go.AddComponent<NarrationTrigger>().Configure("", text, 0f, delay);
        }

        /// <summary>塔基与出生点。</summary>
        private static void BuildBase(Transform root)
        {
            // 塔底地面：一整块，玩家开局站这里
            Box(root, "Tower_Ground", new Vector3(0f, -0.5f, 0f),
                new Vector3(kShaftHalf * 2f + 6f, 1f, kShaftHalf * 2f + 6f), m_Ground);

            // 塔底外围的视觉深渊（无碰撞）
            Box(root, "Void_Backdrop",
                new Vector3(0f, kKillHeight - 4f, 0f), new Vector3(70f, 0.3f, 70f), m_Void, false);

            // 四角立柱，把塔的轮廓拉出来
            float c = kShaftHalf + 1.5f;
            float wallH = kFloorCount * kFloorHeight;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Box(root, "CornerPillar_" + (sx > 0 ? "E" : "W") + (sz > 0 ? "N" : "S"),
                        new Vector3(sx * c, wallH * 0.5f, sz * c),
                        new Vector3(3f, wallH, 3f), m_Landmark);
                }
            }

            // 出生点地标（给一进场的方位感）
            Box(root, "Base_Landmark", new Vector3(0f, 0.6f, -kShaftHalf - 2f),
                new Vector3(6f, 1.2f, 1.5f), m_Landmark);
        }

        /// <summary>
        /// 井道四壁。每层的墙在**不同侧面**开一个洞 ——
        /// 那就是玩家绕开障碍的路线，也是这一版"塔壁洞口"这个元素的落点。
        /// </summary>
        private static void BuildShaft(Transform root)
        {
            const float thickness = 0.8f;

            for (int floor = 0; floor < kFloorCount; floor++)
            {
                float bottom = floor * kFloorHeight;
                float top = bottom + kFloorHeight;

                // 洞开在哪一面：跟着层数绕圈，玩家每层都要往不同方向绕
                int openingSide = floor % 4;

                for (int side = 0; side < 4; side++)
                {
                    // 每面墙只在"一段层高"内生成，方便开洞
                    Vector3 outward = OutwardOf(side);
                    float midY = (bottom + top) * 0.5f;

                    if (side == openingSide)
                    {
                        // 这一面开一个 4m 宽、5m 高的洞
                        BuildWallWithOpening(root, "Wall_F" + floor + "_" + SideName(side),
                            outward, bottom, top, thickness, 4f, 5f);
                    }
                    else
                    {
                        // 整面墙
                        bool alongX = (side == 0 || side == 2);
                        Vector3 size = alongX
                            ? new Vector3(kShaftHalf * 2f + thickness, kFloorHeight, thickness)
                            : new Vector3(thickness, kFloorHeight, kShaftHalf * 2f + thickness);

                        Box(root, "Wall_F" + floor + "_" + SideName(side),
                            outward * (kShaftHalf + thickness * 0.5f) + Vector3.up * midY,
                            size, m_Edge);
                    }

                    // 洞边加一圈亮色框，让洞口在灰墙里一眼可见
                    if (side == openingSide)
                    {
                        Vector3 framePos = outward * kShaftHalf + Vector3.up * midY;

                        Box(root, "WallOpening_F" + floor + "_" + SideName(side),
                            framePos, new Vector3(4.4f, 0.35f, 0.35f), m_Bounce);
                    }
                }
            }
        }

        /// <summary>
        /// 一面积了洞的墙：拆成"左、右、下、上"四块。
        /// 用四块方盒拼出一个洞，比用布尔运算或自定义网格简单得多，
        /// 而且碰撞体是现成的。
        /// </summary>
        private static void BuildWallWithOpening(Transform root, string name, Vector3 outward,
                                                 float bottom, float top, float thickness,
                                                 float openingWidth, float openingHeight)
        {
            bool alongX = (Mathf.Abs(outward.x) < 0.01f);
            float wallLength = kShaftHalf * 2f + thickness;
            float sideWidth = (wallLength - openingWidth) * 0.5f;
            float sideOffset = openingWidth * 0.5f + sideWidth * 0.5f;

            float wallY = (bottom + top) * 0.5f;
            float planeBase = kShaftHalf + thickness * 0.5f;

            // 洞在竖直方向的位置：偏下一点，玩家飘升时正好从洞里穿过
            float openingCenterY = bottom + kFloorHeight * 0.45f;
            float openingBottom = openingCenterY - openingHeight * 0.5f;
            float openingTop = openingCenterY + openingHeight * 0.5f;

            Vector3 basePos = outward * planeBase;

            // 左右两块（沿着墙的方向错开）
            Vector3 lateral = alongX ? Vector3.right : Vector3.forward;

            AddWallPiece(root, name + "_L", basePos - lateral * sideOffset + Vector3.up * wallY,
                alongX, sideWidth, kFloorHeight, thickness);
            AddWallPiece(root, name + "_R", basePos + lateral * sideOffset + Vector3.up * wallY,
                alongX, sideWidth, kFloorHeight, thickness);

            // 洞下方
            float lowerHeight = openingBottom - bottom;
            if (lowerHeight > 0.05f)
            {
                AddWallPiece(root, name + "_Below",
                    basePos + Vector3.up * (bottom + lowerHeight * 0.5f),
                    alongX, openingWidth, lowerHeight, thickness);
            }

            // 洞上方
            float upperHeight = top - openingTop;
            if (upperHeight > 0.05f)
            {
                AddWallPiece(root, name + "_Above",
                    basePos + Vector3.up * (openingTop + upperHeight * 0.5f),
                    alongX, openingWidth, upperHeight, thickness);
            }
        }

        private static void AddWallPiece(Transform root, string name, Vector3 position,
                                         bool alongX, float width, float height, float thickness)
        {
            Vector3 size = alongX
                ? new Vector3(width, height, thickness)
                : new Vector3(thickness, height, width);

            Box(root, name, position, size, m_Edge);
        }

        // ==================== 攀爬层 ====================

        /// <summary>
        /// 一层一层往上摆：可塌地板 + 这一层独有的障碍组合。
        ///
        /// 【为什么改成"每层一个模式"】
        /// 之前是无脑循环：每层都是 错位横梁 + 旋转杆 + 尖刺 +
        /// 洞口方位按 4 取模 —— 于是第 1/5/9 层长得一模一样，第 2/6/10 层也一样。
        /// 爬过半座塔之后玩家就只是在重复同一套动作，很腻。
        ///
        /// 现在 1~3 层保持"一次只教一种"（不改），4 层往上每层给一个**不同的组合**，
        /// 见 <see cref="BuildFloorPattern"/>。
        /// </summary>
        private static void BuildClimb(Transform root)
        {
            for (int floor = 1; floor <= kFloorCount; floor++)
            {
                float y = floor * kFloorHeight;

                // ---- 落脚地板：踩上 5 秒后塌 ----
                // 偏在一侧（不完全封死井道），留出泡泡上升的通道
                int side = floor % 4;
                Vector3 outward = OutwardOf(side);
                bool alongX = (side == 0 || side == 2);

                Box(root, "Landing_F" + floor,
                    outward * (kShaftHalf * 0.45f) + Vector3.up * (y - 0.4f),
                    alongX ? new Vector3(7f, 0.8f, 5f) : new Vector3(5f, 0.8f, 7f),
                    m_Ground).AddComponent<CollapsingFloor>().Configure(kFloorCollapseDelay);

                // ---- 这一层独有的障碍组合 ----
                BuildFloorPattern(root, floor, y, side, outward);

                // ---- 层号标记：贴在墙上，给玩家"爬到第几层"的读感 ----
                Box(root, "FloorMark_F" + floor,
                    outward * (kShaftHalf - 0.2f) + Vector3.up * (y + 2.2f),
                    alongX ? new Vector3(0.4f, 0.25f, 1.2f) : new Vector3(1.2f, 0.25f, 0.4f),
                    floor % 3 == 0 ? m_PinkWall : m_Landmark);
            }
        }

        /// <summary>
        /// 每层的障碍配方。
        ///
        /// 【1~3 层刻意不动】
        /// 那三层是教学：一次只出现一种障碍，让玩家分别摸清
        /// "横梁要绕""转杆要等时机""尖刺不能贴墙走"。改动它们会破坏教学节奏。
        ///
        /// 【4 层往上各有各的记忆点】
        ///   4：旋转杆当**唯一**障碍物，但转速快、贴得低 —— 考纯时机
        ///   5：错位横梁 + 贴墙尖刺 —— 考路线，而且这条路每层换边
        ///   6：上下**两道**旋转杆反向转 —— 考节奏，不能只等一次
        ///   7：长横梁 + 中间一根短杆 —— 考"先绕再等"的顺序
        ///   8：塔顶，前四种全上，但每样都只给一份 —— 收尾
        /// </summary>
        private static void BuildFloorPattern(Transform root, int floor, float y,
                                              int side, Vector3 outward)
        {
            switch (floor)
            {
                // ================= 教学层（1~3，保持原样）=================
                case 1:
                    BuildStaggeredBeam(root, floor, y);
                    break;

                case 2:
                    BuildMovingHazard(root, floor, y);
                    break;

                case 3:
                    BuildSpikeCluster(root, floor, y);
                    break;

                // ================= 4 层往上：每层不同 =================
                case 4:
                    // 双转杆：一高一低，反向转 → 中间的缝会左右游走
                    BuildMovingHazard(root, floor, y, heightOffset: 2.4f, speed: 78f, scale: 0.82f);
                    BuildMovingHazard(root, floor, y, heightOffset: 6.0f, speed: -52f, scale: 0.62f);
                    break;

                case 5:
                    // 横梁 + 贴着**另一面墙**的尖刺 → 逼玩家走固定的那条缝
                    BuildStaggeredBeam(root, floor, y);
                    BuildSpikeCluster(root, floor, y, sideOverride: (side + 2) % 4);
                    break;

                case 6:
                    // 只留两道反向转杆，但都在玩家必经的高度上，且间距很窄
                    BuildMovingHazard(root, floor, y, heightOffset: 3.2f, speed: 96f, scale: 0.95f);
                    BuildMovingHazard(root, floor, y, heightOffset: 5.4f, speed: -96f, scale: 0.95f);
                    break;

                case 7:
                    // 长横梁 + 一根短转杆：先绕开梁，再等杆转走 —— 两步顺序
                    BuildStaggeredBeam(root, floor, y, lengthOverride: 9.5f);
                    BuildMovingHazard(root, floor, y, heightOffset: 4.2f, speed: 64f, scale: 0.45f);
                    break;

                case 8:
                    // 塔顶层：四种各来一份，作为收尾
                    BuildStaggeredBeam(root, floor, y);
                    BuildMovingHazard(root, floor, y, heightOffset: 2.8f, speed: 70f, scale: 0.7f);
                    BuildSpikeCluster(root, floor, y, sideOverride: (side + 1) % 4);
                    break;
            }
        }

        /// <summary>
        /// 错位横梁：从墙面伸出来的短梁，交错排布。
        /// 它挡住井道的一部分，玩家在泡泡里只能小幅横移 —— 所以要提前瞄准走位。
        /// </summary>
        /// <param name="lengthOverride">指定长度。&lt;= 0 则按层数自动算（越往上越长）</param>
        private static void BuildStaggeredBeam(Transform root, int floor, float y,
                                               float lengthOverride = -1f)
        {
            // 每层换一个方向伸出来，形成交错
            int side = (floor + 2) % 4;
            Vector3 outward = OutwardOf(side);
            bool alongX = (side == 0 || side == 2);

            // 横梁长度：一层比一层长（越往上越难绕）；显式给了就用给的值
            float length = lengthOverride > 0f
                ? lengthOverride
                : Mathf.Lerp(5f, 9f, Mathf.Clamp01((floor - 1f) / kFloorCount));

            // 伸出来的一端贴着墙，另一端朝井道中心
            Vector3 position = outward * (kShaftHalf - length * 0.5f) + Vector3.up * (y + 3.2f);

            Vector3 size = alongX
                ? new Vector3(length, 0.7f, 1.8f)
                : new Vector3(1.8f, 0.7f, length);

            GameObject beam = Box(root, "Beam_F" + floor, position, size, m_PinkWall);

            // 第二个参数 onlyPopRidden = false → **任何**碰到它的泡泡都会破。
            // 玩家朝上吹泡泡试探时，泡泡撞到横梁就会碎 —— 这是"这里过不去"的反馈。
            beam.AddComponent<BubblePopObstacle>().Configure(false, false);

            // 梁端加一个亮色的"危险"标记
            Box(root, "BeamTip_F" + floor,
                outward * (kShaftHalf - length) + Vector3.up * (y + 3.2f),
                new Vector3(1.1f, 1.1f, 1.1f), m_Bomb);
        }

        /// <summary>
        /// 移动障碍：横梁绕着井道中心**旋转**，扫过整条上升通道。
        /// 玩家得等它转开再往上，或者在它扫过来的间隙里穿过去。
        /// </summary>
        /// <param name="heightOffset">相对层地面的高度（米）</param>
        /// <param name="speed">转速（度/秒）。**负数表示反向转** ——
        /// 同一层放两根反向的，中间的缝就会左右游走，比单纯"等一次"难</param>
        /// <param name="scale">杆的长度/粗细倍率。小 = 短杆，只挡一段</param>
        /// <param name="suffix">名字后缀，同一层放多根时用来区分</param>
        private static void BuildMovingHazard(Transform root, int floor, float y,
                                              float heightOffset = 4.6f, float speed = 0f,
                                              float scale = 1f, string suffix = "")
        {
            string id = "MovingHazard_F" + floor + suffix;

            var pivot = new GameObject(id);
            pivot.transform.SetParent(root, false);
            pivot.transform.position = new Vector3(0f, y + heightOffset, 0f);

            float armLength = kShaftHalf * 1.85f * scale;

            // 横贯井道的长杆，绕 Y 轴扫
            GameObject arm = Box(pivot.transform, "Arm",
                pivot.transform.position,
                new Vector3(armLength, 0.8f * scale, 1.4f * scale), m_Bomb);

            arm.AddComponent<BubblePopObstacle>().Configure(false, false);

            // 没指定转速就按层数自动递增（保持 1~2 层原来的手感）
            float resolvedSpeed = Mathf.Approximately(speed, 0f) ? 42f + floor * 4f : speed;

            var motion = pivot.AddComponent<RotatingHazard>();
            motion.Configure(new Vector3(0f, 1f, 0f), resolvedSpeed);

            // 两端加配重，让"这是一根会扫的杆"更明确
            Sphere(pivot.transform, "Weight_L",
                pivot.transform.position + Vector3.left * (armLength * 0.5f), 1.4f * scale, m_Landmark);
            Sphere(pivot.transform, "Weight_R",
                pivot.transform.position + Vector3.right * (armLength * 0.5f), 1.4f * scale, m_Landmark);
        }

        /// <summary>
        /// 尖刺/珊瑚群：一簇一簇贴在墙上，密集但不封路。
        /// 和横梁的区别是它**不动** —— 考验的是路线记忆，不是时机。
        /// </summary>
        /// <param name="sideOverride">指定贴哪面墙。&lt; 0 则按层数自动选</param>
        private static void BuildSpikeCluster(Transform root, int floor, float y,
                                              int sideOverride = -1)
        {
            int side = sideOverride >= 0 ? sideOverride : (floor + 1) % 4;
            Vector3 outward = OutwardOf(side);

            // 五个尖刺沿墙排开，高低错落
            for (int i = 0; i < 5; i++)
            {
                float t = (i - 2) / 2f;                       // -1 ~ 1
                float height = y + 3f + Mathf.Abs(t) * 2.4f;  // 两边高中间低

                Vector3 lateral = Mathf.Abs(outward.x) < 0.01f ? Vector3.right : Vector3.forward;
                Vector3 position = outward * (kShaftHalf - 1.2f) + lateral * (t * 3.6f)
                                 + Vector3.up * (height - y);

                GameObject spike = Sphere(root, "Spike_F" + floor + "_" + i,
                    position, 1.5f - Mathf.Abs(t) * 0.4f, m_Sticky);

                spike.AddComponent<BubblePopObstacle>().Configure(false, false);
            }
        }

        // ==================== 辅助 ====================

        private static string SideName(int side)
        {
            switch (side)
            {
                case 0: return "N";
                case 1: return "E";
                case 2: return "S";
                default: return "W";
            }
        }

        /// <summary>第 side 面朝外的单位方向：0=北(+Z) 1=东(+X) 2=南(-Z) 3=西(-X)。</summary>
        private static Vector3 OutwardOf(int side)
        {
            switch (side)
            {
                case 0: return Vector3.forward;
                case 1: return Vector3.right;
                case 2: return Vector3.back;
                default: return Vector3.left;
            }
        }

        /// <summary>
        /// 坠落重生。挂在场景根上，不是地形根物体下 ——
        /// 这样重搭地形（会删掉地形根物体）时它还在，不用重复配。
        /// </summary>
        private static void EnsureFallRespawn(Scene scene)
        {
            FallRespawn existing =
                Object.FindFirstObjectByType<FallRespawn>(FindObjectsInactive.Include);

            if (existing != null)
            {
                existing.Configure(kKillHeight, null);
                return;
            }

            var go = new GameObject("Fall Respawn (掉出塔底自动重生)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<FallRespawn>().Configure(kKillHeight, null);
        }

        // ==================== 材质 ====================

        private static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(kMaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/GameMain/Art", "WhiteboxMaterials");
            }

            // 和 LevelWhiteboxSetup 用同名材质，两边共享，改一次两边都变
            m_Ground = GetMaterial("WB_Ground", new Color(0.72f, 0.76f, 0.75f));
            m_Edge = GetMaterial("WB_Platform", new Color(0.55f, 0.65f, 0.66f));
            m_Void = GetMaterial("WB_Void", new Color(0.055f, 0.13f, 0.18f));
            m_Bounce = GetMaterial("WB_Bounce", new Color(0.20f, 0.63f, 0.88f));
            m_Sticky = GetMaterial("WB_FloatSticky", new Color(0.20f, 0.76f, 0.67f));
            m_Bomb = GetMaterial("WB_Bomb", new Color(0.96f, 0.43f, 0.20f));
            m_PinkWall = GetMaterial("WB_Destructible", new Color(0.88f, 0.42f, 0.63f));
            m_Landmark = GetMaterial("WB_Landmark", new Color(0.77f, 0.83f, 0.80f));

            AssetDatabase.SaveAssets();
        }

        /// <summary>已有同名材质就复用，没有才新建 —— 避免重复执行时堆出一堆材质。</summary>
        private static Material GetMaterial(string name, Color color)
        {
            string path = kMaterialFolder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader);
            material.name = name;
            material.color = color;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ==================== 几何辅助 ====================

        private static GameObject Box(Transform parent, string name, Vector3 position,
                                      Vector3 size, Material material, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;

            if (!collider)
            {
                Collider c = go.GetComponent<Collider>();

                if (c != null)
                {
                    Object.DestroyImmediate(c);
                }
            }

            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 position,
                                         float diameter, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // ==================== Build Settings ====================

        private static void EnsureInBuildSettings()
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].path == kScenePath)
                {
                    return;
                }
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(current);
            list.Add(new EditorBuildSettingsScene(kScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[ShiliuyeMap] 已把 " + kScenePath + " 加进 Build Settings。");
        }

        // ==================== 自动执行 ====================

        /// <summary>
        /// 地形版本对不上就自动重搭。
        ///
        /// 【为什么用文件标记而不是 EditorPrefs】
        /// EditorPrefs 的标记跨会话持久，一旦置位就永远不会再执行 ——
        /// 工程里别的构建脚本就因为这个变成"永不再跑"。
        /// 这里记的是"自动搭过哪个版本"，版本号变了会重新执行。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void HookAutoBuild()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    {
                        return;
                    }

                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) == null)
                    {
                        return;
                    }

                    // 只在**用户已经打开**这个场景时动手。
                    // 不主动 OpenScene —— 那会打断手头的工作（丢掉未保存的改动）。
                    Scene active = SceneManager.GetActiveScene();

                    if (active.path != kScenePath)
                    {
                        return;
                    }

                    GameObject root = GameObject.Find(kRootName);
                    LayoutVersionStamp stamp = root != null
                        ? root.GetComponentInChildren<LayoutVersionStamp>(true)
                        : null;

                    int built = stamp != null ? stamp.Version : 0;

                    if (built == kLayoutVersion)
                    {
                        return;   // 已经是最新版本
                    }

                    Debug.Log("[ShiliuyeMap] 地形版本 " + built + " → " + kLayoutVersion + "，正在重搭爬塔…");
                    Build();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[ShiliuyeMap] 自动搭建失败：" + e.Message +
                                   "\n请用菜单 Tools → PAO → 关卡白盒 → 搭建 shiliuye map（爬塔）手动执行。");
                }
            };
        }
    }
}
