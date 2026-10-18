// ---------------------------------------------------------------------------
// PAO / Environment / AbyssFog —— 深渊体积雾（半透明层）
//
// 用法：铺在深渊底板上方的水平面，2～3 层不同高度/尺度/颜色。
// 相机移动时层与层之间会产生真实的视差，看起来像一团有厚度的雾，
// 而不是贴在底板上的一张噪声图。
//
// 注意：
//   · 这是 Transparent 材质，不写深度；不要给它加碰撞体。
//   · 平面边缘用 UV 做了淡出，所以边缘不会出现一条直线。
//   · 远处按距离再淡一次，避免雾铺到地平线变成一条硬带。
// ---------------------------------------------------------------------------
Shader "PAO/Environment/AbyssFog"
{
    Properties
    {
        [Header(Color)]
        _FogColor ("雾色", Color) = (0.010, 0.045, 0.065, 1)
        _Opacity ("不透明度", Range(0, 1)) = 0.22

        [Header(Shape)]
        _Scale ("雾团尺度（越小团越大）", Range(0.002, 0.2)) = 0.025
        _FlowSpeed ("流动速度", Range(0, 1)) = 0.06
        _Threshold ("雾气阈值", Range(0, 1)) = 0.45
        _Softness ("边缘软度", Range(0.01, 0.5)) = 0.25
        _EdgeFade ("平面边缘淡出", Range(0.005, 0.3)) = 0.08
        _Seed ("随机偏移", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FogVertex
            #pragma fragment FogFragment

            #include "PAO_AbyssFogCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
            };

            Varyings FogVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv         = input.uv;

                return output;
            }

            half4 FogFragment(Varyings input) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 p = input.positionWS.xz * _Scale + float2(t, t * 0.6) + _Seed;

                float n1 = PAO_ENV_Fbm(p);
                float n2 = PAO_ENV_Fbm(p * 2.1 + 13.7);
                float cloud = n1 * 0.65 + n2 * 0.35;

                float alpha = smoothstep(_Threshold, _Threshold + _Softness, cloud) * _Opacity;

                // 平面边缘淡出（Plane / Quad 的 UV 都是 0..1）
                float2 uv = input.uv;
                float2 edge = smoothstep(0.0, _EdgeFade, uv) * smoothstep(0.0, _EdgeFade, 1.0 - uv);
                alpha *= edge.x * edge.y;

                // 远处淡出，避免地平线出现一条雾带
                float dist = distance(_WorldSpaceCameraPos, input.positionWS);
                alpha *= 1.0 - smoothstep(80.0, 220.0, dist);

                return half4(_FogColor.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
