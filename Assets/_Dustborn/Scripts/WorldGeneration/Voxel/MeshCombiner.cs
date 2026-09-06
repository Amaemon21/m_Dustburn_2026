using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class MeshCombiner
{
    private readonly struct Part
    {
        public Mesh Mesh { get; }
        public int SubMesh { get; }
        public Material Material { get; }
        public Matrix4x4 Local { get; }

        public Part(Mesh mesh, int subMesh, Material material, Matrix4x4 local)
        {
            Mesh = mesh;
            SubMesh = subMesh;
            Material = material;
            Local = local;
        }
    }

    private readonly List<Part> _parts = new();
    private readonly List<Material> _materials = new();

    public PrefabProfile Profile { get; private set; }

    public IReadOnlyList<Material> Materials => _materials;

    public MeshCombiner(GameObject prefab)
    {
        Profile = Probe(prefab);
    }

    public MeshCombiner(Mesh mesh, Material material)
    {
        if (mesh == null || material == null)
        {
            Profile = PrefabProfile.Empty;

            return;
        }

        _materials.Add(material);
        _parts.Add(new Part(mesh, 0, material, Matrix4x4.identity));

        Profile = new PrefabProfile
        {
            HasMesh = true,
            Readable = mesh.isReadable,
            Instanceable = material.enableInstancing,
            Vertices = mesh.vertexCount
        };
    }

    public int Instance(IReadOnlyList<DecorInstance> instances, Transform parent, string name, ShadowCastingMode shadows, float drawDistance)
    {
        var matrices = new Matrix4x4[instances.Count];

        Vector3 origin = instances[0].Position;
        var bounds = new Bounds(origin, Vector3.zero);

        for (int i = 0; i < instances.Count; i++)
        {
            DecorInstance instance = instances[i];

            matrices[i] = Matrix4x4.TRS(instance.Position, Quaternion.Euler(0f, instance.Rotation, 0f),
                new Vector3(instance.Scale, instance.Height, instance.Scale)) * _parts[0].Local;

            bounds.Encapsulate(instance.Position);
        }

        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = origin;

        bounds.Expand(4f);

        holder.AddComponent<VoxelDecorRenderer>().Setup(_parts[0].Mesh, _materials[0], matrices, bounds, shadows, drawDistance);

        return 1;
    }

    public int Combine(IReadOnlyList<DecorInstance> instances, int vertexBudget, Transform parent, string name, ShadowCastingMode shadows)
    {
        int batch = MeshCombinePlan.BatchSize(Profile, vertexBudget);
        int built = 0;

        for (int start = 0; start < instances.Count; start += batch)
        {
            int count = Mathf.Min(batch, instances.Count - start);

            if (Build(instances, start, count, parent, $"{name} {built}", shadows))
                built++;
        }

        return built;
    }

    private bool Build(IReadOnlyList<DecorInstance> instances, int start, int count, Transform parent, string name, ShadowCastingMode shadows)
    {
        var buckets = new List<CombineInstance>[_materials.Count];

        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<CombineInstance>();

        Vector3 origin = instances[start].Position;

        for (int i = 0; i < count; i++)
        {
            DecorInstance instance = instances[start + i];

            Matrix4x4 world = Matrix4x4.TRS(
                instance.Position - origin,
                Quaternion.Euler(0f, instance.Rotation, 0f),
                new Vector3(instance.Scale, instance.Height, instance.Scale));

            foreach (Part part in _parts)
            {
                buckets[_materials.IndexOf(part.Material)].Add(new CombineInstance
                {
                    mesh = part.Mesh,
                    subMeshIndex = part.SubMesh,
                    transform = world * part.Local
                });
            }
        }

        var merged = new List<CombineInstance>();

        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i].Count == 0)
                continue;

            var slice = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            slice.CombineMeshes(buckets[i].ToArray(), true, true);

            merged.Add(new CombineInstance { mesh = slice, subMeshIndex = 0, transform = Matrix4x4.identity });
        }

        if (merged.Count == 0)
            return false;

        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

        mesh.CombineMeshes(merged.ToArray(), false, false);
        mesh.RecalculateBounds();

        foreach (CombineInstance slice in merged)
            Object.DestroyImmediate(slice.mesh);

        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = origin;

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = holder.AddComponent<MeshRenderer>();

        renderer.sharedMaterials = _materials.ToArray();
        renderer.shadowCastingMode = shadows;

        GeneratedMesh.Own(holder);

        return true;
    }

    private PrefabProfile Probe(GameObject prefab)
    {
        _parts.Clear();
        _materials.Clear();

        if (prefab == null)
            return PrefabProfile.Empty;

        var profile = new PrefabProfile
        {
            HasLodGroup = prefab.GetComponentInChildren<LODGroup>(true) != null,
            HasCollider = prefab.GetComponentInChildren<Collider>(true) != null
        };

        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        bool readable = true;

        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer))
                continue;

            Matrix4x4 local = toRoot * filter.transform.localToWorldMatrix;
            Material[] materials = renderer.sharedMaterials;

            profile.Vertices += filter.sharedMesh.vertexCount;
            readable &= filter.sharedMesh.isReadable;

            for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
            {
                Material material = sub < materials.Length ? materials[sub] : null;

                if (material == null)
                    continue;

                if (!_materials.Contains(material))
                    _materials.Add(material);

                _parts.Add(new Part(filter.sharedMesh, sub, material, local));
            }
        }

        profile.HasMesh = _parts.Count > 0;
        profile.Readable = readable;
        profile.Instanceable = _parts.Count == 1 && _materials.Count == 1 && !profile.HasLodGroup && _materials[0].enableInstancing;

        return profile;
    }
}
