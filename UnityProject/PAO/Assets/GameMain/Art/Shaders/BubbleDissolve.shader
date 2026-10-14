// ---------------------------------------------------------------------------
// PAO / 泡泡溶解 · 平滑推进
//
// 参考效果：一个实体慢慢被"吃掉" ——
//   1. 一条**发亮的边缘**（偏白的窄边）沿噪声边界推进
//   2. 边缘内侧**完全裁掉**（clip），所以看起来是"真的少了一块"
//   3. 边界是不规则的有机形状，不是光滑圆弧
//   4. 整体是**逐渐**消失，不是整体变淡
//
// 【和前两版试错的区别】
//   第 1 版 BubbleDissolvePuff：只有"边缘侵蚀"参数，但**从没随时间驱动过** ——
//     实际效果是整体淡出，泡泡没有被吃掉。这是那一版最根本的 bug。
//   第 2 版 BubbleDissolveBurn：做成了火烧 —— 橘色火苗、焦黑、火星。
//     那是"燃烧"，不是"溶解"。火是个强烈的事件，溶解应该是安静的。
//   本版：去掉火的一切（不留橘色、不留焦黑），只保留"发亮边缘 + 逐层裁掉"。
//
// 【关键属性】
//   _DissolveAmount  0 = 完好，1 = 完全消失。运行时靠 MaterialPropertyBlock 每帧推进。
//   _EdgeWidth       发亮边缘的宽度。窄一点更利落，宽一点更柔和。
//   _EdgeIntensity   边缘亮度。参考图里那条白边是明显发亮的。
//
// 【光照】
//   极简 URP 主光 + 环境项，让泡泡在溶解过程中立体感不突变。
//   溶解时颜色本身会变淡（_FadeTint），模拟"材质在失去"。
// ---------------------------------------------------------------------------
Shader "PAO/BubbleDissolve"
{
    Properties
    {
        // 只保留 Color / Range 声明，不写 [Header(...)]。
        // 之前给另一个 shader 写过带中文和破折号的 [Header(...)]，触发过
        // "Shader error in '': Parse error"，报错连文件名都没有，极难定位。
        _BaseColor     ("溶解前的底色", Color) = (0.55, 0.85, 1.0, 1.0)
        _FadeTint      ("溶解中的颜色（往这里退）", Color) = (0.72, 0.88, 1.0, 1.0)
        _Ambient       ("环境光强度", Range(0, 1)) = 0.6

        _NoiseScale    ("溶解纹理密度", Range(0.5, 30)) = 8.0
        _NoiseSoftness ("边界柔和度（0=硬边，1=很柔）", Range(0.01, 1.0)) = 0.22

        _EdgeWidth     ("发亮边缘宽度", Range(0.002, 0.5)) = 0.055
        _EdgeColor     ("发亮边缘颜色", Color) = (1.0, 0.98, 0.96, 1.0)
        _EdgeIntensity ("发亮边缘亮度", Range(0, 12)) = 3.2

        _DissolveAmount("溶解进度（0=完好 1=消失，运行时用 PropertyBlock 改）", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Opaque"
            "Queue"           = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BubbleDissolve"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _FadeTint;
                float  _Ambient;

                float  _NoiseScale;
                float  _NoiseSoftness;

                float  _EdgeWidth;
                float4 _EdgeColor;
                float  _EdgeIntensity;

                float  _DissolveAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(input.normalOS);

                o.positionCS = pos.positionCS;
                o.normalWS   = nrm.normalWS;
                o.positionOS = input.positionOS.xyz;

                return o;
            }

            // ---- 程序噪声（哈希 + 双线性插值）----
            // 不用贴图：少一个资源依赖，密度还能按泡泡大小现调。
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 三个倍频叠加：块状 + 中等 + 细节。
            // 三层比两层更"有机"，边界不会显得是一坨坨圆斑。
            float DissolveNoise(float3 posOS)
            {
                float2 p = posOS.xy * _NoiseScale + posOS.z * _NoiseScale * 0.6;

                float n1 = ValueNoise(p);
                float n2 = ValueNoise(p * 2.13 + 7.7);
                float n3 = ValueNoise(p * 4.41 + 23.3);

                return saturate(n1 * 0.55 + n2 * 0.30 + n3 * 0.15);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float noise = DissolveNoise(i.positionOS);

                // ---- 逐层吃掉 ----
                // 进度 0→1 时 threshold 从 -_NoiseSoftness 推到 1：
                // 起步给一点负偏移，这样 alpha 刚开始时不会有零星碎片先掉。
                float threshold = _DissolveAmount * (1.0 + _NoiseSoftness) - _NoiseSoftness;

                float amount = noise - threshold;

                // 低于阈值的片元直接裁掉 —— 这一步才叫"被吃掉"，
                // 而不是整体调透明度（整体变淡那版是错的）
                clip(amount);

                // ---- 发亮的边缘 ----
                // 紧贴裁掉那一侧的窄条
                float edge = 1.0 - smoothstep(0.0, _EdgeWidth, amount);

                // ---- 颜色：溶解过程中往 _FadeTint 退，看起来像"材质在失去" ----
                half3 col = lerp(_BaseColor.rgb, _FadeTint.rgb, _DissolveAmount * 0.7);

                // 边缘加色叠上去（比底色亮很多，就是参考图里那条白边）
                col += _EdgeColor.rgb * (edge * _EdgeIntensity);

                // ---- 极简光照，保持立体感 ----
                float3 normalWS = normalize(i.normalWS);
                Light mainLight = GetMainLight();

                float ndl = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = _Ambient + mainLight.color * ndl;

                col *= lighting;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
