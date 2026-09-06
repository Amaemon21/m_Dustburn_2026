#ifndef REPETITIONLESSTERRAINDEPTHONLYPASS_INCLUDED
#define REPETITIONLESSTERRAINDEPTHONLYPASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 clipPos  : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings DepthOnlyVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    TerrainInstancing(input.positionOS, input.normalOS, input.texcoord);

    output.clipPos = TransformObjectToHClip(input.positionOS.xyz);
    output.texcoord = input.texcoord;
    return output;
}

half4 DepthOnlyFragment(Varyings input) : SV_TARGET
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.texcoord);
#endif
#ifdef SCENESELECTIONPASS
    // We use depth prepass for scene selection in the editor, this code allow to output the outline correctly
    return half4(_ObjectId, _PassValue, 1.0, 1.0);
#endif
    return input.clipPos.z;
}
#endif