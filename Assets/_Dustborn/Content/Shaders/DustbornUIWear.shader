Shader "Dustborn/UI/Worn Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Additional Tint", Color) = (1,1,1,1)
        [Enum(Sprite,0,Horizontal,1,Vertical,2)] _Shape ("Shape", Float) = 0
        _SurfaceStrength ("Surface Wear", Range(0,1)) = 0.28
        _GrainSize ("Grain Size - UI Units", Range(0.25,8)) = 1.2
        _WearTex ("Grayscale Wear Texture (Optional)", 2D) = "gray" {}
        _TextureInfluence ("Texture Influence", Range(0,1)) = 0
        _TextureTileSize ("Texture Tile Size - UI Units", Float) = 384
        _TextureContrast ("Texture Contrast", Range(0.1,8)) = 2
        _EdgeRoughness ("Edge Loss - UI Units", Range(0,4)) = 0.45
        _EdgeChipSize ("Edge Chip Size - UI Units", Range(0.25,16)) = 2.6
        _CrackAmount ("Crack Probability", Range(0,1)) = 0.2
        _CrackWidth ("Crack Width - UI Units", Range(0,3)) = 0.4
        _CrackDepth ("Crack Depth - UI Units", Range(0,8)) = 1
        _CrackSpacing ("Crack Cell Size - UI Units", Float) = 18
        _TaperStart ("Start Taper - UI Units", Float) = 24
        _TaperEnd ("End Taper - UI Units", Float) = 32
        _TipPower ("Tip Profile", Range(0.25,3)) = 1
        _LineFeather ("Line Antialiasing - UI Units", Range(0,1.5)) = 0.25
        _PatternSeed ("Material Pattern Seed", Float) = 0

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"
               "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "WornUI"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "DustbornUIWear.cginc"

            float4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 local : TEXCOORD1;
                float4 atlas : TEXCOORD2;
                float4 variation : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 local : TEXCOORD1;
                float4 atlas : TEXCOORD2;
                float4 mask : TEXCOORD3;
                float seed : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.local = v.local;
                o.atlas = v.atlas;
                o.seed = v.variation.x;
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                float2 pixel = o.vertex.w / max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), 0.00001);
                o.mask.xy = v.vertex.xy * 2 - rect.xy - rect.zw;
                o.mask.zw = 0.25 / max(0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixel), 0.00001);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 tint = i.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace != 0)
                    tint.rgb = GammaToLinearSpace(tint.rgb);
                #endif
                float4 result = DW_Shade(i.uv, i.local.xy, i.local.zw, i.atlas, i.seed, tint * _Color);
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                result.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }
    CustomEditor "Dustborn.UI.Editor.DustbornUIWearShaderGUI"
    Fallback Off
}
