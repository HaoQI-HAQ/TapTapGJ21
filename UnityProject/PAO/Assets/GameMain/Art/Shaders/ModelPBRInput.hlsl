// ---------------------------------------------------------------------------
// PAO / ModelPBR 的公共声明与取样函数
//
// 被 ModelPBR.shader 的四个 Pass 共用（和 BubbleBall.shader + BubbleBallCore.hlsl
// 一个路数）。这里只放"贴图声明 + 取样"，不放光照，免得 ShadowCaster / DepthOnly
// 也跟着把 Lighting.hlsl 拖进来编译。
// ---------------------------------------------------------------------------
#ifndef PAO_MODEL_PBR_INPUT_INCLUDED
#define PAO_MODEL_PBR_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
TEXTURE2D(_MetallicMap);    SAMPLER(sampler_MetallicMap);
TEXTURE2D(_RoughnessMap);   SAMPLER(sampler_RoughnessMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);
TEXTURE2D(_OpacityMap);     SAMPLER(sampler_OpacityMap);

// SRP Batcher 要求材质属性全部集中在一个叫 UnityPerMaterial 的 CBUFFER 里，
// 少写一个（哪怕只是给混合状态用的）都会让这个 shader 变成 "not compatible"。
// 贴图本身不进来，只有 _BaseMap_ST 这个缩放偏移要进来。
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _NormalScale;
    half   _Metallic;
    half   _Roughness;
    half4  _EmissionColor;
    half   _Cutoff;
    half   _Opacity;
    float  _AlphaClip;
    float  _EmissionEnabled;
    float  _UseOpacityMap;
    float  _TransparentMode;
    float  _SrcBlend;
    float  _DstBlend;
    float  _ZWrite;
    float  _Cull;

    // ---- 描边（反向外壳）----
    half4  _OutlineColor;
    half   _OutlineWidth;
    float  _OutlineEnabled;
CBUFFER_END

// 基础色。RGB 是 albedo，A 交给 SampleOpacity 处理。
half4 SampleAlbedoAlpha(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
}

// 不透明度。四个 Pass 都用这一个函数，保证"被裁掉的地方不投影"。
//
// 默认直接用 BaseColor 的 alpha：Jar 的 *_BaseColor.png 自带 alpha（0.66~1.0），
// 而 Jar_op.png 就是同一份数据。两个一起乘会把透明度平方，所以这里是二选一：
// 勾了 _UseOpacityMap 才改用独立的 op 贴图。
half SampleOpacity(float2 uv)
{
    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;

#if defined(_OPACITYMAP)
    a = SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, uv).r;
#endif

    return saturate(a * _Opacity);
}

// 金属度。灰度图统一取 .r —— 导入设置里 Single Channel 要选 Red，
// 若显示 Alpha 则这张图会被导成 Alpha8，这里读到的就恒为 1。
half SampleMetallic(float2 uv)
{
    return saturate(SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, uv).r * _Metallic);
}

// 粗糙度 -> 光滑度。URP 的 BRDF 吃的是 smoothness，所以这里翻一下。
half SampleSmoothness(float2 uv)
{
    half roughness = saturate(SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r * _Roughness);
    return 1.0h - roughness;
}

// 切线空间法线转世界空间。tangentWS.w 是切线手性符号（-1/1），
// bitangent 由叉乘现算，和 URP 内置 Lit 的做法一致。
half3 SampleNormalWS(float2 uv, half3 normalWS, half4 tangentWS)
{
#if defined(_NORMALMAP)
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalScale);
    real  sgn = tangentWS.w;
    real3 bitangent = sgn * cross(normalWS, tangentWS.xyz);
    half3 n = TransformTangentToWorld(normalTS, half3x3(tangentWS.xyz, bitangent, normalWS));
    return NormalizeNormalPerPixel(n);
#else
    return NormalizeNormalPerPixel(normalWS);
#endif
}

// 自发光。Jellyfish_Emissive 大部分是黑的、只有稀疏亮点，
// 所以颜色是 [HDR] 的，靠把颜色推亮来做出"发光点"。
half3 SampleEmission(float2 uv)
{
#if defined(_EMISSION)
    return SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
#else
    return half3(0.0h, 0.0h, 0.0h);
#endif
}

#endif // PAO_MODEL_PBR_INPUT_INCLUDED
