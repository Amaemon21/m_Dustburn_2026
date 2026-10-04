Shader "Dustborn/GrassCard"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.4
        _FadeStart("Fade Start", Float) = 72
        _FadeEnd("Fade End", Float) = 96
        _WindStrength("Wind Strength", Float) = 0.14
        _WindSpeed("Wind Speed", Float) = 1.1
        _WindFrequency("Wind Frequency", Float) = 0.09
        _NormalBlend("Normal Blend To Up", Range(0, 1)) = 0.45
        _Translucency("Translucency", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            float _FadeStart;
            float _FadeEnd;
            float _WindStrength;
            float _WindSpeed;
            float _WindFrequency;
            half _NormalBlend;
            half _Translucency;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // The card is two crossed quads with uv.y running from 0 at the root to 1 at the tip, so uv.y
        // squared is the bend: the root stays planted and only the top travels.
        float3 Sway(float3 positionWS, float height)
        {
            float phase = (positionWS.x + positionWS.z) * _WindFrequency + _Time.y * _WindSpeed;
            float wave = sin(phase) + 0.5 * sin(phase * 2.3 + 1.7);

            positionWS.xz += wave * _WindStrength * height * height;

            return positionWS;
        }

        // Screen-space dither, R2 low discrepancy sequence. Clipping against it turns a hard cut-off
        // into a dissolve without a transparent queue and without sorting.
        float Dither(float2 screen)
        {
            return frac(dot(screen, float2(0.75487766624669276, 0.56984029099805327)));
        }

        float Coverage(float3 positionWS)
        {
            float range = distance(positionWS, GetCameraPositionWS());

            return 1.0 - saturate((range - _FadeStart) / max(0.001, _FadeEnd - _FadeStart));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float coverage : TEXCOORD4;
                float3 faceWS : TEXCOORD5;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = Sway(TransformObjectToWorld(input.positionOS.xyz), input.uv.y);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.faceWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.coverage = Coverage(positionWS);

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                clip(albedo.a - _Cutoff);
                clip(input.coverage - Dither(input.positionCS.xy));

                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // The card is drawn from both sides, so the plane normal is turned toward the viewer
                // instead of asking for a facing semantic, then bent toward up: a blade lit purely by
                // its own plane reads as cardboard, one lit purely by up takes no light at all.
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 face = normalize(input.faceWS);

                face = dot(face, view) < 0.0 ? -face : face;

                half3 normalWS = normalize(lerp(face, normalize(input.normalWS), _NormalBlend));

                half wrapped = saturate(dot(normalWS, main.direction) * 0.5 + 0.5);
                half through = saturate(dot(-face, main.direction)) * _Translucency;

                half3 lighting = main.color * ((wrapped * wrapped + through) * main.shadowAttenuation) + SampleSH(normalWS);
                half3 color = albedo.rgb * lighting;

                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float coverage : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = Sway(TransformObjectToWorld(input.positionOS.xyz), input.uv.y);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.coverage = Coverage(positionWS);

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;

                clip(alpha - _Cutoff);
                clip(input.coverage - Dither(input.positionCS.xy));

                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Simple Lit"
}
