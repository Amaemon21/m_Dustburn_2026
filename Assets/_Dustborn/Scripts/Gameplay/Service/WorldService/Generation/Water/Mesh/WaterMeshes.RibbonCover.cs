using System.Collections.Generic;
using UnityEngine;

public static partial class WaterMeshes
{
    private sealed class RibbonCover
    {
        private const float CELL = 8f;
        private const float EDGE_TOLERANCE = 1e-4f;

        private readonly List<Vector3> _corners = new();
        private readonly Dictionary<long, List<int>> _cells = new();
        private readonly List<int> _pending = new();

        public int Source { get; private set; } = -1;

        public void Begin(int source)
        {
            Commit();
            Source = source;
        }

        public void Commit()
        {
            foreach (int triangle in _pending)
            {
                Vector3 a = _corners[triangle], b = _corners[triangle + 1], c = _corners[triangle + 2];
                int minX = Cell(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), maxX = Cell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int minZ = Cell(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), maxZ = Cell(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));

                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        long key = Key(x, z);

                        if (!_cells.TryGetValue(key, out List<int> list))
                            _cells[key] = list = new List<int>();

                        list.Add(triangle);
                    }
                }
            }

            _pending.Clear();
        }

        public void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            _pending.Add(_corners.Count);
            _corners.Add(a);
            _corners.Add(b);
            _corners.Add(c);
        }

        public bool Inside(float x, float z, out float surface)
        {
            surface = float.NegativeInfinity;

            if (!_cells.TryGetValue(Key(Cell(x), Cell(z)), out List<int> list))
                return false;

            foreach (int triangle in list)
            {
                if (Barycentric(_corners[triangle], _corners[triangle + 1], _corners[triangle + 2], x, z, out float height))
                    surface = Mathf.Max(surface, height);
            }

            return !float.IsNegativeInfinity(surface);
        }

        private static bool Barycentric(Vector3 a, Vector3 b, Vector3 c, float x, float z, out float height)
        {
            height = 0f;
            float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);

            if (Mathf.Abs(area) < 1e-8f)
                return false;

            float u = ((b.x - x) * (c.z - z) - (c.x - x) * (b.z - z)) / area;
            float v = ((c.x - x) * (a.z - z) - (a.x - x) * (c.z - z)) / area;
            float w = 1f - u - v;

            if (u < -EDGE_TOLERANCE || v < -EDGE_TOLERANCE || w < -EDGE_TOLERANCE)
                return false;

            height = u * a.y + v * b.y + w * c.y;
            return true;
        }

        private static int Cell(float value)
        {
            return Mathf.FloorToInt(value / CELL);
        }

        private static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }
}
