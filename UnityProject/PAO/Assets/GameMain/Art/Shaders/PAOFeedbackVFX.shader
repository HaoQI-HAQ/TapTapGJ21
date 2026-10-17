Shader "PAO/FeedbackVFX"
{
    Properties
    {
        _Shape ("Ring Amount", Range(0, 1)) = 0
        _Softness ("Ring Softness", Range(0.025, 0.4)) = 0.12
        _Brightness ("Brightness", Range(0, 4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FeedbackMembrane"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                float _Softness;
                float _Brightness;
            CBUFFER_END

            // Written by PAOVisualDirector once per frame. Unity initializes them to zero
            // when the director is absent, so authored materials remain valid on their own.
            float _PAO_Mood;
            float _PAO_Energy;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float theta = atan2(p.y, p.x);
                float wobble = 0.018 * sin(theta * 5.0 + _Time.y * 1.7)
                             + 0.009 * sin(theta * 9.0 - _Time.y * 1.1);

                // Both forms are analytically circular; no texture or imported sprite is
                // needed and the billboard's square corners can never become visible.
                float ringDistance = (radius - (0.73 + wobble)) / max(_Softness, 0.025);
                float ring = exp(-ringDistance * ringDistance);
                float pearl = 1.0 - smoothstep(0.52, 1.0, radius + wobble);
                float core = exp(-radius * radius * 8.0);
                float alpha = lerp(pearl * (0.65 + 0.35 * core), ring, saturate(_Shape));
                alpha *= input.color.a;
                clip(alpha - 0.003);

                float tension = saturate(0.5 - 0.5 * _PAO_Mood);
                float3 tint = lerp(input.color.rgb, float3(1.0, 0.52, 0.77), tension * 0.15);
                float glint = lerp(core, ring, saturate(_Shape));
                float3 rgb = tint * (0.7 + glint * 0.6) * _Brightness;
                rgb += float3(0.20, 0.55, 0.66) * saturate(_PAO_Energy) * glint * 0.18;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
