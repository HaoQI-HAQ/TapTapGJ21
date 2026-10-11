// ---------------------------------------------------------------------------
// PAO / ModelPBR —— 金属度与粗糙度分开的 PBR 模型 shader
//
// 为什么不用 URP 自带的 Universal Render Pipeline/Lit：
//   URP Lit 要求金属度和光滑度打在同一张图的 R / A 通道里
//   （Metallic = R，Smoothness = A，而且 A 存的是"光滑度"不是"粗糙度"）。
//   美术给的是 Metallic / Roughness 两张独立的灰度图，没有 AO，
//   Height 图也基本是纯色（实测 2048² 全是 127，flat = 1.00），
//   所以这里按"金属度图 + 粗糙度图分开"来写，Smoothness = 1 - Roughness。
//
// 每个材质的 5 个贴图插槽与导入设置：
//   _BaseMap       *_BaseColor.png     sRGB 开；Jar 的这张自带 alpha
//   _NormalMap     *_Normal.png        Texture Type = Normal map
//   _MetallicMap   *_Metallic.png      sRGB 关，Single Channel = Red
//   _RoughnessMap  *_Roughness.png     sRGB 关，Single Channel = Red
//   _EmissionMap   *_Emissive.png      sRGB 开（只有 Jar / Jellyfish 有）
//   另有一个可选的 _OpacityMap（*_op.png），默认不用，见 ModelPBRInput.hlsl
//
// 四个 Pass：
//   ForwardLit / ShadowCaster / DepthOnly / DepthNormals
//   没有 GBuffer Pass —— 工程里三个 URP Renderer 的 m_RenderingMode 都是 0（Forward）
//
// 透明材质（比如玻璃罐 Jar）在材质面板上要另外改三处，shader 管不了：
//   Render Queue = 3000，勾 _TransparentMode，_ZWrite = 0，
//   混合用 SrcAlpha / OneMinusSrcAlpha
// ---------------------------------------------------------------------------
Shader "PAO/ModelPBR"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("基础色 BaseColor", 2D) = "white" {}
        [MainColor]   _BaseColor ("颜色叠加（A = 整体透明度）", Color) = (1, 1, 1, 1)

        [Header(Normal)]
        [Normal][NoScaleOffset] _NormalMap ("法线 Normal", 2D) = "bump" {}
        _NormalScale ("法线强度", Range(0, 2)) = 1

        [Header(Metallic and Roughness)]
        [NoScaleOffset] _MetallicMap ("金属度 Metallic（R，默认全黑 = 非金属）", 2D) = "black" {}
        _Metallic ("金属度倍数", Range(0, 1)) = 1
        [NoScaleOffset] _RoughnessMap ("粗糙度 Roughness（R）", 2D) = "white" {}
        _Roughness ("粗糙度倍数", Range(0, 2)) = 1

        [Header(Emission)]
        [Toggle(_EMISSION)] _EmissionEnabled ("启用自发光", Float) = 0
        [HDR] _EmissionColor ("自发光颜色（可 >1 推亮亮点）", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("自发光 Emissive", 2D) = "black" {}

        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha 裁剪（镂空用）", Float) = 0
        _Cutoff ("裁剪阈值", Range(0, 1)) = 0.5
        [Toggle(_OPACITYMAP)] _UseOpacityMap ("用 op 贴图作为不透明度", Float) = 0
        [NoScaleOffset] _OpacityMap ("不透明度 op（默认不用）", 2D) = "white" {}
        _Opacity ("整体不透明度", Range(0, 1)) = 1

        [Header(Render State)]
        [Toggle(_SURFACE_TYPE_TRANSPARENT)] _TransparentMode ("透明模式（同时把 Render Queue 设 3000）", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("源混合", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("目标混合", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("深度写入", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除模式", Float) = 2

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _OutlineEnabled ("启用描边（反向外壳）", Float) = 1
        _OutlineColor ("描边颜色", Color) = (0.10, 0.12, 0.20, 1)
        _OutlineWidth ("描边宽度（模型本地单位，本模型最长边约 6.7）", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"           = "Opaque"
            "RenderPipeline"       = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector"      = "True"
        }
        LOD 300

        // ==================== 描边（反向外壳，最先画） ====================
        // Cull Front 只画背面，沿法线外扩出一圈壳；主 Pass 的正面比壳更近，
        // 于是壳只在轮廓之外露出来，就是描边。和 PAO/BubbleBall 一个路数。
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #pragma shader_feature_local _OUTLINE_ON
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            OutlineVaryings OutlineVertex(OutlineAttributes input)
            {
                OutlineVaryings output = (OutlineVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                #if defined(_OUTLINE_ON)
                    // 沿法线在「物体本地空间」外扩。
                    // 用本地空间而不是世界空间，是为了让粗细自动跟模型尺寸成比例：
                    // 这个模型从 Maya 出来是 6.72 个本地单位，_OutlineWidth = 0.05
                    // 就是最长边的约 0.74%，换导入缩放或换机器都不会跑偏。
                    // 代价：物体被非等比缩放时描边会跟着某个轴变粗。BubbleBall 里的泡泡
                    // 会被拉伸，所以那边改用世界空间外扩；这里是整体缩放的模型，本地空间更稳。
                    float3 positionOS = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                    output.positionCS = TransformObjectToHClip(positionOS);
                    output.fogFactor = ComputeFogFactor(output.positionCS.z);
                #else
                    // 描边关掉时把所有顶点收到同一个裁剪空间点，三角形面积为 0，
                    // 一个像素都不会产生（比在片元里 clip 更省填充率）
                    output.positionCS = float4(0.0, 0.0, 0.0, 1.0);
                    output.fogFactor = 0.0h;
                #endif

                return output;
            }

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if !defined(_OUTLINE_ON)
                    clip(-1.0);
                #endif

                // 和主 Pass 共用 SampleOpacity，镂空的地方描边也跟着断开
                #if defined(_ALPHATEST_ON)
                    clip(SampleOpacity(input.uv) - _Cutoff);
                #endif

                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, _OutlineColor.a);
            }
            ENDHLSL
        }

        // ==================== 主光照 Pass ====================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            // 材质关键字
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT

            // URP 关键字（照抄 URP 14 的 Lit.shader，少一个关键字就会有光照对不上）
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS

            // Unity 关键字
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 1);
                float3 positionWS : TEXCOORD2;
                half3  normalWS   : TEXCOORD3;
                half4  tangentWS  : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
                half3  vertexLighting : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                output.normalWS = normalInputs.normalWS;
                // w 存切线手性符号，片元里用来现算 bitangent
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexLighting = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);

                return output;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;

                half4 albedoAlpha = SampleAlbedoAlpha(uv);
                half alpha = SampleOpacity(uv);
                half3 normalWS = SampleNormalWS(uv, input.normalWS, input.tangentWS);
                half metallic = SampleMetallic(uv);
                half smoothness = SampleSmoothness(uv);
                half3 emission = SampleEmission(uv);

                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedoAlpha.rgb;
                surfaceData.alpha = alpha;
                surfaceData.metallic = metallic;
                surfaceData.smoothness = smoothness;
                surfaceData.occlusion = 1.0h;
                surfaceData.emission = emission;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                // GetShadowCoord 要一个 VertexPositionInputs，片元里只有 WS / CS 两个字段有用
                VertexPositionInputs shadowInputs = (VertexPositionInputs)0;
                shadowInputs.positionWS = input.positionWS;
                shadowInputs.positionCS = input.positionCS;
                inputData.shadowCoord = GetShadowCoord(shadowInputs);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = alpha;
                return color;
            }
            ENDHLSL
        }

        // ==================== 阴影投射 ====================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"
            // URP 的 Shadows.hlsl 里用到了 core 的 LerpWhiteTo，但它自己没 include
            // CommonMaterial.hlsl —— URP 内置的 Pass 是靠 SurfaceInput.hlsl 间接带进来的，
            // 我们没走 SurfaceInput，所以这里要显式补上，否则报 undeclared identifier。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // 由 ShadowUtils.SetupShadowCasterConstantBuffer 每帧喂进来
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHATEST_ON)
                    float2 uv : TEXCOORD0;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #if defined(_ALPHATEST_ON)
                    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #endif

                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 和 ForwardLit 用同一个 SampleOpacity，镂空的地方才不投影
                #if defined(_ALPHATEST_ON)
                    clip(SampleOpacity(input.uv) - _Cutoff);
                #endif

                return 0;
            }
            ENDHLSL
        }

        // ==================== 深度 ====================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                #if defined(_ALPHATEST_ON)
                    float2 uv : TEXCOORD0;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #if defined(_ALPHATEST_ON)
                    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_ALPHATEST_ON)
                    clip(SampleOpacity(input.uv) - _Cutoff);
                #endif

                return 0;
            }
            ENDHLSL
        }

        // ==================== 深度 + 法线（SSAO、贴花等要用） ====================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"

            struct NormalAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalVaryings
            {
                float4 positionCS : SV_POSITION;
                // uv 必须无条件声明：即使既没开 _ALPHATEST_ON 也没开 _NORMALMAP，
                // 片元里的 SampleNormalWS / SampleOpacity 还是会引用它
                float2 uv       : TEXCOORD0;
                half3 normalWS  : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            NormalVaryings DepthNormalsVertex(NormalAttributes input)
            {
                NormalVaryings output = (NormalVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                return output;
            }

            half4 DepthNormalsFragment(NormalVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_ALPHATEST_ON)
                    clip(SampleOpacity(input.uv) - _Cutoff);
                #endif

                half3 normalWS = SampleNormalWS(input.uv, input.normalWS, input.tangentWS);

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalize(normalWS));
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
