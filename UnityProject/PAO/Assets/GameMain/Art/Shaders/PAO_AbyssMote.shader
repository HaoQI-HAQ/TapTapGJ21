// ---------------------------------------------------------------------------
// PAO / Environment / AbyssMote —— 深渊浮尘粒子
//
// 不用贴图：片元里画一个软圆，配合 ParticleSystemRenderer 传进来的顶点色。
// 加法混合，所以叠在一起会自然变亮，适合做深渊里漂浮的冷色微粒。
// ---------------------------------------------------------------------------
Shader "PAO/Environment/AbyssMote"
{
    Properties
    {
        _Color ("颜色", Color) = (0.45, 0.85, 1.0, 1)
        _Intensity ("强度", Range(0, 4)) = 1.4
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

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MoteVertex
            #pragma fragment MoteFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings MoteVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 MoteFragment(Varyings input) : SV_Target
            {
                float2 delta = input.uv - 0.5;
                float mask = saturate(1.0 - length(delta) * 2.0);
                mask *= mask;

                float3 color = _Color.rgb * input.color.rgb * _Intensity;
                return half4(color, input.color.a * mask);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
