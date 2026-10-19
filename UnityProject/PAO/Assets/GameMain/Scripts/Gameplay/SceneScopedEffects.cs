using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO
{
    /// <summary>
    /// 按场景名决定"这一关要不要播特效"。
    ///
    /// 【为什么需要这个】
    /// 泡泡的爆炸 / 溶解特效是加在 <see cref="Bubble"/> 上的，而 Bubble 是
    /// **所有关卡共用**的脚本 —— 直接在里面调 VFX，等于给每个场景都加上特效。
    ///
    /// 项目里还有别的目录里的场景（例如 Assets/Scenes/cs.unity），
    /// 那些场景的风格和节奏该由它们自己定。所以这里做成**按场景名白名单**：
    /// 只有列在这里的场景才播，其余一行代码都不碰。
    /// 当前名单已经是 Assets/GameMain/Scenes/ 下的全部关卡。
    ///
    /// 【和 AbilityGateLevel 是同一个套路】
    /// 那套能力锁也是"运行时按场景名判断"，因为它当时挂在共享的玩家预制体上。
    /// 这里换成静态工具类，因为特效是从 Bubble 里调的，没有 MonoBehaviour 可挂。
    ///
    /// 【怎么让别的关卡也播】
    /// 把它们加进 <see cref="kVfxScenes"/> 就行。
    ///
    /// 【怎么关掉全部特效】
    /// 把数组清空，或者把 <see cref="Enabled"/> 改成 false。
    /// </summary>
    public static class SceneScopedEffects
    {
        /// <summary>总开关。排查"特效到底有没有跑"时可以临时关掉。</summary>
        public static bool Enabled = true;

        /// <summary>
        /// 会播放泡泡特效的场景（不带 .unity）。
        /// **只有列在这里的场景才播**，别的地图完全不受影响。
        ///
        /// 当前范围：Assets/GameMain/Scenes/ 下的**全部关卡**。
        /// 别的目录里的场景（例如 Assets/Scenes/cs.unity）仍然不在名单里，
        /// 要加就把场景名照抄进来。
        /// </summary>
        private static readonly string[] kVfxScenes =
        {
            "Map0",
            "Map1",
            "Map2",
            "Map2_5",
            "Map3",
            "Map3_5",
            "Map4",
            "Map5",
            "shiliuye map",
            "wengjie",
            "洛琪希",
        };

        /// <summary>当前场景要不要播泡泡特效。</summary>
        public static bool BubbleVfxAllowed
        {
            get
            {
                if (!Enabled)
                {
                    return false;
                }

                return IsVfxScene(SceneManager.GetActiveScene().name);
            }
        }

        /// <summary>指定场景名要不要播。</summary>
        public static bool IsVfxScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            for (int i = 0; i < kVfxScenes.Length; i++)
            {
                if (kVfxScenes[i] == sceneName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
