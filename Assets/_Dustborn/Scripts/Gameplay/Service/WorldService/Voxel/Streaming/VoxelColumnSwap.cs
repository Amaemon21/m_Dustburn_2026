using System.Collections.Generic;

public sealed class VoxelColumnSwap
{
    public List<VoxelColumnKey> Added { get; } = new();
    public List<VoxelColumnKey> Removed { get; } = new();

    public static List<VoxelColumnSwap> Plan(IReadOnlyList<VoxelColumnKey> added, IReadOnlyList<VoxelColumnKey> removed)
    {
        int[] parents = new int[added.Count + removed.Count];

        for (int i = 0; i < parents.Length; i++)
            parents[i] = i;

        for (int a = 0; a < added.Count; a++)
        {
            for (int r = 0; r < removed.Count; r++)
            {
                if (Overlaps(added[a], removed[r]))
                    parents[Root(parents, a)] = Root(parents, added.Count + r);
            }
        }

        var groups = new Dictionary<int, VoxelColumnSwap>();
        var result = new List<VoxelColumnSwap>();

        for (int i = 0; i < parents.Length; i++)
        {
            int root = Root(parents, i);

            if (!groups.TryGetValue(root, out VoxelColumnSwap group))
            {
                group = new VoxelColumnSwap();
                groups.Add(root, group);
                result.Add(group);
            }

            if (i < added.Count)
                group.Added.Add(added[i]);
            else
                group.Removed.Add(removed[i - added.Count]);
        }

        return result;
    }

    private static int Root(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static bool Overlaps(VoxelColumnKey a, VoxelColumnKey b)
    {
        int sa = 1 << a.Lod;
        int sb = 1 << b.Lod;

        return a.X * sa < (b.X + 1) * sb && b.X * sb < (a.X + 1) * sa
            && a.Z * sa < (b.Z + 1) * sb && b.Z * sb < (a.Z + 1) * sa;
    }
}
