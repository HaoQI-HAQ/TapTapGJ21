using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 删掉 shiliuye map 里所有"旁白触发"物体（底部对白）。
    ///
    /// 【为什么用编辑器脚本，而不是直接改 .unity 文件】
    /// 场景是 Unity 自动分配 fileID 的 YAML ——
    /// 每个物体有 GameObject / Transform / BoxCollider / MonoBehaviour 四五个块，
    /// 互相用 fileID 引用。手删很容易留下悬空引用或者删错同名的块。
    /// 我在这个工程里手改场景 YAML 已经失败过几次，所以一律走 API。
    ///
    /// 【删的是什么】
    /// 挂了 <see cref="NarrationTrigger"/> 组件的物体。当前场景里有 9 个：
    ///   · 我搭建时加的 6 个：Start / FirstHazard / FirstLanding / Midway / High / Top
    ///   · 被 PAOAudioInstaller 装进来的 3 个：
    ///     Resonance_Core / Memory_Beach / Trench_Shard
    /// （那 3 个本来是给 Map4 的，位置也确实是 Map4 的坐标，不知怎么进到这场景了。
    ///   PAOAudioInstaller 是手动菜单、只针对 Map4，**不会自动加回来**，所以删掉是永久的。）
    ///
    /// **只删这些物体，不碰别的位置和摆放。**
    ///
    /// ============================================================
    /// 【两种执行方式】
    /// ============================================================
    /// 1. 自动（默认）：切回 Unity 时如果打开的就是 shiliuye map，
    ///    会自动清一次并写标记文件，之后就再也不管了。
    ///    这样不需要手动点菜单 —— 之前那版菜单一直没人点，9 个旁白一直留着。
    ///
    /// 2. 手动：Tools → PAO → 关卡白盒 → 删除 shiliuye map 的旁白触发点
    ///    （带确认框，删完可以 Ctrl+Z）
    ///
    /// 【想恢复旁白】
    /// 删掉标记文件 ProjectSettings/PAO_ShiliuyeNarrationRemoved.txt，
    /// 然后重新跑一次「搭建 shiliuye map 白盒」，或者用 PAOAudioInstaller。
    /// </summary>
    public static class ShiliuyeNarrationRemover
    {
        private const string kScenePath = "Assets/GameMain/Scenes/shiliuye map.unity";

        /// <summary>自动执行过的标记。存在就不再自动跑。</summary>
        private const string kMarkerPath = "ProjectSettings/PAO_ShiliuyeNarrationRemoved.txt";

        private const string kMenuPath = "Tools/PAO/关卡白盒/删除 shiliuye map 的旁白触发点";

        /// <summary>
        /// 自动清理只删这 9 个**已知**的旁白点。
        ///
        /// 【为什么不删"所有 NarrationTrigger"】
        /// 自动执行是在打开场景时触发的，如果它删掉"所有"旁白物体，
        /// 那么你**自己新加的**旁白也会被一起删掉 —— 而且是在你不知情的时候。
        /// 所以自动路径只认这份名单，多出来的一个都不碰。
        /// （手动菜单仍然是"删所有"，但那个有确认框、会把名字列给你看。）
        /// </summary>
        private static readonly string[] kKnownNarrationNames =
        {
            // 搭建脚本生成的 6 个
            "Narration_Start",
            "Narration_FirstHazard",
            "Narration_FirstLanding",
            "Narration_Midway",
            "Narration_High",
            "Narration_Top",

            // 被 PAOAudioInstaller 装进来的 3 个（本来是 Map4 的）
            "Narration_Resonance_Core",
            "Narration_Memory_Beach",
            "Narration_Trench_Shard",
        };

        private static bool IsKnownNarration(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            for (int i = 0; i < kKnownNarrationNames.Length; i++)
            {
                if (go.name == kKnownNarrationNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        // ==================== 自动执行 ====================

        [InitializeOnLoadMethod]
        private static void HookAutoRemove()
        {
            // 场景打开时再检查一次 —— 覆盖"打开 Unity 时没开这个场景，
            // 之后再切过来"的情况。
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;

            // 启动时当前场景可能已经就是这个场景了，延迟一帧再查，
            // 避免在资源导入过程中操作场景。
            EditorApplication.delayCall -= TryAutoRemove;
            EditorApplication.delayCall += TryAutoRemove;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            TryAutoRemove();
        }

        private static void TryAutoRemove()
        {
            if (File.Exists(kMarkerPath))
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != kScenePath)
            {
                return;      // 不是目标场景，什么都不做
            }

            // 场景有未保存改动时不动手 —— 自动保存会把玩家没存的东西一起写下去。
            // 这种情况下留给手动菜单处理。
            if (scene.isDirty)
            {
                Debug.Log("[ShiliuyeNarrationRemover] 场景有未保存改动，跳过自动清理。" +
                          "需要清理请用菜单：" + kMenuPath);
                return;
            }

            int removed = RemoveInternal(scene, false, true);

            if (removed >= 0)
            {
                WriteMarker(removed);
            }
        }

        private static void WriteMarker(int removed)
        {
            try
            {
                File.WriteAllText(kMarkerPath,
                    "shiliuye map 的旁白触发点已于 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " 自动删除（" + removed + " 个）。\n" +
                    "想恢复：删掉本文件，再跑一次「搭建 shiliuye map 白盒」。\n");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ShiliuyeNarrationRemover] 写标记文件失败：" + e.Message);
            }
        }

        // ==================== 手动菜单 ====================

        [MenuItem(kMenuPath, priority = 86)]
        public static void RemoveNarration()
        {
            // 不主动 OpenScene —— 那会丢掉未保存的改动（刚摆好的关卡）。
            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != kScenePath)
            {
                EditorUtility.DisplayDialog("删除旁白触发点",
                    "当前打开的是：\n" + scene.name + "\n\n" +
                    "请先打开 " + kScenePath + " 再执行。",
                    "好");
                return;
            }

            NarrationTrigger[] triggers =
                Object.FindObjectsByType<NarrationTrigger>(FindObjectsSortMode.None);

            if (triggers == null || triggers.Length == 0)
            {
                EditorUtility.DisplayDialog("删除旁白触发点",
                    "场景里没有旁白触发物体（可能已经删过了）。", "好");

                // 顺手写标记，免得自动逻辑下次又跑一遍
                if (!File.Exists(kMarkerPath)) { WriteMarker(0); }
                return;
            }

            string names = "";

            for (int i = 0; i < triggers.Length; i++)
            {
                names += "  · " + triggers[i].gameObject.name + "\n";
            }

            bool ok = EditorUtility.DisplayDialog(
                "删除旁白触发点",
                "即将删除这 " + triggers.Length + " 个物体：\n\n" + names +
                "\n只删这些旁白点，不动任何其它摆放。\n继续？",
                "删除", "取消");

            if (!ok)
            {
                return;
            }

            int removed = RemoveInternal(scene, true, false);

            if (!File.Exists(kMarkerPath)) { WriteMarker(removed); }

            EditorUtility.DisplayDialog("删除旁白触发点",
                "已删除 " + removed + " 个旁白触发物体，场景已保存。\n\n" +
                "如果删错了：Ctrl+Z 可以撤销。", "好");
        }

        // ==================== 实际删除 ====================

        /// <summary>
        /// 删掉场景里的 NarrationTrigger 物体并保存场景。
        /// </summary>
        /// <param name="scene">目标场景（必须是已加载的活动场景）</param>
        /// <param name="interactive">true = 记 Undo，可以 Ctrl+Z；自动执行时不需要</param>
        /// <param name="onlyKnown">
        /// true = 只删 <see cref="kKnownNarrationNames"/> 里的 9 个（自动执行走这条，
        ///        避免把用户新加的旁白一起删掉）；
        /// false = 删场景里所有 NarrationTrigger（手动菜单走这条，带确认框）
        /// </param>
        /// <returns>删掉的个数；-1 表示出错没删</returns>
        private static int RemoveInternal(Scene scene, bool interactive, bool onlyKnown)
        {
            NarrationTrigger[] triggers;

            try
            {
                triggers = Object.FindObjectsByType<NarrationTrigger>(FindObjectsSortMode.None);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ShiliuyeNarrationRemover] 查找旁白失败：" + e.Message);
                return -1;
            }

            if (triggers == null || triggers.Length == 0)
            {
                return 0;
            }

            int group = -1;

            if (interactive)
            {
                Undo.SetCurrentGroupName("删除旁白触发点");
                group = Undo.GetCurrentGroup();
            }

            int removed = 0;

            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] == null)
                {
                    continue;
                }

                // 自动路径只认名单里的名字 —— 别的一律不碰
                if (onlyKnown && !IsKnownNarration(triggers[i].gameObject))
                {
                    continue;
                }

                if (interactive)
                {
                    Undo.DestroyObjectImmediate(triggers[i].gameObject);
                }
                else
                {
                    Object.DestroyImmediate(triggers[i].gameObject);
                }

                removed++;
            }

            if (interactive && group >= 0)
            {
                Undo.CollapseUndoOperations(group);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[ShiliuyeNarrationRemover] 已删除 " + removed +
                      " 个旁白触发物体并保存场景。其余摆放未改动。");

            return removed;
        }
    }
}
