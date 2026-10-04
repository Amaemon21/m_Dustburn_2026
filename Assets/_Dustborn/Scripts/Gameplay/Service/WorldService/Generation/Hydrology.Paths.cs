using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public sealed partial class Hydrology
{
    private bool[] MainMask(HydrologyGrid grid)
    {
        var mask = new bool[grid.Height.Length];
        DrainageValley valley = _valleys?.Main;

        if (valley == null || valley.Points.Count < 2)
            return mask;

        int resolution = grid.Resolution;
        int span = Mathf.CeilToInt(MAIN_REACH / grid.CellSize);

        for (int i = 0; i + 1 < valley.Points.Count; i++)
        {
            Vector2 a = valley.Points[i], b = valley.Points[i + 1];
            Vector2 axis = b - a;
            float length = axis.sqrMagnitude;
            int c0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) / grid.CellSize) - span), c1 = Mathf.Min(resolution - 1, Mathf.FloorToInt(Mathf.Max(a.x, b.x) / grid.CellSize) + span);
            int r0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) / grid.CellSize) - span), r1 = Mathf.Min(resolution - 1, Mathf.FloorToInt(Mathf.Max(a.y, b.y) / grid.CellSize) + span);

            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    var point = new Vector2((c + 0.5f) * grid.CellSize, (r + 0.5f) * grid.CellSize);
                    float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, axis) / length);

                    if ((a + axis * t - point).sqrMagnitude <= MAIN_REACH * MAIN_REACH && !grid.Outlet[r * resolution + c])
                        mask[r * resolution + c] = true;
                }
            }
        }

        return mask;
    }

    private List<(List<int> Cells, int JoinPath, int JoinCell)> TracePaths(HydrologyGrid grid)
    {
        int count = grid.Accumulation.Length;
        float start = RiverStartCells(grid);
        var river = new bool[count];
        var donors = new List<int>[count];

        for (int i = 0; i < count; i++)
            river[i] = grid.Accumulation[i] >= start && !grid.Outlet[i];

        for (int i = 0; i < count; i++)
        {
            int receiver = grid.Receiver[i];

            if (river[i] && receiver >= 0 && river[receiver])
                (donors[receiver] ??= new List<int>()).Add(i);
        }

        var mouths = new List<(int Cell, List<int> Stem, int Score)>();

        for (int i = 0; i < count; i++)
        {
            if (!river[i] || grid.Receiver[i] >= 0 && river[grid.Receiver[i]])
                continue;

            List<int> stem = Stem(grid, donors, i);
            mouths.Add((i, stem, MainScore(stem)));
        }

        mouths.Sort((a, b) =>
        {
            if (a.Score != b.Score)
                return b.Score.CompareTo(a.Score);

            float fa = grid.Accumulation[a.Cell], fb = grid.Accumulation[b.Cell];

            return fa != fb ? fb.CompareTo(fa) : a.Cell.CompareTo(b.Cell);
        });

        var paths = new List<(List<int> Cells, int JoinPath, int JoinCell)>();
        var queue = new Queue<(List<int> Stem, int JoinPath, int JoinCell)>();

        foreach ((int _, List<int> stem, int _) in mouths)
            queue.Enqueue((stem, -1, -1));

        MainCells = mouths.Count > 0 && mouths[0].Score >= MIN_MAIN_CELLS ? mouths[0].Score : 0;

        while (queue.Count > 0)
        {
            (List<int> stem, int joinPath, int joinCell) = queue.Dequeue();
            int index = paths.Count;
            var cells = new List<int>(stem.Count + 1);

            for (int k = stem.Count - 1; k >= 0; k--)
                cells.Add(stem[k]);

            if (joinCell >= 0)
                cells.Add(joinCell);
            else if (!IntoSink(grid, cells))
                IntoOutlet(grid, cells);

            if (cells.Count < 2)
                continue;

            paths.Add((cells, joinPath, joinCell));
            var onStem = new HashSet<int>(stem);

            for (int k = stem.Count - 1; k >= 0; k--)
            {
                int cell = stem[k];

                if (donors[cell] == null)
                    continue;

                foreach (int donor in donors[cell])
                {
                    if (!onStem.Contains(donor))
                        queue.Enqueue((Stem(grid, donors, donor), index, cell));
                }
            }
        }

        return paths;
    }

    private static bool IntoSink(HydrologyGrid grid, List<int> cells)
    {
        int receiver = grid.Receiver[cells[^1]];

        return receiver >= 0 && grid.Sink[receiver];
    }

    private static void IntoOutlet(HydrologyGrid grid, List<int> cells)
    {
        int current = grid.Receiver[cells[^1]];

        for (int step = 0; step < OUTLET_CELLS && current >= 0; step++)
        {
            cells.Add(current);
            current = grid.Outlet[current] ? Deeper(grid, current, cells) : grid.Receiver[current];
        }
    }

    private static int Deeper(HydrologyGrid grid, int cell, List<int> taken)
    {
        int resolution = grid.Resolution;
        int column = cell % resolution, row = cell / resolution;
        int best = -1;
        float lowest = grid.Height[cell];

        for (int direction = 0; direction < 8; direction++)
        {
            int c = column + OffsetX[direction], r = row + OffsetZ[direction];

            if (c < 0 || r < 0 || c >= resolution || r >= resolution)
                continue;

            int next = r * resolution + c;

            if (!grid.Outlet[next] || grid.Height[next] >= lowest || taken.Contains(next))
                continue;

            lowest = grid.Height[next];
            best = next;
        }

        return best;
    }

    private List<int> Stem(HydrologyGrid grid, List<int>[] donors, int first)
    {
        var stem = new List<int> { first };

        for (int current = first; donors[current] != null;)
        {
            int best = -1;

            foreach (int donor in donors[current])
            {
                if (best < 0 || Upstream(grid, donor, best))
                    best = donor;
            }

            stem.Add(best);
            current = best;
        }

        return stem;
    }

    private bool Upstream(HydrologyGrid grid, int candidate, int best)
    {
        bool main = _main != null && _main[candidate], other = _main != null && _main[best];

        if (main != other)
            return main;

        float a = grid.Accumulation[candidate], b = grid.Accumulation[best];

        return a != b ? a > b : candidate < best;
    }

    private int MainScore(List<int> stem)
    {
        if (_main == null)
            return 0;

        int score = 0;

        foreach (int cell in stem)
        {
            if (_main[cell])
                score++;
        }

        return score;
    }
}
