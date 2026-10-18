// ---------------------------------------------------------------------------
// PAO / UnderwaterCeiling —— 水下抬头看到的那层水面
//
// 用法：这个 shader 不挂在平面上，挂在 PAO.Underwater.UnderwaterCeiling 组件
// 运行时生成的"水体壳"上（顶面 + 一圈往下垂的裙边）。网格原点就在水面上，
// 所以物体 Y 就是水位，shader 靠这个判断相机有没有出水。
//
// 为什么是 Opaque 队列而不是 Transparent：
//   水面永远在玩家头顶几十米、且玩家永远在水下，它其实就是这个世界的"天"。
//   走 Opaque + 写深度之后，
//     - 珊瑚天花板靠深度测试天然挡在水面前面，不需要任何排序；
//     - 满地图的泡泡粒子（透明队列）永远画在水面之后，不会随机穿插；
//     - 顺带少一次全屏级的透明混合。
//
// 三个 Pass：
//   ForwardLit / DepthOnly / DepthNormals
//   没有 ShadowCaster —— 水面不投影，少一个 pass 也省得给自己挖坑。
//   工程三个 URP Renderer 的 m_RenderingMode 都是 0（Forward），没有 GBuffer。
// ---------------------------------------------------------------------------
Shader "PAO/UnderwaterCeiling"
{
    Properties
    {
        [Header(Color)]
        _DeepColor     ("深水 / 远处色",                    Color) = (0.02, 0.12, 0.34, 1)
        _ShallowColor  ("品牌蓝（CartoonWater 的 Shallow）", Color) = (0.00, 0.393, 0.992, 1)
        _LightColor    ("天顶透光色",                        Color) = (0.72, 0.93, 1.00, 1)
        _RippleColor   ("波纹色（原版是白）",                Color) = (1.00, 1.00, 1.00, 1)
        _FoamColor     ("泡沫色（原版是白）",                Color) = (1.00, 1.00, 1.00, 1)

        [Header(Ripple)]
        _RippleTiling    ("波纹 UV 缩放（越小格子越大）", Range(0.001, 0.5)) = 0.08
        _RippleDensity   ("Voronoi 细胞密度",             Range(0.1, 40))    = 4
        _RippleSpeed     ("滚动速度",                     Range(0, 4))       = 1.5
        _RippleSlimness  ("锐利度（原版 5.1）",           Range(0.5, 14))    = 5.1
        _RippleAlpha     ("波纹强度（原版 1.26）",        Range(0, 3))       = 1.26
        _ShearStrength   ("径向扭曲强度",                 Range(0, 0.05))    = 0.003
        _RippleFadeStart ("波纹开始淡出的距离（米）", Float) = 60
        _RippleFadeEnd   ("波纹完全消失的距离（米）", Float) = 200

        [Header(Foam)]
        _FoamScale    ("泡沫密度（越小块越大）", Range(0.001, 2)) = 0.25
        _FoamSpeed    ("泡沫移动速度",           Float) = 0.6
        _FoamCutoff   ("泡沫阈值",               Range(0, 1)) = 0.62
        _FoamStrength ("泡沫强度",               Range(0, 1)) = 0.28

        [Header(Window)]
        _WindowPower ("天顶收敛快慢（越大越集中在正上方）", Range(0.5, 8)) = 2.6

        [Header(Fade)]
        _FadeStart ("开始收敛到深水色的距离（米）", Float) = 150
        _FadeEnd   ("完全收敛的距离（米）",         Float) = 320

        [Header(Misc)]
        _Dither ("色带打散强度", Range(0, 0.02)) = 0.004
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 100

        // ==================== 主 Pass ====================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Cull Off：双面。玩家虽然永远在水下，但编辑器自由视角、演出镜头
            // 都可能越过水面，单面渲染会在那一刻整片消失。
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   CeilingVertex
            #pragma fragment CeilingFragment
            #pragma multi_compile_fog

            #include "UnderwaterCeilingCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                // 由 UnderwaterCeiling 组件写进网格：x = 裙边归一化深度（0 = 水面）
                float2 shellParam : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  shellT     : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings CeilingVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                const VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.shellT     = input.shellParam.x;
                output.fogFactor  = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 CeilingFragment(Varyings input) : SV_Target
            {
                half3 color = PAO_ShadeUnderwaterCeiling(input.positionWS,
                                                         normalize(input.normalWS),
                                                         input.shellT);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================== 深度 ====================
        // 工程里 SSAO 是开着的（URP-HighFidelity-Renderer 上的 SSAO feature），
        // 少了这两个 Pass，水面会被当成"没有深度/法线"的对象，SSAO 会脏。
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   DepthVertex
            #pragma fragment DepthFragment

            #include "UnderwaterCeilingCore.hlsl"

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
            #pragma vertex   DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "UnderwaterCeilingCore.hlsl"

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

    // 兜底：万一 URP 没起来，至少不是粉红
    FallBack "Universal Render Pipeline/Unlit"
}
