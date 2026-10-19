using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 炸弹爆炸特效：实例化第三方包 VFX_Klaus 的 FX_splash_magic_01_air。
    ///
    /// 【为什么单独包一层，而不是在 Bubble 里直接 Instantiate】
    /// 这个预制体有三件事必须每帧都做对，散在调用点很容易漏：
    ///
    ///   1. 七个子粒子系统的 <b>Scaling Mode 都是 Local</b>。
    ///      Local 只认自己这一层的 transform，父级的缩放会被直接忽略 ——
    ///      这就是「只改父级没用，得父级加子级一起改」的根源。
    ///      这里统一改成 Hierarchy，之后只缩放根节点，整团特效就一起缩。
    ///
    ///   2. 预制体是按「大场面」授权的（粒子尺寸 5~10、速度 25+、整体十几米），
    ///      必须按爆炸半径缩到 0.1~0.9 之间，才配得上 0.3~1.8 米的爆炸。
    ///
    ///   3. 颜色全部写在粒子系统里（贴图只是遮罩），得按泡泡类型重新上色，
    ///      见 <see cref="ExplosionPalette"/>。
    ///
    /// 【为什么大小直接跟「爆炸半径」走】
    /// 现在半径本身就跟蓄力走（0.3 → 0.6），被吸收后再乘吸收倍率。
    /// 视觉只要和半径成固定比例，就一定和判定范围对得上，
    /// 不会出现「看着炸得很大、其实打不到」的错位。
    /// </summary>
    public static class SplashExplosionVFX
    {
        /// <summary>当前还活着的特效数量，用来做并发上限。</summary>
        private static int s_Active;

        /// <summary>
        /// 在指定位置播一次爆炸特效。
        /// </summary>
        /// <param name="center">爆炸中心（世界坐标）</param>
        /// <param name="blastRadius">这次爆炸的实际影响半径（米，已经乘过吸收倍率）</param>
        /// <param name="tint">按「谁炸的」上色：Bomb = 红，Sticky = 青蓝，Bouncy = 黄</param>
        /// <param name="settings">预制体与缩放参数</param>
        /// <returns>实例化出来的根节点；没播（配置缺失 / 超过并发上限）时返回 null</returns>
        public static GameObject Spawn(Vector3 center, float blastRadius, BubbleType tint,
                                       ExplosionVfxSettings settings)
        {
            if (settings == null || settings.prefab == null)
            {
                return null;
            }

            int cap = Mathf.Max(1, settings.maxConcurrent);
            if (s_Active >= cap)
            {
                return null;
            }

            GameObject root = Object.Instantiate(settings.prefab, center, Quaternion.identity);
            root.name = "Splash Explosion VFX";

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);

            // 先全部停掉、清干净，等参数配好再统一播。
            // 否则 playOnAwake 可能已经吐出第一颗粒子，那颗还是原色/原尺寸。
            StopAll(systems);

            // ---- ① 让父级缩放真正生效 ----
            // Hierarchy = 用整条 transform 链的缩放；Local = 只认自己这一层。
            // 这个包的 7 个系统全是 Local，所以父节点怎么拉都不动，
            // 只能一颗一颗改子节点 —— 改成 Hierarchy 之后，只动根节点就够了。
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            // ---- ② 尺寸 ----
            // 系数 0.5：半径 0.3 → 0.15 倍，半径 0.6 → 0.3 倍；
            // 被吸收后半径 0.9~1.8，就自动变成 0.45~0.9 倍。
            float scale = Mathf.Clamp(blastRadius * settings.scalePerBlastRadius,
                                      settings.minScale, settings.maxScale);
            root.transform.localScale = Vector3.one * scale;

            // ---- ③ 上色（颜色全在粒子系统里，见 ExplosionPalette）----
            ExplosionPalette.Apply(systems, tint, settings.tintStrength, scale);

            // ---- ④ 开播 ----
            PlayAll(systems);

            // ---- ⑤ 自毁 + 并发计数 ----
            SplashExplosionLifetime life = root.AddComponent<SplashExplosionLifetime>();
            life.Begin(settings.lifeTime);

            s_Active++;
            return root;
        }

        /// <summary>特效销毁时把并发计数还回去（由 SplashExplosionLifetime 调用）。</summary>
        internal static void Release()
        {
            if (s_Active > 0)
            {
                s_Active--;
            }
        }

        private static void StopAll(ParticleSystem[] systems)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static void PlayAll(ParticleSystem[] systems)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Play();
            }
        }
    }
}
