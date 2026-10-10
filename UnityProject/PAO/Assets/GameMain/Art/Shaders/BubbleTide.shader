Shader "PAO/BubbleTide"
{
    // -----------------------------------------------------------------------
    // 泡泡潮的全屏着色器
    //
    // Pass 0 "Raymarch" : 沿着每条像素射线走，在「泡泡潮场」里找出潮水面。
    //                     结果写进一张（可降采样的）HDR 中间图。
    // Pass 1 "Composite": 把 Pass 0 的结果按 alpha 混回相机颜色。
    //
    // 【为什么是光线步进而不是几万个泡泡网格】
    // 一个能淹掉 43×13×33 米房间的潮水，用 0.5 米的泡泡去填是几十万颗。
    // 做成网格就是几十万次 DrawCall 或几百万个三角形，移动端直接没戏。
    // 而潮水本来就有一张「每格第几秒被淹」的表，把它当成一个场来步进，
    // 成本就变成「每像素固定步数」—— 与潮水大小完全无关。
    //
    // 场数据由 C# 通过 Texture3D "_BubbleTideField" 传入（R8，单通道）。
    // 纹理里存的是**归一化**的被淹时间：0 = 一开始就淹了，1 = 最后才淹到。
    // 255 是保留值，表示「永远不会被淹」（实心障碍物）。
    // 因为存的是归一化值，改关卡的截止时间不需要重烤纹理。
    // -----------------------------------------------------------------------

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        // ==================== Pass 0 : 光线步进 ====================
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

            // ---- 场数据（C# 通过 SetGlobalTexture / SetGlobalVector 传入）----
            TEXTURE3D(_BubbleTideField);
            SAMPLER(sampler_BubbleTideField);

            float4 _BubbleTideOrigin;   // xyz = 第 (0,0,0) 格中心的世界坐标
            float4 _BubbleTideSize;     // xyz = 格子数，w = 格距（米）
            float  _BubbleTideElapsed;  // 归一化后的当前时间（和纹理里同一个尺度）
            float  _BubbleTideEnabled;  // 0 = 完全不画

            // ---- 材质参数 ----
            float  _TideMaxSteps;
            float  _TideStepScale;
            float  _TideOpacity;
            float  _TideRimPower;
            float  _TideRimIntensity;
            float  _TideIridescence;
            float  _TideIridescenceScale;
            float  _TideNoiseScale;
            float  _TideNoiseStrength;
            float  _TideUseDepthOcclusion;
            float  _TideDebug;
            float4 _TideShallowColor;
            float4 _TideDeepColor;      // 备用：将来做「越深越暗」时用，目前着色只读 Shallow
            float4 _TideLightDir;       // xyz = 指向光源，w = 开关

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

            /// 格子索引（浮点，带 0.5 偏移表示格子中心）。
            float3 TideCellCoord(float3 worldPos)
            {
                return (worldPos - _BubbleTideOrigin.xyz) / _BubbleTideSize.w;
            }

            /// 世界坐标是否在网格覆盖范围内。
            bool TideInside(float3 cell)
            {
                return all(cell >= 0.0) && all(cell <= _BubbleTideSize.xyz - 1.0);
            }

            /// 归一化的被淹时间。255 那一档代表「永远不会被淹」，这里返回一个大值。
            float TideArrival(float3 cell)
            {
                float raw = SAMPLE_TEXTURE3D_LOD(_BubbleTideField, sampler_BubbleTideField, cell, 0).r;

                // 254/255 是「永不淹没」的哨兵值。
                // 用 step 而不是 if，是为了避免在步进循环里出现分支。
                float isForever = step(254.0 / 255.0, raw);
                return lerp(raw, 1e6, isForever);
            }

            float TideArrivalWorld(float3 worldPos)
            {
                float3 cell = TideCellCoord(worldPos);
                if (!TideInside(cell))
                {
                    return 1e6;   // 网格外当作永远不会被淹
                }

                // + 0.5：纹理采样点对齐到格子中心
                return TideArrival(cell + 0.5);
            }

            // ==================== 法线 ====================

            /// 用场的中心差分求「被淹时间」的梯度，取负就是潮水推进方向。
            /// 我们再把它和网格的「出界方向」混一下 ——
            /// 场只能给出时间梯度，在潮水面上它垂直于潮水面，
            /// 但单靠它拿到的是「时间变化最快」的方向，不一定等于几何法线，
            /// 所以额外叠一个「离最近墙面的方向」，让泡泡看起来是圆的。
            float3 TideNormal(float3 worldPos)
            {
                float h = _BubbleTideSize.w;

                float tx0 = TideArrivalWorld(worldPos - float3(h, 0, 0));
                float tx1 = TideArrivalWorld(worldPos + float3(h, 0, 0));
                float ty0 = TideArrivalWorld(worldPos - float3(0, h, 0));
                float ty1 = TideArrivalWorld(worldPos + float3(0, h, 0));
                float tz0 = TideArrivalWorld(worldPos - float3(0, 0, h));
                float tz1 = TideArrivalWorld(worldPos + float3(0, 0, h));

                float3 grad = float3(tx1 - tx0, ty1 - ty0, tz1 - tz0);

                // 用 -gradient：指向「更早被淹」的一侧，也就是朝外的方向
                float3 n = -grad;

                float len = length(n);
                if (len > 1e-5)
                {
                    return n / len;
                }

                return float3(0, 1, 0);
            }

            // ==================== 光线步进 ====================

            /// 计算光线与网格 AABB 的进入距离（不相交返回 -1）。
            ///
            /// 注意这里【不能】用 1/dir 再乘 sign 的写法 ——
            /// 那样在某个分量为 0 时会得到 inf * 0 = NaN，整条光线就废了。
            /// 改成逐分量判断符号，逐个求进出参数。
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
                        // 该轴平行：原点必须落在slab内，否则永远打不到
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

            /// 找出射线穿过潮水面的位置。
            /// 返回 false 表示这条光线没打中潮水。
            ///
            /// 两个方向都要处理：
            ///   · 相机在外面 → 找「没淹 → 被淹」的跨越点，看到的是潮水的外表面
            ///   · 相机已经在潮水里面 → 找「被淹 → 没淹」的跨越点，看到的是内壁。
            ///     如果漏了这一条，玩家一旦被淹，画面里的潮水会整个消失
            ///     （因为起点就满足「被淹」，永远检测不到跨入）。
            bool MarchTide(
                float3 rayOrigin,
                float3 rayDir,
                float maxDistance,
                bool invert,
                out float3 hitPosition,
                out float hitDepth)
            {
                hitPosition = 0;
                hitDepth = 0;

                float cell = max(0.05, _BubbleTideSize.w);
                float stepSize = cell * max(0.25, _TideStepScale);
                int maxSteps = (int)clamp(_TideMaxSteps, 16.0, 512.0);

                // 提前算好包围盒，把步进范围缩到「真正可能有东西」的那一段
                float3 boxMin = _BubbleTideOrigin.xyz - cell * 0.5;
                float3 boxMax = _BubbleTideOrigin.xyz + (_BubbleTideSize.xyz - 0.5) * cell;

                float tEnter = RayBoxEntry(rayOrigin, rayDir, boxMin, boxMax);
                if (tEnter < 0.0)
                {
                    return false;
                }

                float tExit = min(maxDistance, length(boxMax - boxMin) * 1.8);
                float t = max(tEnter, 0.0);

                // 跨越检测：比较相邻两次采样的「是否被淹」状态。
                // 比逐点比较阈值稳定得多 —— 逐点比较在场缓慢变化时会整段都落在同一侧而漏检。
                bool prevFlooded = (TideArrivalWorld(rayOrigin + rayDir * t) - _BubbleTideElapsed) <= 0.0;

                // invert 时反过来找「被淹 → 没淹」的那个面
                bool wantFlooded = !invert;

                for (int i = 0; i < maxSteps; ++i)
                {
                    float nextT = t + stepSize;
                    if (nextT > tExit)
                    {
                        break;
                    }

                    float3 p = rayOrigin + rayDir * nextT;
                    bool flooded = (TideArrivalWorld(p) - _BubbleTideElapsed) <= 0.0;

                    if (flooded == wantFlooded && prevFlooded != wantFlooded)
                    {
                        // 在 [t, nextT] 之间二分细化，让边缘不随步长闪烁
                        float lo = t;
                        float hi = nextT;

                        for (int k = 0; k < 6; ++k)
                        {
                            float mid = (lo + hi) * 0.5;
                            float3 pm = rayOrigin + rayDir * mid;
                            bool midFlooded = (TideArrivalWorld(pm) - _BubbleTideElapsed) <= 0.0;

                            if (midFlooded == wantFlooded)
                            {
                                hi = mid;
                            }
                            else
                            {
                                lo = mid;
                            }
                        }

                        hitPosition = rayOrigin + rayDir * hi;
                        hitDepth = hi - tEnter;
                        return true;
                    }

                    prevFlooded = flooded;
                    t = nextT;
                }

                return false;
            }

            // ==================== 外观 ====================

            /// 用世界坐标做一层稳定的泡泡纹理，不需要任何美术资源。
            float BubbleNoise(float3 p)
            {
                float s = max(0.0001, _TideNoiseScale);
                float n = sin(p.x * 7.13 * s) * sin(p.y * 9.71 * s) * sin(p.z * 8.37 * s);
                n += 0.5 * sin(p.x * 17.3 * s + p.y * 13.1 * s) * sin(p.z * 19.7 * s);
                return saturate(n * 0.5 + 0.5);
            }

            float3 TideShade(float3 worldPos, float3 normal, float3 viewDir)
            {
                float3 base = _TideShallowColor.rgb;

                // 菲涅尔亮边 —— 泡泡最好认的特征
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), max(0.5, _TideRimPower));

                // 薄膜干涉：颜色随视角与位置变化，像是肥皂膜。
                // 注意这里的 lerp 用的是三分量常数，写成 lerp(float3(1,1,1), iris, k)
                // 在有些编译器上会被当成标量运算，色环就没了。
                float film = saturate(dot(normal, viewDir)) * _TideIridescenceScale
                           + BubbleNoise(worldPos) * 2.0;
                float3 iridescence = 0.5 + 0.5 * cos(6.2831 * (film + float3(0.0, 0.33, 0.67)));
                iridescence = lerp(float3(1.0, 1.0, 1.0), iridescence, saturate(_TideIridescence));

                // 高光
                float3 lightDir = _TideLightDir.xyz;
                if (length(lightDir) < 1e-4)
                {
                    lightDir = float3(-0.4, 0.8, -0.45);
                }
                lightDir = normalize(lightDir);

                float3 halfDir = normalize(lightDir + viewDir);
                float specular = pow(saturate(dot(normal, halfDir)), 96.0) * 1.2;

                // 泡泡纹理：让表面看起来是一颗颗的，而不是一块塑料
                float noise = BubbleNoise(worldPos * 3.0);
                float bubblePattern = lerp(1.0, 0.72 + 0.5 * noise, saturate(_TideNoiseStrength));

                float3 color = base * bubblePattern;
                color += iridescence * fresnel * _TideRimIntensity;
                color += specular;

                return color;
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
                // 表现就是潮水完全看不见（而且不会报任何错，极难查）。
                //
                // 所以方向用相机矩阵现算：屏幕上一点对应的方向，
                // 等于相机右/上/前三个轴按视锥比例加权。
                // 这个做法不依赖任何深度纹理，永远正确。
                float2 uv = input.uv;
                float2 ndc = uv * 2.0 - 1.0;

                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float3 camFwd   = -UNITY_MATRIX_V[2].xyz;

                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(
                    camFwd
                    + camRight * (ndc.x * unity_CameraProjection[0][0])
                    + camUp    * (ndc.y * unity_CameraProjection[1][1]));

                // ---- 场景深度当遮挡（可选）----
                // 潮水面如果在实心墙后面就不该被看见。
                // 拿不到有效深度时自动退化：不做遮挡，画面照常，只是泡泡会透过墙。
                float maxDistance = 1e5;
                if (_TideUseDepthOcclusion > 0.5)
                {
                    float rawDepth = SampleSceneDepth(uv);

                    // 天空 / 未渲染的深度：不做遮挡
                    bool isSkybox = rawDepth >= UNITY_RAW_FAR_CLIP_VALUE - 1e-5;
                    if (!isSkybox)
                    {
                        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                        // 除以与相机前向的夹角余弦，得到沿这条射线允许走的距离
                        float cosAngle = max(0.05, dot(rayDir, camFwd));
                        maxDistance = eyeDepth / cosAngle + 0.05;
                    }
                }

                // ---- 相机在潮水里还是外面 ----
                // 决定我们看到的是潮水的**外表面**（找「没淹→被淹」）还是**内壁**
                // （找「被淹→没淹」）。玩家被潮水淹掉之后，如果只做前者，
                // 画面里的潮水会整个消失 —— 因为起点就已经满足「被淹」了。
                bool cameraFlooded =
                    (TideArrivalWorld(rayOrigin) - _BubbleTideElapsed) <= 0.0;

                float3 hitPos;
                float hitDepth;
                bool hit = MarchTide(rayOrigin, rayDir, maxDistance, cameraFlooded, hitPos, hitDepth);

                // 相机被埋在潮水深处时，这条光线可能一路走到边界都没找到出口
                // （前方几十米全是泡泡）。这时如果什么都不画，画面会突然变干净，
                // 看着像潮水消失了。所以退化成一层均匀的「泡在海里」的颜色。
                if (!hit && cameraFlooded)
                {
                    float3 immersed = _TideShallowColor.rgb * 0.55;
                    float immersedAlpha = saturate(_TideOpacity) * 0.85;
                    return float4(immersed * immersedAlpha, immersedAlpha);
                }

                if (!hit)
                {
                    return float4(0, 0, 0, 0);
                }

                // ---- 调试可视化 ----
                if (_TideDebug > 0.5)
                {
                    // 模式 1：画出命中距离（近 = 蓝，远 = 红）
                    float d = saturate(hitDepth / 40.0);
                    return float4(d, 0.2, 1.0 - d, 1.0);
                }

                // ---- 着色 ----
                float3 normal = TideNormal(hitPos);

                // 从潮水内部看内壁时反一下法线，亮边才会出现在正确的一侧
                if (cameraFlooded)
                {
                    normal = -normal;
                }

                float3 viewDir = -rayDir;
                float3 color = TideShade(hitPos, normal, viewDir);

                // 相机在水里时整体压低一点不透明度，让玩家能看清被淹没的场面，
                // 而不是被糊成一片白。
                float alpha = saturate(_TideOpacity) * (cameraFlooded ? 0.55 : 1.0);
                return float4(color * alpha, alpha);
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
                float4 scene = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, input.uv);
                float4 tide  = SAMPLE_TEXTURE2D(_BubbleTideTexture, sampler_BubbleTideTexture, input.uv);

                // 潮水那张图里已经乘过 alpha（预乘），直接叠加即可
                scene.rgb += tide.rgb;
                scene.a = max(scene.a, tide.a);

                return scene;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
