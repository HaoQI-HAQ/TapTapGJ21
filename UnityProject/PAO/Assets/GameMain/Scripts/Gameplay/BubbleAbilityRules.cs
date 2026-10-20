using System.Collections.Generic;
using PAO.UI.V2;
using UnityEngine.SceneManagement;

namespace PAO
{
    /// <summary>
    /// 首周目的能力限制。
    ///
    /// 规则表（没列到的关卡一律三种泡泡全开）：
    ///   Map1  第一次进入：只有弹力
    ///   Map3  第一次进入：弹力 + 炸弹（黏浮仍锁）
    ///
    /// 「解除限制」有两个入口：
    ///   · 通关：UIRunState.Complete 会顺手记下；
    ///   · 从章节地图进入：视为自由练习，进入前就解除（BeginFreePlay）。
    ///
    /// 解除记录写在 RunProgress.abilityTutorialsCleared 里，随存档保存。
    /// 因此读档、死亡重开（整场景重载）都不会把限制重新打开 ——
    /// 标记只能打在「离开这一关」的一侧，不能打在「进入场景」的一侧。
    /// </summary>
    public static class BubbleAbilityRules
    {
        /// <summary>空数组 = 不限制。</summary>
        private static readonly BubbleType[] s_Unrestricted = new BubbleType[0];

        /// <summary>场景名 → 首周目允许的泡泡。Map2 / Map2_5 已是废案，不进表。</summary>
        private static readonly Dictionary<string, BubbleType[]> s_FirstRunLoadouts =
            new Dictionary<string, BubbleType[]>
            {
                { "Map1", new[] { BubbleType.Bouncy } },
                { "Map3", new[] { BubbleType.Bouncy, BubbleType.Bomb } },
            };

        // 结果按「场景实例 + 存档实例」缓存：换场景、换存档时才重算。
        private static int s_SceneHandle = int.MinValue;
        private static RunProgress s_Progress;
        private static BubbleType[] s_Allowed = s_Unrestricted;

        public static bool IsAvailable(BubbleType type)
        {
            if (type < BubbleType.Sticky || type > BubbleType.Bomb) return false;

            BubbleType[] allowed = AllowedForActiveScene();
            if (allowed.Length == 0) return true;

            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == type) return true;
            }

            return false;
        }

        public static BubbleType Resolve(BubbleType requested)
        {
            return IsAvailable(requested) ? requested : BubbleType.Bouncy;
        }

        /// <summary>
        /// 从章节地图进入某关：当作自由练习，直接解除这一关的首周目限制。
        /// 必须在 LoadScene 之前调用 —— 切换器是在新场景 Awake 时读规则的。
        /// </summary>
        public static void BeginFreePlay(string scene)
        {
            UIRunState.ClearAbilityTutorial(scene);
        }

        private static BubbleType[] AllowedForActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            RunProgress progress = UIRunState.Progress;

            if (scene.handle != s_SceneHandle || !ReferenceEquals(progress, s_Progress))
            {
                s_SceneHandle = scene.handle;
                s_Progress = progress;
                s_Allowed = AllowedIn(scene.name, progress);
            }

            return s_Allowed;
        }

        private static BubbleType[] AllowedIn(string sceneName, RunProgress progress)
        {
            BubbleType[] loadout;
            if (string.IsNullOrEmpty(sceneName) || !s_FirstRunLoadouts.TryGetValue(sceneName, out loadout))
            {
                return s_Unrestricted;
            }

            if (progress != null && progress.abilityTutorialsCleared != null &&
                progress.abilityTutorialsCleared.Contains(sceneName))
            {
                return s_Unrestricted;
            }

            return loadout;
        }
    }
}
