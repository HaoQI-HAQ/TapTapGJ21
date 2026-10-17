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

    // ---- 溶解 ----
    // 必须在这个 CBUFFER 里：SRP Batcher 要求材质上出现的属性全部集中定义，
    // 少一个（哪怕没用到）都会让整个 shader 变成 "not compatible"。
    float  _DissolveEnabled;
    float  _DissolveAmount;
    float  _DissolveNoise;
    float  _DissolveEdge;
    float4 _DissolveEdgeColor;
    float  _DissolveEdgeBoost;
CBUFFER_END

TEXTURECUBE(_Cube);
SAMPLER(sampler_Cube);

// Runtime scene response. The values are zero when PAOVisualDirector is absent.
float _PAO_Mood;
float _PAO_Energy;
float4 _PAO_Impulse; // world position xyz, decaying spatial reach w

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

// ======================= 溶解 =======================
//
// 【设计要点：只裁掉像素，不改任何着色】
// 这个函数只做两件事：
//   1. 算出这个像素处在溶解边界的哪一边 —— 该死掉的直接 clip 掉
//   2. 算出"离边界多近" —— 越近越亮，做出一条发光的燃烧边
//
// 除此之外的所有着色（彩虹虹彩、卡通色阶、高光、边缘光、透射）**完全不变**。
// 所以溶解期间泡泡还是原来那个泡泡，只是被一点点吃掉 —— 这正是要的效果。
//
// 【噪声为什么用 positionOS】
// Varyings 里已经带了顶点局部坐标，拿它算噪声不用额外传 UV，
// 而且泡泡是球体，用局部坐标能让"吃掉"的边界贴合球面、不会随视角滑动。

// 便宜的哈希 + 双线性插值噪声。三倍频叠加，边界才有有机的碎边，
// 单层噪声出来是一坨一坨的圆斑，很假。
float Hash21_D(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 34.23);
    return frac(p.x * p.y);
}

float ValueNoise_D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = Hash21_D(i);
    float b = Hash21_D(i + float2(1.0, 0.0));
    float c = Hash21_D(i + float2(0.0, 1.0));
    float d = Hash21_D(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float DissolveNoise(float3 posOS)
{
    float s = max(_DissolveNoise, 0.001);
    float2 p = posOS.xy * s + posOS.z * s * 0.6;

    float n1 = ValueNoise_D(p);
    float n2 = ValueNoise_D(p * 2.13 + 7.7);
    float n3 = ValueNoise_D(p * 4.41 + 23.3);

    return saturate(n1 * 0.55 + n2 * 0.30 + n3 * 0.15);
}

/// 溶解边界处的"余量"。&gt; 0 表示这个像素还在；&lt;= 0 表示该被裁掉。
///
/// 【为什么不在这个函数里 clip】
/// clip/discard 写在辅助函数里能工作，但编译器对"带 discard 的嵌套调用"
/// 的处理不如直接写在片元里稳妥（某些 target 下会额外警告或退化成慢路径）。
/// 所以这里只算数值，裁不裁由 Frag 决定 —— 见下面的 discard。
float DissolveMargin(float3 posOS)
{
    float noise = DissolveNoise(posOS);
    float threshold = _DissolveAmount * 1.02 - 0.01;
    return noise - threshold;
}

/// 溶解边界的亮度：0 = 离得远，1 = 正好在边界上（会被染成发光边）。
float DissolveEdgeGlow(float amount)
{
    return 1.0 - smoothstep(0.0, max(_DissolveEdge, 0.001), amount);
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
        posOS += dirOS * (wob * _WobbleAmplitude * (1.0 + saturate(_PAO_Energy) * 0.25));
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

    // ---- 溶解裁剪：必须在所有分支之前 ----
    //
    // 三个 Pass（描边 / 背面 / 正面）都要裁，否则泡泡溶解时会剩下一圈
    // 描边外壳和背面膜 —— 那就是"变成碎片"的样子，正是要避免的。
    // 所以放在 #ifdef 之前，让三个 Pass 共用同一套裁剪。
    //
    // 正常泡泡（_DissolveEnabled = 0）时 dissolveEdge 恒为 0，
    // 下面的 discard 一次都不会触发，不会为这个功能付任何代价。
    float dissolveEdge = 0.0;

    if (_DissolveEnabled > 0.5)
    {
        float dissolveMargin = DissolveMargin(i.positionOS);

        // 低于阈值的像素直接丢掉 —— 这一步才叫"被吃掉"。
        // discard 写在片元里，比塞进辅助函数里更稳。
        if (dissolveMargin < 0.0)
        {
            discard;
        }

        dissolveEdge = DissolveEdgeGlow(dissolveMargin);
    }

#ifdef PAO_OUTLINE
    // 描边也跟着溶解，但不加发光边（它本来就是个深色外壳）
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

    float impulseReach = max(_PAO_Impulse.w, 0.001);
    float impulse = saturate(_PAO_Impulse.w * 0.30) *
                    saturate(1.0 - distance(i.positionWS, _PAO_Impulse.xyz) / impulseReach);
    float3 moodAccent = lerp(float3(1.0, 0.55, 0.78), float3(0.55, 1.0, 0.92),
                             saturate(_PAO_Mood * 0.5 + 0.5));
    col += moodAccent * (saturate(_PAO_Energy) * 0.08 + impulse * 0.42);

    // 中心几乎全透、边缘收拢变实。这一步也走色阶，边缘会有一圈清晰的"壳"
    float alphaBand = XStep(saturate(fresnel * 0.8 + specBand + spec2Band),
                            _ToonSteps, _ToonSoftness);
    float a = saturate(_BaseColor.a * _Opacity * (0.10 + 0.90 * alphaBand));
    a = max(a, _Opacity * 0.05);

    a *= faceFade;
    a = saturate(a + impulse * 0.09);

    // ---- 溶解发光边 ----
    //
    // 只在溶解进行时叠加（dissolveEdge 为 0 时这段等价于什么都不加）。
    // 颜色用 _DissolveEdgeColor，默认接近白 —— 和泡泡本身的水色系不冲突，
    // 看起来像"边缘在发亮、然后消失"，而不是换了种材质。
    //
    // 顺带把边缘的 alpha 抬到 1：边界那一圈是"最薄、正在断掉"的地方，
    // 半透明会让它糊掉，看不出裁切形状。
    if (dissolveEdge > 0.001)
    {
        col += _DissolveEdgeColor.rgb * (dissolveEdge * _DissolveEdgeBoost);
        a = max(a, dissolveEdge);
    }

    return half4(col, a);
#endif
}

#endif // PAO_BUBBLE_BALL_CORE
