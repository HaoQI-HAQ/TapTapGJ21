Shader "Distant Lands/Stylized Caustics URP"
{
    // -----------------------------------------------------------------------
    // Custom Render Texture 专用的焦散生成 shader —— URP 版
    //
    // 【为什么要有这个文件】
    //   原包里的 "Stylized Caustics.shader" 是 BiRP 写的：
    //     CGPROGRAM / #include "UnityCustomRenderTexture.cginc" / tex2D()
    //   而本项目是 URP。虽然 UnityCustomRenderTexture.cginc 本身是管线无关的，
    //   但为了不和 URP 的头文件混用（那是"CG 和 HLSL 混编"最容易出静默错误的地方），
    //   这里用 URP 的 Core.hlsl 重写一遍。
    //
    // 【数学是逐行照搬的，没有改】
    //   两层不同方向、不同速度的滚动采样，然后 ★ 取 min。
    //   取 min（不是 max）是关键：min 让暗部变多、亮纹变细变锐，
    //   正好是焦散那种"细细的亮网"。用 max 出来的是粗亮纹，不像。
    //
    // 【参数名和原版完全一致】
    //   _CausticTexture / _CausticScale / _CausticSpeed / _CausticIntensity
    //   → 所以原包的 Caustic.mat 换到这个 shader 上，参数直接生效，不用重调。
    // -----------------------------------------------------------------------

    Properties
    {
        _CausticScale("Caustic Scale", Float) = 10
        _CausticTexture("Caustic Texture", 2D) = "white" {}
        _CausticSpeed("Caustic Speed", Float) = 1
        _CausticIntensity("Caustic Intensity", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "Custom RT Update"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CausticTexture);
            SAMPLER(sampler_CausticTexture);

            CBUFFER_START(UnityPerMaterial)
                float  _CausticScale;
                float4 _CausticTexture_ST;
                float  _CausticSpeed;
                float  _CausticIntensity;
            CBUFFER_END

            // Custom Render Texture 的尺寸。xy = (宽, 高)，用来把 localTexcoord 归一化。
            // Unity 自己会给 Custom Render Texture 的材质注入这个 uniform。
            float4 _CustomRenderTextureInfo;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;

                // 全屏三角形，和 Unity 的 Blitter 用的是同一套顶点生成方式
                o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);

                // 【为什么从 SV_POSITION 反推 uv，而不是直接用 GetFullScreenTriangleTexCoord】
                //   Custom Render Texture 每一帧的位置由引擎按 update zone 摆好，
                //   顶点位置里已经包含了那些信息。而 GetFullScreenTriangleTexCoord
                //   只给出「在三角形里的 0~1 坐标」，两者在 update zone 不是全屏时会错位。
                //
                //   从最终的裁剪空间坐标反推，得到的永远是「这片 RT 上的真实位置」，
                //   和 update zone 怎么设无关。
                float2 ndc = o.positionCS.xy / max(_CustomRenderTextureInfo.xy, 1.0) * 2.0 - 1.0;

                // 裁剪空间 → 纹理 uv。y 要翻一下：NDC 的 +Y 在上，纹理的 +V 在下。
                o.uv = float2(ndc.x * 0.5 + 0.5, ndc.y * -0.5 + 0.5);

                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 texCoord = input.uv;

                // ---- 两层滚动（方向和速度都不同）----
                float2 pannerA = _Time.y * (float2(-0.2,  0.0) * 1.5 * _CausticSpeed) + texCoord;
                float2 pannerB = _Time.y * (float2( 0.2, -0.1) * 1.5 * _CausticSpeed) + texCoord;

                // _CausticScale 越大，图案越密（原版就是 1/scale）
                float invScale = 1.0 / max(_CausticScale, 1e-4);

                float4 a = SAMPLE_TEXTURE2D(_CausticTexture, sampler_CausticTexture, pannerA * (invScale * 6.0));
                float4 b = SAMPLE_TEXTURE2D(_CausticTexture, sampler_CausticTexture, pannerB * (invScale * 4.0));

                // ★ 取 min，不是 max —— 这一行决定了它像不像焦散
                return min(a, b) * _CausticIntensity;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
