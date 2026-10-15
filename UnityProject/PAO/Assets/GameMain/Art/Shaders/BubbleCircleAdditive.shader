// ---------------------------------------------------------------------------
// PAO / 圆形粒子 · 加色版（发光火星）
//
// 和 PAO/BubbleCircle 完全同一套圆形逻辑（径向衰减 + 圆外 clip，
// 所以一定是圆、不可能出三角形），唯一的区别是**混合模式**：
//
//   普通版：Blend SrcAlpha OneMinusSrcAlpha  —— 像一块有颜色的胶
//   本  版：Blend SrcAlpha One               —— 纯加色，叠加会越来越亮
//
// 火烧边缘飞溅出来的火星必须用加色，才会"像在发光"；
// 用普通混合的话在深色背景上看起来就是一些橘色贴纸。
//
// 另外本版给高光分量乘了一个倍数，让火星中心过曝成白色 —— 这是火星的关键观感。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleCircleAdditive"
{
    Properties
    {
        // 只保留 Color / Range 声明，不写 [Header(...)]（见 BubbleDissolveBurn 的说明）
        _Tint          ("底色", Color) = (1.0, 0.65, 0.25, 1.0)
        _Opacity       ("整体不透明度", Range(0, 4)) = 1.6

        _Radius        ("圆的范围（1 = 贴满粒子方块）", Range(0.5, 1.5)) = 1.0
        _EdgeSoftness  ("边缘柔和度（0=硬边）", Range(0.01, 1.0)) = 0.55
        _CoreSolid     ("核心实心范围", Range(0, 1)) = 0.0

        _GlowColor     ("亮光颜色", Color) = (1.0, 0.95, 0.80, 1.0)
        _GlowStrength  ("亮光强度", Range(0, 6)) = 2.2
        _GlowWidth     ("亮光宽度", Range(0.01, 1.0)) = 0.65

        _CoreBoost     ("核心过曝倍数（火星中心发白）", Range(1, 8)) = 2.6

        _WobbleAmp     ("边缘流动幅度（很小，只做轻微扰动）", Range(0, 0.25)) = 0.05
        _WobbleSpeed   ("边缘流动速度", Range(0, 8)) = 3.5
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
            Name "BubbleCircleAdditive"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One        // ← 唯一和 BubbleCircle 不同的地方

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float  _Opacity;

                float  _Radius;
                float  _EdgeSoftness;
                float  _CoreSolid;

                float4 _GlowColor;
                float  _GlowStrength;
                float  _GlowWidth;

                float  _CoreBoost;

                float  _WobbleAmp;
                float  _WobbleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 custom     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float4 custom     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = input.uv;
                o.color      = input.color;
                o.custom     = input.custom;

                return o;
            }

            float BlobNoise(float2 uv, float t)
            {
                float n = sin(uv.x * 3.1 + t * 1.00)
                        + sin(uv.y * 3.7 - t * 1.23)
                        + sin((uv.x + uv.y) * 2.3 + t * 0.71);
                return n * (1.0 / 3.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // ---- 径向距离：保证一定是圆 ----
                float2 centered = (i.uv - 0.5) * 2.0;
                float  dist = length(centered);

                float t = _Time.y * _WobbleSpeed;
                float blob = BlobNoise(centered + i.custom.x, t);
                float wobbled = dist * (1.0 - blob * _WobbleAmp);

                float soft = max(_EdgeSoftness, 0.01);
                float circle = 1.0 - smoothstep(_Radius - soft, _Radius, wobbled);

                // 圆外直接丢弃
                clip(circle - 0.002);

                float core = 1.0 - smoothstep(_CoreSolid, _Radius, wobbled);

                // 靠近边缘的亮环
                float rimDist = abs(wobbled - _Radius * (1.0 - _GlowWidth));
                float rim = 1.0 - smoothstep(0.0, max(_GlowWidth, 0.01), rimDist);

                // ---- 火星的颜色构成 ----
                // 中心过曝发白、外围橘色，这样才像"烧红的颗粒"
                half3 col = _Tint.rgb * core;
                col += _GlowColor.rgb * (rim * _GlowStrength);
                col += _GlowColor.rgb * (core * core * _CoreBoost);

                float alpha = circle * _Opacity * i.color.a;

                col *= i.color.rgb;

                // 加色混合下，亮的地方不需要高 alpha —— 但中心还是给足
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
