Shader "PAO/WaterSurface"
{
    // -----------------------------------------------------------------------
    // 水面（从下方往上看）
    //
    // 一块铺在 waterLevel 高度上的大平面，玩家在水下抬头就能看见。
    // 参考图里水面是整张画面最亮的地方 —— 亮到会被 Bloom 咬一口，
    // 边缘挂一圈柔光。这个「过曝」不是失误，是效果的一部分。
    //
    // -----------------------------------------------------------------------
    // 【为什么是平面，不是半球穹顶】
    //
    // 半球穹顶适合「一望无际的大海」，但房间是有墙的：
    // 半径 200、水面在 y=10 的穹顶，往外 100 米处已经降到 y=-11.6，
    // 早就在地板下面了，玩家会看到水面**穿进地板**。
    //
    // 而一块大平面从下方看永远是一条水平线，正是参考图里那种效果。
    // 尺寸开到比房间大 3 倍以上，玩家根本看不到边。
    //
    // -----------------------------------------------------------------------
    // 【为什么 Cull Front】
    //   玩家在水下，永远是从**下方**看这块面。Cull Front 让背面可见 = 
    //   从下面看得到、从上面看不到。这样万一相机跑到水面以上，
    //   也不会看到一块奇怪的发光地板。
    //
    // 【为什么队列设成 Geometry 而不是 Transparent】
    //   它是个实心面，需要正常写深度，才能把水下的泡泡、鱼、地形正确挡住。
    //   设成 Transparent 的话它会和 Sphere 那几个泡泡抢排序，一闪一闪的。
    // -----------------------------------------------------------------------

    Properties
    {
        [Header(Surface)]
        _BaseColor        ("底色", Color)         = (0.72, 0.95, 1.0, 1)
        _Brightness       ("亮度（超过 1 触发 Bloom）", Range(0, 8)) = 2.4
        _RippleScale      ("涟漪频率", Range(0.5, 60))   = 14
        _RippleSpeed      ("涟漪速度", Range(0, 4))      = 0.8
        _RimIntensity     ("掠射亮边", Range(0, 4))      = 1.4

        [Header(Caustics)]
        _CausticStrength  ("焦散反光强度（默认 0：水面自己已经很亮，\n多算一遍焦散是白花算力。想更闪可以开到 0.3）", Range(0, 2)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "WaterSurface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front          // 只从下方可见
            ZWrite On
            ZTest LEqual
            Blend One OneMinusSrcAlpha     // 预乘 alpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UnderwaterInclude.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            // 材质自带的几个参数。用 UNDERWATER_GLOBAL 宏切换数据来源，
            // 这样材质在 URP 里既能被 RenderFeature 全局控制，也能单独摆一个用。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Brightness;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _RimIntensity;
                float  _CausticStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv         = input.uv;

                return o;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);

                // 平面法线是 +Y。玩家在下方 → viewDir.y > 0，
                // NoV 随视线变斜而变小（掠射 → 趋近 0）。
                float NoV = saturate(viewDir.y);

                return PAO_WaterSurfaceShade(
                    input.positionWS,
                    viewDir,
                    NoV,
                    PAO_WaterTime(),
                    _RippleScale,
                    _RippleSpeed,
                    _RimIntensity,
                    _CausticStrength,
                    _BaseColor.rgb,
                    _Brightness);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
