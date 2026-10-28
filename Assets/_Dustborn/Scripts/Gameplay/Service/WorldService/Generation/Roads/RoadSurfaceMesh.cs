using System.Collections.Generic;
using UnityEngine;

public sealed partial class RoadSurfaceMesh
{
    private const float CELL_SIZE = 32f;
    private const float MIN_AREA = 0.00001f;
    private const float FOOTPRINT_SLACK = 0.5f;

    private sealed class Segment
    {
        public Vector2 From;
        public Vector2 To;
        public Vector2 Direction;
        public Vector2 Side;
        public float Length;
        public float Along;
        public float StartWidth;
        public float EndWidth;
        public Vector2 StartNormal;
        public Vector2 EndNormal;
        public int Chain;
        public int Previous = -1;
        public int Next = -1;
        public int Style;
        public bool Tail;
        public int[] Neighbours;
        public int[] Fillets = System.Array.Empty<int>();
        public Fillet Fillet;
        public Vector2 Min;
        public Vector2 Max;
        public Vector2[] Boundary;
        public Vector2[] Hole;
    }

    private sealed class Hole
    {
        public Vector2[] Points;
        public Vector2 Min;
        public Vector2 Max;
    }

    private sealed class Scratch
    {
        public readonly HashSet<int> Seen = new();
        public readonly List<int> Candidates = new();
        public readonly List<int> Covering = new();
        public readonly List<Vector3> Marks = new();

        private readonly Stack<List<Vertex>> _polygons = new();
        private readonly Stack<List<List<Vertex>>> _groups = new();

        public List<Vertex> Polygon()
        {
            return _polygons.Count > 0 ? _polygons.Pop() : new List<Vertex>(16);
        }

        public void Release(List<Vertex> polygon)
        {
            polygon.Clear();
            _polygons.Push(polygon);
        }

        public List<List<Vertex>> Group()
        {
            return _groups.Count > 0 ? _groups.Pop() : new List<List<Vertex>>(4);
        }

        public void Release(List<List<Vertex>> group)
        {
            group.Clear();
            _groups.Push(group);
        }
    }

    private readonly struct Vertex
    {
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Vector2 Point => new(Position.x, Position.z);

        public Vertex(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }

        public static Vertex Lerp(Vertex from, Vertex to, float t)
        {
            return new Vertex(Vector3.Lerp(from.Position, to.Position, t), Vector3.Lerp(from.Normal, to.Normal, t));
        }
    }

    public sealed class Geometry
    {
        public List<Vector3> Vertices { get; } = new();
        public List<Vector3> Normals { get; } = new();
        public List<Vector3> Tangents { get; } = new();
        public List<Vector2> Uv { get; } = new();
        public List<Vector2> Profile { get; } = new();
        public List<Vector3> Markings { get; } = new();
        public List<int> Triangles { get; } = new();
    }

    private readonly List<Segment> _segments = new();
    private readonly List<Chain> _chains = new();
    private readonly List<Fillet> _fillets = new();
    private readonly Dictionary<(int X, int Z), List<int>> _cells = new();
    private readonly WaterMap _water;
    private readonly List<Hole> _bridgeHoles = new();

    public RoadSurfaceMesh(IReadOnlyList<Road> roads, WaterMap water = null)
    {
        _water = water;

        if (water != null)
        {
            foreach (WaterCrossing crossing in water.Crossings)
            {
                if (!crossing.Bridge || crossing.Span <= 0f)
                    continue;

                Vector2 direction = crossing.Direction;
                Vector2 side = new(-direction.y, direction.x);
                float length = crossing.Span * 0.5f;
                float width = crossing.RoadWidth * 0.5f + BridgeMesh.KERB;
                _bridgeHoles.Add(NewHole(new[]
                {
                    crossing.Center - direction * length - side * width,
                    crossing.Center - direction * length + side * width,
                    crossing.Center + direction * length + side * width,
                    crossing.Center + direction * length - side * width
                }));
            }
        }

        Plan(roads);
    }

    private static Hole NewHole(Vector2[] points)
    {
        Vector2 min = points[0];
        Vector2 max = points[0];

        foreach (Vector2 point in points)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return new Hole { Points = points, Min = min, Max = max };
    }

    private static void Register(Dictionary<(int X, int Z), List<int>> grid, Vector2 min, Vector2 max, int index)
    {
        for (int z = Cell(min.y); z <= Cell(max.y); z++)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                var key = (x, z);

                if (!grid.TryGetValue(key, out List<int> items))
                    grid[key] = items = new List<int>();

                items.Add(index);
            }
        }
    }

    public int FilletCount => _fillets.Count;

    public bool Touches(Vector2 min, Vector2 max)
    {
        for (int z = Cell(min.y); z <= Cell(max.y); z++)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                if (!_cells.TryGetValue((x, z), out List<int> items))
                    continue;

                foreach (int index in items)
                {
                    Segment segment = _segments[index];

                    if (segment.Max.x >= min.x && segment.Min.x <= max.x && segment.Max.y >= min.y && segment.Min.y <= max.y)
                        return true;
                }
            }
        }

        return false;
    }

    public bool Touches(VoxelColumnKey column, float chunkMetres)
    {
        var min = new Vector2(column.X * chunkMetres - FOOTPRINT_SLACK, column.Z * chunkMetres - FOOTPRINT_SLACK);
        var max = new Vector2((column.X + 1) * chunkMetres + FOOTPRINT_SLACK, (column.Z + 1) * chunkMetres + FOOTPRINT_SLACK);

        return Touches(min, max);
    }

    public bool Sample(Vector2 point, out Vector2 uv, out Vector3 markings)
    {
        return Sample(point, out uv, out markings, out _);
    }

    public bool Sample(Vector2 point, out Vector2 uv, out Vector3 markings, out Vector2 profile)
    {
        uv = Vector2.zero;
        markings = Vector3.one;
        profile = Vector2.zero;

        if (WaterCrossings.OverSpan(_water, point) || !_cells.TryGetValue((Cell(point.x), Cell(point.y)), out List<int> items))
            return false;

        int owner = int.MaxValue;

        foreach (int index in items)
        {
            if (index < owner && Contains(_segments[index].Boundary, point))
                owner = index;
        }

        if (owner == int.MaxValue)
            return false;

        Segment segment = _segments[owner];
        Vector3 frame = Frame(point, segment);
        uv = new Vector2(frame.x / frame.z, frame.y);
        markings = Marks(point, segment, frame);
        profile = new Vector2(frame.z, segment.Style);
        return true;
    }

    public Geometry Build(VoxelMesh source)
    {
        if (_segments.Count == 0 || source.IsEmpty)
            return new Geometry();

        return Build(source.Vertices.AsArray().ToArray(), source.Normals.AsArray().ToArray(), source.Triangles.AsArray().ToArray());
    }

    public Geometry Build(Vector3[] positions, Vector3[] normals, int[] triangles)
    {
        var geometry = new Geometry();

        if (_segments.Count == 0 || triangles.Length == 0)
            return geometry;

        var scratch = new Scratch();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = new Vertex(positions[triangles[i]], normals[triangles[i]]);
            var b = new Vertex(positions[triangles[i + 1]], normals[triangles[i + 1]]);
            var c = new Vertex(positions[triangles[i + 2]], normals[triangles[i + 2]]);

            if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).y <= MIN_AREA)
                continue;

            Vector2 min = Vector2.Min(a.Point, Vector2.Min(b.Point, c.Point));
            Vector2 max = Vector2.Max(a.Point, Vector2.Max(b.Point, c.Point));
            Query(min, max, scratch);

            if (scratch.Candidates.Count == 0)
                continue;

            scratch.Covering.Clear();

            foreach (int index in scratch.Candidates)
            {
                Segment segment = _segments[index];

                if (segment.Max.x < min.x || segment.Min.x > max.x || segment.Max.y < min.y || segment.Min.y > max.y)
                    continue;

                List<Vertex> polygon = scratch.Polygon();
                polygon.Add(a);
                polygon.Add(b);
                polygon.Add(c);

                for (int edge = 0; edge < segment.Boundary.Length && polygon.Count >= 3; edge++)
                {
                    List<Vertex> clipped = scratch.Polygon();
                    Clip(polygon, segment.Boundary[edge], segment.Boundary[(edge + 1) % segment.Boundary.Length], true, clipped);
                    scratch.Release(polygon);
                    polygon = clipped;
                }

                if (polygon.Count < 3)
                {
                    scratch.Release(polygon);
                    continue;
                }

                List<List<Vertex>> pieces = scratch.Group();
                pieces.Add(polygon);

                foreach (int earlier in scratch.Covering)
                {
                    if (earlier == segment.Previous || earlier == segment.Next)
                        continue;

                    Segment previous = _segments[earlier];
                    pieces = Subtract(pieces, previous.Hole, previous.Min, previous.Max, scratch);

                    if (pieces.Count == 0)
                        break;
                }

                foreach (Hole hole in _bridgeHoles)
                {
                    if (pieces.Count == 0)
                        break;

                    pieces = Subtract(pieces, hole.Points, hole.Min, hole.Max, scratch);
                }

                if (pieces.Count > 0)
                    scratch.Covering.Add(index);

                foreach (List<Vertex> piece in pieces)
                {
                    Emit(piece, segment, geometry, scratch);
                    scratch.Release(piece);
                }

                scratch.Release(pieces);
            }
        }

        return geometry;
    }

    private void Query(Vector2 min, Vector2 max, Scratch scratch)
    {
        scratch.Seen.Clear();
        scratch.Candidates.Clear();

        for (int z = Cell(min.y); z <= Cell(max.y); z++)
        {
            for (int x = Cell(min.x); x <= Cell(max.x); x++)
            {
                if (!_cells.TryGetValue((x, z), out List<int> items))
                    continue;

                foreach (int index in items)
                {
                    if (scratch.Seen.Add(index))
                        scratch.Candidates.Add(index);
                }
            }
        }

        scratch.Candidates.Sort();
    }

    private static bool Contains(Vector2[] polygon, Vector2 point)
    {
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 from = polygon[i];
            Vector2 to = polygon[(i + 1) % polygon.Length];

            if (Cross(to - from, point - from) < 0f)
                return false;
        }

        return true;
    }

    private static int Cell(float value) => Mathf.FloorToInt(value / CELL_SIZE);
}
