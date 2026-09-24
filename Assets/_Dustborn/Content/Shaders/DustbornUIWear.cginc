#ifndef DUSTBORN_UI_WEAR_INCLUDED
#define DUSTBORN_UI_WEAR_INCLUDED

// Local coordinates are Canvas reference units, not atlas UV coordinates.
// No _Time: the pattern remains fixed when the UI moves or is redrawn.
sampler2D _MainTex;
sampler2D _WearTex;
float4 _MainTex_TexelSize;
float4 _TextureSampleAdd;
float _Shape, _SurfaceStrength, _GrainSize;
float _TextureInfluence, _TextureTileSize, _TextureContrast;
float _EdgeRoughness, _EdgeChipSize;
float _CrackAmount, _CrackWidth, _CrackDepth, _CrackSpacing;
float _TaperStart, _TaperEnd, _TipPower, _LineFeather;
float _PatternSeed;

float DW_Hash(float2 p)
{
    float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float DW_Noise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = DW_Hash(cell);
    float b = DW_Hash(cell + float2(1, 0));
    float c = DW_Hash(cell + float2(0, 1));
    float d = DW_Hash(cell + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float DW_EdgeNoise(float2 p)
{
    float2 q = p / max(_EdgeChipSize, 0.25);
    return 0.60 * DW_Noise(q) + 0.28 * DW_Noise(q * 2.37 + 13.7)
         + 0.12 * DW_Noise(q * 5.13 + 7.1);
}

float2 DW_ClampSpriteUV(float2 uv, float4 bounds)
{
    float2 halfTexel = min(abs(_MainTex_TexelSize.xy) * 0.5,
                          max(bounds.zw - bounds.xy, 0.0) * 0.49);
    return clamp(uv, bounds.xy + halfTexel, bounds.zw - halfTexel);
}

// Samples outside THIS sprite are transparent, even when packed in an atlas.
float DW_Alpha(float2 uv, float4 bounds)
{
    float2 inside = step(bounds.xy, uv) * step(uv, bounds.zw);
    return saturate(tex2D(_MainTex, DW_ClampSpriteUV(uv, bounds)).a + _TextureSampleAdd.a)
         * inside.x * inside.y;
}

float DW_NearAlpha(float2 uv, float2 ux, float2 uy, float4 bounds, float radius)
{
    float a = DW_Alpha(uv + ux * radius, bounds);
    a = min(a, DW_Alpha(uv - ux * radius, bounds));
    a = min(a, DW_Alpha(uv + uy * radius, bounds));
    return min(a, DW_Alpha(uv - uy * radius, bounds));
}

// A sparse, slightly bent short incision. Its length is limited by the edge band.
float DW_CrackStripe(float along, float across, float seed)
{
    float spacing = max(_CrackSpacing, 3.0);
    float cell = floor(along / spacing);
    float chance = DW_Hash(float2(cell, seed + 27.0));
    float enabledCrack = (1.0 - step(_CrackAmount, chance)) * step(0.0001, _CrackWidth);
    float h = DW_Hash(float2(cell + 19.0, seed));
    float center = (cell + lerp(0.20, 0.80, h)) * spacing;
    float lean = (DW_Hash(float2(cell + 7.0, seed + 3.0)) - 0.5) * 0.6;
    float bend = (DW_Noise(float2(across * 0.45, cell + seed)) - 0.5) * 0.65;
    float delta = along - center + bend + sin(across * 0.12 + h * 6.28) * lean;
    float width = _CrackWidth * lerp(0.7, 1.2, h);
    float aa = max(fwidth(delta) * 0.5, 0.08);
    return enabledCrack * (1.0 - smoothstep(width * 0.5 - aa, width * 0.5 + aa, abs(delta)));
}

float DW_Cracks(float2 p, float seed)
{
    if (_CrackAmount <= 0.0 || _CrackDepth <= 0.0)
        return 0.0;
    return max(DW_CrackStripe(p.x, p.y, seed),
               DW_CrackStripe(p.y, p.x, seed + 113.0));
}

float DW_LineDistance(float2 p, float2 size)
{
    bool vertical = _Shape > 1.5;
    float along = vertical ? p.y : p.x;
    float across = vertical ? p.x : p.y;
    float axisLength = max(vertical ? size.y : size.x, 0.001);
    float axisWidth = max(vertical ? size.x : size.y, 0.001);
    float start = max(_TaperStart, 0.0);
    float end = max(_TaperEnd, 0.0);
    float fit = min(1.0, axisLength / max(start + end, 0.001));
    start *= fit;
    end *= fit;
    float s = start > 0.0 ? saturate(along / max(start, 0.001)) : 1.0;
    float e = end > 0.0 ? saturate((axisLength - along) / max(end, 0.001)) : 1.0;
    float taper = pow(min(s, e), max(_TipPower, 0.1));
    float side = axisWidth * 0.5 * taper - abs(across - axisWidth * 0.5);
    return min(side, min(along, axisLength - along));
}

float4 DW_Shade(float2 uv, float2 p, float2 size, float4 bounds, float seed, float4 tint)
{
    // Missing UV component: show the ordinary sprite instead of destroying it.
    if (min(size.x, size.y) < 0.001)
        return (tex2D(_MainTex, uv) + _TextureSampleAdd) * tint;
    // Clamp colour as well as alpha: magnification must not pick up atlas neighbours.
    float4 base = tex2D(_MainTex, DW_ClampSpriteUV(uv, bounds)) + _TextureSampleAdd;

    seed = frac((seed + _PatternSeed) * 0.01371) * 997.0;
    float2 pattern = p + float2(seed * 1.37, seed * 0.73);
    float rough = _EdgeRoughness * pow(DW_EdgeNoise(pattern), 1.4);
    float cracks = DW_Cracks(pattern, seed);
    float alpha;

    if (_Shape > 0.5)
    {
        // The line is an actual narrowing silhouette. It is not an opacity fade.
        float distance = DW_LineDistance(p, size);
        float aa = max(max(fwidth(distance) * 0.5, _LineFeather), 0.08);
        alpha = smoothstep(-aa, aa, distance - rough);
        float band = 1.0 - smoothstep(0.0, max(_CrackDepth, 0.001), max(distance, 0.0));
        alpha *= 1.0 - saturate(cracks * band * 1.5);
        base.rgb = float3(1, 1, 1);
    }
    else
    {
        alpha = saturate(base.a);
        // Invert the screen-to-local Jacobian. This keeps wear in local units
        // through Canvas scaling, rotation and the piecewise UVs of 9-slicing.
        float2 dx = ddx(p);
        float2 dy = ddy(p);
        float2 tx = ddx(uv);
        float2 ty = ddy(uv);
        float det = dx.x * dy.y - dx.y * dy.x;
        float safeDet = abs(det) > 1e-10 ? det : (det < 0.0 ? -1e-10 : 1e-10);
        float2 ux = (tx * dy.y - ty * dx.y) / safeDet;
        float2 uy = (ty * dx.x - tx * dy.x) / safeDet;
        if (_EdgeRoughness > 0.0)
            alpha = min(alpha, DW_NearAlpha(uv, ux, uy, bounds, rough));
        if (_CrackAmount > 0.0 && _CrackDepth > 0.0)
        {
            float core = DW_NearAlpha(uv, ux, uy, bounds, _CrackDepth);
            float band = saturate((base.a - core) / max(base.a, 0.0001));
            alpha *= 1.0 - saturate(cracks * band * 1.5);
        }
    }

    float grain = 0.65 * DW_Noise(pattern / max(_GrainSize, 0.25))
                + 0.35 * DW_Noise(pattern / max(_GrainSize * 3.8, 0.25) + 31.2);
    if (_TextureInfluence > 0.0)
    {
        // Only a scalar is taken from the texture: its colour is never applied.
        float3 texGrain = tex2D(_WearTex, pattern / max(_TextureTileSize, 1.0)).rgb;
        float gray = dot(texGrain, float3(0.2126, 0.7152, 0.0722));
        gray = saturate((gray - 0.5) * _TextureContrast + 0.5);
        grain = lerp(grain, gray, saturate(_TextureInfluence));
    }
    float gain = 1.0 - saturate(_SurfaceStrength) * (1.0 - grain);
    return float4(base.rgb * gain, saturate(alpha)) * tint;
}
#endif
