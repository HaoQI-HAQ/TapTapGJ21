using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 诊断「shiliuye map 地形为什么没更新」。
    ///
    /// 【为什么单独写一个诊断，而不是直接猜】
    /// 地形没变化有好几种可能，现象却长得一样：
    ///   · 场景里还是旧版地形（标记文件没写 / 脚本没重编译）
    ///   · 地形压根没搭过（钩子从没触发）
    ///   · 场景根本没保存（改了但没落盘）
    ///   · 用户看的是别的东西（比如另一个场景）
    /// 光看截图分不出来，所以直接把每一条事实打出来。
    ///
    /// 菜单：**Tools → PAO → 关卡白盒 → 诊断 shiliuye map**
    /// </summary>
    public static class ShiliuyeDiagnose
    {
        private const string kScenePath = "Assets/GameMain/Scenes/shiliuye map.unity";
        private const string kRootName = "Shiliuye_Whitebox";

        [MenuItem("Tools/PAO/关卡白盒/诊断 shiliuye map", priority = 83)]
        public static void Diagnose()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========== shiliuye map 地形诊断 ==========");

            // ---- 1. 当前打开的场景 ----
            Scene active = SceneManager.GetActiveScene();
            sb.AppendLine("[1] 当前打开的场景");
            sb.AppendLine("    名字   : " + active.name);
            sb.AppendLine("    路径   : " + (string.IsNullOrEmpty(active.path) ? "(未保存的新场景)" : active.path));
            sb.AppendLine("    是目标 : " + (active.path == kScenePath ? "是" : "**不是** ← 自动钩子只在目标场景里动手"));
            sb.AppendLine("    有改动 : " + (active.isDirty ? "**有未保存改动**" : "无"));

            // ---- 2. 场景里有没有地形根物体 ----
            sb.AppendLine();
            sb.AppendLine("[2] 地形根物体 \"" + kRootName + "\"");
            GameObject root = null;

            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == kRootName)
                {
                    root = go;
                    break;
                }
            }

            if (root == null)
            {
                sb.AppendLine("    **找不到** → 这个场景里还没有地形（或者你看的不是目标场景）");
            }
            else
            {
                sb.AppendLine("    位置   : " + root.transform.position);
                sb.AppendLine("    缩放   : " + root.transform.localScale);
                sb.AppendLine("    子物体 : " + root.transform.childCount + " 个");

                // ---- 3. 版本戳 ----
                LayoutVersionStamp stamp = root.GetComponentInChildren<LayoutVersionStamp>(true);
                sb.AppendLine();
                sb.AppendLine("[3] 版本戳");
                if (stamp == null)
                {
                    sb.AppendLine("    **没有版本戳** → 这份地形是用「还没加版本戳」的旧脚本搭的");
                    sb.AppendLine("    → 自动钩子会认为版本 0，应该会重搭。如果没重搭，看 [5]。");
                }
                else
                {
                    sb.AppendLine("    场景里的版本 : " + stamp.Version);
                    sb.AppendLine("    脚本要求版本 : 2（写成字面量，方便对比；以脚本里的常量为准）");
                    sb.AppendLine("    " + (stamp.Version >= 2 ? "✓ 是最新版" : "**是旧版** → 需要重搭"));
                }

                // ---- 4. 关键物体在不在（用名字判断是新版还是旧版）----
                sb.AppendLine();
                sb.AppendLine("[4] 新旧版特征物体");

                string[] v2Markers =
                {
                    "Start_Wall", "Climb_Shelf_01", "Chest_Bouncy", "Chest_Bomb",
                    "Gap_Island_01", "PinkWall_Main", "Chest_Extra"
                };

                string[] v1Markers =
                {
                    "Bounce_Tower_Top", "Chasm_FarLedge", "Sticky_Anchor_01",
                    "SideRoute_Pad_00", "Final_Runway"
                };

                int v2Found = CountFound(root.transform, v2Markers);
                int v1Found = CountFound(root.transform, v1Markers);

                sb.AppendLine("    新版(v2)特征  : " + v2Found + " / " + v2Markers.Length);
                sb.AppendLine("    旧版(v1)特征  : " + v1Found + " / " + v1Markers.Length);
                sb.AppendLine();

                if (v2Found > 0 && v1Found == 0)
                {
                    sb.AppendLine("    → 判定：**新版地形**，已经搭好了。");
                }
                else if (v1Found > 0 && v2Found == 0)
                {
                    sb.AppendLine("    → 判定：**旧版地形**，需要用菜单重搭一次。");
                    sb.AppendLine("       菜单：Tools → PAO → 关卡白盒 → 搭建 shiliuye map");
                }
                else if (v2Found == 0 && v1Found == 0)
                {
                    sb.AppendLine("    → 判定：两版特征都没有，地形可能是空的或结构不对。");
                }
                else
                {
                    sb.AppendLine("    → 判定：新旧混杂 —— 可能是搭到一半失败，建议重搭。");
                }
            }

            // ---- 5. 自动搭建标记 ----
            sb.AppendLine();
            sb.AppendLine("[5] 自动搭建标记文件");
            string markerPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath),
                "ProjectSettings", "PAO_ShiliuyeAutoBuild.txt");

            sb.AppendLine("    路径 : " + markerPath);

            if (System.IO.File.Exists(markerPath))
            {
                sb.AppendLine("    内容 : " + System.IO.File.ReadAllText(markerPath).Trim());
                sb.AppendLine("    → 标记 >= 2 的话，自动钩子不会再自动重搭（设计如此）。");
            }
            else
            {
                sb.AppendLine("    **不存在** → 说明自动钩子那套「主动打开+搭建」的逻辑**还没跑过**。");
                sb.AppendLine("    → 可能原因：脚本还没重编译（切回 Unity 等一下），或者编译报错了。");
                sb.AppendLine("    → 查 Console 有没有红色 error；有的话先把编译错误修掉。");
            }

            // ---- 6. 场景文件落盘时间 ----
            sb.AppendLine();
            sb.AppendLine("[6] 场景文件");
            string fullScenePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), kScenePath);

            if (System.IO.File.Exists(fullScenePath))
            {
                var info = new System.IO.FileInfo(fullScenePath);
                sb.AppendLine("    大小     : " + (info.Length / 1024) + " KB");
                sb.AppendLine("    最后写入 : " + info.LastWriteTime);
                sb.AppendLine("    → 如果这个时间比你改动脚本的时间早，说明场景没被重新保存过。");
            }
            else
            {
                sb.AppendLine("    **文件不存在** : " + fullScenePath);
            }

            sb.AppendLine();
            sb.AppendLine("==========================================");

            Debug.Log(sb.ToString());
        }

        private static int CountFound(Transform root, string[] names)
        {
            int found = 0;

            for (int i = 0; i < names.Length; i++)
            {
                if (FindDeep(root, names[i]) != null)
                {
                    found++;
                }
            }

            return found;
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
    }
}
