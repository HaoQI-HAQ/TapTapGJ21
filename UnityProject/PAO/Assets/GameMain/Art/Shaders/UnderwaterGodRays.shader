Shader "PAO/UnderwaterGodRays"
{
    // -----------------------------------------------------------------------
    // 体积光（单独一趟，跑在半分辨率上）
    //
    // -----------------------------------------------------------------------
    // 【为什么它必须单独成一趟，不能并进雾那一趟】
    //
    // 雾是「一个像素一次深度采样 + 一个 lerp」，放在全分辨率上几乎免费。
    // 体积光是「一个像素 14 步循环 × 每步一个 3D FBM（8 次哈希）」——
    // 全分辨率下这是整个水下效果里最贵的一项，比其余全部加起来还贵。
    //
    // 而它恰好又是**最不需要全分辨率**的一项：
    // 光柱是十几米厚的体积，边缘本来就是晕开的，
    // 半分辨率下靠双线性上采样补回来，肉眼分辨不出差别。
    //
    // 所以拆开：雾（全分辨率，便宜）与体积光（半分辨率，贵）分开跑。
    // 省下来的正好是一半算力，观感损失约等于零。
    //
    // -----------------------------------------------------------------------
    // 【★ 从 _PAO_ColorTexture 读，写相机颜色】
    //
    // 和 UnderwaterFullscreen 的另外两趟一样，遵守同一条规则 ——
    // 输入必须是纹理，不能是相机颜色本身。原因见那个文件顶部的说明。
    //
    // 这里读的是**半分辨率**的画面备份，写进全分辨率的相机目标。
    // 这一升采样是双线性的（备份用 FilterMode.Bilinear），等于是免费的抗锯齿。
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

            // 画面备份。半分辨率的那一份。
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

            float4 FragGodRays(Varyings input) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D(_PAO_ColorTexture,
                                              sampler_PAO_ColorTexture,
                                              input.uv);

                float3 cameraPos = GetCameraPositionWS();

                float amount = PAO_UnderwaterAmount(cameraPos) * _PAO_UnderwaterStrength;

                if (_PAO_GodRayEnabled < 0.5 || amount < 0.001)
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

                // 光线最多走到第一个挡住它的东西 —— 墙后面不该有光柱。
                //
                // 天空像素要传一个大数：那是「没打到任何东西」，不能用深度
                // 限制它。PAO_GodRays 内部还有自己的最大步进距离
                // （_PAO_GodRayDistance），所以这里给个大数不会失控。
                float cosAngle = max(0.05, dot(rayDir, camFwd));
                float maxDistance = lerp(100000.0, eyeDepth / cosAngle + 0.05, 1.0 - sky);

                float3 sunDir = normalize(_PAO_SunDirection.xyz);

                float rays = PAO_GodRays(cameraPos, rayDir, maxDistance,
                                         cameraPos, PAO_WaterTime());

                // 顺着太阳方向往上看，光柱最亮 ——
                // 这一层额外强调是让光柱「有来处」的关键。
                rays *= lerp(1.0, 1.6, saturate(dot(rayDir, sunDir)) * _PAO_GodRaySpread);

                // 用浅处水色给光柱染色：水下的光不是白的，是被水滤过的。
                float3 color = src.rgb + _PAO_ShallowColor.rgb * rays * amount;

                return float4(color, src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
