using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct DecorPick
{
    public int Layer { get; }
    public DecorInstance Instance { get; }

    public DecorPick(int layer, DecorInstance instance)
    {
        Layer = layer;
        Instance = instance;
    }

    public DecorKey Key => new(Layer, Instance.CellX, Instance.CellY);

    public Vector2 Point => new(Instance.Position.x, Instance.Position.z);
}

public sealed class DecorEditSession : IDisposable
{
    private readonly VoxelDensityField _field;
    private readonly List<DecorInstance> _scratch = new();

    public VoxelDecorPlacer Placer { get; }
    public int Seed { get; }

    public DecorEditSession(VoxelDensityField field, VoxelDecorPlacer placer, int seed)
    {
        _field = field;
        Placer = placer;
        Seed = seed;
    }

    public float Surface(Vector2 point)
    {
        return _field.Surface(point.x, point.y);
    }

    public bool Accepts(DecorEditsAsset edits)
    {
        return edits == null || edits.Fits(Seed);
    }

    public void Reindex(DecorEditsAsset edits)
    {
        Placer.Edits = DecorEditIndex.For(edits, Seed, Placer.Layers);
    }

    public bool TryPick(Vector2 point, float radius, out DecorPick pick)
    {
        pick = default;

        float best = radius * radius;
        bool found = false;
        var origin = new Vector2(point.x - radius, point.y - radius);

        for (int layer = 0; layer < Placer.Layers.Count; layer++)
        {
            if (Placer.Layers[layer].Kind == DecorKind.Grass)
                continue;

            _scratch.Clear();
            Placer.Place(layer, origin, 2f * radius, _scratch);

            foreach (DecorInstance instance in _scratch)
            {
                float range = DistanceUtility.SqrDistance(point, new Vector2(instance.Position.x, instance.Position.z));

                if (range > best)
                    continue;

                best = range;
                pick = new DecorPick(layer, instance);
                found = true;
            }
        }

        return found;
    }

    public bool TryFind(DecorKey key, Vector2 near, float radius, out DecorPick pick)
    {
        pick = default;

        if (key.Layer < 0 || key.Layer >= Placer.Layers.Count)
            return false;

        _scratch.Clear();
        Placer.Place(key.Layer, new Vector2(near.x - radius, near.y - radius), 2f * radius, _scratch);

        foreach (DecorInstance instance in _scratch)
        {
            if (instance.CellX != key.CellX || instance.CellY != key.CellY)
                continue;

            pick = new DecorPick(key.Layer, instance);

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _field.Dispose();
    }
}
