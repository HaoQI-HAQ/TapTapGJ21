Shader "PAO/WaterSurface"
{
    // -----------------------------------------------------------------------
    // 水面（从下方往上看）—— 风格化波光
    //
    // 一块铺在 waterLevel 高度上的大平面，玩家在水下抬头就能看见。
    // 参考图里水面是整张画面最亮的地方 —— 亮到会被 Bloom 咬一口，
    // 边缘挂一圈柔光。这个「过曝」不是失误，是效果的一部分。
    //
    // -----------------------------------------------------------------------
    // 【风格化波光是怎么做出来的】
    //
    // 三个成分，各管一件事（详细说明见 UnderwaterInclude 里的
    // PAO_WaterSurfaceShader）：
    //
    //   1. 焦散网    —— 主体形状。和地板焦散共用同一个细胞噪声，
    //                   所以抬头的水面和脚下的地板是同一池水
    //   2. 峰值闪光  —— 「波光粼粼」四个字的全部含义。把网的峰值挑出来
    //                   平方两次，只有最亮的点留下，其余压暗，交给 Bloom 咬
    //   3. 方向性反光 —— 两层正弦波解析求法线 + 太阳高光。
    //                   波纹朝哪边斜，高光就往哪边跑
    //
    // 调参顺序建议：先调 `_Sparkle`（闪不闪），再调 `_CausticScale`
    // （网多大），最后调 `_Brightness`（整体多亮）。
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
    //   设成 Transparent 的话它会和泡泡抢排序，一闪一闪的。
    // -----------------------------------------------------------------------

    Properties
    {
        [Header(Surface)]
        _BaseColor        ("底色", Color)         = (0.72, 0.95, 1.0, 1)
        _Brightness       ("亮度（超过 1 触发 Bloom）", Range(0, 8)) = 2.6
        _RimIntensity     ("掠射亮边", Range(0, 4))      = 1.4

        [Header(Caustics Net)]
        _CausticScale     ("焦散网尺寸（米）", Range(1, 60)) = 16
        _RippleSpeed      ("流动速度", Range(0, 4))      = 0.8

        [Header(Sparkle)]
        _Sparkle          ("闪光强度（波光粼粼）", Range(0, 4)) = 1.5
        _LightDir         ("反光方向（指向光源）", Vector) = (0.25, 1, 0.35, 0)
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Brightness;
                float  _RimIntensity;
                float  _CausticScale;
                float  _RippleSpeed;
                float  _Sparkle;
                float4 _LightDir;
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

                // 【为什么取 abs 而不是直接 viewDir.y】
                //   玩家在水下，viewDir.y 应该为正（从下往上看）。
                //   但相机贴着水面、或者那一帧刚好在水面之上时它会变负，
                //   NoV 变负 → pow 出 NaN → 整片水面变黑闪一下。
                //   abs 兜住这个边角，代价是水面之上看水面也不会有异常。
                float NoV = saturate(abs(viewDir.y));

                // 反光方向：留空的话给一个默认的斜上方
                float3 lightDir = _LightDir.xyz;
                if (dot(lightDir, lightDir) < 1e-6)
                {
                    lightDir = float3(0.25, 1.0, 0.35);
                }

                return PAO_WaterSurfaceShade(
                    input.positionWS,
                    viewDir,
                    NoV,
                    PAO_WaterTime(),
                    _CausticScale,
                    _RippleSpeed,
                    _RimIntensity,
                    _Sparkle,
                    lightDir,
                    _BaseColor.rgb,
                    _Brightness);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
