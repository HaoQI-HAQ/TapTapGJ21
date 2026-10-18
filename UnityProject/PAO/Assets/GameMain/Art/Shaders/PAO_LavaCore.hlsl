#ifndef PAO_LAVA_CORE_INCLUDED
#define PAO_LAVA_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "PAO_EnvironmentNoise.hlsl"

// SRP Batcher 要求材质属性集中在一个 UnityPerMaterial CBUFFER 里。
// 深度/法线 Pass 也 include 这个文件，保证四个 Pass 的 CBUFFER 布局一致。
CBUFFER_START(UnityPerMaterial)
    float4 _DarkColor;
    float4 _MidColor;
    float4 _HotColor;
    float4 _CrustColor;

    float _Scale;
    float _FlowSpeed;
    float _CrustAmount;
    float _CrustEdge;
    float _CrackWidth;
    float _CrackGlow;
    float _Bands;
    float _BandStrength;

    float _Emission;
    float _PulseSpeed;
    float _PulseAmount;
CBUFFER_END

#endif
