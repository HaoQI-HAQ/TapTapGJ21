using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 爆炸特效的三套配色，以及「把第三方原色换成项目配色」的算法。
    ///
    /// 【这个特效的颜色到底存在哪 —— 先搞清楚再改】
    /// 贴图 Tex_fx_splash_set 的 R/G/B 分别是「主形状 / 溶解遮罩 / UV 扰动」，
    /// 是纯遮罩，不含颜色。它在 Shader Graph 里的取色是：
    ///
    ///     最终色 = Lerp( 顶点色 × 贴图R , UV1.rgb , 顶点色.a × UV1.a )
    ///
    /// 也就是两个颜色来源，都在粒子系统里：
    ///   · 基础色 = 粒子的顶点色（Start Color × Color over Lifetime）
    ///   · 高光色 = Custom Data 2 的颜色（被写进 UV1.rgb 读出来）
    ///
    /// 所以换色不需要动 Shader、不需要新建材质，改粒子系统的颜色就行，
    /// 而且改的是**运行时实例**，第三方原包一个字节都不会变。
    ///
    /// 【为什么不直接乘一个 Tint】
    /// 原色不是白的：基础色是「白 → 品红 → 暗紫」，高光是「金 → 橙」或「青绿」。
    /// 直接乘红色，金和青会被乘成近乎黑，整团糊掉。
    ///
    /// 这里改成「保留原来的明暗节奏，把色相换成调色板」：
    ///   白色核心 → 亮白（带一点点主色）
    ///   中间调   → 主色
    ///   暗部     → 暗主色
    /// 这样原本「核心过曝、边缘有色、尾巴发暗」的结构还在，
    /// 只是整套颜色换成了红 / 黄 / 青蓝。
    /// </summary>
    public static class ExplosionPalette
    {
        /// <summary>一套配色。数值可以超过 1 —— 这是 HDR，过曝的地方要靠它。</summary>
        public struct Colors
        {
            public Color baseDark;      // 基础色的暗部（尾巴）
            public Color baseMid;       // 基础色的主色
            public Color baseBright;    // 基础色的亮部（核心）
            public Color glowDeep;      // 高光的暗部
            public Color glowMain;      // 高光的主色
            public Color glowHot;       // 高光的最亮处
        }

        /// <summary>
        /// 按「谁炸的」取配色。
        ///
        /// 炸弹自己炸 = 红；被黏浮泡泡（蓝）吸收后炸 = 青蓝；被弹力泡泡（黄）吸收后炸 = 黄。
        /// 这样爆炸的颜色和宿主泡泡的颜色是同一套语言，
        /// 玩家一眼能看出「这一炸是从哪个泡泡里出来的」。
        /// </summary>
        public static Colors For(BubbleType type)
        {
            switch (type)
            {
                case BubbleType.Sticky:
                    return new Colors
                    {
                        baseDark   = new Color(0.02f, 0.12f, 0.22f),
                        baseMid    = new Color(0.22f, 0.80f, 1.00f),
                        baseBright = new Color(0.82f, 0.96f, 1.00f),
                        glowDeep   = new Color(0.05f, 0.32f, 0.62f),
                        glowMain   = new Color(0.40f, 0.85f, 1.20f),
                        glowHot    = new Color(1.15f, 1.40f, 1.55f),
                    };

                case BubbleType.Bouncy:
                    return new Colors
                    {
                        baseDark   = new Color(0.24f, 0.14f, 0.02f),
                        baseMid    = new Color(1.00f, 0.80f, 0.22f),
                        baseBright = new Color(1.00f, 0.97f, 0.82f),
                        glowDeep   = new Color(0.78f, 0.42f, 0.04f),
                        glowMain   = new Color(1.25f, 0.85f, 0.18f),
                        glowHot    = new Color(1.55f, 1.35f, 0.90f),
                    };

                case BubbleType.Bomb:
                default:
                    return new Colors
                    {
                        baseDark   = new Color(0.22f, 0.03f, 0.06f),
                        baseMid    = new Color(1.00f, 0.26f, 0.14f),
                        baseBright = new Color(1.00f, 0.88f, 0.74f),
                        glowDeep   = new Color(0.70f, 0.10f, 0.03f),
                        glowMain   = new Color(1.15f, 0.45f, 0.10f),
                        glowHot    = new Color(1.60f, 1.10f, 0.65f),
                    };
            }
        }

        /// <summary>
        /// 把一整套粒子系统换成指定配色。
        /// </summary>
        /// <param name="systems">要处理的粒子系统（通常是 prefab 实例里的全部子系统）</param>
        /// <param name="type">按谁炸的取色</param>
        /// <param name="strength">0 = 完全保留第三方原色，1 = 完全换成新配色</param>
        /// <param name="scale">整体缩放。只在「世界空间模拟」时需要拿来补速度</param>
        public static void Apply(ParticleSystem[] systems, BubbleType type, float strength, float scale)
        {
            if (systems == null || systems.Length == 0)
            {
                return;
            }

            Colors palette = For(type);

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = ps.main;

                // 【世界空间模拟的补丁】
                // 本地空间模拟时，transform 缩放会把速度、重力一起缩掉，什么都不用做。
                // 世界空间模拟时速度是绝对量，缩放只影响尺寸和出生位置，
                // 结果就是「粒子变小了但飞得还是那么远」—— 这里手动补一下。
                if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                {
                    main.startSpeed = ScaleCurve(main.startSpeed, scale);
                    main.gravityModifier = ScaleCurve(main.gravityModifier, scale);
                }

                if (strength <= 0.001f)
                {
                    continue;
                }

                // ① 起始色：只留明暗，去掉原来的品红/青绿倾向。
                //    它是**乘**在生命周期渐变上的，带着色相的话
                //    「红 × 品红」会叠成脏暗红。
                main.startColor = Neutralize(main.startColor, strength);

                // ② 生命周期渐变：明暗映射到调色板，时间轴和透明度原样保留。
                ParticleSystem.ColorOverLifetimeModule overLife = ps.colorOverLifetime;
                if (overLife.enabled)
                {
                    overLife.color = Remap(overLife.color, palette, false, strength);
                }

                // ③ Custom Data 2：这团特效的「发光 / 高光」全靠它。
                //    模式可能是 Vector（拿去做溶解扰动），那种就别乱改。
                ParticleSystem.CustomDataModule custom = ps.customData;
                if (custom.enabled &&
                    custom.GetMode(ParticleSystemCustomData.Custom2) == ParticleSystemCustomDataMode.Color)
                {
                    custom.SetColor(ParticleSystemCustomData.Custom2,
                        Remap(custom.GetColor(ParticleSystemCustomData.Custom2), palette, true, strength));
                }
            }
        }

        // ==================== 调色板取样 ====================

        /// <summary>基础色取样：0 = 最暗，0.5 = 主色，1 = 最亮。</summary>
        private static Color BaseRamp(Colors p, float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? Color.Lerp(p.baseDark, p.baseMid, t * 2f)
                : Color.Lerp(p.baseMid, p.baseBright, (t - 0.5f) * 2f);
        }

        /// <summary>高光取样：0 = 暗高光，0.55 = 主高光，1 = 过曝。</summary>
        private static Color GlowRamp(Colors p, float t)
        {
            const float kKnee = 0.55f;
            t = Mathf.Clamp01(t);
            return t < kKnee
                ? Color.Lerp(p.glowDeep, p.glowMain, t / kKnee)
                : Color.Lerp(p.glowMain, p.glowHot, (t - kKnee) / (1f - kKnee));
        }

        /// <summary>
        /// 把颜色换算成「有多亮」，用来决定它落在调色板的哪一段。
        /// 除 1.4 是为了兼容 HDR 颜色（原高光有 2.0 这种值），
        /// 再开 0.65 次方把暗部抬一点，免得中间调全糊成黑。
        /// </summary>
        private static float Tone(Color c)
        {
            float luma = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            return Mathf.Pow(Mathf.Clamp01(luma / 1.4f), 0.65f);
        }

        // ==================== 颜色改写 ====================

        private static Color Neutralize(Color c, float strength)
        {
            if (c.a <= 0.001f)
            {
                return c;
            }

            float luma = Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            Color gray = new Color(luma, luma, luma, c.a);
            return Color.Lerp(c, gray, strength);
        }

        private static Gradient Neutralize(Gradient src, float strength)
        {
            GradientColorKey[] colorKeys = src.colorKeys;
            for (int i = 0; i < colorKeys.Length; i++)
            {
                colorKeys[i].color = Neutralize(colorKeys[i].color, strength);
            }

            return RebuildGradient(src, colorKeys, src.alphaKeys);
        }

        private static ParticleSystem.MinMaxGradient Neutralize(ParticleSystem.MinMaxGradient src,
                                                               float strength)
        {
            switch (src.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Neutralize(src.color, strength));

                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Neutralize(src.colorMin, strength),
                                                             Neutralize(src.colorMax, strength));

                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Neutralize(src.gradient, strength));

                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Neutralize(src.gradientMin, strength),
                                                             Neutralize(src.gradientMax, strength));

                // RandomColor 走的是两个渐变随机取，但 mode 要保留成 RandomColor，
                // 不然会变成「每颗粒子随机选一个渐变」，观感会变。这里干脆不动它。
                default:
                    return src;
            }
        }

        private static ParticleSystem.MinMaxGradient Remap(ParticleSystem.MinMaxGradient src,
                                                          Colors palette, bool glow, float strength)
        {
            switch (src.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(
                        Remap(src.color, palette, glow, strength));

                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(
                        Remap(src.colorMin, palette, glow, strength),
                        Remap(src.colorMax, palette, glow, strength));

                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(
                        RemapGradient(src.gradient, palette, glow, strength));

                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        RemapGradient(src.gradientMin, palette, glow, strength),
                        RemapGradient(src.gradientMax, palette, glow, strength));

                default:
                    return src;
            }
        }

        private static Color Remap(Color c, Colors palette, bool glow, float strength)
        {
            if (c.a <= 0.001f)
            {
                // 全透明的关键帧保持原样：它不参与成像，
                // 但改了 RGB 会在渐变插值时把别的颜色带脏。
                return c;
            }

            Color target = glow ? GlowRamp(palette, Tone(c)) : BaseRamp(palette, Tone(c));
            target.a = c.a;
            return Color.Lerp(c, target, strength);
        }

        private static Gradient RemapGradient(Gradient src, Colors palette, bool glow, float strength)
        {
            GradientColorKey[] colorKeys = src.colorKeys;

            for (int i = 0; i < colorKeys.Length; i++)
            {
                Color c = colorKeys[i].color;
                if (c.a <= 0.001f)
                {
                    continue;
                }

                Color target = glow ? GlowRamp(palette, Tone(c)) : BaseRamp(palette, Tone(c));
                target.a = c.a;
                colorKeys[i].color = Color.Lerp(c, target, strength);
            }

            // 透明度、关键帧时间、混合模式都原样带过去 ——
            // 这个特效的「溶解 / 淡出」节奏就靠它们，动了整团就散。
            return RebuildGradient(src, colorKeys, src.alphaKeys);
        }

        private static Gradient RebuildGradient(Gradient src, GradientColorKey[] colorKeys,
                                                GradientAlphaKey[] alphaKeys)
        {
            Gradient g = new Gradient();
            g.mode = src.mode;
            g.SetKeys(colorKeys, alphaKeys);
            return g;
        }

        // ==================== 曲线缩放（只给世界空间模拟用） ====================

        private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(curve.constant * scale);

                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(curve.constantMin * scale,
                                                          curve.constantMax * scale);

                case ParticleSystemCurveMode.Curve:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier * scale, curve.curve);

                case ParticleSystemCurveMode.TwoCurves:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier * scale,
                                                          curve.curveMin, curve.curveMax);

                default:
                    return curve;
            }
        }
    }
}
