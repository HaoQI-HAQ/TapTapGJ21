using UnityEngine;
using TMPro;   // TMP_FontAsset / TMP_Settings

namespace PAO.UI
{
    /// <summary>
    /// 给运行时生成的界面文字套上中文字体。
    ///
    /// 【为什么必须做这件事】
    /// TextMeshPro 的默认字体是 LiberationSans，**不含汉字**。
    /// 运行时用代码新建的 TMP 文字如果不显式指定中文字体，中文会全部画成方块 □。
    ///
    /// 【字体从哪来】
    /// 现有中文字体在 Assets/GameMain/Art/Fonts/26汉仪趣报简 SDF.asset，
    /// 但那个目录**不在 Resources 下**，运行时 Resources.Load 取不到。
    /// 所以这里按优先级找：
    ///   1. Resources/PAO/ChineseFont   （推荐：把字体复制进 Resources 即可，一次搞定）
    ///   2. TMP 全局回退字体列表里的字体（FontSwapTool 可能已经配好）
    ///   3. TMP 默认字体（中文会是方块，会打一条明确的警告）
    ///
    /// 如果走的是第 3 条，Console 会告诉你怎么把字体放进 Resources。
    /// </summary>
    public static class SaveLoadFont
    {
        /// <summary>中文字体在 Resources 下的路径（不带扩展名）。</summary>
        public const string FontResourcePath = "PAO/ChineseFont";

        private static TMP_FontAsset s_Font;
        private static bool s_Resolved;
        private static bool s_Warned;

        /// <summary>取中文字体；取不到返回 null（调用方会退回默认字体）。</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (!s_Resolved)
                {
                    s_Resolved = true;
                    s_Font = Resolve();
                }

                return s_Font;
            }
        }

        /// <summary>把中文字体套到一段文字上。取不到字体时保留原样。</summary>
        public static void Apply(TMPro.TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset font = Font;
            if (font == null)
            {
                WarnOnce();
                return;
            }

            text.font = font;

            // 只换 font 不换 material，文字会拿旧材质采样新图集 → 什么都画不出来。
            // 这个坑在换字体工具里踩过，这里一并处理。
            if (font.material != null)
            {
                text.fontSharedMaterial = font.material;
            }
        }

        // ==================== 内部 ====================

        private static TMP_FontAsset Resolve()
        {
            // ① 首选：Resources 下放好的中文字体
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>(FontResourcePath);
            if (font != null)
            {
                return font;
            }

            // ② 其次：TMP 全局回退字体里的第一个（FontSwapTool 或 TMP Settings 可能已经配了）
            if (TMP_Settings.fallbackFontAssets != null)
            {
                for (int i = 0; i < TMP_Settings.fallbackFontAssets.Count; i++)
                {
                    if (TMP_Settings.fallbackFontAssets[i] != null)
                    {
                        return TMP_Settings.fallbackFontAssets[i];
                    }
                }
            }

            // ③ 都不行：返回 null，调用方会退回默认字体（中文会变方块）
            return null;
        }

        private static void WarnOnce()
        {
            if (s_Warned)
            {
                return;
            }

            s_Warned = true;

            Debug.LogWarning("[SaveLoadFont] 找不到中文字体，存档面板的中文会显示成方块 □。\n" +
                             "解决办法（任选其一）：\n" +
                             "  1. 把 Assets/GameMain/Art/Fonts/26汉仪趣报简 SDF.asset 复制到 " +
                             "Assets/GameMain/Resources/PAO/ 下，重命名为 ChineseFont；\n" +
                             "  2. 菜单 PAO → UI → 修复存档面板字体（自动完成第 1 步）。");
        }
    }
}
