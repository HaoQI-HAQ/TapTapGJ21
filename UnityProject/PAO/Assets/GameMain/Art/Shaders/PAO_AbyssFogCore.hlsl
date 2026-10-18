#ifndef PAO_ABYSS_FOG_CORE_INCLUDED
#define PAO_ABYSS_FOG_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "PAO_EnvironmentNoise.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _FogColor;
    float _Opacity;
    float _Scale;
    float _FlowSpeed;
    float _Threshold;
    float _Softness;
    float _EdgeFade;
    float _Seed;
CBUFFER_END

#endif
