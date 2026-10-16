// ---------------------------------------------------------------------------
// PAO / 泡泡潮 · 卡通泡泡（性能优化版）
//
// 这是 **BubbleBall.shader 的优化版**：三个 Pass 的结构、顺序、Cull 方向、
// 混合方式全部和原版一致，只把最贵的几项换成了等价但更省的做法。
// 详细说明见 BubbleTideToonOptCore.hlsl 的头部注释。
//
// 一句话总结优化了什么：
//   · 每像素 12 个 sin  → 0 个（扰动搬到顶点，用切向梯度在片元重建法线）
//   · 每像素 2 次 cubemap 采样 → 0 次（换成解析的天光渐变）
//   · 删掉 2 个没用上的量（_SpecPower / _TransmissionTint）
//
// ⚠ 三个 Pass 的**顺序和 Cull 方向不要改**：
//     1 Outline (Cull Front)  反向外壳描边，最先画
//     2 Back    (Cull Front)  背面垫底，给泡泡体积
//     3 Front   (Cull Back)   正面主着色
//   这个顺序是原版能正常显示的关键，动过就会出问题。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleTideToon"
{
    Properties
    {
        [Header(Base)]
        _BaseColor        ("底色", Color) = (0.75, 0.90, 1.0, 1.0)
        _Opacity          ("不透明度", Range(0, 1)) = 0.62
        _RimPower         ("边缘聚拢 Fresnel 指数", Range(0.5, 8)) = 2.2
        _RimIntensity     ("边缘强度", Range(0, 4)) = 1.1

        [Header(Toon Shading)]
        _ToonSteps        ("色阶数（少 = 更卡通）", Range(1, 8)) = 3
        _ToonSoftness     ("色阶边缘软度（0 = 硬边）", Range(0, 0.3)) = 0.03
        _RimThreshold     ("边缘光起点", Range(0, 1)) = 0.25
        _SpecThreshold    ("高光起点", Range(0, 1)) = 0.72
        _SpecColor        ("高光颜色", Color) = (1, 1, 1, 1)
        _IridColorA       ("彩虹色相 A", Color) = (0.45, 0.95, 0.85, 1)
        _IridColorB       ("彩虹色相 B", Color) = (1.0, 0.62, 0.92, 1)

        [Header(Iridescence)]
        _Iridescence      ("彩虹强度", Range(0, 4)) = 1.0
        _IridescenceScale ("彩虹密度", Range(0.1, 8)) = 2.2
        _ThicknessNoise   ("膜厚扰动强度", Range(0, 2)) = 0.5
        _FlowSpeed        ("膜的流动速度", Range(0, 3)) = 0.4

        [Header(Specular)]
        _SpecIntensity    ("高光强度", Range(0, 8)) = 1.4
        _Spec2Intensity   ("第二高光强度", Range(0, 4)) = 0.55

        [Header(Environment)]
        _EnvColor         ("环境色", Color) = (0.55, 0.72, 0.95, 1.0)
        _EnvIntensity     ("环境反射强度", Range(0, 2)) = 0.55
        [NoScaleOffset] _Cube ("环境 Cube（本版不再逐像素采样，仅保留属性）", Cube) = "" {}

        [Header(Transmission)]
        _Transmission     ("透射/背光强度", Range(0, 2)) = 0.6

        [Header(Wobble)]
        _WobbleAmplitude  ("外围扰动幅度（相对半径）", Range(0, 0.5)) = 0.06
        _WobbleFrequency  ("扰动频率", Range(0.1, 8)) = 1.6
        _WobbleSpeed      ("扰动速度", Range(0, 5)) = 1.2
        _NormalWobble     ("法线扰动（搅动高光/边缘）", Range(0, 1)) = 0.18

        [Header(Outline)]
        _OutlineColor     ("描边颜色（A = 0 则整条描边关闭）", Color) = (0.10, 0.12, 0.20, 1.0)
        _OutlineWidth     ("描边宽度（米）", Range(0, 0.2)) = 0.012
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        // ============ Pass 1：描边（反向外壳，最先画）============
        Pass
        {
            Name "BubbleOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #define PAO_OUTLINE 1
            #include "BubbleTideToonOptCore.hlsl"
            ENDHLSL
        }

        // ============ Pass 2：背面（垫底，给泡泡体积）============
        Pass
        {
            Name "BubbleBack"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #define PAO_BACKFACE 1
            #include "BubbleTideToonOptCore.hlsl"
            ENDHLSL
        }

        // ============ Pass 3：正面（主着色，最后画）============
        Pass
        {
            Name "BubbleFront"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "BubbleTideToonOptCore.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
