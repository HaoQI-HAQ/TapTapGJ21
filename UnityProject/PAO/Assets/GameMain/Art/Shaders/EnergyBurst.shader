// ---------------------------------------------------------------------------
// PAO / 能量爆发（Energy Burst）
//
// 【为什么不用粒子，改用这个】
// "塞尔达能量爆炸"那种观感的本质不是"很多小粒子"，而是**一大团发光的能量**：
// 过曝的白热核心、从中心射出的粗光束、向外推的亮环、整体泛光。
//
// 用粒子做不到这几点：
//   · 粒子是很多小图元，凑不出一整片连续的发光体
//   · 粒子没有"从中心射出、越远越淡"的连续光束形状
//   · 没有后处理 Bloom 时，粒子再怎么亮也不会"溢出来"
//
// 所以改成：**一张正对相机的程序化贴图**，在片元里直接画出能量结构。
// 这样形状是连续的、光束是渐变的、核心可以远超 1.0 让它过曝成白。
//
// 【"假泛光"是怎么做的】
// 工程里没有任何 Volume / Bloom 后处理（10 个场景 0 处引用），
// 所以光晕必须自己画：把高亮区域用**逐次递减的宽高斯**叠出来，
// 再靠加色混合（Blend One One）把亮度堆到过曝。
// 视觉上就和 Bloom 很接近，而且不依赖任何后处理设施。
//
// 【结构（从内到外）】
//   1. 核心：白热，pow(saturate(1 - r/_CoreRadius)) —— 远超 1.0 让它白掉
//   2. 光束：极坐标下的尖峰周期函数 + 噪声扰动，越远越淡
//   3. 环  ：以 _Progress 为半径的高斯环，向外扩张
//   4. 泛光：三层不同宽度的高斯，叠出柔和外晕
//
// 【参数全由 MaterialPropertyBlock 驱动】
// 同一个共享材质 + 每实例 PropertyBlock，不需要为每次爆炸建材质。
// ---------------------------------------------------------------------------
Shader "PAO/EnergyBurst"
{
    Properties
    {
        _Tint        ("能量颜色", Color) = (0.35, 0.78, 1.0, 1.0)
        _HotTint     ("过曝核心色（近白）", Color) = (1.0, 0.98, 0.94, 1.0)
        _Intensity   ("整体强度", Range(0, 8)) = 2.2

        _Progress    ("爆发进度 0=起始 1=结束", Range(0, 1)) = 0
        _Seed        ("随机种子（每次爆炸错开形状）", Range(0, 100)) = 0

        _CoreRadius  ("核心半径（0~1）", Range(0.02, 0.6)) = 0.16
        _CorePower   ("核心硬度（越大越锐）", Range(1, 8)) = 2.6
        _CoreBoost   ("核心亮度倍数", Range(0, 8)) = 3.0

        _RayCount    ("光束数量", Range(2, 48)) = 11
        _RaySharp    ("光束锐度（越大越细）", Range(0.5, 16)) = 3.2
        _RayBoost    ("光束亮度", Range(0, 6)) = 1.5

        _RingWidth   ("扩张环宽度", Range(0.01, 0.4)) = 0.075
        _RingBoost   ("扩张环亮度", Range(0, 6)) = 1.4

        _GlowWidth   ("外泛光宽度", Range(0.1, 1.0)) = 0.85
        _GlowBoost   ("外泛光亮度", Range(0, 4)) = 0.55

        _NoiseAmount ("能量扰动强度", Range(0, 2)) = 0.55
        _NoiseScale  ("能量扰动密度", Range(0.5, 12)) = 3.0
        _AnimSpeed   ("流动速度", Range(0, 6)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "EnergyBurst"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual

            // 纯加色：把每个像素的能量直接加到画面上。
            // 亮度会累加到超过 1.0，被显示器裁成白 —— 这就是"过曝"。
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _HotTint;
                float  _Intensity;
                float  _Progress;
                float  _Seed;
                float  _CoreRadius;
                float  _CorePower;
                float  _CoreBoost;
                float  _RayCount;
                float  _RaySharp;
                float  _RayBoost;
                float  _RingWidth;
                float  _RingBoost;
                float  _GlowWidth;
                float  _GlowBoost;
                float  _NoiseAmount;
                float  _NoiseScale;
                float  _AnimSpeed;
            CBUFFER_END

            // ---------------- 噪声 ----------------

            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 两层叠加够用了 —— 再多也不明显，只是多花指令
            float Fbm(float2 p)
            {
                float n = ValueNoise(p) * 0.65;
                n += ValueNoise(p * 2.07 + 11.3) * 0.35;
                return n;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 中心为原点的极坐标。r 在中心是 0，方的四角约 1.41
                float2 p = i.uv * 2.0 - 1.0;
                float  r = length(p);
                float  ang = atan2(p.y, p.x);

                float t = _Time.y * _AnimSpeed;

                // ---- 能量扰动：让形状有机，不是完美的几何图形 ----
                float2 np = p * _NoiseScale + _Seed + t * 0.35;
                float  nz = Fbm(np) - 0.5;                 // -0.5 ~ 0.5
                float  wobble = nz * _NoiseAmount;

                // ---------------- 1. 白热核心 ----------------
                // 核心被噪声轻微扭曲边界，看起来像"能量在翻涌"
                float coreR = max(_CoreRadius + wobble * 0.06, 0.01);
                float core = pow(saturate(1.0 - r / coreR), _CorePower);

                // ---------------- 2. 放射光束 ----------------
                // 极坐标下的"尖峰周期函数"：
                //   cos(ang * N) 在角度上是平滑的周期波，
                //   用 pow(., k) 把波峰拉尖 → 就变成一束一束的光。
                // 角度上再加噪声扰动，光束就不会等距排布（等距会像齿轮）。
                float angN = ang + wobble * 1.6;
                float rayA = pow(saturate(0.5 + 0.5 * cos(angN * _RayCount + _Seed)), _RaySharp);
                float rayB = pow(saturate(0.5 + 0.5 * cos(angN * (_RayCount * 1.73) - _Seed * 2.3)), _RaySharp * 1.6);

                float rays = rayA + rayB * 0.55;

                // 光束长度随进度扩张：起始短、展开后拉长
                float rayLen = lerp(0.42, 1.0, _Progress);
                float rayMask = saturate(1.0 - r / rayLen);
                rayMask = pow(rayMask, 1.5);

                // 光束也要被噪声断续，否则像塑料
                rayMask *= saturate(0.55 + nz * 1.4);

                rays *= rayMask;

                // ---------------- 3. 向外推的亮环 ----------------
                float ringR = _Progress * 1.02;
                float ringD = (r - ringR) / max(_RingWidth, 0.001);
                float ring  = exp(-ringD * ringD);          // 高斯环

                // ---------------- 4. 假泛光（外晕）----------------
                // 三层不同宽度的衰减叠起来，近似 Bloom 的柔光
                float glow = 0.0;
                glow += exp(-r / _GlowWidth)              * 0.55;
                glow += exp(-r / (_GlowWidth * 0.45))     * 0.30;
                glow += exp(-r / (_GlowWidth * 0.18))     * 0.15;

                // ---------------- 合成 ----------------
                // 核心用"近白热色"并且强度远超 1 —— 这样加色后会被裁成白色，
                // 看起来就是过曝；边缘是能量色，形成彩色光边。
                float3 col = float3(0, 0, 0);

                col += _HotTint.rgb  * (core * _CoreBoost);
                col += _Tint.rgb     * (rays * _RayBoost);
                col += _Tint.rgb     * (ring * _RingBoost);
                col += _Tint.rgb     * (glow * _GlowBoost);

                col *= _Intensity;

                // 圆形遮罩：把方块的四角切掉，避免看到方形边界
                float circle = saturate(1.0 - smoothstep(0.86, 1.0, r));
                col *= circle;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
