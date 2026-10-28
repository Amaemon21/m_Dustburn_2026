using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class MeshCombiner
{
    public static MeshCombiner Far(GameObject prefab, DecorTint tint)
    {
        var combiner = new MeshCombiner(tint);

        if (prefab == null)
            return combiner;

        Renderer[] renderers = Coarsest(prefab);
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;

        foreach (Renderer renderer in renderers)
        {
            if (renderer is not MeshRenderer || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                continue;

            Matrix4x4 local = toRoot * filter.transform.localToWorldMatrix;
            Material[] materials = renderer.sharedMaterials;

            for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < materials.Length; sub++)
            {
                if (materials[sub] != null)
                    combiner._parts.Add(new Part(filter.sharedMesh, sub, WithInstancing(materials[sub]), local));
            }
        }

        combiner.Profile = new PrefabProfile { HasMesh = combiner._parts.Count > 0, Instanceable = true };
        return combiner;
    }

    public int InstanceParts(IReadOnlyList<DecorInstance> instances, Transform parent, string name, ShadowCastingMode shadows, float drawDistance)
    {
        if (_parts.Count == 0 || instances.Count == 0)
            return 0;

        Vector3 origin = instances[0].Position;
        var placements = new Matrix4x4[instances.Count];
        var levels = new int[instances.Count];
        var counts = new int[DecorTint.LEVELS];

        for (int i = 0; i < instances.Count; i++)
        {
            DecorInstance instance = instances[i];
            placements[i] = Placement(instance);
            levels[i] = _tint.Level(instance.Shade);
            counts[levels[i]]++;
        }

        int built = 0;

        foreach (Part part in _parts)
        {
            for (int level = 0; level < DecorTint.LEVELS; level++)
            {
                if (counts[level] == 0)
                    continue;

                var matrices = new Matrix4x4[counts[level]];
                int next = 0;

                for (int i = 0; i < placements.Length; i++)
                {
                    if (levels[i] == level)
                        matrices[next++] = placements[i] * part.Local;
                }

                var holder = new GameObject(name);
                holder.transform.SetParent(parent, false);
                holder.transform.localPosition = origin;
                holder.AddComponent<VoxelDecorRenderer>().Setup(part.Mesh, Variant(part.Material, level), matrices, shadows, drawDistance, part.SubMesh);

                built++;
            }
        }

        return built;
    }

    private static Renderer[] Coarsest(GameObject prefab)
    {
        LODGroup group = prefab.GetComponentInChildren<LODGroup>(true);

        if (group == null)
            return prefab.GetComponentsInChildren<Renderer>(true);

        LOD[] lods = group.GetLODs();

        for (int i = lods.Length - 1; i >= 0; i--)
        {
            if (lods[i].renderers != null && lods[i].renderers.Length > 0)
                return lods[i].renderers;
        }

        return prefab.GetComponentsInChildren<Renderer>(true);
    }

    private static Material WithInstancing(Material material)
    {
        if (material.enableInstancing)
            return material;

        if (!Instancing.TryGetValue(material, out Material copy) || copy == null)
            Instancing[material] = copy = new Material(material) { name = material.name + " (instanced)", enableInstancing = true, hideFlags = HideFlags.DontSave };

        return copy;
    }

    public int Instance(IReadOnlyList<DecorInstance> instances, Transform parent, string name, ShadowCastingMode shadows, float drawDistance)
    {
        if (_card)
            return Holder(instances, parent, name, shadows, drawDistance, _materials[0], Tints(instances));

        if (_tint.IsNeutral)
            return Holder(instances, parent, name, shadows, drawDistance, _materials[0], null);

        var shaded = new List<DecorInstance>(instances.Count);
        int built = 0;

        for (int level = 0; level < DecorTint.LEVELS; level++)
        {
            shaded.Clear();

            foreach (DecorInstance instance in instances)
            {
                if (_tint.Level(instance.Shade) == level)
                    shaded.Add(instance);
            }

            if (shaded.Count > 0)
                built += Holder(shaded, parent, name, shadows, drawDistance, Variant(_materials[0], level), null);
        }

        return built;
    }

    private int Holder(IReadOnlyList<DecorInstance> instances, Transform parent, string name, ShadowCastingMode shadows, float drawDistance,
        Material material, Vector4[] tints)
    {
        var matrices = new Matrix4x4[instances.Count];

        for (int i = 0; i < instances.Count; i++)
            matrices[i] = Placement(instances[i]) * _parts[0].Local;

        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = instances[0].Position;

        holder.AddComponent<VoxelDecorRenderer>().Setup(_parts[0].Mesh, material, matrices, shadows, drawDistance, 0, tints);

        return 1;
    }

    private Vector4[] Tints(IReadOnlyList<DecorInstance> instances)
    {
        var tints = new Vector4[instances.Count];

        for (int i = 0; i < tints.Length; i++)
            tints[i] = _tint.Exact(instances[i].Shade);

        return tints;
    }
}
