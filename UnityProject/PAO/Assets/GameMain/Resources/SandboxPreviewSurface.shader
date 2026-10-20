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
                half diffuse = .38 + .62 * saturate(dot(n, key)) + .16 * saturate(dot(n, fill));
                half rim = pow(1 - saturate(dot(n, v)), 3);
                half specular = pow(saturate(dot(n, normalize(key + v))), lerp(45, 110, _Pearl));
                half broad = pow(saturate(dot(n, normalize(fill + v))), 16);
                half3 color = _BaseColor.rgb * diffuse;
                color += specular * lerp(.25, .7, _Pearl) + broad * .08;
                color += rim * lerp(half3(.035, .045, .06), half3(.14, .13, .2), _Pearl);
                half alpha = 1;
                if (_Ground > .5)
                {
                    float2 uv = input.local.xz / 2;
                    float2 grid = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                    float gridLine = 1 - saturate(min(grid.x, grid.y));
                    float2 stageUV = input.local.xz / max(_GroundRadius, .001);
                    float radius = length(stageUV);
                    float edge = smoothstep(.38, .96, radius);
                    float2 lightOffset = (stageUV - float2(-.16, .12)) * float2(1.15, .85);
                    float spotlight = exp(-dot(lightOffset, lightOffset) * 4.8);
                    float contact = 1 - .7 * exp(-dot(stageUV, stageUV) * 40);
                    color = (_BaseColor.rgb + half3(.18, .12, .057) * spotlight) * contact;
                    color += half3(.04, .034, .025) * gridLine * (.2 + .8 * spotlight);
                    // Write straight alpha into the isolated texture; the UI blends it once.
                    alpha = 1 - edge;
                    // The offscreen viewport ends before the controls. Let the floor dissolve
                    // into the theatre instead of exposing the texture's rectangular edge.
                    float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                    float2 screenEdge = min(screenUV, 1 - screenUV);
                    alpha *= smoothstep(0, .14, screenEdge.x) * smoothstep(0, .18, screenEdge.y);
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
        // The transparent atmosphere uses this depth to stop in front of model surfaces.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct DepthAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz); return output;
            }
            half DepthFrag(DepthVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
}
