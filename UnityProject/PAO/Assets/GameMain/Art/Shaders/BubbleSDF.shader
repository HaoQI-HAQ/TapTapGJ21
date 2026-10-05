Shader "PAO/BubbleSDF"
{
    // -----------------------------------------------------------------------
    // 泡泡 SDF 全屏着色器
    //
    // Pass 0 "BubbleSDF"   : 逐像素向场景球体追踪（raymarching），现场求出一堆泡泡的
    //                        「距离场」，把实心球挖空成薄膜球壳，再算薄膜干涉彩虹、
    //                        高光、菲涅尔。结果写进一张（可降采样的）HDR 中间图。
    // Pass 1 "Composite"   : 把 Pass 0 的结果按 alpha 混回相机颜色。
    //
    // 形状数据由 C# 通过 StructuredBuffer "_ShapeDataBuffer" 传入；
    // 其余调参统一走 SetGlobalFloat / SetGlobalInt，不占用材质面板。
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
            Name "BubbleSDF"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSDF
            #pragma target 4.5          // StructuredBuffer 需要 SM4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "BubbleSDFInclude.hlsl"

            #define PAO_MAX_STEP      128
            #define PAO_MIN_DIST      0.001
            #define PAO_MAX_DIST      200.0
            #define PAO_MAX_STEP_SIZE 0.5

            // ---- GPU 侧形状数据 ------------------------------------------
            // 必须与 C# 的 BubblePayload 逐字段对应：**20 × float = 80 字节**。
            //
            // 布局上只用 float4 打底 + 散装 float，【故意不用 float3】：
            // float 是 4 字节自对齐，这样字段之间不会有任何填充，
            // 托管侧和 HLSL 必然一致（float3 是 12 字节，后面跟字段时要补位，
            // 两边补齐规则还可能不一样，很容易差几个字节）。
            //
            // 数的时候注意：位置组 4 个、尺寸组 4 个、颜色组 4 个、
            // custom 组 4 个、预留组 4 个 —— 一共 20 个，不是 18 个。
            // 改这里必须同步改 C# 的 BubblePayload（Stride / FieldCount）。
            struct ShapeData
            {
                float4 posType;      // position.xyz + shapeType
                float4 sizeOp;       // size.xyz（直径）+ operation
                float4 color;        // rgb + 不透明度
                float  shellRatio;   // 壳厚 / 半径
                float  fuseStrength; // 融合强度（米）
                float  distortion;   // 抖动幅度（相对半径）
                float  distortionFreq;// 抖动频率
                float  blendStrength;// 预留
                float  numChildren;  // 预留
                float  phase;        // 每颗泡泡的相位（错开彩虹）
                float  pad0;
                float  pad1;         // 对齐用，保证和 C# 一样是 20 个 float
            };

            StructuredBuffer<ShapeData> _ShapeDataBuffer;

            // ---- 全局参数（C# 驱动）----
            int      _ShapeDataCount;
            float    _BubbleGlobalFuse;
            float    _BubbleDistortionSpeed;
            float    _BubbleIridescence;
            float    _BubbleIridescenceScale;
            float    _BubbleSpecularIntensity;
            float    _BubbleSpecularPower;
            float    _BubbleOpacity;
            float    _BubbleRimPower;
            float4   _BubbleLightDir;      // xyz = 指向光源, w = 开关
            float4   _BubbleTint;          // 全局色调
            float    _BubbleEnabled;
            float    _BubbleOcclusion;  // >0.5 = 用场景深度遮挡泡泡
            float4x4 _IVP;
            int      _BubbleDebug;      // 0 = 正常；见 BubbleSDFRenderFeature.DebugMode

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);
            TEXTURE2D(_DetailTexture);
            SAMPLER(sampler_DetailTexture);

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

            // ======================= 距离场 =======================

            // 单颗泡泡：外壳 - 内腔 = 一层薄膜球壳。
            // 返回值 0 处是壳体中面，也就是我们最终看到的表面。
            float BubbleShellDistance(ShapeData d, float3 p)
            {
                float  radius    = max(d.sizeOp.x * 0.5, 1e-4);
                float  thickness = clamp(d.shellRatio, 0.004, 0.5);
                float  inner     = radius * (1.0 - thickness);
                float3 q         = p - d.posType.xyz;

                float dOuter = sdSphere(q, radius);
                float dInner = sdSphere(q, inner);

                // 硬差集：内腔之外 且 外壳之内。这样 SDF 在壳体内外符号相反、
                // 梯度幅值接近 1，适合步进。（若用 smax，|SDF| 会被抬高，
                // 在较厚的壳上会明显高估剩余距离。）
                float dist = max(dOuter, -dInner);

                // 表面不安定感：沿半径方向的低频正弦扰动，幅度随半径缩放。
                // 只在贴近表面处生效，避免球心附近破坏 SDF 性质。
                //
                // 渐隐半径必须比 |dist| 可能达到的范围更大：
                // 壳体最厚处 |dist| = thickness/2 * radius，而 amp 最大是 radius/4。
                // 取 radius*1.1 覆盖得住。若渐隐半径太小，抖动会在壳体内部
                // 被截断成台阶，那些台阶会被光线步进当成真表面 —— 表现为
                // 泡泡内部冒出脏壳。这个余量是必须的，别调小。
                float amp = d.distortion * radius * 0.25;
                if (amp > 1e-5 && abs(dist) < radius * 1.1)
                {
                    float3 nb = normalize(q + 1e-4);
                    float  tt = _Time.y * _BubbleDistortionSpeed;
                    float  w  = d.distortionFreq;

                    float wob = sin(tt * 1.00 + nb.x * w * 12.9898)
                              * sin(tt * 1.31 + nb.y * w * 7.2330)
                              * sin(tt * 0.77 + nb.z * w * 9.5170);

                    dist -= wob * amp * saturate(1.0 - abs(dist) / (radius * 1.1));
                }

                return dist;
            }

            // 全场景距离场：所有泡泡平滑并集，重叠处自然融合成一坨融球。
            float SceneDistance(float3 p)
            {
                float d = PAO_MAX_DIST;
                for (int i = 0; i < _ShapeDataCount; ++i)
                {
                    ShapeData s = _ShapeDataBuffer[i];
                    float di = BubbleShellDistance(s, p);
                    float k  = max(s.fuseStrength, _BubbleGlobalFuse);
                    d = smin(d, di, k);
                }
                return d;
            }

            // 场景里最薄的那层壳。决定法线采样步长与步进上限。
            float SceneMinThickness()
            {
                float mn = 1e9;
                for (int i = 0; i < _ShapeDataCount; ++i)
                {
                    ShapeData s = _ShapeDataBuffer[i];
                    mn = min(mn, max(s.sizeOp.x * 0.5 * s.shellRatio, 1e-3));
                }
                return (mn > 1e8) ? 0.01 : mn;
            }

            // 中心差分重建法线。壳很薄，采样步长必须比壳厚小一个量级。
            float3 SceneNormal(float3 p, float eps)
            {
                float2 e = float2(eps, 0.0);
                float3 n;
                n.x = SceneDistance(p + e.xyy) - SceneDistance(p - e.xyy);
                n.y = SceneDistance(p + e.yxy) - SceneDistance(p - e.yxy);
                n.z = SceneDistance(p + e.yyx) - SceneDistance(p - e.yyx);
                float len = length(n);
                return len > 1e-8 ? n / len : float3(0.0, 0.0, 1.0);
            }

            // 与 p 处壳面的贴近程度 -> 用于取颜色 / 透明度
            float WeightAt(ShapeData s, float3 p)
            {
                float di = BubbleShellDistance(s, p);
                return exp2(-abs(di) * 6.0);
            }

            float3 SceneColor(float3 p)
            {
                float3 col  = float3(0.0, 0.0, 0.0);
                float  wsum = 0.0;
                for (int i = 0; i < _ShapeDataCount; ++i)
                {
                    ShapeData s = _ShapeDataBuffer[i];
                    float w = WeightAt(s, p);
                    col  += s.color.rgb * w;
                    wsum += w;
                }
                return wsum > 1e-5 ? col / wsum : float3(1.0, 1.0, 1.0);
            }

            float SceneShapeAlpha(float3 p)
            {
                float a = 0.0;
                for (int i = 0; i < _ShapeDataCount; ++i)
                {
                    ShapeData s = _ShapeDataBuffer[i];
                    a = max(a, s.color.a * WeightAt(s, p));
                }
                return a;
            }

            // ======================= 光线步进 =======================
            //
            // 球壳的 SDF 在壳外远处等于到外壳的距离，可以大步走；
            // 一旦进入某个包围球，就必须把步长压到远小于壳厚，
            // 否则光线会直接穿壳而过——这是薄壁 raymarching 最典型的漏检。
            //
            // tMax：沿光线的最大行进距离（世界单位）。
            //   - 没开遮挡时就是 PAO_MAX_DIST
            //   - 开了遮挡时传「该像素的场景深度」，这样泡泡一旦跑到实体后面
            //     就会自然判定为未命中 —— 这就是「显示在 3D 里」的关键。

            float4 Raymarch(float3 rayPos, float3 rayDir, float minThickness, float tMax)
            {
                float t     = 0.0;
                float prevD = PAO_MAX_DIST;

                for (int step = 0; step < PAO_MAX_STEP; ++step)
                {
                    float3 p = rayPos + rayDir * t;
                    float  d = SceneDistance(p);

                    if (d < PAO_MIN_DIST)
                        return float4(p, 1.0);              // 命中壳面

                    // 距离在变大且已离开包围体 -> 后面不可能再命中
                    if (step > 0 && d > prevD && d > minThickness * 2.0)
                        break;
                    prevD = d;

                    // 到最近那颗泡泡球面的距离（解析式，比再过一遍 SDF 便宜）
                    float minSurf = 1e9;
                    for (int j = 0; j < _ShapeDataCount; ++j)
                    {
                        ShapeData s = _ShapeDataBuffer[j];
                        minSurf = min(minSurf, length(p - s.posType.xyz) - s.sizeOp.x * 0.5);
                    }

                    float maxStep = PAO_MAX_STEP_SIZE;
                    if (minSurf < minThickness * 4.0)
                        maxStep = max(minThickness * 0.4, 1e-4);   // 贴壳：保守步长

                    t += clamp(d, minThickness * 0.3, maxStep);
                    if (t > tMax) break;
                }

                return float4(rayPos + rayDir * t, 0.0);        // 未命中
            }

            // ======================= 着色 =======================

            half4 FragSDF(Varyings i) : SV_Target
            {
                if (_BubbleEnabled < 0.5 || _ShapeDataCount <= 0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float  minThickness = SceneMinThickness();
                float2 uv  = i.uv;
                float2 ndc = uv * 2.0 - 1.0;
                ndc.y = -ndc.y;

                // ---- 场景深度：既用来反投影，也用来做遮挡 ----
                //
                // LinearEyeDepth 把原始深度变成「沿相机前向的视空间距离」。
                // 我们的光线是单位向量，而光线与相机前向的夹角余弦正好是
                // dot(rayDir, forward)，所以视空间深度 / 该余弦 = 沿光线的距离。
                // 这个换算在透视和正交下都成立。
                float rawDepth   = SampleSceneDepth(uv);
                float sceneEye   = LinearEyeDepth(rawDepth, _ZBufferParams);
                float depthValid = (rawDepth > 0.0 && rawDepth < 1.0 && sceneEye < 1e6) ? 1.0 : 0.0;

                float4 H = float4(ndc, rawDepth, 1.0);
                float4 D = mul(_IVP, H);
                float3 depthPos = D.xyz / D.w;

                // 深度图不可用时反投影会得到 NaN / 天文数字，退回远平面
                if (!(depthPos.x == depthPos.x) || dot(depthPos, depthPos) > 1e12 || rawDepth >= 1.0)
                {
                    H = float4(ndc, 1.0, 1.0);
                    D = mul(_IVP, H);
                    depthPos = D.xyz / D.w;
                }

                // ---- 构造光线 ----
                float3 rayPos;
                float3 rayDir;
                if (unity_OrthoParams.w > 0.0)   // unity_OrthoParams.w != 0 表示正交相机
                {
                    rayDir = normalize(mul((float3x3)unity_CameraToWorld, float3(0.0, 0.0, -1.0)));

                    // 正交：光线从近平面出发。从 depthPos 出发的话，
                    // 该点已经在实体表面上了，会从"实体内部"开始步进。
                    rayPos = _WorldSpaceCameraPos;
                }
                else
                {
                    rayPos = _WorldSpaceCameraPos;
                    rayDir = normalize(depthPos - rayPos);
                }

                // 遮挡：光线最多只能走到该像素的场景深度处。
                // 走到头 = 泡泡在实体后面 -> 判定未命中，泡泡被正确挡住。
                float tMax = PAO_MAX_DIST;
                if (_BubbleOcclusion > 0.5 && depthValid > 0.5 && !(_BubbleDebug > 0))
                {
                    float cosFwd = max(dot(rayDir, -unity_WorldToCamera[2].xyz), 1e-4);
                    tMax = max(sceneEye / cosFwd * 0.995, minThickness * 2.0);
                }

                float4 hit          = Raymarch(rayPos, rayDir, minThickness, tMax);
                float3 p            = hit.xyz;
                float  marched      = hit.w;

                // 步进是被 tMax 截断的 -> 泡泡在实体后面，直接丢弃
                if (tMax < PAO_MAX_DIST * 0.5 && dot(p - rayPos, rayDir) >= tMax * 0.99)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float dis = SceneDistance(p);

                // ---- 调试可视化（_BubbleDebug != 0 时短路，直接输出中间量）----
                if (_BubbleDebug > 0)
                {
                    if (_BubbleDebug == 1)   // 命中掩码：白 = 步进收敛到壳面
                        return half4(marched, marched, marched, 1.0);

                    if (_BubbleDebug == 2)   // 场景距离场（环绕显示，看形状对不对）
                        return half4(saturate(dis * 0.1 + 0.5).xxx, 1.0);

                    if (_BubbleDebug == 3)   // 命中点世界坐标（看位置和尺度）
                        return half4(frac(p * 0.25), 1.0);

                    if (_BubbleDebug == 4)   // 世界空间法线
                    {
                        float3 dn = SceneNormal(p, max(minThickness * 0.25, 1e-4)) * 0.5 + 0.5;
                        return half4(dn, 1.0);
                    }

                    if (_BubbleDebug == 5)   // 光线方向（看射线重建对不对）
                        return half4(rayDir * 0.5 + 0.5, 1.0);

                    if (_BubbleDebug == 6)   // 反投影深度（看深度重建对不对）
                        return half4(frac(depthPos * 0.01), 1.0);

                    if (_BubbleDebug == 7)   // 壳面收敛程度：黑 = 没贴近壳面
                        return half4(saturate(1.0 - abs(dis) / (minThickness * 2.0)).xxx, 1.0);
                }

                // 是否算「命中」用最终的 |SDF| 判断，所以即使步进次数用尽
                // 也能得到连续的软边，而不是硬切口。
                float surface = saturate(1.0 - abs(dis) / (minThickness * 2.0));
                if (marched < 0.5 && dis > minThickness * 2.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // ---- 几何 ----
                float3 n   = SceneNormal(p, max(minThickness * 0.25, 1e-4));
                float3 V   = -rayDir;
                float  NoV = saturate(dot(n, V));
                float  fresnel = pow(1.0 - NoV, max(_BubbleRimPower, 0.5));

                // ---- 薄膜干涉彩虹 ----
                // 真实泡泡膜厚 100~1000nm，光程差 ≈ 2·n·d·cosθ 随观察角变化，
                // 各波长交替相长 / 相消，于是形成一圈圈彩虹。
                // 这里用「相位随 (1-NoV) 增长」近似。
                float  phase = _BubbleIridescenceScale * (1.0 - NoV) * 3.6;
                float3 rainbow = palette(phase,
                                         float3(0.5, 0.5, 0.5),
                                         float3(0.5, 0.5, 0.5),
                                         float3(1.0, 1.0, 1.0),
                                         float3(0.0, 0.33, 0.67));
                float  iridMask = saturate(fresnel * 1.7) * _BubbleIridescence;

                // ---- 高光 ----
                float3 L   = normalize(_BubbleLightDir.xyz);
                float3 Hv  = normalize(L + V);
                float  spec  = pow(saturate(dot(n, Hv)), max(_BubbleSpecularPower, 1.0))
                             * _BubbleSpecularIntensity * _BubbleLightDir.w;
                // 一颗较弱的反向高光，加「玻璃球」质感
                float3 L2  = normalize(float3(-L.x, -L.y * 0.4, -L.z));
                float3 Hv2 = normalize(L2 + V);
                float  spec2 = pow(saturate(dot(n, Hv2)), max(_BubbleSpecularPower * 0.5, 1.0))
                             * _BubbleSpecularIntensity * 0.35 * _BubbleLightDir.w;

                // ---- 底色 ----
                float3 base       = SceneColor(p) * _BubbleTint.rgb;
                float  shapeAlpha = SceneShapeAlpha(p) * _BubbleTint.a;

                // 泡泡中心几乎全透，掠射角处才亮起来
                float3 col = base * 0.10;
                col += rainbow * iridMask * 1.3;
                col += float3(1.0, 1.0, 1.0) * (spec + spec2);
                col += float3(0.55, 0.75, 0.95) * fresnel * 0.30;

                float alpha = saturate(
                    shapeAlpha
                    * (0.18 + fresnel * 0.95 + spec + spec2)
                    * (0.5 + _BubbleOpacity)
                    * surface);

                // 未命中（走到步数上限）时压暗，避免糊成一片
                if (marched < 0.5)
                    alpha *= 0.30;

                // ---- 预乘 alpha 输出，交给 Pass 1 做 over ----
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }

        // ==================== Pass 1 : 合成 ====================
        Pass
        {
            Name "Composite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SourceTexture);
            SAMPLER(sampler_SourceTexture);

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

            half4 FragComposite(Varyings i) : SV_Target
            {
                // _MainTex: Pass 0 的结果（预乘 alpha）；_SourceTexture: 渲染前的画面
                float4 sdf = SAMPLE_TEXTURE2D(_MainTex,       sampler_MainTex,       i.uv);
                float4 src = SAMPLE_TEXTURE2D(_SourceTexture, sampler_SourceTexture, i.uv);

                float  a   = saturate(sdf.a);
                float3 col = sdf.rgb + src.rgb * (1.0 - a);   // sdf.rgb 已预乘

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
