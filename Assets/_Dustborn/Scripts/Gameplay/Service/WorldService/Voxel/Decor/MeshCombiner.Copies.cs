using System;
using System.Collections.Generic;
using UnityEngine;

public partial class MeshCombiner
{
    public const float WIND_MARGIN = 1.5f;

    private static readonly List<Renderer> Renderers = new();
    private static readonly HashSet<string> Misfits = new();

    private Material[][] _fitted;

    public void Prepare(GameObject copy, float shade, bool widen = true)
    {
        bool tinted = !_card && !_tint.IsNeutral;
        int level = tinted ? _tint.Level(shade) : 0;

        copy.GetComponentsInChildren(true, Renderers);

        _fitted ??= Fit(Renderers, copy.name);

        Material[][] painted = tinted ? _painted[level] ??= new Material[Renderers.Count][] : null;

        for (int i = 0; i < Renderers.Count; i++)
        {
            Renderer renderer = Renderers[i];
            Material[] fitted = i < _fitted.Length ? _fitted[i] : null;

            if (widen && renderer is MeshRenderer)
                Widen(renderer);

            if (painted == null || i >= painted.Length)
            {
                if (fitted != null)
                    renderer.sharedMaterials = fitted;

                continue;
            }

            painted[i] ??= Variants(fitted ?? renderer.sharedMaterials, level);
            renderer.sharedMaterials = painted[i];
        }

        Renderers.Clear();
    }

    public static void FitMaterials(GameObject instance, string owner)
    {
        instance.GetComponentsInChildren(true, Renderers);

        Material[][] fitted = Fit(Renderers, owner);

        for (int i = 0; i < fitted.Length; i++)
        {
            if (fitted[i] != null)
                Renderers[i].sharedMaterials = fitted[i];
        }

        Renderers.Clear();
    }

    private static Material[][] Fit(List<Renderer> renderers, string owner)
    {
        var fitted = new Material[renderers.Count][];

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            Mesh mesh = MeshOf(renderer);
            Material[] materials = renderer.sharedMaterials;
            fitted[i] = materials;

            if (mesh == null || materials.Length <= mesh.subMeshCount)
                continue;

            fitted[i] = new Material[mesh.subMeshCount];
            Array.Copy(materials, fitted[i], mesh.subMeshCount);

            string key = $"{owner}/{renderer.name}";

            if (Misfits.Add(key))
                Debug.LogWarning($"{key}: {materials.Length} materials on mesh '{mesh.name}' with {mesh.subMeshCount} submeshes. "
                    + "The extra slots are dropped on spawn, as the GPU Resident Drawer skips them anyway; remove them from the prefab", renderer);
        }

        return fitted;
    }

    private static Mesh MeshOf(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;

        return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
    }

    private static void Widen(Renderer renderer)
    {
        Bounds bounds = renderer.localBounds;

        bounds.Expand(2f * WIND_MARGIN);

        renderer.localBounds = bounds;
    }
}
