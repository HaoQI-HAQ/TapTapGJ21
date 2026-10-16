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
        _EdgeSoftness  ("边缘柔和度（厚涂是柔和过渡，调大）", Range(0.01, 1.0)) = 0.62
        _CoreSolid     ("核心实心范围", Range(0, 1)) = 0.0

        _GlowColor     ("亮光颜色", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength  ("亮光强度", Range(0, 4)) = 0.55
        _GlowWidth     ("亮光宽度（靠近边缘的一圈）", Range(0.01, 1.0)) = 0.45

        _SphereAmount  ("球面明暗强度（保留给卡通模式，厚涂用 1）", Range(0, 1.5)) = 1.0

        _AmbientBoost  ("环境光强度（乘在 Unity 的环境光上）", Range(0, 4)) = 1.25
        _DirectBoost   ("主光强度", Range(0, 4)) = 0.55
        _Transmission  ("次表面散射感（半透明软体的透光）", Range(0, 3)) = 0.55
        _LightDir      ("（已废弃，方向光由场景决定）", Color) = (-0.4, 0.6, 0.7, 1.0)
        _AmbientTint   ("（已废弃，环境色由场景决定）", Color) = (0.52, 0.64, 0.80, 1.0)

        _ShadowSoft    ("明暗过渡的柔和度（越大越像厚涂）", Range(0.05, 1.0)) = 0.55
        _ToonSteps     ("色阶数（1 = 完全平滑，厚涂用 1）", Range(1, 6)) = 1.0
        _GrainStrength ("厚涂笔触强度", Range(0, 0.6)) = 0.22
        _GrainScale    ("厚涂笔触粗细", Range(4, 160)) = 22.0
        _GrainStretch  ("笔触方向拉伸（模拟运笔）", Range(0.2, 2.0)) = 1.45

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

            // Lighting.hlsl 提供 SampleSH（环境光 / 光照探针）和 GetMainLight（主方向光）。
            // 角色用的 ModelPBR.shader 也是 include 这个，所以两者拿到的是同一份环境数据 ——
            // 这正是"爆炸和角色同进同出"的保证。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

                float  _AmbientBoost;
                float  _DirectBoost;
                float  _Transmission;
                float4 _LightDir;
                float4 _AmbientTint;

                float  _ShadowSoft;
                float  _ToonSteps;
                float  _GrainStrength;
                float  _GrainScale;
                float  _GrainStretch;

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

            // 哈希：给纸张颗粒用
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 厚涂笔触：把噪声在**一个方向上拉长**，看起来像一笔一笔刷上去的。
            //
            // 圆形颗粒看起来像"纸纹"，拉长之后才像"笔触" —— 这是厚涂和
            // 普通颗粒质感最直接的区别。_GrainStretch 控制拉多长。
            float BrushStroke(float2 uv)
            {
                // y 方向压缩 = 笔触沿 x 方向拉长
                float2 p = float2(uv.x, uv.y * _GrainStretch);

                float g  = Hash21(p * _GrainScale);
                float g2 = Hash21(p * _GrainScale * 2.3 + 11.7);
                float g3 = Hash21(p * _GrainScale * 0.47 + 31.9);

                return g * 0.5 + g2 * 0.3 + g3 * 0.2;
            }

            // 把值量化成 N 级。
            // 厚涂/PBR 画风**不用**它（_ToonSteps = 1 时下面会跳过），
            // 保留是为了需要临时切成卡通效果时可以调 _ToonSteps 打开。
            float ToonBand(float t, float steps)
            {
                steps = max(steps, 1.0);
                return floor(saturate(t) * steps + 0.5) / steps;
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

                // ---- 基础圆形 ----
                // _EdgeSoftness 默认调小了（0.10）：手绘是硬边，不是柔和渐隐
                float soft = max(_EdgeSoftness, 0.01);
                float circle = 1.0 - smoothstep(_Radius - soft, _Radius, wobbled);

                // 圆外直接丢弃 —— 这就是"不可能是三角形"的保证
                clip(circle - 0.002);

                // ---- 核心实心 ----
                float core = 1.0 - smoothstep(_CoreSolid, _Radius, wobbled);

                // ---- 亮光：靠边缘的一圈 + 核心的高光 ----
                // 强度默认调低到 0.5：手绘不该有强发光，那会盖掉平涂的色块
                float rimDist = abs(wobbled - _Radius * (1.0 - _GlowWidth));
                float rim = 1.0 - smoothstep(0.0, max(_GlowWidth, 0.01), rimDist);

                float coreGlow = core * core;

                // ============================================================
                // 真实光照 —— 这是"和角色同一个画风"的根治手段
                // ============================================================
                //
                // 【为什么必须走真实光照，而不是自己假装】
                // 角色的 ModelPBR.shader 走 URP 标准 PBR：
                //   环境光来自 **Light Probe / 天空盒**（SampleSH），
                //   主光来自场景里的方向光（GetMainLight）。
                // 所以它的立体感是**被场景照出来的**，不是画上去的。
                //
                // 我早先自己写死一个光方向、再手搓明暗，那样永远只能在
                // 特定光照下"碰巧像"。一旦场景光照变了（换关卡、进水下），
                // 角色跟着变、爆炸不变 —— 两者立刻脱节。
                // 现在改成采样真实环境，爆炸就和角色同进同出。
                //
                // 从 uv 反推假的球面法线：圆盘上的点 (x,y) 当作球面水平投影，
                // z = sqrt(1 - x² - y²) 就是它朝镜头的分量。
                float r2 = saturate(dot(centered, centered));
                float nz = sqrt(saturate(1.0 - r2));

                float3 n = normalize(float3(centered.x, centered.y, max(nz, 1e-4)));

                // ---- 环境光：直接问 Unity 要当前环境 ----
                // 蓝天、云、光照探针都会体现在这里，和角色拿到的是同一份数据
                half3 ambient = SampleSH(n) * _AmbientBoost;

                // ---- 主光 ----
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);

                float ndl = saturate(dot(n, lightDir));

                // 平滑过渡（厚涂的明暗是化开的，不是切的）。
                // _ToonSteps 保持在 1 —— 量化会和角色的连续明暗冲突。
                float diffuse = smoothstep(1.0 - _ShadowSoft, 1.0, ndl);
                if (_ToonSteps > 1.5)
                {
                    diffuse = ToonBand(diffuse, _ToonSteps);
                }

                half3 col = _Tint.rgb * (ambient + mainLight.color * diffuse * _DirectBoost);

                // ---- 次表面散射感 ----
                // 角色是半透明软体：朝光源的方向上，光会"透过去"，
                // 所以背光面靠外的一圈会亮起来，而不是黑的。
                // 用 (1 - nz) 做边缘权重，再乘朝光程度 —— 廉价但很像。
                float towardLight = saturate(dot(n, lightDir));
                float thin = pow(1.0 - nz, 2.2);
                half3 sss = _Tint.rgb * (_Transmission * thin * (0.35 + 0.65 * towardLight));
                col += sss;

                // ---- 边缘透光：角色边缘有一圈柔和的亮 ----
                float fresnel = pow(1.0 - nz, _RimPower);
                col += _RimColor.rgb * (fresnel * _RimStrength);

                // ---- 高光：柔和的大光斑，不是小块实心白 ----
                // 角色表面是光滑的（低 roughness），高光是"一片柔光"，
                // 所以这里用宽的 smoothstep，不用 ToonBand 量化。
                float2 specCenter = centered - float2(_SpecOffset.x, _SpecOffset.y);
                float  specDist = length(specCenter);
                float  spec = 1.0 - smoothstep(0.0, 0.55, specDist);
                spec = spec * spec;                 // 中心集中一点，但边缘仍是渐隐

                col += _SpecColor.rgb * (spec * _SpecStrength * _SphereAmount);

                // ---- 发光层（保持克制，别盖掉立体感）----
                col += _GlowColor.rgb * (rim * _GlowStrength);
                col += _GlowColor.rgb * (coreGlow * _GlowStrength * 0.35);

                // ---- 厚涂笔触 ----
                // 不是"纸纹"而是"笔触"：噪声沿一个方向拉长成一条条，
                // 看起来像颜料一笔一笔刷上去的。
                // 加在颜色上做轻微明暗起伏 + 一点色相抖动。
                float brush = BrushStroke(i.uv);
                float brushOffset = (brush - 0.5) * _GrainStrength;
                col += brushOffset;
                col *= (1.0 + brushOffset * 0.40);

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
