using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 必须显式引入 PAO —— 本文件在 namespace PAO.EditorTools 里，
// 子命名空间不会自动带入父命名空间下的类型（PlayerController 在 PAO 下）。
using PAO;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把玩家角色和一台能跟随的第三人称相机装进 shiliuye map。
    ///
    /// 【为什么要装相机枢轴】
    /// 玩家的 <see cref="PlayerController"/> 是"枢轴 + 相机"两级结构的：
    ///   · m_CameraPivot   空物体，脚本把 yaw/pitch 加在它身上
    ///   · m_CameraTransform  枢轴的子物体，脚本只改它的 localPosition.z
    ///                        （= -当前距离），保留 Inspector 里设的 X/Y 偏移
    ///
    /// 如果 m_CameraTransform 留空，就是**第一人称模式** ——
    /// 镜头贴在角色眼睛里，看不见角色本人。
    /// 所以要"看到角色"，必须建这个两级结构。
    ///
    /// 【为什么玩家用预制体实例化，而不是新建 GameObject】
    /// takoyaki.prefab 上已经挂好了玩家需要的全部组件
    /// （PlayerController / BubbleLauncher / BubbleTypeSwitcher / BubbleResourceManager /
    ///   动画驱动 / 表情系统 / 骑乘交互 / 炸弹管理）。
    /// 手工新建一个空物体再逐个挂组件，很容易漏，而且漏了不报错、只是功能静默失效。
    ///
    /// 菜单：**Tools → PAO → 关卡白盒 → 给 shiliuye map 装玩家**
    /// </summary>
    public static class ShiliuyePlayerSetup
    {
        private const string kScenePath = "Assets/GameMain/Scenes/shiliuye map.unity";

        /// <summary>不带 .unity 的场景名 —— 装 HUD 那套流程是按名字校验的。</summary>
        private const string kSceneName = "shiliuye map";
        private const string kPlayerPrefabPath = "Assets/GameMain/Art/Models/takoyaki.prefab";
        private const string kSpawnPointName = "SpawnPoint";

        /// <summary>第三人称相机距离。塔内空间窄，比默认 4.5 稍远一点看全角色。</summary>
        private const float kCameraDistance = 5.5f;

        /// <summary>摄像机相对枢轴的高度偏移，稍微抬高一点看得到脚下。</summary>
        private const float kCameraHeight = 0.6f;

        [MenuItem("Tools/PAO/关卡白盒/给 shiliuye map 装玩家", priority = 84)]
        public static void Install()
        {
            InstallInternal(true);
        }

        /// <summary>
        /// 一键装齐：玩家 + 第三人称相机 + HUD + 暂停界面。
        ///
        /// 顺序很重要 —— 先装玩家与相机，再装 HUD：
        /// HUD 里那个 BubbleTypeHud 要去找 PauseMenu 接线，
        /// 而 PauseMenu 是 HUD 安装流程实例化出来的。反过来做没区别，
        /// 但玩家必须在场，否则 BubbleTypeSwitcher 那段检查会自己造一个空的切换器
        /// （造出来的没有玩家、切了也没用），玩家进来后又变成两个切换器打架。
        /// </summary>
        [MenuItem("Tools/PAO/关卡白盒/给 shiliuye map 装玩家 + HUD（一键）", priority = 85)]
        public static void InstallEverything()
        {
            InstallInternal(false);

            Debug.Log("[ShiliuyePlayer] 玩家与相机装好了，接着装 HUD 与暂停界面…");

            // HUD 那边要求"当前打开的必须是目标场景" —— 上一步结尾已经保存并留在该场景
            PauseHudSetup.Build(false, false, kSceneName);

            Debug.Log("[ShiliuyePlayer] 一键安装完成：玩家 + 相机 + HUD + 暂停界面。" +
                      "按 Play 试玩；按 Esc 弹暂停菜单。");
        }

        /// <summary><paramref name="framePlayer"/> 为 true 时把 Scene 视图聚焦到玩家。</summary>
        private static void InstallInternal(bool framePlayer)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) == null)
            {
                EditorUtility.DisplayDialog("装玩家", "找不到场景：\n" + kScenePath, "好");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(kScenePath, OpenSceneMode.Single);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kPlayerPrefabPath);

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("装玩家", "找不到玩家预制体：\n" + kPlayerPrefabPath, "好");
                return;
            }

            // ---- 1. 出生点 ----
            GameObject spawn = FindInScene(scene, kSpawnPointName);
            Vector3 spawnPosition = spawn != null
                ? spawn.transform.position
                : new Vector3(0f, 1f, -26f);
            Quaternion spawnRotation = spawn != null
                ? spawn.transform.rotation
                : Quaternion.LookRotation(Vector3.forward, Vector3.up);

            // SpawnPoint 是我们自己建的空物体，放玩家时不该留下它
            // （留着也无害，但会让 Hierarchy 多一个没用的东西）
            if (spawn != null)
            {
                Object.DestroyImmediate(spawn);
            }

            // ---- 2. 清掉上次装的玩家和相机枢轴，保证可重复执行 ----
            RemoveIfExists(scene, "Player");
            RemoveIfExists(scene, "CameraPivot");

            // ---- 3. 实例化玩家 ----
            GameObject player = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;

            if (player == null)
            {
                Debug.LogError("[ShiliuyePlayer] 玩家预制体实例化失败。");
                return;
            }

            player.name = "Player";
            player.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            // 让角色站在地面上：CharacterController 的胶囊中心在角色原点上方，
            // 直接放在 y=1 附近就够（SpawnPoint 给的就是 1）
            Debug.Log("[ShiliuyePlayer] 玩家已放入场景，位置 " + spawnPosition + "。");

            // ---- 4. 相机枢轴 + 相机 ----
            SetUpCamera(scene, player, spawnRotation);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = player;
            EditorGUIUtility.PingObject(player);

            // 只有单装玩家时才把 Scene 视图拉过去；
            // 一键安装时会接着装 HUD，那时候别抢视图焦点
            if (framePlayer && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            Debug.Log("[ShiliuyePlayer] 完成：玩家 + 第三人称相机（枢轴距离 " +
                      kCameraDistance + "m）已装好并保存。直接按 Play 就能走。");
        }

        /// <summary>
        /// 建立 "枢轴 → 相机" 两级结构，并接进 PlayerController。
        /// </summary>
        private static void SetUpCamera(Scene scene, GameObject player, Quaternion spawnRotation)
        {
            Camera camera = Camera.main;

            if (camera == null)
            {
                // 场景里没有 MainCamera tag 的相机就新建一台，
                // 否则玩家会因为没有 Camera.main 而拿不到枢轴（Awake 里那条兜底会失效）
                var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                SceneManager.MoveGameObjectToScene(cameraGo, scene);
                cameraGo.tag = "MainCamera";
                camera = cameraGo.GetComponent<Camera>();
                Debug.LogWarning("[ShiliuyePlayer] 场景里没有 Main Camera，已新建一台。");
            }

            // 枢轴：放在出生点，朝向和玩家一致（这样开局镜头在角色正后方）
            var pivot = new GameObject("CameraPivot");
            SceneManager.MoveGameObjectToScene(pivot, scene);
            pivot.transform.SetPositionAndRotation(
                player.transform.position, spawnRotation);

            // 把相机挂到枢轴下面，摆到正后方
            camera.transform.SetParent(pivot.transform, false);
            camera.transform.localPosition = new Vector3(0f, kCameraHeight, -kCameraDistance);
            camera.transform.localRotation = Quaternion.identity;

            // 接进 PlayerController
            var controller = player.GetComponent<PlayerController>();

            if (controller == null)
            {
                Debug.LogWarning("[ShiliuyePlayer] 玩家上没有 PlayerController，" +
                                 "相机枢轴没接上，会是固定的镜头。");
                return;
            }

            var so = new SerializedObject(controller);

            SerializedProperty pivotProp = so.FindProperty("m_CameraPivot");
            SerializedProperty cameraProp = so.FindProperty("m_CameraTransform");
            SerializedProperty distanceProp = so.FindProperty("m_CameraDistance");

            if (pivotProp != null) { pivotProp.objectReferenceValue = pivot.transform; }
            if (cameraProp != null) { cameraProp.objectReferenceValue = camera.transform; }
            if (distanceProp != null) { distanceProp.floatValue = kCameraDistance; }

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[ShiliuyePlayer] 相机接好：枢轴=" + pivot.name +
                      "，相机=" + camera.name +
                      "，距离=" + kCameraDistance + "m（第三人称）。");
        }

        // ==================== 辅助 ====================

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindDeep(root.transform, name);

                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == name)
                {
                    return child;
                }

                Transform deeper = FindDeep(child, name);

                if (deeper != null)
                {
                    return deeper;
                }
            }

            return null;
        }

        private static void RemoveIfExists(Scene scene, string name)
        {
            GameObject existing = FindInScene(scene, name);

            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }

        // ==================== 自动执行 ====================
        //
        // 让"切回 Unity 就能直接按 Play 测试"，不用先想起来点菜单。
        //
        // 【只自动装玩家 + 相机，不自动装 HUD】
        // 装玩家是"把一个物体放进场景"，结果明确、风险低；
        // 而装 HUD 会补 Canvas + EventSystem + 两个界面实例，
        // 改动面大得多。那种规模的改动应该由人显式点菜单 ——
        // 免得你打开场景什么都没做，回来发现场景被改了一大片还被存了盘。
        //
        // HUD 想装的话点：Tools → PAO → 关卡白盒 → 给 shiliuye map 装玩家 + HUD（一键）

        /// <summary>自动装过一次就记下来，别每次域重载都重来。</summary>
        private static string AutoMarkerPath
        {
            get
            {
                return System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.dataPath),
                    "ProjectSettings", "PAO_ShiliuyePlayerAuto.txt");
            }
        }

        [InitializeOnLoadMethod]
        private static void HookAutoInstall()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    {
                        return;
                    }

                    // 已经自动装过就不再重复 —— 用文件标记而不是 EditorPrefs
                    // （EditorPrefs 那个标记跨会话持久，一旦置位就永远不再执行）
                    if (System.IO.File.Exists(AutoMarkerPath))
                    {
                        return;
                    }

                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(kScenePath) == null)
                    {
                        return;
                    }

                    // 只在**用户已经打开**这个场景时自动装。
                    // 不主动 OpenScene —— 那会打断手头的工作。
                    Scene active = SceneManager.GetActiveScene();

                    if (active.path != kScenePath)
                    {
                        return;
                    }

                    // 已经有相机枢轴说明装过了
                    if (FindInScene(active, "CameraPivot") != null &&
                        FindInScene(active, "Player") != null)
                    {
                        WriteMarker();
                        return;
                    }

                    Debug.Log("[ShiliuyePlayer] 检测到 shiliuye map 还没有可玩的角色，正在自动装…");

                    InstallInternal(false);

                    WriteMarker();
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[ShiliuyePlayer] 自动安装失败：" + e.Message +
                                   "\n可以手动执行：Tools → PAO → 关卡白盒 → 给 shiliuye map 装玩家");
                }
            };
        }

        private static void WriteMarker()
        {
            try
            {
                System.IO.File.WriteAllText(AutoMarkerPath, "done");
            }
            catch (System.Exception)
            {
                // 写不进去不影响功能，只是下次可能再装一遍（那个操作是幂等的）
            }
        }
    }
}
