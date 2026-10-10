using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡玩法与泡泡潮之间的唯一桥梁。
    ///
    /// 【为什么要有这么一个文件】
    /// 直接让 Bubble.cs / TerrainBubble.cs 去调 BubbleTideDirector 当然也能跑，
    /// 但那样玩法代码就硬依赖了潮水系统 —— 换个关卡、或者把潮水整个删掉，
    /// 玩法就会编译不过。这里把所有跨系统的调用收在一处，并且**每个调用都判空**：
    /// 场景里没有潮水时全部静默跳过，玩法一点不受影响。
    ///
    /// 用法就一句话：玩法里想在某个时机影响潮水，就调这里对应的方法。
    ///
    /// 【三条反制分别是什么】
    ///   炸弹泡泡爆炸   → 在潮水里炸出一个洞（局部把潮水往后推几秒）
    ///   黏浮泡泡粘成墙 → 那片区域潮水变慢（玩家自己筑的堤坝）
    ///   弹力泡泡被地形吸收 → 等于从潮水里抽走一份容量，整条潮水一起变慢
    ///
    /// 这三条是让「泡泡潮」从一根会动的血条变成可玩系统的关键 ——
    /// 没有它们，玩家面对潮水只能跑；有了它们，玩家能主动争取时间。
    /// </summary>
    public static class BubbleTideBridge
    {
        /// <summary>场景里有没有可用的潮水。玩法代码可以拿它来决定要不要显示相关提示。</summary>
        public static bool HasTide
        {
            get
            {
                BubbleTideDirector director = BubbleTideDirector.Instance;
                return director != null && director.Field != null && director.Field.IsBaked;
            }
        }

        /// <summary>
        /// 炸弹泡泡爆炸了 → 在潮水里炸出一个洞。
        /// 由 Bubble.ExplodeWithMultiplier 调用。
        /// </summary>
        /// <param name="center">爆炸中心（世界坐标）。</param>
        /// <param name="blastRadius">爆炸半径（米）。</param>
        /// <returns>被推迟的格子数。没有潮水时返回 0。</returns>
        public static int OnBombExploded(Vector3 center, float blastRadius)
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            if (director == null)
            {
                return 0;
            }

            return director.OnBombExploded(center, blastRadius);
        }

        /// <summary>
        /// 黏浮泡泡粘住变成墙了 → 那片区域潮水变慢。
        /// 由 Bubble.StickToSurface / Bubble.BecomeStuck 调用。
        /// </summary>
        /// <param name="center">泡泡的位置（世界坐标）。</param>
        /// <param name="radius">影响半径（米）。传 0 或负数就用 Director 上的默认值。</param>
        /// <returns>被推迟的格子数。没有潮水时返回 0。</returns>
        public static int OnStickyBubbleAnchored(Vector3 center, float radius)
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            if (director == null)
            {
                return 0;
            }

            return director.OnStickyBubbleAnchored(center, radius);
        }

        /// <summary>
        /// 弹力泡泡被地形泡泡吸收了 → 整条潮水变慢一点点。
        /// 由 TerrainBubble.Absorb 调用。
        /// </summary>
        /// <param name="slowdownRatio">
        /// 每抽走一份容量慢多少。0.02 = 慢 2%。
        /// 千万别填大 —— 一个地形泡泡能吸好几颗，全局减速是乘算的，很容易调过头。
        /// </param>
        public static void OnBubbleConsumedByTerrain(float slowdownRatio)
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            if (director == null)
            {
                return;
            }

            director.OnBubbleConsumedByTerrain(slowdownRatio);
        }

        /// <summary>
        /// 玩家当前位置距离潮水面还有多远（米）。负值表示已经被淹。
        /// UI 想显示「危险」提示时可以读它。没有潮水时返回一个很大的正数。
        /// </summary>
        public static float GetDistanceToTideFront(Vector3 worldPosition)
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            if (director == null || director.Field == null || !director.Field.IsBaked)
            {
                return float.PositiveInfinity;
            }

            float arrival = director.Field.SampleArrivalTime(worldPosition);
            if (float.IsPositiveInfinity(arrival))
            {
                return float.PositiveInfinity;
            }

            return (arrival - director.Elapsed) * director.Field.EstimatedSpeed;
        }

        /// <summary>离终点被淹还剩多少秒。负值表示已经过了。</summary>
        public static float GetTimeUntilGoalFlooded()
        {
            BubbleTideDirector director = BubbleTideDirector.Instance;
            return director != null ? director.TimeUntilGoalFlooded : float.PositiveInfinity;
        }
    }
}
