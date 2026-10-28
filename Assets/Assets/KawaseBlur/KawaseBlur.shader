Shader "Custom/RenderFeature/KawaseBlur"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "KawaseBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _offset;

            half3 Tap(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 step = _offset * _BlitTexture_TexelSize.xy;

                half3 color = Tap(uv);
                color += Tap(uv + float2(step.x, step.y));
                color += Tap(uv + float2(step.x, -step.y));
                color += Tap(uv + float2(-step.x, step.y));
                color += Tap(uv + float2(-step.x, -step.y));

                return half4(color / 5.0, 1.0);
            }
            ENDHLSL
        }
    }
}
