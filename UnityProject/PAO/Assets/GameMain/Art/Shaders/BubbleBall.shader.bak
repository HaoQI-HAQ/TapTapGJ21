// ---------------------------------------------------------------------------
// PAO / 泡泡球材质（挂在 Mesh 球上）
//
// 和 PAO/BubbleSDF 的区别：
//   BubbleSDF   是全屏光线步进，开销跟屏幕像素数走，靠距离场做自动融合
//   本 shader   是普通网格材质，开销跟泡泡个数走，天生就是个"球"
//
// 用在会飞、会发射、数量多的泡泡上，用这个。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleBall"
{
    Properties
    {
        [Header(Base)]
        _BaseColor        ("底色", Color) = (0.75, 0.90, 1.0, 1.0)
        _Opacity          ("不透明度", Range(0, 1)) = 0.55
        _RimPower         ("边缘聚拢 Fresnel 指数", Range(0.5, 8)) = 2.5
        _RimIntensity     ("边缘强度", Range(0, 4)) = 1.4

        [Header(Iridescence)]
        _Iridescence      ("彩虹强度", Range(0, 4)) = 1.3
        _IridescenceScale ("彩虹密度", Range(0.1, 8)) = 2.2
        _ThicknessNoise   ("膜厚噪声强度", Range(0, 1)) = 0.35
        _FlowSpeed        ("膜的流动速度", Range(0, 3)) = 0.35

        [Header(Specular)]
        _SpecIntensity    ("高光强度", Range(0, 8)) = 1.6
        _SpecPower        ("高光锐度", Range(1, 512)) = 160
        _Spec2Intensity   ("第二高光强度", Range(0, 4)) = 0.5

        [Header(Environment)]
        _EnvColor         ("环境色", Color) = (0.55, 0.70, 0.90, 1.0)
        _EnvIntensity     ("环境反射强度", Range(0, 2)) = 0.6
        [NoScaleOffset] _Cube ("环境 Cube（留空则用环境色）", Cube) = "" {}

        [Header(Transmission)]
        _Transmission     ("透射/背光强度", Range(0, 2)) = 0.5
        _TransmissionTint ("透射色调", Color) = (0.85, 0.95, 1.0, 1.0)
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

        // ================= Pass 1：背面（先画，给正面垫底）=================
        Pass
        {
            Name "BubbleBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #define PAO_BACKFACE 1
            #include "BubbleBallCore.hlsl"
            ENDHLSL
        }

        // ================= Pass 2：正面（后画，覆盖上去）=================
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

            #include "BubbleBallCore.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
