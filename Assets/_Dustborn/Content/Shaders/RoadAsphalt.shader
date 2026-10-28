Shader "Dustborn/Road Asphalt"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap("Asphalt Strip", 2D) = "white" {}
        [MainColor] _BaseColor("Asphalt Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 0.3
        _MaskMap("Mask (R Cracks G Repairs B Dust A Solidity)", 2D) = "black" {}
        _MaskStrength("Authored Mask Strength", Range(0,1)) = 0
        _TileLength("Tile Length Metres", Float) = 8
        _Smoothness("Smoothness", Range(0,1)) = 0.08
        _MacroVariation("Macro Tint Variation", Range(0,0.3)) = 0.08
        [Header(Markings)]
        _EdgeColor("Edge Paint", Color) = (0.77,0.75,0.68,1)
        _CenterColor("Center Paint", Color) = (0.72,0.48,0.08,1)
        _EdgeLineWidth("Edge Line Width Metres", Float) = 0.12
        _EdgeLineInset("Edge Line Inset Metres", Float) = 0.4
        _CenterLineWidth("Center Line Width Metres", Float) = 0.12
        _DashLength("Dash Length Metres", Float) = 3
        _GapLength("Dash Gap Metres", Float) = 6
        _PaintWear("Paint Wear", Range(0,1)) = 0.32
        _DetailFadeStart("Detail Fade Start Metres", Float) = 180
        _DetailFadeEnd("Detail Fade End Metres", Float) = 600
        [Header(Edges)]
        _EdgeBand("Edge Band Fraction", Range(0.01,0.3)) = 0.08
        _EdgeBreakup("Edge Breakup", Range(0,1)) = 0.35
        [Header(Junctions)]
        _JunctionFillStart("Fill Start Fraction", Range(0,1)) = 0.68
        _JunctionFillEnd("Fill End Fraction", Range(0,1)) = 0.78
        _JunctionFillShift("Fill Source Shift", Range(0,2)) = 0.86
        _FilletFillShift("Fillet Source Shift", Range(0,2)) = 0.57
        _DepthBias("Depth Bias Metres", Range(0,0.1)) = 0.02
        [Header(Cover)]
        _CoverColor("Dust Or Snow Color", Color) = (0.5,0.45,0.35,1)
        _CoverAmount("Cover Amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EdgeColor;
            half4 _CenterColor;
            half4 _CoverColor;
            float _TileLength;
            float _EdgeLineWidth;
            float _EdgeLineInset;
            float _CenterLineWidth;
            float _DashLength;
            float _GapLength;
            float _DetailFadeStart;
            float _DetailFadeEnd;
            float _DepthBias;
            half _BumpScale;
            half _MaskStrength;
            half _Smoothness;
            half _MacroVariation;
            half _PaintWear;
            half _EdgeBand;
            half _EdgeBreakup;
            half _JunctionFillStart;
            half _JunctionFillEnd;
            half _JunctionFillShift;
            half _FilletFillShift;
            half _CoverAmount;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 profile : TEXCOORD1;
            float3 markings : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half3 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            float2 profile : TEXCOORD4;
            float3 markings : TEXCOORD5;
            half fog : TEXCOORD6;
            half3 vertexLight : TEXCOORD7;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453);
        }
        float Noise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f*f*(3.0-2.0*f);
            return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                        lerp(Hash(cell+float2(0,1)),Hash(cell+float2(1,1)),f.x),f.y);
        }
        Varyings RoadVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(output.positionWS);
            float3 view = TransformWorldToView(output.positionWS);
            float4 biased = mul(UNITY_MATRIX_P,float4(view+float3(0,0,_DepthBias),1));
            output.positionCS.z = biased.z / max(abs(biased.w),0.0001) * output.positionCS.w;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
            output.uv = input.uv;
            output.profile = input.profile;
            output.markings = input.markings;
            output.fog = ComputeFogFactor(output.positionCS.z);
            output.vertexLight = VertexLighting(output.positionWS,output.normalWS);
            return output;
        }
        float EdgeWeight(Varyings input)
        {
            return input.uv.x >= 0.0 ? input.markings.y : input.markings.z;
        }
        float DetailFade(float3 positionWS)
        {
            return 1.0-smoothstep(_DetailFadeStart,max(_DetailFadeEnd,_DetailFadeStart+1.0),distance(positionWS,GetCameraPositionWS()));
        }
        float2 RoadUv(Varyings input)
        {
            return float2(input.uv.x*0.5+0.5,input.uv.y/max(_TileLength,0.01));
        }
        float FilletWeight(Varyings input)
        {
            return 1.0-step(0.5,input.profile.y);
        }
        float2 FillUv(Varyings input)
        {
            float shift = lerp(_JunctionFillShift,_FilletFillShift,FilletWeight(input));
            float u = input.uv.x-(input.uv.x < 0.0 ? -shift : shift);
            return float2(u*0.5+0.5,input.uv.y/max(_TileLength,0.01)+0.37);
        }
        float JunctionFill(Varyings input)
        {
            float band = smoothstep(_JunctionFillStart,_JunctionFillEnd,abs(input.uv.x));
            return lerp((1.0-saturate(EdgeWeight(input)))*band,1.0-band,FilletWeight(input));
        }
        half4 RoadMask(Varyings input)
        {
            half4 mask = lerp(SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,RoadUv(input)),SAMPLE_TEXTURE2D(_MaskMap,sampler_MaskMap,FillUv(input)),JunctionFill(input));
            return lerp(half4(0,0,0,1),mask,_MaskStrength);
        }
        void ClipRoad(Varyings input,half4 mask)
        {
            float edge = smoothstep(1.0-_EdgeBand,1.0,abs(input.uv.x))*saturate(EdgeWeight(input));
            float breakup = Noise(input.positionWS.xz*2.7);
            clip(mask.a - edge - _EdgeBreakup*DetailFade(input.positionWS)*(breakup-0.35) + 0.001);
        }
        float Line(float distanceFromLine,float width,float aa)
        {
            return 1.0-smoothstep(width*0.5-aa,width*0.5+aa,abs(distanceFromLine));
        }
        half3 RoadNormal(Varyings input)
        {
            half3 normal = normalize(input.normalWS);
            half3 tangent = normalize(input.tangentWS-normal*dot(normal,input.tangentWS));
            half3 bitangent = normalize(cross(normal,tangent));
            half4 packed = lerp(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,RoadUv(input)),SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,FillUv(input)),JunctionFill(input));
            half3 normalTS = UnpackNormalScale(packed,_BumpScale);
            return normalize(tangent*normalTS.x+bitangent*normalTS.y+normal*normalTS.z);
        }
        half4 RoadFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 mask = RoadMask(input);
            ClipRoad(input,mask);
            half3 albedo = lerp(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,RoadUv(input)),SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,FillUv(input)),JunctionFill(input)).rgb*_BaseColor.rgb;
            float macro = Noise(input.positionWS.xz*0.015);
            albedo *= 1.0+(macro*2.0-1.0)*_MacroVariation;
            albedo *= 1.0-mask.g*0.15;
            albedo = lerp(albedo,_CoverColor.rgb,mask.b*0.2);
            float lateral = input.uv.x*input.profile.x;
            float aa = max(fwidth(lateral),0.015);
            float style = input.profile.y;
            float edgePaint = Line(abs(lateral)-(input.profile.x-_EdgeLineInset),_EdgeLineWidth,aa)*step(1.5,style);
            float centerPaint = Line(lateral,_CenterLineWidth,aa)*step(1.5,style);
            float cycle = max(_DashLength+_GapLength,0.01);
            float phase = frac(input.uv.y/cycle)*cycle;
            float dashAa = max(fwidth(input.uv.y),0.02);
            float dash = smoothstep(0,dashAa,phase)*(1.0-smoothstep(_DashLength-dashAa,_DashLength+dashAa,phase));
            centerPaint *= lerp(1.0,dash,step(2.5,style));
            float wear = smoothstep(_PaintWear*0.7,_PaintWear*0.7+0.3,Noise(input.positionWS.xz*4.0));
            float paint = DetailFade(input.positionWS)*wear*(1.0-mask.r);
            albedo = lerp(albedo,_EdgeColor.rgb,edgePaint*paint*saturate(EdgeWeight(input)));
            albedo = lerp(albedo,_CenterColor.rgb,centerPaint*paint*saturate(input.markings.x));
            albedo = lerp(albedo,_CoverColor.rgb,_CoverAmount*(0.5+0.5*macro));
            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            data.normalWS = RoadNormal(input);
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                data.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
            #else
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            data.fogCoord = input.fog;
            data.vertexLighting = input.vertexLight;
            data.bakedGI = SampleSH(data.normalWS);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            data.shadowMask = half4(1,1,1,1);
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = albedo;
            surface.alpha = 1;
            surface.normalTS = half3(0,0,1);
            surface.smoothness = _Smoothness*(1.0-mask.b);
            surface.occlusion = 1;
            half4 color = UniversalFragmentPBR(data,surface);
            color.rgb = MixFog(color.rgb,input.fog);
            return color;
        }
        half4 RoadDepth(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ClipRoad(input,RoadMask(input));
            return 0;
        }
        half4 RoadDepthNormals(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ClipRoad(input,RoadMask(input));
            half3 normal = RoadNormal(input);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
            #else
                return half4(normal,0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoadVertex
            #pragma fragment RoadFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoadVertex
            #pragma fragment RoadDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoadVertex
            #pragma fragment RoadDepthNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
