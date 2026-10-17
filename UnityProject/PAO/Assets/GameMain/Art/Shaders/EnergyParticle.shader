// ---------------------------------------------------------------------------
// PAO / 能量粒子（加色 + 贴图 + 精灵表）
//
// 【为什么需要这个】
// 工程里已有的 PAO/BubbleCircleAdditive 只能画"程序生成的圆形"，
// 贴不了图。而能量爆炸要用的素材（nova 球状爆发、冲击环、放射光条）
// 都是**带纹理的精灵**，所以需要一个能采样贴图的加色粒子着色器。
//
// 【为什么要自己写，而不是用 Epic Toon FX 原素材的材质】
// 那套素材是给 **Built-in 管线**做的，材质用的是 Standard 着色器。
// 本工程是 **URP** —— Standard 材质在 URP 里会直接变成品红色。
// 所以贴图照用，材质和着色器必须换成本工程自己的。
//
// 【加色混合】
// Blend SrcAlpha One —— 只加不减。能量光效靠这个才"发亮"，
// 用普通 alpha 混合会变成一层半透明的贴纸。
//
// 【精灵表】
// nova 那张是 6 列 × 3 行 = 18 帧的爆发动画。
// 用 _SheetCols / _SheetRows 指定网格，_SheetFrame 指定播到第几帧。
// 不填（默认 1 × 1）就是当普通贴图用。
// ---------------------------------------------------------------------------
Shader "PAO/EnergyParticle"
{
    Properties
    {
        _MainTex      ("贴图", 2D) = "white" {}
        _Tint         ("颜色", Color) = (0.35, 0.75, 1.0, 1.0)
        _Brightness   ("亮度", Range(0, 8)) = 2.2

        // 粒子系统会自动写进这两个（Start Color / 生命周期颜色）
        _Color        ("粒子颜色（由 ParticleSystem 写入）", Color) = (1, 1, 1, 1)

        // 精灵表
        _SheetCols    ("精灵表列数", Float) = 1
        _SheetRows    ("精灵表行数", Float) = 1
        _SheetFrame   ("当前帧（0 起）", Float) = 0

        // 边缘柔化：把贴图边缘的硬切线压掉，加色混合下硬边很扎眼
        _EdgeFade     ("边缘淡出（0=不处理）", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "EnergyParticle"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One          // 加色：只加不减，能量光效靠这个发亮

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // 所有材质属性集中在一个 CBUFFER —— SRP Batcher 的要求。
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float  _Brightness;
                float4 _Color;
                float  _SheetCols;
                float  _SheetRows;
                float  _SheetFrame;
                float  _EdgeFade;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = input.uv;
                o.color      = input.color;

                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // 粒子自己的 uv（永远 0~1，代表这一颗粒子的方块）
                float2 quadUV = i.uv;
                float2 uv = quadUV;

                // ---- 精灵表：把 0~1 的 uv 换算成"某一帧"的 uv ----
                // 网格是 cols × rows 排的，帧号从左上往右下数。
                // 不填时 cols = rows = 1，这一整段等价于什么都不做。
                float cols = max(_SheetCols, 1.0);
                float rows = max(_SheetRows, 1.0);

                if (cols > 1.0 || rows > 1.0)
                {
                    float frame = floor(_SheetFrame);
                    float col = fmod(frame, cols);
                    float row = floor(frame / cols);

                    // 贴图的行是从上往下的，而 uv 的 v 是从下往上的，所以 row 要翻
                    //
                    // 【为什么要先夹住再算】
                    // quadUV 理论上在 0~1，但粒子的几何有时会略超出去一点点
                    // （浮点误差或 UV 通道被拉伸）。不夹的话 col=5 时会算出
                    // 大于 1 的 uv，Clamp 模式下整块变成边缘色 —— 看起来就是一个
                    // 填满的方色块，而不是球。
                    float2 clamped = saturate(quadUV);

                    uv = float2((clamped.x + col) / cols,
                                (clamped.y + (rows - 1.0 - row)) / rows);
                }

                // ---- 贴图 × 粒子颜色 ----
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // 粒子的 Start Color / 生命周期颜色都通过顶点色传进来。
                // 乘进去之后，同一个材质能做"逐渐变暗/变透明"的收尾。
                half3 col = tex.rgb * _Tint.rgb * i.color.rgb * _Color.rgb;
                half  a   = tex.a * _Tint.a * i.color.a * _Color.a;

                // ---- 边缘淡出 ----
                // 加色混合下贴图的硬边会很扎眼（像一块方块）。
                // 用"这一颗粒子自己的 uv"到中心的距离压一圈，边缘自然消失。
                //
                // 【必须用 quadUV 而不是 uv】
                // uv 在精灵表模式下已经被平移到某一帧的区间（比如 x 在 0.83~1.0），
                // 拿它减 0.5 算距离会得到"整块都很远"，于是整颗粒子被淡出成透明，
                // 或者反过来边缘全亮 —— 那个"大方块"就是这么来的。
                if (_EdgeFade > 0.001)
                {
                    float2 c = quadUV - 0.5;
                    // 用切比雪夫距离（max）而不是欧氏距离：
                    // 精灵表里的每一帧都被网格硬切过，用方形衰减才不会
                    // 在格线上出现一条发亮的方框边。
                    float d = max(abs(c.x), abs(c.y)) * 2.0;
                    a *= 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, d);
                }

                // 加色混合下 alpha 只当"强度"用，所以亮度直接乘在颜色上
                col *= _Brightness;

                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
