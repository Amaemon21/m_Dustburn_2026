Shader "BiomeGrass/2D Recolor Wind URP"
{
    Properties
    {
        [MainTexture] _BaseMap("White Plant Texture (RGBA)", 2D) = "white" {}
        [NoScaleOffset] _ColorMask("Color Mask (R Leaves G Stems B Flowers)", 2D) = "red" {}
        [MainColor] _LeafColor("Leaves / Grass Color (R)", Color) = (0.42,0.5,0.24,1)
        _StemColor("Stems / Branches Color (G)", Color) = (0.32,0.27,0.15,1)
        _FlowerColor("Flowers / Seeds Color (B)", Color) = (0.8,0.62,0.3,1)
        _TipTint("Upper Part Tint (Multiply)", Color) = (1,1,1,1)
        _RootDarkening("Root Darkening", Range(0,0.8)) = 0.15
        _Variation("Per Object Color Variation", Range(0,0.35)) = 0.08
        _TextureShading("White Texture Facet Shading", Range(0,1)) = 1
        _Cutoff("Alpha Cutoff", Range(0.01,0.95)) = 0.35
        _FadeStart("Fade Start", Float) = 86.4
        _FadeEnd("Fade End", Float) = 96
        [Toggle] _AlphaToCoverage("Alpha To Coverage (MSAA Only)", Float) = 0
        _SnowColor("Snow Color", Color) = (0.92,0.96,1,1)
        _SnowAmount("Snow Amount", Range(0,1)) = 0
        _SnowStart("Snow Start (UV Height)", Range(0,0.95)) = 0.5
        _WindDirection("World Wind Direction (X Z)", Vector) = (1,0,0.3,0)
        _WindStrength("Wind Sway (World Metres)", Range(0,0.5)) = 0.08
        _WindSpeed("Wind Speed", Range(0,6)) = 1.6
        _WindScale("World Wind Wave Frequency", Range(0.1,5)) = 0.9
        _WindRoot("Pinned Root Height (UV)", Range(0,0.5)) = 0.13
        _WindExponent("Bending Towards Tips", Range(1,4)) = 2
        _Flutter("Texture Flutter (UV)", Range(0,0.02)) = 0.003
        _Roundness("Card Normal Roundness", Range(0,2)) = 0.7
        _NormalUp("Upward Normal Bias", Range(0,1)) = 0.3
        [Toggle(_FACET_RELIEF)] _EnableFacetRelief("Enable Texture Relief (Extra Samples)", Float) = 0
        _FacetRelief("Facet Relief From White Texture", Range(0,5)) = 0.8
        _Translucency("Leaf Backlighting", Range(0,1)) = 0.18
        _WrapLight("Soft Wrap Lighting", Range(0,1)) = 0.25
        _AmbientStrength("Ambient Light Strength", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" "DisableBatching"="True" }
        Cull Off
        ZWrite On
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BaseMap_TexelSize;
        half4 _LeafColor, _StemColor, _FlowerColor, _TipTint, _SnowColor;
        float4 _WindDirection;
        float _WindStrength, _WindSpeed, _WindScale, _WindRoot, _WindExponent, _Flutter, _FadeStart, _FadeEnd;
        half _RootDarkening, _Variation, _TextureShading, _Cutoff, _AlphaToCoverage;
        half _SnowAmount, _SnowStart, _Roundness, _NormalUp, _FacetRelief, _EnableFacetRelief;
        half _Translucency, _WrapLight, _AmbientStrength;
        CBUFFER_END
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
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            float3 phaseFogVariation : TEXCOORD4;
            half3 vertexLight : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        float HeightWeight(float y)
        {
            return pow(saturate((y - _WindRoot) / max(1.0 - _WindRoot, 0.01)), _WindExponent);
        }
        float3 WindPosition(Attributes v, float phase)
        {
            float3 p = TransformObjectToWorld(v.positionOS.xyz);
            float2 direction = _WindDirection.xz;
            direction *= rsqrt(max(dot(direction, direction), 0.0001));
            float t = _Time.y * _WindSpeed + phase;
            float sway = sin(t) * 0.7 + sin(t * 1.83 + 0.7) * 0.3;
            p.xz += direction * sway * _WindStrength * HeightWeight(v.uv.y);
            return p;
        }
        Varyings GrassVert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 pivotWS = TransformObjectToWorld(float3(0,0,0));
            float phase = dot(pivotWS.xz, float2(0.71, 1.13)) * _WindScale;
            o.positionWS = WindPosition(v, phase);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            float3 normalOS = normalize(v.normalOS);
            float3 fallbackAxis = abs(normalOS.y) < 0.99 ? float3(0,1,0) : float3(0,0,1);
            float3 tangentOS = dot(v.tangentOS.xyz, v.tangentOS.xyz) > 0.01 ? v.tangentOS.xyz : normalize(cross(fallbackAxis, normalOS));
            o.tangentWS = half4(TransformObjectToWorldDir(tangentOS), v.tangentOS.w == 0 ? 1 : v.tangentOS.w * GetOddNegativeScale());
            o.uv = v.uv;
            float random = frac(sin(dot(pivotWS.xz, float2(12.9898,78.233))) * 43758.5453);
            o.phaseFogVariation = float3(phase, ComputeFogFactor(o.positionCS.z), random);
            o.vertexLight = VertexLighting(o.positionWS, o.normalWS);
            return o;
        }
        float2 PlantUV(Varyings i)
        {
            float2 uv = i.uv;
            float flutter = sin(uv.y * 13 + _Time.y * _WindSpeed * 2.1 + i.phaseFogVariation.x);
            uv.x += flutter * _Flutter * saturate(_WindStrength * 20) * HeightWeight(uv.y);
            return TRANSFORM_TEX(uv, _BaseMap);
        }
        float Dither(float2 screen)
        {
            return frac(dot(screen, float2(0.75487766624669276, 0.56984029099805327)));
        }
        float Coverage(float3 positionWS)
        {
            float range = distance(positionWS, GetCameraPositionWS());
            return 1.0 - saturate((range - _FadeStart) / max(0.001, _FadeEnd - _FadeStart));
        }
        half4 PlantBase(float2 uv, float3 positionWS, float2 screen)
        {
            half4 b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            clip(b.a - _Cutoff);
            clip(Coverage(positionWS) - Dither(screen));
            return b;
        }
        half3 PlantWeights(float2 uv)
        {
            half3 w = max(SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).rgb, 0);
            half total = w.r + w.g + w.b;
            return total > 0.001h ? w / total : half3(1,0,0);
        }
        half3 PlantNormal(Varyings i, float2 uv, half faceSign)
        {
            half3 n = normalize(i.normalWS) * faceSign;
            half3 t = normalize(i.tangentWS.xyz - n * dot(n, i.tangentWS.xyz));
            half3 b = normalize(cross(n, t)) * i.tangentWS.w * faceSign;
            half2 slope = half2((i.uv.x - 0.5h) * 2 * _Roundness, _NormalUp);
            #if defined(_FACET_RELIEF)
            float2 dx = float2(_BaseMap_TexelSize.x * 2, 0);
            float2 dy = float2(0, _BaseMap_TexelSize.y * 2);
            half4 l = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - dx);
            half4 r = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + dx);
            half4 d = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - dy);
            half4 u = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + dy);
            half valid = min(min(l.a, r.a), min(d.a, u.a));
            slope -= half2(r.r - l.r, u.r - d.r) * _FacetRelief * valid;
            #endif
            return normalize(n + t * slope.x + b * slope.y);
        }
        half3 PlantLight(half3 n, half3 viewDir, half leafWeight, Light light)
        {
            half ndl = saturate((dot(n, light.direction) + _WrapLight) / (1 + _WrapLight));
            half back = pow(saturate(dot(-viewDir, light.direction)), 3) * _Translucency * leafWeight;
            return (ndl + back) * light.color * light.distanceAttenuation * light.shadowAttenuation;
        }
        half4 GrassFrag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 uv = PlantUV(i);
            half4 base = PlantBase(uv, i.positionWS, i.positionCS.xy);
            half3 w = PlantWeights(uv);
            half3 tint = w.r * _LeafColor.rgb + w.g * _StemColor.rgb + w.b * _FlowerColor.rgb;
            tint *= lerp(half3(1,1,1), _TipTint.rgb, saturate(i.uv.y));
            tint *= 1 + (i.phaseFogVariation.z * 2 - 1) * _Variation;
            half snow = smoothstep(_SnowStart, 1, i.uv.y) * _SnowAmount;
            tint = lerp(tint, _SnowColor.rgb, snow);
            half3 albedo = tint * lerp(half3(1,1,1), base.rgb, _TextureShading);
            albedo *= 1 - _RootDarkening * (1 - smoothstep(0, 0.55, i.uv.y));
            half3 n = PlantNormal(i, uv, IS_FRONT_VFACE(face, 1.0h, -1.0h));
            InputData inputData = (InputData)0;
            inputData.positionWS = i.positionWS;
            inputData.normalWS = n;
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            float4 shadowCoord;
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
            #else
                shadowCoord = TransformWorldToShadowCoord(i.positionWS);
            #endif
            half leafWeight = w.r + w.b;
            half3 lighting = max(SampleSH(n), 0.0h) * _AmbientStrength;
            lighting += PlantLight(n, inputData.viewDirectionWS, leafWeight, GetMainLight(shadowCoord));
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += i.vertexLight;
            #endif
            #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    lighting += PlantLight(n, inputData.viewDirectionWS, leafWeight, light);
                }
                #endif
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, i.positionWS, half4(1,1,1,1));
                    lighting += PlantLight(n, inputData.viewDirectionWS, leafWeight, light);
                LIGHT_LOOP_END
            #endif
            return half4(MixFog(albedo * lighting, i.phaseFogVariation.y), base.a);
        }
        half4 GrassDepth(Varyings i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            PlantBase(PlantUV(i), i.positionWS, i.positionCS.xy);
            return 0;
        }
        half4 GrassNormals(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 uv = PlantUV(i);
            PlantBase(uv, i.positionWS, i.positionCS.xy);
            float3 n = PlantNormal(i, uv, IS_FRONT_VFACE(face, 1.0h, -1.0h));
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
            #else
                return half4(n,0);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            AlphaToMask [_AlphaToCoverage]
            HLSLPROGRAM
            #pragma vertex GrassVert
            #pragma fragment GrassFrag
            #pragma shader_feature_local_fragment _FACET_RELIEF
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex GrassShadow
            #pragma fragment GrassDepth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings GrassShadow(Attributes v)
            {
                Varyings o = GrassVert(v);
                UNITY_SETUP_INSTANCE_ID(v);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - o.positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, n, lightDir));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex GrassVert
            #pragma fragment GrassDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma vertex GrassVert
            #pragma fragment GrassNormals
            #pragma shader_feature_local_fragment _FACET_RELIEF
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
