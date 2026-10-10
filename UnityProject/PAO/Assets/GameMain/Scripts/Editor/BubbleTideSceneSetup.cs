using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleTide.EditorTools
{
    /// <summary>
    /// 泡泡潮的一键搭建与安装工具。
    ///
    /// 和泡泡 SDF 那套工具的分工一样：
    ///   · 安装渲染 Feature → 照搬 URP 自己的 AddComponent 流程，绝不手改 YAML
    ///   · 搭建场景         → 摆好总控 / 八个角 / 终点，并把范围写进 Inspector
    ///
    /// ⚠️ 安装只处理 Assets/ 下的 Renderer。【绝不能碰 Packages/ 里的】——
    /// URP 包里自带一个隐藏的 UniversalRendererData.asset，改它 Unity 会报
    /// 「asset(s) located in immutable packages were unexpectedly altered」，
    /// 而且包一升级修改就丢。
    /// </summary>
    public static class BubbleTideSceneSetup
    {
        private const string k_InstallPath   = "Tools/PAO/泡泡潮/安装到 URP Renderer";
        private const string k_UninstallPath = "Tools/PAO/泡泡潮/从 URP Renderer 卸载";
        private const string k_SetupPath     = "Tools/PAO/泡泡潮/一键搭建潮水";
        private const string k_StatusPath    = "Tools/PAO/泡泡潮/检查安装状态";

        private const string k_RootName = "BubbleTide";

        // ==================================================================
        // 安装渲染 Feature
        // ==================================================================

        [MenuItem(k_InstallPath, priority = 20)]
        public static void Install()
        {
            List<ScriptableRendererData> targets = CollectRendererData(includePackages: false);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("泡泡潮",
                    "没找到工程自己的 URP Renderer 资产。\n请确认 Assets/Settings 下有 URP-*-Renderer.asset。",
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

            Debug.Log("[泡泡潮] 安装完成：新增 " + installed + "，跳过 " + skipped + "\n" + report);

            EditorUtility.DisplayDialog("泡泡潮",
                "安装完成。\n新增 " + installed + " 个，跳过 " + skipped + " 个。\n\n详见 Console。\n\n" +
                "接下来：\n工具 菜单 -> PAO -> 泡泡潮 -> 一键搭建潮水",
                "好");
        }

        [MenuItem(k_UninstallPath, priority = 22)]
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
                    report.AppendLine("· " + data.name + " — 已移除  (" + AssetDatabase.GetAssetPath(data) + ")");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[泡泡潮] 卸载完成：移除 " + removed + " 处\n" + report);

            EditorUtility.DisplayDialog("泡泡潮",
                removed > 0
                    ? "已移除 " + removed + " 处 Bubble Tide Render Feature。\n\n详见 Console。"
                    : "没有找到已安装的 Bubble Tide Render Feature。",
                "好");
        }

        // ==================================================================
        // 一键搭建
        // ==================================================================

        /// <summary>
        /// 在场景里搭出潮水需要的全部物体：
        ///   · BubbleTide 空物体（挂总控 + 渲染 + 推挤）
        ///   · 八个角（正方体的 8 个顶点）
        ///   · 终点
        /// 并把算出来的范围写进总控，免得手填。
        /// </summary>
        [MenuItem(k_SetupPath, priority = 24)]
        public static void SetupScene()
        {
            // ---- 1. 先算出场景几何的包围盒 ----
            Bounds bounds;
            if (!TryComputeSceneBounds(out bounds))
            {
                EditorUtility.DisplayDialog("泡泡潮",
                    "场景里找不到任何几何体（Renderer / Collider）。\n" +
                    "先搭好房间再跑这个工具。",
                    "好");
                return;
            }

            // 稍微外扩，让八个角落在房间外面一点，避免潮水从墙体内侧才开始
            bounds.Expand(0.4f);

            Undo.SetCurrentGroupName("搭建泡泡潮");
            int group = Undo.GetCurrentGroup();

            // ---- 2. 根物体 ----
            GameObject root = GameObject.Find(k_RootName);
            if (root == null)
            {
                root = new GameObject(k_RootName);
                Undo.RegisterCreatedObjectUndo(root, "创建泡泡潮根物体");
            }

            // 放在房间中心，方便在 Hierarchy 里找到
            root.transform.position = bounds.center;

            BubbleTideDirector director = root.GetComponent<BubbleTideDirector>();
            if (director == null)
            {
                director = Undo.AddComponent<BubbleTideDirector>(root);
            }

            Undo.RecordObject(director, "设置潮水范围");
            director.SetExplicitBounds(bounds.min, bounds.max);
            EditorUtility.SetDirty(director);

            // ---- 3. 八个角 ----
            // 正方体的 8 个顶点 = min/max 在所有轴上的组合
            int created = 0;

            for (int i = 0; i < 8; i++)
            {
                // 用位运算展开 8 种组合，比三重循环好读
                float x = (i & 1) != 0 ? bounds.max.x : bounds.min.x;
                float y = (i & 2) != 0 ? bounds.max.y : bounds.min.y;
                float z = (i & 4) != 0 ? bounds.max.z : bounds.min.z;

                Vector3 corner = new Vector3(x, y, z);

                GameObject anchorObject = FindAnchorAt(corner);
                if (anchorObject == null)
                {
                    anchorObject = new GameObject(AnchorName(i));
                    Undo.RegisterCreatedObjectUndo(anchorObject, "创建潮水源");
                    anchorObject.transform.SetParent(root.transform, true);
                    anchorObject.transform.position = corner;
                    created++;
                }

                if (anchorObject.GetComponent<BubbleTideAnchor>() == null)
                {
                    Undo.AddComponent<BubbleTideAnchor>(anchorObject);
                }
            }

            // ---- 4. 终点 ----
            BubbleTideGoal goal = Object.FindObjectOfType<BubbleTideGoal>();
            string goalNote;

            if (goal == null)
            {
                GameObject goalObject = new GameObject("终点 (BubbleTideGoal)");
                Undo.RegisterCreatedObjectUndo(goalObject, "创建终点");
                goalObject.transform.SetParent(root.transform, true);

                // 【关键】终点要放在「离玩家出生点最远的那个角」。
                //
                // 不能固定放某个角：场景里 pao（终点）和玩家出生点本来就在同一点，
                // 如果这里再把终点摆在固定角落，很可能正好落在出生点旁边，
                // 那样一进 Play 就直接通关，潮水这个功能根本没法试。
                //
                // 所以先问玩家出生点在哪，然后取对角。
                Vector3 goalPosition = ResolveOppositeCorner(bounds);
                goalObject.transform.position = goalPosition;

                goal = Undo.AddComponent<BubbleTideGoal>(goalObject);

                goalNote = "已新建，放在离出生点最远的角： " + goalPosition;
            }
            else
            {
                goalNote = "场景里已有终点，保持原位： " + goal.transform.position;

                float proximity = Vector3.Distance(goal.transform.position, ResolvePlayerPosition());
                if (proximity < 8f)
                {
                    goalNote += "\n  ⚠ 它离玩家出生点只有 " + proximity.ToString("F1") +
                                " 米，一进 Play 可能立刻通关。建议把它挪远。";
                }
            }

            EditorUtility.SetDirty(director);

            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(string.Format(
                "[泡泡潮] 搭建完成。\n" +
                "  范围 {0} ~ {1}（{2:F1} × {3:F1} × {4:F1} 米）\n" +
                "  新建潮水源 {5} 个（共 8 个）\n" +
                "  终点：{6}\n\n" +
                "注意：安装渲染 Feature 是另一步 —— 工具 → PAO → 泡泡潮 → 安装到 URP Renderer。",
                bounds.min, bounds.max, bounds.size.x, bounds.size.y, bounds.size.z,
                created, goalNote));

            EditorUtility.DisplayDialog("泡泡潮",
                "搭建完成。\n\n" +
                "范围：" + bounds.size.x.ToString("F1") + " × " +
                bounds.size.y.ToString("F1") + " × " +
                bounds.size.z.ToString("F1") + " 米\n" +
                "新建潮水源：" + created + " 个\n" +
                "终点：" + goalNote + "\n\n" +
                "别忘了还要装渲染 Feature：\n工具 → PAO → 泡泡潮 → 安装到 URP Renderer",
                "好");
        }

        private static string AnchorName(int index)
        {
            // 用 ±X / ±Y / ±Z 拼出来，比「角 3」好认
            string x = (index & 1) != 0 ? "+X" : "-X";
            string y = (index & 2) != 0 ? "+Y" : "-Y";
            string z = (index & 4) != 0 ? "+Z" : "-Z";
            return "潮水源 " + x + " " + y + " " + z;
        }

        /// <summary>
        /// 找玩家出生点。优先用带 PlayerController 的物体，
        /// 找不到就退回场景主相机的位置，再找不到就用房间中心。
        /// </summary>
        private static Vector3 ResolvePlayerPosition()
        {
            PAO.PlayerController controller = Object.FindObjectOfType<PAO.PlayerController>();
            if (controller != null)
            {
                return controller.transform.position;
            }

            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// 在包围盒的 8 个顶点里，挑出离玩家出生点最远的那个，稍微往房间内缩一点。
        /// 终点的默认落点就是它。
        /// </summary>
        private static Vector3 ResolveOppositeCorner(Bounds bounds)
        {
            Vector3 playerPosition = ResolvePlayerPosition();

            Vector3 best = bounds.min;
            float bestDistance = -1f;

            for (int i = 0; i < 8; i++)
            {
                float x = (i & 1) != 0 ? bounds.max.x : bounds.min.x;
                float y = (i & 2) != 0 ? bounds.max.y : bounds.min.y;
                float z = (i & 4) != 0 ? bounds.max.z : bounds.min.z;

                Vector3 corner = new Vector3(x, y, z);
                float distance = Vector3.Distance(corner, playerPosition);

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = corner;
                }
            }

            // 从角落往房间中心缩一点：角落本身通常是墙里，终点嵌在墙里玩家够不着。
            // 高度固定在离地 1.5 米，也就是玩家胸口高度。
            Vector3 inward = (bounds.center - best).normalized * 2.5f;
            Vector3 result = best + inward;
            result.y = bounds.min.y + 1.5f;

            return result;
        }

        /// <summary>
        /// 已经有一个角摆在这个位置了吗（容差 0.5 米）。
        /// 用来支持「重复跑这个工具不会重复创建」。
        /// </summary>
        private static GameObject FindAnchorAt(Vector3 position)
        {
            BubbleTideAnchor[] anchors = Object.FindObjectsOfType<BubbleTideAnchor>();

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] != null && Vector3.Distance(anchors[i].transform.position, position) < 0.5f)
                {
                    return anchors[i].gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 用场景里所有 Renderer + Collider 拼出包围盒。
        ///
        /// 会跳过灯光、相机、后处理 Volume 和潮水自己的物体 ——
        /// 灯光通常挂在很高或很远的位置，算进去会让包围盒凭空大出好几倍。
        /// </summary>
        private static bool TryComputeSceneBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasAny = false;

            GameObject root = GameObject.Find(k_RootName);

            // Renderer：拿到可见几何
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || ShouldIgnore(renderer.gameObject, root))
                {
                    continue;
                }

                if (!hasAny)
                {
                    bounds = renderer.bounds;
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            // Collider：可见几何可能没碰撞体，也可能碰撞体比网格大
            Collider[] colliders = Object.FindObjectsOfType<Collider>();
            foreach (Collider collider in colliders)
            {
                if (collider == null || ShouldIgnore(collider.gameObject, root))
                {
                    continue;
                }

                if (!hasAny)
                {
                    bounds = collider.bounds;
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasAny;
        }

        private static bool ShouldIgnore(GameObject candidate, GameObject tideRoot)
        {
            if (candidate == null)
            {
                return true;
            }

            // 潮水自己的物体不算
            if (tideRoot != null
                && (candidate == tideRoot || candidate.transform.IsChildOf(tideRoot.transform)))
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

        // ==================================================================
        // 状态检查
        // ==================================================================

        [MenuItem(k_StatusPath, priority = 26)]
        public static void CheckStatus()
        {
            var report = new StringBuilder();

            // ---- 渲染 Feature ----
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

            // ---- 场景物体 ----
            report.AppendLine();
            report.AppendLine("【场景物体】");

            BubbleTideDirector director = Object.FindObjectOfType<BubbleTideDirector>();
            report.AppendLine("  " + (director != null ? "✔" : "✘") + " BubbleTideDirector" +
                              (director != null ? "  (" + director.gameObject.name + ")" : "  ← 需要搭建"));

            BubbleTideAnchor[] anchors = Object.FindObjectsOfType<BubbleTideAnchor>();
            report.AppendLine("  " + (anchors.Length == 8 ? "✔" : "✘") + " 潮水源 " + anchors.Length + " / 8");

            BubbleTideGoal[] goals = Object.FindObjectsOfType<BubbleTideGoal>();
            report.AppendLine("  " + (goals.Length > 0 ? "✔" : "✘") + " 终点 " + goals.Length +
                              (goals.Length == 0 ? "  ← 需要搭建" : ""));

            // ---- 计算量预估 ----
            report.AppendLine();
            report.AppendLine("【计算量预估】");

            if (director != null)
            {
                Bounds bounds = director.GetEffectiveBounds();
                SerializedObject so = new SerializedObject(director);
                SerializedProperty cellProp = so.FindProperty("m_CellSize");
                float cell = cellProp != null ? cellProp.floatValue : 0.5f;

                int sx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cell));
                int sy = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cell));
                int sz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cell));
                int total = sx * sy * sz;

                report.AppendLine(string.Format(
                    "  格距 {0} 米 → {1} × {2} × {3} = {4:N0} 格",
                    cell, sx, sy, sz, total));

                // 经验值：CheckSphere 大约 6 万格/秒
                report.AppendLine(string.Format(
                    "  预计烤制约 {0:F1} 秒，纹理占用约 {1:F0} KB",
                    total / 60000f, total / 1024f));

                if (total > 2000000)
                {
                    report.AppendLine("  ⚠ 格数偏多，建议把格距调大到 1.0 米。");
                }
            }

            Debug.Log("[泡泡潮] 状态检查\n" + report);

            EditorUtility.DisplayDialog("泡泡潮 · 状态", report.ToString(), "好");
        }

        // ==================================================================
        // 内部实现（与 BubbleSDFInstaller 同一套做法）
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
                Debug.LogError("[泡泡潮] " + data.name +
                               " 找不到 m_RendererFeatures / m_RendererFeatureMap，请手动 Add Renderer Feature。");
                return false;
            }

            var feature = ScriptableObject.CreateInstance<BubbleTideRenderFeature>();
            feature.name = "BubbleTideRenderFeature";
            Undo.RegisterCreatedObjectUndo(feature, "Add Bubble Tide Render Feature");

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
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                if (!(feature is BubbleTideRenderFeature))
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
                if (feature is BubbleTideRenderFeature)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
