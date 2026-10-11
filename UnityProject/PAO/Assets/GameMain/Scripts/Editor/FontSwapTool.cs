using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把界面上所有 TMP 文字换成中文 SDF 字体。
    ///
    /// 为什么需要它：Unity 内置的 LiberationSans SDF 只覆盖 ASCII，
    /// 中文会一个字都画不出来（日志里刷 "The character with Unicode value ... was not found
    /// in the [LiberationSans SDF] font asset"，画面上显示成 □）。
    ///
    /// 字体资源本身是完整的（101 字符 / 101 glyph，shader 也能解析到），
    /// 所以只要把每个 TMP_Text 的 font 指过去即可 —— font 和 material 一起设，
    /// 否则会出现「有新图集、却还在用旧材质」的错配。
    ///
    /// 用法：菜单 **PAO → UI → 全部换成中文 SDF 字体**
    /// 幂等：已经是目标字体的会跳过。
    /// </summary>
    public static class FontSwapTool
    {
        private const string kFontPath = "Assets/GameMain/Art/Fonts/26汉仪趣报简 SDF.asset";

        private static readonly string[] kScenes =
        {
            "Assets/GameMain/UI/Menu.unity",
            "Assets/GameMain/UI/Control UI.unity"
        };

        private const string kAutoApplyKey = "PAO.FontSwap.AutoApplied.v1";

        [MenuItem("PAO/UI/全部换成中文 SDF 字体", false, 30)]
        public static void ApplyFromMenu()
        {
            int total = ApplyToAllScenes(true);
            EditorUtility.DisplayDialog("替换中文字体",
                total > 0
                    ? "已替换 " + total + " 处文字，场景已保存。"
                    : "所有文字已经在使用目标字体，无需改动。",
                "好");
        }

        /// <summary>把两个场景里所有 TMP 文字换成目标字体，返回改动的组件数。</summary>
        public static int ApplyToAllScenes(bool showLog)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kFontPath);
            if (font == null)
            {
                Debug.LogError("[FontSwapTool] 找不到字体资源：" + kFontPath);
                return 0;
            }

            string current = SceneManager.GetActiveScene().path;
            int total = 0;

            foreach (string path in kScenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning("[FontSwapTool] 场景不存在，跳过：" + path);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int changed = ApplyToScene(font);

                if (changed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log("[FontSwapTool] " + System.IO.Path.GetFileName(path) +
                              "：替换了 " + changed + " 处文字。");
                }
                else if (showLog)
                {
                    Debug.Log("[FontSwapTool] " + System.IO.Path.GetFileName(path) + "：无需改动。");
                }

                total += changed;
            }

            // 回到原来打开的场景，别把用户正在编辑的场景换掉
            if (!string.IsNullOrEmpty(current) && System.IO.File.Exists(current))
            {
                EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
            }

            AssetDatabase.SaveAssets();
            return total;
        }

        /// <summary>替换当前已加载场景里所有 TMP 文字的字体。</summary>
        public static int ApplyToScene(TMP_FontAsset font)
        {
            if (font == null)
            {
                return 0;
            }

            // 材质要用字体自己的 —— 只换 fontAsset 不换 sharedMaterial，
            // 文字会拿旧材质去采样新图集，直接画不出来。
            Material mat = font.material;
            int changed = 0;

            TMP_Text[] all = Object.FindObjectsOfType<TMP_Text>(true);
            for (int i = 0; i < all.Length; i++)
            {
                TMP_Text text = all[i];
                if (text == null)
                {
                    continue;
                }

                if (text.font == font && (mat == null || text.fontSharedMaterial == mat))
                {
                    continue;   // 已经是目标字体
                }

                text.font = font;
                if (mat != null)
                {
                    text.fontSharedMaterial = mat;
                }

                EditorUtility.SetDirty(text);
                changed++;
            }

            return changed;
        }

        // ==================== 自动执行一次 ====================

        [InitializeOnLoadMethod]
        private static void AutoApplyOnce()
        {
            if (EditorPrefs.GetBool(kAutoApplyKey, false))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    {
                        return;
                    }

                    int total = ApplyToAllScenes(false);
                    // 只有真的动过、或确认无需改动时才打标，
                    // 免得中途被 Play 打断后再也不重试
                    EditorPrefs.SetBool(kAutoApplyKey, true);

                    if (total > 0)
                    {
                        Debug.Log("[FontSwapTool] 已把 " + total + " 处文字换成中文字体（26汉仪趣报简 SDF）。");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[FontSwapTool] 自动替换失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 全部换成中文 SDF 字体 手动执行。");
                }
            };
        }
    }
}
