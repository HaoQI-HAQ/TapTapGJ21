// ---------------------------------------------------------------------------
// PAO / 水彩泡泡爆炸彩块
//
// 给爆炸粒子用：一颗粒子 = 一块圆形水彩，边缘用程序噪声咬出不规则的边，
// 中间实、边缘带一圈浅色"水痕"，模仿颜料在纸上洇开的样子。
//
// 与 PAO/BubbleBall 的一致性：
//   · 同一个色系（冰蓝底 + 青/粉彩虹）
//   · 同样的"硬边 + 一圈亮边"卡通处理（XStep 色阶 + 菲涅尔亮边）
//   · 顶点扰动用的是"低频正弦叠加"而不是高频噪声 —— 保持液态流动感
//
// 和 BubbleBall 的区别：这个是**粒子**用的，走 Particle System 的
// 顶点流（TEXCOORD0.xy = 粒子UV，NORMAL.xy + TANGENT.x = 自定义数据），
// 不参与光照，纯自发光叠加，保证爆炸在暗处也看得清。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleBurstSplash"
{
    Properties
    {
        [Header(Base)]
        _MainTex        ("贴图（留空 = 纯程序圆）", 2D) = "white" {}
        _Tint           ("底色", Color) = (0.72, 0.90, 1.0, 1.0)
        _Opacity        ("整体不透明度", Range(0, 1)) = 1.0

        [Header(Soft Blob)]
        _Softness       ("边缘柔和度（噪声扰动幅度）", Range(0, 0.8)) = 0.20
        _CorePower      ("核心实心范围", Range(0.05, 1.0)) = 0.55

        [Header(Wet Edge)]
        _RimColor       ("水痕颜色", Color) = (0.85, 0.98, 1.0, 1.0)
        _RimWidth       ("水痕宽度", Range(0.01, 0.5)) = 0.16
        _RimStrength    ("水痕强度", Range(0, 3)) = 1.15

        [Header(Motion)]
        _WobbleAmp      ("边缘流动幅度", Range(0, 12)) = 4.2
        _WobbleSpeed    ("边缘流动速度", Range(0, 8)) = 2.4
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
            Name "BubbleBurstSplash"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            // 粒子顶点流：INSTANCED 让一个 DrawCall 画多颗粒子
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;

                float  _Opacity;
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
                float3 normalOS   : NORMAL;      // 粒子：xyz 直接就是法线
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;   // 粒子：xy 就是粒子UV
                float4 custom     : TEXCOORD1;   // 粒子自定义数据 xyzw
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

            // 低频正弦叠加：和 BubbleBallCore 的 WobbleAmount 同一套思路，
            // 要的是"液态有机"的边，不是颗粒噪声
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

                // 粒子UV 以 0.5 为中心，半径归一化到 0~1（1 = 贴边）
                float2 centered = i.uv - 0.5;
                float  radius   = length(centered);

                // ---- 边缘流动：让这块水彩的边一直在轻微蠕动 ----
                float t = _Time.y * _WobbleSpeed;
                float blob = BlobNoise(centered * 2.0 + i.custom.x, t);

                // 用噪声把半径推来推去，得到不规则边
                float softAmt = _Softness * (0.75 + 0.25 * blob);
                float r = radius * (1.0 + blob * softAmt * 0.5);

                // ---- 主体：中间实、边缘化开 ----
                // _CorePower 越小核心越大
                float core = 1.0 - smoothstep(_CorePower, 0.5, r);

                // ---- 水痕：靠近边缘的一圈亮边（颜料堆积）----
                float rimDist = abs(r - (1.0 - _RimWidth));
                float rim = 1.0 - smoothstep(0.0, _RimWidth, rimDist);

                // ---- 贴图（有就用，没有就是纯程序圆）----
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                half3 baseCol = _Tint.rgb * tex.rgb;

                // 水痕是加亮，不是替换 —— 这样在深色背景上也能看见边
                half3 col = baseCol * core + _RimColor.rgb * (rim * _RimStrength * core);

                // 卡通硬边：把核心压成两级，避免糊成一团
                float alpha = XStep(core, 4.0, 0.12);

                alpha *= tex.a * _Opacity * i.color.a;

                // 粒子系统的 Start Color 参与调色（爆炸闪白就是用这个做的）
                col *= i.color.rgb;

                // 完全透明的直接丢弃，省 overdraw
                clip(alpha - 0.004);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
