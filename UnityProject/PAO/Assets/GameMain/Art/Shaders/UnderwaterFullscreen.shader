Shader "PAO/UnderwaterFullscreen"
{
    // -----------------------------------------------------------------------
    // 水下全屏效果（焦散 + 深度雾），三趟：
    //
    // Pass 0 "Caustics"          : 用场景深度重建世界坐标与法线，算出焦散量，
    //                              写进一张半分辨率的中间纹理。
    // Pass 1 "CausticsComposite" : 把焦散加法混到画面上，写回相机颜色。
    // Pass 2 "Fog"               : 深度雾 + 焦散补色，写回相机颜色。
    //
    // 体积光是独立的一个 shader（UnderwaterGodRays.shader），跑在半分辨率上。
    //
    // -----------------------------------------------------------------------
    // 【为什么拆成三趟、注入点还隔得那么远】
    //
    // 焦散必须**长在几何体上**（参考图里地面的光斑是长在地板上的，不是浮在
    // 画面最上层的一层膜），所以它得在透明物体之前跑 —— 那时候画面里只有
    // 不透明几何体和深度，正好是投影需要的状态。
    //
    // 雾反过来，必须**罩在所有东西之上**（包括泡泡、潮水这些透明物体），
    // 否则泡泡会是「泡在干净空气里」，一眼穿帮。所以它排在透明物体之后。
    //
    // 中间隔着整个透明渲染阶段，两段共享不了寄存器，只能走纹理。
    // -----------------------------------------------------------------------
    // 【★ 所有 Pass 都遵守同一条规则：从纹理读，写相机颜色】
    //
    // 这条规则不是风格问题，是硬性要求：
    //
    //   · 渲染到 RT 和渲染到相机颜色，两者的 UV **Y 方向是相反的**
    //     （Unity 的 RT 约定）。这个差异由 Unity 的全屏三角形辅助函数
    //     GetFullScreenTriangleTexCoord 通过 _ProjectionParams.x 自动补偿。
    //     所以「从纹理读 + 写相机颜色」这个组合天然是对的。
    //
    //   · 反过来说，**不能让输入也指向相机颜色**。那样 UV 约定不成立，
    //     而且同一张 RT 既读又写，在部分平台上是未定义行为。
    //     更要紧的是：C# 那边的 Blitter.BlitCameraTexture 在这种用法下会直接抛
    //     Assertion failed + ArgumentNullException 并每帧刷屏。
    //     —— 这个坑实际踩过一遍，别改回去。
    //
    // 所以 C# 侧永远先准备一份 _PAO_ColorTexture（画面备份），这里只从它读。
    // -----------------------------------------------------------------------

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        // ==================== Pass 0 : 焦散投影 ====================
        //
        // 输出到 _PAO_CausticsTexture（不是相机颜色）：
        //   R = 焦散量（0~1 以上）
        //   G = 该像素的视距 / 20（米，归一化）
        //
        // 【为什么要在 G 里存视距】
        //   这一趟跑在透明物体之前，画面里只有不透明几何体。
        //   到了最后一趟（透明物体之后），如果无脑把焦散加回去，
        //   它就会**穿透**所有的泡泡和玻璃 —— 近处的泡泡上浮着地板的亮纹。
        //   存下视距，最后一趟就能比一比「这个像素还是原来那个表面吗」。
        //   20 米量程、half 精度下分辨率约 0.02 米，够用。
        Pass
        {
            Name "UnderwaterCaustics"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCaustics
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "UnderwaterInclude.hlsl"

            // 【注意】这一趟不读画面 —— 焦散是「凭空算出来的光」，
            // 不需要原画面。少一次全屏采样，比后两趟便宜得多。

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

            /// 屏幕 uv + 原始深度 → 世界坐标。
            ///
            /// 【为什么用 _PAO_CameraInvVP，而不是在 shader 里自己拼】
            ///   URP 的相机颜色目标可能被平台翻转（D3D 和 GL 的 Y 方向不同），
            ///   而 blit 的 uv 已经在采样器里被处理过一次。
            ///   唯一稳妥的做法是让 C# 送进来一个「和当前相机、当前平台一致」的
            ///   逆矩阵。见 UnderwaterCameraUtil.SetCameraMatrices。
            float3 ReconstructWorldPos(float2 uv, float rawDepth)
            {
                float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                float4 worldH = mul(_PAO_CameraInvVP, clip);
                return worldH.xyz / worldH.w;
            }

            /// 从深度反推世界法线。
            ///
            /// 【为什么必须做这一步】
            ///   焦散是「从上面打下来的光」，物理上只出现在朝上的面。
            ///   如果法线写死成 (0,1,0)，墙壁和天花板也会长出光斑 ——
            ///   玩家一眼就看出这是屏空间滤镜，不是光。
            ///
            ///   对地板 / 墙 / 天花板这种大平面，深度的屏幕空间梯度已经足够准，
            ///   不需要 GBuffer 法线（省一张纹理和一次采样）。
            float3 ReconstructWorldNormal(float2 uv, float rawDepth, float3 worldPos)
            {
                float2 texel = _PAO_CameraBufferSize.zw;

                float3 right = ReconstructWorldPos(uv + float2(texel.x, 0.0), rawDepth);
                float3 up    = ReconstructWorldPos(uv + float2(0.0, texel.y), rawDepth);

                float3 n = cross(right - worldPos, up - worldPos);

                float len = length(n);
                if (len < 1e-8)
                {
                    return float3(0.0, 1.0, 0.0);
                }

                n /= len;

                // 统一朝上翻。地板和天花板都会正确，墙面的水平分量也保留。
                return n.y < 0.0 ? -n : n;
            }

            float2 FragCaustics(Varyings input) : SV_Target
            {
                if (_PAO_CausticsEnabled < 0.5 || _PAO_UnderwaterStrength < 0.001)
                {
                    return float2(0.0, 0.0);
                }

                float rawDepth = SampleSceneDepth(input.uv);

                // 天空盒上没有几何体：反推出来的世界坐标是垃圾，
                // 而且天空也不该有焦散。
                if (PAO_IsSky(rawDepth) > 0.5)
                {
                    return float2(0.0, 0.0);
                }

                float eyeDepth = PAO_LinearEyeDepth(rawDepth);

                float3 worldPos = ReconstructWorldPos(input.uv, rawDepth);
                float3 normal   = ReconstructWorldNormal(input.uv, rawDepth, worldPos);

                float caustic = PAO_SampleCaustics(worldPos, PAO_WaterTime());

                float visibility = PAO_CausticsVisibility(normal, eyeDepth);

                float amount = saturate(caustic * visibility * _PAO_CausticsIntensity
                                        * _PAO_UnderwaterStrength);

                return float2(amount, eyeDepth / 20.0);
            }
            ENDHLSL
        }

        // ==================== Pass 1 : 焦散合成 ====================
        //
        // 把中间纹理里的焦散**加法**混回画面：
        //   · 焦散是「额外的光」，不是「替换掉的颜色」，所以是加不是 lerp
        //   · 中间纹理的 G 通道存的是视距，用 Blend One One 会连它一起
        //     加到绿色上（地板会整体发绿）。所以手动做混合：
        //     Blend One Zero，自己拿输入的 alpha 还原相机 alpha。
        Pass
        {
            Name "UnderwaterCausticsComposite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCausticsComposite
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UnderwaterInclude.hlsl"

            TEXTURE2D(_PAO_ColorTexture);
            SAMPLER(sampler_PAO_ColorTexture);

            TEXTURE2D(_PAO_CausticsTexture);
            SAMPLER(sampler_PAO_CausticsTexture);

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

            float4 FragCausticsComposite(Varyings input) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_PAO_ColorTexture,
                                              sampler_PAO_ColorTexture,
                                              input.uv);

                float caustic = SAMPLE_TEXTURE2D(_PAO_CausticsTexture,
                                                 sampler_PAO_CausticsTexture,
                                                 input.uv).r;

                float3 rgb = src.rgb + _PAO_CausticsColor.rgb * caustic;

                return float4(rgb, src.a);
            }
            ENDHLSL
        }

        // ==================== Pass 2 : 深度雾 ====================
        //
        // 全分辨率。它很便宜：一次深度采样 + 一个 lerp。
        // 体积光不在这里 —— 它贵得多，而且半分辨率看不出差别，
        // 所以单独成一趟（UnderwaterGodRays.shader）。
        Pass
        {
            Name "UnderwaterFog"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragFog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "UnderwaterInclude.hlsl"

            TEXTURE2D(_PAO_ColorTexture);
            SAMPLER(sampler_PAO_ColorTexture);

            // 焦散那一趟算好的量（R）+ 当时的视距（G）。
            // 这里只补一点亮度，让焦散也能进 Bloom —— 参考图里地面亮纹有柔光。
            TEXTURE2D(_PAO_CausticsTexture);
            SAMPLER(sampler_PAO_CausticsTexture);

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

            /// 屏幕 uv + 原始深度 → 世界坐标。
            /// 和 Pass 0 里同名函数完全一致 —— 调试模式要靠它验证
            /// 「世界坐标到底重建对没有」，两边不一致的话调试结果就没意义了。
            float3 ReconstructWorldPos(float2 uv, float rawDepth)
            {
                float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                float4 worldH = mul(_PAO_CameraInvVP, clip);
                return worldH.xyz / worldH.w;
            }

            float4 FragFog(Varyings input) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_PAO_ColorTexture,
                                              sampler_PAO_ColorTexture,
                                              input.uv);

                float3 cameraPos = GetCameraPositionWS();

                // 相机在水面之上、而且过渡带也过了 —— 什么都不做。
                // 这不是性能优化，是正确性：站在岸上时画面不该有任何水下残留。
                float amount = PAO_UnderwaterAmount(cameraPos) * _PAO_UnderwaterStrength;

                if (amount < 0.001)
                {
                    return src;
                }

                float rawDepth = SampleSceneDepth(input.uv);

                float sky = PAO_IsSky(rawDepth);
                float eyeDepth = PAO_LinearEyeDepth(rawDepth);

                // 天空没有几何体，用视距的远景当成它的深度。
                // 不这么做的话天空盒会是**唯一一块没被雾染色的地方**，
                // 在青绿的画面里就是一个刺眼的洞。
                float fogDepth = lerp(eyeDepth, 300.0, sky);

                // ---- 深度雾 ----
                //
                // 指数衰减：1 - exp(-density * d)
                // 用这个而不是线性雾，是因为指数雾在相机附近几乎全透、
                // 远处迅速饱和 —— 正是水下「近处清楚、五米外就糊了」的手感。
                float fogFactor = 1.0 - exp(-_PAO_FogDensity * fogDepth);

                // 相机越深，水色越往深处那一档走
                float cameraDepth = max(0.0, -PAO_HeightAboveWater(cameraPos));
                float depthMix = saturate(cameraDepth / 18.0);

                float3 waterTint = lerp(_PAO_ShallowColor.rgb, _PAO_DeepColor.rgb, depthMix);

                // 「越深越暗」：单独一层压暗，和距离雾是两回事
                float darken = 1.0 / (1.0 + _PAO_DepthDarkening * cameraDepth * 0.02);
                waterTint *= darken;

                float f = saturate(fogFactor * amount);

                float3 color = lerp(src.rgb, waterTint, f);

                // ---- 焦散补色 ----
                //
                // Pass 0 那一趟跑在透明物体之前，画面里还只有不透明几何体。
                // 到了这里（透明物体之后），如果无脑把焦散加回去，它就会
                // **穿透**所有的泡泡和玻璃 —— 近处的泡泡上浮着地板的亮纹。
                //
                // 所以 Pass 0 顺手存了那一趟看到的视距（G 通道）。
                // 现在比一比：如果这个像素的深度和当时基本一致，说明它还是
                // 个不透明几何表面，可以补色；差得多就是被透明物体盖住了。
                float2 stored = SAMPLE_TEXTURE2D(_PAO_CausticsTexture,
                                                 sampler_PAO_CausticsTexture,
                                                 input.uv).rg;

                float storedDepth = stored.g * 20.0;

                // 容差随距离放大：远处像素本身精度就低，
                // 用固定阈值会让远处的焦散整片消失。
                float tolerance = max(0.08, eyeDepth * 0.012);

                float surfaceMatch = 1.0 - saturate(abs(storedDepth - eyeDepth) / tolerance);

                color += _PAO_CausticsColor.rgb * stored.r * 0.35 * surfaceMatch * amount;

                // ---- 调试可视化 ----
                //
                // 【为什么放在最后这一趟，而不是焦散那一趟】
                //   焦散那一趟的输出是中间纹理，不是画面 —— 在那里画什么都看不见。
                //   只有这一趟是全屏最后一道，它的输出才是玩家/你看到的东西。
                //
                // 【为什么背景用非黑的颜色】
                //   深灰底 (0.12) 而不是纯黑：这样「没算出来」和「算出来是 0」
                //   能区分开。纯黑底的话两种情况长得一模一样，等于没调试。
                if (_PAO_DebugMode > 0.5)
                {
                    return float4(DebugCaustics(input, rawDepth, eyeDepth, stored, surfaceMatch), 1.0);
                }

                return float4(color, src.a);
            }

            /// 调试输出。每种模式对应一类失败原因，见 UnderwaterInclude 里的说明。
            float3 DebugCaustics(Varyings input,
                                 float rawDepth,
                                 float eyeDepth,
                                 float2 stored,
                                 float surfaceMatch)
            {
                float3 background = 0.12;   // 深灰：能区分「没算出来」和「算出来是 0」

                if (_PAO_DebugMode < 1.5)
                {
                    // 1 = 焦散量（绿通道里那一份）→ 应该看到一张会动的亮网
                    return stored.r * float3(0.3, 1.0, 0.4);
                }

                if (_PAO_DebugMode < 2.5)
                {
                    // 2 = 表面匹配度 → 白 = 这个像素还是原来的几何表面
                    //     全黑就说明被透明物体盖住了（或者深度对不上）
                    return surfaceMatch * float3(0.4, 0.8, 1.0);
                }

                if (_PAO_DebugMode < 3.5)
                {
                    // 3 = 视距（米 / 20）→ 应该是一层随距离变亮的灰
                    return (eyeDepth / 20.0) * float3(1.0, 0.9, 0.5);
                }

                if (_PAO_DebugMode < 4.5)
                {
                    // 4 = 焦散图案在世界里的平铺 → 
                    //     应该看到规则的格子；糊成一片或者条纹化 = 世界坐标重建有问题
                    float3 worldPos = ReconstructWorldPos(input.uv, rawDepth);
                    float2 cell = frac(worldPos.xz / max(_PAO_CausticsScale, 0.01));

                    float grid = step(cell.x, 0.06) + step(cell.y, 0.06);
                    grid += step(0.94, cell.x) + step(0.94, cell.y);

                    float3 base = float3(cell.x, cell.y, 0.35) * 0.55;
                    return base + saturate(grid) * float3(0.45, 0.45, 0.45);
                }

                // 5 = 相机是否在水下 / 水下程度
                //     （在体积光那一趟里有更直接的版本，这里给个红的兜底）
                return float3(0.7, 0.1, 0.1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
