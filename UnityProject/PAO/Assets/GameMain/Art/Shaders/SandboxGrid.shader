Shader "PAO/SandboxGrid"
{
    Properties
    {
        _BaseColor("Floor", Color) = (0.16, 0.33, 0.36, 1)
        _LineColor("Grid", Color) = (0.32, 0.60, 0.59, 1)
        _CellSize("Cell size", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _LineColor;
            float _CellSize;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.world);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.world.xz / max(.01, _CellSize);
                float2 grid = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                float gridLine = 1 - saturate(min(grid.x, grid.y));
                float2 major = abs(frac(uv / 4 - .5) - .5) / max(fwidth(uv / 4), .0001);
                float emphasis = (1 - saturate(min(major.x, major.y))) * .55;
                half3 color = lerp(_BaseColor.rgb, _LineColor.rgb, max(gridLine * .55, emphasis));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
