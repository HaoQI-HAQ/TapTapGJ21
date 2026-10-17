using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 一键删掉场景里所有"旁白触发"物体。
    ///
    /// 【为什么用编辑器脚本，而不是直接改 .unity 文件】
    /// 场景是 Unity 自动分配 fileID 的 YAML ——
    /// 每个物体有 GameObject / Transform / BoxCollider / MonoBehaviour 四五个块，
    /// 互相用 fileID 引用。手删很容易留下悬空引用或者删错同名的块。
    /// 我在这个工程里手改场景 YAML 已经失败过几次，所以一律走 API。
    ///
    /// 【删的是什么】
    /// 挂了 <see cref="NarrationTrigger"/> 组件的物体。
    /// 这些是我给爬塔关加的 6 处旁白触发点（Start / FirstHazard / FirstLanding /
    /// Midway / High / Top）—— 你说不需要对白了，所以清掉。
    ///
    /// **只删这些物体，不碰别的位置和摆放。**
    ///
    /// 菜单：Tools → PAO → 关卡白盒 → 删除 shiliuye map 的旁白触发点
    /// </summary>
    public static class ShiliuyeNarrationRemover
    {
        private const string kScenePath = "Assets/GameMain/Scenes/shiliuye map.unity";

        [MenuItem("Tools/PAO/关卡白盒/删除 shiliuye map 的旁白触发点", priority = 86)]
        public static void RemoveNarration()
        {
            // 只接受"当前打开的就是目标场景"。
            // 不主动 OpenScene —— 那会丢掉未保存的改动（你刚摆好的关卡）。
            Scene scene = SceneManager.GetActiveScene();

            if (scene.path != kScenePath)
            {
                EditorUtility.DisplayDialog("删除旁白触发点",
                    "当前打开的是：\n" + scene.name + "\n\n" +
                    "请先打开 " + kScenePath + " 再执行。",
                    "好");
                return;
            }

            // 收集场景里所有挂了 NarrationTrigger 的物体
            NarrationTrigger[] triggers =
                Object.FindObjectsByType<NarrationTrigger>(FindObjectsSortMode.None);

            if (triggers == null || triggers.Length == 0)
            {
                EditorUtility.DisplayDialog("删除旁白触发点",
                    "场景里没有旁白触发物体（可能已经删过了）。", "好");
                return;
            }

            // 先确认，避免误删
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

            Undo.SetCurrentGroupName("删除旁白触发点");
            int group = Undo.GetCurrentGroup();

            int removed = 0;

            for (int i = 0; i < triggers.Length; i++)
            {
                if (triggers[i] == null)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(triggers[i].gameObject);
                removed++;
            }

            Undo.CollapseUndoOperations(group);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[ShiliuyeNarrationRemover] 已删除 " + removed +
                      " 个旁白触发物体并保存场景。其余摆放未改动。");

            EditorUtility.DisplayDialog("删除旁白触发点",
                "已删除 " + removed + " 个旁白触发物体，场景已保存。\n\n" +
                "如果删错了：Ctrl+Z 可以撤销。", "好");
        }
    }
}
