// ---------------------------------------------------------------------------
// PAO / ModelPBRExpression —— 表情专用 PBR shader（只给水母脸部面片用）
//
// 和 ModelPBR.shader 的关系：
//   参数、贴图插槽、光照全部一致，额外多一层「表情图层」。
//   这样脸和身体在同样的光照下渲染，不会出现两块颜色对不上的问题。
//
// 表情图层的工作方式（关键）：
//   * 底色 / 法线 / 金属 / 粗糙 / 自发光全部用 TEXCOORD0（UV0）采样，
//     和身体完全一样 —— 面片是身体表面复制出来的，UV0 也一起复制了，
//     所以脸和身体的无缝衔接是"白送"的，不需要额外烘焙。
//   * 表情贴图用 TEXCOORD1（UV1 在 Unity 里算第二套 = Blender 的 UVFace）。
//     这套 UV 是正面正交投影，1024² 画布，8 张表情共用同一个坐标系，
//     所以每个表情只要换一张贴图，不用动 UV、不用动模型。
//   * 表情颜色用 HDR 的 _ExpressionColor 调制，推亮就能当"发光表情"用。
//
// 脸部面片模型（jellyfish_face_patch）的特征：
//   177 面 / 209 顶点、挂在同一个骨架下、带同样 82 个骨骼权重、
//   沿法线外推了 0.03 单位（避免与身体共面 Z-fighting）。
//
// 材质设置（8 个表情各一个材质，只差 _ExpressionMap）：
//   _BaseMap / _NormalMap / _MetallicMap / _RoughnessMap / _EmissionMap
//     → 与身体材质指向同一批贴图（*_BaseColor / *_Normal / ...）
//   _ExpressionMap → T_JellyFace_0X_*.png（Alpha8 压缩、Clamp、sRGB）
//   _OutlineEnabled → 0（面片不参与描边，否则脸边缘会多一圈描边外壳）
// ---------------------------------------------------------------------------
Shader "PAO/ModelPBRExpression"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("基础色 BaseColor", 2D) = "white" {}
        [MainColor]   _BaseColor ("颜色叠加（A = 整体透明度）", Color) = (1, 1, 1, 1)

        [Header(Expression)]
        [NoScaleOffset] _ExpressionMap ("表情贴图（UV1 / 1024 画布）", 2D) = "black" {}
        [HDR] _ExpressionColor ("表情颜色（可 >1 推亮）", Color) = (1, 1, 1, 1)

        [Header(Normal)]
        [Normal][NoScaleOffset] _NormalMap ("法线 Normal", 2D) = "bump" {}
        _NormalScale ("法线强度", Range(0, 2)) = 1

        [Header(Metallic and Roughness)]
        [NoScaleOffset] _MetallicMap ("金属度 Metallic（R）", 2D) = "black" {}
        _Metallic ("金属度倍数", Range(0, 1)) = 1
        [NoScaleOffset] _RoughnessMap ("粗糙度 Roughness（R）", 2D) = "white" {}
        _Roughness ("粗糙度倍数", Range(0, 2)) = 1

        [Header(Emission)]
        [Toggle(_EMISSION)] _EmissionEnabled ("启用自发光", Float) = 0
        [HDR] _EmissionColor ("自发光颜色", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("自发光 Emissive", 2D) = "black" {}

        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha 裁剪", Float) = 0
        _Cutoff ("裁剪阈值", Range(0, 1)) = 0.5
        [Toggle(_OPACITYMAP)] _UseOpacityMap ("用 op 贴图作为不透明度", Float) = 0
        [NoScaleOffset] _OpacityMap ("不透明度 op", 2D) = "white" {}
        _Opacity ("整体不透明度", Range(0, 1)) = 1

        [Header(Render State)]
        [Toggle(_SURFACE_TYPE_TRANSPARENT)] _TransparentMode ("透明模式（Render Queue 设 3000）", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("源混合", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("目标混合", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("深度写入", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除模式", Float) = 2

        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _OutlineEnabled ("启用描边（脸部面片建议保持 0）", Float) = 0
        _OutlineColor ("描边颜色", Color) = (0.10, 0.12, 0.20, 1)
        _OutlineWidth ("描边宽度", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"            = "Opaque"
            "RenderPipeline"        = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector"       = "True"
        }
        LOD 300

        // ==================== 描边（默认关闭） ====================
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
                    float3 positionOS = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                    output.positionCS = TransformObjectToHClip(positionOS);
                    output.fogFactor = ComputeFogFactor(output.positionCS.z);
                #else
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

                #if defined(_ALPHATEST_ON)
                    clip(SampleOpacity(input.uv) - _Cutoff);
                #endif

                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, _OutlineColor.a);
            }
            ENDHLSL
        }

        // ==================== 主光照 Pass（含表情图层） ====================
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

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _OPACITYMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "ModelPBRInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 表情贴图与采样器只在这个 Pass 用得到，所以单独声明（不放 Input.hlsl）
            TEXTURE2D(_ExpressionMap);  SAMPLER(sampler_ExpressionMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 uvFace     : TEXCOORD1;   // Blender 的 UVFace（第二套 UV）
                float2 lightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uvFace     : TEXCOORD1;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 2);
                float3 positionWS : TEXCOORD3;
                half3  normalWS   : TEXCOORD4;
                half4  tangentWS  : TEXCOORD5;
                half   fogFactor  : TEXCOORD6;
                half3  vertexLighting : TEXCOORD7;
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
                // 表情 UV 不做 ST 变换：它是画布坐标，只应受材质上那张图控制
                output.uvFace = input.uvFace;

                output.normalWS = normalInputs.normalWS;
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

                float2 uv     = input.uv;
                float2 uvFace = input.uvFace;

                half4 albedoAlpha = SampleAlbedoAlpha(uv);
                half  alpha       = SampleOpacity(uv);
                half3 normalWS    = SampleNormalWS(uv, input.normalWS, input.tangentWS);
                half  metallic    = SampleMetallic(uv);
                half  smoothness  = SampleSmoothness(uv);
                half3 emission    = SampleEmission(uv);

                // ---- 表情图层：叠在底色之上（透明处保留底色） ----
                half4 expr = SAMPLE_TEXTURE2D(_ExpressionMap, sampler_ExpressionMap, uvFace);
                half3 exprRGB = expr.rgb * _ExpressionColor.rgb;
                half  exprA   = saturate(expr.a * _ExpressionColor.a);
                // 非预乘混合：line art 的彩色描边不会被 alpha 冲淡
                albedoAlpha.rgb = lerp(albedoAlpha.rgb, exprRGB, exprA);

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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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

        // ==================== 深度 + 法线 ====================
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
