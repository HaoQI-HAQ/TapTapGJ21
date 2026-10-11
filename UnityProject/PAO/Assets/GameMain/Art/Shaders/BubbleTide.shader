Shader "PAO/BubbleTide"
{
    // -----------------------------------------------------------------------
    // 泡泡潮的全屏着色器
    //
    // Pass 0 "Raymarch" : 在「泡泡球距离场」里做真正的 SDF 光线步进，找到泡泡壳面，
    //                     用 BubbleSDF 那套着色模型（菲涅尔亮边 + 薄膜干涉彩虹 +
    //                     双高光）画出来，结果写进一张（可降采样的）HDR 中间图。
    // Pass 1 "Composite": 把 Pass 0 的结果按 over 混回相机颜色。
    //
    // -----------------------------------------------------------------------
    // 【为什么必须是「真 SDF 步进」，而不是一张等值面】
    //
    // 泡泡的观感几乎全部来自**球面法线**：菲涅尔边缘、薄膜干涉彩虹、两颗高光，
    // 全都建立在「法线是一个球面的法线」这个前提上。
    //
    // 如果潮水只用一张「第几秒被淹」的表（那只是个体积掩码），
    // 光线只能知道「这里算不算被淹」，重建不出球面法线 ——
    // 只能从掩码的梯度猜一个方向出来。而那个方向是「潮水推进方向」，
    // 不是球面法线，所以画出来永远是一层膜，不是泡泡。
    //
    // 解决办法：在烤制阶段往空腔里铺一层球，把「到最近泡泡球面的距离」也烤进
    // 同一张 3D 纹理。于是 shader 里能：
    //   · 用距离场做大步进（安全，不会穿壳）
    //   · 用距离场的**梯度**重建出正确的球面法线
    //   · 用同一个距离场判断壳面在哪，做出薄壁泡泡的「空心」感
    //
    // -----------------------------------------------------------------------
    // 【纹理：RG16，两个通道各管一件事】
    //   R = 归一化的「第几秒被淹」 → 潮水的**范围**（哪里已经漫到了）
    //   G = 到最近泡泡球面的距离场 → 潮水的**形状**（泡泡长什么样）
    //
    // G 的编码：value = -exp(-|d| / k)，k = 半径 × 0.5
    //   球面上 ≈ -1，离球面越远越趋近 0，球内部也是负的。
    // -----------------------------------------------------------------------

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        // ==================== Pass 0 : SDF 光线步进 ====================
        Pass
        {
            Name "BubbleTideRaymarch"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRaymarch
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // 复用泡泡 SDF 那套图元与调色板，保证潮水和真泡泡是同一套视觉语言
            #include "BubbleSDFInclude.hlsl"

            #define PAO_TIDE_MAX_DIST 300.0

            // ---- 场数据（C# 通过 SetGlobalTexture / SetGlobalVector 传入）----
            TEXTURE3D(_BubbleTideField);
            SAMPLER(sampler_BubbleTideField);

            float4 _BubbleTideOrigin;       // xyz = 第 (0,0,0) 格中心的世界坐标
            float4 _BubbleTideSize;         // xyz = 格子数，w = 格距（米）
            float  _BubbleTideElapsed;      // 归一化后的当前时间（和 R 通道同一个尺度）
            float  _BubbleTideEnabled;      // 0 = 完全不画
            float  _BubbleTideDistScale;    // 距离场解码尺度（= 3 × 泡泡半径）
            float  _BubbleTideBubbleRadius; // 泡泡基准半径（米）

            // ---- 材质参数（对应 BubbleSDF 的同名参数，观感对齐）----
            float  _TideMaxSteps;
            float  _TideStepScale;
            float  _TideOpacity;
            float  _TideRimPower;
            float  _TideSpecularIntensity;
            float  _TideSpecularPower;
            float  _TideIridescence;
            float  _TideIridescenceScale;
            float  _TideUseDepthOcclusion;
            float  _TideDebug;
            float4 _TideShallowColor;       // 泡泡固有色（对应 BubbleSDF 的 _BubbleTint）
            float4 _TideDeepColor;          // 备用：将来做「越深越暗」
            float4 _TideLightDir;           // xyz = 指向光源，w = 开关

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv         = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            // ==================== 场采样 ====================

            /// 世界坐标 → 纹理坐标（0~1 归一化）。
            float3 TideUVW(float3 worldPos)
            {
                return (worldPos - _BubbleTideOrigin.xyz)
                     / (_BubbleTideSize.xyz * _BubbleTideSize.w)
                     + 0.5 / _BubbleTideSize.xyz;
            }

            /// 纹理坐标是否落在网格内。
            bool TideInside(float3 uvw)
            {
                return all(uvw > 0.0) && all(uvw < 1.0);
            }

            /// R 通道：归一化的被淹时间。1 = 永不淹没。
            float TideReveal(float3 uvw)
            {
                return SAMPLE_TEXTURE3D_LOD(_BubbleTideField, sampler_BubbleTideField, uvw, 0).r;
            }

            /// G 通道：距离场值（-1 ~ 0）。
            float TideFieldRaw(float3 uvw)
            {
                return SAMPLE_TEXTURE3D_LOD(_BubbleTideField, sampler_BubbleTideField, uvw, 0).g;
            }

            /// 距离场 → 粗略的「到球面的距离」（米）。
            ///
            /// 反解 value = -exp(-d/k)：  d = -k * ln(-value)
            ///
            /// k 不单独传，直接用 _BubbleTideDistScale / 6 ——
            /// DistScale 是按「半径 × 3」定的，而 k = 半径 × 0.5，正好是它的 1/6。
            /// 这样 C# 只需要传一个数，不会出现两个尺度互相漂移。
            float TideDecodeDistance(float value)
            {
                float k = max(1e-4, _BubbleTideDistScale / 6.0);

                // value 越接近 0 说明离球面越远，距离趋于无穷 —— 夹一下防止 log 炸掉
                float v = clamp(-value, 1e-4, 1.0);
                return -k * log(v);
            }

            /// 某个世界坐标处的「到最近泡泡球面的距离」（米）。
            float TideBubbleDistance(float3 worldPos)
            {
                float3 uvw = TideUVW(worldPos);
                if (!TideInside(uvw))
                {
                    return PAO_TIDE_MAX_DIST;   // 网格外当作没有泡泡
                }

                return TideDecodeDistance(TideFieldRaw(uvw));
            }

            /// 淹没程度 0~1，在一个格子的宽度内平滑过渡。
            ///
            /// 【为什么不能直接用二值判断】
            /// 潮水面是按格子推进的，直接用「时间 <= 当前」做二值判断的话，
            /// 前沿会以格距为单位一格一格地跳 —— 播放出来是明显的方块状撕裂。
            /// 这里把「还有多少时间才淹到」换算成空间距离，在半个格子里淡入，
            /// 前沿就是一条干净的线。
            float TideRevealAlpha(float3 worldPos)
            {
                float3 uvw = TideUVW(worldPos);
                if (!TideInside(uvw))
                {
                    return 0.0;
                }

                float arrival = TideReveal(uvw);
                float remaining = arrival - _BubbleTideElapsed;   // 正值 = 还没淹到

                if (remaining <= 0.0)
                {
                    return 1.0;
                }

                // 一个格子在时间轴上有多宽？（用归一化尺度折算）
                float cellWidth = max(1e-4,
                    1.0 / max(_BubbleTideSize.x, max(_BubbleTideSize.y, _BubbleTideSize.z)));

                return saturate(1.0 - remaining / cellWidth);
            }

            // ==================== 法线 ====================

            /// 用距离场的中心差分重建球面法线。
            /// 这是「看起来像泡泡」最关键的一步 —— 没有它就只能从掩码梯度猜方向。
            ///
            /// 采样步长取泡泡半径的 1/4：太小会被格子量化噪声放大，
            /// 太大就把相邻几颗泡泡抹成一个平面。
            float3 TideNormal(float3 worldPos)
            {
                float h = max(0.02, _BubbleTideBubbleRadius * 0.25);

                float dx = TideBubbleDistance(worldPos + float3(h, 0, 0))
                         - TideBubbleDistance(worldPos - float3(h, 0, 0));
                float dy = TideBubbleDistance(worldPos + float3(0, h, 0))
                         - TideBubbleDistance(worldPos - float3(0, h, 0));
                float dz = TideBubbleDistance(worldPos + float3(0, 0, h))
                         - TideBubbleDistance(worldPos - float3(0, 0, h));

                float3 n = float3(dx, dy, dz);
                float len = length(n);

                return len > 1e-6 ? n / len : float3(0.0, 1.0, 0.0);
            }

            // ==================== 光线步进 ====================

            /// 计算光线与网格 AABB 的进入距离（不相交返回 -1）。
            ///
            /// 【注意】不能用 1/dir 再乘 sign 的写法 —— 某个分量为 0 时会得到
            /// inf × 0 = NaN，整条光线就废了。改成逐分量判断符号。
            float RayBoxEntry(float3 origin, float3 dir, float3 boxMin, float3 boxMax)
            {
                float tmin = -1e30;
                float tmax = 1e30;

                for (int axis = 0; axis < 3; ++axis)
                {
                    float o = origin[axis];
                    float d = dir[axis];

                    if (abs(d) < 1e-6)
                    {
                        // 该轴平行：原点必须落在 slab 内，否则永远打不到
                        if (o < boxMin[axis] || o > boxMax[axis])
                        {
                            return -1.0;
                        }

                        continue;
                    }

                    float inv = 1.0 / d;
                    float t0 = (boxMin[axis] - o) * inv;
                    float t1 = (boxMax[axis] - o) * inv;

                    tmin = max(tmin, min(t0, t1));
                    tmax = min(tmax, max(t0, t1));
                }

                if (tmax < max(tmin, 0.0))
                {
                    return -1.0;
                }

                return max(tmin, 0.0);
            }

            /// 在泡泡距离场里步进，找出潮水面所在的泡泡壳面。
            ///
            /// 与真泡泡那套（BubbleSDF 的 Raymarch）的关键区别：
            /// 那边是一堆独立的球壳，需要自己算「到最近球面的解析距离」来避免穿壳；
            /// 我们这边已经有一张烤好的距离场，可以直接拿它当安全步长。
            ///
            /// 命中条件：走到距离场接近 0（壳面）**且**那里已经被淹。
            /// 两个条件都要 —— 距离场铺满了整个房间，
            /// 只用距离场的话玩家会看到还没漫到的泡泡。
            bool MarchTide(
                float3 rayOrigin,
                float3 rayDir,
                float maxDistance,
                out float3 hitPosition,
                out float hitReveal)
            {
                hitPosition = rayOrigin;
                hitReveal = 0.0;

                float cell = max(0.05, _BubbleTideSize.w);
                float surfaceEps = max(0.01, _BubbleTideBubbleRadius * 0.06);
                int maxSteps = (int)clamp(_TideMaxSteps, 16.0, 512.0);

                float3 boxMin = _BubbleTideOrigin.xyz - cell * 0.5;
                float3 boxMax = _BubbleTideOrigin.xyz + (_BubbleTideSize.xyz - 0.5) * cell;

                float tEnter = RayBoxEntry(rayOrigin, rayDir, boxMin, boxMax);
                if (tEnter < 0.0)
                {
                    return false;
                }

                float tExit = min(maxDistance, length(boxMax - boxMin) * 1.8);
                float t = max(tEnter, 0.0);

                float minStep = max(0.005, surfaceEps * 0.25);
                float stepScale = max(0.15, _TideStepScale);

                for (int i = 0; i < maxSteps; ++i)
                {
                    if (t > tExit)
                    {
                        return false;
                    }

                    float3 p = rayOrigin + rayDir * t;
                    float d = TideBubbleDistance(p);

                    if (d < surfaceEps)
                    {
                        // 走到壳面了，但还得看这块漫到了多少
                        float reveal = TideRevealAlpha(p);

                        if (reveal > 0.01)
                        {
                            hitPosition = p;
                            hitReveal = reveal;
                            return true;
                        }

                        // 完全还没漫到：直接往前推一大步跳过这颗泡泡，
                        // 否则会在未淹没的泡泡壳上原地磨到步数用完
                        t += max(cell * 2.0, _BubbleTideBubbleRadius * 2.0);
                        continue;
                    }

                    // 安全步长：距离场保证不会穿过壳面，乘个保守系数兜住场的近似误差
                    float step = clamp(d * stepScale, minStep, cell * 3.0 * stepScale);
                    t += step;
                }

                return false;
            }

            // ==================== 着色 ====================

            /// 泡泡的着色模型。这一段是照着 BubbleSDF.shader 的 FragSDF 写的，
            /// 参数名一一对应 —— 改观感时要两边一起改，
            /// 否则潮水和真泡泡会看起来是两种东西。
            ///
            /// 关键特征（少一个就不像泡泡）：
            ///   · 中心几乎全透（底色只乘 0.10），掠射角处才亮起来
            ///   · 菲涅尔亮边 pow(1 - NoV, rimPower)
            ///   · 薄膜干涉用余弦调色板，相位随 (1-NoV) 增长 → 一圈圈彩虹
            ///   · 两颗高光：主高光 + 一颗弱的反向高光，做出玻璃球质感
            float4 TideShade(float3 p, float3 n, float3 V)
            {
                float NoV = saturate(dot(n, V));
                float fresnel = pow(1.0 - NoV, max(_TideRimPower, 0.5));

                // ---- 薄膜干涉彩虹 ----
                // 真实泡泡膜厚 100~1000nm，光程差 ≈ 2·n·d·cosθ 随观察角变化，
                // 各波长交替相长/相消，于是形成一圈圈彩虹。
                // 这里用「相位随 (1-NoV) 增长」近似 —— 和 BubbleSDF 完全一致。
                float phase = _TideIridescenceScale * (1.0 - NoV) * 3.6;
                float3 rainbow = palette(phase,
                                         float3(0.5, 0.5, 0.5),
                                         float3(0.5, 0.5, 0.5),
                                         float3(1.0, 1.0, 1.0),
                                         float3(0.0, 0.33, 0.67));
                float iridMask = saturate(fresnel * 1.7) * _TideIridescence;

                // ---- 双高光 ----
                float3 L = _TideLightDir.xyz;
                if (length(L) < 1e-4)
                {
                    L = float3(-0.4, 0.8, -0.45);
                }
                L = normalize(L);

                float3 Hv = normalize(L + V);
                float spec = pow(saturate(dot(n, Hv)), max(_TideSpecularPower, 1.0))
                           * _TideSpecularIntensity * _TideLightDir.w;

                // 一颗较弱的反向高光，加「玻璃球」质感
                float3 L2 = normalize(float3(-L.x, -L.y * 0.4, -L.z));
                float3 Hv2 = normalize(L2 + V);
                float spec2 = pow(saturate(dot(n, Hv2)), max(_TideSpecularPower * 0.5, 1.0))
                            * _TideSpecularIntensity * 0.35 * _TideLightDir.w;

                // ---- 底色 ----
                float3 base = _TideShallowColor.rgb;

                float3 col = base * 0.10;
                col += rainbow * iridMask * 1.3;
                col += float3(1.0, 1.0, 1.0) * (spec + spec2);
                col += float3(0.55, 0.75, 0.95) * fresnel * 0.30;

                float alpha = saturate(
                    (0.18 + fresnel * 0.95 + spec + spec2)
                    * (0.5 + _TideOpacity));

                return float4(col * alpha, alpha);   // 预乘 alpha
            }

            // ==================== 主函数 ====================

            float4 FragRaymarch(Varyings input) : SV_Target
            {
                if (_BubbleTideEnabled < 0.5)
                {
                    return float4(0, 0, 0, 0);
                }

                // ---- 重建这一像素的世界空间射线 ----
                //
                // 【关键】射线方向**不能**从场景深度反推。
                // 项目的三个 URP 资产里 m_RequireDepthTexture 都是 0，
                // 意味着 _CameraDepthTexture 可能根本没被渲染 ——
                // 那时 SampleSceneDepth 拿到的是垃圾值，整条射线都会是错的，
                // 表现就是潮水完全看不见（而且不报任何错，极难查）。
                //
                // 所以方向用相机矩阵现算：屏幕上一点对应的方向，
                // 等于相机右/上/前三个轴按视锥比例加权。不依赖任何深度纹理，永远正确。
                float2 uv = input.uv;
                float2 ndc = uv * 2.0 - 1.0;

                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float3 camFwd   = -UNITY_MATRIX_V[2].xyz;

                float3 rayOrigin = _WorldSpaceCameraPos;

                float3 rayDir;
                if (unity_OrthoParams.w > 0.0)
                {
                    // 正交相机：所有光线同向
                    rayDir = camFwd;
                }
                else
                {
                    rayDir = normalize(
                        camFwd
                        + camRight * (ndc.x * unity_CameraProjection[0][0])
                        + camUp    * (ndc.y * unity_CameraProjection[1][1]));
                }

                // ---- 场景深度当遮挡（可选）----
                // 潮水面在实心墙后面就不该被看见。
                // 拿不到有效深度时自动退化：不做遮挡，画面照常，只是泡泡会透过墙。
                float maxDistance = PAO_TIDE_MAX_DIST;
                if (_TideUseDepthOcclusion > 0.5)
                {
                    float rawDepth = SampleSceneDepth(uv);
                    bool isSkybox = rawDepth >= UNITY_RAW_FAR_CLIP_VALUE - 1e-5;

                    if (!isSkybox)
                    {
                        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                        float cosAngle = max(0.05, dot(rayDir, camFwd));
                        maxDistance = eyeDepth / cosAngle + 0.05;
                    }
                }

                float3 hitPos;
                float hitReveal;
                bool hit = MarchTide(rayOrigin, rayDir, maxDistance, hitPos, hitReveal);

                // 相机被埋在泡泡堆深处时，这条光线可能一路走到边界都没找到壳面
                // （前方几十米全是泡泡）。这时如果什么都不画，画面会突然变干净，
                // 看着像潮水消失了。所以退化成一层均匀的「泡在海里」的颜色。
                if (!hit)
                {
                    float immersedReveal = TideRevealAlpha(rayOrigin);

                    if (immersedReveal > 0.01)
                    {
                        float immersed = 0.70 * immersedReveal;
                        float3 immersedColor = _TideShallowColor.rgb * 0.25;
                        return float4(immersedColor * immersed, immersed);
                    }

                    return float4(0, 0, 0, 0);
                }

                // ---- 调试可视化 ----
                if (_TideDebug > 0.5)
                {
                    float d = saturate(distance(hitPos, rayOrigin) / 40.0);
                    return float4(d, 0.2, 1.0 - d, 1.0);
                }

                // ---- 着色 ----
                float3 n = TideNormal(hitPos);
                float3 V = -rayDir;

                float4 shaded = TideShade(hitPos, n, V);

                // 前沿淡入：让潮水面是一条干净的线，而不是按格子跳的方块边
                return shaded * hitReveal;
            }

            ENDHLSL
        }

        // ==================== Pass 1 : 合成 ====================
        Pass
        {
            Name "BubbleTideComposite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_BubbleTideTexture);
            SAMPLER(sampler_BubbleTideTexture);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv         = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            float4 FragComposite(Varyings input) : SV_Target
            {
                // _BlitTexture: 相机当前画面
                // _BubbleTideTexture: Pass 0 的结果（预乘 alpha）
                float4 src  = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                float4 tide = SAMPLE_TEXTURE2D(_BubbleTideTexture, sampler_BubbleTideTexture, input.uv);

                // 标准的 over 合成：tide.rgb 已预乘，所以直接加 + 背景乘 (1-a)
                float a = saturate(tide.a);
                float3 col = tide.rgb + src.rgb * (1.0 - a);

                return float4(col, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
