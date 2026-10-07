using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GrassCardFactory : IDisposable
{
    private const string SHADER = "BiomeGrass/2D Recolor Wind URP";
    private const string PLAIN = "Universal Render Pipeline/Simple Lit";
    private const string FALLBACK = "Universal Render Pipeline/Lit";

    private const float CUTOFF = 0.35f;
    private const float FADE_FRACTION = 0.9f;

    private const float SNOW_START = 0.55f;
    private const float AMBIENT_STRENGTH = 1f;
    private const float WIND_SCALE = 0.9f;
    private const float WIND_ROOT = 0.13f;
    private const float WIND_EXPONENT = 2f;

    private static readonly Color SnowColor = new(0.933f, 0.957f, 0.969f);
    private static readonly Vector4 WindDirection = new(1f, 0f, 0.3f, 0f);

    private readonly Dictionary<GrassCardStyle, Material> _materials = new();

    private Mesh _mesh;

    public float Distance { get; set; }

    public Mesh Mesh => _mesh == null ? _mesh = BuildMesh() : _mesh;

    public Material Material(Texture2D card, Color tint)
    {
        return Material(new GrassCardStyle(card, tint));
    }

    public Material Material(GrassCardStyle style)
    {
        if (_materials.TryGetValue(style, out Material cached))
            return cached;

        Material material = BuildMaterial(style, Distance);

        _materials[style] = material;

        return material;
    }

    public void Dispose()
    {
        foreach (Material material in _materials.Values)
            Release(material);

        _materials.Clear();

        Release(_mesh);

        _mesh = null;
    }

    private static Mesh BuildMesh()
    {
        var mesh = new Mesh { name = "Grass Card", hideFlags = HideFlags.DontSave };

        var vertices = new Vector3[8];
        var normals = new Vector3[8];
        var tangents = new Vector4[8];
        var uv = new Vector2[8];
        var triangles = new int[12];

        for (int quad = 0; quad < 2; quad++)
        {
            Vector3 side = quad == 0 ? new Vector3(0.5f, 0f, 0f) : new Vector3(0f, 0f, 0.5f);
            Vector3 face = quad == 0 ? Vector3.back : Vector3.right;
            Vector3 tangent = quad == 0 ? Vector3.right : Vector3.forward;
            int first = quad * 4;

            vertices[first + 0] = -side;
            vertices[first + 1] = side;
            vertices[first + 2] = side + Vector3.up;
            vertices[first + 3] = -side + Vector3.up;

            uv[first + 0] = new Vector2(0f, 0f);
            uv[first + 1] = new Vector2(1f, 0f);
            uv[first + 2] = new Vector2(1f, 1f);
            uv[first + 3] = new Vector2(0f, 1f);

            for (int corner = 0; corner < 4; corner++)
            {
                normals[first + corner] = face;
                tangents[first + corner] = new Vector4(tangent.x, tangent.y, tangent.z, -1f);
            }

            triangles[quad * 6 + 0] = first + 0;
            triangles[quad * 6 + 1] = first + 2;
            triangles[quad * 6 + 2] = first + 1;
            triangles[quad * 6 + 3] = first + 0;
            triangles[quad * 6 + 4] = first + 3;
            triangles[quad * 6 + 5] = first + 2;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.uv = uv;
        mesh.triangles = triangles;

        mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 1.2f, 1.5f));

        return mesh;
    }

    private static Shader Pick()
    {
        Shader card = Shader.Find(SHADER);

        if (card != null && card.isSupported)
            return card;

        if (card != null)
            Debug.LogError($"{SHADER} did not compile, grass falls back to {PLAIN}: no recoloring, wind or distance fade");

        return Shader.Find(PLAIN) ?? Shader.Find(FALLBACK);
    }

    private static Material BuildMaterial(GrassCardStyle style, float distance)
    {
        Shader shader = Pick();

        if (shader == null)
        {
            Debug.LogError($"No URP shader for grass cards: neither {SHADER} nor {PLAIN} was found");

            return null;
        }

        var material = new Material(shader)
        {
            name = $"Grass Card {style.Card.name}",
            hideFlags = HideFlags.DontSave,
            enableInstancing = true,
            renderQueue = (int)RenderQueue.AlphaTest,
            doubleSidedGI = true
        };

        SetTexture(material, "_BaseMap", style.Card);
        SetTexture(material, "_ColorMask", style.Mask);
        SetColor(material, "_BaseColor", style.Leaves);
        SetColor(material, "_LeafColor", style.Leaves);
        SetColor(material, "_StemColor", style.Stems);
        SetColor(material, "_FlowerColor", style.Flowers);
        SetColor(material, "_TipTint", style.TipTint);
        SetColor(material, "_SnowColor", SnowColor);
        SetFloat(material, "_Cutoff", CUTOFF);
        SetFloat(material, "_AlphaClip", 1f);
        SetFloat(material, "_Cull", (float)CullMode.Off);
        SetFloat(material, "_SpecularHighlights", 0f);
        SetFloat(material, "_Smoothness", 0f);
        SetFloat(material, "_SnowAmount", Mathf.Clamp01(style.SnowAmount));
        SetFloat(material, "_SnowStart", SNOW_START);
        SetFloat(material, "_RootDarkening", Mathf.Clamp(style.RootDarkening, 0f, 0.8f));
        SetFloat(material, "_TextureShading", Mathf.Clamp01(style.TextureShading));
        SetFloat(material, "_Translucency", Mathf.Clamp01(style.Translucency));
        SetFloat(material, "_WrapLight", Mathf.Clamp01(style.WrapLight));
        SetFloat(material, "_AmbientStrength", AMBIENT_STRENGTH);
        SetFloat(material, "_EnableFacetRelief", 0f);
        SetFloat(material, "_AlphaToCoverage", 0f);
        SetVector(material, "_WindDirection", WindDirection);
        SetFloat(material, "_WindStrength", Mathf.Max(0f, style.WindStrength));
        SetFloat(material, "_WindSpeed", Mathf.Clamp(style.WindSpeed, 0f, 6f));
        SetFloat(material, "_WindScale", WIND_SCALE);
        SetFloat(material, "_WindRoot", WIND_ROOT);
        SetFloat(material, "_WindExponent", WIND_EXPONENT);
        SetFloat(material, "_Flutter", Mathf.Clamp(style.Flutter, 0f, 0.02f));
        SetFloat(material, "_Roundness", Mathf.Clamp(style.Roundness, 0f, 2f));
        SetFloat(material, "_NormalUp", Mathf.Clamp01(style.NormalUp));
        SetFloat(material, "_Variation", Mathf.Clamp(style.Variation, 0f, 0.35f));

        material.EnableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetOverrideTag("RenderType", "TransparentCutout");

        if (distance > 0f && material.HasProperty("_FadeEnd"))
        {
            material.SetFloat("_FadeStart", distance * FADE_FRACTION);
            material.SetFloat("_FadeEnd", distance);
        }

        return material;
    }

    private static void SetTexture(Material material, string property, Texture texture)
    {
        if (texture != null && material.HasProperty(property))
            material.SetTexture(property, texture);
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
            material.SetColor(property, color);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static void SetVector(Material material, string property, Vector4 value)
    {
        if (material.HasProperty(property))
            material.SetVector(property, value);
    }

    private static void Release(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(target);
        else
            UnityEngine.Object.DestroyImmediate(target);
    }
}
