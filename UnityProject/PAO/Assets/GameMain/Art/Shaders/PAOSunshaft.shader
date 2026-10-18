// ---------------------------------------------------------------------------
// PAO / Sunshafts —— 水下"丁达尔光"光柱（Thalassophobia 太阳光柱的替身）
//
// 为什么不用原来的 URP ParticlesUnlit：
//   1. 那套光柱（Sunshafts / Sunshafts (Area) 预制体）是**没有贴图**的拉伸公告板，
//      全靠软粒子（Soft Particles）+ 相机淡出做边缘，深度缓冲一挡就没。
//   2. 现在头顶水面是一整块**不透明**的水面顶盖（PAO/UnderwaterCeiling），
//      它把射到水面上方的光柱全挡住了 —— 所以看起来"丁达尔光没有了"。
//
// 这个 shader 的做法：
//   - ZTest Always：光柱永远画在画面最上层，风格化处理（和之前观感一致，
//     水面顶盖 / 任何几何都挡不住光柱）；
//   - 沿公告板长边（UV.y）做软边渐变，不需要贴图就是两头虚的光柱；
//   - 近/远距离淡出，避免镜头怼到光柱上或远处糊成一片；
//   - 加法混合，只加亮不遮暗；
//   - 不做软粒子（Soft Particles）：那套靠场景深度做淡出，水面顶盖一挡
//     整根光柱就没了 —— 这正是之前"丁达尔光没有了"的原因。
// ---------------------------------------------------------------------------
Shader "PAO/Sunshafts"
{
    Properties
    {
        _MainTex ("贴图（可空，空=纯白）", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1, 1, 1, 0.65)
        _Intensity ("强度", Range(0, 6)) = 1.6
        _SoftEdge ("两端的软边比例", Range(0, 0.49)) = 0.42
        _NearFadeStart ("离相机多近开始淡出", Float) = 1.5
        _NearFadeEnd ("离相机多远完全显现", Float) = 6
        _FarFadeStart ("多远开始淡出", Float) = 220
        _FarFadeEnd ("多远完全消失", Float) = 360
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One     // 加法混合（带 alpha 权重）
            ZWrite Off
            ZTest Always           // 光柱风格化：永远画在最上层，不被水面顶盖挡住
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _BaseColor;
                float  _Intensity;
                float  _SoftEdge;
                float  _NearFadeStart;
                float  _NearFadeEnd;
                float  _FarFadeStart;
                float  _FarFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                float  dist       : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.uv         = TRANSFORM_TEX(input.uv, _MainTex);
                output.color      = input.color;
                output.dist       = length(posInputs.positionWS - _WorldSpaceCameraPos);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // 贴图为空时默认白色，这里两种都成立
                half3 rgb = tex.rgb * input.color.rgb * _BaseColor.rgb;
                half  a   = tex.a * input.color.a * _BaseColor.a;

                // 沿长边两头做软边（没有贴图的拉伸公告板全靠这个才不像一块板）
                half edge = smoothstep(0.0, max(_SoftEdge, 1e-3), input.uv.y)
                          * smoothstep(1.0, 1.0 - max(_SoftEdge, 1e-3), input.uv.y);
                a *= edge;

                // 近 / 远距离淡出
                a *= saturate((input.dist - _NearFadeStart) / max(_NearFadeEnd - _NearFadeStart, 1e-3));
                a *= 1.0 - saturate((input.dist - _FarFadeStart) / max(_FarFadeEnd - _FarFadeStart, 1e-3));

                return half4(rgb * _Intensity, saturate(a));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
