Shader "Piloto Studio/Simply Toon"
{
    Properties
    {
        _MainTex("Base Color", 2D) = "white" {}
        [HDR] _RimColor("Rim Color", Color) = (0, 0.5549643, 1, 0)
        _RimOffset("Rim Offset", Range(0, 1)) = 0.24
        _RimFalloff("Rim Falloff", Vector) = (0, 0, 0, 0)
        _RimShadow("Rim Shadow", Range(0, 1)) = 0
        _Dimming("Dimming", Range(0, 1)) = 0.75
        _BandingBias("Banding Bias", Float) = 2
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.universal"
        }

        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _RimColor;
                float2 _RimFalloff;
                half _RimOffset;
                half _RimShadow;
                half _Dimming;
                half _BandingBias;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half3 lightDirection = mainLight.direction;
                half lightAmount = saturate(dot(normalWS, lightDirection) * 0.26h + 0.5h);
                half band = smoothstep(0.45h, 0.55h, lightAmount * max(_BandingBias, 0.001h) * 0.5h);
                half shade = lerp(0.75h, 1.0h, band) * _Dimming;

                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rimWidth = max(abs(_RimFalloff.y - _RimFalloff.x), 0.1h);
                half rim = smoothstep(_RimOffset, min(_RimOffset + rimWidth, 1.0h), 1.0h - saturate(dot(normalWS, viewDirection)));
                half rimStrength = lerp(1.0h, 0.25h, _RimShadow);

                return half4(baseColor.rgb * shade + baseColor.rgb * _RimColor.rgb * rim * rimStrength, 1.0h);
            }
            ENDHLSL
        }
    }

    SubShader
    {
        PackageRequirements
        {
            "com.unity.render-pipelines.high-definition"
        }

        Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _RimColor;
                float2 _RimFalloff;
                float _RimOffset;
                float _RimShadow;
                float _Dimming;
                float _BandingBias;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS);
                float3 lightDirection = float3(0.4, 0.8, 0.2);
                if (_DirectionalLightCount > 0)
                {
                    lightDirection = -_DirectionalLightDatas[0].forward;
                }

                float lightAmount = saturate(dot(normalWS, lightDirection) * 0.26 + 0.5);
                float band = smoothstep(0.45, 0.55, lightAmount * max(_BandingBias, 0.001) * 0.5);
                float shade = lerp(0.75, 1.0, band) * _Dimming;

                float4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rimWidth = max(abs(_RimFalloff.y - _RimFalloff.x), 0.1);
                float rim = smoothstep(_RimOffset, min(_RimOffset + rimWidth, 1.0), 1.0 - saturate(dot(normalWS, viewDirection)));
                float rimStrength = lerp(1.0, 0.25, _RimShadow);

                return float4(baseColor.rgb * shade + baseColor.rgb * _RimColor.rgb * rim * rimStrength, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}