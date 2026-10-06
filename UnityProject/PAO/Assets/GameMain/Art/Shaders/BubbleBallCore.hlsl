#ifndef PAO_BUBBLE_BALL_CORE
#define PAO_BUBBLE_BALL_CORE

// ---------------------------------------------------------------------------
// 卡通渲染泡泡球 · 核心实现
//
// 被 BubbleBall.shader 的四个 Pass 共用：
//   不定义 PAO_BACKFACE / PAO_OUTLINE = 正面（主要着色）
//   定义   PAO_BACKFACE               = 背面（垫底，让泡泡有体积）
//   定义   PAO_OUTLINE                = 描边（反向外壳）
//
// ── 卡通感从哪来 ──
//   1. 色阶量化：菲涅尔边缘、高光、彩虹全部走 XStep() 压成硬边色块，
//      而不是真实感的平滑渐变
//   2. 反向外壳描边：沿法线外扩一圈纯色，勾出干净的外轮廓
//   3. 低频流动扰动：顶点起伏 + 法线扭曲，用正弦叠加而不是高频噪声 ——
//      卡通要的是"液态的流动感"，不是颗粒感
// ---------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// ---- 材质参数（所有 Pass 共用，必须一致）----
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

    // ---- 卡通着色 ----
    float  _ToonSteps;          // 色阶数
    float  _ToonSoftness;       // 色阶边缘软度（0 = 硬边）
    float  _RimThreshold;       // 边缘光从哪个菲涅尔值开始亮
    float  _SpecThreshold;      // 高光从哪个 NdotH 开始亮
    float4 _IridColorA;         // 卡通彩虹的两个色相
    float4 _IridColorB;
    float4 _SpecColor;

    // ---- 扰动 ----
    float  _WobbleAmplitude;    // 顶点起伏幅度（相对半径）
    float  _WobbleFrequency;
    float  _WobbleSpeed;
    float  _NormalWobble;       // 法线扰动量（高光/边缘会被搅动）

    // ---- 描边 ----
    float4 _OutlineColor;
    float  _OutlineWidth;
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
    float  wobble     : TEXCOORD4;   // 本顶点的起伏量，片元里用来让着色同步晃动
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// ======================= 卡通色阶 =======================

// 把 t 量化成 Steps 级。Softness 控制台阶边缘的软硬，
// 0 = 完全硬边（经典卡通），0.05 左右会有一点过渡（更柔和）。
float XStep(float t, float steps, float softness)
{
    steps = max(steps, 1.0);
    float scaled = saturate(t) * steps;
    float idx    = floor(scaled);
    float f      = scaled - idx;
    float soft   = max(softness, 1e-4);
    return (idx + smoothstep(0.5 - soft, 0.5 + soft, f)) / steps;
}

// ======================= 扰动 =======================
//
// 关键：先 normalize(positionOS)，这样起伏是沿「半径方向」的，
// 泡泡会保持球形鼓包，而不是被拉成奇怪的形状。
// 用三组不同频率/方向的正弦叠加，得到有机的、非重复的流动感。
float WobbleAmount(float3 dirOS, float t)
{
    float3 d1 = float3( 1.00,  0.35,  0.20);
    float3 d2 = float3(-0.30,  1.00,  0.45);
    float3 d3 = float3( 0.25, -0.40,  1.00);

    float n = sin(dot(dirOS, d1) * 4.1 + t * 1.00)
            + sin(dot(dirOS, d2) * 5.7 + t * 1.37)
            + sin(dot(dirOS, d3) * 3.3 + t * 0.81);

    return n * (1.0 / 3.0);
}

Varyings Vert(Attributes input)
{
    Varyings o = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);

    float3 posOS = input.positionOS.xyz;

    // ---- 顶点起伏 ----
    float wob = 0.0;
#ifndef PAO_OUTLINE
    // 描边 Pass 不加扰动，否则描边和本体各晃各的，会错位
    if (_WobbleAmplitude > 1e-5)
    {
        float3 dirOS = normalize(posOS + 1e-5);
        wob = WobbleAmount(dirOS * _WobbleFrequency, _Time.y * _WobbleSpeed);
        posOS += dirOS * (wob * _WobbleAmplitude);
    }
#endif

    VertexPositionInputs pos = GetVertexPositionInputs(posOS);
    VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

    o.positionWS = pos.positionWS;
    o.normalWS   = nrm.normalWS;
    o.uv         = input.uv;
    o.positionOS = posOS;
    o.wobble     = wob;

#ifdef PAO_OUTLINE
    // ---- 反向外壳描边 ----
    // 沿法线在世界空间外扩。用变换后的世界法线 + 世界空间偏移，
    // 这样泡泡被缩放时描边粗细仍然均匀。
    //
    // 注意：offset 不参与裁剪空间的 w 除法，所以透视下远近粗细会略有变化。
    // 这是反向外壳法的固有特性，卡通渲染里通常可以接受。
    float3 offsetWS = normalize(nrm.normalWS) * _OutlineWidth;
    o.positionCS = TransformWorldToHClip(pos.positionWS + offsetWS);
#else
    o.positionCS = pos.positionCS;
#endif

    return o;
}

// ======================= 着色 =======================

half4 Frag(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);

#ifdef PAO_OUTLINE
    return half4(_OutlineColor.rgb, _OutlineColor.a);
#else

    float3 N = normalize(i.normalWS);

#ifdef PAO_BACKFACE
    // 背面：法线翻转，贡献压低 —— 泡泡远端的膜本来就是透过近端看的
    N = -N;
    const float faceFade = 0.42;
#else
    const float faceFade = 1.0;
#endif

    float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

    // ---- 法线扰动：用扰动量的梯度近似地"推"一下法线 ----
    // 让高光和边缘光跟着表面一起晃动，泡泡才有"活着"的感觉。
    // 幅度刻意压得很小：卡通风格要的是缓慢流动，不是高频颗粒。
    if (_NormalWobble > 1e-5)
    {
        float3 nOff = float3(
            WobbleAmount(normalize(N + float3(0.08, 0.0, 0.0)) * _WobbleFrequency, _Time.y * _WobbleSpeed),
            WobbleAmount(normalize(N + float3(0.0, 0.08, 0.0)) * _WobbleFrequency, _Time.y * _WobbleSpeed),
            WobbleAmount(normalize(N + float3(0.0, 0.0, 0.08)) * _WobbleFrequency, _Time.y * _WobbleSpeed));
        N = normalize(N + nOff * _NormalWobble);
    }

    float NoV = saturate(dot(N, V));
    float fresnel = pow(1.0 - NoV, max(_RimPower, 0.5));

    // ================= 边缘光（卡通色阶）=================
    float rimBand = XStep(saturate((fresnel - _RimThreshold) / max(1.0 - _RimThreshold, 1e-3)),
                          _ToonSteps, _ToonSoftness);
    float rim = rimBand * _RimIntensity;

    // ================= 高光（卡通硬边）=================
    Light mainLight = GetMainLight();
    float3 L = mainLight.direction;
    float3 Hv = normalize(L + V);
    float  NoH = saturate(dot(N, Hv));

    float specBand = XStep(saturate((NoH - _SpecThreshold) / max(1.0 - _SpecThreshold, 1e-3)),
                           max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    float3 spec = specBand * _SpecIntensity * mainLight.color * _SpecColor.rgb;

    // 第二颗补光，让球体另一侧也有个硬边亮点 —— 卡通常见的"双高光"
    float3 L2 = normalize(float3(-L.x, -L.y * 0.5, -L.z));
    float3 Hv2 = normalize(L2 + V);
    float  spec2Band = XStep(saturate((saturate(dot(N, Hv2)) - _SpecThreshold) /
                                      max(1.0 - _SpecThreshold, 1e-3)),
                             max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    float3 spec2 = spec2Band * _Spec2Intensity * float3(1.0, 1.0, 1.0);

    // ================= 薄膜干涉（卡通双色）=================
    // 真实泡泡的彩虹是连续光谱，卡通化会糊成一团脏色。
    // 这里压成「两个色相之间来回」，再叠一层量化，读起来清楚得多。
    float thickNoise = WobbleAmount(normalize(i.positionOS + 1e-5) * 2.5,
                                    _Time.y * _FlowSpeed * 3.0);
    float phase = _IridescenceScale * (1.0 - NoV) * 3.6 + thickNoise * _ThicknessNoise;

    // 用相位在两个色相之间插值，再做量化保留色阶感
    float mixT = XStep(frac(phase * 0.5), _ToonSteps, _ToonSoftness * 0.5);
    float3 rainbow = lerp(_IridColorA.rgb, _IridColorB.rgb, mixT);

    // 只在掠射角显色，并且本身也过一遍色阶
    float iridBand = XStep(saturate(1.0 - NoV), _ToonSteps, _ToonSoftness);
    float3 irid = rainbow * iridBand * _Iridescence;

    // ================= 环境色带 =================
    // 卡通风格里环境反射通常简化成一层"天光渐变"，不做真实立方体采样。
    // 想让泡泡更贴场景的话，把 _Cube 配上会叠加真实反射。
    float envBand = XStep(saturate(NoV), max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    float3 env = lerp(_EnvColor.rgb, float3(1.0, 1.0, 1.0), envBand * 0.35) * _EnvIntensity;

    float3 cubeRef = _Cube.SampleLevel(sampler_Cube, reflect(-V, N), 0).rgb;
    env += cubeRef * _EnvIntensity * 0.5;

    // ================= 透射 / 背光 =================
    float3 T = refract(-V, N, 0.92);
    float3 transCube = _Cube.SampleLevel(sampler_Cube, T, 0).rgb;
    float  backLight = saturate(dot(-V, L));
    float  transBand = XStep(backLight, max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    float3 trans = (_TransmissionTint.rgb * (0.6 + 0.4 * transBand) + transCube)
                 * _Transmission * 0.5;

    // ================= 合成 =================
    float3 col = _BaseColor.rgb * 0.16;
    col += irid;
    col += spec + spec2;
    col += env * 0.5;
    col += trans;
    col += _BaseColor.rgb * rim * 0.45;

    // 中心几乎全透、边缘收拢变实。这一步也走色阶，边缘会有一圈清晰的"壳"
    float alphaBand = XStep(saturate(fresnel * 0.8 + specBand + spec2Band),
                            _ToonSteps, _ToonSoftness);
    float a = saturate(_BaseColor.a * _Opacity * (0.10 + 0.90 * alphaBand));
    a = max(a, _Opacity * 0.05);

    a *= faceFade;

    return half4(col, a);
#endif
}

#endif // PAO_BUBBLE_BALL_CORE
