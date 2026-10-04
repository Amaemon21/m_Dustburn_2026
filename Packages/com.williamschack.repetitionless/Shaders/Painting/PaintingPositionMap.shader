Shader "Hidden/Repetitionless/PaintingPositionMap"
{
    SubShader
    {
        Pass
        {
            ZTest Off
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            int _FlipY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv       : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position      : SV_POSITION;
                float3 localPosition : TEXCOORD0; 
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float2 uv = input.uv;
                if (_FlipY == 1)
                    uv.y = 1 - uv.y;
                uv = uv * 2 - 1;

                output.position = float4(uv, 0, 1);
                output.localPosition = input.positionOS.xyz;

                return output;
            }

            float4 Frag(Varyings input) : SV_TARGET
            {
                return float4(input.localPosition, 1.0);
            }
            ENDCG
        }
    }
}
