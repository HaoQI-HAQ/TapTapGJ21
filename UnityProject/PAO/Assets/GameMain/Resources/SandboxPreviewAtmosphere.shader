Shader "PAO/SandboxPreviewAtmosphere"
{
    Properties
    {
        _StageOrigin("Stage origin", Vector) = (0,0,0,0)
        _BoundsMin("Volume minimum", Vector) = (-10,-1,-10,0)
        _BoundsMax("Volume maximum", Vector) = (10,10,10,0)
        _FloorY("Floor", Float) = -1
        _ContentHeight("Work height", Float) = 10
        _StageRadius("Stage radius", Float) = 10
        _PreviewTime("Unscaled slow drift", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _StageOrigin, _BoundsMin, _BoundsMax;
                float _FloorY, _ContentHeight, _StageRadius, _PreviewTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.world); return output;
            }
            float Hash(float3 p)
            { p = frac(p * .1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float Noise(float3 p)
            {
                float3 cell = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x),
                    lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x),
                    lerp(Hash(cell + float3(0,1,1)), Hash(cell + 1), f.x), f.y), f.z);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 camera = GetCameraPositionWS();
                float3 origin = camera - _StageOrigin.xyz;
                float3 ray = normalize(input.world - camera);
                float3 safeRay = (step(0, ray) * 2 - 1) * max(abs(ray), .00001);
                float3 a = (_BoundsMin.xyz - origin) / safeRay;
                float3 b = (_BoundsMax.xyz - origin) / safeRay;
                float3 nearPlane = min(a, b), farPlane = max(a, b);
                float entry = max(0, max(nearPlane.x, max(nearPlane.y, nearPlane.z)));
                float exit = min(farPlane.x, min(farPlane.y, farPlane.z));
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 scenePosition = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                exit = min(exit, distance(scenePosition, camera));
                if (exit <= entry) return 0;
                const int steps = 24;
                float stride = (exit - entry) / steps;
                // A stable midpoint avoids full-screen stipple and temporal shimmer.
                float jitter = .5;
                float height = max(2, _ContentHeight), radius = max(8, _StageRadius);
                float3 lamp = float3(-radius * .36, _FloorY + height * 1.65, -radius * .2);
                float3 lampDir = normalize(float3(radius * .28, -height * 1.65, radius * .2));
                float4 fog = 0;
                [loop] for (int s = 0; s < steps; s++)
                {
                    float3 p = origin + ray * (entry + (s + jitter) * stride);
                    float floorHeight = max(0, p.y - _FloorY);
                    float radial = length(p.xz / radius);
                    float boundary = 1 - smoothstep(.45, .98, radial);
                    float noise = Noise(p / height * 5 + float3(_PreviewTime, 0, -_PreviewTime * .6));
                    float lowMist = exp(-floorHeight / (height * .085)) * (.3 + noise * .9);
                    float clearAroundWork = 1 - .7 * exp(-dot(p.xz, p.xz) / (height * height * .11));
                    float3 fromLamp = p - lamp;
                    float axial = dot(fromLamp, lampDir);
                    float beamRadius = max(.01, axial * .44);
                    float beam = exp(-dot(fromLamp - lampDir * axial, fromLamp - lampDir * axial) / (beamRadius * beamRadius));
                    beam *= smoothstep(0, height * .2, axial);
                    float density = (lowMist * .9 * clearAroundWork + beam * .05) * boundary;
                    float alpha = 1 - exp(-density * stride / height);
                    float pool = exp(-dot(p.xz / radius, p.xz / radius) * 2.6);
                    float3 light = float3(.11, .13, .16) + float3(.95, .66, .34) * (beam * .9 + pool * .22);
                    fog.rgb += (1 - fog.a) * light * alpha;
                    fog.a += (1 - fog.a) * alpha;
                }
                return half4(fog);
            }
            ENDHLSL
        }
    }
}
