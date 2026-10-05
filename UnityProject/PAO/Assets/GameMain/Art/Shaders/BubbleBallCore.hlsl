#ifndef PAO_BUBBLE_BALL_CORE
#define PAO_BUBBLE_BALL_CORE

// ---------------------------------------------------------------------------
// 泡泡球材质的核心实现。
// 被 BubbleBall.shader 的两个 Pass 共用：
//   不定义 PAO_BACKFACE = 正面
//   定义   PAO_BACKFACE = 背面
// 两个 Pass 都写深度（ZWrite On），所以泡泡之间、泡泡与场景之间遮挡正常，
// 不会再有"贴在屏幕上"的观感。
// ---------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// ---- 材质参数（两块 Pass 共用，必须一致）----
CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor;
    float  _Opacity;
    float  _RimPower;
    float  _RimIntensity;

    float  _Iridescence;
    float  _IridescenceScale;
    float  _ThicknessNoise;
    float  _FlowSpeed;

    float  _SpecIntensity;
    float  _SpecPower;
    float  _Spec2Intensity;

    float4 _EnvColor;
    float  _EnvIntensity;

    float  _Transmission;
    float4 _TransmissionTint;
CBUFFER_END

TEXTURECUBE(_Cube);
SAMPLER(sampler_Cube);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float2 uv         : TEXCOORD2;
    float3 positionOS : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings Vert(Attributes input)
{
    Varyings o = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);

    VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

    o.positionCS = pos.positionCS;
    o.positionWS = pos.positionWS;
    o.normalWS   = nrm.normalWS;
    o.uv         = input.uv;
    o.positionOS = input.positionOS.xyz;

    return o;
}

// ---- 噪声：给膜厚加扰动，免得彩虹是一圈死板的同心圆 ----
float Hash13(float3 p)
{
    p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// 三轴正弦叠加的低频噪声，够用且便宜
float BubbleNoise(float3 p)
{
    return sin(p.x * 3.1 + sin(p.y * 2.3)) *
           sin(p.y * 2.7 + sin(p.z * 3.7)) *
           sin(p.z * 3.3 + sin(p.x * 2.1));
}

// 余弦调色板（Inigo Quilez）：把相位映射成彩虹
float3 Palette(float t)
{
    return 0.5 + 0.5 * cos(6.28318530718 * (t + float3(0.0, 0.33, 0.67)));
}

half4 Frag(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);

    float3 N = normalize(i.normalWS);

    // 背面：法线翻转，贡献也弱一些 ——
    // 泡泡远端的膜是透过近端看的，本来就更淡
#ifdef PAO_BACKFACE
    N = -N;
    const float backFaceFade = 0.45;
#else
    const float backFaceFade = 1.0;
#endif

    float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
    float  NoV = saturate(dot(N, V));

    // ---- 薄膜干涉彩虹 ----
    // 真实泡泡的膜厚约 100~1000nm，光程差 ≈ 2·n·d·cosθ 随观察角变化，
    // 各波长交替相长 / 相消，于是形成一圈圈彩虹。
    // 这里用「相位随 (1 - NoV) 增长」近似，再叠一层随时间流动的膜厚噪声。
    float thickNoise = BubbleNoise(i.positionOS * 2.5 + _Time.y * _FlowSpeed);
    float phase = _IridescenceScale * (1.0 - NoV) * 3.6
                + thickNoise * _ThicknessNoise * 3.0;
    float3 rainbow = Palette(phase);

    // 彩虹只在掠射角明显，正对相机的中心几乎不显色
    float iridMask = pow(1.0 - NoV, 2.0) * _Iridescence;

    // ---- 菲涅尔：边缘亮、中心暗 ----
    float fresnel = pow(1.0 - NoV, max(_RimPower, 0.5)) * _RimIntensity;

    // ---- 高光 ----
    // 需要法线在世界空间，且视图空间转世界空间，才能保证和 "万向" 的太阳一致
    Light mainLight = GetMainLight();
    float3 L = mainLight.direction;
    float3 Hv = normalize(L + V);
    float  spec = pow(saturate(dot(N, Hv)), max(_SpecPower, 1.0))
                * _SpecIntensity * mainLight.color;

    // 第二颗较弱的高光，模拟"另一侧的天光/补光"，让球体更有玻璃感
    float3 L2 = normalize(float3(-L.x, -L.y * 0.5, -L.z));
    float3 Hv2 = normalize(L2 + V);
    float  spec2 = pow(saturate(dot(N, Hv2)), max(_SpecPower * 0.45, 1.0))
                 * _Spec2Intensity;

    // ---- 环境反射 ----
    float3 R = reflect(-V, N);
    float3 env = _Cube.SampleLevel(sampler_Cube, R, 0).rgb;
    // 没配 Cube 时退回环境色
    env = lerp(_EnvColor.rgb, env, step(0.001, dot(env, env) + 0.001));
    env *= _EnvIntensity * (0.4 + fresnel);

    // ---- 透射 / 背光 ----
    // 用环境色近似透过泡泡看到的背景，再加一点主光的背光散射
    float3 T = refract(-V, N, 0.92);
    float3 trans = _Cube.SampleLevel(sampler_Cube, T, 0).rgb;
    trans = lerp(_EnvColor.rgb, trans, step(0.001, dot(trans, trans) + 0.001));
    float backLight = saturate(dot(-V, L)) * 0.5 + 0.5;
    trans *= _TransmissionTint.rgb * _Transmission * backLight;

    // ---- 合成 ----
    float3 col = _BaseColor.rgb * 0.18;          // 极淡的体色
    col += rainbow * iridMask;
    col += float3(1.0, 1.0, 1.0) * spec;
    col += float3(1.0, 1.0, 1.0) * spec2;
    col += env;
    col += trans;
    col += _BaseColor.rgb * fresnel * 0.5;

    // 球体中心几乎全透，边缘收拢变实 —— 这是"肥皂泡"最关键的一步
    float a = saturate(_BaseColor.a * _Opacity * (0.12 + 0.88 * saturate(fresnel * 0.8 + spec + spec2)));
    a = max(a, _Opacity * 0.06);                 // 保证不至于完全消失

    a *= backFaceFade;

    return half4(col, a);
}

#endif // PAO_BUBBLE_BALL_CORE
