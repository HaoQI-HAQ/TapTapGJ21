// Camera-local haze also reaches imported materials that do not implement URP fog.
Shader "PAO/TempleWaterHaze"
{
    Properties
    {
        _HazeColor ("Water colour", Color) = (.36,.63,.74,1)
        _SkyColor ("Upper water light", Color) = (.61,.73,.86,1)
        _Density ("Distance haze", Float) = .016
        _NearClear ("Clear foreground metres", Float) = 3
        _MaxOpacity ("Maximum haze", Range(0,1)) = .88
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+150" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half4 _SkyColor;
                float _Density;
                float _NearClear;
                float _MaxOpacity;
            CBUFFER_END
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.uv = GetFullScreenTriangleTexCoord(vertexID);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 view = world - _WorldSpaceCameraPos;
                float distanceToSurface = length(view);
                float skyAmount = saturate(view.y / max(distanceToSurface,.001) * 1.4 + .28);
                half3 colour = lerp(_HazeColor.rgb,_SkyColor.rgb,skyAmount);
                float opticalDepth = max(0, distanceToSurface - _NearClear) * _Density;
                half alpha = min(_MaxOpacity, 1 - exp(-opticalDepth));
                return half4(colour*alpha,alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
