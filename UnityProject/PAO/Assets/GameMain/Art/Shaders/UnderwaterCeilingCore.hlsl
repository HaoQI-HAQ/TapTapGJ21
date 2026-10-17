// ---------------------------------------------------------------------------
// PAO / UnderwaterCeiling 的公共声明与着色函数
//
// 这个 shader 只解决一件事：玩家永远待在水面之下时，"抬头看到的那层水"应该长什么样。
//
// 为什么不用 Assets/CartoonWater 里那套：
//   1. 那套是 Unlit + Transparent + 单面（m_TwoSided: false），相机一旦越过水面
//      整片水会被背面剔除掉，直接消失。
//   2. 它的核心是 DepthFade = saturate((sceneDepth - screenPos.w) / WaterDepth)，
//      这个式子的方向是"相机在水面之上，往下看时水面背后的物体有多近"。
//      从下往上看水面时，水面背后是天空、没有不透明的物体，sceneDepth 恒为远平面，
//      于是整条渐变色被钉死在一个极端值上 —— 翻过来只会得到一块抛光的塑料盖。
//   3. 它还要 Scene Color 做屏幕折射，而工程的 URP-HighFidelity.asset 里
//      m_RequireOpaqueTexture = 0，这个节点本来就没数据。
//
// 所以这里换成"只按几何和视线算"的一套：
//   - 双面（Cull Off），任何角度都不存在"整片消失"。
//   - 用"向上看的分量"决定亮暗：正上方透光最亮，掠射方向收敛成远处的水色。
//     这样网格的边缘会先溶进远处水色，看不出边。
//   - 不采样 Scene Color，不依赖 Opaque Texture，也不进透明队列
//     （Opaque 队列 + 写深度，永远不可能和满地图的泡泡粒子抢排序）。
// ---------------------------------------------------------------------------
#ifndef PAO_UNDERWATER_CEILING_CORE_INCLUDED
#define PAO_UNDERWATER_CEILING_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// SRP Batcher 要求材质上出现的每个属性都集中在这一个 CBUFFER 里。
// 少写一个（哪怕只是给混合状态用的）整个 shader 就会变成 "not compatible"。
CBUFFER_START(UnityPerMaterial)
    // ---- 颜色 ----
    half4 _HorizonColor;      // 远处 / 掠射方向的水色，也是网格边缘的收敛色
    half4 _SurfaceColor;      // 水面本体的颜色
    half4 _WindowColor;       // 正上方"天窗"的透光色
    half4 _RippleColor;       // 波纹亮线的颜色
    half4 _SkirtBottomColor;  // 裙边（水下侧壁）最底部的颜色

    // ---- 天窗（Snell's window 的卡通近似）----
    half  _WindowPower;       // 越大，天窗越小、越集中在正上方
    half  _WindowStrength;    // 天窗的混合强度

    // ---- 波纹 ----
    half  _RippleScale;       // 世界空间密度：1 / 细胞边长（米）
    half  _RippleSpeed;       // 流动速度
    half  _RippleStrength;    // 亮线强度
    half  _RippleWidth;       // 亮线宽度
    half  _RippleFadeStart;   // 多远开始淡出波纹
    half  _RippleFadeEnd;     // 多远波纹完全消失

    // ---- 大起伏 ----
    // 只有 Voronoi 的时候整片水面会均匀得像壁纸，加一层波长上百米的大起伏，
    // 让"这一块亮、那一块暗"，才像真的水面。
    half  _SwellScale;        // 大起伏的空间频率（越小波长越长）
    half  _SwellStrength;     // 大起伏压暗波纹的幅度

    // ---- 远处淡出 ----
    half  _FadeStart;         // 多远开始收敛到 _HorizonColor
    half  _FadeEnd;           // 多远完全等于 _HorizonColor

    // ---- 杂项 ----
    half  _Dither;            // 大块渐变在 8bit 输出下的色带打散
CBUFFER_END

// 两个整数格点之间的伪随机。够用、够便宜，不需要额外的噪声图。
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

// 2D Voronoi，3x3 邻域。
//   .x = 到最近点的距离 F1
//   .y = F2 - F1，也就是到细胞边界的距离
// 水面要的是细胞边界上的亮线，所以后面只用 .y。
float2 PAO_Voronoi(float2 p)
{
    const float2 cell  = floor(p);
    const float2 local = p - cell;

    float f1 = 8.0;
    float f2 = 8.0;

    [unroll]
    for (int y = -1; y <= 1; ++y)
    {
        [unroll]
        for (int x = -1; x <= 1; ++x)
        {
            const float2 offset  = float2((float)x, (float)y);
            const float2 toPoint = offset + PAO_Hash22(cell + offset) - local;
            const float  d       = dot(toPoint, toPoint);

            // 先比 F1 再比 F2，两个 if 而不是 min()，这样才能同时拿到次近点
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) { f2 = d; }
        }
    }

    const float r1 = sqrt(f1);
    const float r2 = sqrt(f2);
    return float2(r1, r2 - r1);
}

// 水面波纹：两层速度/密度都不同的细胞噪声叠出交错的亮线。
float PAO_WaterPattern(float2 worldXZ, float time)
{
    const float2 p1 = worldXZ * _RippleScale
                    + float2(time * 0.060, time * 0.035);
    const float2 p2 = worldXZ * (_RippleScale * 1.41)
                    + float2(time * -0.045, time * 0.055);

    // 细胞边界 -> 亮线。F2-F1 越小说明越贴近边界。
    const float edge1 = PAO_Voronoi(p1).y;
    const float edge2 = PAO_Voronoi(p2).y;

    const float line1 = 1.0 - smoothstep(0.0, max(_RippleWidth, 1e-3), edge1);
    const float line2 = 1.0 - smoothstep(0.0, max(_RippleWidth * 1.35, 1e-3), edge2);

    float pattern = line1 * 0.78 + line2 * 0.30;

    // 上百米波长的大起伏：两块相邻的水面不会一样亮，不至于整片像壁纸。
    const float2 swellUV = worldXZ * max(_SwellScale, 1e-4)
                         + float2(time * 0.012, time * -0.009);
    const float swell = 0.5 + 0.5 * sin(swellUV.x) * sin(swellUV.y);
    pattern *= lerp(1.0 - saturate(_SwellStrength), 1.0, swell);

    return saturate(pattern);
}

// 水面着色。
//   positionWS : 像素世界坐标
//   normalWS   : 世界法线（本网格是"内表面"，顶面法线朝下）
//   shellT     : 网格自带的参数，0 = 水面，0..1 = 裙边往下的归一化深度
//
// 约定：物体原点就在水面上（顶面 local y = 0，缩放 1、不旋转），
// 所以水面高度可以直接从 unity_ObjectToWorld 的平移分量取，不需要额外的材质参数。
half3 PAO_ShadeUnderwaterCeiling(float3 positionWS, half3 normalWS, float shellT)
{
    // 水面高度 = 物体原点的世界高度。用矩阵乘单位点来取，避免 _m13 这类
    // 行列下标在 HLSL 里容易被写反（列主序 vs 行主序）的坑。
    const float  waterY = mul(unity_ObjectToWorld, float4(0.0, 0.0, 0.0, 1.0)).y;
    const float3 toCamera = _WorldSpaceCameraPos - positionWS;
    const float  distanceToCamera = length(toCamera);
    const float3 V = toCamera / max(distanceToCamera, 1e-4);

    // 相机有没有越过水面。正常玩法里恒为 0（玩家不出水），这里只是保险：
    // 万一是编辑器自由视角或者演出镜头飞到上面去了，也不会突然变成一片死色。
    const float cameraAbove = step(waterY, _WorldSpaceCameraPos.y);

    // 向上看的分量：玩家在水下、水面在头顶时，正上方为 1，掠射方向趋近 0。
    const float viewUp = saturate(-V.y);

    // 透光量：越接近正上方越亮，就是水下抬头看到的"天窗"。
    const float window = pow(viewUp, max(_WindowPower, 0.01));

    // 远处的波纹要淡掉：程序化噪声在远处会退化成一堆高频闪烁，且没有 mip 可以救。
    const float rippleFade = 1.0 - saturate((distanceToCamera - _RippleFadeStart)
                             / max(1e-4, _RippleFadeEnd - _RippleFadeStart));

    const float pattern = PAO_WaterPattern(positionWS.xz, _Time.y * _RippleSpeed) * rippleFade;

    // ---- 顶面：从下往上看 ----
    // 从远处的水色渐变到头顶的水面色，再叠波纹亮线，最后在正上方开出天窗。
    half3 surface = lerp(_HorizonColor.rgb, _SurfaceColor.rgb, window);
    surface += _RippleColor.rgb * (pattern * _RippleStrength * (0.15 + 0.85 * window));
    surface = lerp(surface, _WindowColor.rgb, smoothstep(0.72, 1.0, window) * _WindowStrength);

    // ---- 顶面：从上往下看 ----
    // 本游戏到不了水面之上，这一支只是防止编辑器自由视角看到"翻面"的怪东西。
    const half3 fromAbove = lerp(_SurfaceColor.rgb, _WindowColor.rgb, pattern * 0.6);
    surface = lerp(surface, fromAbove, cameraAbove);

    // ---- 裙边：水面边缘往下垂的一圈侧壁 ----
    // 顶端收敛到 _HorizonColor（和水面边缘完全同色，接缝看不出来），
    // 越往下越深，于是从任何角度看都不会露出网格的边。
    const float skirtT = saturate(shellT);
    half3 skirt = lerp(_HorizonColor.rgb, _SkirtBottomColor.rgb, skirtT);
    skirt += _RippleColor.rgb * (pattern * _RippleStrength * 0.2 * (1.0 - skirtT));

    half3 color = lerp(surface, skirt, skirtT);

    // ---- 远处统一收敛 ----
    // 这是"看不出网格边界"的关键：只要够远，颜色就等于 _HorizonColor，
    // 而裙边顶端也是 _HorizonColor，两者自然接上。
    const float fade = saturate((distanceToCamera - _FadeStart)
                      / max(1e-4, _FadeEnd - _FadeStart));
    color = lerp(color, _HorizonColor.rgb, fade);

    // 8bit 输出下这么大的平滑渐变会有色带，用世界空间抖色打散。
    // 用世界空间而不是屏幕空间，是为了相机移动时抖色不会在屏幕上"爬"。
    color += half3((PAO_Hash21(positionWS.xz) - 0.5) * _Dither,
                   (PAO_Hash21(positionWS.xz + 41.7) - 0.5) * _Dither,
                   (PAO_Hash21(positionWS.xz + 87.3) - 0.5) * _Dither);

    return color;
}

#endif // PAO_UNDERWATER_CEILING_CORE_INCLUDED
