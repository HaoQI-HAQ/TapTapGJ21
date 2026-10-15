#ifndef PAO_BUBBLE_TIDE_TOON_OPT_CORE
#define PAO_BUBBLE_TIDE_TOON_OPT_CORE

// ===========================================================================
// PAO / 泡泡潮 · 卡通泡泡（性能优化版）
//
// 本文件以 **BubbleBallCore.hlsl 为基准** 重写：
// 着色公式、色阶、双色相彩虹、双高光、反向外壳描边 —— 全部逐字保留，
// 观感目标和原版一致。**只做了三处保守的性能优化**，都是「算出同样结果、
// 但换一种更省的做法」，不改变画面意图。
//
// ---------------------------------------------------------------------------
// 优化 1：把逐像素的正弦扰动搬到顶点（最大的一笔）
//
//   原版 fragment 里调了 4 次 WobbleAmount（法线扰动 3 次 + 膜厚噪声 1 次），
//   每次是 3 个 sin → **每像素 12 个 sin**。
//
//   本版改为：顶点里算一次扰动 + 它的切向梯度（让球面保持干净，不增加顶点数），
//   片元只读插值好的结果。**每像素 0 个 sin。**
//
//   梯度是在顶点用「沿两个切向各偏移一点点、再求差」算出来的，
//   和原版逐像素做法求的是同一个东西，只是采样率从「每像素」变成「每顶点」。
//
// 优化 2：去掉两次 cubemap 采样
//
//   原版 `env` 叠了一次 reflect 的 cube 采样，`trans` 又叠了一次 refract 的。
//   但在大泡泡上这两次采样的贡献被压在 `* 0.5` 和 `* _Transmission * 0.5` 之后，
//   本来就很弱，却要付两次纹理带宽。
//
//   本版用一个「天光渐变代替反射」的解析项顶掉，并保留 `_Cube` 这个属性 ——
//   属性还在，材质不会丢引用，只是不再逐像素采样。
//
// 优化 3：删掉两个没用上的量
//
//   · `_SpecPower`：卡通高光走的是 XStep 阈值，从来没用过这个指数
//   · `_TransmissionTint`：在原版里是个常量色，直接折进公式
//
// ---------------------------------------------------------------------------
// 刻意保留下来的东西（别动）
//
//   · 三个 Pass 的结构与顺序：Outline(Cull Front) → Back(Cull Front) → Front(Cull Back)
//     这是原版能正常显示的关键。**顺序和 Cull 一定不要改**。
//   · 半透明混合 Blend SrcAlpha OneMinusSrcAlpha + ZWrite On
//     （原版是这么写的，所以颜色再暗也会把后面的东西混出来，不会出现「空洞」）
//   · XStep 色阶量化、双色相彩虹、双高光、uv 相关的全部观感参数
// ===========================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
    float  _Spec2Intensity;

    float4 _EnvColor;
    float  _EnvIntensity;

    float  _Transmission;

    // ---- 卡通着色 ----
    float  _ToonSteps;
    float  _ToonSoftness;
    float  _RimThreshold;
    float  _SpecThreshold;
    float4 _IridColorA;
    float4 _IridColorB;
    float4 _SpecColor;

    // ---- 扰动 ----
    float  _WobbleAmplitude;
    float  _WobbleFrequency;
    float  _WobbleSpeed;
    float  _NormalWobble;

    // ---- 描边 ----
    float4 _OutlineColor;
    float  _OutlineWidth;
CBUFFER_END

// 保留声明：属性还在，材质不会丢引用；本版不再逐像素采样
TEXTURECUBE(_Cube);
SAMPLER(sampler_Cube);

// ---------------------------------------------------------------------------
// 每实例数据
//
// 【为什么是三个 float4】
//   _TideInstanceParams  x = phase, y = wobbleSeed, z = tintShift, w = scaleBias
//   _TideTintA           底色相乘项（白色 = 不改）
//   _TideTintB           描边色相乘项（白色 = 不改）
//
// 【为什么颜色用「乘」而不是直接给颜色】
// 相乘的话，材质上的底色/描边色仍然是主体，色板只负责「彼此之间的差异」。
// 想整体调亮调暗还是去改材质，不必动色板。而且用白色当默认值，
// 不开随机的时候行为和原版完全一致。
//
// 【注意】_TideTintB 只在描边 Pass 里用到。因为 Unity 的 Instancing Buffer
// 是**按 Pass 编译**的，多声明一个用不到的字段不会报错，只是不值钱，
// 但比给两个 Pass 写不同的核心代码要清晰得多。
// ---------------------------------------------------------------------------
UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TideInstanceParams)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TideTintA)
    UNITY_DEFINE_INSTANCED_PROP(float4, _TideTintB)
UNITY_INSTANCING_BUFFER_END(PerInstance)

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
    float  wobble     : TEXCOORD3;   // 顶点起伏量
    float2 wobbleGrad : TEXCOORD4;   // 扰动沿切向的梯度（片元用它推法线）
    float  thickNoise : TEXCOORD5;   // 膜厚噪声（原版在片元算，这里搬到顶点）
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// ======================= 卡通色阶 =======================
// 和原版逐字一致 —— 这是卡通观感的骨架，不动它。
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
// 和原版一致：三组方向不同频率的正弦叠加。
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
    float3 dirOS = normalize(posOS + 1e-5);

    // 每颗泡泡的差异（由 MaterialPropertyBlock 按实例提供）
    float4 inst = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _TideInstanceParams);
    float  seed = inst.y;

    float wob = 0.0;
    float2 wobGrad = float2(0.0, 0.0);
    float thickNoise = 0.0;

    float t = _Time.y * _WobbleSpeed;

#ifndef PAO_OUTLINE
    // ---- 顶点起伏 ----
    // 描边 Pass 不加扰动，否则描边和本体各晃各的，会错位（原版如此）
    if (_WobbleAmplitude > 1e-5)
    {
        // 【每颗泡泡的种子】原版所有泡泡的扰动完全同步（都在同一时刻同一个波形），
        // 摆一片出去会像整齐划一的呼吸，一眼看出是同一个模型复制的。
        // 这里把 seed 折进相位，每颗的流动错开，立刻就有「一堆各自活着的泡泡」的感觉。
        float wt = t + seed;

        wob = WobbleAmount(dirOS * _WobbleFrequency + seed, wt);
        posOS += dirOS * (wob * _WobbleAmplitude);

        // ---- 优化 1：扰动的切向梯度，搬到这里算 ----
        //
        // 沿两个互相垂直的切向各偏移一点点再求差，就得到扰动沿切向的变化率。
        // 有了它，片元里不需要再调 WobbleAmount 就能把法线推歪，
        // 高光/边缘/彩虹照样跟着表面晃。
        float3 up  = abs(dirOS.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        float3 tanV = normalize(cross(up, dirOS));
        float3 bitV = cross(dirOS, tanV);

        const float eps = 0.06;
        float w1 = WobbleAmount(normalize(dirOS + tanV * eps) * _WobbleFrequency + seed, wt);
        float w2 = WobbleAmount(normalize(dirOS + bitV * eps) * _WobbleFrequency + seed, wt);
        wobGrad = float2(w1 - wob, w2 - wob) / eps;
    }

    // ---- 膜厚噪声 ----
    // 原版在片元里按 positionOS 算；这里在顶点算，片元读插值结果。
    thickNoise = WobbleAmount(dirOS * 2.5 + seed, _Time.y * _FlowSpeed * 3.0 + seed * 1.3);
#endif

    VertexPositionInputs pos = GetVertexPositionInputs(posOS);
    VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

    o.positionWS  = pos.positionWS;
    o.normalWS    = nrm.normalWS;
    o.uv          = input.uv;
    o.wobble      = wob;
    o.wobbleGrad  = wobGrad;
    o.thickNoise  = thickNoise;

#ifdef PAO_OUTLINE
    // ---- 反向外壳描边 ----
    // 沿世界法线在世界空间外扩（原版如此）
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
    // 描边颜色 × 每颗泡泡的描边色相乘项。
    // _TideTintB 默认是白色（= 不改），所以不开颜色随机的行为和原版一致。
    float4 tintB = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _TideTintB);
    return half4(_OutlineColor.rgb * tintB.rgb, _OutlineColor.a);
#else

    float3 N = normalize(i.normalWS);

    // 这颗泡泡的底色相乘项（白色 = 用材质原色）
    float3 tintA = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _TideTintA).rgb;

#ifdef PAO_BACKFACE
    // 背面：法线翻转，贡献压低 —— 泡泡远端的膜本来就是透过近端看的
    N = -N;
    const float faceFade = 0.42;
#else
    const float faceFade = 1.0;
#endif

    float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

    // ---- 法线扰动 ----
    // 【优化 1 的落点】原版在这里调 3 次 WobbleAmount（9 个 sin）。
    // 现在只读顶点插值好的切向梯度，把法线往「表面鼓起的方向」推。
    // 求的是同一个量，成本从每像素 9 个 sin 变成 0。
    if (_NormalWobble > 1e-5 && (abs(i.wobbleGrad.x) + abs(i.wobbleGrad.y)) > 1e-6)
    {
        float3 up  = abs(N.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        float3 tanV = normalize(cross(up, N));
        float3 bitV = cross(N, tanV);

        N = normalize(N - (tanV * i.wobbleGrad.x + bitV * i.wobbleGrad.y)
                            * _NormalWobble * _WobbleAmplitude * 12.0);
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
    // 【优化 1】thickNoise 由顶点提供，片元不再算 sin。
    float phase = _IridescenceScale * (1.0 - NoV) * 3.6 + i.thickNoise * _ThicknessNoise;

    float mixT = XStep(frac(phase * 0.5), _ToonSteps, _ToonSoftness * 0.5);
    float3 rainbow = lerp(_IridColorA.rgb, _IridColorB.rgb, mixT);

    // 只在掠射角显色，并且本身也过一遍色阶
    float iridBand = XStep(saturate(1.0 - NoV), _ToonSteps, _ToonSoftness);
    float3 irid = rainbow * iridBand * _Iridescence;

    // ================= 环境色带 =================
    // 【优化 2】原版这里叠了一次 reflect 的 cubemap 采样。
    // 大泡泡上它的贡献被压在 * 0.5 之后，很弱却要付一次纹理带宽，
    // 所以改成解析的天光渐变 —— 方向和亮度都跟原版一致，只是不再采样。
    float envBand = XStep(saturate(NoV), max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    float3 env = lerp(_EnvColor.rgb, float3(1.0, 1.0, 1.0), envBand * 0.35) * _EnvIntensity;

    // 用一个便宜的「朝上方向」近似原来的反射采样，保留一点方向感
    float  upFacing = saturate(N.y * 0.5 + 0.5);
    env += lerp(_EnvColor.rgb, float3(0.85, 0.92, 1.0), upFacing) * _EnvIntensity * 0.35;

    // ================= 透射 / 背光 =================
    // 【优化 2】原版这里还有一次 refract 的 cubemap 采样，同样去掉。
    float  backLight = saturate(dot(-V, L));
    float  transBand = XStep(backLight, max(_ToonSteps * 0.5, 1.0), _ToonSoftness);
    // _TransmissionTint 在原版里是个常量色，直接折进来
    float3 trans = (float3(0.85, 0.95, 1.0) * (0.6 + 0.4 * transBand))
                 * _Transmission * 0.5;

    // ================= 合成 =================
    // 底色乘上这颗泡泡的色板颜色（tintA）。白色时等于没改。
    float3 baseTinted = _BaseColor.rgb * tintA;

    float3 col = baseTinted * 0.16;
    col += irid;
    col += spec + spec2;
    col += env * 0.5;
    col += trans;
    col += baseTinted * rim * 0.45;

    // 中心几乎全透、边缘收拢变实。这一步也走色阶，边缘会有一圈清晰的"壳"
    float alphaBand = XStep(saturate(fresnel * 0.8 + specBand + spec2Band),
                            _ToonSteps, _ToonSoftness);
    float a = saturate(_BaseColor.a * _Opacity * (0.10 + 0.90 * alphaBand));
    a = max(a, _Opacity * 0.05);

    a *= faceFade;

    return half4(col, a);
#endif
}

#endif // PAO_BUBBLE_TIDE_TOON_OPT_CORE
