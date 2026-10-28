using System;
using System.Collections.Generic;
using UnityEngine;

public partial class MeshCombiner
{
    private static readonly string[] TintProperties = { "_BaseColor", "_Color_Tint", "_Color" };
    private static readonly HashSet<Material> Untinted = new();

    private readonly Dictionary<(Material, int), Material> _variants = new();
    private readonly Material[][][] _painted = new Material[DecorTint.LEVELS][][];

    public void Dispose()
    {
        foreach (KeyValuePair<(Material Source, int Level), Material> pair in _variants)
        {
            if (pair.Value != pair.Key.Source)
                Release(pair.Value);
        }

        _variants.Clear();

        Array.Clear(_painted, 0, _painted.Length);
    }

    private Material[] Variants(Material[] sources, int level)
    {
        var variants = new Material[sources.Length];

        for (int i = 0; i < sources.Length; i++)
            variants[i] = Variant(sources[i], level);

        return variants;
    }

    private Material Variant(Material source, int level)
    {
        if (source == null || _card || _tint.IsNeutral)
            return source;

        if (_variants.TryGetValue((source, level), out Material cached) && cached != null)
            return cached;

        string property = TintProperty(source);
        Material variant = property == null ? source : Tinted(source, property, _tint.Stepped(level), level);

        _variants[(source, level)] = variant;

        return variant;
    }

    private static Material Tinted(Material source, string property, Color tint, int level)
    {
        Color current = source.GetColor(property);

        var variant = new Material(source)
        {
            name = $"{source.name} (tint {level})",
            hideFlags = HideFlags.DontSave,
            enableInstancing = source.enableInstancing
        };

        variant.SetColor(property, new Color(current.r * tint.r, current.g * tint.g, current.b * tint.b, current.a));

        return variant;
    }

    private static string TintProperty(Material material)
    {
        foreach (string property in TintProperties)
        {
            if (material.HasProperty(property))
                return property;
        }

        if (Untinted.Add(material))
            Debug.LogWarning($"Decor material {material.name} ({material.shader.name}) has no colour property, its decor layer tint is not applied");

        return null;
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
