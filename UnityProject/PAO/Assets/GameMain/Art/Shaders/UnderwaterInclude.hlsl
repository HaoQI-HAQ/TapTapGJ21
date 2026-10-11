#ifndef PAO_UNDERWATER_INCLUDE
#define PAO_UNDERWATER_INCLUDE

// ---------------------------------------------------------------------------
// PAO / 水下效果共享库
//
// 【谁在用】
//   · UnderwaterFullscreen.shader  —— 焦散投影、深度雾、体积光
//   · WaterSurface.shader          —— 抬头看见的水面波光
//   两边共用同一套水面高度、同一套噪声，保证「水面上的波光」和
//   「地面上的焦散」是**同一池水**产生的，而不是两个各画各的效果。
//   —— 这跟 BubbleTide 和 BubbleSDF 共用 BubbleSDFInclude.hlsl 是一个道理。
//
// 【C# 侧的名字契约】
//   所有 _PAO_ 变量的名字在 Scripts/Gameplay/Underwater/UnderwaterShaderIDs.cs
//   里统一定义。**改名字只改那一处**，这里只是对应的声明。
// ---------------------------------------------------------------------------

#include "WaterNoise.hlsl"

// --- 相机（由 C# 每帧送）---------------------------------------------------

// 与「相机颜色目标」严格一致的逆 View-Projection。
// 注意不是 UNITY_MATRIX_I_VP —— URP 的颜色目标可能被平台翻转，
// 必须用 C# 里按同一套投影矩阵算出来的那一份。见 UnderwaterPass.SetCameraMatrices。
float4x4 _PAO_CameraInvVP;

// xy = 相机颜色目标尺寸（像素），zw = 1 / 尺寸
float4 _PAO_CameraBufferSize;

// --- 水面 ------------------------------------------------------------------

// xyzw = (水面高度 Y, 0, 0, 0)
float4 _PAO_WaterPlane;

// rgb = 浅处水色 / 深处水色
float4 _PAO_ShallowColor;
float4 _PAO_DeepColor;

// x = 水面之上仍保留的过渡高度, y = 水面之下的淡入深度
float4 _PAO_FadeRange;

float _PAO_FogDensity;
float _PAO_DepthDarkening;
float _PAO_UnderwaterStrength;

// --- 焦散 ------------------------------------------------------------------

float _PAO_CausticsEnabled;
float _PAO_CausticsIntensity;
float _PAO_CausticsScale;
float _PAO_CausticsSpeed;
float4 _PAO_CausticsColor;
float _PAO_CausticsWallBleed;
float _PAO_CausticsFadeDistance;

// 调试可视化模式。0 = 正常。见 UnderwaterRenderFeature 上的同名参数。
//
// 【为什么值得为它写一段代码】
//   「焦散看不见」有五种完全不同的原因，而它们在画面上表现完全一样（都是没有）：
//     1. 焦散图案本身没算出来
//     2. 法线判断把它滤掉了（法线反了 / 面朝下）
//     3. 距离衰减把它灭掉了（衰减距离设得比房间还小）
//     4. 世界坐标重建错了（深度 uv 对不上，图案被拉伸到看不出是图案）
//     5. 强度参数太小
//   靠改参数一个个试要来回很多次，每次还得重新进 Play。
//   把中间量直接画出来，一眼就能定位是哪一个。
float _PAO_DebugMode;

// 焦散图案贴图（**可选**）。
//
// 【没放图也能跑】—— 这是刻意的设计，不是偷懒：
//   空纹理在 HLSL 里采样返回黑色（Unity 会给每个未绑定的纹理挂一张默认图），
//   于是下面的分支会自然退回到程序化版本。所以美术还没出图的时候
//   整套效果照常工作，不需要改任何代码或参数。
//
// 放了图之后 = 两层不同速度、不同方向的滚动采样 + 一层低频扭曲。
TEXTURE2D(_PAO_CausticsTex);
SAMPLER(sampler_PAO_CausticsTex);

// xy = 贴图尺寸（像素），zw = 该图在世界里铺多少米（由 C# 从格距算出来）
float4 _PAO_CausticsTex_ST;

// --- 体积光 ----------------------------------------------------------------

float _PAO_GodRayEnabled;
float4 _PAO_SunDirection;       // xyz = 指向太阳
float _PAO_GodRayIntensity;
float _PAO_GodRaySpread;
float _PAO_GodRayDistance;
float _PAO_GodRaySteps;
float _PAO_GodRayNoiseScale;
float _PAO_GodRaySpeed;

// --- 水面波光 --------------------------------------------------------------

// 【注意这里没有「水面波光的全局参数」】
//
// 水面是唯一一个走材质、而不是走全屏 Pass 的东西，它既需要全局数据
// （水面高度、水色、时间），又需要自己那份观感参数。
// 为了不让「同一个参数有两个来源」这种情况出现，
// 这里的做法是：
//   · 水面高度、水色、焦散 —— 走全局变量（全屏 Pass 也要用，必须共享）
//   · 波光频率 / 亮度 / 亮边 —— 走**材质属性**，全局变量不参与
//
// 于是「改哪个地方生效」永远是确定的，不用猜。
// 编辑器工具在水面材质上烤了一份初始值，运行时想调就直接改材质。

// ===========================================================================
// 水面
// ===========================================================================

/// 水面高度（世界 Y，米）。
float PAO_WaterLevel()
{
    return _PAO_WaterPlane.x;
}

/// 世界坐标到水面的高度差。正值 = 在水面之上。
float PAO_HeightAboveWater(float3 worldPos)
{
    return worldPos.y - _PAO_WaterPlane.x;
}

/// 全局动画时钟。用同一个时间源，水面波光和光柱才是同步的。
float PAO_WaterTime()
{
    return _Time.y;
}

/// 水下强度 0~1：相机在水面之上 = 0，往下 fadeInDepth 米 = 1。
///
/// 【为什么要这段过渡】
/// 玩家从水里冒出来时如果效果是硬切，会看到整张画面「啪」地一下从青绿变正常，
/// 非常廉价。用 smoothstep 拉一段一两米的过渡带，冒头的过程就变成
/// 「水从镜头上退下去」，观感贵很多，成本是零。
float PAO_UnderwaterAmount(float3 cameraPos)
{
    float maxAbove = max(_PAO_FadeRange.x, 0.0);
    float fadeIn   = max(_PAO_FadeRange.y, 1e-3);

    float height = PAO_HeightAboveWater(cameraPos);

    // 从 (+maxAbove) 平滑降到 (-fadeIn)
    return 1.0 - smoothstep(-fadeIn, maxAbove, height);
}

// ===========================================================================
// 焦散
// ===========================================================================

/// 焦散强度。**有贴图走贴图，没贴图走程序化。**
///
/// 【为什么两条路都留着，而不是二选一】
///   程序化版本的价值不在「好看」，在于**它天生无缝、永远能跑**：
///   美术还没出图、图丢了、图还在改 —— 效果都不会变成一片黑。
///   贴图版本的价值在「具体长什么样」：细胞的粗细疏密是美术的判断，
///   程序化写不出来。
///
///   所以不是替换，是**回退**。C# 只在真的指定了贴图时才把它推上来；
///   没指定时这里查到的是 Unity 的默认黑纹理，自然走程序化分支。
float PAO_SampleCaustics(float3 worldPos, float time)
{
    float s = max(_PAO_CausticsScale, 0.01);
    float2 p = worldPos.xz / s;

    float t = time * _PAO_CausticsSpeed;

    float hasTexture = max(_PAO_CausticsTex_ST.x, 0.0);

    if (hasTexture > 0.5)
    {
        // ---- 有贴图 ----
        //
        // 注意这里【没有】用 TRANSFORM_TEX。那张图是按世界坐标平铺的，
        // 没有材质级的 tiling/offset —— 平铺密度直接由 causticsScale
        // （一格多少米）决定，改那一个参数就够了，不会有第二个地方跟着漂。

        // 低频扭曲：让图案自己变形，而不只是整体平移
        float warp = PAO_Fbm2(p * 0.18 + float2(t * 0.11, -t * 0.08), 2) - 0.5;

        // 两层不同方向、不同速度
        float2 uv1 = p + float2( t * 0.29,  t * 0.21) + warp * 0.9;
        float2 uv2 = p + float2(-t * 0.17,  t * 0.26) + warp * 1.3;

        float c1 = SAMPLE_TEXTURE2D(_PAO_CausticsTex, sampler_PAO_CausticsTex, uv1).r;
        float c2 = SAMPLE_TEXTURE2D(_PAO_CausticsTex, sampler_PAO_CausticsTex, uv2).r;

        // 取最大值而不是相加 —— 相加会让交叉处过曝成白斑
        float caustic = max(c1, c2 * 0.85);

        // 【贴图要「呼吸」】纯滚动看久了还是能看出是一张图在动。
        // 用第三层高频采样做一次对比度调制，亮纹会自己明灭。
        float breathe = SAMPLE_TEXTURE2D(_PAO_CausticsTex, sampler_PAO_CausticsTex,
                                         p * 0.37 + float2(-t * 0.13, t * 0.11)).r;

        caustic *= lerp(0.72, 1.28, breathe);

        return saturate(caustic);
    }

    // ---- 没贴图：程序化 ----
    //
    // 【为什么要「两层不同速度 + 一层扭曲」】
    //   单层滚动一眼就能看出是「一张图在平移」，因为所有亮纹作为一个整体在动。
    //   真实水面的每一条亮纹都是独立游动的，用两层不同方向和速度叠起来，
    //   再加上一层低频噪声去扭 UV，图案就会「自己变形」而不只是「平移」。
    //   这三层加起来比单层贵不到一倍，但可信度差一个数量级。
    float warpP = PAO_Fbm2(p * 0.18 + float2(t * 0.11, -t * 0.08), 2) - 0.5;

    float2 uvA = p + float2( t * 0.29,  t * 0.21) + warpP * 0.9;
    float2 uvB = p + float2(-t * 0.17,  t * 0.26) + warpP * 1.3;
    float2 uvC = p * 2.1 + float2(t * 0.41, -t * 0.33) + warpP * 1.8;

    float ca = PAO_CausticPattern(uvA, 0.34);
    float cb = PAO_CausticPattern(uvB, 0.46);
    float cc = PAO_CausticPattern(uvC, 0.55) * 0.45;

    return max(max(ca, cb * 0.85), cc);
}

/// 焦散在某点、某法线上的可见度。
///
/// 【为什么需要法线】
///   焦散是「从上面往下打的光」，物理上只出现在**朝上的面**。
///   如果无脑全屏乘一遍，墙壁和天花板也会长出光斑 —— 
///   玩家一眼就看出这是屏空间滤镜，而不是光。
///
///   wallBleed 是给「不物理但好看」留的口子：
///   真实水下录像里墙面确实也会有一点微弱的流动光，
///   全砍掉反而显得干。0 = 严格只打地板。
float PAO_CausticsVisibility(float3 worldNormal, float viewDistance)
{
    float upness = saturate(worldNormal.y);

    float bleed = saturate(_PAO_CausticsWallBleed);

    // 地板（n=(0,1,0)）→ 1；墙（n=(1,0,0)）→ bleed；
    // 朝下的面（n=(0,-1,0)）→ 0
    float facing = lerp(upness * upness, 1.0, bleed);

    // 远处衰减：防止远处的亮纹密到糊成噪点
    float far = 1.0 - saturate(viewDistance / max(_PAO_CausticsFadeDistance, 1.0));

    return facing * far * far;
}

// ===========================================================================
// 体积光
// ===========================================================================

/// 光柱的「光从天上来」遮罩。
///
/// 【为什么不用焦散图案当光源】
///   试过。焦散是一张**细密**的网，拿它当光源会得到一片均匀的亮雾 —— 
///   因为每条光线在几十米里总会穿过好几条亮纹，平均下来就平了。
///   光柱需要的是**大块**的明暗：几米宽的亮区和几米宽的暗区。
///   所以这里单独用一层低频、被竖直拉伸的噪声 —— 拉伸是为了让亮区
///   变成「一道一道」而不是「一团一团」。
float PAO_ShaftPattern(float3 worldPos, float time)
{
    float ns = max(_PAO_GodRayNoiseScale, 0.01);
    float t  = time * _PAO_GodRaySpeed;

    float3 p = worldPos / ns;

    // 竖直方向压扁 3.2 倍 = 水平方向拉伸 → 光柱是竖着的
    float3 q = float3(p.x, p.y * 3.2, p.z) + float3(t * 0.21, -t * 0.13, t * 0.17);

    float n = PAO_Fbm3(q, 2);

    // 【为什么要减 bias 再除】
    // FBM 的值永远在 0~1，直接拿来当亮度会得到「整片都亮」。
    // 减掉 0.34 再放大，等于把噪声重新分布成「大部分区域是暗的，
    // 少数区域是亮的」—— 这才是光柱该有的样子。
    return saturate((n - 0.34) * 2.1);
}

/// 沿视线步进，累加散射。返回 HDR 亮度（未乘颜色和强度）。
///
/// 【参数为什么带 maxDistance】
///   这是这个函数能做对的关键：先用场景深度算出「这条光线最多走多远」，
///   再把它当步进的上界传进来。这样：
///     · 光线撞到墙就自然停在墙前，不会在墙后继续加散射
///     · 不需要在 shader 里对深度图做 12 次比较（那才是真正的浪费）
///   深度已经在调用处采过一次了，白用白不用。
float PAO_GodRays(float3 rayOrigin,
                  float3 rayDir,
                  float maxDistance,
                  float3 cameraPos,
                  float time)
{
    if (_PAO_GodRayEnabled < 0.5)
    {
        return 0.0;
    }

    // 水面在相机上方多远。相机在水面上的话，视野里根本没有水面，
    // 也就不存在「从水面漏下来的光」。
    float above = PAO_HeightAboveWater(cameraPos);
    if (above >= 0.0)
    {
        return 0.0;
    }

    float waterAbove = -above;                       // 相机到水面的垂直距离（米）
    float totalDist = min(maxDistance, _PAO_GodRayDistance);

    // 从相机和水面之间的一半开始走 —— 贴着相机的头几米没有意义，
    // 那部分在屏幕上是近裁剪面附近的糊块。
    float startDist = waterAbove * 0.35;
    if (startDist >= totalDist)
    {
        return 0.0;
    }

    int steps = (int)clamp(_PAO_GodRaySteps, 1.0, 64.0);
    float stepSize = (totalDist - startDist) / float(steps);

    float3 sunDir = normalize(_PAO_SunDirection.xyz);

    float accum = 0.0;
    float weight = 0.0;

    [loop] for (int i = 0; i < steps; ++i)
    {
        // 抖动起点：用像素坐标做偏移，把规则的步进噪点打散成高频噪点，
        // 高频噪点比层状 banding 好看得多（人眼对条纹比对方块敏感）。
        float jitter = frac(sin(dot(float2(i, rayOrigin.x + rayOrigin.z), float2(12.9898, 78.233))) * 43758.5453);

        float dist = startDist + (float(i) + jitter) * stepSize;
        float3 samplePos = rayOrigin + rayDir * dist;

        // 走到水面上方了就别加了 —— 那只可能是从侧面穿出去
        if (samplePos.y > PAO_WaterLevel())
        {
            continue;
        }

        float shaft = PAO_ShaftPattern(samplePos, time);

        // 越接近水面越亮：光的来源在水面，往下越深越弱。
        // 这个梯度是「光柱有方向」的关键 —— 没有它，光柱会像
        // 悬浮在空中的雾，而不是「从上面照下来的光」。
        float depthFalloff = saturate(1.0 - (dist - startDist) / max(totalDist - startDist, 1e-4));

        // 朝向太阳的方向散射（Henyey-Greenstein 的廉价近似）。
        // 抬头对着光源看时，光柱会明显变强。
        float sunAlign = saturate(dot(rayDir, sunDir) * 0.5 + 0.5);
        float forward = lerp(1.0, 0.35 + 1.65 * sunAlign * sunAlign, _PAO_GodRaySpread);

        float w = depthFalloff * forward;

        accum  += shaft * w;
        weight += w;
    }

    if (weight <= 1e-5)
    {
        return 0.0;
    }

    // 归一化：让结果和步数无关。改步数只改质量，不改亮度 ——
    // 不然调步数会连带把亮度也改掉，参数之间互相打架。
    return (accum / weight) * _PAO_GodRayIntensity;
}

// ===========================================================================
// 水面着色
// ===========================================================================

/// 水面的完整着色。被 WaterSurface.shader 调用。
///
/// 【为什么参数这么多，不让函数自己去读材质属性】
///   因为水面同时需要**两种来源**的数据：
///     · 全局变量 —— 水面高度、水色、焦散（全屏 Pass 也在用同一份）
///     · 材质属性 —— 波光频率、亮度、亮边（只有这块面用）
///   与其在函数里写一堆宏去切换数据源（那种代码三个月后没人看得懂），
///   不如把来源摊在参数表上，调用处一眼能看出谁喂的。
///
/// 【这个函数在算什么 —— 风格化水面的三个成分】
///
///   1. **焦散网**（主体）。真实水面的波光就是水把光聚成的网。
///      直接复用焦散那套细胞噪声 —— 于是抬头看到的水面和脚下的地板
///      是**同一池水**产生的，而不是两个各画各的效果。
///
///   2. **峰值闪光**（波光粼粼的来源）。
///      把网的峰值挑出来、平方、乘上 HDR 亮度，超过 1 的那一截
///      会被 Bloom 咬成一片柔光。这一步是「粼粼」两个字的全部含义 ——
///      没有它，水面只是一层会动的图案，不会闪。
///
///   3. **方向性反光**（让它像水面而不是像贴图）。
///      用两层正弦波解析地算出一个水面法线，和太阳方向做高光。
///      波纹朝哪边斜，高光就往哪边跑 —— 这是唯一能骗过眼睛的部分。
///
///   【为什么第 1 和第 2 步都靠噪声，却要分开做】
///     焦散网给的是「光聚在哪里」（低频、成片），
///     闪光给的是「哪里最亮」（高频、成点）。
///     合成同一个函数会两边都不像。
float4 PAO_WaterSurfaceShade(float3 worldPos,
                             float3 viewDir,
                             float NoV,
                             float time,
                             float causticScale,
                             float speed,
                             float rimIntensity,
                             float sparkle,
                             float3 lightDir,
                             float3 surfaceColor,
                             float brightness)
{
    float s = max(causticScale, 0.01);
    float2 p = worldPos.xz / s;
    float t = time * speed;

    // --- 1. 焦散网 ----------------------------------------------------------
    //
    // 两层不同方向、不同速度，取最大值 —— 和地板焦散用的是同一招。
    // 用 max 而不是相加：相加会让交叉处过曝成白斑，取最大保持「一张网」。
    float2 warp = (PAO_Fbm2(p * 0.22 + float2(t * 0.13, -t * 0.09), 2) - 0.5).xx;

    float netA = PAO_CausticPattern(p * 1.00 + float2( t * 0.31,  t * 0.22) + warp * 1.1, 0.34);
    float netB = PAO_CausticPattern(p * 1.45 + float2(-t * 0.19,  t * 0.28) + warp * 0.8, 0.48);

    float net = max(netA, netB * 0.85);

    // --- 2. 峰值闪光 --------------------------------------------------------
    //
    // 【这里为什么要平方两次】
    //   第一次平方：把网收窄，亮脉之外迅速掉下去。
    //   第二次平方：只有最亮的那几个峰值能留下来。
    //   结果就是「大部分水面是暗的，少数点在爆亮」—— 这正是波光粼粼的样子。
    //
    //   只平方一次的话整张网都在亮，看起来像发光的蜘蛛网，不像水。
    float peak = net * net;
    peak *= peak;

    // 再叠一层独立的高频闪烁，让闪光点不在网的格子上规则分布 ——
    // 真实水面的闪光点是碎的、乱的。
    float twinkle = PAO_ValueNoise2(p * 3.7 + float2(t * 0.83, -t * 0.71));
    twinkle = saturate((twinkle - 0.55) * 3.2);
    peak += twinkle * twinkle * 0.55 * net;

    // --- 3. 方向性反光 ------------------------------------------------------
    //
    // 【为什么用正弦波而不是噪声】
    //   因为要**解析地**求法线。噪声只能靠差分（多采好几次），
    //   正弦波可以直接写出导数 —— 两次三角函数，比差分便宜得多，而且平滑。
    //
    // 两层不同方向、不同频率的波，凑出足够复杂的法线。
    const float2 dirA = float2( 0.86,  0.51);
    const float2 dirB = float2(-0.42,  0.91);

    float fA = t * 1.15;
    float fB = t * 0.83;

    float waveA = sin(dot(p, dirA) * 2.6 + fA * 1.7);
    float waveB = sin(dot(p, dirB) * 4.1 + fB * 2.3);

    // 导数（斜率），用来构造法线
    float slopeA = cos(dot(p, dirA) * 2.6 + fA * 1.7) * 2.6;
    float slopeB = cos(dot(p, dirB) * 4.1 + fB * 2.3) * 4.1;

    float3 n = normalize(float3(-(slopeA * dirA.x + slopeB * dirB.x) * 0.16,
                                1.0,
                                -(slopeA * dirA.y + slopeB * dirB.y) * 0.16));

    // 法线叠加一点焦散网的起伏 —— 让高光也跟着网走，两者才是一个整体
    n = normalize(n + float3((netA - netB) * 0.35, 0.0, (netA - netB) * 0.2));

    // 高光：半角向量
    float3 h = normalize(normalize(lightDir) + viewDir);
    float sheen = pow(saturate(dot(n, h)), 48.0);

    // --- 掠射亮边 -----------------------------------------------------------
    //
    // 越贴着水面看（视线越斜），越接近全反射，越亮。
    float rim = pow(saturate(1.0 - NoV), 3.0);

    // --- 合成 ---------------------------------------------------------------
    float3 color = surfaceColor;

    // 【为什么闪光项乘的是 (0.35 + net) 而不是 net】
    //   闪光点是**从网上长出来的** —— 没有网的地方不该有闪光。
    //   但完全乘 net 又会让亮脉之间的暗区彻底死黑，失去「水面」的连续感。
    float glow = net * 0.85                          // 网的底色
               + peak * sparkle * (0.35 + net)       // 闪光峰值
               + sheen * sparkle * 0.7               // 方向性反光
               + rim * rimIntensity;                 // 掠射亮边

    color *= glow * brightness;

    // 过一个指数，把动态范围压回 0~1 附近 —— 超出 1 的部分变成柔和的高光
    // 而不是硬邦邦的死白。Bloom 咬的就是溢出的那一截。
    //
    // 【为什么这里要乘 1.6】
    //   指数压缩会把峰值削掉不少。乘一个系数把峰值顶回 1 以上，
    //   Bloom 才有东西可咬。1.6 是「亮但不糊」的位置。
    color = 1.0 - exp(-color * 1.6);

    // 很淡的一层透明度：让水面即使「暗」也不至于完全消失，
    // 玩家永远能看出头顶有个面。
    float alpha = saturate(0.82 + rim * 0.18);

    return float4(color * alpha, alpha);   // 预乘 alpha
}

// ===========================================================================
// 通用小工具
// ===========================================================================

/// 从深度纹理的原始值算线性视距（米）。
/// URP 的 _ZBufferParams 已经处理好正交/透视和平台差异，直接用即可。
float PAO_LinearEyeDepth(float rawDepth)
{
    return 1.0 / (_ZBufferParams.z * rawDepth + _ZBufferParams.w);
}

/// 这个深度值是不是天空（背景）。
float PAO_IsSky(float rawDepth)
{
    return rawDepth >= UNITY_RAW_FAR_CLIP_VALUE - 1e-5 ? 1.0 : 0.0;
}

#endif // PAO_UNDERWATER_INCLUDE
