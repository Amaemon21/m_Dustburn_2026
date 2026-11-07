Shader "Dustborn/Menu Ground Mist"
{
    Properties
    {
        [HDR] _Tint("Mist Color", Color) = (0.19, 0.245, 0.32, 0.3)
        _Seed("Patch Seed", Float) = 1
        _DepthFade("Surface Fade Distance", Range(0.1, 8)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Seed;
                float _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
            };

            float Hash(float2 coordinates)
            {
                return frac(sin(dot(coordinates, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 coordinates)
            {
                float2 cell = floor(coordinates);
                float2 blend = frac(coordinates);
                blend = blend * blend * (3.0 - 2.0 * blend);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1, 0)), blend.x),
                    lerp(Hash(cell + float2(0, 1)), Hash(cell + float2(1, 1)), blend.x), blend.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.eyeDepth = -position.positionVS.z;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float horizontal = pow(saturate(1.0 - abs(uv.x * 2.0 - 1.0)), 1.5);
                float plume = Noise(float2(uv.x * 4.3 + _Seed * 1.37, _Seed * 2.71));
                float heightOffset = (plume - 0.5) * 0.42;
                float shapedHeight = uv.y + heightOffset;
                float vertical = smoothstep(0.02, 0.27, shapedHeight) * (1.0 - smoothstep(0.38, 0.93, shapedHeight));
                float borderFade = smoothstep(0.0, 0.12, uv.y) * (1.0 - smoothstep(0.82, 1.0, uv.y));
                float openings = Noise(float2(uv.x * 6.1 + _Seed * 4.19, _Seed * 5.31));
                float coverage = lerp(0.25, 1.0, smoothstep(0.25, 0.72, openings));
                float2 noiseUV = uv * float2(13.0, 3.5) + _Seed * float2(7.13, 3.71);
                float patches = Noise(noiseUV) * 0.65 + Noise(noiseUV * 2.17 + 19.1) * 0.35;
                float mask = horizontal * vertical * borderFade * coverage * smoothstep(0.18, 0.8, patches);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceFade = saturate((sceneDepth - input.eyeDepth) / max(0.01, _DepthFade));
                float distanceFade = smoothstep(18.0, 35.0, input.eyeDepth);
                return half4(_Tint.rgb, saturate(mask * surfaceFade * distanceFade * _Tint.a));
            }
            ENDHLSL
        }
    }
}
