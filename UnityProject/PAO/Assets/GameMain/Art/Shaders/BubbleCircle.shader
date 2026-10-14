// ---------------------------------------------------------------------------
// PAO / 圆形泡泡粒子
//
// 专门解决一个问题：早先用噪声去"咬"圆形边，结果没有保证基础圆形，
// 粒子方块的四角会露出来，看起来就是一片片三角形/菱形。
//
// 这里改成**径向衰减打底 + 噪声只做轻微扰动**：
//   1. 先算 dist = length(uv - 0.5) / 0.5  → 严格保证 0~1 的圆形范围
//   2. 用 smoothstep 做柔和边缘，核心实、边缘淡
//   3. 噪声只把边缘推动一点（幅度小），不可能推出圆形之外
//
// 所以无论参数怎么调，输出一定是个圆，不可能出现方形/三角形。
//
// 亮光怎么来：
//   核心区额外叠一层接近白的高光，边缘再叠一圈水痕亮边；
//   配合粒子系统的 Start Color 和 Additive 混合的 Glow 子系统，
//   整体亮度足够在水下暗环境里看清。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleCircle"
{
    Properties
    {
        // 【血的教训】这里只保留最简单的 Color / Range 声明，不写 [Header(...)]。
        //
        // 之前给这个 shader 加过 [Header(Sphere — 让它看起来是个球，不是一张圆片)]
        // 这种带中文和破折号的 Header，结果整个 shader 编译失败：
        //     Shader error in '': Parse error: syntax error, unexpected $undefined
        // 而报错时连文件名都没有（''），极难定位。运行时材质直接变成品红色。
        //
        // 工程里其他 shader（BubbleBall / WaterSurface）的 [Header] 是纯英文，能编译；
        // 所以怀疑是 Header 里的**非 ASCII 字符**（中文 + 长破折号 —）触发的。
        // 分组靠这行注释就够了，没必要冒这个险。
        //
        // 另外方向类参数（_LightDir / _SpecOffset）这里声明成 Color：
        // 它内部就是 float4，C# 端用 SetVector 照样能赋值，比 Vector 更保险。
        _Tint          ("底色", Color) = (0.72, 0.90, 1.0, 1.0)
        _Opacity       ("整体不透明度", Range(0, 2)) = 1.0

        _Radius        ("圆的范围（1 = 贴满粒子方块）", Range(0.5, 1.5)) = 1.0
        _EdgeSoftness  ("边缘柔和度（0=硬边）", Range(0.01, 1.0)) = 0.42
        _CoreSolid     ("核心实心范围", Range(0, 1)) = 0.35

        _GlowColor     ("亮光颜色", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength  ("亮光强度", Range(0, 4)) = 1.1
        _GlowWidth     ("亮光宽度（靠近边缘的一圈）", Range(0.01, 1.0)) = 0.30

        _SphereAmount  ("球面明暗强度（0=平涂，1=像球）", Range(0, 1.5)) = 0.75
        _LightDir      ("主光方向（屏幕空间 xyz）", Color) = (-0.4, 0.6, 0.7, 1.0)
        _AmbientTint   ("暗部颜色（水下的冷色环境光）", Color) = (0.42, 0.55, 0.72, 1.0)

        _SpecColor     ("高光颜色", Color) = (1.0, 1.0, 1.0, 1.0)
        _SpecStrength  ("高光强度", Range(0, 4)) = 1.1
        _SpecPower     ("高光锐度（越大越像水珠）", Range(1, 256)) = 48
        _SpecOffset    ("高光位置偏移（往左上挪一点）", Color) = (-0.18, 0.20, 0, 0)

        _RimColor      ("边缘透光颜色", Color) = (1.0, 0.92, 0.94, 1.0)
        _RimStrength   ("边缘透光强度", Range(0, 4)) = 0.9
        _RimPower      ("边缘收窄程度（越大越靠边）", Range(0.5, 8)) = 2.6

        _WobbleAmp     ("边缘流动幅度（很小，只做轻微扰动）", Range(0, 0.25)) = 0.06
        _WobbleSpeed   ("边缘流动速度", Range(0, 8)) = 2.0
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
            Name "BubbleCircle"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float  _Opacity;

                float  _Radius;
                float  _EdgeSoftness;
                float  _CoreSolid;

                float4 _GlowColor;
                float  _GlowStrength;
                float  _GlowWidth;

                float  _SphereAmount;
                float4 _LightDir;
                float4 _AmbientTint;

                float4 _SpecColor;
                float  _SpecStrength;
                float  _SpecPower;
                float4 _SpecOffset;

                float4 _RimColor;
                float  _RimStrength;
                float  _RimPower;

                float  _WobbleAmp;
                float  _WobbleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 custom     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float4 custom     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv         = input.uv;
                o.color      = input.color;
                o.custom     = input.custom;

                return o;
            }

            // 低频正弦叠加：和 BubbleBallCore 同一套，只要轻微的液态感
            float BlobNoise(float2 uv, float t)
            {
                float n = sin(uv.x * 3.1 + t * 1.00)
                        + sin(uv.y * 3.7 - t * 1.23)
                        + sin((uv.x + uv.y) * 2.3 + t * 0.71);
                return n * (1.0 / 3.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                // ---- 径向距离：这一步就决定了"一定是圆" ----
                // 以粒子方块中心为原点，把最大半径归一化到 1
                float2 centered = (i.uv - 0.5) * 2.0;
                float  dist = length(centered);

                // ---- 轻微边缘扰动 ----
                // 幅度上限 0.25，而且只作用在圆的外侧边缘，
                // 不可能把形状推出圆形范围变成方块
                float t = _Time.y * _WobbleSpeed;
                float blob = BlobNoise(centered + i.custom.x, t);
                float wobbled = dist * (1.0 - blob * _WobbleAmp);

                // ---- 基础圆形：硬性裁掉圆外的一切 ----
                float soft = max(_EdgeSoftness, 0.01);
                float circle = 1.0 - smoothstep(_Radius - soft, _Radius, wobbled);

                // 圆外直接丢弃 —— 这就是"不可能是三角形"的保证
                clip(circle - 0.002);

                // ---- 核心实心 ----
                float core = 1.0 - smoothstep(_CoreSolid, _Radius, wobbled);

                // ---- 亮光：靠边缘的一圈 + 核心的高光 ----
                float rimDist = abs(wobbled - _Radius * (1.0 - _GlowWidth));
                float rim = 1.0 - smoothstep(0.0, max(_GlowWidth, 0.01), rimDist);

                float coreGlow = core * core;

                // ============================================================
                // 球面着色 —— "有质感"的关键
                // ============================================================
                //
                // 粒子是 billboard（一张正对镜头的方片），本身没有体积。
                // 但我们要它看起来像主角水母那样的**球**：有明暗过渡、有高光、
                // 边缘透光。做法是从 uv 反推一个假的球面法线：
                //
                //   把圆盘上的点 x,y ∈ [-1,1] 当作球面上一点的水平投影，
                //   那么 z = sqrt(1 - x² - y²) 就是它"朝镜头方向"的分量。
                //   于是 (x, y, z) 就是一个单位球面法线 —— 圆盘中心朝镜头（z=1），
                //   边缘与视线相切（z=0）。
                //
                // 有了这个法线，就能照 3D 那样做 N·L 漫反射和高光了。
                float r2 = saturate(dot(centered, centered));
                float nz = sqrt(saturate(1.0 - r2));

                float3 n = normalize(float3(centered.x, centered.y, max(nz, 1e-4)));

                // ---- 漫反射：明暗过渡 ----
                float3 lightDir = normalize(float3(_LightDir.x, _LightDir.y,
                                                   max(abs(_LightDir.z), 0.2)));

                float ndl = saturate(dot(n, lightDir));

                // 卡通感的软过渡：smoothstep 收一下，不要写实那种死板的余弦衰减
                float diffuse = smoothstep(0.05, 1.0, ndl);

                // 暗部不是纯黑，而是往环境色压 —— 水下的环境光是冷青色的
                half3 shadowCol = _Tint.rgb * _AmbientTint.rgb;
                half3 litCol = _Tint.rgb * (0.72 + 0.55 * diffuse);

                // _SphereAmount 控制"像球的程度"：
                //   0 = 完全平涂（原来的样子），1 = 完整的球面明暗
                float shade = lerp(1.0, diffuse, _SphereAmount);
                half3 col = lerp(shadowCol, litCol, shade);

                // ---- 边缘透光：越靠边越薄，光穿过来 ----
                // 用 (1 - nz) 做菲涅尔近似：边缘 nz→0，透光最强
                float fresnel = pow(1.0 - nz, _RimPower);
                col += _RimColor.rgb * (fresnel * _RimStrength * _SphereAmount);

                // ---- 镜面高光：一颗亮"水珠" ----
                // 高光位置可以偏移，放到偏左上，和主角水母的高光位置呼应
                float2 specCenter = centered - float2(_SpecOffset.x, _SpecOffset.y);
                float  specDist = length(specCenter);
                float  spec = 1.0 - smoothstep(0.0, 0.5, specDist);
                spec = pow(saturate(spec), _SpecPower * 0.08 + 1.0);

                col += _SpecColor.rgb * (spec * _SpecStrength * _SphereAmount);

                // ---- 原来的发光层 ----
                col += _GlowColor.rgb * (rim * _GlowStrength);
                col += _GlowColor.rgb * (coreGlow * _GlowStrength * 0.35);

                // ---- 中心稍微压一点，让球更有体积感 ----
                float3 fakeNormal = normalize(float3(centered.x * 0.5, centered.y * 0.5, nz));
                col *= (1.0 + 0.10 * fakeNormal.z * _SphereAmount);

                float alpha = circle * _Opacity * i.color.a;

                // 边缘稍微透一点 —— 泡泡的边是薄的
                alpha *= lerp(1.0, 0.55 + 0.45 * nz, _SphereAmount * 0.6);

                // 粒子系统的 Start Color 参与调色
                col *= i.color.rgb;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
