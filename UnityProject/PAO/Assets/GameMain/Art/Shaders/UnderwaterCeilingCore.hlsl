// ---------------------------------------------------------------------------
// PAO / UnderwaterCeiling —— 复刻 CartoonWater 视觉语言的水下天顶
//
// 【为什么是复刻，不是直接用那个 shader】
// 把 Assets/CartoonWater 的图拆开之后，Color 口的真实公式是：
//
//   A = SceneColor + RippleColor * (pow(Voronoi(带径向扭曲的 UV), 5.1) * 1.26)
//   B = lerp(水色, FoamColor, foamMask)
//   Color = lerp(A, B, B.x)      // B.x 恰好等于 foamMask
//
// 也就是：
//   Color = lerp(SceneColor + 白色波纹, 白色泡沫, foamMask)
//
// 它的 Shallow / Deep 两个颜色 R 通道都是 0，又被接到了 Lerp 的 T 上，
// 所以那个"水色"从头到尾没有真正上过色 —— 这层水的本体几乎是透明的，
// 好看的部分全部来自：对水底的折射 + 白色波纹 + 泡沫。
//
// 玩家在水下抬头时"水底"不存在，SceneColor 这条腿必然断掉。
// 所以这里保留它真正的视觉语言（同一套 Voronoi 波纹 + 同一套 step 泡沫），
// 只把 SceneColor 换成"抬头看到的水体"：深水 -> 品牌蓝 -> 天顶透光。
//
// 【和原版的两处必要偏差】
// 1. UV 换成世界坐标：原版用网格 UV（只在 demo 那块小平面上成立），
//    这里的水面是几百米见方的一整块，必须用世界 XZ 才能保证波纹密度一致。
// 2. 波纹显式滚动：原版的 AngleOffset 只影响 Cells 输出，不会让距离场动起来，
//    单靠它波纹是静止的。
// ---------------------------------------------------------------------------
#ifndef PAO_UNDERWATER_CEILING_CORE_INCLUDED
#define PAO_UNDERWATER_CEILING_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// SRP Batcher 要求材质属性全部集中在这一个 CBUFFER 里，少一个就 "not compatible"。
CBUFFER_START(UnityPerMaterial)
    // ---- 颜色（取自 CartoonWater.mat 的品牌色）----
    half4 _DeepColor;      // 远处 / 掠射：深水
    half4 _ShallowColor;   // 中段：CartoonWater 那个饱和蓝 (0, 0.39, 0.99)
    half4 _LightColor;     // 天顶透光
    half4 _RippleColor;    // 波纹（原版是白色）
    half4 _FoamColor;      // 泡沫（原版是白色）

    // ---- 波纹（复刻 CartoonWater）----
    half  _RippleTiling;     // 世界坐标 -> UV 的缩放
    half  _RippleDensity;    // Voronoi 细胞密度（对应原版 RippleDensity）
    half  _RippleSpeed;      // 滚动速度
    half  _RippleSlimness;   // 对应原版 RippleSlimness = 5.1
    half  _RippleAlpha;      // 对应原版 RippleAlpha = 1.26
    half  _ShearStrength;    // 径向扭曲（对应原版 RadialShear Strength = 1）
    half  _RippleFadeStart;  // 多远开始淡出波纹
    half  _RippleFadeEnd;    // 多远波纹完全消失

    // ---- 泡沫 ----
    half  _FoamScale;
    half  _FoamSpeed;
    half  _FoamCutoff;       // 对应原版 Foam Cutoff = 1.03
    half  _FoamStrength;

    // ---- 视角 ----
    half  _WindowPower;      // 天顶收敛的快慢

    // ---- 远处收纳 ----
    half  _FadeStart;
    half  _FadeEnd;

    // ---- 杂项 ----
    half  _Dither;
CBUFFER_END

// ---------------------------------------------------------------- 噪声

float2 PAO_Hash22(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)),
               dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

float PAO_Hash21(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// 2D 梯度噪声，输出大致 0..1，形状贴近 ShaderGraph 的 Gradient Noise。
float PAO_GradientNoise(float2 p)
{
    const float2 i = floor(p);
    const float2 f = frac(p);
    const float2 u = f * f * (3.0 - 2.0 * f);

    const float a = dot(PAO_Hash22(i + float2(0.0, 0.0)) * 2.0 - 1.0, f - float2(0.0, 0.0));
    const float b = dot(PAO_Hash22(i + float2(1.0, 0.0)) * 2.0 - 1.0, f - float2(1.0, 0.0));
    const float c = dot(PAO_Hash22(i + float2(0.0, 1.0)) * 2.0 - 1.0, f - float2(0.0, 1.0));
    const float d = dot(PAO_Hash22(i + float2(1.0, 1.0)) * 2.0 - 1.0, f - float2(1.0, 1.0));

    return saturate(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 1.4 + 0.5);
}

// Voronoi 的"到最近特征点距离"，和 ShaderGraph 的 Voronoi.Out 是同一个东西：
// 细胞中心接近 0，细胞角落接近 1。3x3 邻域够用。
float PAO_VoronoiDistance(float2 uv, float cellDensity)
{
    const float2 g = floor(uv * cellDensity);
    const float2 f = frac(uv * cellDensity);

    float t = 8.0;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            const float2 lattice = float2((float)x, (float)y);
            const float2 r = lattice + PAO_Hash22(g + lattice) - f;
            t = min(t, length(r));
        }
    }

    return t;
}

// ---------------------------------------------------------------- 复刻 CartoonWater 的波纹
// 原版：pow(Voronoi(TilingOffset(UV) + RadialShear(UV, 0.5, 1, 0)), 5.1) * 1.26
// 这里 UV 换成世界 XZ，径向扭曲换成绕世界原点的同一套式子。
float PAO_RippleLayer(float2 worldXZ, float time, float scale, float2 scroll, float shear)
{
    float2 uv = worldXZ * (_RippleTiling * scale);

    // RadialShear：位移 = r² * (-dy, dx)，把整片波纹拧成漩涡
    uv += float2(-uv.y, uv.x) * (dot(uv, uv) * shear);

    // 原版靠 AngleOffset 动不起来（只影响 Cells 输出），这里显式滚动
    uv += scroll * (time * _RippleSpeed);

    const float distanceToCell = PAO_VoronoiDistance(uv, max(_RippleDensity * scale, 1e-3));

    // pow(距离, 5.1)：细胞角落亮、中心暗 —— 原版那种一片片浮着的光
    return pow(saturate(distanceToCell), max(_RippleSlimness, 1e-3));
}

float PAO_Ripple(float2 worldXZ, float time)
{
    // 单层 Voronoi 在几百米的水面上会露出生硬的规则网格，
    // 叠一层错开密度和滚动方向的，把它打散。
    const float a = PAO_RippleLayer(worldXZ, time, 1.0,  float2(0.055, 0.031), _ShearStrength);
    const float b = PAO_RippleLayer(worldXZ, time, 1.83, float2(-0.041, 0.047), 0.0);

    return (a * 0.85 + b * 0.45) * _RippleAlpha;
}

// ---------------------------------------------------------------- 复刻 CartoonWater 的泡沫
// 同一张噪声同时干两件事：定泡沫的位置，以及压暗一部分波纹，
// 免得整片水面的闪点均匀得像张网。
float PAO_SurfaceNoise(float2 worldXZ, float time)
{
    const float2 uv = worldXZ * _FoamScale + time * _FoamSpeed * 0.06;
    return PAO_GradientNoise(uv);
}

// ---------------------------------------------------------------- 主体
//   positionWS : 像素世界坐标
//   normalWS   : 世界法线（本网格是内表面，顶面法线朝下）
//   shellT     : 0 = 水面，0..1 = 裙边往下的归一化深度
//
// 约定：物体原点就在水面上（顶面 local y = 0，不旋转、缩放 1），
// 所以水面高度直接从物体矩阵取，不需要额外的材质参数。
half3 PAO_ShadeUnderwaterCeiling(float3 positionWS, half3 normalWS, float shellT)
{
    const float  waterY = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).y;
    const float3 toCamera = _WorldSpaceCameraPos - positionWS;
    const float  distanceToCamera = length(toCamera);
    const float3 V = toCamera / max(distanceToCamera, 1e-4);

    // 相机越过水面（正常玩法到不了，纯保险，防止编辑器自由视角看到翻面）
    const float cameraAbove = step(waterY, _WorldSpaceCameraPos.y);

    // 向上看的分量：正上方 1，掠射 0
    const float viewUp = saturate(-V.y);
    const float t = pow(viewUp, max(_WindowPower, 0.01));

    // ---- 底座：深水 -> 品牌蓝 -> 天顶透光 ----
    half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(t * 1.35));
    color = lerp(color, _LightColor.rgb, smoothstep(0.42, 0.95, t));

    // 越远波纹越细，程序化图案到远处会闪
    const float rippleFade = 1.0 - saturate((distanceToCamera - _RippleFadeStart)
                             / max(1e-4, _RippleFadeEnd - _RippleFadeStart));

    const float time = _Time.y;

    // 一张噪声：既压暗部分波纹，也决定泡沫位置
    const float surfaceNoise = PAO_SurfaceNoise(positionWS.xz * 0.15, time);
    const float rippleMask = lerp(0.45, 1.0, surfaceNoise);

    // ---- 波纹：加法叠白，和原版 `SceneColor + ripple` 一个路数 ----
    const float ripple = PAO_Ripple(positionWS.xz, time) * rippleFade * rippleMask;
    color += _RippleColor.rgb * (ripple * (0.20 + 0.80 * t));

    // ---- 泡沫：细碎，只在天顶附近出现 ----
    const float foamNoise = PAO_SurfaceNoise(positionWS.xz, time);
    const float foam = step(_FoamCutoff, foamNoise)
                     * smoothstep(0.45, 0.95, t) * _FoamStrength;
    color = lerp(color, _FoamColor.rgb, foam);

    // ---- 裙边：顶端和水面同色，接缝看不出来；越往下越深 ----
    const float skirtT = saturate(shellT);
    half3 skirt = lerp(_DeepColor.rgb, _DeepColor.rgb * 0.35h, skirtT);
    skirt += _RippleColor.rgb * (ripple * 0.18 * (1.0 - skirtT));
    color = lerp(color, skirt, skirtT);

    // ---- 远处统一收进深水色：网格边界因此看不见 ----
    const float fade = saturate((distanceToCamera - _FadeStart)
                      / max(1e-4, _FadeEnd - _FadeStart));
    color = lerp(color, _DeepColor.rgb, fade);

    // 相机在上方时给"从上往下看"的兜底，避免翻面翻出怪东西
    const half3 fromAbove = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.85h);
    color = lerp(color, fromAbove, cameraAbove * 0.6h);

    // 8bit 输出下大块渐变的色带
    color += half3((PAO_Hash21(positionWS.xz) - 0.5) * _Dither,
                   (PAO_Hash21(positionWS.xz + 41.7) - 0.5) * _Dither,
                   (PAO_Hash21(positionWS.xz + 87.3) - 0.5) * _Dither);

    return color;
}

#endif // PAO_UNDERWATER_CEILING_CORE_INCLUDED
