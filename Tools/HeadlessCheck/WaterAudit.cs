using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

static class WaterAudit
{
    private const float PROBE = 2f;
    private const float COPLANAR = 0.03f;
    private const float HOLE = 0.15f;
    private const float HANG = 0.1f;
    private const float LOD_MARGIN = 0.3f;
    private const float TRENCH = 6f;
    private const float STEP_GRADE = 0.15f;
    private const float INDEX_CELL = 8f;

    public sealed class Result
    {
        public long Probes;
        public long Holes, Hanging, ZFight, Stacked, Shore, LodFlood, LodMissing;
        public long[] FloodByLod = new long[8], MissingByLod = new long[8];
        public int Steps, TrenchPoints, RiverPoints;
        public float WorstStep, WorstTrench, WorstHang;
        public int Triangles, Parts;
        public readonly ConcurrentDictionary<string, ConcurrentBag<Vector2>> Examples = new();

        public float Area(long probes) => probes * PROBE * PROBE;

        public string Describe()
        {
            return $"проб {Probes:N0} ({Area(Probes) / 1e6f:0.00} км² у воды)\n"
                + $"  дыры (земля ниже воды, воды нет)          {Area(Holes),10:N0} м²\n"
                + $"  вода висит над сушей                       {Area(Hanging),10:N0} м², до {WorstHang:0.00} м\n"
                + $"  z-fighting двух вод                         {Area(ZFight),10:N0} м²\n"
                + $"  двойная вода (две поверхности)              {Area(Stacked),10:N0} м²\n"
                + $"  мерцание берега (вода и земля в 3 см)       {Area(Shore),10:N0} м²\n"
                + $"  LOD: вода видна над сушей                   {Area(LodFlood),10:N0} м²\n"
                + $"  LOD: вода скрыта грубой землёй              {Area(LodMissing),10:N0} м²\n"
                + $"  LOD по кольцам, видна над сушей / скрыта (м²): {ByLod()}\n"
                + $"  ступеньки уровня реки круче {STEP_GRADE:P0}              {Steps,10:N0} (до {WorstStep:0.00} м/м) из {RiverPoints:N0} точек\n"
                + $"  траншеи глубже {TRENCH:0} м                          {TrenchPoints,10:N0} точек (до {WorstTrench:0.0} м)\n"
                + $"  меши: {Parts} частей, {Triangles:N0} треугольников";
        }

        private string ByLod()
        {
            var parts = new List<string>();

            for (int lod = 0; lod < FloodByLod.Length; lod++)
            {
                if (FloodByLod[lod] + MissingByLod[lod] > 0)
                    parts.Add($"L{lod} {Area(FloodByLod[lod]):N0}/{Area(MissingByLod[lod]):N0}");
            }

            return string.Join(", ", parts);
        }

        public void Example(string kind, Vector2 point)
        {
            ConcurrentBag<Vector2> bag = Examples.GetOrAdd(kind, _ => new ConcurrentBag<Vector2>());

            if (bag.Count < 6)
                bag.Add(point);
        }
    }

    private sealed class Tri
    {
        public Vector3 A, B, C;
        public int Source;
    }

    public static void Run(string[] args, WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois)
    {
        const string GENERATED = "../../Assets/_Dustborn/Generated";

        var clock = Stopwatch.StartNew();
        HeightMap map;
        WaterMap water;
        string origin;
        HeightMap raw = null;

        if (Array.IndexOf(args, "--pipeline") >= 0)
        {
            WorldMapResult world = new WorldMapPipeline(config, biomes, pois).Generate();
            map = world.Heights;
            water = world.Water;
            origin = $"полный конвейер текущего кода за {clock.Elapsed.TotalSeconds:0} с";
        }
        else if (Array.IndexOf(args, "--generate") >= 0)
        {
            string cache = null;
            int at = Array.IndexOf(args, "--raw-cache");

            if (at >= 0)
                cache = args[at + 1];

            if (cache != null && File.Exists(cache))
            {
                raw = HeightMap.FromRaw16(File.ReadAllBytes(cache), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
            }
            else
            {
                BiomeMap biomeMap = new BiomeMapGenerator(config, biomes).Generate();
                raw = new HeightMapGenerator(config, biomes).Generate(biomeMap);

                if (cache != null)
                    File.WriteAllBytes(cache, raw.ToRaw16());
            }

            if (Array.IndexOf(args, "--profile") >= 0)
                Profile(config, raw);

            clock.Restart();
            map = WorldMapPipeline.Hydrate(config, raw, out _);
            water = map.Water;
            origin = $"вода текущего кода на рельефе {(cache != null ? "из кеша" : "заново")}, гидрология {clock.ElapsedMilliseconds} мс";
        }
        else
        {
            map = HeightMap.FromRaw16(File.ReadAllBytes($"{GENERATED}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
            byte[] bytes = File.ReadAllBytes($"{GENERATED}/WaterMap.bytes");

            clock.Restart();
            water = WaterMap.FromBytes(bytes);
            origin = $"запечённый мир из Generated/, декодирование {clock.ElapsedMilliseconds} мс";
        }

        int inspect = Array.IndexOf(args, "--at");

        for (int at = inspect; at >= 0 && at + 2 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ix); at += 2)
        {
            Inspect(map, water, ix, float.Parse(args[at + 2], System.Globalization.CultureInfo.InvariantCulture));

            if (at + 3 >= args.Length || args[at + 3].StartsWith("--"))
                return;
        }

        var viewer = new Vector2(2036.5f, 4577.5f);
        Result result = Measure(map, water, viewer, "water_audit.png");
        WaterShoreChecks.Result shore = WaterShoreChecks.Measure(map, raw ?? map, water, config.Water.RiverBankWidth);

        Console.WriteLine($"аудит воды ({water.Rivers.Count} рек, {water.Bodies.Count} озёр и прудов), {origin}:");
        Console.WriteLine($"отпечаток: вода {Fingerprint(water.ToBytes())}, рельеф {Fingerprint(Bytes(map.Heights))}");

        if (water.Stamps != null)
            Console.WriteLine(WaterStampChecks.Describe(water.Stamps));
        Console.WriteLine(result.Describe());
        Console.WriteLine(shore.Describe());
        PrintExamples(result);

        int debug = Array.IndexOf(args, "--water-debug");

        if (debug >= 0 && debug + 1 < args.Length)
            Images(args[debug + 1], map, water, config);

        int view = Array.IndexOf(args, "--view");

        if (view >= 0 && view + 1 < args.Length)
        {
            bool listed = view + 2 < args.Length && File.Exists(args[view + 2]);
            int lod = Array.IndexOf(args, "--view-lod");
            float distance = lod >= 0 ? float.Parse(args[lod + 1], System.Globalization.CultureInfo.InvariantCulture) : 0f;

            WaterView.Render(args[view + 1], map, water, listed ? WaterView.Read(args[view + 2]) : WaterView.Pick(water, map), distance);
        }

        Benchmark(water, map);
    }

    private static string Fingerprint(byte[] bytes)
    {
        using var hash = System.Security.Cryptography.SHA256.Create();

        return Convert.ToHexString(hash.ComputeHash(bytes)).Substring(0, 16);
    }

    private static byte[] Bytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);

        return bytes;
    }

    private static void Images(string directory, HeightMap map, WaterMap water, WorldGenerationConfig config)
    {
        const int OVERVIEW = 2048;
        const int DETAIL = 4096;

        Directory.CreateDirectory(directory);

        Png.Write(Path.Combine(directory, "water_mask.png"), WaterDebugImages.Mask(water), water.Resolution, water.Resolution);
        Png.Write(Path.Combine(directory, "body_id.png"), WaterDebugImages.BodyIds(water), water.Resolution, water.Resolution);
        Png.Write(Path.Combine(directory, "overview.png"), WaterDebugImages.Overview(map, water, Array.Empty<TerrainStampPlacement>(), OVERVIEW), OVERVIEW, OVERVIEW);
        Png.Write(Path.Combine(directory, "refined_water_mask.png"), WaterDebugImages.Refined(map, water, DETAIL), DETAIL, DETAIL);
        Png.Write(Path.Combine(directory, "coarse_vs_refined.png"), WaterDebugImages.Comparison(map, water, DETAIL), DETAIL, DETAIL);
        Png.Write(Path.Combine(directory, "shoreline.png"), WaterDebugImages.Shoreline(map, water, DETAIL), DETAIL, DETAIL);
        Png.Write(Path.Combine(directory, "river_centerlines.png"), WaterDebugImages.Rivers(map, water, OVERVIEW, WaterDebugImages.RiverTint.Centerline), OVERVIEW, OVERVIEW);
        Png.Write(Path.Combine(directory, "river_width.png"), WaterDebugImages.Rivers(map, water, OVERVIEW, WaterDebugImages.RiverTint.Width), OVERVIEW, OVERVIEW);
        Png.Write(Path.Combine(directory, "river_depth.png"), WaterDebugImages.Rivers(map, water, OVERVIEW, WaterDebugImages.RiverTint.Depth), OVERVIEW, OVERVIEW);

        if (water.Stamps != null)
            StampImages(directory, map, water, config);

        Console.WriteLine($"  картинки воды в {directory}");
    }

    private static void StampImages(string directory, HeightMap map, WaterMap water, WorldGenerationConfig config)
    {
        const int OVERVIEW = 2048;
        const int ZOOM = 1024;
        const float WINDOW = 1600f;

        WaterStampLibrary library = WaterStampLibrary.Create(config.Water);

        Png.Write(Path.Combine(directory, "stamps_overview.png"), WaterDebugImages.Stamps(map, water, library, OVERVIEW, Vector2.zero, water.WorldSize), OVERVIEW, OVERVIEW);
        Png.Write(Path.Combine(directory, "stamp_masks.png"), WaterDebugImages.StampMasks(water, library, OVERVIEW), OVERVIEW, OVERVIEW);
        File.WriteAllText(Path.Combine(directory, "stamps.txt"), WaterDebugImages.StampReport(water.Stamps));

        string zooms = Environment.GetEnvironmentVariable("WATER_ZOOM");

        if (!string.IsNullOrEmpty(zooms))
        {
            foreach (string zoom in zooms.Split(';'))
            {
                string[] parts = zoom.Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                float x = float.Parse(parts[0], culture), z = float.Parse(parts[1], culture), extent = float.Parse(parts[2], culture);
                Png.Write(Path.Combine(directory, $"zoom_{x:0}_{z:0}_{extent:0}.png"), WaterDebugImages.Stamps(map, water, library, ZOOM, new Vector2(x - 0.5f * extent, z - 0.5f * extent), extent), ZOOM, ZOOM);
            }
        }

        int rivers = 0, lakes = 0;

        for (int i = 0; i < water.Stamps.Placements.Count; i++)
        {
            WaterStampPlacement placement = water.Stamps.Placements[i];
            bool river = placement.Kind == WaterStampKind.River;

            if (river ? rivers++ >= 8 : lakes++ >= 6)
                continue;

            Vector2 origin = placement.Center - new Vector2(0.5f * WINDOW, 0.5f * WINDOW);
            Png.Write(Path.Combine(directory, $"stamp_{i:000}_{placement.Name}.png"), WaterDebugImages.Stamps(map, water, library, ZOOM, origin, WINDOW), ZOOM, ZOOM);
        }
    }

    private static void Profile(WorldGenerationConfig config, HeightMap raw)
    {
        const int RUNS = 3;

        var totals = new double[5];
        var clock = new Stopwatch();

        for (int run = 0; run < RUNS; run++)
        {
            var hydrology = new Hydrology(config, raw);

            clock.Restart();
            HydrologyGrid grid = hydrology.Analyze();
            totals[0] += clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            WaterMap water = hydrology.Build(grid);
            totals[1] += clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            HeightMap carved = RiverCarver.Carve(raw, water, config.Water.RiverBankWidth);
            totals[2] += clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            WaterShore.Finish(water, carved);
            totals[3] += clock.Elapsed.TotalMilliseconds;

            clock.Restart();
            WaterMeshes.Build(water);
            totals[4] += clock.Elapsed.TotalMilliseconds;
        }

        Console.WriteLine($"профиль воды, среднее из {RUNS}: анализ стока {totals[0] / RUNS:0} мс, реки и озёра {totals[1] / RUNS:0} мс, "
            + $"врезка {totals[2] / RUNS:0} мс, берега {totals[3] / RUNS:0} мс, меши {totals[4] / RUNS:0} мс");
    }

    public static void Inspect(HeightMap map, WaterMap water, float x, float z)
    {
        float ground = map.SampleWorldSmooth(x, z);
        WaterSample s = water.Sample(x, z);
        int c = water.CellIndex(x, z);
        Console.WriteLine($"  uncarved {(water.Uncarved != null ? water.Uncarved.SampleWorldSmooth(x, z) : float.NaN):0.00}");
        Console.WriteLine($"({x},{z}) ground {ground:0.00} sample {s.Kind} surface {s.Surface:0.00} width {s.Width:0.0} body {s.Body} | cell kind {(WaterKind)water.Kinds[c]} bodyId {water.BodyIds[c]} shoreDist {water.ShoreDistance[c]:0.0} shoreSurf {water.ShoreSurface[c]:0.00}");
        for (int r = 0; r < water.Rivers.Count; r++)
        {
            var pts = water.Rivers[r].Points;
            for (int i = 0; i < pts.Count; i++)
            {
                if (Vector2.Distance(pts[i].Position, new Vector2(x, z)) > 30f) continue;
                float g = map.SampleWorldSmooth(pts[i].Position.x, pts[i].Position.y);
                Console.WriteLine($"  river {r} pt {i} ({pts[i].Position.x:0.0},{pts[i].Position.y:0.0}) surf {pts[i].Surface:0.00} bed {pts[i].Bed:0.00} w {pts[i].Width:0.0} sub {pts[i].Submerged} ground {g:0.00} dist {Vector2.Distance(pts[i].Position, new Vector2(x, z)):0.0}");
            }
        }
        for (int dz = -2; dz <= 2; dz++) { var line = ""; for (int dx = -2; dx <= 2; dx++) { int cc = water.CellIndex(x + dx * water.CellSize, z + dz * water.CellSize); line += $"{(WaterKind)water.Kinds[cc],-5}{water.BodyIds[cc],4}{(water.DetailSlot[cc] >= 0 ? "d" : " ")} "; } Console.WriteLine("  " + line); }

        bool covered = water.Covers(x, z, out short owner);
        Console.WriteLine($"  owner {water.OwnerAt(x, z)} covers {covered} ({owner})");

        int start = water.DetailStart(c);

        if (start >= 0)
        {
            for (int j = WaterMap.CELL_SIDE_NODES - 1; j >= 0; j--)
            {
                var line = "";

                for (int i = 0; i < WaterMap.CELL_SIDE_NODES; i++)
                {
                    int k = start + j * WaterMap.CELL_SIDE_NODES + i;
                    line += $"{water.DetailOwner(k),3}:{water.DetailGround(k),7:0.00} ";
                }

                Console.WriteLine("    " + line);
            }
        }

        foreach (WaterMeshPart part in WaterMeshes.Build(water))
        {
            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                var tri = new Tri { A = part.Vertices[part.Triangles[t]], B = part.Vertices[part.Triangles[t + 1]], C = part.Vertices[part.Triangles[t + 2]] };

                if (Inside(tri, x, z, out float y))
                    Console.WriteLine($"  mesh {part.Name} source {part.Sources[t / 3]} y {y:0.00}  tri ({tri.A.x:0.0},{tri.A.z:0.0}) ({tri.B.x:0.0},{tri.B.z:0.0}) ({tri.C.x:0.0},{tri.C.z:0.0})");
            }
        }
    }

    public static void PrintExamples(Result result)
    {
        foreach (KeyValuePair<string, ConcurrentBag<Vector2>> pair in result.Examples)
        {
            var points = new List<string>();

            foreach (Vector2 point in pair.Value)
                points.Add($"({point.x:0}, {point.y:0})");

            Console.WriteLine($"  примеры {pair.Key}: {string.Join(" ", points)}");
        }
    }

    public static Result Measure(HeightMap map, WaterMap water, Vector2 viewer, string image)
    {
        var result = new Result();

        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        var voxels = new VoxelConfig();
        using var field = new VoxelDensityField(map, voxels, lowest * map.MaxHeight);
        var surface = new VoxelSurfaceProbe(field.Sampler, new VoxelStreamPlan(voxels, 6, 96f, map.WorldSize), viewer, voxels.ChunkSize);

        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        var triangles = new List<Tri>();

        foreach (WaterMeshPart part in parts)
        {
            result.Parts++;

            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                triangles.Add(new Tri
                {
                    A = part.Vertices[part.Triangles[t]],
                    B = part.Vertices[part.Triangles[t + 1]],
                    C = part.Vertices[part.Triangles[t + 2]],
                    Source = part.Sources.Count > t / 3 ? part.Sources[t / 3] : 0
                });
            }
        }

        result.Triangles = triangles.Count;

        var index = new Dictionary<long, List<Tri>>();

        foreach (Tri tri in triangles)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(tri.A.x, Mathf.Min(tri.B.x, tri.C.x)) / INDEX_CELL);
            int x1 = Mathf.FloorToInt(Mathf.Max(tri.A.x, Mathf.Max(tri.B.x, tri.C.x)) / INDEX_CELL);
            int z0 = Mathf.FloorToInt(Mathf.Min(tri.A.z, Mathf.Min(tri.B.z, tri.C.z)) / INDEX_CELL);
            int z1 = Mathf.FloorToInt(Mathf.Max(tri.A.z, Mathf.Max(tri.B.z, tri.C.z)) / INDEX_CELL);

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

        int resolution = water.Resolution;
        float cell = water.CellSize;
        float near = water.ShoreReach;

        long probes = 0, holes = 0, hanging = 0, zfight = 0, stacked = 0, shore = 0, lodFlood = 0, lodMissing = 0;
        var floodByLod = new long[8];
        var missingByLod = new long[8];
        float worstHang = 0f;
        var marks = new ConcurrentBag<(Vector2 Point, byte R, byte G, byte B)>();
        object gate = new();

        Parallel.For(0, resolution, row =>
        {
            long p = 0, h = 0, hg = 0, zf = 0, st = 0, sh = 0, lf = 0, lm = 0;
            float hangMax = 0f;
            var covers = new List<(float Y, int Source)>();
            var floods = new long[8];
            var misses = new long[8];

            for (int column = 0; column < resolution; column++)
            {
                int c = row * resolution + column;

                if (water.Kinds[c] == 0 && !(water.ShoreDistance[c] <= near))
                    continue;

                for (float dz = 0.5f; dz < cell; dz += PROBE)
                {
                    for (float dx = 0.5f; dx < cell; dx += PROBE)
                    {
                        float x = column * cell + dx;
                        float z = row * cell + dz;

                        if (x >= map.WorldSize || z >= map.WorldSize)
                            continue;

                        p++;

                        float ground = map.SampleWorldSmooth(x, z);
                        WaterSample sample = water.Sample(x, z);
                        bool wet = sample.IsWater && ground < sample.Surface;

                        covers.Clear();

                        if (index.TryGetValue(((long)Mathf.FloorToInt(x / INDEX_CELL) << 32) ^ (uint)Mathf.FloorToInt(z / INDEX_CELL), out List<Tri> list))
                        {
                            foreach (Tri tri in list)
                            {
                                if (Inside(tri, x, z, out float y))
                                    covers.Add((y, tri.Source));
                            }
                        }

                        float top = float.NegativeInfinity;

                        foreach ((float y, int _) in covers)
                            top = Mathf.Max(top, y);

                        if (wet && ground < sample.Surface - HOLE && (covers.Count == 0 || top < ground))
                        {
                            h++;
                            marks.Add((new Vector2(x, z), 255, 0, 0));
                        }

                        if (!wet && covers.Count > 0 && top > ground + HANG)
                        {
                            hg++;
                            if (top - ground > 3f)
                                result.Example("висит выше 3 м", new Vector2(x, z));

                            hangMax = Mathf.Max(hangMax, top - ground);
                            marks.Add((new Vector2(x, z), 255, 0, 255));
                        }

                        bool fight = false, pile = false;

                        for (int i = 0; i < covers.Count && !(fight && pile); i++)
                        {
                            for (int j = i + 1; j < covers.Count; j++)
                            {
                                if (covers[i].Source == covers[j].Source)
                                    continue;

                                float gap = Mathf.Abs(covers[i].Y - covers[j].Y);

                                if (gap < COPLANAR)
                                    fight = true;
                                else if (covers[i].Y > ground + 0.05f && covers[j].Y > ground + 0.05f)
                                    pile = true;
                            }
                        }

                        if (fight)
                        {
                            zf++;
                            marks.Add((new Vector2(x, z), 255, 255, 0));
                        }

                        if (pile)
                        {
                            st++;
                            marks.Add((new Vector2(x, z), 255, 140, 0));
                        }

                        if (covers.Count > 0 && Mathf.Abs(top - ground) < COPLANAR)
                            sh++;

                        if (covers.Count == 0)
                            continue;

                        float rendered = surface.Height(x, z);

                        if (!wet && top > rendered + 0.05f && ground >= top + LOD_MARGIN)
                        {
                            lf++;
                            floods[surface.LodAt(x, z)]++;
                            marks.Add((new Vector2(x, z), 0, 255, 255));
                        }

                        if (wet && ground < sample.Surface - LOD_MARGIN && top < rendered)
                        {
                            lm++;
                            misses[surface.LodAt(x, z)]++;
                        }
                    }
                }
            }

            lock (gate)
            {
                probes += p;
                holes += h;
                hanging += hg;
                zfight += zf;
                stacked += st;
                shore += sh;
                lodFlood += lf;
                lodMissing += lm;

                for (int lod = 0; lod < 8; lod++)
                {
                    floodByLod[lod] += floods[lod];
                    missingByLod[lod] += misses[lod];
                }

                worstHang = Mathf.Max(worstHang, hangMax);
            }
        });

        result.Probes = probes;
        result.Holes = holes;
        result.Hanging = hanging;
        result.ZFight = zfight;
        result.Stacked = stacked;
        result.Shore = shore;
        result.LodFlood = lodFlood;
        result.LodMissing = lodMissing;
        result.FloodByLod = floodByLod;
        result.MissingByLod = missingByLod;
        result.WorstHang = worstHang;

        foreach ((Vector2 point, byte r, byte g, byte b) in marks)
        {
            string kind = r == 255 && g == 0 && b == 0 ? "дыра" : r == 255 && b == 255 ? "висит" : r == 255 && g == 255 ? "z-fight"
                : r == 255 ? "двойная" : "LOD";
            result.Example(kind, point);
        }

        Rivers(map, water, result, marks);

        if (image != null)
            Draw(image, map, water, marks);

        return result;
    }

    private static void Rivers(HeightMap map, WaterMap water, Result result, ConcurrentBag<(Vector2, byte, byte, byte)> marks)
    {
        float bank = 10f;

        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (point.Submerged)
                    continue;

                result.RiverPoints++;

                if (i > 0 && !points[i - 1].Submerged)
                {
                    float run = Mathf.Max(0.01f, Vector2.Distance(points[i - 1].Position, point.Position));
                    float grade = (points[i - 1].Surface - point.Surface) / run;

                    if (grade > STEP_GRADE)
                    {
                        result.Steps++;
                        result.WorstStep = Mathf.Max(result.WorstStep, grade);
                        result.Example("ступенька", point.Position);
                    }
                }

                Vector2 before = points[Mathf.Max(0, i - 1)].Position;
                Vector2 after = points[Mathf.Min(points.Count - 1, i + 1)].Position;
                Vector2 tangent = (after - before).normalized;
                var normal = new Vector2(-tangent.y, tangent.x);
                float reach = point.Width * 0.5f + bank + 4f;

                Vector2 left = point.Position + normal * reach;
                Vector2 right = point.Position - normal * reach;

                float incision = Mathf.Min(map.SampleWorldSmooth(left.x, left.y), map.SampleWorldSmooth(right.x, right.y)) - point.Surface;

                if (incision > TRENCH)
                {
                    result.TrenchPoints++;
                    result.WorstTrench = Mathf.Max(result.WorstTrench, incision);
                    result.Example("траншея", point.Position);
                    marks.Add((point.Position, 150, 80, 20));
                }
            }
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

        if (a < -1e-5f || b < -1e-5f || c < -1e-5f)
            return false;

        y = a * tri.A.y + b * tri.B.y + c * tri.C.y;
        return true;
    }

    private static void Draw(string path, HeightMap map, WaterMap water, ConcurrentBag<(Vector2 Point, byte R, byte G, byte B)> marks)
    {
        const int SIZE = 2048;

        byte[] pixels = WaterDebugImages.Overview(map, water, Array.Empty<TerrainStampPlacement>(), SIZE);
        float step = (float)map.WorldSize / SIZE;

        foreach ((Vector2 point, byte r, byte g, byte b) in marks)
        {
            int column = Mathf.Clamp((int)(point.x / step), 0, SIZE - 1);
            int row = SIZE - 1 - Mathf.Clamp((int)(point.y / step), 0, SIZE - 1);
            int i = (row * SIZE + column) * 3;

            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }

        Png.Write(path, pixels, SIZE, SIZE);
    }

    public static void Benchmark(WaterMap water, HeightMap map)
    {
        const int COUNT = 2_000_000;

        var random = new System.Random(11);
        var points = new Vector3[COUNT];

        for (int i = 0; i < COUNT; i++)
        {
            float x = (float)random.NextDouble() * water.WorldSize;
            float z = (float)random.NextDouble() * water.WorldSize;
            points[i] = new Vector3(x, map.SampleWorldSmooth(x, z), z);
        }

        int hits = 0;
        var clock = Stopwatch.StartNew();

        foreach (Vector3 point in points)
        {
            if (water.IsWater(point.x, point.z, point.y))
                hits++;
        }

        double isWater = clock.Elapsed.TotalMilliseconds * 1e6 / COUNT;
        clock.Restart();

        foreach (Vector3 point in points)
        {
            if (water.IsWet(point.x, point.z, point.y, 6f))
                hits++;
        }

        double isWet = clock.Elapsed.TotalMilliseconds * 1e6 / COUNT;
        clock.Restart();

        for (int i = 0; i + 1 < COUNT; i += 2)
        {
            if (water.CrossesRiver(new Vector2(points[i].x, points[i].z), new Vector2(points[i].x + 32f, points[i].z + 32f), out _, out _))
                hits++;
        }

        double crosses = clock.Elapsed.TotalMilliseconds * 1e6 / (COUNT / 2);
        clock.Restart();

        byte[] bytes = water.ToBytes();
        double encode = clock.Elapsed.TotalMilliseconds;
        clock.Restart();

        WaterMap.FromBytes(bytes);
        double decode = clock.Elapsed.TotalMilliseconds;
        clock.Restart();

        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        double meshes = clock.Elapsed.TotalMilliseconds;

        Console.WriteLine($"скорость: IsWater {isWater:0} нс, IsWet {isWet:0} нс, CrossesRiver {crosses:0} нс на вызов; "
            + $"ToBytes {encode:0} мс, FromBytes {decode:0} мс, меши {meshes:0} мс ({parts.Count} частей); контроль {hits}");
    }
}
