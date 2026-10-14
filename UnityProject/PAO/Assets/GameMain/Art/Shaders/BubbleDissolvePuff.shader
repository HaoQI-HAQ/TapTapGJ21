// ---------------------------------------------------------------------------
// PAO / 泡泡溶解 / 消散
//
// 泡泡存活时间到了、化掉时用。三个阶段的观感：
//   0.0~0.5  泡泡体积向内收缩，边缘开始起毛
//   0.4~1.0  一圈细小水珠从壳上渗出来，向上飘散（水里是上浮）
//   0.6~1.0  整体淡出，彩色水痕最后消失
//
// 【收缩怎么做】
// 泡泡那层"往内收"由粒子系统的 Size over Lifetime 负责 —— 溶解彩块是
// 正对镜头的 2D billboard，缩放它在画面上就等价于泡泡收缩，
// 不需要再用自定义顶点流把内缩量传进来（那样要多配一处活动顶点流，收益为零）。
//
// 风格沿用 PAO/BubbleBurstSplash：程序圆 + 低频正弦咬边 + 水痕亮边。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleDissolvePuff"
{
    Properties
    {
        [Header(Base)]
        _Tint          ("底色", Color) = (0.72, 0.90, 1.0, 1.0)
        _Opacity       ("整体不透明度", Range(0, 1)) = 1.0

        [Header(Shrink)]
        _Erode         ("边缘侵蚀（把圆周咬掉）", Range(0, 1)) = 0.0

        [Header(Blob)]
        _Softness      ("边缘柔和度", Range(0, 0.8)) = 0.24
        _CorePower     ("核心实心范围", Range(0.05, 1.0)) = 0.50

        [Header(Wet Edge)]
        _RimColor      ("水痕颜色", Color) = (0.88, 0.98, 1.0, 1.0)
        _RimWidth      ("水痕宽度", Range(0.01, 0.5)) = 0.18
        _RimStrength   ("水痕强度", Range(0, 3)) = 1.2

        [Header(Motion)]
        _WobbleAmp     ("边缘流动幅度", Range(0, 12)) = 3.4
        _WobbleSpeed   ("边缘流动速度", Range(0, 8)) = 1.8
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
            Name "BubbleDissolvePuff"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;

                float  _Opacity;
                float  _Erode;

                float  _Softness;
                float  _CorePower;

                float4 _RimColor;
                float  _RimWidth;
                float  _RimStrength;

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

            // 低频正弦叠加：和 BubbleBallCore 的 WobbleAmount 同一套思路
            float BlobNoise(float2 uv, float t)
            {
                float n = sin(uv.x * 3.1 + t * 1.00)
                        + sin(uv.y * 3.7 - t * 1.23)
                        + sin((uv.x + uv.y) * 2.3 + t * 0.71)
                        + sin((uv.x - uv.y) * 4.1 - t * 0.89);
                return n * 0.25;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                // 粒子的几何缩放（Size over Lifetime）已经负责"泡泡收缩"，
                // 这里不再做顶点内缩 —— 溶解彩块是 2D billboard，
                // 缩放粒子和压缩顶点在画面上等价，少一层可出错的地方。
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = input.uv;
                o.color      = input.color;
                o.custom     = input.custom;

                return o;
            }

            // 把 t 量化成 Steps 级，做出卡通硬边
            float XStep(float t, float steps, float softness)
            {
                steps = max(steps, 1.0);
                float scaled = saturate(t) * steps;
                float idx    = floor(scaled);
                float f      = scaled - idx;
                float soft   = max(softness, 1e-4);
                return (idx + smoothstep(0.5 - soft, 0.5 + soft, f)) / steps;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float2 centered = i.uv - 0.5;
                float  radius   = length(centered);

                // ---- 边缘流动 ----
                float t    = _Time.y * _WobbleSpeed;
                float blob = BlobNoise(centered * 2.0 + i.custom.x, t);

                float softAmt = _Softness * (0.75 + 0.25 * blob);
                float r = radius * (1.0 + blob * softAmt * 0.5);

                // ---- 主体 ----
                float core = 1.0 - smoothstep(_CorePower, 0.5, r);

                // ---- 边缘侵蚀：溶解时把圆周一点点咬掉 ----
                if (_Erode > 0.0001)
                {
                    // 用噪声决定"从哪个方向开始烂"，而不是整圈一起变淡
                    float bite = smoothstep(_Erode - 0.25, _Erode + 0.25, r + blob * 0.25);
                    core *= 1.0 - bite;
                }

                // ---- 水痕：溶解时反而更明显（水在往外渗）----
                float rimDist = abs(r - (1.0 - _RimWidth));
                float rim = 1.0 - smoothstep(0.0, _RimWidth, rimDist);

                half3 col = _Tint.rgb * core + _RimColor.rgb * (rim * _RimStrength * core);

                float alpha = XStep(core, 4.0, 0.14);
                alpha *= _Opacity * i.color.a;
                col   *= i.color.rgb;

                clip(alpha - 0.004);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
