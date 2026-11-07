Shader "Dustborn/Menu Road Detail"
{
    Properties
    {
        _Strength("Wear Strength", Range(0, 1)) = 0.65
        _Wetness("Damp Patches", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Strength;
            float _Wetness;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float mask : TEXCOORD3;
        };
        float RoadNoise(float2 coordinates)
        {
            float2 cell = floor(coordinates);
            float2 blend = frac(coordinates);
            blend = blend * blend * (3 - 2 * blend);
            float4 h = frac(sin(float4(dot(cell, float2(127.1, 311.7)), dot(cell + float2(1, 0), float2(127.1, 311.7)),
                dot(cell + float2(0, 1), float2(127.1, 311.7)), dot(cell + 1, float2(127.1, 311.7)))) * 43758.5453);
            return lerp(lerp(h.x, h.y, blend.x), lerp(h.z, h.w, blend.x), blend.y);
        }
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            output.mask = input.color.a;
            return output;
        }
        float RoadMask(Varyings input)
        {
            return input.mask * smoothstep(5, 11, input.uv.y) * (1 - smoothstep(90, 110, input.uv.y));
        }
        half4 FragTint(Varyings input) : SV_Target
        {
            float broad = RoadNoise(input.positionWS.xz * 0.22 + 17.3);
            float breaks = RoadNoise(input.uv * float2(1.7, 0.6) + 43.7);
            float tracks = 1 - smoothstep(0.12, 0.36, abs(abs(input.uv.x) - 0.95));
            tracks *= lerp(0.15, 1, smoothstep(0.25, 0.75, breaks));
            float damp = smoothstep(0.48, 0.8, broad) * _Wetness;
            float wear = (broad * 0.12 + tracks * 0.24 + damp * 0.3) * RoadMask(input) * _Strength;
            float3 normalWS = normalize(input.normalWS);
            float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
            Light moon = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            float3 halfway = normalize(viewDirection + moon.direction);
            float reflection = pow(saturate(dot(normalWS, halfway)), 96) * damp * RoadMask(input);
            return half4(1 - wear * float3(0.85, 0.95, 1) + moon.color * moon.shadowAttenuation * reflection * 0.6, 1);
        }
        ENDHLSL
        Pass
        {
            Name "GroundWear"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTint
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            ENDHLSL
        }
    }
}
