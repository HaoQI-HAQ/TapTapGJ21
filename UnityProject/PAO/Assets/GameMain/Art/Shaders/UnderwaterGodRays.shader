Shader "PAO/UnderwaterGodRays"
{
    // -----------------------------------------------------------------------
    // 体积光：水面亮斑往下投出的一束束光柱（半分辨率独立一趟）
    //
    // =======================================================================
    // 【★ 这一版和上一版的根本区别 —— 形状从哪来】
    //
    // 上一版：沿视线步进，每步采样一个 3D 噪声当亮度。
    //   结果是**均匀的体积雾**，不是"一束一束"。原因是每条光线在几十米里
    //   总会穿过好几块噪声，平均下来就平了 ——
    //   噪声场里根本没有"一束"这个概念，怎么调参数都调不出来。
    //
    // 这一版：把**焦散网沿垂直方向投影下来**当光源。
    //
    //   真实水下光柱的成因：水面被波浪折射，把阳光聚成一张会动的亮网；
    //   亮的地方往下投出光柱，暗的地方就是暗的。
    //   所以光柱的**边界是水面的形状**，不是空间里的噪声。
    //
    //   两条关键性质，缺一条就不像：
    //     · 光柱沿自己的竖直方向**不变**（所以才叫"柱"）
    //     · 光柱横向上的明暗，和抬头看到的水面亮斑**一一对应**
    //
    //   实现：射线打到水面 → 拿到命中点的水面光照强度 → 那束光的强度
    //   就定死了 → 沿路往下累加时**用同一个强度**，不再随采样位置变。
    //
    // 额外收益：光柱和地板焦散现在是**物理自洽**的 ——
    // 地板上的亮斑正好在光柱底下，因为它们本来就是同一张网的两种投影。
    // 上一版做不到这一点，光柱和焦散各画各的。
    // =======================================================================
    //
    // 【为什么仍然单独成一趟、跑半分辨率】
    //   光柱是十几米厚的体积，边缘本来就是晕开的，半分辨率靠双线性上采样
    //   补回来，肉眼分辨不出。而这一趟每像素要走十几步，全分辨率就是白烧一半算力。
    //
    // 【★ 从 _PAO_ColorTexture 读，写相机颜色】
    //   和 UnderwaterFullscreen 的另外两趟一样遵守同一条规则：
    //   输入必须是纹理，不能是相机颜色本身。原因见那个文件顶部的说明。
    // -----------------------------------------------------------------------

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "UnderwaterGodRays"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGodRays
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "UnderwaterInclude.hlsl"

            // 画面备份（半分辨率的那一份）
            TEXTURE2D(_PAO_ColorTexture);
            SAMPLER(sampler_PAO_ColorTexture);

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

            /// 光线和水面（y = 水面高度）的交点。相机在水下时必然存在。
            ///
            /// 【为什么要单独算这个】
            ///   这是「光柱形状从水面来」这条思路的入口 ——
            ///   没有这个交点，就不知道该采水面上的哪个位置。
            float3 RayWaterHit(float3 rayOrigin, float3 rayDir, float waterLevel)
            {
                // 【不能写成 (waterLevel - rayOrigin.y) / rayDir.y】
                //   rayDir.y 是浮点数，方向几乎水平时会算出巨大的值或 NaN，
                //   整条光线就废了，而且不会报错。夹一下最省事。
                float dy = max(rayDir.y, 0.05);

                float t = (waterLevel - rayOrigin.y) / dy;

                return rayOrigin + rayDir * max(t, 0.0);
            }

            float4 FragGodRays(Varyings input) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_PAO_ColorTexture,
                                              sampler_PAO_ColorTexture,
                                              input.uv);

                float3 cameraPos = GetCameraPositionWS();

                float amount = PAO_UnderwaterAmount(cameraPos) * _PAO_UnderwaterStrength;

                // 相机在水面之上时视野里没有水面，也就没有「从水面漏下来的光」
                if (_PAO_GodRayEnabled < 0.5 || amount < 0.001)
                {
                    return src;
                }

                float waterLevel = PAO_WaterLevel();
                if (cameraPos.y >= waterLevel)
                {
                    return src;
                }

                float rawDepth = SampleSceneDepth(input.uv);

                float sky = PAO_IsSky(rawDepth);
                float eyeDepth = PAO_LinearEyeDepth(rawDepth);

                // ---- 重建射线方向 ----
                //
                // 和 BubbleTide.shader 用的是同一套做法：从相机矩阵现算，
                // 不依赖深度纹理。那种「从深度反推方向」的写法在拿不到
                // 有效深度时会静默出错，整条射线都是错的，极难查。
                float2 ndc = input.uv * 2.0 - 1.0;

                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float3 camFwd   = -UNITY_MATRIX_V[2].xyz;

                float3 rayDir;
                if (unity_OrthoParams.w > 0.0)
                {
                    rayDir = camFwd;
                }
                else
                {
                    rayDir = normalize(camFwd
                                     + camRight * (ndc.x * unity_CameraProjection[0][0])
                                     + camUp    * (ndc.y * unity_CameraProjection[1][1]));
                }

                // ---- 这束光的强度，由水面决定 ----
                //
                // 【这一步是整个效果的核心】
                //   水面命中点采样一次焦散图案，得到的值就是这束光从水面带下来的能量。
                //   它会沿整条射线保持不变 —— 这正是「柱」的定义。
                float3 waterHit = RayWaterHit(cameraPos, rayDir, waterLevel);

                float beam = PAO_SampleCaustics(waterHit, PAO_WaterTime());

                // ---- 调试：直接看光柱强度 ----
                if (_PAO_DebugMode > 4.5 && _PAO_DebugMode < 5.5)
                {
                    return float4(beam.xxx, 1.0);
                }

                // 【为什么必须做对比度压缩，而且不能省】
                //   焦散图案本身的动态范围很大（暗区接近 0，亮脉可以到 1）。
                //   直接拿来当光柱亮度，会有一半的屏幕是全黑的 —— 因为大部分
                //   视线打到的水面位置本来就在暗区。
                //
                //   压到 [floor, 1] 之后，暗区仍然有微光（水下永远不是纯黑），
                //   亮区保持突出。0.35 是「暗处还看得见但不抢戏」的量。
                beam = lerp(0.35, 1.0, saturate(beam));

                // 光柱的角度扩散：越靠近太阳方向越亮。
                // 这一层让光柱「有来处」，而不是凭空均匀地罩下来。
                float3 sunDir = normalize(_PAO_SunDirection.xyz);
                float sunAlign = saturate(dot(rayDir, sunDir));
                beam *= lerp(1.0, 1.7, sunAlign * _PAO_GodRaySpread);

                // ---- 沿视线步进累加 ----
                //
                // 【形状已经定好了，这里只负责衰减和体积感】
                //   beam 沿整条射线是常数，所以循环里除了「往下走多远」
                //   之外不需要再采样任何噪声 —— 比上一版少 8 次哈希/步。
                float waterAbove = waterLevel - cameraPos.y;      // 相机到水面的垂直距离

                float maxDistance = lerp(100000.0, eyeDepth / max(0.05, dot(rayDir, camFwd)) + 0.05,
                                         1.0 - sky);
                float totalDist = min(maxDistance, _PAO_GodRayDistance);

                // 从相机和水面之间的一半开始走 —— 贴着相机的头几米在屏幕上
                // 是近裁剪面附近的糊块，加了反而脏。
                float startDist = waterAbove * 0.35;

                if (startDist >= totalDist)
                {
                    return src;
                }

                int steps = (int)clamp(_PAO_GodRaySteps, 1.0, 64.0);
                float stepSize = (totalDist - startDist) / float(steps);

                float accum = 0.0;

                [loop] for (int i = 0; i < steps; ++i)
                {
                    // 抖动起点：把规则的步进噪点打散成高频噪点。
                    // 人眼对条纹比对方块敏感，所以这一步很值。
                    float jitter = frac(sin(dot(float2(i, input.uv.x + input.uv.y),
                                                float2(12.9898, 78.233))) * 43758.5453);

                    float dist = startDist + (float(i) + jitter) * stepSize;
                    float3 samplePos = cameraPos + rayDir * dist;

                    // 走到水面上方就别加了 —— 那只可能是从侧面穿出去
                    if (samplePos.y > waterLevel)
                    {
                        continue;
                    }

                    // 越往下越暗：光在水里被吸收。
                    // 用平方而不是线性，因为衰减在头几米最快，之后趋缓。
                    float depthFalloff = 1.0 - dist / max(totalDist, 1e-4);
                    depthFalloff *= depthFalloff;

                    accum += depthFalloff;
                }

                // 归一化：让结果和步数无关。改步数只改质量，不改亮度 ——
                // 不然调步数会连带把亮度也改掉，参数之间互相打架。
                float volumetric = accum / float(steps);

                float rays = beam * volumetric * _PAO_GodRayIntensity;

                // 用浅处水色给光柱染色：水下的光不是白的，是被水滤过的。
                float3 color = src.rgb + _PAO_ShallowColor.rgb * rays * amount;

                return float4(color, src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
