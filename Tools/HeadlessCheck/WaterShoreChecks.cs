using System;
using System.Collections.Generic;
using UnityEngine;

static class WaterShoreChecks
{
    private const float QUANTUM = 1000f;
    private const float FRAGMENT_AREA = 16f;
    private const float SATELLITE_AREA = 800f;
    private const float TINY_HOLE_AREA = 8f;
    private const float GRID_EPSILON = 1e-3f;
    private const float MOUTH_STEP = 0.5f;
    private const float SECTION_STEP = 0.5f;
    private const float SECTION_SPACING = 40f;
    private const float CLIFF_GRADE = 1f;
    private const float INDEX_CELL = 8f;
    private const float SUBMERGED = 0.15f;

    public sealed class Result
    {
        public int Bodies, SplitBodies, Fragments, TinyHoles, Islets;
        public float WorstMainShare = 1f;
        public double Boundary, GridBoundary;
        public int ShoreSamples;
        public float ShoreMedian, ShoreP95, ShoreHighP95;
        public int Hanging;
        public int Spikes;
        public int Mouths, MouthSamples, MouthGaps;
        public int Sections, WetCenters, FallingBanks, Cliffs;
        public int TJunctions, NonManifold, Triangles;
        public readonly List<(int Source, Vector2 At)> FragmentExamples = new();
        public readonly List<Vector2> MouthExamples = new();
        public readonly List<Vector2> HangingExamples = new();
        public readonly List<Vector2> JunctionExamples = new();

        public float GridShare => Boundary <= 0 ? 0f : (float)(GridBoundary / Boundary);
        public float SpikesPerKm => Boundary <= 0 ? 0f : (float)(Spikes / (Boundary / 1000.0));

        public string Describe()
        {
            return $"  связность: {Bodies} водоёмов, с лишними кусками {SplitBodies} (главная часть не меньше {WorstMainShare:P1}), обрывков меньше {FRAGMENT_AREA:0} м² {Fragments}\n"
                + $"  дырки меньше {TINY_HOLE_AREA:0} м² с землёй под водой {TinyHoles}, островки над водой {Islets}\n"
                + $"  берег {Boundary / 1000.0:0.0} км, по линиям сетки гидрологии {GridShare:P2}, узких шипов {Spikes} ({SpikesPerKm:0.00} на км)\n"
                + $"  точность берега: {ShoreSamples} проб, |земля − уровень − {WaterMap.MESH_UNDERLAP:0.0}| медиана {ShoreMedian:0.000} м, p95 {ShoreP95:0.000} м; меш над сухой землёй p95 {ShoreHighP95:0.000} м; край меша над водой {Hanging}\n"
                + $"  устья: {Mouths}, проб {MouthSamples}, сухих разрывов {MouthGaps}\n"
                + $"  сечения рек: {Sections}, центр под водой {WetCenters}, берег падает от воды {FallingBanks}, обрывов от врезки {Cliffs}\n"
                + $"  меш стоячей воды: {Triangles} треугольников, T-стыков {TJunctions}, рёбер больше чем у двух треугольников {NonManifold}"
                + (FragmentExamples.Count == 0 ? "" : "\n  обрывки: " + string.Join(" ", FragmentExamples.ConvertAll(f => $"{f.Source}@({f.At.x:0}, {f.At.y:0})")))
                + (MouthExamples.Count == 0 ? "" : "\n  разрывы в устьях: " + string.Join(" ", MouthExamples.ConvertAll(p => $"({p.x:0}, {p.y:0})")))
                + (HangingExamples.Count == 0 ? "" : "\n  край меша над водой: " + string.Join(" ", HangingExamples.ConvertAll(p => $"({p.x:0}, {p.y:0})")))
                + (JunctionExamples.Count == 0 ? "" : "\n  T-стыки и лишние рёбра: " + string.Join(" ", JunctionExamples.ConvertAll(p => $"({p.x:0.0}, {p.y:0.0})")));
        }
    }

    private readonly struct Tri
    {
        public readonly Vector3 A, B, C;
        public readonly int Source;

        public Tri(Vector3 a, Vector3 b, Vector3 c, int source)
        {
            A = a;
            B = b;
            C = c;
            Source = source;
        }
    }

    public static Result Measure(HeightMap carved, HeightMap raw, WaterMap water, float bankWidth)
    {
        var result = new Result();
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        var standing = new List<Tri>();
        var all = new List<Tri>();

        foreach (WaterMeshPart part in parts)
        {
            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                var tri = new Tri(part.Vertices[part.Triangles[t]], part.Vertices[part.Triangles[t + 1]], part.Vertices[part.Triangles[t + 2]], part.Sources[t / 3]);
                all.Add(tri);

                if (part.Kind != WaterKind.River)
                    standing.Add(tri);
            }
        }

        result.Triangles = standing.Count;

        Topology(carved, water, standing, result);
        Spikes(water, result);
        Mouths(carved, water, Index(all), result);
        Sections(carved, raw, water, bankWidth, result);

        return result;
    }

    public static bool SameMeshes(WaterMap first, WaterMap second)
    {
        List<WaterMeshPart> a = WaterMeshes.Build(first);
        List<WaterMeshPart> b = WaterMeshes.Build(second);

        if (a.Count != b.Count)
            return false;

        for (int p = 0; p < a.Count; p++)
        {
            if (a[p].Name != b[p].Name || a[p].Vertices.Count != b[p].Vertices.Count || a[p].Triangles.Count != b[p].Triangles.Count)
                return false;

            for (int v = 0; v < a[p].Vertices.Count; v++)
            {
                if (a[p].Vertices[v].x != b[p].Vertices[v].x || a[p].Vertices[v].y != b[p].Vertices[v].y || a[p].Vertices[v].z != b[p].Vertices[v].z)
                    return false;
            }

            for (int t = 0; t < a[p].Triangles.Count; t++)
            {
                if (a[p].Triangles[t] != b[p].Triangles[t])
                    return false;
            }
        }

        return true;
    }

    private static long Key(Vector3 v)
    {
        long x = (long)Math.Round(v.x * QUANTUM);
        long z = (long)Math.Round(v.z * QUANTUM);

        return (x << 32) ^ (z & 0xFFFFFFFFL);
    }

    private static void Topology(HeightMap map, WaterMap water, List<Tri> triangles, Result result)
    {
        var vertices = new Dictionary<(int, long), int>();
        var positions = new List<Vector3>();
        var ids = new int[triangles.Count * 3];

        for (int t = 0; t < triangles.Count; t++)
        {
            Tri tri = triangles[t];
            ids[t * 3] = Vertex(vertices, positions, tri.Source, tri.A);
            ids[t * 3 + 1] = Vertex(vertices, positions, tri.Source, tri.B);
            ids[t * 3 + 2] = Vertex(vertices, positions, tri.Source, tri.C);
        }

        var edges = new Dictionary<(int, int), (int Count, int First)>();

        for (int t = 0; t < triangles.Count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = ids[t * 3 + k], b = ids[t * 3 + (k + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b));

                edges[key] = edges.TryGetValue(key, out (int Count, int First) entry) ? (entry.Count + 1, entry.First) : (1, t);
            }
        }

        var parent = new int[triangles.Count];

        for (int t = 0; t < parent.Length; t++)
            parent[t] = t;

        var directed = new Dictionary<int, List<int>>();

        for (int t = 0; t < triangles.Count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = ids[t * 3 + k], b = ids[t * 3 + (k + 1) % 3];
                (int count, int first) = edges[(Math.Min(a, b), Math.Max(a, b))];

                if (count > 2 && result.JunctionExamples.Count < 8)
                    result.JunctionExamples.Add(new Vector2(positions[a].x, positions[a].z));

                if (count > 2)
                    result.NonManifold++;

                if (count >= 2)
                {
                    Union(parent, t, first);
                    continue;
                }

                if (!directed.TryGetValue(a, out List<int> list))
                    directed[a] = list = new List<int>();

                list.Add(b);
            }
        }

        result.NonManifold /= 3;

        Components(triangles, parent, result);
        Loops(map, water, positions, directed, Index(triangles), result);
        Junctions(triangles, positions, ids, result);
    }

    private static int Vertex(Dictionary<(int, long), int> vertices, List<Vector3> positions, int source, Vector3 v)
    {
        var key = (source, Key(v));

        if (vertices.TryGetValue(key, out int id))
            return id;

        id = positions.Count;
        positions.Add(v);
        vertices[key] = id;
        return id;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);

        if (ra != rb)
            parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }

    private static float Area(Tri tri)
    {
        return 0.5f * Mathf.Abs((tri.B.x - tri.A.x) * (tri.C.z - tri.A.z) - (tri.C.x - tri.A.x) * (tri.B.z - tri.A.z));
    }

    private static void Components(List<Tri> triangles, int[] parent, Result result)
    {
        var area = new Dictionary<int, double>();
        var source = new Dictionary<int, int>();

        for (int t = 0; t < triangles.Count; t++)
        {
            int root = Find(parent, t);
            area[root] = (area.TryGetValue(root, out double a) ? a : 0.0) + Area(triangles[t]);
            source[root] = triangles[t].Source;
        }

        var bodies = new Dictionary<int, List<double>>();

        foreach (KeyValuePair<int, double> pair in area)
        {
            int body = source[pair.Key];

            if (!bodies.TryGetValue(body, out List<double> list))
                bodies[body] = list = new List<double>();

            list.Add(pair.Value);

            if (pair.Value >= FRAGMENT_AREA)
                continue;

            result.Fragments++;

            if (result.FragmentExamples.Count < 8)
                result.FragmentExamples.Add((body, new Vector2(triangles[pair.Key].A.x, triangles[pair.Key].A.z)));
        }

        foreach (KeyValuePair<int, List<double>> pair in bodies)
        {
            if (pair.Key == -1)
                continue;

            result.Bodies++;

            double total = 0, largest = 0;

            foreach (double a in pair.Value)
            {
                total += a;
                largest = Math.Max(largest, a);
            }

            float share = (float)(largest / Math.Max(total, 1e-6));
            result.WorstMainShare = Mathf.Min(result.WorstMainShare, share);

            foreach (double a in pair.Value)
            {
                if (a < largest && a < SATELLITE_AREA)
                {
                    result.SplitBodies++;
                    break;
                }
            }
        }
    }

    private static void Loops(HeightMap map, WaterMap water, List<Vector3> positions, Dictionary<int, List<int>> directed, Dictionary<long, List<Tri>> index, Result result)
    {
        var used = new HashSet<(int, int)>();
        var shore = new List<float>();
        var high = new List<float>();
        float world = water.WorldSize;

        foreach (KeyValuePair<int, List<int>> start in directed)
        {
            foreach (int first in start.Value)
            {
                if (used.Contains((start.Key, first)))
                    continue;

                var loop = new List<int> { start.Key };
                int current = start.Key, next = first;

                while (used.Add((current, next)))
                {
                    loop.Add(next);
                    current = next;

                    if (!directed.TryGetValue(current, out List<int> outgoing))
                        break;

                    next = -1;

                    foreach (int candidate in outgoing)
                    {
                        if (!used.Contains((current, candidate)))
                        {
                            next = candidate;
                            break;
                        }
                    }

                    if (next < 0)
                        break;
                }

                Loop(map, water, positions, loop, world, shore, high, index, result);
            }
        }

        shore.Sort();
        high.Sort();
        result.ShoreSamples = shore.Count;
        result.ShoreMedian = shore.Count == 0 ? 0f : shore[shore.Count / 2];
        result.ShoreP95 = shore.Count == 0 ? 0f : shore[(int)(shore.Count * 0.95f)];
        result.ShoreHighP95 = high.Count == 0 ? 0f : high[(int)(high.Count * 0.95f)];
    }

    private static void Loop(HeightMap map, WaterMap water, List<Vector3> positions, List<int> loop, float world, List<float> shore, List<float> high, Dictionary<long, List<Tri>> index, Result result)
    {
        double signed = 0;
        Vector2 centroid = Vector2.zero;
        float surface = positions[loop[0]].y;

        for (int i = 0; i + 1 < loop.Count; i++)
        {
            Vector3 a = positions[loop[i]], b = positions[loop[i + 1]];
            signed += 0.5 * ((double)a.x * b.z - (double)b.x * a.z);
            centroid += new Vector2(a.x, a.z);

            float length = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
            bool border = OnBorder(a, b, world);

            if (border || Seam(index, a, b))
                continue;

            result.Boundary += length;

            if (OnGridLine(a, b, water.CellSize))
                result.GridBoundary += length;

            float x = 0.5f * (a.x + b.x), z = 0.5f * (a.z + b.z);

            if (water.TryRiver(x, z, out _))
                continue;

            float ground = map.SampleWorldSmooth(x, z);
            shore.Add(Mathf.Abs(ground - surface - WaterMap.MESH_UNDERLAP));
            high.Add(Mathf.Max(0f, ground - surface));

            if (ground >= surface - SUBMERGED)
                continue;

            result.Hanging++;

            if (result.HangingExamples.Count < 12 && result.HangingExamples.TrueForAll(p => (p - new Vector2(x, z)).sqrMagnitude > 250f * 250f))
                result.HangingExamples.Add(new Vector2(x, z));
        }

        centroid /= Mathf.Max(1, loop.Count - 1);

        if (signed <= 0 || Math.Abs(signed) >= TINY_HOLE_AREA)
            return;

        if (map.SampleWorldSmooth(centroid.x, centroid.y) < surface)
            result.TinyHoles++;
        else
            result.Islets++;
    }


    private static bool Seam(Dictionary<long, List<Tri>> index, Vector3 a, Vector3 b)
    {
        const float SIDE = 0.05f;

        float dx = b.x - a.x, dz = b.z - a.z;
        float length = Mathf.Sqrt(dx * dx + dz * dz);

        if (length < 1e-4f)
            return true;

        float nx = -dz / length * SIDE, nz = dx / length * SIDE;
        float mx = 0.5f * (a.x + b.x), mz = 0.5f * (a.z + b.z);

        return Level(index, mx + nx, mz + nz, a.y) && Level(index, mx - nx, mz - nz, a.y);
    }

    private static bool Level(Dictionary<long, List<Tri>> index, float x, float z, float y)
    {
        return Covered(index, x, z, out float top) && Mathf.Abs(top - y) < 1e-3f;
    }

    private static bool OnBorder(Vector3 a, Vector3 b, float world)
    {
        return Mathf.Abs(a.x - b.x) < GRID_EPSILON && (a.x < GRID_EPSILON || a.x > world - GRID_EPSILON)
            || Mathf.Abs(a.z - b.z) < GRID_EPSILON && (a.z < GRID_EPSILON || a.z > world - GRID_EPSILON);
    }

    private static bool OnGridLine(Vector3 a, Vector3 b, float cell)
    {
        if (Mathf.Abs(a.x - b.x) < GRID_EPSILON)
            return Mathf.Abs(a.x / cell - (float)Math.Round(a.x / cell)) * cell < GRID_EPSILON;

        if (Mathf.Abs(a.z - b.z) < GRID_EPSILON)
            return Mathf.Abs(a.z / cell - (float)Math.Round(a.z / cell)) * cell < GRID_EPSILON;

        return false;
    }

    private static void Junctions(List<Tri> triangles, List<Vector3> positions, int[] ids, Result result)
    {
        var grid = new Dictionary<long, List<int>>();

        for (int v = 0; v < positions.Count; v++)
        {
            long cell = Cell(positions[v].x, positions[v].z);

            if (!grid.TryGetValue(cell, out List<int> list))
                grid[cell] = list = new List<int>();

            list.Add(v);
        }

        var seen = new HashSet<(int, int)>();

        for (int t = 0; t < triangles.Count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = ids[t * 3 + k], b = ids[t * 3 + (k + 1) % 3];

                if (!seen.Add((Math.Min(a, b), Math.Max(a, b))))
                    continue;

                Vector3 pa = positions[a], pb = positions[b];
                int x0 = Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) / INDEX_CELL), x1 = Mathf.FloorToInt(Mathf.Max(pa.x, pb.x) / INDEX_CELL);
                int z0 = Mathf.FloorToInt(Mathf.Min(pa.z, pb.z) / INDEX_CELL), z1 = Mathf.FloorToInt(Mathf.Max(pa.z, pb.z) / INDEX_CELL);

                for (int cz = z0; cz <= z1; cz++)
                {
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        if (!grid.TryGetValue(((long)cx << 32) ^ (uint)cz, out List<int> near))
                            continue;

                        foreach (int v in near)
                        {
                            if (v == a || v == b || Mathf.Abs(positions[v].y - pa.y) > 1e-4f)
                                continue;

                            if (!Interior(pa, pb, positions[v]))
                                continue;

                            result.TJunctions++;

                            if (result.JunctionExamples.Count < 8)
                                result.JunctionExamples.Add(new Vector2(positions[v].x, positions[v].z));
                        }
                    }
                }
            }
        }
    }

    private static long Cell(float x, float z)
    {
        return ((long)Mathf.FloorToInt(x / INDEX_CELL) << 32) ^ (uint)Mathf.FloorToInt(z / INDEX_CELL);
    }

    private static bool Interior(Vector3 a, Vector3 b, Vector3 p)
    {
        float dx = b.x - a.x, dz = b.z - a.z;
        float length = dx * dx + dz * dz;

        if (length < 1e-8f)
            return false;

        float t = ((p.x - a.x) * dx + (p.z - a.z) * dz) / length;

        if (t <= 1e-4f || t >= 1f - 1e-4f)
            return false;

        float cross = (p.x - a.x) * dz - (p.z - a.z) * dx;

        return cross * cross / length < 1e-6f;
    }

    private static void Spikes(WaterMap water, Result result)
    {
        int nodes = water.NodeResolution;
        int sub = WaterMap.SUBDIVISION;
        var owner = new short[nodes * nodes];
        var wet = new bool[nodes * nodes];
        float step = water.NodeStep;

        for (int cell = 0; cell < water.Kinds.Length; cell++)
        {
            int start = water.DetailStart(cell);

            if (start < 0 && !water.IsFull(cell))
                continue;

            int i0 = cell % water.Resolution * sub, j0 = cell / water.Resolution * sub;

            for (int k = 0; k < WaterMap.CELL_NODES; k++)
            {
                int node = (j0 + k / WaterMap.CELL_SIDE_NODES) * nodes + i0 + k % WaterMap.CELL_SIDE_NODES;
                short o = start < 0 ? water.CellOwner(cell) : water.DetailOwner(start + k);
                bool w = start < 0 || water.DetailGround(start + k) < water.LevelOf(o);

                if (!w || wet[node])
                    continue;

                wet[node] = true;
                owner[node] = o;
            }
        }

        for (int j = 1; j + 1 < nodes; j++)
        {
            for (int i = 1; i + 1 < nodes; i++)
            {
                int node = j * nodes + i;

                if (!wet[node] || Same(wet, owner, nodes, node) != 1)
                    continue;

                int neighbour = Neighbour(wet, owner, nodes, node);

                if (neighbour >= 0 && Same(wet, owner, nodes, neighbour) <= 2 && Straight(wet, owner, nodes, neighbour))
                    result.Spikes++;
            }
        }
    }

    private static readonly int[] StepX = { 1, 0, -1, 0 };
    private static readonly int[] StepZ = { 0, 1, 0, -1 };

    private static int Same(bool[] wet, short[] owner, int nodes, int node)
    {
        int count = 0;

        for (int d = 0; d < 4; d++)
        {
            int next = node + StepZ[d] * nodes + StepX[d];

            if (next >= 0 && next < wet.Length && wet[next] && owner[next] == owner[node])
                count++;
        }

        return count;
    }

    private static int Neighbour(bool[] wet, short[] owner, int nodes, int node)
    {
        for (int d = 0; d < 4; d++)
        {
            int next = node + StepZ[d] * nodes + StepX[d];

            if (next >= 0 && next < wet.Length && wet[next] && owner[next] == owner[node])
                return next;
        }

        return -1;
    }

    private static bool Straight(bool[] wet, short[] owner, int nodes, int node)
    {
        bool east = Wet(wet, owner, node, node + 1), west = Wet(wet, owner, node, node - 1);
        bool north = Wet(wet, owner, node, node + nodes), south = Wet(wet, owner, node, node - nodes);

        return !(east || west) || !(north || south);
    }

    private static bool Wet(bool[] wet, short[] owner, int from, int node)
    {
        return node >= 0 && node < wet.Length && wet[node] && owner[node] == owner[from];
    }

    private static Dictionary<long, List<Tri>> Index(List<Tri> triangles)
    {
        var index = new Dictionary<long, List<Tri>>();

        foreach (Tri tri in triangles)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x)) / INDEX_CELL), x1 = Mathf.FloorToInt(Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x)) / INDEX_CELL);
            int z0 = Mathf.FloorToInt(Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z)) / INDEX_CELL), z1 = Mathf.FloorToInt(Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z)) / INDEX_CELL);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    long key = ((long)x << 32) ^ (uint)z;

                    if (!index.TryGetValue(key, out List<Tri> list))
                        index[key] = list = new List<Tri>();

                    list.Add(tri);
                }
            }
        }

        return index;
    }

    private static bool Covered(Dictionary<long, List<Tri>> index, float x, float z, out float top)
    {
        top = float.NegativeInfinity;

        if (!index.TryGetValue(Cell(x, z), out List<Tri> near))
            return false;

        foreach (Tri tri in near)
        {
            float d = (tri.B.z - tri.C.z) * (tri.A.x - tri.C.x) + (tri.C.x - tri.B.x) * (tri.A.z - tri.C.z);

            if (Mathf.Abs(d) < 1e-9f)
                continue;

            float u = ((tri.B.z - tri.C.z) * (x - tri.C.x) + (tri.C.x - tri.B.x) * (z - tri.C.z)) / d;
            float v = ((tri.C.z - tri.A.z) * (x - tri.C.x) + (tri.A.x - tri.C.x) * (z - tri.C.z)) / d;

            if (u < -1e-5f || v < -1e-5f || 1f - u - v < -1e-5f)
                continue;

            top = Mathf.Max(top, u * tri.A.y + v * tri.B.y + (1f - u - v) * tri.C.y);
        }

        return !float.IsNegativeInfinity(top);
    }

    private static void Mouths(HeightMap map, WaterMap water, Dictionary<long, List<Tri>> index, Result result)
    {
        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (!water.RiverOpen(points[i]) || water.RiverOpen(points[i + 1]) || !water.StandingAt(points[i + 1].Position.x, points[i + 1].Position.y, out float standing))
                    continue;

                result.Mouths++;

                int from = Mathf.Max(0, i - 2), to = Mathf.Min(points.Count - 1, i + 3);

                for (int k = from; k < to; k++)
                {
                    Vector2 a = points[k].Position, b = points[k + 1].Position;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / MOUTH_STEP));

                    for (int s = 0; s < steps; s++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                        float ground = map.SampleWorldSmooth(p.x, p.y);
                        float level = Mathf.Max(standing, Mathf.Lerp(points[k].Surface, points[k + 1].Surface, s / (float)steps));

                        if (ground > level - SUBMERGED)
                            continue;

                        result.MouthSamples++;

                        if (Covered(index, p.x, p.y, out float top) && top >= ground)
                            continue;

                        result.MouthGaps++;

                        if (result.MouthExamples.Count < 8)
                            result.MouthExamples.Add(p);
                    }
                }
            }
        }
    }

    private static void Sections(HeightMap carved, HeightMap raw, WaterMap water, float bankWidth, Result result)
    {
        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;
            float travelled = 0f, next = SECTION_SPACING;

            for (int i = 1; i + 1 < points.Count; i++)
            {
                travelled += Vector2.Distance(points[i - 1].Position, points[i].Position);

                if (travelled < next || !water.RiverOpen(points[i]) || !water.RiverOpen(points[i - 1]) || !water.RiverOpen(points[i + 1]))
                    continue;

                next = travelled + SECTION_SPACING;
                Section(carved, raw, water, points[i], (points[i + 1].Position - points[i - 1].Position).normalized, bankWidth, result);
            }
        }
    }

    private static void Section(HeightMap carved, HeightMap raw, WaterMap water, RiverPoint point, Vector2 tangent, float bankWidth, Result result)
    {
        var normal = new Vector2(-tangent.y, tangent.x);
        float half = 0.5f * point.Width;
        float bank = half + RiverCarver.BankWidth(half, bankWidth, 0f);

        if (water.StandingAt(point.Position.x + normal.x * bank, point.Position.y + normal.y * bank, out _)
            || water.StandingAt(point.Position.x - normal.x * bank, point.Position.y - normal.y * bank, out _))
            return;

        result.Sections++;

        if (carved.SampleWorldSmooth(point.Position.x, point.Position.y) < point.Surface)
            result.WetCenters++;

        foreach (float side in new[] { 1f, -1f })
        {
            Vector2 direction = normal * side;
            Vector2 edge = point.Position + direction * half;
            Vector2 outer = point.Position + direction * bank;

            if (carved.SampleWorldSmooth(outer.x, outer.y) < carved.SampleWorldSmooth(edge.x, edge.y) - 0.1f
                && raw.SampleWorldSmooth(outer.x, outer.y) >= raw.SampleWorldSmooth(edge.x, edge.y) - 0.1f)
                result.FallingBanks++;

            for (float d = half; d + SECTION_STEP <= bank; d += SECTION_STEP)
            {
                Vector2 a = point.Position + direction * d, b = point.Position + direction * (d + SECTION_STEP);
                float carvedGrade = Mathf.Abs(carved.SampleWorldSmooth(b.x, b.y) - carved.SampleWorldSmooth(a.x, a.y)) / SECTION_STEP;
                float rawGrade = Mathf.Abs(raw.SampleWorldSmooth(b.x, b.y) - raw.SampleWorldSmooth(a.x, a.y)) / SECTION_STEP;

                if (carvedGrade <= Mathf.Max(CLIFF_GRADE, rawGrade * 1.25f + 0.1f))
                    continue;

                result.Cliffs++;
                break;
            }
        }
    }
}
