// ---------------------------------------------------------------------------
// PAO / Environment / Lava —— 卡通岩浆
//
// 视觉目标：
//   · 深色结壳一块块浮在橙红岩浆上，块与块之间是发亮的缝
//   · 大面积色块（不是写实噪声），远看也能读出"岩浆"的暖色
//   · 颜色自带亮度，不依赖 Bloom —— 工程里的场景没有全局 Volume
//
// 技术点：
//   · 世界坐标三平面投影（PAO_ENV_PlanarUV），任意大小的面密度一致
//   · Voronoi F2-F1 切结壳：块内部大、块缝接近 0，比 F1 阈值干净
//   · 两层 FBM 反向流动，避免"整张图在平移"的廉价感
//
// 用法：直接拖到平面上。_Scale 越小，结壳块越大。
// ---------------------------------------------------------------------------
Shader "PAO/Environment/Lava"
{
    Properties
    {
        [Header(Color)]
        _DarkColor ("暗部（冷却表面）", Color) = (0.10, 0.008, 0.004, 1)
        _MidColor ("主色（橙红岩浆）", Color) = (0.78, 0.12, 0.012, 1)
        _HotColor ("高温（亮黄）", Color) = (1.0, 0.55, 0.08, 1)
        _CrustColor ("结壳（黑褐）", Color) = (0.035, 0.012, 0.008, 1)

        [Header(Shape)]
        _Scale ("纹理尺度（越小块越大）", Range(0.005, 0.5)) = 0.22
        _FlowSpeed ("流动速度", Range(0, 3)) = 0.30
        _CrustAmount ("结壳面积", Range(0, 1)) = 0.80
        _CrustEdge ("结壳边缘", Range(0.005, 0.3)) = 0.06
        _CrackWidth ("裂纹宽度", Range(0.002, 0.2)) = 0.03
        _CrackGlow ("裂纹发光", Range(0, 5)) = 0.7
        _Bands ("卡通色阶数", Range(1, 8)) = 4
        _BandStrength ("色阶强度", Range(0, 1)) = 0.55

        [Header(Glow)]
        _Emission ("发光强度", Range(0, 8)) = 1.2
        _PulseSpeed ("脉动速度", Range(0, 5)) = 1.2
        _PulseAmount ("脉动幅度", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 200

        // 双面：岩浆池被从下方看、或者做成天花板时都不会整块消失
        Cull Off

        // ==================== 主 Pass ====================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Blend Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma multi_compile_fog

            #include "PAO_LavaCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            Varyings LavaVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor  = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 LavaFragment(Varyings input) : SV_Target
            {
                float2 p = PAO_ENV_PlanarUV(input.positionWS, normalize(input.normalWS)) * _Scale;
                float t = _Time.y * _FlowSpeed;

                // 两层反向流动：一层慢慢推开，一层快速打碎
                float n1 = PAO_ENV_Fbm(p + float2(t, t * 0.35));
                float n2 = PAO_ENV_Fbm(p * 2.1 + float2(-t * 0.6, t * 0.85) + n1 * 0.7);
                float heat = saturate(n1 * 0.6 + n2 * 0.4);

                // 卡通分层：把连续的 heat 压成几档，远看更像手绘色块
                float bands = max(1.0, floor(_Bands + 0.5));
                float bandedHeat = floor(heat * bands) / bands;
                bandedHeat = lerp(heat, bandedHeat, _BandStrength);

                float3 color = lerp(_DarkColor.rgb, _MidColor.rgb, smoothstep(0.12, 0.55, bandedHeat));
                color = lerp(color, _HotColor.rgb, smoothstep(0.58, 0.92, bandedHeat));

                // 结壳：Voronoi 的块内 F2-F1 大，块缝接近 0。
                // 先用低频噪声把格子推歪，避免出现规整的六边形网格。
                float2 warp = float2(n1 - 0.5, n2 - 0.5) * float2(0.6, 0.3);
                float2 voro = PAO_ENV_Voronoi(p * 0.85 + warp + float2(t * 0.22, -t * 0.18));
                float border = voro.y - voro.x;
                float crackWidth = max(0.005, _CrackWidth * 0.4);
                float plate = smoothstep(crackWidth, crackWidth + 0.03 + _CrustEdge * 0.3, border);
                float crack = 1.0 - plate;

                // 只有一部分区域结壳，剩下的保持熔融，避免整片发黑
                float crustMask = smoothstep(0.32, 0.72, n1 + n2 * 0.25);
                float3 crustShade = lerp(_CrustColor.rgb, _DarkColor.rgb, smoothstep(0.25, 0.75, n2));
                color = lerp(color, crustShade, plate * crustMask * _CrustAmount);

                // 缝隙里的高温岩浆：越热的地方缝越亮
                float crackGlow = pow(saturate(crack), 2.2) * (0.55 + heat * 0.75);
                color += _HotColor.rgb * crackGlow * _CrackGlow;

                // 缓慢的呼吸感
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;
                color *= _Emission * pulse;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ==================== 深度 ====================
        // 工程里 SSAO 开着，缺了这两个 Pass 会被当成"没有深度/法线"的对象
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #include "PAO_LavaCore.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ==================== 深度 + 法线 ====================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "PAO_LavaCore.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    // 兜底：URP 没起来时至少不是粉红
    FallBack "Universal Render Pipeline/Unlit"
}
