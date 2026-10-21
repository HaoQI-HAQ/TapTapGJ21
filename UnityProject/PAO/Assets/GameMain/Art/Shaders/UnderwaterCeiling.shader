// ---------------------------------------------------------------------------
// PAO / UnderwaterCeiling —— 水下抬头看到的那层水面
//
// 用法：这个 shader 不挂在平面上，挂在 PAO.Underwater.UnderwaterCeiling 组件
// 运行时生成的"水体壳"上（顶面 + 一圈往下垂的裙边）。网格原点就在水面上，
// 所以物体 Y 就是水位，shader 靠这个判断相机有没有出水。
//
// 【队列必须是 Transparent-1（= 2999），绝不能留在 Geometry】
//   水面永远在玩家头顶几十米、且玩家永远在水下，它其实就是这个世界的"天"。
//   ForwardLit 是 ZWrite Off（不写深度）：珊瑚靠 ZTest 就能挡住它，泡泡和光柱
//   也不会被它顶掉。但「不透明段 + 不写深度」是致命组合：
//     URP 的顺序是  不透明物体(250) → 天空盒(350) → 透明物体(450)，
//     天空盒用 ZTest LEqual 往「还停在最远值」的深度缓冲上画，
//     于是水面被天空盒整片刷掉 —— 只有 RenderSettings.skybox 为空的场景
//     才侥幸看得见（Map1 / Map3 就是这样幸存的，带天空盒的 5 张图全中招）。
//   放进 Transparent-1（= 2999）之后：
//     - 晚于天空盒 → 不会被刷掉；
//     - 早于其它透明物（光柱 / 泡泡 / 焦散，3000 起）→ 它们照旧叠在水面前面；
//     - ZTest LEqual 不变 → 珊瑚、地形照旧正常遮挡。
//   ★ 队列写在下面 SubShader 的 Tags 里，不写材质的 Custom Render Queue：
//     材质队列 -1（跟随 Shader）才正确，而 UnderwaterCeilingSetup 的
//     「重置水面材质为 shader 默认值」会用 new Material(shader) 重建材质，
//     任何写在材质上的队列都会被抹掉。
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
            "Queue"          = "Transparent-1"
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
            // ★ ZWrite Off + SubShader 队列 Transparent-1（= 2999，透明段最前）：
            //   水面在「天空盒之后、所有透明光柱之前」画，且不写深度。
            //   这样：
            //     · 天空盒早就画完了，不会被它抢（留在 Geometry 就会被抢）；
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
        // 工程里 SSAO 是开着的（URP-HighFidelity-Renderer 上的 SSAO feature）。
        // 队列改成 Transparent-1 之后，URP 的深度/法线预渲染只扫不透明段
        // （三个 Renderer 的 m_DepthPrimingMode 都是 0，本来也没开预渲染），
        // 水面不会再被扫到；这两个 Pass 留着无害，万一以后队列改回不透明段，
        // SSAO 也不会被水面带脏。
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
