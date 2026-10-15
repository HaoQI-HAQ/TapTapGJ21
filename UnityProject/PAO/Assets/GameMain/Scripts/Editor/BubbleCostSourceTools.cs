using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PAO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 泡泡消耗「改了却不生效」的排查与清理工具。
    ///
    /// 起因：BubbleLauncher 同时存在两份消耗数据 ——
    ///   ① BubbleLoadout 资产（唯一权威来源）
    ///   ② 组件上的 m_StickySettings / m_BouncySettings / m_BombSettings（已弃用的回退值）
    /// 只要 m_Loadout 有值，② 就永远不会被读到。而 Inspector 里两处长得几乎一样，
    /// 很容易改错地方；cs 场景里还额外压着两条 m_BombSettings 的预制体实例覆盖，
    /// 在 Inspector 上显示成 10 / 20，进一步误导。
    ///
    /// 菜单：Tools/PAO/泡泡配置/*
    /// </summary>
    public static class BubbleCostSourceTools
    {
        private const string kLoadoutPath = "Assets/GameMain/Resources/CSBubbleLoadout.asset";
        private static readonly string[] kTypeKeys = { "sticky", "bouncy", "bomb" };

        /// <summary>已弃用的组件字段名，出现在预制体覆盖里就是僵尸数据。</summary>
        private static readonly string[] kStaleFieldNames = { "m_StickySettings", "m_BouncySettings", "m_BombSettings" };

        /// <summary>正常预制体资产里不该出现、只可能来自预制体覆盖的属性路径行。</summary>
        private static readonly Regex kPrefabOverridePath = new Regex(
            @"propertyPath:\s*(m_(?:Sticky|Bouncy|Bomb)Settings)", RegexOptions.Multiline);

        /// <summary>BubbleLauncher 的脚本 guid。</summary>
        private const string kLauncherScriptGuid = "dcac0db11c8f2ea43a4ea2022fd2dc6a";

        /// <summary>takoyaki 预制体（挂着 BubbleLauncher 的那一个）的 guid。</summary>
        private const string kPlayerPrefabGuid = "533472653752a5e42b3a814b5ef9cbac";

        [MenuItem("Tools/PAO/泡泡配置/诊断：消耗到底读哪一份", false, 10)]
        public static void Diagnose()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("[泡泡消耗来源诊断]");
            report.AppendLine();

            int problems = 0;

            // ---------- 1. 磁盘文件 vs 引擎当前加载的对象 ----------
            problems += CompareDiskWithLoaded(report);

            // ---------- 2. 场景 / 预制体里的引用与僵尸覆盖 ----------
            List<string> scenes = ScenePaths();
            problems += ScanBundles(scenes, PrefabPaths(), report);

            // ---------- 3. 结论 ----------
            report.AppendLine();
            if (problems == 0)
            {
                report.AppendLine("✔ 未发现冲突：所有 BubbleLauncher 都指向同一份资产，也没有僵尸覆盖。");
                report.AppendLine("  如果这时游戏里消耗仍不对，直接改 " + kLoadoutPath + " 并 Ctrl+S 即可。");
            }
            else
            {
                report.AppendLine("✘ 发现 " + problems + " 处问题（见上面的 ⟶ 行）。");
                report.AppendLine("  修法：菜单 Tools/PAO/泡泡配置/清理已删字段的僵尸覆盖；");
                report.AppendLine("  消耗数值本身请改 " + kLoadoutPath + "。");
            }

            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/PAO/泡泡配置/清理已删字段的僵尸覆盖", false, 30)]
        public static void CleanStaleOverrides()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[泡泡配置] 当前场景有未保存改动，已取消清理。");
                return;
            }

            StringBuilder report = new StringBuilder("[泡泡配置] 清理已弃用字段（m_StickySettings / m_BouncySettings / m_BombSettings）的僵尸覆盖\n");
            int scenes = 0;

            foreach (string path in ScenePaths())
            {
                if (!SceneHasStaleOverrides(path))
                {
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (CleanScene(scene, report))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    scenes++;
                }
            }

            foreach (string path in PrefabPaths())
            {
                // 预制体资产不在这里动刀：LoadPrefabContents + SaveAsPrefabAsset
                // 会把嵌套预制体的覆盖关系压平，代价太大。有的话只提示人工处理。
                int stale = CountPrefabOverridePaths(File.ReadAllText(path));
                if (stale > 0)
                {
                    report.AppendLine("   ⚠ 跳过 " + path + "：有 " + stale +
                        " 条已弃用字段覆盖，请双击打开该预制体、在 Inspector 里对「① / ② / ③」那几组右键 Revert。");
                }
            }

            AssetDatabase.SaveAssets();
            report.AppendLine();
            report.AppendLine("完成：处理 " + scenes + " 个场景（预制体资产未自动改动）。");
            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/PAO/泡泡配置/修复缺失的共用配置引用", false, 31)]
        public static void RepairMissingLoadout()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[泡泡配置] 当前场景有未保存改动，已取消修复。");
                return;
            }

            BubbleLoadout loadout = AssetDatabase.LoadAssetAtPath<BubbleLoadout>(kLoadoutPath);
            if (loadout == null)
            {
                Debug.LogError("[泡泡配置] 加载不到 " + kLoadoutPath);
                return;
            }

            StringBuilder report = new StringBuilder("[泡泡配置] 为缺失共用配置的 BubbleLauncher 补上 m_Loadout\n");
            int fixedCount = 0;

            foreach (string path in ScenePaths())
            {
                if (!SceneHasMissingLoadout(path))
                {
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (RepairScene(scene, loadout, report))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    fixedCount++;
                }
            }

            report.AppendLine();
            report.AppendLine(fixedCount == 0
                ? "没有需要修复的引用（所有 BubbleLauncher 都已挂上共用配置）。"
                : "完成：修复了 " + fixedCount + " 个场景。");
            Debug.Log(report.ToString());
        }

        // ---------------------------------------------------------------- 诊断实现

        private static int CompareDiskWithLoaded(StringBuilder report)
        {
            int problems = 0;
            report.AppendLine("① 磁盘文件 vs 引擎当前加载的对象");

            if (!File.Exists(kLoadoutPath))
            {
                report.AppendLine("   ✘ 文件不存在：" + kLoadoutPath);
                return 1;
            }

            string[] diskValues = ReadCostsFromDisk(kLoadoutPath);
            BubbleLoadout loaded = Resources.Load<BubbleLoadout>(BubbleLauncher.SharedLoadoutPath);
            if (loaded == null)
            {
                report.AppendLine("   ✘ Resources.Load<BubbleLoadout>(\"" + BubbleLauncher.SharedLoadoutPath + "\") 返回 null");
                return 1;
            }

            // 只有确认「磁盘上根本没有这个数值」才算问题：
            // Unity 的 ScriptableObject 是二进制序列化的（type: 2），
            // 只要改过并保存过，YAML 里就一定能找到这个数字。
            for (int type = 0; type < 3; type++)
            {
                BubbleSettings settings = loaded.SettingsFor((BubbleType)type);
                string disk = diskValues[type];
                report.AppendLine("   " + kTypeKeys[type].PadRight(7) + " 磁盘(YAML)：minCost=" + disk +
                    "   引擎内：minCost=" + settings.minCost + " / maxCost=" + settings.maxCost);

                bool inDisk = disk != "?" && disk == settings.minCost + "/" + settings.maxCost;
                if (!inDisk)
                {
                    problems++;
                    report.AppendLine("      ⟶ 这个数值在磁盘文件里找不到，说明引擎读到的不是这份文件" +
                        "（可能是别处的覆盖，或 Unity 没重新导入）。");
                }
            }

            report.AppendLine();
            return problems;
        }

        /// <summary>
        /// 直接读 YAML 里各类型段落下的 minCost / maxCost。
        /// 不经过 Inspector，所以不会像 Inspector 那样骗人。
        /// </summary>
        private static string[] ReadCostsFromDisk(string assetPath)
        {
            string[] result = { "?", "?", "?" };
            string[] lines = File.ReadAllLines(assetPath, Encoding.UTF8);
            int type = -1;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                for (int i = 0; i < kTypeKeys.Length; i++)
                {
                    if (trimmed == kTypeKeys[i] + ":")
                    {
                        type = i;
                        break;
                    }
                }

                if (type < 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("minCost:"))
                {
                    result[type] = trimmed.Substring("minCost:".Length).Trim();
                }
                else if (trimmed.StartsWith("maxCost:"))
                {
                    result[type] = result[type] + "/" + trimmed.Substring("maxCost:".Length).Trim();
                }
            }

            return result;
        }

        private static int ScanBundles(List<string> scenePaths, List<string> prefabPaths, StringBuilder report)
        {
            int problems = 0;
            BubbleLoadout shared = AssetDatabase.LoadAssetAtPath<BubbleLoadout>(kLoadoutPath);

            report.AppendLine("② 场景与预制体（路径都在 Assets 内，按文本扫描，已排除 Packages）");

            foreach (string path in scenePaths)
            {
                string bundle = File.ReadAllText(path);

                // BubbleLauncher 组件对应的 guid
                if (!bundle.Contains("guid: " + kLauncherScriptGuid))
                {
                    continue;
                }

                if (SceneBundleNeedsLoadout(bundle))
                {
                    problems++;
                    report.AppendLine("   ✘ " + path + "：有 BubbleLauncher 但没有任何 m_Loadout 引用，也不含玩家预制体实例");
                    report.AppendLine("      ⟶ 运行时靠 Awake 兜底加载 " + kLoadoutPath + "（能跑，但建议修复引用）");
                }

                int stale = CountPrefabOverridePaths(bundle);
                if (stale > 0)
                {
                    problems++;
                    report.AppendLine("   ✘ " + path + "：压着 " + stale + " 条已弃用字段的预制体覆盖（m_*Settings.*）");
                    report.AppendLine("      ⟶ 这些值不会被读到，但会在 Inspector 里显示成「当前配置」，误导调参");
                }
            }

            foreach (string path in prefabPaths)
            {
                string bundle = File.ReadAllText(path);
                if (!bundle.Contains("guid: " + kLauncherScriptGuid))
                {
                    continue;
                }

                int stale = CountPrefabOverridePaths(bundle);
                if (stale > 0)
                {
                    problems++;
                    report.AppendLine("   ✘ " + path + "：有 " + stale + " 条已弃用字段的覆盖（通常来自预制体变体）");
                }
            }

            report.AppendLine();
            report.AppendLine("③ 参考：共用配置 " + kLoadoutPath +
                (shared != null ? "（已加载，sticky/bouncy/bomb 均为 minCost=" + shared.sticky.minCost + "~" + shared.sticky.maxCost + "）" : "（加载失败）"));
            report.AppendLine();
            return problems;
        }

        private static int CountPrefabOverridePaths(string text)
        {
            return kPrefabOverridePath.Matches(text).Count;
        }

        // ---------------------------------------------------------------- 清理 / 修复实现

        private static bool SceneHasStaleOverrides(string path)
        {
            return CountPrefabOverridePaths(File.ReadAllText(path)) > 0;
        }

        private static bool SceneHasMissingLoadout(string path)
        {
            string bundle = File.ReadAllText(path);
            return bundle.Contains("guid: " + kLauncherScriptGuid) && SceneBundleNeedsLoadout(bundle);
        }

        /// <summary>
        /// 场景里到底有没有「读不到共用配置」的发射器。
        ///
        /// 场景 YAML 不存继承自预制体的字段值，所以「场景文本里没有 m_Loadout」
        /// 并不等于「运行时为空」—— 只要场上有玩家预制体实例，m_Loadout 就是从预制体继承来的。
        /// 只有既没有 m_Loadout 文本、又没有玩家预制体实例时，才真的需要修。
        /// </summary>
        private static bool SceneBundleNeedsLoadout(string bundle)
        {
            return !bundle.Contains("m_Loadout:") && !bundle.Contains("guid: " + kPlayerPrefabGuid);
        }

        private static bool CleanScene(Scene scene, StringBuilder report)
        {
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BubbleLauncher launcher in root.GetComponentsInChildren<BubbleLauncher>(true))
                {
                    GameObject owner = launcher.gameObject;
                    List<PropertyModification> stale = StaleModifications(owner);
                    if (stale.Count == 0)
                    {
                        continue;
                    }

                    PropertyModification[] all = PrefabUtility.GetPropertyModifications(owner);
                    List<PropertyModification> keep = new List<PropertyModification>();
                    HashSet<PropertyModification> drop = new HashSet<PropertyModification>(stale);
                    if (all != null)
                    {
                        foreach (PropertyModification item in all)
                        {
                            if (!drop.Contains(item))
                            {
                                keep.Add(item);
                            }
                        }
                    }

                    // 注意：Unity 只有 Get / SetPropertyModifications，
                    // 没有 RemovePropertyModifications —— 去掉一条 = 写回"不含它的完整列表"。
                    PrefabUtility.SetPropertyModifications(owner, keep.ToArray());
                    EditorUtility.SetDirty(owner);
                    changed = true;

                    report.AppendLine("   清掉 " + scene.name + " / " + owner.name + " 的 " + stale.Count + " 条覆盖：");
                    foreach (PropertyModification item in stale)
                    {
                        report.AppendLine("      · " + item.propertyPath + " = " + (item.value ?? "(null)"));
                    }
                }
            }

            return changed;
        }

        private static List<PropertyModification> StaleModifications(GameObject owner)
        {
            List<PropertyModification> stale = new List<PropertyModification>();
            if (!PrefabUtility.IsPartOfPrefabInstance(owner))
            {
                return stale;
            }

            PropertyModification[] all = PrefabUtility.GetPropertyModifications(owner);
            if (all == null)
            {
                return stale;
            }

            foreach (PropertyModification item in all)
            {
                if (string.IsNullOrEmpty(item.propertyPath))
                {
                    continue;
                }

                foreach (string field in kStaleFieldNames)
                {
                    if (item.propertyPath.StartsWith(field))
                    {
                        stale.Add(item);
                        break;
                    }
                }
            }

            return stale;
        }

        private static bool RepairScene(Scene scene, BubbleLoadout loadout, StringBuilder report)
        {
            bool changed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BubbleLauncher launcher in root.GetComponentsInChildren<BubbleLauncher>(true))
                {
                    if (launcher.Loadout != null)
                    {
                        continue;
                    }

                    Undo.RecordObject(launcher, "Assign bubble loadout");
                    launcher.SetLoadout(loadout);
                    EditorUtility.SetDirty(launcher);
                    changed = true;
                    report.AppendLine("   " + scene.name + " / " + launcher.gameObject.name + " ← " + loadout.name);
                }
            }

            return changed;
        }

        // ---------------------------------------------------------------- 路径收集

        private static List<string> ScenePaths()
        {
            return AssetPaths("t:Scene", "Assets");
        }

        private static List<string> PrefabPaths()
        {
            return AssetPaths("t:Prefab", "Assets");
        }

        private static List<string> AssetPaths(string filter, string folder)
        {
            List<string> result = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/") && File.Exists(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }
    }
}
