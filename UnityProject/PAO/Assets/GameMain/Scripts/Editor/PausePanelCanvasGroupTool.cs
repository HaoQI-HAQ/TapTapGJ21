using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>
    /// 给暂停面板预制体补一个 CanvasGroup。
    ///
    /// 【为什么需要它】
    /// PauseMenu 原本用 gameObject.SetActive(false) 来隐藏界面，这会**关掉整个物体** ——
    /// 挂在同一物体上的 Image、CanvasRenderer、以及 PauseMenu 自己全部停用。
    /// 在 Inspector 里看起来就像「连 Image 也被关掉了」，而且 PauseMenu 停了自己，
    /// 下次就没人收 Esc。
    ///
    /// 改成用 CanvasGroup 的 alpha 控制显隐后，物体始终保持激活，只有画面消失。
    /// 运行时 PauseMenu 也会自动补一个，但把组件预先写进预制体更干净 ——
    /// 编辑器里不需要进 Play 就能看到正确的组件结构。
    ///
    /// 用法：菜单 **PAO → UI → 给暂停面板补 CanvasGroup**
    /// 幂等：已经有了就跳过。
    /// </summary>
    public static class PausePanelCanvasGroupTool
    {
        private const string kPrefabPath = "Assets/GameMain/UI/Pause Panel.prefab";
        private const string kAutoKey = "PAO.PausePanelCanvasGroup.v1";

        [MenuItem("PAO/UI/给暂停面板补 CanvasGroup", false, 28)]
        public static void RunFromMenu()
        {
            bool changed = Apply(true);
            EditorUtility.DisplayDialog("暂停面板 CanvasGroup",
                changed ? "已给暂停面板补上 CanvasGroup，预制体已保存。"
                        : "暂停面板已经有 CanvasGroup，无需改动。",
                "好");
        }

        /// <summary>给预制体根物体加 CanvasGroup，返回是否做了改动。</summary>
        public static bool Apply(bool verbose)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(kPrefabPath);
            if (root == null)
            {
                Debug.LogError("[PausePanelCanvasGroupTool] 打不开预制体：" + kPrefabPath);
                return false;
            }

            bool changed = false;

            try
            {
                if (root.GetComponent<CanvasGroup>() != null)
                {
                    if (verbose)
                    {
                        Debug.Log("[PausePanelCanvasGroupTool] 已有 CanvasGroup，跳过。");
                    }
                }
                else
                {
                    CanvasGroup group = root.AddComponent<CanvasGroup>();
                    group.alpha = 1f;
                    group.interactable = true;
                    group.blocksRaycasts = true;

                    changed = true;

                    if (verbose)
                    {
                        Debug.Log("[PausePanelCanvasGroupTool] 已给 \"" + root.name + "\" 添加 CanvasGroup。");
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, kPrefabPath);
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changed;
        }

        // ==================== 自动执行一次 ====================

        [InitializeOnLoadMethod]
        private static void HookAutoApply()
        {
            // 故意不查 EditorPrefs 做早退 —— 那个标记跨会话持久，
            // 一旦置位就永远不会再跑（已经因为这个坑浪费过好几轮）。
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    {
                        return;
                    }

                    if (AssetDatabase.LoadAssetAtPath<GameObject>(kPrefabPath) == null)
                    {
                        return;
                    }

                    bool changed = Apply(false);
                    EditorPrefs.SetBool(kAutoKey, true);

                    if (changed)
                    {
                        Debug.Log("[PausePanelCanvasGroupTool] 已自动给暂停面板预制体补上 CanvasGroup。");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[PausePanelCanvasGroupTool] 自动执行失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 给暂停面板补 CanvasGroup 手动执行。");
                }
            };
        }
    }
}
