using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public partial class MeshCombiner : IDisposable
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

    private static readonly Dictionary<Material, Material> Instancing = new();

    private readonly List<Part> _parts = new();
    private readonly List<Material> _materials = new();
    private readonly DecorTint _tint;
    private readonly bool _card;

    public PrefabProfile Profile { get; private set; }

    public IReadOnlyList<Material> Materials => _materials;
    public bool IsCard => _card;

    public GrassCardMeshBuild ScheduleCards(IReadOnlyList<DecorInstance> instances, int start, int count)
    {
        return new GrassCardMeshBuild(instances, start, count, _tint);
    }

    public void AttachCards(GrassCardMeshBuild build, Transform parent, string name, ShadowCastingMode shadows)
    {
        Attach(build.Upload(name), new[] { _materials[0] }, parent, build.Origin, name, shadows);
    }

    public MeshCombiner(GameObject prefab, DecorTint tint)
    {
        _tint = tint;

        Profile = Probe(prefab);
    }

    public MeshCombiner(Mesh mesh, Material material, DecorTint tint)
    {
        _tint = tint;
        _card = true;

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

    private MeshCombiner(DecorTint tint)
    {
        _tint = tint;

        Profile = PrefabProfile.Empty;
    }

    public static Quaternion Orientation(in DecorInstance instance)
    {
        Quaternion turn = Quaternion.Euler(0f, instance.Rotation, 0f);

        if (instance.Lean <= 0f)
            return turn;

        Vector3 normal = new Vector3(-instance.Slope.x, 1f, -instance.Slope.y).normalized;

        return Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, instance.Lean)) * turn;
    }

    public static Matrix4x4 Placement(in DecorInstance instance)
    {
        return Matrix4x4.TRS(instance.Position, Orientation(instance), new Vector3(instance.Scale, instance.Height, instance.Scale));
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
        Vector3 origin = instances[start].Position;

        if (_card)
        {
            Attach(GrassCardFactory.Batch(instances, start, count, origin, _tint, name), new[] { _materials[0] }, parent, origin, name, shadows);

            return true;
        }

        int slots = _materials.Count * DecorTint.LEVELS;
        var buckets = new List<CombineInstance>[slots];

        for (int i = 0; i < count; i++)
        {
            DecorInstance instance = instances[start + i];
            int level = _tint.Level(instance.Shade);

            Matrix4x4 world = Matrix4x4.TRS(instance.Position - origin, Orientation(instance), new Vector3(instance.Scale, instance.Height, instance.Scale));

            foreach (Part part in _parts)
            {
                int slot = level * _materials.Count + _materials.IndexOf(part.Material);

                (buckets[slot] ??= new List<CombineInstance>()).Add(new CombineInstance
                {
                    mesh = part.Mesh,
                    subMeshIndex = part.SubMesh,
                    transform = world * part.Local
                });
            }
        }

        var merged = new List<CombineInstance>();
        var materials = new List<Material>();

        for (int slot = 0; slot < slots; slot++)
        {
            if (buckets[slot] == null)
                continue;

            var slice = new Mesh { indexFormat = IndexFormat.UInt32 };

            slice.CombineMeshes(buckets[slot].ToArray(), true, true);

            if (slice.vertexCount == 0 || slice.GetIndexCount(0) == 0)
            {
                UnityEngine.Object.DestroyImmediate(slice);
                continue;
            }

            merged.Add(new CombineInstance { mesh = slice, subMeshIndex = 0, transform = Matrix4x4.identity });
            materials.Add(Variant(_materials[slot % _materials.Count], slot / _materials.Count));
        }

        if (merged.Count == 0)
            return false;

        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };

        mesh.CombineMeshes(merged.ToArray(), false, false);
        mesh.RecalculateBounds();

        Bounds bounds = mesh.bounds;

        bounds.Expand(2f * WIND_MARGIN);
        mesh.bounds = bounds;

        foreach (CombineInstance slice in merged)
            UnityEngine.Object.DestroyImmediate(slice.mesh);

        if (mesh.subMeshCount < materials.Count)
        {
            if (Misfits.Add(name))
                Debug.LogWarning($"{name}: combined decor came out with {mesh.subMeshCount} submeshes for {materials.Count} materials, the extra materials are dropped");

            materials.RemoveRange(mesh.subMeshCount, materials.Count - mesh.subMeshCount);
        }

        Attach(mesh, materials.ToArray(), parent, origin, name, shadows);

        return true;
    }

    private static void Attach(Mesh mesh, Material[] materials, Transform parent, Vector3 origin, string name, ShadowCastingMode shadows)
    {
        var holder = new GameObject(name);

        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = origin;

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = holder.AddComponent<MeshRenderer>();

        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = shadows;

        GeneratedMesh.Own(holder);
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
            HasCollider = prefab.GetComponentInChildren<Collider>(true) != null,
            Interactive = prefab.GetComponentInChildren<IDecorObject>(true) != null
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
