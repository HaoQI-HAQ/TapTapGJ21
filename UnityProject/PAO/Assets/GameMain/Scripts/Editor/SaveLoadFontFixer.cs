using System.IO;
using UnityEditor;
using UnityEngine;
using TMPro;   // TMP_FontAsset

namespace PAO.EditorTools
{
    /// <summary>
    /// 把中文字体放进 Resources，让运行时新建的 UI 文字不再是方块。
    ///
    /// 【为什么需要这一步】
    /// TMP 默认字体 LiberationSans **不含汉字**。用代码新建的 TMP 文字如果不显式指定
    /// 中文字体，中文会全部画成 □。而现有的中文字体在
    /// Assets/GameMain/Art/Fonts/26汉仪趣报简 SDF.asset —— 那个目录不在 Resources 下，
    /// 运行时 Resources.Load 取不到。
    ///
    /// 所以本工具把字体**复制**一份到 Assets/GameMain/Resources/PAO/ChineseFont.asset。
    /// 改名为 .asset 是必要的：Resources.Load&lt;TMP_FontAsset&gt; 按扩展名过滤。
    ///
    /// 用法：菜单 **PAO → UI → 修复存档面板字体**
    /// 幂等：目标已存在就跳过。
    /// </summary>
    public static class SaveLoadFontFixer
    {
        private const string kSourceFont = "Assets/GameMain/Art/Fonts/26汉仪趣报简 SDF.asset";
        private const string kTargetFolder = "Assets/GameMain/Resources/PAO";
        private const string kTargetFont = kTargetFolder + "/ChineseFont.asset";
        private const string kAutoKey = "PAO.SaveLoadFont.Fixed.v1";

        [MenuItem("PAO/UI/修复存档面板字体", false, 22)]
        public static void RunFromMenu()
        {
            bool changed = EnsureFont(out string message);

            EditorUtility.DisplayDialog("存档面板字体", message, "好");

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>确保中文字体在 Resources 下。返回是否做了改动。</summary>
        public static bool EnsureFont(out string message)
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kTargetFont) != null)
            {
                message = "字体已就位：\n" + kTargetFont + "\n\n无需改动。";
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kSourceFont) == null)
            {
                message = "找不到源字体：\n" + kSourceFont + "\n\n请确认文件是否存在（文件名含中文和空格）。";
                Debug.LogError("[SaveLoadFontFixer] " + message);
                return false;
            }

            // 目标目录可能不存在，先建出来
            if (!Directory.Exists(kTargetFolder))
            {
                Directory.CreateDirectory(kTargetFolder);
                AssetDatabase.Refresh();
            }

            // 用 Unity 的复制接口，它会同时处理 .meta（生成新 GUID）。
            // 注意：CopyAsset 返回的是 bool（成功/失败），不是错误信息字符串。
            bool copied = AssetDatabase.CopyAsset(kSourceFont, kTargetFont);

            if (!copied)
            {
                message = "复制字体失败。\n源：" + kSourceFont + "\n目标：" + kTargetFont +
                          "\n\n请确认目标目录可写，或手动把字体拖过去并改名为 ChineseFont.asset。";
                Debug.LogError("[SaveLoadFontFixer] " + message);
                return false;
            }

            AssetDatabase.Refresh();

            bool ok = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kTargetFont) != null;

            message = ok
                ? "完成。中文字体已复制到：\n" + kTargetFont + "\n\n存档面板的中文不会再是方块了。"
                : "复制完成，但读不到 TMP_FontAsset。请手动检查 " + kTargetFont;

            Debug.Log("[SaveLoadFontFixer] " + message.Replace("\n", " "));
            return ok;
        }

        // ==================== 自动执行一次 ====================

        [InitializeOnLoadMethod]
        private static void HookAutoFix()
        {
            // 故意不做 EditorPrefs 早退 —— 那个标记跨会话持久，一旦置位就永远不再执行
            // （这个坑在别的构建脚本上踩过）
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    {
                        return;
                    }

                    if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kTargetFont) != null)
                    {
                        return;   // 已经有了
                    }

                    if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(kSourceFont) == null)
                    {
                        return;   // 源字体都没有，别折腾
                    }

                    string message;
                    if (EnsureFont(out message))
                    {
                        EditorPrefs.SetBool(kAutoKey, true);
                        AssetDatabase.SaveAssets();
                        Debug.Log("[SaveLoadFontFixer] 已自动把中文字体复制到 Resources/PAO/ChineseFont.asset");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[SaveLoadFontFixer] 自动执行失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 修复存档面板字体 手动执行。");
                }
            };
        }
    }
}
