// ---------------------------------------------------------------------------
// PAO / Environment / Abyss —— 黑色深渊（漩涡洋流）
//
// 这一版的"深渊"不是一块黑地板，而是一个缓慢旋转的深渊漩涡：
//   · 中心是纯黑的黑洞，边缘一圈很弱的冷光
//   · 两套不同速度的螺旋洋流，近中心越卷越紧
//   · 洋流边缘有断续的冷色"泡沫"，不是连续的霓虹线
//   · 外圈收进纯黑，保证离漩涡远的地方仍然是"深渊"
//
// UV 中心 = 漩涡中心，所以整块深渊可以随便缩放/移动；
// 配合 PF_Abyss 里的两层体积雾和浮尘粒子，远看有深度，近看有细节。
// ---------------------------------------------------------------------------
Shader "PAO/Environment/Abyss"
{
    Properties
    {
        [Header(Color)]
        _VoidColor ("深渊底色", Color) = (0.0006, 0.0010, 0.0030, 1)
        _MistColorA ("内层洋流（青）", Color) = (0.020, 0.100, 0.120, 1)
        _MistColorB ("外层洋流（紫）", Color) = (0.060, 0.030, 0.150, 1)
        _FoamColor ("泡沫 / 高光", Color) = (0.250, 0.650, 0.800, 1)
        _RingColor ("中心冷光", Color) = (0.080, 0.400, 0.550, 1)
        _MoteColor ("浮尘微光", Color) = (0.400, 0.850, 1.000, 1)

        [Header(Shape)]
        _Scale ("细节尺度", Range(0.5, 6)) = 3.2
        _Arms ("螺旋臂数量", Range(1, 8)) = 3
        _Twist ("螺旋扭曲", Range(0, 10)) = 7
        _SpinSpeed ("旋转速度", Range(-2, 2)) = 0.35
        _VortexRadius ("漩涡半径（相对平面）", Range(0.1, 1.0)) = 0.35
        _MistStrength ("洋流强度", Range(0, 2)) = 0.55
        _FoamStrength ("泡沫强度", Range(0, 2)) = 0.32
        _RingStrength ("中心冷光", Range(0, 2)) = 0.40
        _MoteStrength ("浮尘强度", Range(0, 1)) = 0.40
        _OuterFade ("外缘收黑", Range(0.2, 1.5)) = 0.95
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex AbyssVertex
            #pragma fragment AbyssFragment
            #pragma multi_compile_fog

            #include "PAO_AbyssCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            Varyings AbyssVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.uv         = input.uv;
                output.fogFactor  = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 AbyssFragment(Varyings input) : SV_Target
            {
                // UV 中心 = 漩涡中心；_VortexRadius 控制漩涡占平面多大
                float2 p = (input.uv * 2.0 - 1.0) / max(0.05, _VortexRadius);
                float r = length(p);
                float a = atan2(p.y, p.x);
                float t = _Time.y * _SpinSpeed;

                // 主螺旋 + 副螺旋（不同速度，互相错开）
                float twistA = a + (1.0 - saturate(r)) * 3.1 + t * 0.35;
                float armsA = sin(twistA * _Arms - r * _Twist + t * 1.2);

                float twistB = a + (1.0 - saturate(r)) * 2.3 + t * 0.22;
                float armsB = sin(twistB * (_Arms + 1.0) - r * (_Twist * 0.65) + t * 0.8 + 1.7);

                float armA = smoothstep(0.05, 0.95, armsA * 0.5 + 0.5);
                float armB = smoothstep(0.20, 0.95, armsB * 0.5 + 0.5);

                float n1 = PAO_ENV_Fbm(p * _Scale + float2(t * 0.10, -t * 0.08));
                float n2 = PAO_ENV_Fbm(p * (_Scale * 1.9) + float2(-t * 0.05, t * 0.07) + 4.0);

                float bodyA = smoothstep(0.34, 0.68, n1) * armA;
                float bodyB = smoothstep(0.45, 0.80, n2) * armB;

                float3 color = _VoidColor.rgb;
                color += _MistColorA.rgb * (bodyA * _MistStrength);
                color += _MistColorB.rgb * (bodyB * _MistStrength * 0.55);

                // 断续的泡沫：只在螺旋边缘、并且被噪声打断
                float edge = smoothstep(0.86, 1.0, armsA * 0.5 + 0.5);
                float foam = edge * smoothstep(0.25, 0.60, n1) * smoothstep(0.20, 0.55, n2);
                foam *= smoothstep(0.10, 0.55, r);
                color += _FoamColor.rgb * (foam * _FoamStrength);

                // 中心黑洞
                float hole = smoothstep(0.10, 0.28, r);
                color *= hole;

                // 中心一圈很弱的冷光
                float ringD = (r - 0.22) / 0.045;
                float ring = exp(-ringD * ringD);
                color += _RingColor.rgb * (ring * _RingStrength);

                // 稀疏浮尘
                float2 q = p * 6.0 + float2(t * 0.20, -t * 0.15);
                float2 cell = floor(q);
                float2 local = frac(q);
                float rnd = PAO_ENV_Hash21(cell);
                float2 center = PAO_ENV_Hash22(cell + 5.0) * 0.6 + 0.2;
                float d = length(local - center);
                float mote = smoothstep(0.09, 0.0, d) * step(0.94, rnd);
                color += _MoteColor.rgb * (mote * _MoteStrength);

                // 外圈收黑
                float outer = 1.0 - smoothstep(_OuterFade - 0.25, _OuterFade, r);
                color = lerp(_VoidColor.rgb, color, outer);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================== 深度 ====================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #include "PAO_AbyssCore.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ==================== 深度 + 法线 ====================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "PAO_AbyssCore.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
