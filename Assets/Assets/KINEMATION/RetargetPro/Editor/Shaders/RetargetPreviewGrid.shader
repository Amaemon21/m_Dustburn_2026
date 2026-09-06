Shader "KINEMATION/Retarget Pro/Preview Grid"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5, 0.47, 0.5, 1)
        _BaseColorAlt ("Alt Base Color", Color) = (0.55, 0.52, 0.55, 1)
        _FogColor ("Fog Color", Color) = (0.75, 0.72, 0.74, 1)
        _MinorLineColor ("Minor Line Color", Color) = (0.69, 0.66, 0.69, 1)
        _MajorLineColor ("Major Line Color", Color) = (0.94, 0.92, 0.94, 1)
        _ChunkSize ("Chunk Size (Meters)", Float) = 0.5
        [RetargetPreviewGridIntMin(1)] _SubChunkCount ("Sub-Chunks Per Chunk", Int) = 5
        _MinorLineWidth ("Minor Line Width", Float) = 0.12
        _MajorLineWidth ("Major Line Width", Float) = 0.28
        _FadeStart ("Fade Start", Float) = 20
        _FadeEnd ("Fade End", Float) = 60
    }

    CGINCLUDE
    #pragma target 3.0
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
    };

    struct v2f
    {
        float4 vertex : SV_POSITION;
        float3 worldPos : TEXCOORD0;
    };

    fixed4 _BaseColor;
    fixed4 _BaseColorAlt;
    fixed4 _FogColor;
    fixed4 _MinorLineColor;
    fixed4 _MajorLineColor;
    float _ChunkSize;
    float _SubChunkCount;
    float _MinorLineWidth;
    float _MajorLineWidth;
    float _FadeStart;
    float _FadeEnd;

    v2f vert(appdata v)
    {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        return o;
    }

    float GridMask(float2 worldXZ, float stepSize, float lineWidth)
    {
        float safeStep = max(stepSize, 1e-4);
        float2 gridCoord = worldXZ / safeStep;
        float2 derivative = max(fwidth(gridCoord), float2(1e-4, 1e-4));
        float2 cell = abs(frac(gridCoord - 0.5) - 0.5) / derivative;
        float lineDistance = min(cell.x, cell.y);
        return 1.0 - saturate(lineDistance - lineWidth);
    }

    fixed4 frag(v2f i) : SV_Target
    {
        float2 worldXZ = i.worldPos.xz;
        float chunkSize = max(_ChunkSize, 1e-4);
        float subChunkCount = max(_SubChunkCount, 1.0);
        float minorStep = chunkSize / subChunkCount;
        float2 majorCell = floor(worldXZ / chunkSize);
        float checker = frac((majorCell.x + majorCell.y) * 0.5) * 2.0;

        float minorMask = GridMask(worldXZ, minorStep, _MinorLineWidth) * 0.32;
        float majorMask = GridMask(worldXZ, chunkSize, _MajorLineWidth) * 0.78;

        float3 baseColor = lerp(_BaseColor.rgb, _BaseColorAlt.rgb, saturate(checker));
        float3 nearColor = lerp(baseColor, _MinorLineColor.rgb, saturate(minorMask));
        nearColor = lerp(nearColor, _MajorLineColor.rgb, saturate(majorMask));

        float cameraDistance = distance(_WorldSpaceCameraPos.xz, worldXZ);
        float fadeRange = max(_FadeEnd - _FadeStart, 1e-4);
        float fade = 1.0 - saturate((cameraDistance - _FadeStart) / fadeRange);
        fade *= fade;

        float3 finalColor = lerp(_FogColor.rgb, nearColor, fade);
        return float4(finalColor, 1.0);
    }
    ENDCG

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalRenderPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "HDRenderPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardOnly"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "HighDefinitionRenderPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardOnly"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback Off
}
