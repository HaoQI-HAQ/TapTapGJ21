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
        _HorizonColor     ("远处 / 掠射水色", Color) = (0.09, 0.29, 0.42, 1)
        _SurfaceColor     ("水面本体色",       Color) = (0.38, 0.78, 0.93, 1)
        _WindowColor      ("正上方透光色",     Color) = (0.80, 0.96, 1.00, 1)
        _RippleColor      ("波纹亮线色",       Color) = (1.00, 1.00, 1.00, 1)
        _SkirtBottomColor ("裙边底部色",       Color) = (0.02, 0.08, 0.14, 1)

        [Header(Window)]
        _WindowPower     ("天窗集中程度（越大越只在正上方）", Range(0.5, 8)) = 2.2
        _WindowStrength  ("天窗强度",                         Range(0, 1))   = 0.85

        [Header(Ripple)]
        _RippleScale     ("波纹密度（细胞 / 米）", Range(0.02, 2))  = 0.20
        _RippleSpeed     ("波纹速度",             Range(0, 3))     = 0.35
        _RippleStrength  ("波纹强度",             Range(0, 2))     = 0.45
        _RippleWidth     ("亮线宽度",             Range(0.01, 0.8)) = 0.30
        _RippleFadeStart ("波纹开始淡出的距离（米）", Float) = 40
        _RippleFadeEnd   ("波纹完全消失的距离（米）", Float) = 150

        [Header(Swell)]
        _SwellScale    ("大起伏空间频率（越小波长越长）", Range(0.001, 0.5)) = 0.055
        _SwellStrength ("大起伏压暗幅度",                Range(0, 1))        = 0.68

        [Header(Fade)]
        _FadeStart ("开始收敛到远处水色的距离（米）", Float) = 90
        _FadeEnd   ("完全收敛的距离（米）",           Float) = 240

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
            // ★ ZWrite Off + 材质渲染队列 2999（透明段最前）：
            //   水面在「不透明物体之后、所有透明光柱之前」画，且不写深度。
            //   这样：
            //     · 天空盒早就画完了，不会被它抢；
            //     · 丁达尔光柱（透明、加法）在它之后画，不会被这层水面挡掉；
            //     · 光柱本身仍然正常被角色/地形遮挡。
            ZWrite Off
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
