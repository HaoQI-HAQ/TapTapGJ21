Shader "PAO/CameraOcclusionParticles"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.16, 0.33, 0.36, 1)
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        _LineColor("Grid color", Color) = (0.32, 0.60, 0.59, 1)
        _CellSize("Grid size", Float) = 2
        [HideInInspector] _Grid("Grid", Float) = 0
        _OcclusionFade("Occlusion", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _LineColor;
                float4 _BaseMap_ST;
                float _CellSize, _Grid, _OcclusionFade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 world : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.normal = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.world);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            float2 Hash(float2 p)
            {
                return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normal);
                half3 color = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                if (_Grid > .5)
                {
                    float2 uv = input.world.xz / max(.01, _CellSize);
                    float2 grid = abs(frac(uv - .5) - .5) / max(fwidth(uv), .0001);
                    float2 major = abs(frac(uv / 4 - .5) - .5) / max(fwidth(uv / 4), .0001);
                    float gridLine = max((1 - saturate(min(grid.x, grid.y))) * .55,
                        (1 - saturate(min(major.x, major.y))) * .55);
                    color = lerp(color, _LineColor.rgb, gridLine);
                }
                else
                {
                    Light light = GetMainLight();
                    color *= SampleSH(normal) + light.color * saturate(dot(normal, light.direction));
                }
                // Project onto the surface's dominant plane, so vertical walls and floors both have round motes.
                float3 weights = abs(normal);
                float2 plane = weights.y > max(weights.x, weights.z) ? input.world.xz :
                    (weights.x > weights.z ? input.world.zy : input.world.xy);
                float2 field = plane * 5.0;
                float2 cell = floor(field);
                float2 random = Hash(cell);
                float2 center = .5 + sin(_Time.y * .65 + random * 6.283) * .23;
                float radius = lerp(.10, .24, random.x);
                float d = length(frac(field) - center);
                float softDot = 1 - smoothstep(radius * .12, radius, d);
                float presence = step(.62, random.y);
                float shimmer = .72 + .28 * sin(_Time.y * 1.5 + random.x * 12);
                float solid = pow(1 - saturate(_OcclusionFade), 3);
                float distanceFade = 1 - smoothstep(6, 24, distance(_WorldSpaceCameraPos, input.world));
                float particle = softDot * presence * shimmer * .18 * distanceFade;
                float alpha = lerp(solid, particle, _OcclusionFade);
                clip(alpha - .002);
                half3 moteColor = lerp(_BaseColor.rgb, half3(.65, .95, .91), .55);
                color = lerp(color, moteColor, _OcclusionFade);
                return half4(MixFog(color, input.fog), alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
