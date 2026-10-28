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

    internal const float SWAY_REACH = 0.5f;

    private const float SNOW_START = 0.55f;
    private const float AMBIENT_STRENGTH = 1f;
    private const float WIND_SCALE = 0.9f;
    private const float WIND_ROOT = 0.13f;
    private const float WIND_EXPONENT = 2f;

    private static readonly Color SnowColor = new(0.933f, 0.957f, 0.969f);
    private static readonly Vector4 WindDirection = new(1f, 0f, 0.3f, 0f);

    private static readonly List<Vector3> BatchVertices = new();
    private static readonly List<Vector3> BatchNormals = new();
    private static readonly List<Vector4> BatchTangents = new();
    private static readonly List<Vector2> BatchCorners = new();
    private static readonly List<Vector3> BatchRoots = new();
    private static readonly List<Color> BatchTints = new();
    private static readonly List<int> BatchIndices = new();

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

    public static Mesh Batch(IReadOnlyList<DecorInstance> instances, int start, int count, Vector3 origin, DecorTint tint, string name)
    {
        ClearBatch();

        for (int i = 0; i < count; i++)
        {
            DecorInstance instance = instances[start + i];

            AddTuft(instance.Position - origin, instance.Rotation, new Vector2(instance.Scale, instance.Height), instance.Slope,
                tint.Exact(instance.Shade));
        }

        Mesh mesh = Upload(name);

        ClearBatch();

        return mesh;
    }

    private static Mesh BuildMesh()
    {
        ClearBatch();
        AddTuft(Vector3.zero, 0f, Vector2.one, Vector2.zero, Color.white);

        Mesh mesh = Upload("Grass Tuft");

        mesh.hideFlags = HideFlags.DontSave;

        ClearBatch();

        return mesh;
    }

    private static Mesh Upload(string name)
    {
        var mesh = new Mesh
        {
            name = name,
            indexFormat = BatchVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };

        mesh.SetVertices(BatchVertices);
        mesh.SetNormals(BatchNormals);
        mesh.SetTangents(BatchTangents);
        mesh.SetUVs(0, BatchCorners);
        mesh.SetUVs(1, BatchRoots);
        mesh.SetColors(BatchTints);
        mesh.SetIndices(BatchIndices, MeshTopology.Triangles, 0);

        Bounds bounds = mesh.bounds;

        bounds.Expand(2f * SWAY_REACH);
        mesh.bounds = bounds;

        return mesh;
    }

    private static void AddTuft(Vector3 root, float yaw, Vector2 size, Vector2 slope, Color tint)
    {
        for (int plane = 0; plane < GrassTuft.PLANES; plane++)
        {
            int first = BatchVertices.Count;
            Vector3 normal = GrassTuft.Normal(yaw, plane);
            Vector3 tangent = GrassTuft.Tangent(yaw, plane);

            foreach (Vector2 corner in GrassTuft.Corners)
            {
                Vector3 offset = GrassTuft.Offset(yaw, plane, corner, size, slope);

                BatchVertices.Add(root + offset);
                BatchNormals.Add(normal);
                BatchTangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, -1f));
                BatchCorners.Add(corner);
                BatchRoots.Add(-offset);
                BatchTints.Add(tint);
            }

            foreach (int index in GrassTuft.Quad)
                BatchIndices.Add(first + index);
        }
    }

    private static void ClearBatch()
    {
        BatchVertices.Clear();
        BatchNormals.Clear();
        BatchTangents.Clear();
        BatchCorners.Clear();
        BatchRoots.Clear();
        BatchTints.Clear();
        BatchIndices.Clear();
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
        SetFloat(material, "_EdgeHide", GrassTuft.EDGE_HIDE);
        SetFloat(material, "_EdgeShow", GrassTuft.EDGE_SHOW);

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
