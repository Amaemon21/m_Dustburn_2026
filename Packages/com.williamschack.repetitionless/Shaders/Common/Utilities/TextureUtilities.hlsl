#ifndef TEXTUREUTILITIES_INCLUDED
#define TEXTUREUTILITIES_INCLUDED

float Remap(float In, float2 InMinMax, float2 OutMinMax)
{
    return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
}

float2 RotateUVDegrees(float2 UV, float2 Center, float Rotation)
{
    Rotation = Rotation * (3.1415926f / 180.0f);
    UV -= Center;
    float s = sin(Rotation);
    float c = cos(Rotation);
    float2x2 rMatrix = float2x2(c, -s, s, c);
    rMatrix *= 0.5;
    rMatrix += 0.5;
    rMatrix = rMatrix * 2 - 1;
    UV.xy = mul(UV.xy, rMatrix);
    UV += Center;
    return UV;
}

half3 UnpackNormalMap(float4 PackedNormal, float Strength = 1.0)
{
    half3 normal = half3(PackedNormal.xy, 0);
    normal.xy = PackedNormal.xy * 2 - 1;
    normal.xy *= Strength;
    normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));

    return normalize(normal);
}

#endif