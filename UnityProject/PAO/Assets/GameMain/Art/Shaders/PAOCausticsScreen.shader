// ---------------------------------------------------------------------------
// PAO / CausticsScreen —— 水下场景的"焦散亮线"全屏叠加
//
// 为什么不用光照 cookie：
//   Thalassophobia 的焦散是"方向光 + cookie"。URP 的附加方向光 cookie 根本不渲染
//   （Unity 会直接报 "Additional Directional light ... cookie will not be visible"），
//   所以上一个版本里地面上一条焦散都没有。
//
// 这个 shader 的做法：
//   挂在相机前方的一块全屏 Quad 上（Transparent 队列、加法混合、ZTest Always），
//   采样 _CameraDepthTexture 反投影出每个像素的世界坐标，
//   再用世界 XZ 上的 Voronoi 细胞边界画"亮线"，乘上"水面以下的深度"和距离衰减。
//
//   因为用的是屏幕深度，所以地面 / 墙 / 道具 / 台阶全都会自动有焦散，
//   不需要一块块摆面片 —— 这就是"铺到全部场景"。
//
// 图案本体和 PAO/UnderwaterCeiling 是同一套 Voronoi（同样的数学），
// 所以抬头看到的水面亮线和脚下地面的焦散是一套语言，不会打架。
// ---------------------------------------------------------------------------
Shader "PAO/CausticsScreen"
{
    Properties
    {
        [Header(Color)]
        _CausticColor ("焦散颜色", Color) = (0.72, 0.93, 1.0, 1)

        [Header(Strength)]
        _Intensity   ("强度",                 Range(0, 4))   = 0.85
        _Scale       ("密度（细胞 / 米）",     Range(0.02, 2)) = 0.30
        _Speed       ("流动速度",             Range(0, 3))   = 0.45
        _LineWidth   ("亮线宽度",             Range(0.01, 0.6)) = 0.16
        _SwellScale  ("大起伏频率",           Range(0.001, 0.5)) = 0.045
        _SwellStrength ("大起伏强度",         Range(0, 1))   = 0.55

        [Header(Surface Mask)]
        _WaterY      ("水面高度 Y",           Float) = 76
        _DepthFade   ("水下多少米开始全亮",    Float) = 2.5

        [Header(Distance Fade)]
        _FadeStart   ("开始淡出距离（米）",    Float) = 70
        _FadeEnd     ("完全淡出距离（米）",    Float) = 190
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CausticsScreen"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One          // 加法：只加亮，不遮暗
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CausticColor;
                float _Intensity;
                float _Scale;
                float _Speed;
                float _LineWidth;
                float _SwellScale;
                float _SwellStrength;
                float _WaterY;
                float _DepthFade;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            // ---- 和 UnderwaterCeilingCore.hlsl 同一套 Voronoi / 图案 ----
            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float2 Voronoi(float2 p)
            {
                const float2 cell  = floor(p);
                const float2 local = p - cell;

                float f1 = 8.0;
                float f2 = 8.0;

                [unroll]
                for (int y = -1; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = -1; x <= 1; ++x)
                    {
                        const float2 offset  = float2((float)x, (float)y);
                        const float2 toPoint = offset + Hash22(cell + offset) - local;
                        const float  d       = dot(toPoint, toPoint);

                        if (d < f1) { f2 = f1; f1 = d; }
                        else if (d < f2) { f2 = d; }
                    }
                }

                return float2(sqrt(f1), sqrt(f2) - sqrt(f1));
            }

            float CausticPattern(float2 worldXZ, float time)
            {
                const float2 p1 = worldXZ * _Scale
                                + float2(time * 0.060, time * 0.035);
                const float2 p2 = worldXZ * (_Scale * 1.41)
                                + float2(time * -0.045, time * 0.055);

                const float width1 = max(_LineWidth, 1e-3);
                const float width2 = max(_LineWidth * 1.35, 1e-3);

                const float line1 = 1.0 - smoothstep(0.0, width1, Voronoi(p1).y);
                const float line2 = 1.0 - smoothstep(0.0, width2, Voronoi(p2).y);

                // 两层取 max：地面要的是"亮网"，而不是水面那种两层相加的柔光
                float pattern = max(line1 * 0.90, line2 * 0.55);

                // 上百米波长的大起伏：一块亮一块暗，免得像壁纸
                const float2 swellUV = worldXZ * max(_SwellScale, 1e-4)
                                     + float2(time * 0.012, time * -0.009);
                const float swell = 0.5 + 0.5 * sin(swellUV.x) * sin(swellUV.y);
                pattern *= lerp(1.0 - saturate(_SwellStrength), 1.0, swell);

                return saturate(pattern);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos  = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.screenPos.xy / max(input.screenPos.w, 1e-5);

                // 天空/没有几何的像素没有焦散
                float rawDepth = SampleSceneDepth(uv);
                float linear01 = Linear01Depth(rawDepth, _ZBufferParams);
                if (linear01 > 0.9995) return half4(0, 0, 0, 1);

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                // 只给"水面以下"的表面加焦散；越深越亮（浅处被水面 eaten）
                float depthBelow = _WaterY - positionWS.y;
                float depthMask  = saturate(depthBelow / max(_DepthFade, 0.01));
                if (depthMask <= 0.0) return half4(0, 0, 0, 1);

                // 距离淡出：远处图案会退化成高频闪烁
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                float distFade = 1.0 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1e-3));

                float pattern = CausticPattern(positionWS.xz, _Time.y * _Speed);

                half3 color = _CausticColor.rgb
                            * (pattern * _Intensity * depthMask * distFade);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
