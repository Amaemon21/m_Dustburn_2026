using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GrassCardFactory : IDisposable
{
    private const string SHADER = "Dustborn/GrassCard";
    private const string PLAIN = "Universal Render Pipeline/Simple Lit";
    private const string FALLBACK = "Universal Render Pipeline/Lit";

    private const float CUTOFF = 0.4f;
    private const float FADE_FRACTION = 0.9f;

    private readonly Dictionary<(Texture2D, Color), Material> _materials = new();

    private Mesh _mesh;

    // Metres at which a card is fully dissolved. Zero leaves the cards visible to wherever the ring
    // ends, which is a hard edge, so the streamer hands its grass distance down.
    public float Distance { get; set; }

    public Mesh Mesh => _mesh == null ? _mesh = BuildMesh() : _mesh;

    public Material Material(Texture2D card, Color tint)
    {
        (Texture2D, Color) key = (card, tint);

        if (_materials.TryGetValue(key, out Material cached))
            return cached;

        Material material = BuildMaterial(card, tint, Distance);

        _materials[key] = material;

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
            Vector3 face = quad == 0 ? Vector3.forward : Vector3.right;
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
                normals[first + corner] = Vector3.up;
                tangents[first + corner] = new Vector4(face.x, face.y, face.z, 1f);
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

        mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);

        return mesh;
    }

    // A shader that fails to compile is still found by name and would paint the world magenta, so the
    // card shader is taken only when the platform reports it as supported.
    private static Shader Pick()
    {
        Shader card = Shader.Find(SHADER);

        if (card != null && card.isSupported)
            return card;

        if (card != null)
            Debug.LogError($"{SHADER} did not compile, grass falls back to {PLAIN}: no wind and no distance fade");

        return Shader.Find(PLAIN) ?? Shader.Find(FALLBACK);
    }

    private static Material BuildMaterial(Texture2D card, Color tint, float distance)
    {
        Shader shader = Pick();

        if (shader == null)
        {
            Debug.LogError($"No URP shader for grass cards: neither {SHADER} nor {PLAIN} was found");

            return null;
        }

        var material = new Material(shader)
        {
            name = $"Grass Card {card.name}",
            hideFlags = HideFlags.DontSave,
            enableInstancing = true,
            renderQueue = (int)RenderQueue.AlphaTest,
            doubleSidedGI = true
        };

        material.SetTexture("_BaseMap", card);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Cutoff", CUTOFF);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_Smoothness", 0f);

        material.EnableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetOverrideTag("RenderType", "TransparentCutout");

        if (distance > 0f && material.HasFloat("_FadeEnd"))
        {
            material.SetFloat("_FadeStart", distance * FADE_FRACTION);
            material.SetFloat("_FadeEnd", distance);
        }

        return material;
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
