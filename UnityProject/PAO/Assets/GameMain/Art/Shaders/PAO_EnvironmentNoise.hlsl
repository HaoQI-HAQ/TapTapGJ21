#ifndef PAO_ENVIRONMENT_NOISE_INCLUDED
#define PAO_ENVIRONMENT_NOISE_INCLUDED

// ---------------------------------------------------------------------------
// PAO / 环境材质公用噪声
//
// 岩浆和深渊两个 shader 共用这一套，保证它们的"流动感"是同一种语言：
//   · 值噪声 + FBM —— 大块的流动、浓淡
//   · Voronoi F1/F2 —— 岩浆结壳的块和缝
//
// 全部是世界坐标驱动的（见 PAO_ENV_PlanarUV），
// 所以同一块材质铺在 5 米和 50 米的面上，纹理密度是一致的。
// ---------------------------------------------------------------------------

// 三个 hash 都返回 0..1，避免 sin 版本在不同平台上精度不一致
float PAO_ENV_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 PAO_ENV_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float PAO_ENV_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = PAO_ENV_Hash21(i);
    float b = PAO_ENV_Hash21(i + float2(1.0, 0.0));
    float c = PAO_ENV_Hash21(i + float2(0.0, 1.0));
    float d = PAO_ENV_Hash21(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 4 个八度，归一化到 0..1
float PAO_ENV_Fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.5;

    for (int i = 0; i < 4; i++)
    {
        value += amplitude * PAO_ENV_ValueNoise(p);
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }

    return value * 1.0666667;
}

// x = 到最近特征点的距离（F1），y = 到第二近特征点的距离（F2）
// F2 - F1 在"块内部"大、在"块与块的缝"上接近 0，正好拿来切结壳。
float2 PAO_ENV_Voronoi(float2 p)
{
    float2 cell = floor(p);
    float2 local = frac(p);

    float d1 = 8.0;
    float d2 = 8.0;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y);
            float2 feature = PAO_ENV_Hash22(cell + offset);
            float2 diff = offset + feature - local;
            float distanceSq = dot(diff, diff);

            if (distanceSq < d1)
            {
                d2 = d1;
                d1 = distanceSq;
            }
            else if (distanceSq < d2)
            {
                d2 = distanceSq;
            }
        }
    }

    return sqrt(float2(d1, d2));
}

// 世界坐标三平面投影：平面用 XZ，竖面用 ZY / XY。
// 不做法线混合，取主轴就够了 —— 省一次三倍采样，边缘也不会出现糊。
float2 PAO_ENV_PlanarUV(float3 positionWS, float3 normalWS)
{
    float3 n = abs(normalWS);

    if (n.y >= n.x && n.y >= n.z)
    {
        return positionWS.xz;
    }

    if (n.x >= n.z)
    {
        return positionWS.zy;
    }

    return positionWS.xy;
}

#endif
