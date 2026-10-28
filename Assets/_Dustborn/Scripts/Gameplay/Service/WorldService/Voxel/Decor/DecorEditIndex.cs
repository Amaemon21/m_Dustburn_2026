using System.Collections.Generic;
using UnityEngine;

public sealed class DecorEditIndex
{
    private static readonly List<DecorEdit> None = new();

    private readonly HashSet<DecorKey> _overridden = new();
    private readonly List<DecorEdit>[] _moved;
    private readonly bool[] _touched;

    public int Count { get; }
    public int Ignored { get; }

    public DecorEditIndex(IReadOnlyList<DecorEdit> edits, IReadOnlyList<VoxelDecorLayer> layers)
    {
        _moved = new List<DecorEdit>[layers.Count];
        _touched = new bool[layers.Count];

        foreach (DecorEdit edit in edits)
        {
            if (!Matches(edit, layers) || !_overridden.Add(edit.Key))
            {
                Ignored++;
                continue;
            }

            Count++;
            _touched[edit.Layer] = true;

            if (!edit.Removed)
                (_moved[edit.Layer] ??= new List<DecorEdit>()).Add(edit);
        }
    }

    public static DecorEditIndex For(DecorEditsAsset asset, int seed, IReadOnlyList<VoxelDecorLayer> layers)
    {
        if (asset == null || asset.Edits.Count == 0)
            return null;

        if (!asset.Fits(seed))
        {
            Debug.LogWarning($"Decor edits {asset.name} were made on seed {asset.Seed} and this world is seed {seed}: none of them are applied");

            return null;
        }

        var index = new DecorEditIndex(asset.Edits, layers);

        if (index.Ignored > 0)
            Debug.LogWarning($"Decor edits {asset.name}: {index.Ignored} of {asset.Edits.Count} point at a decor layer that is gone or holds another prefab now, they are skipped");

        return index;
    }

    public bool Touches(int layer)
    {
        return layer >= 0 && layer < _touched.Length && _touched[layer];
    }

    public bool Overrides(DecorKey key)
    {
        return _overridden.Contains(key);
    }

    public IReadOnlyList<DecorEdit> Moved(int layer)
    {
        return Touches(layer) ? _moved[layer] ?? None : None;
    }

    private static bool Matches(DecorEdit edit, IReadOnlyList<VoxelDecorLayer> layers)
    {
        if (edit.Layer < 0 || edit.Layer >= layers.Count)
            return false;

        VoxelDecorLayer layer = layers[edit.Layer];

        return layer.Kind != DecorKind.Grass && layer.Prefab == edit.Prefab;
    }
}
