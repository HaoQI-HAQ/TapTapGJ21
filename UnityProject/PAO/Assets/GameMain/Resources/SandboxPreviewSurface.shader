Shader "PAO/SandboxPreviewSurface"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.4, 0.85, 0.8, 1)
        _Pearl("Pearl finish", Range(0, 1)) = 1
        _Ground("Ground", Float) = 0
        _GroundRadius("Ground radius", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Pearl, _Ground, _GroundRadius;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; half3 normal : TEXCOORD1;
                float3 local : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.normal = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.world);
                output.local = input.positionOS.xyz;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normal);
                half3 v = normalize(GetWorldSpaceViewDir(input.world));
                half3 key = normalize(half3(-.5, .8, -.6));
                half3 fill = normalize(half3(.8, .35, .4));
                half diffuse = .48 + .44 * saturate(dot(n, key)) + .18 * saturate(dot(n, fill));
                half rim = pow(1 - saturate(dot(n, v)), 3);
                half specular = pow(saturate(dot(n, normalize(key + v))), lerp(45, 110, _Pearl));
                half broad = pow(saturate(dot(n, normalize(fill + v))), 16);
                half3 color = _BaseColor.rgb * diffuse;
                color += specular * lerp(.25, .7, _Pearl) + broad * .08;
                color += rim * lerp(half3(.035, .045, .06), half3(.14, .13, .2), _Pearl);
                if (_Ground > .5)
                {
                    float2 uv = input.local.xz / 2;
                    float2 grid = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                    float gridLine = 1 - saturate(min(grid.x, grid.y));
                    float edge = smoothstep(_GroundRadius * .45, _GroundRadius, length(input.local.xz));
                    color = lerp(_BaseColor.rgb * .7 + gridLine * .045, half3(.065, .085, .11), edge);
                }
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
