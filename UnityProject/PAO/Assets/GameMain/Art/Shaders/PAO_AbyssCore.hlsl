#ifndef PAO_ABYSS_CORE_INCLUDED
#define PAO_ABYSS_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "PAO_EnvironmentNoise.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _VoidColor;
    float4 _MistColorA;
    float4 _MistColorB;
    float4 _FoamColor;
    float4 _RingColor;
    float4 _MoteColor;

    float _Scale;
    float _Arms;
    float _Twist;
    float _SpinSpeed;
    float _VortexRadius;
    float _MistStrength;
    float _FoamStrength;
    float _RingStrength;
    float _MoteStrength;
    float _OuterFade;
CBUFFER_END

#endif
