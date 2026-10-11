using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 【注意这里为什么没有 using UnityEngine.Rendering;】
//   有它的话 Volume / ShadowCastingMode 这些名字会和别处撞，
//   尤其是 Volume —— UnityEngine.Rendering.Volume 和
//   UnityEngine.Rendering.Universal 下的一堆 Volume 相关类型放在一起，
//   IntelliSense 会给出看不出区别的候选。
//   所以本文件统一写全名（UnityEngine.Rendering.ShadowCastingMode 等）。

namespace PAO.Underwater.EditorTools
{
    /// <summary>
    /// 水下效果的一键搭建与安装工具。
    ///
    /// 和泡泡潮 / 泡泡 SDF 那两套工具的分工一样：
    ///   · 安装渲染 Feature → 照搬 URP 自己的 AddComponent 流程，绝不手改 YAML
    ///   · 搭建场景         → 摆好水面、水底触发体、后处理 Volume，
    ///                        并把按包围盒算出来的尺寸写进 Inspector
    ///
    /// ⚠️ 安装只处理 Assets/ 下的 Renderer。【绝不能碰 Packages/ 里的】——
    /// URP 包里自带一个隐藏的 UniversalRendererData.asset，改它 Unity 会报
    /// 「asset(s) located in immutable packages were unexpectedly altered」，
    /// 而且包一升级修改就丢。
    ///
    /// =======================================================================
    /// 【这个工具为什么不直接用 YAML 拼资产，而是让 Unity 自己序列化】
    ///
    /// 后处理 Volume 的每个 effect 都有几十个字段（每个还带 overrideState
    /// 和 min/max），手写 YAML 意味着几十个字段名、格式、取值范围全靠猜 ——
    /// 错一个字段名不会报错，只会**静默地用默认值**，
    /// 表现是「一键搭建完之后画面没变化」，然后开始怀疑人生。
    ///
    /// 所以这里全部走 UnityEngine.Rendering.VolumeProfile.Add&lt;T&gt;() + 参数赋值 + SaveAssets，
    /// 让 Unity 自己去写序列化。这条路慢一点（要刷新资产库），但对得多。
    /// =======================================================================
    /// </summary>
    public static class UnderwaterSceneSetup
    {
        private const string k_InstallPath   = "Tools/PAO/水下/安装到 URP Renderer";
        private const string k_UninstallPath = "Tools/PAO/水下/从 URP Renderer 卸载";
        private const string k_SetupPath     = "Tools/PAO/水下/一键搭建水下";
        private const string k_StatusPath    = "Tools/PAO/水下/检查安装状态";
        private const string k_RemovePath    = "Tools/PAO/水下/移除场景里的水下物体";

        private const string k_RootName      = "Underwater";
        private const string k_MeshAssetPath = "Assets/GameMain/Art/Models/WaterSurface.asset";
        private const string k_MatAssetPath  = "Assets/GameMain/Art/Materials/WaterSurface.mat";
        private const string k_ProfilePath   = "Assets/Settings/UnderwaterProfile.asset";

        private const string k_FullscreenShader = "PAO/UnderwaterFullscreen";
        private const string k_GodRayShader     = "PAO/UnderwaterGodRays";
        private const string k_SurfaceShader    = "PAO/WaterSurface";

        /// <summary>把水面抬到玩家头顶至少这么高（米）。</summary>
        private const float k_MinHeadroom = 6f;

        // ==================================================================
        // 菜单
        // ==================================================================

        [MenuItem(k_SetupPath, priority = 40)]
        public static void SetupScene()
        {
            // ---- 0. 前置检查 ----
            if (!CheckPrerequisites(out string problem))
            {
                EditorUtility.DisplayDialog("水下效果", problem, "好");
                return;
            }

            if (!TryComputeSceneBounds(out Bounds bounds))
            {
                EditorUtility.DisplayDialog("水下效果",
                    "场景里找不到任何几何体（Renderer / Collider）。\n" +
                    "先搭好房间再跑这个工具。",
                    "好");
                return;
            }

            Undo.SetCurrentGroupName("搭建水下效果");
            int undoGroup = Undo.GetCurrentGroup();

            // ---- 1. 生成水面网格 ----
            Mesh mesh = CreateOrUpdateWaterMesh(bounds);

            // ---- 2. 材质 ----
            Material mat = CreateOrUpdateWaterMaterial();

            // ---- 3. 后处理 Profile ----
            UnityEngine.Rendering.VolumeProfile profile = CreateOrUpdateProfile();

            // ---- 4. 算水面高度 ----
            float waterLevel = EstimateWaterLevel(bounds);
            float waterSize   = EstimateWaterSize(bounds);

            // ---- 5. 摆场景物体 ----
            GameObject root = BuildHierarchy(bounds, mesh, mat, profile,
                                             waterLevel, waterSize);

            Undo.CollapseUndoOperations(undoGroup);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            // 推一次全局变量，让 Scene 视图立刻看到效果（不用进 Play）
            root.GetComponent<WaterVolume>()?.PushGlobals();

            Debug.Log(string.Format(
                "[水下] 搭建完成。\n" +
                "  水面高度 {0:F2} 米，水面尺寸 {1:F2} × {2:F2} 米\n" +
                "  水面网格 {3}\n  水面材质 {4}\n  后处理 {5}\n\n" +
                "接下来：\n" +
                "  1. 工具 → PAO → 水下 → 安装到 URP Renderer\n" +
                "  2. 选中 {6} 调「水面高度」，按 Scene 视图里的 Gizmo 对齐",
                waterLevel, waterSize * 2f, waterSize * 2f,
                k_MeshAssetPath, k_MatAssetPath, k_ProfilePath, k_RootName));

            EditorUtility.DisplayDialog("水下效果",
                "搭建完成。\n\n" +
                "⚠️ 水面高度是按场景包围盒估的（" + waterLevel.ToString("F2") + " 米），\n" +
                "**一定要在 Scene 视图里看着 Gizmo 手动确认一遍** ——\n" +
                "这个值填错时画面只会「怪」，不会报任何错。\n\n" +
                "接下来：\n" +
                "工具 → PAO → 水下 → 安装到 URP Renderer",
                "好");
        }

        [MenuItem(k_RemovePath, priority = 42)]
        public static void RemoveFromScene()
        {
            var existing = Object.FindObjectsOfType<WaterVolume>();
            if (existing.Length == 0)
            {
                EditorUtility.DisplayDialog("水下效果", "场景里没有 WaterVolume。", "好");
                return;
            }

            if (!EditorUtility.DisplayDialog("水下效果",
                    "要删掉场景里 " + existing.Length + " 个水下物体吗？\n\n" +
                    "（Assets 下的网格 / 材质 / 后处理 Profile 会保留，不删）",
                    "删除", "取消"))
            {
                return;
            }

            Undo.SetCurrentGroupName("移除水下效果");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (WaterVolume water in existing)
            {
                // 只删自己搭出来的那个根物体，不连坐它的父级
                Undo.DestroyObjectImmediate(water.gameObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ==================================================================
        // 安装渲染 Feature
        // ==================================================================

        [MenuItem(k_InstallPath, priority = 44)]
        public static void Install()
        {
            List<ScriptableRendererData> targets = CollectRendererData(includePackages: false);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("水下效果",
                    "没找到工程自己的 URP Renderer 资产。\n" +
                    "请确认 Assets/Settings 下有 URP-*-Renderer.asset。",
                    "好");
                return;
            }

            var report = new StringBuilder();
            int installed = 0;
            int skipped = 0;

            foreach (ScriptableRendererData data in targets)
            {
                if (HasFeature(data))
                {
                    skipped++;
                    report.AppendLine("· " + data.name + " — 已安装，跳过");
                    continue;
                }

                if (AddFeature(data))
                {
                    installed++;
                    report.AppendLine("· " + data.name + " — 已安装");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[水下] 安装完成：新增 " + installed + "，跳过 " + skipped + "\n" + report);

            EditorUtility.DisplayDialog("水下效果",
                "安装完成。\n新增 " + installed + " 个，跳过 " + skipped + " 个。\n\n" +
                "详见 Console。",
                "好");
        }

        [MenuItem(k_UninstallPath, priority = 46)]
        public static void Uninstall()
        {
            List<ScriptableRendererData> targets = CollectRendererData(includePackages: true);

            var report = new StringBuilder();
            int removed = 0;

            foreach (ScriptableRendererData data in targets)
            {
                if (data == null || !HasFeature(data))
                {
                    continue;
                }

                if (RemoveFeature(data))
                {
                    removed++;
                    report.AppendLine("· " + data.name + " — 已移除  (" +
                                      AssetDatabase.GetAssetPath(data) + ")");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[水下] 卸载完成：移除 " + removed + " 处\n" + report);

            EditorUtility.DisplayDialog("水下效果",
                removed > 0
                    ? "已移除 " + removed + " 处 Underwater Render Feature。\n\n详见 Console。"
                    : "没有找到已安装的 Underwater Render Feature。",
                "好");
        }

        // ==================================================================
        // 状态检查
        // ==================================================================

        [MenuItem(k_StatusPath, priority = 48)]
        public static void CheckStatus()
        {
            var report = new StringBuilder();

            // ---- Shader ----
            report.AppendLine("【Shader】");
            ReportShader(report, k_FullscreenShader);
            ReportShader(report, k_SurfaceShader);
            ReportShader(report, k_GodRayShader);

            // ---- 渲染 Feature ----
            report.AppendLine();
            report.AppendLine("【渲染 Feature】");
            List<ScriptableRendererData> targets = CollectRendererData(includePackages: false);

            if (targets.Count == 0)
            {
                report.AppendLine("  没找到工程自己的 URP Renderer 资产。");
            }

            foreach (ScriptableRendererData data in targets)
            {
                bool has = HasFeature(data);
                report.AppendLine("  " + (has ? "✔" : "✘") + " " + data.name +
                                  (has ? "" : "  ← 需要安装"));

                if (has)
                {
                    report.AppendLine("      " + AssetDatabase.GetAssetPath(data));
                }
            }

            // ---- 深度纹理 ----
            //
            // 这一项单独列出来，是因为它关掉时【不会报任何错】，
            // 只是焦散整体消失。是最难查的一种失败。
            report.AppendLine();
            report.AppendLine("【深度纹理】（焦散和体积光都依赖它）");

            foreach (ScriptableRendererData data in targets)
            {
                string path = AssetDatabase.GetAssetPath(data);
                var urpAsset = FindUrpAssetUsing(data);

                if (urpAsset == null)
                {
                    report.AppendLine("  ? " + data.name + " — 没有 URP Asset 在用它");
                    continue;
                }

                var so = new SerializedObject(urpAsset);
                SerializedProperty depthProp = so.FindProperty("m_RequireDepthTexture");
                bool required = depthProp == null || depthProp.boolValue;

                report.AppendLine("  " + (required ? "✔" : "✘") + " " + urpAsset.name +
                                  " → " + data.name +
                                  (required ? "" : "  ← 需要在 URP Asset 上勾 Depth Texture"));
            }

            // ---- 场景物体 ----
            report.AppendLine();
            report.AppendLine("【场景物体】");

            WaterVolume[] waters = Object.FindObjectsOfType<WaterVolume>();
            report.AppendLine("  " + (waters.Length == 1 ? "✔" : "✘") + " WaterVolume " +
                              waters.Length + " 个" +
                              (waters.Length == 0 ? "  ← 需要搭建"
                               : waters.Length > 1 ? "  ← 应该只有 1 个" : ""));

            foreach (WaterVolume water in waters)
            {
                report.AppendLine(string.Format(
                    "      {0}: 水面 y={1:F2}  尺寸 {2:F1}m  强度 {3:F2}  {4}",
                    water.gameObject.name, water.waterLevel, water.waterSize * 2f,
                    water.underwaterStrength,
                    water.causticsEnabled ? "焦散开" : "焦散关"));

                if (water.transform.Find("WaterSurface") == null)
                {
                    report.AppendLine("      ⚠ 没有 WaterSurface 子物体 —— 抬头看不到水面");
                }
            }

            // ---- 贴图替换点 ----
            report.AppendLine();
            // ---- 贴图 ----
            //
            // 这一项列出来是因为「有没有放图」直接决定 shader 走哪条分支，
            // 而两条分支的观感差别不小。放了一张不合规的图（比如没开 Repeat）
            // 时画面会出现明显的接缝，但不会有任何报错。
            report.AppendLine();
            report.AppendLine("【贴图】（可选，留空会自动退回程序化版本）");

            foreach (WaterVolume water in waters)
            {
                Texture2D tex = water.causticsTexture;

                if (tex == null)
                {
                    report.AppendLine("  · 焦散贴图：未指定 → 用程序化细胞噪声");
                    continue;
                }

                report.AppendLine(string.Format(
                    "  · 焦散贴图：{0}  {1}×{2}",
                    tex.name, tex.width, tex.height));

                // 逐项检查那些「错了不报错、只是画面怪」的设置
                if (tex.wrapMode != TextureWrapMode.Repeat)
                {
                    report.AppendLine("      ⚠ Wrap Mode 是 " + tex.wrapMode +
                                      "，应该是 Repeat —— 否则平铺时会出现明显接缝");
                }

                if (tex.width != tex.height)
                {
                    report.AppendLine("      ⚠ 不是正方形。平铺后图案会被拉伸");
                }

                if (IsPowerOfTwo(tex.width) == false)
                {
                    report.AppendLine("      ⚠ 边长不是 2 的幂，某些平台会被强制缩放");
                }

                if (tex.isReadable)
                {
                    report.AppendLine("      · Read/Write 开着 —— 建议关掉，省一份内存副本");
                }

                if (tex.width > 1024)
                {
                    report.AppendLine("      ⚠ 尺寸偏大。这张图会在世界里反复平铺，" +
                                      "512 就够，做大了纯浪费显存");
                }

                string path = AssetDatabase.GetAssetPath(tex);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer != null && importer.sRGBTexture)
                {
                    report.AppendLine("      · sRGB 开着。这张图是「图案」不是「颜色」，" +
                                      "关掉更符合预期（影响很小，可以先不管）");
                }
            }

            Debug.Log("[水下] 状态检查\n" + report);
            EditorUtility.DisplayDialog("水下效果 · 状态", report.ToString(), "好");
        }

        /// <summary>2 的幂判断。贴图边长建议是 2 的幂，否则部分平台会自动缩放。</summary>
        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        // ==================================================================
        // 内部：前置检查
        // ==================================================================

        private static bool CheckPrerequisites(out string problem)
        {
            Shader fullscreen = Shader.Find(k_FullscreenShader);
            Shader godRay     = Shader.Find(k_GodRayShader);
            Shader surface    = Shader.Find(k_SurfaceShader);

            if (fullscreen == null || godRay == null || surface == null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("有 Shader 找不到，先解决编译错误再搭建 —— ");
                sb.AppendLine("否则会把一个粉色（missing）材质烤进场景。");
                sb.AppendLine();
                sb.AppendLine((fullscreen == null ? "✘ " : "✔ ") + k_FullscreenShader);
                sb.AppendLine((godRay     == null ? "✘ " : "✔ ") + k_GodRayShader);
                sb.AppendLine((surface    == null ? "✘ " : "✔ ") + k_SurfaceShader);
                sb.AppendLine();
                sb.AppendLine("确认这四个文件存在且能编译（或直接跑「检查安装状态」，");
                sb.AppendLine("那里会把 ShaderUtil 的编译错误原样列出来）：");
                sb.AppendLine("  Assets/GameMain/Art/Shaders/UnderwaterFullscreen.shader");
                sb.AppendLine("  Assets/GameMain/Art/Shaders/UnderwaterGodRays.shader");
                sb.AppendLine("  Assets/GameMain/Art/Shaders/WaterSurface.shader");
                sb.AppendLine("  Assets/GameMain/Art/Shaders/UnderwaterInclude.hlsl");
                sb.AppendLine("  Assets/GameMain/Art/Shaders/WaterNoise.hlsl");

                problem = sb.ToString();
                return false;
            }

            problem = null;
            return true;
        }

        // ==================================================================
        // 内部：场景几何
        // ==================================================================

        /// <summary>
        /// 用「场景里所有渲染出来的几何体」算包围盒。
        /// 工具自己的物体、灯光、相机、Volume 都不算。
        /// </summary>
        private static bool TryComputeSceneBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;

            GameObject root = GameObject.Find(k_RootName);

            foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || ShouldIgnore(r.gameObject, root))
                {
                    continue;
                }

                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                if (c == null || ShouldIgnore(c.gameObject, root))
                {
                    continue;
                }

                if (!has)
                {
                    bounds = c.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(c.bounds);
                }
            }

            return has;
        }

        private static bool ShouldIgnore(GameObject candidate, GameObject waterRoot)
        {
            if (candidate == null)
            {
                return true;
            }

            // 水下自己的物体不算
            if (waterRoot != null &&
                (candidate == waterRoot || candidate.transform.IsChildOf(waterRoot.transform)))
            {
                return true;
            }

            // 灯光、相机、后处理不该影响「房间多大」
            if (candidate.GetComponentInParent<Light>() != null)
            {
                return true;
            }

            if (candidate.GetComponentInParent<Camera>() != null)
            {
                return true;
            }

            if (candidate.GetComponentInParent<UnityEngine.Rendering.Volume>() != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 估一个水面高度。
        ///
        /// 【这是整个工具里唯一一个「必须人工确认」的值】
        ///   水面高度填错的后果：焦散、雾、光柱全部按错误的高度算 —— 
        ///   画面只会「怪」，不会报任何错。
        ///   所以这里给的是个稳妥的初值，然后靠对话框提醒 + Gizmo 让人去核。
        ///
        /// 策略：
        ///   1. 房间顶部的 85% 高度（别贴着天花板，留一点空间给光柱）
        ///   2. 但必须高出**地板**至少 k_MinHeadroom 米 ——
        ///      否则水面就在头顶一两米，稍微一跳玩家就冒出去了
        /// </summary>
        private static float EstimateWaterLevel(Bounds bounds)
        {
            float level = Mathf.Lerp(bounds.min.y, bounds.max.y, 0.85f);

            float minimum = EstimateFloorY(bounds) + k_MinHeadroom;
            if (level < minimum)
            {
                level = minimum;
            }

            return Mathf.Round(level * 100f) / 100f;
        }

        /// <summary>
        /// 估地板高度：从主相机所在的位置往下打一条探测。
        ///
        /// 【为什么用「主相机 + 探测」而不是找 PlayerController】
        ///   为了一行「玩家在哪」去依赖 PAO.PlayerController，会让这个工具
        ///   没法在只有一个空场景 + 一个相机的工程里跑。而主相机是 Unity 的
        ///   通用约定，一定有。
        ///
        /// 【为什么要临时挂一个 Collider】
        ///   Physics.SphereCast 只认 Collider。玩家身上挂的是
        ///   CharacterController —— 它有碰撞体，但没有 Collider 组件，
        ///   SphereCast 看不见它，但看得见它脚下的地板，这就够了。
        ///   临时挂的是 Trigger，不参与任何物理模拟，查完立刻销毁。
        /// </summary>
        private static float EstimateFloorY(Bounds bounds)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return bounds.min.y;
            }

            Vector3 origin = cam.transform.position;
            float maxDistance = Mathf.Max(50f, origin.y - bounds.min.y + 20f);

            var probe = cam.gameObject.AddComponent<SphereCollider>();
            probe.radius = 0.15f;
            probe.isTrigger = true;

            float floorY;

            try
            {
                bool hit = Physics.SphereCast(
                    origin,
                    probe.radius,
                    Vector3.down,
                    out RaycastHit info,
                    maxDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                floorY = hit ? info.point.y : bounds.min.y;
            }
            finally
            {
                // 【必须在 finally 里】—— 上面任何一步抛异常，这个临时 Collider
                // 都会留在相机上，而相机是场景物体，会被一起存进场景文件。
                // 那种污染几乎不可能被发现，所以这里不留任何侥幸。
                Object.DestroyImmediate(probe);
            }

            return floorY;
        }

        /// <summary>
        /// 估水面尺寸。必须比房间大一圈 ——
        /// 玩家抬头能看见水面的边缘就穿帮了，所以给足余量。
        /// </summary>
        private static float EstimateWaterSize(Bounds bounds)
        {
            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);

            // 半边长 = 房间最大水平尺寸 + 40 米余量
            float half = Mathf.Max(40f, horizontal * 0.5f + 40f);

            return Mathf.Round(half);
        }

        // ==================================================================
        // 内部：资产
        // ==================================================================

        private static Mesh CreateOrUpdateWaterMesh(Bounds bounds)
        {
            // 段数按水面尺寸走：一张 400 米的平面上每 4 米一段就够，
            // 顶点只是为了让 Unity 有个正常的包围盒和 LOD 依据 ——
            // 波光全在片元里算，段数多一点少一点看不出差别。
            int segments = Mathf.Clamp(Mathf.RoundToInt(EstimateWaterSize(bounds) / 2f), 16, 128);
            segments = Mathf.NextPowerOfTwo(segments);

            Mesh mesh = WaterSurfaceMeshBuilder.Build(segments);
            mesh.name = "WaterSurface";

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_MeshAssetPath);

            if (existing == null)
            {
                EnsureFolder(k_MeshAssetPath);
                AssetDatabase.CreateAsset(mesh, k_MeshAssetPath);
                return AssetDatabase.LoadAssetAtPath<Mesh>(k_MeshAssetPath);
            }

            // 复用已有的资产对象，保持 GUID 不变（场景里的引用才不会断）
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);

            return existing;
        }

        private static Material CreateOrUpdateWaterMaterial()
        {
            Shader shader = Shader.Find(k_SurfaceShader);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(k_MatAssetPath);

            if (mat == null)
            {
                EnsureFolder(k_MatAssetPath);
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, k_MatAssetPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.name = "WaterSurface";
            EditorUtility.SetDirty(mat);

            return mat;
        }

        /// <summary>
        /// 建 / 更新水下后处理 Profile。
        ///
        /// 【为什么用 UnityEngine.Rendering.VolumeProfile.Add&lt;T&gt;() 而不是手写 .asset】
        ///   见文件头的说明。这里的每个字段名都是从 URP 源码里核对过的，
        ///   但仍然走 API —— 因为手写 YAML 时字段名写错**不会报错**，
        ///   只会静默地用默认值。
        /// </summary>
        private static UnityEngine.Rendering.VolumeProfile CreateOrUpdateProfile()
        {
            UnityEngine.Rendering.VolumeProfile profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(k_ProfilePath);

            if (profile == null)
            {
                EnsureFolder(k_ProfilePath);
                profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(profile, k_ProfilePath);
            }

            // ---- 颜色：把画面压成海水色 ----
            var color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.overrideState = true;
            color.postExposure.value = -0.15f;

            color.contrast.overrideState = true;
            color.contrast.value = 8f;

            color.saturation.overrideState = true;
            color.saturation.value = -6f;

            // 偏青绿。注意这只是一层薄滤镜 —— 
            // 真正的水色由 WaterVolume 的全屏雾给，这里做的是「整体色调统一」，
            // 让不透明几何体、UI 之外的一切都往同一个方向偏。
            color.colorFilter.overrideState = true;
            color.colorFilter.value = new Color(0.82f, 1f, 1f, 1f);

            // ---- 白平衡：往冷里推 ----
            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.overrideState = true;
            wb.temperature.value = -14f;

            wb.tint.overrideState = true;
            wb.tint.value = 5f;

            // ---- 暗角：水下视野的收束感 ----
            var vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.32f;

            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.55f;

            vignette.rounded.overrideState = true;
            vignette.rounded.value = true;

            // ---- 景深：水里的「能见度有限」 ----
            //
            // 用高斯模式而不是 Bokeh：高斯便宜得多，而且在「远处整体发糊」
            // 这个用途上和 Bokeh 看不出差别 —— Bokeh 的圆形光斑是给近景虚化用的。
            var dof = GetOrAdd<DepthOfField>(profile);
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Gaussian;

            dof.gaussianStart.overrideState = true;
            dof.gaussianStart.value = 12f;

            dof.gaussianEnd.overrideState = true;
            dof.gaussianEnd.value = 45f;

            dof.gaussianMaxRadius.overrideState = true;
            dof.gaussianMaxRadius.value = 0.9f;

            // ---- Bloom：让水面的过曝长出柔光 ----
            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.95f;

            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.85f;

            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;

            // ---- 色散：镜头在水里的那点色边 ----
            //
            // 【克制】这个值超过 0.25 就会变成「坏了的老电视」。
            // 0.12 是「说不出哪里不对，但就是更像水下」的量。
            var ca = GetOrAdd<ChromaticAberration>(profile);
            ca.intensity.overrideState = true;
            ca.intensity.value = 0.12f;

            EditorUtility.SetDirty(profile);

            return profile;
        }

        private static T GetOrAdd<T>(UnityEngine.Rendering.VolumeProfile profile) where T : UnityEngine.Rendering.VolumeComponent
        {
            if (profile.TryGet<T>(out T existing))
            {
                return existing;
            }

            return profile.Add<T>(overrides: true);
        }

        // ==================================================================
        // 内部：场景层级
        // ==================================================================

        private static GameObject BuildHierarchy(Bounds bounds,
                                                 Mesh mesh,
                                                 Material mat,
                                                 UnityEngine.Rendering.VolumeProfile profile,
                                                 float waterLevel,
                                                 float waterSize)
        {
            GameObject root = GameObject.Find(k_RootName);

            bool created = root == null;
            if (created)
            {
                root = new GameObject(k_RootName);
                Undo.RegisterCreatedObjectUndo(root, "创建水下效果");
            }

            // 放在房间水平中心，Y 放在水面高度上 —— 
            // 这样 Inspector 里看到的 Transform 位置就是水面位置，Gizmo 也在那。
            root.transform.position = new Vector3(
                Mathf.Round(bounds.center.x * 100f) / 100f,
                waterLevel,
                Mathf.Round(bounds.center.z * 100f) / 100f);
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // ---- WaterVolume ----
            WaterVolume volume = root.GetComponent<WaterVolume>();
            if (volume == null)
            {
                volume = Undo.AddComponent<WaterVolume>(root);
            }

            volume.waterLevel = waterLevel;
            volume.waterSize = waterSize;
            EditorUtility.SetDirty(volume);

            // ---- 水面网格 ----
            GameObject surface = EnsureChild(root, "WaterSurface");
            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.identity;
            // 网格是按「半边长 = 1」建的，所以缩放直接就是半边长
            surface.transform.localScale = new Vector3(waterSize, 1f, waterSize);

            MeshFilter filter = surface.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = Undo.AddComponent<MeshFilter>(surface);
            }

            filter.sharedMesh = mesh;

            MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = Undo.AddComponent<MeshRenderer>(surface);
            }

            renderer.sharedMaterial = mat;

            // 水面不投影、不接收阴影、不参与光照探针和反射探针 ——
            // 它的观感 100% 来自自己的 shader，走 URP 的光照流程只会更慢更不像。
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // ---- 水下触发体 ----
            //
            // 后处理 Volume 走 URP 自己的「相机进入 Volume 才生效」机制。
            // 相机在水下 = 相机在盒子里 = 后处理生效，不需要写一行脚本。
            GameObject trigger = EnsureChild(root, "UnderwaterTrigger");
            trigger.transform.localPosition = Vector3.zero;
            trigger.transform.localRotation = Quaternion.identity;
            trigger.transform.localScale = Vector3.one;

            BoxCollider box = trigger.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = Undo.AddComponent<BoxCollider>(trigger);
            }

            box.isTrigger = true;
            // 盒子从水面往下延伸到场景底部（多给 200 米，保证潜多深都在里面）
            float depth = Mathf.Max(20f, root.transform.position.y - bounds.min.y + 200f);
            box.center = new Vector3(0f, -depth * 0.5f, 0f);
            box.size   = new Vector3(waterSize * 2f, depth, waterSize * 2f);

            UnityEngine.Rendering.Volume vol =
                trigger.GetComponent<UnityEngine.Rendering.Volume>();
            if (vol == null)
            {
                vol = Undo.AddComponent<UnityEngine.Rendering.Volume>(trigger);
            }

            vol.isGlobal = false;
            vol.priority = 10f;          // 比场景里的 Global Volume（0）高
            vol.weight = 1f;
            vol.blendDistance = 0f;      // 边界硬切 —— 水面本身有平滑过渡带，这里不用再叠一层
            vol.sharedProfile = profile;

            // ---- 材质颜色跟 WaterVolume 对齐 ----
            //
            // 【为什么是「烤一次」而不是每帧同步】
            //   水面波光的频率 / 亮度 / 亮边走材质属性，不走全局变量 ——
            //   只有那一块网格用它们，没必要污染全局命名空间。
            //   所以这里是单向的：搭的时候把 WaterVolume 上的初值烤进材质，
            //   之后想调就调材质。
            //   这样「改哪个地方生效」永远是确定的，不用猜。
            if (mat != null)
            {
                mat.SetColor("_BaseColor", volume.surfaceColor);
                mat.SetFloat("_Brightness", volume.surfaceBrightness);
                mat.SetFloat("_RimIntensity", volume.surfaceRimIntensity);
                mat.SetFloat("_CausticScale", volume.surfaceCausticScale);
                mat.SetFloat("_RippleSpeed", volume.surfaceRippleSpeed);
                mat.SetFloat("_Sparkle", volume.surfaceSparkle);
                mat.SetVector("_LightDir", volume.surfaceLightDirection);
                EditorUtility.SetDirty(mat);
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            return root;
        }

        private static GameObject EnsureChild(GameObject parent, string name)
        {
            Transform existing = parent.transform.Find(name);

            if (existing != null)
            {
                return existing.gameObject;
            }

            var child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "创建 " + name);
            child.transform.SetParent(parent.transform, worldPositionStays: false);

            return child;
        }

        // ==================================================================
        // 内部：Renderer Feature 安装（与 BubbleTideSceneSetup 同一套做法）
        // ==================================================================

        /// <summary>照搬 URP 的 AddComponent 流程，featureMap 的 localFileID 必须写对。</summary>
        private static bool AddFeature(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            so.Update();

            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = so.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
            {
                Debug.LogError("[水下] " + data.name +
                               " 找不到 m_RendererFeatures / m_RendererFeatureMap，" +
                               "请手动 Add Renderer Feature。");
                return false;
            }

            var feature = ScriptableObject.CreateInstance<UnderwaterRenderFeature>();
            feature.name = "UnderwaterRenderFeature";
            Undo.RegisterCreatedObjectUndo(feature, "Add Underwater Render Feature");

            // 存成同一个 .asset 的子资产，引用才稳
            if (EditorUtility.IsPersistent(data))
            {
                AssetDatabase.AddObjectToAsset(feature, data);
            }

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            // 先扩容再赋值 —— Unity 序列化列表就是这个套路
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            return true;
        }

        private static bool RemoveFeature(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            so.Update();

            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = so.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
            {
                return false;
            }

            bool changed = false;

            // 从后往前删，避免索引错位
            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue
                              as ScriptableRendererFeature;

                if (!(feature is UnderwaterRenderFeature))
                {
                    continue;
                }

                features.DeleteArrayElementAtIndex(i);

                if (i < featureMap.arraySize)
                {
                    featureMap.DeleteArrayElementAtIndex(i);
                }

                // 子资产也要一起清掉，否则 .asset 里会残留孤儿对象
                if (feature != null && EditorUtility.IsPersistent(feature))
                {
                    AssetDatabase.RemoveObjectFromAsset(feature);
                }

                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            return true;
        }

        private static List<ScriptableRendererData> CollectRendererData(bool includePackages)
        {
            var result = new List<ScriptableRendererData>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableRendererData");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 包里的资产是只读的，改它 Unity 会告警，而且升级就丢
                if (!includePackages && path.StartsWith("Packages/"))
                {
                    continue;
                }

                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data != null)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        private static bool HasFeature(ScriptableRendererData data)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is UnderwaterRenderFeature)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>反查「哪个 URP Asset 在用这个 Renderer」。</summary>
        private static UniversalRenderPipelineAsset FindUrpAssetUsing(ScriptableRendererData data)
        {
            string rendererGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(data));

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);

                if (asset == null)
                {
                    continue;
                }

                var so = new SerializedObject(asset);
                SerializedProperty list = so.FindProperty("m_RendererDataList");

                if (list == null)
                {
                    continue;
                }

                for (int i = 0; i < list.arraySize; i++)
                {
                    var entry = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (entry == null)
                    {
                        continue;
                    }

                    if (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry)) == rendererGuid)
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        // ==================================================================
        // 内部：杂项
        // ==================================================================

        /// <summary>
        /// 检查一个 shader 能不能找到、有没有编译错误。
        ///
        /// 【为什么要专门做这件事】
        ///   Shader 编译失败时 Unity 只是**不会把它挂到材质上**，
        ///   材质会显示成亮粉色（missing），但 Console 里不一定会有一条
        ///   明确的错误。而「一键搭建」如果在这种状态下跑，会把一个粉色材质
        ///   烤进场景，看起来像工具写错了。
        ///
        ///   所以这里把 ShaderUtil 里的编译信息原样打出来 —— 
        ///   它比 Console 里那条「Shader error in ...」更精确（带平台和行号），
        ///   而且能一次性把三个 shader 都过一遍。
        /// </summary>
        private static void ReportShader(StringBuilder report, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);

            if (shader == null)
            {
                report.AppendLine("  ✘ " + shaderName + "  ← 找不到（检查编译错误）");
                return;
            }

            int messageCount = ShaderUtil.GetShaderMessageCount(shader);

            if (messageCount == 0)
            {
                report.AppendLine("  ✔ " + shaderName);
                return;
            }

            report.AppendLine("  ⚠ " + shaderName + " —— 有 " + messageCount + " 条编译信息");

            // 【为什么写全名 UnityEditor.ShaderMessage】
            //   ShaderUtil.GetShaderMessages 返回的是 **UnityEditor** 下的
            //   ShaderMessage，不是 UnityEngine.Rendering 下的同名类型。
            //   两个命名空间都有这个名字，写全名才不会选错。
            UnityEditor.ShaderMessage[] messages =
                ShaderUtil.GetShaderMessages(shader);

            if (messages == null)
            {
                return;
            }

            // 最多列 12 条。Shader 编译错误常常是同一个根因引发几十条，
            // 全列出来会把别的检查项挤出对话框。
            int shown = Mathf.Min(messages.Length, 12);

            for (int i = 0; i < shown; i++)
            {
                UnityEditor.ShaderMessage m = messages[i];

                report.AppendLine(string.Format(
                    "      [{0}] {1}:{2}  {3}",
                    m.platform,
                    System.IO.Path.GetFileName(m.file),
                    m.line,
                    m.message));
            }

            if (messages.Length > shown)
            {
                report.AppendLine("      … 还有 " + (messages.Length - shown) + " 条，见 Console");
            }
        }

        /// <summary>确保某个资产路径的文件夹存在，不存在就建。</summary>
        private static void EnsureFolder(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            // 逐级往上建
            string[] parts = folder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
