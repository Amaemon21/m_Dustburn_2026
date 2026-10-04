// Dustborn: standalone text version of the UI Wear shader.
// Target: Unity 6 / URP / TextMeshProUGUI with an SDF font atlas.
// No Dustborn scripts, custom inspector or external .cginc are required.
//
// SETUP: Duplicate the MATERIAL belonging to your TMP font (not the font asset).
// Keep its Font Atlas and TMP font metrics. Set its shader to:
// Dustborn/UI/Worn Text TMP, then assign it to the text's Material Preset.
// Do not add the DustbornUIWear Image component to a text object.
// Wear sizes are local UI units; a 1920x1080 reference Canvas at 1080p and
// transform scale 1 maps one such unit to one pixel.
// Optional Wear Texture: sRGB OFF, Wrap Repeat. Texture Influence 0 needs no texture.
// For a clean face: Surface Strength, Edge Loss and Crack Amount = 0.
// SDF fonts only. Bitmap/color fonts, TMP <sprite> and underlay are not handled.
// Core math has been checked outside Unity; validate import in your Unity Editor.

Shader "Dustborn/UI/Worn Text TMP"
{
    Properties
    {
        [Header(Text)]
        [NoScaleOffset] _MainTex ("Font Atlas - сохранить от материала шрифта", 2D) = "white" {}
        _FaceColor ("Цвет материала - обычно белый", Color) = (1,1,1,1)
        _FaceDilate ("Толщина букв", Range(-1,1)) = 0
        _OutlineColor ("Цвет обводки", Color) = (0,0,0,1)
        _OutlineWidth ("Толщина обводки", Range(0,1)) = 0
        _OutlineSoftness ("Мягкость контура", Range(0,1)) = 0

        [Header(Effects)]
        [ToggleUI] _UseWear ("Потёртости и рваность", Float) = 1
        [ToggleUI] _UseGlow ("Свечение", Float) = 0

        [Header(Glow)]
        [HDR] _GlowColor ("Цвет свечения - белый = свой цвет", Color) = (1, 1, 1, 1)
        _GlowIntensity ("Яркость свечения - Bloom ярче 1,1", Range(0,16)) = 2
        _GlowPulseSpeed ("Скорость пульсации", Float) = 0
        _GlowPulseAmount ("Сила пульсации", Range(0,1)) = 0

        [Header(Surface)]
        _SurfaceStrength ("Сила потёртостей", Range(0,1)) = 0.25
        _GrainSize ("Размер зерна - единицы UI", Range(0.25,8)) = 0.9
        [NoScaleOffset] _WearTex ("Текстура фактуры - необязательно", 2D) = "gray" {}
        _TextureInfluence ("Доля текстуры - 0 без текстуры", Range(0,1)) = 0
        _TextureTileSize ("Размер повторения текстуры", Float) = 256
        _TextureContrast ("Контраст текстуры", Range(0.1,8)) = 2

        [Header(Edges)]
        _EdgeRoughness ("Глубина рваности - единицы UI", Range(0,3)) = 0.18
        _EdgeChipSize ("Размер неровностей", Range(0.25,12)) = 1.8
        _CrackAmount ("Количество трещин", Range(0,1)) = 0.1
        _CrackWidth ("Ширина трещин", Range(0,2)) = 0.22
        _CrackDepth ("Глубина трещин", Range(0,6)) = 0.6
        _CrackSpacing ("Расстояние между участками трещин", Float) = 14
        _PatternSeed ("Вариант рисунка", Float) = 0

        // Keep the standard property names: TMP uses them for padding,
        // bold text and font-material matching, including fallback fonts.
        [HideInInspector] _WeightNormal ("Weight Normal", Float) = 0
        [HideInInspector] _WeightBold ("Weight Bold", Float) = 0.5
        [HideInInspector] _GradientScale ("Gradient Scale", Float) = 5
        [HideInInspector] _TextureWidth ("Texture Width", Float) = 512
        [HideInInspector] _TextureHeight ("Texture Height", Float) = 512
        [HideInInspector] _ScaleRatioA ("Scale Ratio A", Float) = 1
        [HideInInspector] _ScaleRatioB ("Scale Ratio B", Float) = 1
        [HideInInspector] _ScaleRatioC ("Scale Ratio C", Float) = 1
        [HideInInspector] _ShaderFlags ("Shader Flags", Float) = 0
        [HideInInspector] _ScaleX ("Scale X", Float) = 1
        [HideInInspector] _ScaleY ("Scale Y", Float) = 1
        [HideInInspector] _VertexOffsetX ("Vertex Offset X", Float) = 0
        [HideInInspector] _VertexOffsetY ("Vertex Offset Y", Float) = 0
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        [HideInInspector] _MaskSoftnessX ("Mask Softness X", Float) = 0
        [HideInInspector] _MaskSoftnessY ("Mask Softness Y", Float) = 0
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _CullMode ("Cull Mode", Float) = 0
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "DustbornText"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"

            // BEGIN_DUSTBORN_TEXT_CORE
            sampler2D _MainTex, _WearTex;
            float4 _FaceColor, _OutlineColor;
            float _FaceDilate, _OutlineWidth, _OutlineSoftness;
            float _WeightNormal, _WeightBold, _ScaleRatioA;
            float _SurfaceStrength, _GrainSize;
            float _TextureInfluence, _TextureTileSize, _TextureContrast;
            float _EdgeRoughness, _EdgeChipSize;
            float _CrackAmount, _CrackWidth, _CrackDepth, _CrackSpacing;
            float _PatternSeed;
            float _UseWear, _UseGlow;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowPulseSpeed, _GlowPulseAmount;

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

            float4 DW_Glow(float4 shape)
            {
                float pulse = 0.5 + 0.5 * sin(_Time.y * _GlowPulseSpeed * 6.2831853);
                float strength = max(_GlowIntensity, 0.0) * (1.0 - saturate(_GlowPulseAmount) * pulse);
                shape.rgb *= 1.0 + _GlowColor.rgb * strength;
                return shape;
            }

            // Input is an SDF, not ordinary sprite opacity. Derivatives recover
            // the SDF gradient in local UI units. This avoids extra mesh data
            // and works with TMP's vertex streams without overwriting them.
            // Returns PREMULTIPLIED colour, including face, outline and tint alpha.
            float4 DW_Text(float2 uv, float2 localPos, float bold, float4 tint)
            {
                float field = tex2D(_MainTex, uv).a;
                float2 dx = ddx(localPos);
                float2 dy = ddy(localPos);
                float fx = ddx(field);
                float fy = ddy(field);
                float det = dx.x * dy.y - dx.y * dy.x;
                float safeDet = abs(det) > 1e-10 ? det : (det < 0.0 ? -1e-10 : 1e-10);
                float2 localGradient = float2(fx * dy.y - fy * dx.y,
                                             fy * dx.x - fx * dy.x) / safeDet;
                float gradient = max(length(localGradient), 0.00001);

                float ratio = max(_ScaleRatioA, 0.0);
                float weight = (lerp(_WeightNormal, _WeightBold, saturate(bold)) * 0.25
                              + _FaceDilate) * ratio * 0.5;
                float distance = field - (0.5 - weight);
                float outline = max(_OutlineWidth, 0.0) * ratio * 0.5;
                float aa = max((abs(fx) + abs(fy)) * 0.5
                             + max(_OutlineSoftness, 0.0) * ratio * 0.5, 0.0001);

                bool wear = _UseWear > 0.5;
                float seed = frac(_PatternSeed * 0.01371) * 997.0;
                float2 p = localPos + float2(seed * 1.37, seed * 0.73);
                float rough = wear ? max(_EdgeRoughness, 0.0) * pow(DW_EdgeNoise(p), 1.4) : 0.0;
                float wornDistance = distance - rough * gradient;
                float face = smoothstep(-aa, aa, wornDistance - outline);
                float outer = smoothstep(-aa, aa, wornDistance + outline);
                float ring = max(outer - face, 0.0);

                float crackMask = 1.0;
                if (wear && _CrackAmount > 0.0 && _CrackDepth > 0.0)
                {
                    float crack = max(DW_CrackStripe(p.x, p.y, seed),
                                      DW_CrackStripe(p.y, p.x, seed + 113.0));
                    float fromEdge = max((distance + outline) / gradient, 0.0);
                    float edgeBand = 1.0 - smoothstep(0.0, max(_CrackDepth, 0.001), fromEdge);
                    crackMask = 1.0 - saturate(crack * edgeBand * 1.5);
                }

                float gain = 1.0;
                if (wear)
                {
                    float grain = 0.65 * DW_Noise(p / max(_GrainSize, 0.25))
                                + 0.35 * DW_Noise(p / max(_GrainSize * 3.8, 0.25) + 31.2);
                    if (_TextureInfluence > 0.0)
                    {
                        float3 texGrain = tex2D(_WearTex, p / max(_TextureTileSize, 1.0)).rgb;
                        float gray = dot(texGrain, float3(0.2126, 0.7152, 0.0722));
                        gray = saturate((gray - 0.5) * _TextureContrast + 0.5);
                        grain = lerp(grain, gray, saturate(_TextureInfluence));
                    }
                    gain = 1.0 - saturate(_SurfaceStrength) * (1.0 - grain);
                }
                float faceAlpha = face * saturate(_FaceColor.a);
                float ringAlpha = ring * saturate(_OutlineColor.a);
                float opacity = saturate(tint.a) * crackMask;
                float3 rgb = (faceAlpha * _FaceColor.rgb + ringAlpha * _OutlineColor.rgb)
                           * tint.rgb * opacity * gain;
                float4 shape = float4(rgb, saturate((faceAlpha + ringAlpha) * opacity));
                if (_UseGlow < 0.5)
                    return shape;
                return DW_Glow(shape);
            }
            // END_DUSTBORN_TEXT_CORE

            float4 _ClipRect;
            float _MaskSoftnessX, _MaskSoftnessY, _UIMaskSoftnessX, _UIMaskSoftnessY;
            float _VertexOffsetX, _VertexOffsetY;
            int _UIVertexColorAlwaysGammaSpace;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localPos : TEXCOORD1;
                float4 mask : TEXCOORD2;
                float bold : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xy;
                o.uv = v.uv.xy;
                o.color = v.color;
                // Unity 6 TMP stores signed SDF scale in UV0.w. Older TMP uses UV1.y.
                float signedScale = abs(v.uv.w) > 0.000001 ? v.uv.w : v.uv1.y;
                o.bold = signedScale < 0.0 ? 1.0 : 0.0;
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 pixel = o.vertex.w / max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), 0.00001);
                float2 softness = max(float2(_MaskSoftnessX, _MaskSoftnessY),
                                      float2(_UIMaskSoftnessX, _UIMaskSoftnessY));
                o.mask.xy = v.vertex.xy * 2.0 - rect.xy - rect.zw;
                o.mask.zw = 0.25 / max(0.25 * softness + abs(pixel), 0.00001);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 tint = i.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    tint.rgb = GammaToLinearSpace(tint.rgb);
                #endif
                float4 result = DW_Text(i.uv, i.localPos, i.bold, tint);
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                result *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(max(result.a, max(result.r, max(result.g, result.b))) - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
