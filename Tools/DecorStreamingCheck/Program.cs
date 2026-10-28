using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UObject = UnityEngine.Object;

static class Program
{
    private static int _checks;

    static void Main()
    {
        Check("last copy keeps its own layer materials and key", LastCopy);
        Check("removed last copy advances without spawning", RemovedCopy);
        Check("combined layers yield after one mesh batch", CombinedBatches);
        Check("instanced layers yield after 256 copies", InstancedBatches);
        Check("pooled copies rebind before activation without inflating bounds", Reuse);
        Check("pool isolates layers and limits retained objects", PoolLimits);
        Check("destroyed pooled objects release their capacity", DestroyedPoolEntry);
        Check("deferred demolition visits each child once", DeferredDemolition);
        Check("demolition resumes across frames and pooled reparenting", MixedDemolition);
        Check("block detachment clears identity and local damage", DetachBlock);
        Check("material diagnostic stops at 40 reports", DiagnosticLimit);
        Check("grass job yields until ready and uploads every copy once", GrassJob);
        Check("cancelled grass jobs release their buffers", CancelGrass);
        Check("prop regrounding deduplicates requests and limits raycasts", PropReground);
        Check("destroyed props are skipped during queued regrounding", DestroyedProp);
        Console.WriteLine($"PASS: {_checks} decor streaming checks (fake engine, no Unity execution).");
    }

    private static void Check(string name, Action test)
    {
        test();
        UObject.FlushDestroy();
        _checks++;
        Console.WriteLine($"PASS {name}");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static GameObject Prefab(string name, int slots = 1, bool objects = true)
    {
        var prefab = new GameObject(name)
        {
            Profile = new PrefabProfile { HasMesh = true, HasLodGroup = objects, Readable = true, Vertices = 100 }
        };
        prefab.AddComponent<MeshFilter>().sharedMesh = new Mesh { subMeshCount = slots };
        MeshRenderer renderer = prefab.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Enumerable.Range(0, slots).Select(i => new Material { name = name + i }).ToArray();
        renderer.localBounds = new Bounds { Width = 1f };
        return prefab;
    }

    private static VoxelDecorPlacer Placer(params (GameObject Prefab, int Count)[] layers)
    {
        var placer = new VoxelDecorPlacer();
        foreach ((GameObject prefab, int count) in layers)
        {
            placer.Layers.Add(new VoxelDecorLayer { Prefab = prefab, Kind = DecorKind.Tree });
            placer.Data.Add(Enumerable.Range(0, count).Select(i => new DecorInstance
            {
                CellX = i, CellY = i + 10, Scale = 1f, Height = 1f, Shade = i % 4,
                Position = new Vector3(i, 0f, 0f)
            }).ToList());
        }
        return placer;
    }

    private static VoxelDecorBuild Build(GameObject parent) => new(default, 32f, parent.transform, 0);

    private static void LastCopy()
    {
        var hook = new Hook();
        using var spawner = new VoxelDecorSpawner(Placer((Prefab("TwoSlots", 2), 2), (Prefab("OneSlot"), 1)), new(), 48000, false) { Hook = hook };
        var root = new GameObject("Patch");
        spawner.Spawn(Build(root));
        Require(root.transform.childCount == 3, "A layer-boundary copy was lost.");
        Require(root.transform.GetChild(0).gameObject.GetComponent<MeshRenderer>().sharedMaterials.Length == 2, "Wrong first materials.");
        Require(root.transform.GetChild(1).gameObject.GetComponent<MeshRenderer>().sharedMaterials.Length == 2, "Last copy received another layer's materials.");
        Require(root.transform.GetChild(2).gameObject.GetComponent<MeshRenderer>().sharedMaterials.Length == 1, "Final layer materials corrupted.");
        Require(hook.Keys.Select(key => key.Layer).SequenceEqual(new[] { 0, 0, 1 }), "Gameplay keys crossed layers.");
    }

    private static void RemovedCopy()
    {
        var hook = new Hook { Removed = new DecorKey(0, 0, 10) };
        using var spawner = new VoxelDecorSpawner(Placer((Prefab("Removed"), 1), (Prefab("Present"), 1)), new(), 48000, false) { Hook = hook };
        var root = new GameObject("Patch");
        spawner.Spawn(Build(root));
        Require(root.transform.childCount == 1 && hook.Keys.Single().Layer == 1, "Removal blocked the next layer.");
    }

    private static void CombinedBatches()
    {
        MeshCombiner.Combined.Clear();
        using var spawner = new VoxelDecorSpawner(Placer((Prefab("Combined", objects: false), 13)), new(), 400, false);
        VoxelDecorBuild build = Build(new GameObject("Patch"));
        spawner.Step(build, 1);
        spawner.Step(build, 1);
        Require(MeshCombiner.Combined.Count == 4 && build.Cursor == 4, "One step exceeded the mesh batch.");
        spawner.Spawn(build);
        Require(MeshCombiner.Combined.SequenceEqual(Enumerable.Range(0, 13)), "Combined batch boundaries lost or repeated instances.");
    }

    private static void InstancedBatches()
    {
        MeshCombiner.Instanced.Clear();
        GameObject prefab = Prefab("Instanced", objects: false);
        prefab.Profile.Readable = false;
        prefab.Profile.Instanceable = true;
        using var spawner = new VoxelDecorSpawner(Placer((prefab, 600)), new(), 48000, false);
        VoxelDecorBuild build = Build(new GameObject("Patch"));
        spawner.Step(build, 1);
        spawner.Step(build, 1);
        Require(MeshCombiner.Instanced.Count == 256, "One step exceeded the instance batch.");
        spawner.Spawn(build);
        Require(MeshCombiner.Instanced.SequenceEqual(Enumerable.Range(0, 600)), "Instanced batch boundaries lost or repeated instances.");
    }

    private static void Reuse()
    {
        var world = new GameObject("World");
        var patch = new GameObject("Patch");
        var hook = new Hook { RequireInactive = true };
        VoxelDecorPlacer placer = Placer((Prefab("Tree"), 1));
        using var spawner = new VoxelDecorSpawner(placer, new(), 48000, false) { Hook = hook };
        spawner.EnablePooling(world.transform);
        spawner.Spawn(Build(patch));
        GameObject copy = patch.transform.GetChild(0).gameObject;
        float bounds = copy.GetComponent<MeshRenderer>().localBounds.Width;
        for (int i = 0; i < 20; i++)
        {
            Require(spawner.Recycle(copy), "Live copy was not recycled.");
            Require(!copy.activeSelf && hook.Live.Count == 0, "Returned copy remained bound or active.");
            DecorInstance next = placer.Data[0][0];
            next.Shade = i % 4;
            placer.Data[0][0] = next;
            spawner.Spawn(Build(patch));
            Require(patch.transform.GetChild(0).gameObject == copy, "Pool failed to reuse its copy.");
            Require(copy.GetComponent<MeshRenderer>().sharedMaterials[0].name == "Tree0/shade" + i % 4, "Tint compounded across pool reuse.");
        }
        Require(spawner.Instantiated == 1 && spawner.Reused == 20, "Pool still instantiated on reuse.");
        Require(copy.GetComponent<MeshRenderer>().localBounds.Width == bounds, "Wind bounds expanded on every reuse.");
    }

    private static void PoolLimits()
    {
        var world = new GameObject("World");
        using var pool = new VoxelDecorPool(world.transform, 10);
        GameObject prefab = Prefab("Tree");
        for (int layer = 0; layer < 10; layer++)
        {
            var copies = new List<GameObject>();
            for (int i = 0; i < 70; i++)
                copies.Add(pool.Take(prefab, layer, world.transform, out _));
            foreach (GameObject copy in copies)
                pool.Return(copy);
        }
        UObject.FlushDestroy();
        Transform root = world.transform.GetChild(0);
        Require(root.childCount == 512, "Pool did not enforce its global capacity.");
        Require(root.Children.GroupBy(child => child.gameObject.GetComponent<PooledDecorObject>().Layer).All(group => group.Count() <= 64), "Pool exceeded a layer capacity.");
        GameObject fresh = pool.Take(prefab, 9, world.transform, out bool reused);
        Require(!reused && fresh.GetComponent<PooledDecorObject>().Layer == 9, "Pool borrowed a different layer's materials.");
        pool.Clear();
        UObject.FlushDestroy();
        Require(root.childCount == 0, "Pool clear retained inactive objects.");
    }

    private static void DestroyedPoolEntry()
    {
        var root = new GameObject("World");
        using var pool = new VoxelDecorPool(root.transform, 1);
        GameObject prefab = Prefab("Tree");
        GameObject first = pool.Take(prefab, 0, root.transform, out _);
        pool.Return(first);
        UObject.Destroy(first);
        UObject.FlushDestroy();
        GameObject second = pool.Take(prefab, 0, root.transform, out bool reused);
        Require(!reused && second != null, "Destroyed member was reused.");
        pool.Return(second);
        Require(pool.Take(prefab, 0, root.transform, out reused) == second && reused, "Destroyed entry corrupted pool accounting.");
    }

    private static void DeferredDemolition()
    {
        var root = new GameObject("Patch");
        for (int i = 0; i < 20; i++)
            new GameObject(i.ToString()).transform.SetParent(root.transform, false);
        var demolition = new VoxelDecorDemolition(root);
        var seen = new HashSet<GameObject>();
        while (demolition.TryTake(out GameObject child))
        {
            Require(seen.Add(child), "Deferred destruction visited a child twice.");
            GeneratedMesh.Destroy(child);
        }
        Require(demolition.Complete && seen.Count == 20 && root.transform.childCount == 20, "Demolition depends on immediate Destroy.");
    }

    private static void MixedDemolition()
    {
        var root = new GameObject("Patch");
        var parking = new GameObject("Pool");
        for (int i = 0; i < 12; i++)
            new GameObject(i.ToString()).transform.SetParent(root.transform, false);
        var demolition = new VoxelDecorDemolition(root);
        var seen = new HashSet<GameObject>();
        for (int frame = 0; !demolition.Complete; frame++)
        {
            for (int i = 0; i < 3 && demolition.TryTake(out GameObject child); i++)
            {
                Require(seen.Add(child), "Frame boundary repeated a child.");
                if (i % 2 == 0)
                    child.transform.SetParent(parking.transform, false);
                else
                    GeneratedMesh.Destroy(child);
            }
            UObject.FlushDestroy();
            Require(frame < 10, "Demolition did not terminate.");
        }
        Require(seen.Count == 12 && root.transform.childCount == 0, "Mixed recycling and deletion skipped children.");
    }

    private static void DetachBlock()
    {
        var go = new GameObject("Block");
        BlockView view = go.AddComponent<BlockView>();
        typeof(BlockView).GetField("_config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, new BlockConfig());
        Block local = (Block)view.Damageable;
        view.Detach();
        Require(local.Disposed && !ReferenceEquals(local, view.Damageable), "Local damage survived reuse.");
        var service = new BlockService();
        view.Attach(service, "old");
        service.Views["old"] = view;
        view.Detach();
        Require(view.Id == null && service.Views.Count == 0, "Released view remained in the damage lookup.");
        view.Detach();
        view.Attach(service, "new");
        Require(view.Id == "new", "Reused view kept the old identity.");
    }

    private static void DiagnosticLimit()
    {
        Debug.Warnings.Clear();
        for (int i = 0; i < 80; i++)
        {
            GameObject prefab = Prefab("Mismatch" + i);
            prefab.GetComponent<MeshRenderer>().sharedMaterials = new[] { new Material(), new Material() };
        }
        typeof(RendererMaterialProbe).GetMethod("Scan", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Require(Debug.Warnings.Count == 40, "Diagnostic report limit is not enforced inside the scan.");
    }

    private sealed class Hook : IDecorObjectHook
    {
        public DecorKey? Removed;
        public bool RequireInactive;
        public readonly List<DecorKey> Keys = new();
        public readonly Dictionary<GameObject, DecorKey> Live = new();
        public bool IsRemoved(DecorKey key) => Removed.HasValue && Removed.Value.Equals(key);
        public void Spawned(GameObject copy, DecorKey key)
        {
            Require(!RequireInactive || !copy.activeInHierarchy, "Pooled copy activated before rebinding.");
            Require(!Live.ContainsKey(copy), "Old gameplay binding survived pool return.");
            Keys.Add(key);
            Live.Add(copy, key);
        }
        public void Despawned(GameObject copy) => Live.Remove(copy);
    }

    private static VoxelDecorSpawner CardSpawner(int count, out VoxelDecorBuild build)
    {
        VoxelDecorPlacer placer = Placer((Prefab("Unused"), count));
        placer.Layers[0].Card = new Texture2D();
        placer.Layers[0].Kind = DecorKind.Grass;
        var spawner = new VoxelDecorSpawner(placer, new(), 48, false);
        var root = new GameObject("World");
        spawner.EnablePooling(root.transform);
        build = Build(root);
        spawner.Step(build, 1);
        spawner.Step(build, 1);
        return spawner;
    }

    private static void GrassJob()
    {
        MeshCombiner.Combined.Clear();
        using VoxelDecorSpawner spawner = CardSpawner(9, out VoxelDecorBuild build);
        GrassCardMeshBuild pending = build.CardMesh;
        Require(pending != null && build.Preparing && build.Cursor == 0, "Grass did not defer its mesh build.");
        Require(spawner.Step(build, 1) == 0 && MeshCombiner.Combined.Count == 0, "A pending grass job blocked or uploaded early.");
        pending.Complete();
        spawner.Step(build, 1);
        Require(pending.Disposed && MeshCombiner.Combined.Count == 4 && build.Cursor == 4, "Grass result did not release its buffers or preserve its cursor.");
        spawner.Spawn(build);
        Require(MeshCombiner.Combined.SequenceEqual(Enumerable.Range(0, 9)), "Grass jobs lost or repeated copies across batches.");
    }

    private static void CancelGrass()
    {
        using VoxelDecorSpawner spawner = CardSpawner(9, out VoxelDecorBuild build);
        GrassCardMeshBuild pending = build.CardMesh;
        build.Done = true;
        build.ReleaseMesh();
        build.ReleaseMesh();
        Require(pending.Disposed && build.CardMesh == null, "Cancelled grass retained native buffers.");
    }

    private static PropInstanceBuilder Props(int count, out GameObject parent)
    {
        var ground = new GameObject("Ground") { layer = 9 };
        ground.AddComponent<GeneratedMesh>();
        Collider collider = ground.AddComponent<Collider>();
        Physics.Hit = origin => new RaycastHit { collider = collider, point = new Vector3(origin.x, 2f, origin.z), normal = Vector3.up, distance = 2f };
        parent = new GameObject("Props");
        GameObject prefab = Prefab("Prop");
        var placements = Enumerable.Range(0, count).Select(i => new PropPlacement
        {
            Prefab = prefab, Position = new Vector3(i % 20, 0f, i / 20)
        }).ToArray();
        var builder = new PropInstanceBuilder(placements, parent.transform, ground.transform, 0f, 32f);
        builder.BuildNext(count);
        Physics.Queries = 0;
        return builder;
    }

    private static void PropReground()
    {
        PropInstanceBuilder builder = Props(100, out _);
        var cell = new Vector2Int(0, 0);
        builder.QueueReground(new[] { cell, cell, cell });
        Require(builder.RegroundPending == 100 && Physics.Queries == 0, "Queueing props performed raycasts or duplicated work.");
        Require(builder.RegroundStep(0f) == 0 && Physics.Queries == 0, "A zero budget performed work.");
        Require(builder.RegroundStep(100f, 7) == 7 && Physics.Queries == 7, "The prop limit was exceeded.");
        builder.QueueReground(new[] { cell });
        Require(builder.RegroundPending == 100, "A repeated request duplicated pending props or lost already-processed props.");
        int frames = 0;
        while (builder.RegroundPending > 0)
        {
            Require(builder.RegroundStep(100f, 7) <= 7, "A frame performed too many prop probes.");
            Require(++frames < 30, "The prop queue did not drain.");
        }
        Require(Physics.Queries == 107 && Physics.LastMask == 1 << 9, "Prop probes were duplicated or used the wrong ground layer.");
    }

    private static void DestroyedProp()
    {
        PropInstanceBuilder builder = Props(10, out GameObject parent);
        builder.QueueReground(new[] { new Vector2Int(0, 0) });
        UObject.Destroy(parent.transform.GetChild(0).gameObject);
        UObject.FlushDestroy();
        builder.RegroundStep(100f);
        Require(builder.RegroundPending == 0 && Physics.Queries == 9, "A destroyed prop was probed or kept in the queue.");
    }
}
