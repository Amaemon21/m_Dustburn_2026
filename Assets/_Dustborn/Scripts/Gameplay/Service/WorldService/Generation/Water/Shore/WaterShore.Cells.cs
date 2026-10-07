using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static partial class WaterShore
{
    public static void Refresh(WaterMap water, HeightMap map)
    {
        Classify(water, map, (cell, i, j) => StoredOwner(water, cell, i, j), cell => water.DetailSlot[cell] >= 0 || water.Kinds[cell] != 0);

        for (int pass = 0; pass < SEAL_PASSES && Seal(water, map) > 0; pass++)
            Classify(water, map, (cell, i, j) => StoredOwner(water, cell, i, j), cell => water.DetailSlot[cell] >= 0 || water.Kinds[cell] != 0);

        Distances(water);
    }

    private static short StoredOwner(WaterMap water, int cell, int i, int j)
    {
        int start = water.DetailStart(cell);

        if (start < 0)
            return water.CellOwner(cell);

        int local = (j - cell / water.Resolution * WaterMap.SUBDIVISION) * WaterMap.CELL_SIDE_NODES + i - cell % water.Resolution * WaterMap.SUBDIVISION;

        return water.DetailOwner(start + local);
    }

    public static void Classify(WaterMap water, HeightMap map, Func<int, int, int, short> ownerOf, Func<int, bool> consider)
    {
        int resolution = water.Resolution;
        int sub = WaterMap.SUBDIVISION;
        float step = water.NodeStep;

        var rowCells = new List<int>[resolution];
        var rowOwners = new List<short>[resolution];
        var rowGround = new List<float>[resolution];

        Parallel.For(0, resolution, row =>
        {
            var owners = new short[WaterMap.CELL_NODES];
            var ground = new float[WaterMap.CELL_NODES];
            var tally = new List<(short Owner, int Wet, int Mesh)>(4);

            for (int column = 0; column < resolution; column++)
            {
                int cell = row * resolution + column;

                if (!consider(cell))
                    continue;

                for (int k = 0; k < WaterMap.CELL_NODES; k++)
                {
                    int i = column * sub + k % WaterMap.CELL_SIDE_NODES;
                    int j = row * sub + k / WaterMap.CELL_SIDE_NODES;

                    owners[k] = ownerOf(cell, i, j);
                    ground[k] = map.SampleWorldSmooth(i * step, j * step);
                }

                if (!Evaluate(water, owners, ground, tally, out WaterKind kind, out short body))
                {
                    water.Kinds[cell] = (byte)kind;
                    water.BodyIds[cell] = body;
                    continue;
                }

                water.Kinds[cell] = (byte)kind;
                water.BodyIds[cell] = body;

                rowCells[row] ??= new List<int>();
                rowOwners[row] ??= new List<short>();
                rowGround[row] ??= new List<float>();

                rowCells[row].Add(cell);
                rowOwners[row].AddRange(owners);
                rowGround[row].AddRange(ground);
            }
        });

        var cells = new List<int>();
        var allOwners = new List<short>();
        var allGround = new List<float>();

        for (int row = 0; row < resolution; row++)
        {
            if (rowCells[row] == null)
                continue;

            cells.AddRange(rowCells[row]);
            allOwners.AddRange(rowOwners[row]);
            allGround.AddRange(rowGround[row]);
        }

        water.SetDetail(cells.ToArray(), allOwners.ToArray(), allGround.ToArray());
    }

    private static bool Evaluate(WaterMap water, short[] owners, float[] ground, List<(short Owner, int Wet, int Mesh)> tally,
        out WaterKind kind, out short body)
    {
        tally.Clear();

        bool anyMesh = false, allMesh = true, same = true, seaWet = false;
        short first = owners[0];

        for (int k = 0; k < owners.Length; k++)
        {
            short owner = owners[k];
            float level = water.LevelOf(owner);
            bool mesh = WaterMap.MeshInside(level, ground[k]);
            bool wet = ground[k] < level;

            anyMesh |= mesh;
            allMesh &= mesh;
            same &= owner == first;

            if (owner == WaterMap.OWNER_SEA)
                seaWet |= wet;

            if (owner < 0 || !mesh)
                continue;

            int at = tally.Count - 1;

            while (at >= 0 && tally[at].Owner != owner)
                at--;

            if (at < 0)
                tally.Add((owner, wet ? 1 : 0, 1));
            else
                tally[at] = (owner, tally[at].Wet + (wet ? 1 : 0), tally[at].Mesh + 1);
        }

        body = -1;
        int bestWet = 0, bestMesh = 0;

        foreach ((short owner, int wet, int mesh) in tally)
        {
            if (wet > bestWet || wet == bestWet && mesh > bestMesh || wet == bestWet && mesh == bestMesh && body >= 0 && owner < body)
            {
                body = owner;
                bestWet = wet;
                bestMesh = mesh;
            }
        }

        kind = bestWet > 0 ? water.Bodies[body].Kind : seaWet ? WaterKind.Sea : WaterKind.None;

        if (!anyMesh)
        {
            kind = WaterKind.None;
            body = -1;
            return false;
        }

        bool full = allMesh && same && first != WaterMap.OWNER_NONE && (first >= 0 ? bestWet > 0 : seaWet);

        if (full)
        {
            kind = water.KindOf(first);
            body = first >= 0 ? first : (short)-1;
            return false;
        }

        return true;
    }
}
