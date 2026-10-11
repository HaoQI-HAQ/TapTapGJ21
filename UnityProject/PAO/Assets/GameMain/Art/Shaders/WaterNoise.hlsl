#ifndef PAO_WATER_NOISE_INCLUDE
#define PAO_WATER_NOISE_INCLUDE

// ---------------------------------------------------------------------------
// PAO / 水下效果 · 程序化噪声库
//
// 【为什么全是程序化的，一张贴图都不用】
//   水面波光、焦散、光柱这三样都是**大面积铺开**的效果。
//   用贴图的话有两个甩不掉的毛病：
//     · 平铺重复：玩家一抬头就是整片水面，256 米的 UV 一平铺，
//       重复的花纹肉眼可辨，而水下恰恰是个「一直在看远处」的场景
//     · 分辨率错配：焦散要铺满地板，按 1 米一格算就是 60×60 米 = 3600 格，
//       想不糊就得 4096² 贴图 —— 16 MB 显存换一个能算出来的东西
//
//   程序化版本：0 字节显存，0 次采样，代价是每个像素几十条 ALU。
//   在 PC 上这个交换是稳赚的；而且它**天生无缝**，没有平铺问题。
//
// 【将来美术出了图怎么办】
//   不是替换，是**叠加**：见 UnderwaterInclude.hlsl 里的
//   PAO_SampleCaustics —— 有贴图时走贴图分支，没贴图时走程序化分支。
//   贴图提供「具体长什么样」，程序化提供「一直在动」。
//
// 【这里只放纯数学】
//   和 BubbleSDFInclude.hlsl 一样的定位：不含任何 URP 头文件依赖，
//   任何 shader、任何 Pass 都能直接 include。
// ---------------------------------------------------------------------------

// --- 哈希 ------------------------------------------------------------------
//
// 【为什么不用 sin(dot(...)) 那种一行哈希】
//   那套写法在移动端 GPU 上会因为 sin 的精度差异出现明显的条带，
//   而且在某些角度会退化成常数。整数位运算哈希没有这个问题，也更快。

uint PAO_Hash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float PAO_HashToFloat(uint x)
{
    return float(PAO_Hash(x)) * (1.0 / 4294967296.0);
}

// 2D 整数格点哈希
float PAO_Hash21(int2 p)
{
    uint h = (uint)(p.x * 73856093) ^ (uint)(p.y * 19349663);
    return PAO_HashToFloat(h);
}

// 3D 整数格点哈希
float PAO_Hash31(int3 p)
{
    uint h = (uint)(p.x * 73856093) ^ (uint)(p.y * 19349663) ^ (uint)(p.z * 83492791);
    return PAO_HashToFloat(h);
}

// --- 插值 ------------------------------------------------------------------

/// 五次平滑插值。比 smoothstep 多两次乘法，但一阶、二阶导数都连续 ——
/// 焦散这种「亮纹扫过」的效果如果用 smoothstep，格点边界会出现可见的方向突变。
float PAO_Quintic(float t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// --- 2D 值噪声 -------------------------------------------------------------

float PAO_ValueNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float2 u = float2(PAO_Quintic(f.x), PAO_Quintic(f.y));

    int2 gi = int2(i);

    float a = PAO_Hash21(gi + int2(0, 0));
    float b = PAO_Hash21(gi + int2(1, 0));
    float c = PAO_Hash21(gi + int2(0, 1));
    float d = PAO_Hash21(gi + int2(1, 1));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

/// 2D 分形噪声。octaves 建议 2~4 —— 
/// 每多一层就多一倍采样成本，而水下效果里超过 4 层肉眼已经分不出来了。
float PAO_Fbm2(float2 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    [loop] for (int i = 0; i < octaves; ++i)
    {
        sum  += PAO_ValueNoise2(p) * amp;
        norm += amp;

        // 旋转 37° 再放大，避免各层的格点对齐形成方格状伪影
        p = float2(p.x * 0.8 + p.y * 0.6, -p.x * 0.6 + p.y * 0.8) * 2.03;
        amp *= 0.5;
    }

    return sum / max(norm, 1e-5);
}

// --- 3D 值噪声 -------------------------------------------------------------
//
// 8 次哈希。光柱那边每步采一次，14 步就是 112 次哈希 —— 
// 在半分辨率下这个开销是可以接受的；真要再省就降 octaves，别降步数
//（降步数会出层状 banding，比噪声难看）。

float PAO_ValueNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);

    float3 u = float3(PAO_Quintic(f.x), PAO_Quintic(f.y), PAO_Quintic(f.z));

    int3 gi = int3(i);

    float n000 = PAO_Hash31(gi + int3(0, 0, 0));
    float n100 = PAO_Hash31(gi + int3(1, 0, 0));
    float n010 = PAO_Hash31(gi + int3(0, 1, 0));
    float n110 = PAO_Hash31(gi + int3(1, 1, 0));
    float n001 = PAO_Hash31(gi + int3(0, 0, 1));
    float n101 = PAO_Hash31(gi + int3(1, 0, 1));
    float n011 = PAO_Hash31(gi + int3(0, 1, 1));
    float n111 = PAO_Hash31(gi + int3(1, 1, 1));

    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);

    float y0 = lerp(x00, x10, u.y);
    float y1 = lerp(x01, x11, u.y);

    return lerp(y0, y1, u.z);
}

float PAO_Fbm3(float3 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    [loop] for (int i = 0; i < octaves; ++i)
    {
        sum  += PAO_ValueNoise3(p) * amp;
        norm += amp;

        p = p * 2.07 + float3(11.3, 7.1, 5.7);
        amp *= 0.5;
    }

    return sum / max(norm, 1e-5);
}

// --- 细胞噪声（Voronoi 的 F1 / F2）-----------------------------------------
//
// 焦散的原型就是「一池水把光聚成一张细胞状的亮网」。
// 用 F2 - F1 来表达：细胞边界处 F2 ≈ F1，差值趋近 0 → 亮；
// 细胞中心处 F2 远大于 F1 → 暗。正好是那张网。
//
// 返回 (F1, F2)。3×3 邻域 = 9 次哈希。

float2 PAO_Cellular2(float2 p)
{
    float2 ip = floor(p);
    float2 fp = frac(p);

    float f1 = 8.0;
    float f2 = 8.0;

    int2 base = int2(ip);

    [unroll] for (int y = -1; y <= 1; ++y)
    {
        [unroll] for (int x = -1; x <= 1; ++x)
        {
            int2 cell = base + int2(x, y);

            // 抖动格点，免得细胞排成规整的正方形网格
            float2 jitter = float2(PAO_Hash21(cell * 2 + int2(17, 0)),
                                   PAO_Hash21(cell * 2 + int2(0, 23)));

            float2 offset = float2(x, y) + jitter - fp;
            float d = dot(offset, offset);   // 用平方距离，省一次 sqrt

            if (d < f1)
            {
                f2 = f1;
                f1 = d;
            }
            else if (d < f2)
            {
                f2 = d;
            }
        }
    }

    return float2(sqrt(f1), sqrt(f2));
}

/// 焦散形态：0 = 暗，1 = 最亮。
///
/// width 控制亮纹的粗细。0.35 左右是「一张网」；
/// 调到 0.8 会变成大块光斑，调到 0.1 会变成细密的白线。
float PAO_CausticPattern(float2 p, float width)
{
    float2 f = PAO_Cellular2(p);

    // 细胞边界：F2 - F1 越小越靠近边界
    float edge = f.y - f.x;

    // 翻转并锐化 —— 边界亮、中心暗
    float pattern = 1.0 - saturate(edge / max(width, 1e-3));

    // 三次方：把亮纹收窄，留出大片暗区。
    // 这一步很关键 —— 不做的话整块地板会均匀发亮，看着像「地板自己会发光」，
    // 而不是「光被水聚成了一张网」。
    return pattern * pattern * pattern;
}

#endif // PAO_WATER_NOISE_INCLUDE
