#ifndef PAO_BUBBLE_SDF_INCLUDE
#define PAO_BUBBLE_SDF_INCLUDE

// ---------------------------------------------------------------------------
// PAO / 泡泡 SDF 图元库
//
// 这里只放「与渲染管线无关」的纯数学：有符号距离函数（SDF）与平滑布尔运算。
// 所有函数都以「距离」为单位工作，负数在形状内部、正数在外部。
// ---------------------------------------------------------------------------

// --- 图元 ------------------------------------------------------------------

// 3D 球：p 为相对球心的坐标，r 为半径
float sdSphere(float3 p, float r)
{
    return length(p) - r;
}

// 3D 轴对齐盒：b 为半尺寸
float sdBox(float3 p, float3 b)
{
    float3 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, max(d.y, d.z)), 0.0);
}

// --- 平滑布尔（smooth min / max）-------------------------------------------
//
// 这是「融球 / metaball」效果的核心：把两个形状的距离场用一段软过渡接起来，
// 于是两个泡泡靠近时会像肥皂泡一样先「粘」一下再分开，而不是硬碰硬。
//
// k 为过渡宽度：k -> 0 退化成硬边 min/max，k 越大融合越黏。

float smin(float a, float b, float k)
{
    k = max(k, 1e-5);
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float smax(float a, float b, float k)
{
    return -smin(-a, -b, k);
}

// 平滑差集：把 dSub 从 dBase 里「挖掉」，边缘带 k 的软过渡。
// 泡泡的中空球壳就是 outer 减去 inner。
float ssub(float dBase, float dSub, float k)
{
    return smax(dBase, -dSub, k);
}

// --- 颜色 ------------------------------------------------------------------

// 简单的余弦调色板（Inigo Quilez）。用于把「相位」映射成彩虹色。
float3 palette(float t, float3 a, float3 b, float3 c, float3 d)
{
    return a + b * cos(6.28318530718 * (c * t + d));
}

#endif // PAO_BUBBLE_SDF_INCLUDE
