using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

static class VoxelSeamChecks
{
    private const int WORLD = 4096;
    private const float MAX_HEIGHT = 384f;
    private const float WELD = 1e-3f;
    private const float SKIRT_TOLERANCE = 0.02f;
    private const float WALL_SLOPE = 0.1f;
    private const float WALL_HEIGHT = 2f;

    private static int _failures;

    public static void Run(string[] args)
    {
        var clock = Stopwatch.StartNew();

        int real = Array.IndexOf(args, "--real");

        if (real >= 0)
        {
            CheckReal(float.Parse(args[real + 1], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(args[real + 2], System.Globalization.CultureInfo.InvariantCulture));
            Finish(clock);
            return;
        }

        HeightMap map = Terrain(1);

        CheckPlan();
        CheckPairs(map);
        CheckCorners(map);
        CheckRings();
        CheckTiming(map);

        Finish(clock);
    }

    private static void Finish(Stopwatch clock)
    {
        Console.WriteLine($"voxel seams: {clock.Elapsed.TotalSeconds:0.0} с");

        if (_failures > 0)
        {
            Console.WriteLine($"ПРОВАЛ: {_failures} проверок стыков не прошли");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("Стыки вокселей в порядке: ни щелей, ни T-стыков, ни нахлёстов, ни дыр; юбка одна, со стороны мелкого LOD, и не видна.");
    }

    private static void CheckReal(float viewerX, float viewerZ)
    {
        const string CONTENT = "../../Assets/_Dustborn/Content/World";

        var config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");
        byte[] bytes = System.IO.File.ReadAllBytes("../../Assets/_Dustborn/Generated/HeightMap.bytes");

        HeightMap map = HeightMap.FromRaw16(bytes, config.HeightMapResolution, config.WorldSize, config.MaxHeight);

        var voxels = new VoxelConfig();
        var viewer = new Vector2(viewerX, viewerZ);
        var plan = new VoxelStreamPlan(voxels, 6, 96f, config.WorldSize);

        using var field = Field(map, voxels);
        var envelope = new Envelope(map);

        var layout = new Layout
        {
            Name = $"настоящий мир {config.WorldSize} м, зритель ({viewer.x:0}, {viewer.y:0})",
            Bounds = new Rect(0f, 0f, config.WorldSize, config.WorldSize),
            LodAt = point => plan.LodAt(viewer, point)
        };

        plan.Around(viewer, layout.Keys);

        Expect(Measure(layout, voxels, field, envelope), layout.Name);
    }

    private sealed class Layout
    {
        public readonly List<VoxelColumnKey> Keys = new List<VoxelColumnKey>();
        public Func<Vector2, int> LodAt;
        public Rect Bounds;
        public string Name;
    }

    private sealed class Tri
    {
        public Vector3 A, B, C;
        public int Column;
        public int Chunk;
        public bool Skirt;
    }

    private sealed class Segment
    {
        public Vector3 A, B;
        public int Column;
        public Tri Owner;
    }

    private sealed class Grid<T>
    {
        private readonly float _cell;
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>();

        public Grid(float cell)
        {
            _cell = cell;
        }

        public void Add(T item, float minX, float minZ, float maxX, float maxZ)
        {
            int x0 = Mathf.FloorToInt(minX / _cell), x1 = Mathf.FloorToInt(maxX / _cell);
            int z0 = Mathf.FloorToInt(minZ / _cell), z1 = Mathf.FloorToInt(maxZ / _cell);

            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    long key = Key(x, z);

                    if (!_cells.TryGetValue(key, out List<T> list))
                        _cells[key] = list = new List<T>();

                    list.Add(item);
                }
            }
        }

        public List<T> At(float x, float z)
        {
            return _cells.TryGetValue(Key(Mathf.FloorToInt(x / _cell), Mathf.FloorToInt(z / _cell)), out List<T> list)
                ? list
                : null;
        }

        private static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }

    private sealed class Envelope
    {
        private readonly List<float[]> _low = new List<float[]>();
        private readonly List<float[]> _high = new List<float[]>();
        private readonly List<int> _side = new List<int>();

        public Envelope(HeightMap map)
        {
            int nodes = map.Resolution;
            int side = (nodes - 1 + 1) / 2;

            var low = new float[side * side];
            var high = new float[side * side];

            for (int bz = 0; bz < side; bz++)
            {
                for (int bx = 0; bx < side; bx++)
                {
                    float min = float.MaxValue, max = float.MinValue;

                    for (int z = bz * 2; z <= Math.Min(bz * 2 + 2, nodes - 1); z++)
                    {
                        for (int x = bx * 2; x <= Math.Min(bx * 2 + 2, nodes - 1); x++)
                        {
                            float h = map.Heights[z * nodes + x] * map.MaxHeight;

                            min = Math.Min(min, h);
                            max = Math.Max(max, h);
                        }
                    }

                    low[bz * side + bx] = min;
                    high[bz * side + bx] = max;
                }
            }

            _low.Add(low);
            _high.Add(high);
            _side.Add(side);

            while (side > 1)
            {
                int next = (side + 1) / 2;

                var lower = new float[next * next];
                var higher = new float[next * next];

                for (int bz = 0; bz < next; bz++)
                {
                    for (int bx = 0; bx < next; bx++)
                    {
                        float min = float.MaxValue, max = float.MinValue;

                        for (int z = bz * 2; z <= Math.Min(bz * 2 + 1, side - 1); z++)
                        {
                            for (int x = bx * 2; x <= Math.Min(bx * 2 + 1, side - 1); x++)
                            {
                                min = Math.Min(min, low[z * side + x]);
                                max = Math.Max(max, high[z * side + x]);
                            }
                        }

                        lower[bz * next + bx] = min;
                        higher[bz * next + bx] = max;
                    }
                }

                low = lower;
                high = higher;
                side = next;

                _low.Add(low);
                _high.Add(high);
                _side.Add(side);
            }
        }

        public void Range(float x0, float z0, float x1, float z1, out float low, out float high)
        {
            int level = 0;

            while (level + 1 < _side.Count && (2 << level) * 2 < Math.Max(x1 - x0, z1 - z0))
                level++;

            int block = 2 << level;
            int side = _side[level];

            int minX = Math.Clamp((int)Math.Floor(x0 / block), 0, side - 1), maxX = Math.Clamp((int)Math.Floor(x1 / block), 0, side - 1);
            int minZ = Math.Clamp((int)Math.Floor(z0 / block), 0, side - 1), maxZ = Math.Clamp((int)Math.Floor(z1 / block), 0, side - 1);

            low = float.MaxValue;
            high = float.MinValue;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    low = Math.Min(low, _low[level][z * side + x]);
                    high = Math.Max(high, _high[level][z * side + x]);
                }
            }
        }
    }

    private sealed class Scene
    {
        public Layout Layout;
        public VoxelConfig Voxels;
        public VoxelDensityField Field;
        public Envelope Envelope;
        public readonly List<Tri> Triangles = new List<Tri>();
        public readonly Grid<Tri> Fine = new Grid<Tri>(4f);
        public readonly Grid<Tri> Coarse = new Grid<Tri>(64f);
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public int Chunks;

        public float Voxel(int column)
        {
            return Voxels.VoxelSize * (1 << Layout.Keys[column].Lod);
        }

        public Rect Area(int column)
        {
            VoxelColumnKey key = Layout.Keys[column];
            float size = Voxels.ChunkMetres * (1 << key.Lod);

            return new Rect(key.X * size, key.Z * size, size, size);
        }
    }

    private sealed class Report
    {
        public int NonFinite;
        public int Stray;
        public float StrayWorst;
        public int LongEdges;
        public int Walls;
        public float WallWorst;
        public Vector3 WallAt;
        public string WallKey;
        public int OpenEdges;
        public float OpenWorst;
        public Vector3 OpenAt;
        public string OpenKey;
        public int Holes;
        public Vector2 HoleAt;
        public int Overlaps;
        public float OverlapWorst;
        public int Spikes;
        public float SpikeWorst;
        public int SkirtTriangles;
        public int SkirtExposed;
        public float SkirtExposedWorst;
        public Vector3 SkirtExposedAt;
        public string SkirtExposedKey;
        public int SkirtWrongOwner;
        public int SkirtDuplicates;
        public float SkirtTallest;
        public int Probes;

        public bool Clean => NonFinite == 0 && Stray == 0 && LongEdges == 0 && Walls == 0 && OpenEdges == 0 && Holes == 0
            && Overlaps == 0 && Spikes == 0 && SkirtExposed == 0 && SkirtWrongOwner == 0 && SkirtDuplicates == 0;

        public string Describe()
        {
            string text = $"щелей {OpenEdges} (до {OpenWorst:0.000} м)" + (OpenEdges > 0 ? $" у {Format(OpenAt)} в {OpenKey}" : "")
                + $", дыр {Holes}" + (Holes > 0 ? $" у ({HoleAt.x:0.0}, {HoleAt.y:0.0})" : "")
                + $", нахлёстов {Overlaps} (до {OverlapWorst:0.00} м), шипов {Spikes} (до {SpikeWorst:0.00} м)"
                + $", стенок {Walls}" + (Walls > 0 ? $" (до {WallWorst:0.0} м у {Format(WallAt)} в {WallKey})" : "")
                + $", вершин вне колонки {Stray} (до {StrayWorst:0.00} м), длинных рёбер {LongEdges}, NaN {NonFinite}"
                + $"; юбка: треугольников {SkirtTriangles}, высота до {SkirtTallest:0.0} м, видно {SkirtExposed}"
                + (SkirtExposed > 0 ? $" (стенка до {SkirtExposedWorst:0.0} м у {Format(SkirtExposedAt)} в {SkirtExposedKey})" : "")
                + $", на грубой стороне {SkirtWrongOwner}, дублей {SkirtDuplicates}; проб {Probes}";

            return text;
        }
    }

    private static string Describe(VoxelColumnKey key)
    {
        return $"колонке ({key.X}, {key.Z}) lod {key.Lod} seams {Faces(key.Seams)} morph {Faces(key.Morph)}";
    }

    private static string Faces(int bits)
    {
        string[] names = { "MIN_X", "MAX_X", "MIN_Z", "MAX_Z" };
        var parts = new List<string>();

        for (int i = 0; i < 4; i++)
        {
            if ((bits & (1 << i)) != 0)
                parts.Add(names[i]);
        }

        return parts.Count == 0 ? "-" : string.Join("|", parts);
    }

    private static string Format(Vector3 point)
    {
        return $"({point.x:0.0}, {point.y:0.0}, {point.z:0.0})";
    }

    private static void CheckPairs(HeightMap map)
    {
        Console.WriteLine("пары LOD (A–H): мелкий блок 2x2 против одной грубой колонки, все четыре стороны:");

        var voxels = new VoxelConfig();

        using var field = Field(map, voxels);
        var envelope = new Envelope(map);

        for (int fine = 0; fine < 5; fine++)
        {
            foreach (int side in new[] { 0, 1, 2, 3 })
            {
                Layout layout = Pair(voxels, fine, side, 3);
                Expect(Measure(layout, voxels, field, envelope), layout.Name);
            }
        }

        for (int lod = 0; lod < 2; lod++)
        {
            foreach (bool alongX in new[] { true, false })
            {
                Layout layout = Same(voxels, lod, alongX, 5);
                Expect(Measure(layout, voxels, field, envelope), layout.Name);
            }
        }
    }

    private static void CheckCorners(HeightMap map)
    {
        Console.WriteLine("углы (I): мелкий квадрант среди трёх грубых, все четыре поворота:");

        var voxels = new VoxelConfig();

        using var field = Field(map, voxels);
        var envelope = new Envelope(map);

        for (int fine = 0; fine < 5; fine++)
        {
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                Layout layout = Corner(voxels, fine, quadrant, 2);
                Expect(Measure(layout, voxels, field, envelope), layout.Name);
            }
        }
    }

    private static void CheckPlan()
    {
        int layouts = 0, columns = 0, mismatched = 0, skipped = 0;
        var random = new System.Random(7);

        foreach (int world in new[] { 4096, 8192 })
        {
            var voxels = new VoxelConfig();
            var plan = new VoxelStreamPlan(voxels, 6, 96f, world);
            var keys = new List<VoxelColumnKey>();

            for (int i = 0; i < 200; i++)
            {
                var viewer = new Vector2((float)random.NextDouble() * world, (float)random.NextDouble() * world);

                plan.Around(viewer, keys);
                layouts++;

                var morphs = new Dictionary<(int, int, int), int>();

                foreach (VoxelColumnKey key in keys)
                    morphs[(key.X, key.Z, key.Lod)] = key.Morph;

                foreach (VoxelColumnKey key in keys)
                {
                    columns++;

                    const int X_BITS = VoxelColumnKey.MORPH_MIN_X | VoxelColumnKey.MORPH_MAX_X;
                    const int Z_BITS = VoxelColumnKey.MORPH_MIN_Z | VoxelColumnKey.MORPH_MAX_Z;

                    if (morphs.TryGetValue((key.X + 1, key.Z, key.Lod), out int east) && (east & Z_BITS) != (key.Morph & Z_BITS))
                        mismatched++;

                    if (morphs.TryGetValue((key.X, key.Z + 1, key.Lod), out int north) && (north & X_BITS) != (key.Morph & X_BITS))
                        mismatched++;

                    float size = plan.ChunkMetres(key.Lod);

                    foreach (Vector2 point in new[]
                    {
                        new Vector2(key.X * size - 0.25f, (key.Z + 0.5f) * size),
                        new Vector2((key.X + 1) * size + 0.25f, (key.Z + 0.5f) * size),
                        new Vector2((key.X + 0.5f) * size, key.Z * size - 0.25f),
                        new Vector2((key.X + 0.5f) * size, (key.Z + 1) * size + 0.25f)
                    })
                    {
                        int level = plan.LodAt(viewer, point);

                        if (level >= 0 && Math.Abs(level - key.Lod) > 1)
                            skipped++;
                    }
                }
            }
        }

        bool clean = mismatched == 0 && skipped == 0;

        Console.WriteLine($"план колец: {layouts} раскладок, {columns} колонок; соседей одного LOD с разным морфом поперёк общей грани {mismatched}, "
            + $"соседей через уровень {skipped} — {(clean ? "ok" : "ПРОВАЛ")}");

        if (!clean)
            _failures++;
    }

    private static void CheckRings()
    {
        Console.WriteLine("кольца целиком (J): шесть LOD на случайном рельефе, несколько зрителей:");

        var voxels = new VoxelConfig();

        var viewers = new[]
        {
            new Vector2(2048f + 13.7f, 1792f + 5.3f),
            new Vector2(700.3f, 3300.9f),
            new Vector2(3500.1f, 611.4f)
        };

        for (int seed = 2; seed < 5; seed++)
        {
            HeightMap map = Terrain(seed);

            using var field = Field(map, voxels);
            var envelope = new Envelope(map);

            Vector2 viewer = viewers[seed - 2];
            var plan = new VoxelStreamPlan(voxels, 6, 96f, WORLD);

            var layout = new Layout
            {
                Name = $"кольца seed {seed}, зритель ({viewer.x:0}, {viewer.y:0})",
                Bounds = new Rect(0f, 0f, WORLD, WORLD),
                LodAt = point => plan.LodAt(viewer, point)
            };

            plan.Around(viewer, layout.Keys);

            Expect(Measure(layout, voxels, field, envelope), layout.Name);
        }
    }

    private static void CheckTiming(HeightMap map)
    {
        var voxels = new VoxelConfig();

        using var field = Field(map, voxels);
        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        const int ROUNDS = 7;
        const int SIDE = 8;

        var rounds = new List<double>();

        for (int round = 0; round < ROUNDS; round++)
        {
            var clock = Stopwatch.StartNew();

            for (int x = 20; x < 20 + SIDE; x++)
            {
                for (int z = 20; z < 20 + SIDE; z++)
                {
                    int y = Mathf.FloorToInt(field.Surface(x * 32f + 16f, z * 32f + 16f) / 32f);

                    mesher.Mesh(0, x, y, z, mesh, VoxelColumnKey.FACE_ALL, VoxelColumnKey.MORPH_MIN_X | VoxelColumnKey.MORPH_MAX_Z);
                }
            }

            rounds.Add(clock.Elapsed.TotalMilliseconds / (SIDE * SIDE));
        }

        rounds.Sort();

        Console.WriteLine($"  время меширования (C# без Burst, чанк lod 0 со всеми гранями шва и морфом): лучшее {rounds[0]:0.00} мс, медиана {rounds[ROUNDS / 2]:0.00} мс на чанк");
    }

    private static void Expect(Report report, string name)
    {
        string verdict = report.Clean ? "ok" : "ПРОВАЛ";

        Console.WriteLine($"  {name}: {verdict} — {report.Describe()}");

        if (!report.Clean)
            _failures++;
    }

    private static Layout Pair(VoxelConfig voxels, int fine, int side, int at)
    {
        int coarse = fine + 1;

        var columns = new List<(int X, int Z, int Lod)>();

        int fineX = side == 0 ? 0 : side == 1 ? 1 : 0;
        int fineZ = side == 2 ? 0 : side == 3 ? 1 : 0;

        int coarseX = side == 0 ? 1 : side == 1 ? 0 : 0;
        int coarseZ = side == 2 ? 1 : side == 3 ? 0 : 0;

        int wideX = side < 2 ? 2 : 1;
        int wideZ = side < 2 ? 1 : 2;

        int baseX = at, baseZ = at;

        for (int z = 0; z < 2; z++)
        {
            for (int x = 0; x < 2; x++)
                columns.Add(((baseX + fineX) * 2 + x, (baseZ + fineZ) * 2 + z, fine));
        }

        columns.Add((baseX + coarseX, baseZ + coarseZ, coarse));

        string[] names = { "мелкий слева, грубый на +X", "грубый слева, мелкий на +X", "мелкий снизу, грубый на +Z", "грубый снизу, мелкий на +Z" };

        float size = voxels.ChunkMetres * (1 << coarse);

        return Build(voxels, columns, $"lod {fine}/{coarse} {names[side]}",
            new Rect(baseX * size, baseZ * size, wideX * size, wideZ * size));
    }

    private static Layout Same(VoxelConfig voxels, int lod, bool alongX, int at)
    {
        var columns = new List<(int X, int Z, int Lod)>
        {
            (at, at, lod),
            (alongX ? at + 1 : at, alongX ? at : at + 1, lod)
        };

        float size = voxels.ChunkMetres * (1 << lod);

        return Build(voxels, columns, $"lod {lod}/{lod} соседи по {(alongX ? "X" : "Z")}",
            new Rect(at * size, at * size, (alongX ? 2 : 1) * size, (alongX ? 1 : 2) * size));
    }

    private static Layout Corner(VoxelConfig voxels, int fine, int quadrant, int at)
    {
        int coarse = fine + 1;

        var columns = new List<(int X, int Z, int Lod)>();

        for (int q = 0; q < 4; q++)
        {
            int qx = at + (q & 1);
            int qz = at + (q >> 1);

            bool isFine = q == quadrant;

            if (!isFine)
            {
                columns.Add((qx, qz, coarse));
                continue;
            }

            for (int z = 0; z < 2; z++)
            {
                for (int x = 0; x < 2; x++)
                    columns.Add((qx * 2 + x, qz * 2 + z, fine));
            }
        }

        float size = voxels.ChunkMetres * (1 << coarse);

        return Build(voxels, columns, $"угол lod {fine}/{coarse}, мелкий квадрант {quadrant}",
            new Rect(at * size, at * size, 2 * size, 2 * size));
    }

    private static Layout Build(VoxelConfig voxels, List<(int X, int Z, int Lod)> columns, string name, Rect bounds)
    {
        var layout = new Layout { Name = name, Bounds = bounds };

        layout.LodAt = point =>
        {
            foreach ((int x, int z, int lod) in columns)
            {
                float size = voxels.ChunkMetres * (1 << lod);

                if (point.x >= x * size && point.x < (x + 1) * size && point.y >= z * size && point.y < (z + 1) * size)
                    return lod;
            }

            return -1;
        };

        foreach ((int x, int z, int lod) in columns)
        {
            float size = voxels.ChunkMetres * (1 << lod);

            int seams = 0, morph = 0;

            Face(layout, new Vector2((x - 0.5f) * size, (z + 0.5f) * size), lod, VoxelColumnKey.FACE_MIN_X, VoxelColumnKey.MORPH_MIN_X, ref seams, ref morph);
            Face(layout, new Vector2((x + 1.5f) * size, (z + 0.5f) * size), lod, VoxelColumnKey.FACE_MAX_X, VoxelColumnKey.MORPH_MAX_X, ref seams, ref morph);
            Face(layout, new Vector2((x + 0.5f) * size, (z - 0.5f) * size), lod, VoxelColumnKey.FACE_MIN_Z, VoxelColumnKey.MORPH_MIN_Z, ref seams, ref morph);
            Face(layout, new Vector2((x + 0.5f) * size, (z + 1.5f) * size), lod, VoxelColumnKey.FACE_MAX_Z, VoxelColumnKey.MORPH_MAX_Z, ref seams, ref morph);

            layout.Keys.Add(new VoxelColumnKey(x, z, lod, seams, morph));
        }

        return layout;
    }

    private static void Face(Layout layout, Vector2 point, int lod, int face, int coarser, ref int seams, ref int morph)
    {
        int level = layout.LodAt(point);

        if (level < 0 || level == lod)
            return;

        seams |= face;

        if (level > lod)
            morph |= coarser;
    }

    private static Report Measure(Layout layout, VoxelConfig voxels, VoxelDensityField field, Envelope envelope)
    {
        Scene scene = Mesh(layout, voxels, field);
        scene.Envelope = envelope;
        var report = new Report();

        CheckVertices(scene, report);
        CheckOpenEdges(scene, report);
        CheckCoverage(scene, report);
        CheckSkirts(scene, report);

        return report;
    }

    private static Scene Mesh(Layout layout, VoxelConfig voxels, VoxelDensityField field)
    {
        var scene = new Scene { Layout = layout, Voxels = voxels, Field = field };

        float depth = voxels.SkirtDepth;

        using var mesh = new VoxelMesh();
        using var bare = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        Set(voxels, "SkirtDepth", 0f);

        using var bareMesher = new VoxelChunkMesher(voxels, field);

        Set(voxels, "SkirtDepth", depth);


        for (int column = 0; column < layout.Keys.Count; column++)
        {
            VoxelColumnKey key = layout.Keys[column];
            float size = voxels.ChunkMetres * (1 << key.Lod);
            float step = voxels.VoxelSize * (1 << key.Lod);

            float low = float.MaxValue, high = float.MinValue;

            for (float z = key.Z * size; z <= (key.Z + 1) * size; z += step)
            {
                for (float x = key.X * size; x <= (key.X + 1) * size; x += step)
                {
                    low = Mathf.Min(low, field.Surface(x, z));
                    high = Mathf.Max(high, field.Surface(x, z));
                }
            }

            for (int y = Mathf.FloorToInt(low / size) - 1; y <= Mathf.FloorToInt(high / size) + 1; y++)
            {
                mesher.Mesh(key.Lod, key.X, y, key.Z, mesh, key.Seams, key.Morph);

                bareMesher.Mesh(key.Lod, key.X, y, key.Z, bare, key.Seams, key.Morph);

                if (mesh.IsEmpty)
                    continue;

                scene.Chunks++;

                int offset = scene.Vertices.Count;

                for (int i = 0; i < mesh.Vertices.Length; i++)
                {
                    scene.Vertices.Add(mesh.Vertices[i]);
                    scene.Normals.Add(mesh.Normals[i]);
                }

                int surface = bare.TriangleCount;

                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    var tri = new Tri
                    {
                        A = mesh.Vertices[mesh.Triangles[t * 3]],
                        B = mesh.Vertices[mesh.Triangles[t * 3 + 1]],
                        C = mesh.Vertices[mesh.Triangles[t * 3 + 2]],
                        Column = column,
                        Chunk = scene.Chunks,
                        Skirt = t >= surface
                    };

                    scene.Triangles.Add(tri);

                    if (tri.Skirt)
                        continue;

                    float minX = Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x));
                    float maxX = Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x));
                    float minZ = Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z));
                    float maxZ = Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z));

                    if (!Finite(tri.A) || !Finite(tri.B) || !Finite(tri.C))
                        continue;

                    (key.Lod <= 2 ? scene.Fine : scene.Coarse).Add(tri, minX, minZ, maxX, maxZ);
                }
            }
        }


        return scene;
    }

    private static void CheckVertices(Scene scene, Report report)
    {
        foreach (Vector3 vertex in scene.Vertices)
        {
            if (!Finite(vertex))
                report.NonFinite++;
        }

        foreach (Vector3 normal in scene.Normals)
        {
            if (!Finite(normal))
                report.NonFinite++;
        }

        foreach (Tri tri in scene.Triangles)
        {
            if (tri.Skirt)
                continue;

            Rect area = scene.Area(tri.Column);
            float voxel = scene.Voxel(tri.Column);

            foreach (Vector3 point in new[] { tri.A, tri.B, tri.C })
            {
                float outside = Mathf.Max(
                    Mathf.Max(area.xMin - point.x, point.x - area.xMax),
                    Mathf.Max(area.yMin - point.z, point.z - area.yMax));

                if (outside <= WELD)
                    continue;

                report.Stray++;
                report.StrayWorst = Mathf.Max(report.StrayWorst, outside);
            }

            float limit = voxel * 3f;

            if (Flat(tri.A - tri.B) > limit || Flat(tri.B - tri.C) > limit || Flat(tri.C - tri.A) > limit)
                report.LongEdges++;

            Vector3 across = Vector3.Cross(tri.B - tri.A, tri.C - tri.A);
            float tall = Mathf.Max(tri.A.y, Mathf.Max(tri.B.y, tri.C.y)) - Mathf.Min(tri.A.y, Mathf.Min(tri.B.y, tri.C.y));

            if (Mathf.Abs(across.y) > WALL_SLOPE * Mathf.Sqrt(across.x * across.x + across.z * across.z) || tall <= WALL_HEIGHT)
                continue;

            report.Walls++;

            if (tall > report.WallWorst)
            {
                report.WallWorst = tall;
                report.WallAt = (tri.A + tri.B + tri.C) / 3f;
                report.WallKey = Describe(scene.Layout.Keys[tri.Column]);
            }
        }
    }

    private static void CheckOpenEdges(Scene scene, Report report)
    {
        var edges = new Dictionary<(int, long, long), int>();

        foreach (Tri tri in scene.Triangles)
        {
            if (tri.Skirt)
                continue;

            Count(edges, tri.Column, tri.A, tri.B);
            Count(edges, tri.Column, tri.B, tri.C);
            Count(edges, tri.Column, tri.C, tri.A);
        }

        var open = new List<Segment>();

        foreach (Tri tri in scene.Triangles)
        {
            if (tri.Skirt)
                continue;

            Collect(edges, open, tri, tri.A, tri.B);
            Collect(edges, open, tri, tri.B, tri.C);
            Collect(edges, open, tri, tri.C, tri.A);
        }

        Rect bounds = scene.Layout.Bounds;

        foreach (Segment segment in open)
        {
            float margin = scene.Voxel(segment.Column) * 1.01f;

            for (int i = 1; i <= 3; i++)
            {
                Vector3 point = Vector3.Lerp(segment.A, segment.B, i / 4f);

                if (point.x < bounds.xMin + margin || point.x > bounds.xMax - margin
                    || point.z < bounds.yMin + margin || point.z > bounds.yMax - margin)
                    continue;

                float nearest = Gap(scene, point, segment.Owner);

                if (nearest <= Weld(scene.Voxel(segment.Column)))
                    continue;

                report.OpenEdges++;

                if (nearest > report.OpenWorst)
                {
                    report.OpenWorst = nearest;
                    report.OpenAt = point;
                    report.OpenKey = Describe(scene.Layout.Keys[segment.Column]);
                }

                break;
            }
        }
    }

    private static void Count(Dictionary<(int, long, long), int> edges, int column, Vector3 a, Vector3 b)
    {
        long first = Quantize(a), second = Quantize(b);

        if (first == second)
            return;

        var key = (column, Math.Min(first, second), Math.Max(first, second));

        edges.TryGetValue(key, out int count);
        edges[key] = count + 1;
    }

    private static void Collect(Dictionary<(int, long, long), int> edges, List<Segment> open,
        Tri owner, Vector3 a, Vector3 b)
    {
        long first = Quantize(a), second = Quantize(b);

        if (first == second)
            return;

        var key = (owner.Column, Math.Min(first, second), Math.Max(first, second));

        if (edges[key] != 1)
            return;

        var segment = new Segment { A = a, B = b, Column = owner.Column, Owner = owner };

        open.Add(segment);
    }

    private static float Gap(Scene scene, Vector3 point, Tri owner)
    {
        float best = float.MaxValue;

        foreach (Grid<Tri> grid in new[] { scene.Fine, scene.Coarse })
        {
            List<Tri> near = grid.At(point.x, point.z);

            if (near == null)
                continue;

            foreach (Tri tri in near)
            {
                if (tri == owner)
                    continue;

                best = Mathf.Min(best, (float)Separation(point, tri.A, tri.B, tri.C));
            }
        }

        return best;
    }

    private static double Separation(Vector3 point, Vector3 first, Vector3 second, Vector3 third)
    {


        (double X, double Y, double Z) a = (first.x, first.y, first.z);
        (double X, double Y, double Z) b = (second.x, second.y, second.z);
        (double X, double Y, double Z) c = (third.x, third.y, third.z);
        (double X, double Y, double Z) q = (point.x, point.y, point.z);

        (double X, double Y, double Z) Sub((double X, double Y, double Z) l, (double X, double Y, double Z) r) => (l.X - r.X, l.Y - r.Y, l.Z - r.Z);
        (double X, double Y, double Z) Add((double X, double Y, double Z) l, (double X, double Y, double Z) r, double k) => (l.X + r.X * k, l.Y + r.Y * k, l.Z + r.Z * k);
        double Dot((double X, double Y, double Z) l, (double X, double Y, double Z) r) => l.X * r.X + l.Y * r.Y + l.Z * r.Z;
        double Length((double X, double Y, double Z) v) => Math.Sqrt(Dot(v, v));

        var ab = Sub(b, a);
        var ac = Sub(c, a);
        var ap = Sub(q, a);

        double d1 = Dot(ab, ap), d2 = Dot(ac, ap);

        if (d1 <= 0 && d2 <= 0)
            return Length(ap);

        var bp = Sub(q, b);
        double d3 = Dot(ab, bp), d4 = Dot(ac, bp);

        if (d3 >= 0 && d4 <= d3)
            return Length(bp);

        double vc = d1 * d4 - d3 * d2;

        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return Length(Sub(q, Add(a, ab, d1 / (d1 - d3))));

        var cp = Sub(q, c);
        double d5 = Dot(ab, cp), d6 = Dot(ac, cp);

        if (d6 >= 0 && d5 <= d6)
            return Length(cp);

        double vb = d5 * d2 - d1 * d6;

        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return Length(Sub(q, Add(a, ac, d2 / (d2 - d6))));

        double va = d3 * d6 - d5 * d4;

        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return Length(Sub(q, Add(b, Sub(c, b), (d4 - d3) / (d4 - d3 + (d5 - d6)))));

        double denominator = 1.0 / (va + vb + vc);

        return Length(Sub(q, Add(Add(a, ab, vb * denominator), ac, vc * denominator)));
    }

    private static float Weld(float voxel)
    {
        return Math.Max(WELD, voxel * 2e-4f);
    }

    private static long Quantize(Vector3 point)
    {
        long x = (long)Math.Round(point.x * 256.0) & 0x1FFFFF;
        long y = (long)Math.Round(point.y * 256.0) & 0x1FFFFF;
        long z = (long)Math.Round(point.z * 256.0) & 0x1FFFFF;

        return (x << 42) | (y << 21) | z;
    }

    private static void CheckCoverage(Scene scene, Report report)
    {
        Layout layout = scene.Layout;
        Rect bounds = layout.Bounds;

        for (int column = 0; column < layout.Keys.Count; column++)
        {
            VoxelColumnKey key = layout.Keys[column];

            if (key.Seams == 0)
                continue;

            Rect area = scene.Area(column);
            float fine = scene.Voxel(column) * (key.Morph != 0 ? 1f : 0.5f);
            float wide = scene.Voxel(column) * (key.Morph != 0 ? 2f : 1f) * 2f;
            float step = fine * 0.5f;

            for (float z = area.yMin - wide; z <= area.yMax + wide; z += step)
            {
                for (float x = area.xMin - wide; x <= area.xMax + wide; x += step)
                {
                    float px = x + step * 0.3183f;
                    float pz = z + step * 0.2718f;

                    if (!Near(area, px, pz, wide))
                        continue;

                    float margin = wide;

                    if (px < bounds.xMin + margin || px > bounds.xMax - margin
                        || pz < bounds.yMin + margin || pz > bounds.yMax - margin)
                        continue;

                    Probe(scene, report, px, pz);
                }
            }
        }
    }

    private static bool Near(Rect area, float x, float z, float wide)
    {
        bool inside = x >= area.xMin - wide && x <= area.xMax + wide && z >= area.yMin - wide && z <= area.yMax + wide;
        bool deep = x > area.xMin + wide && x < area.xMax - wide && z > area.yMin + wide && z < area.yMax - wide;

        return inside && !deep;
    }

    private static void Probe(Scene scene, Report report, float x, float z)
    {
        report.Probes++;

        var hits = new List<(float Y, int Column)>();

        Hits(scene.Fine, x, z, hits);
        Hits(scene.Coarse, x, z, hits);

        if (hits.Count == 0)
        {
            if (report.Holes == 0)
                report.HoleAt = new Vector2(x, z);

            report.Holes++;
            return;
        }

        int owner = hits[0].Column;
        float top = hits[0].Y;

        foreach ((float y, int column) in hits)
        {
            if (column == owner)
                continue;

            report.Overlaps++;
            report.OverlapWorst = Mathf.Max(report.OverlapWorst, Mathf.Abs(y - top));
            break;
        }

        float reach = scene.Voxel(owner) * 2f;

        scene.Envelope.Range(x - reach, z - reach, x + reach, z + reach, out float low, out float high);

        foreach ((float y, int column) in hits)
        {
            float excess = Mathf.Max(y - high, low - y);

            if (excess <= 0.05f)
                continue;

            report.Spikes++;
            report.SpikeWorst = Mathf.Max(report.SpikeWorst, excess);
        }
    }

    private static void Hits(Grid<Tri> grid, float x, float z, List<(float Y, int Column)> hits)
    {
        List<Tri> near = grid.At(x, z);

        if (near == null)
            return;

        foreach (Tri tri in near)
        {
            if (Inside(tri, x, z, out float y))
                hits.Add((y, tri.Column));
        }
    }

    private static bool Inside(Tri tri, float x, float z, out float y)
    {
        y = 0f;

        float d = (tri.B.z - tri.C.z) * (tri.A.x - tri.C.x) + (tri.C.x - tri.B.x) * (tri.A.z - tri.C.z);

        if (Mathf.Abs(d) < 1e-9f)
            return false;

        float a = ((tri.B.z - tri.C.z) * (x - tri.C.x) + (tri.C.x - tri.B.x) * (z - tri.C.z)) / d;
        float b = ((tri.C.z - tri.A.z) * (x - tri.C.x) + (tri.A.x - tri.C.x) * (z - tri.C.z)) / d;
        float c = 1f - a - b;

        if (a < 0f || b < 0f || c < 0f)
            return false;

        y = a * tri.A.y + b * tri.B.y + c * tri.C.y;

        return true;
    }

    private static float Top(Scene scene, float x, float z)
    {
        var hits = new List<(float Y, int Column)>();

        Hits(scene.Fine, x, z, hits);
        Hits(scene.Coarse, x, z, hits);

        float top = float.MinValue;

        foreach ((float y, int _) in hits)
            top = Mathf.Max(top, y);

        return top;
    }

    private static void CheckSkirts(Scene scene, Report report)
    {
        var seen = new Dictionary<(long, long, long), int>();
        var planes = new Grid<Tri>(8f);

        foreach (Tri tri in scene.Triangles)
        {
            if (!tri.Skirt)
                continue;

            report.SkirtTriangles++;

            float tallest = Mathf.Max(tri.A.y, Mathf.Max(tri.B.y, tri.C.y)) - Mathf.Min(tri.A.y, Mathf.Min(tri.B.y, tri.C.y));
            report.SkirtTallest = Mathf.Max(report.SkirtTallest, tallest);

            long[] corners = { Quantize(tri.A), Quantize(tri.B), Quantize(tri.C) };
            Array.Sort(corners);

            var key = (corners[0], corners[1], corners[2]);

            if (seen.TryGetValue(key, out int chunk) && chunk != tri.Chunk)
                report.SkirtDuplicates++;

            seen[key] = tri.Chunk;

            Vector3 across = Vector3.Cross(tri.B - tri.A, tri.C - tri.A);
            across.y = 0f;

            if (across.sqrMagnitude < 1e-12f)
                continue;

            across.Normalize();

            planes.Add(tri, Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x)), Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z)),
                Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x)), Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z)));

            Owner(scene, report, tri, across);
            Exposure(scene, report, tri, across);
        }

        foreach (Tri tri in scene.Triangles)
        {
            if (!tri.Skirt)
                continue;

            Vector3 centre = (tri.A + tri.B + tri.C) / 3f;
            List<Tri> near = planes.At(centre.x, centre.z);

            if (near == null)
                continue;

            foreach (Tri other in near)
            {
                if (other.Column == tri.Column)
                    continue;

                if (!Coplanar(other, centre))
                    continue;

                report.SkirtDuplicates++;
                break;
            }
        }
    }

    private static void Owner(Scene scene, Report report, Tri tri, Vector3 across)
    {
        Vector3 centre = (tri.A + tri.B + tri.C) / 3f;
        Rect area = scene.Area(tri.Column);

        var middle = new Vector3(area.center.x, 0f, area.center.y);
        Vector3 outward = Vector3.Dot(across, centre - middle) >= 0f ? across : -across;

        float reach = scene.Voxel(tri.Column) * 4f;
        int own = scene.Layout.Keys[tri.Column].Lod;
        int other = scene.Layout.LodAt(new Vector2(centre.x + outward.x * reach, centre.z + outward.z * reach));

        if (other >= 0 && other < own)
            report.SkirtWrongOwner++;
    }

    private static void Exposure(Scene scene, Report report, Tri tri, Vector3 across)
    {
        float shift = Mathf.Max(0.01f, scene.Voxel(tri.Column) * 0.02f);

        Vector3[] samples =
        {
            (tri.A + tri.B + tri.C) / 3f,
            tri.A * 0.6667f + tri.B * 0.1667f + tri.C * 0.1666f,
            tri.A * 0.1667f + tri.B * 0.6667f + tri.C * 0.1666f,
            tri.A * 0.1667f + tri.B * 0.1666f + tri.C * 0.6667f
        };

        foreach (Vector3 point in samples)
        {
            foreach (float sign in new[] { -1f, 1f })
            {
                float x = point.x + across.x * shift * sign;
                float z = point.z + across.z * shift * sign;

                Rect bounds = scene.Layout.Bounds;

                if (x < bounds.xMin || x > bounds.xMax || z < bounds.yMin || z > bounds.yMax)
                    continue;

                float top = Top(scene, x, z);
                float exposed = top == float.MinValue ? float.MaxValue : point.y - top;

                if (exposed <= SKIRT_TOLERANCE)
                    continue;

                report.SkirtExposed++;

                float wall = Mathf.Max(tri.A.y, Mathf.Max(tri.B.y, tri.C.y)) - Mathf.Min(tri.A.y, Mathf.Min(tri.B.y, tri.C.y));

                if (wall > report.SkirtExposedWorst)
                {
                    report.SkirtExposedWorst = wall;
                    report.SkirtExposedAt = point;
                    report.SkirtExposedKey = Describe(scene.Layout.Keys[tri.Column]);
                }

                return;
            }
        }
    }

    private static bool Coplanar(Tri tri, Vector3 point)
    {
        Vector3 normal = Vector3.Cross(tri.B - tri.A, tri.C - tri.A);

        if (normal.sqrMagnitude < 1e-12f)
            return false;

        normal.Normalize();

        if (Mathf.Abs(Vector3.Dot(point - tri.A, normal)) > WELD)
            return false;

        Vector3 flat = new Vector3(normal.z, 0f, -normal.x);

        float u = Vector3.Dot(point, flat);
        float ua = Vector3.Dot(tri.A, flat), ub = Vector3.Dot(tri.B, flat), uc = Vector3.Dot(tri.C, flat);

        float d = (tri.B.y - tri.C.y) * (ua - uc) + (uc - ub) * (tri.A.y - tri.C.y);

        if (Mathf.Abs(d) < 1e-9f)
            return false;

        float a = ((tri.B.y - tri.C.y) * (u - uc) + (uc - ub) * (point.y - tri.C.y)) / d;
        float b = ((tri.C.y - tri.A.y) * (u - uc) + (ua - uc) * (point.y - tri.C.y)) / d;

        return a > 0.01f && b > 0.01f && 1f - a - b > 0.01f;
    }

    private static float Flat(Vector3 delta)
    {
        return Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
    }

    private static bool Finite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private static VoxelDensityField Field(HeightMap map, VoxelConfig voxels)
    {
        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        return new VoxelDensityField(map, voxels, lowest * MAX_HEIGHT);
    }

    private static HeightMap Terrain(int seed)
    {
        var map = new HeightMap(WORLD + 1, WORLD, MAX_HEIGHT);
        var random = new System.Random(seed);

        double P() => random.NextDouble() * Math.PI * 2.0;

        double p1 = P(), p2 = P(), p3 = P(), p4 = P(), p5 = P(), p6 = P(), p7 = P();
        double angle = random.NextDouble() * Math.PI;
        double ca = Math.Cos(angle), sa = Math.Sin(angle);

        System.Threading.Tasks.Parallel.For(0, WORLD + 1, row =>
        {
            for (int x = 0; x <= WORLD; x++)
            {
                double z = row;

                double broad = 0.12 * Math.Sin(x * 0.0041 + p1) * Math.Cos(z * 0.0033 + p2);
                double ridge = 0.10 * (1.0 - Math.Abs(Math.Sin(x * 0.0019 + z * 0.0027 + p3)));
                double hills = 0.035 * Math.Sin(x * 0.031 + p4) * Math.Sin(z * 0.027 + p5);
                double rough = 0.009 * Math.Sin(x * 0.21 + z * 0.17 + p6) + 0.004 * Math.Sin(x * 0.53 - z * 0.47 + p7);

                double along = x * ca + z * sa;
                double cliff = 0.05 / (1.0 + Math.Exp(-(Math.Sin(along * 0.006) * 120.0) / 3.0));

                double h = 0.35 + broad + ridge + hills + rough + cliff;

                map.Heights[row * (WORLD + 1) + x] = (float)Math.Clamp(h, 0.0, 1.0);
            }
        });

        return map;
    }

    private static void Set(object target, string property, object value)
    {
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(target, value);
    }
}
