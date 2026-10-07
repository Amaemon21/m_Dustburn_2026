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
    private const float CLIFF = 0.3f;
    private const float DRIFT = 0.1f;
    private const float DRY_END_REACH = 24f;
    private const float LOD_MARGIN = 0.3f;
    private const float TRENCH = 6f;
    private const float BRIDGE_APPROACH = 40f;
    private const float STEP_GRADE = 0.15f;
    private const float INDEX_CELL = 8f;
    private const int NEAR_LOD = 1;

    public sealed class Result
    {
        public long Probes;
        public long Holes, Hanging, ZFight, Stacked, Shore, LodFlood, LodMissing, Cliffs;
        public long[] FloodByLod = new long[8], MissingByLod = new long[8];
        public int Steps, TrenchPoints, RiverPoints;
        public float WorstStep, WorstTrench, WorstHang, WorstCliff;
        public Vector2 WorstTrenchAt;
        public float LargestHole, LargestHang, LargestZFight, LargestStack, LargestNearLod, LargestCliff;
        public int Triangles, Parts;
        public string CliffPatches = "";
        public float DriftArea = -1f, WorstDrift, LargestDrift;
        public string DriftPatches = "";
        public readonly ConcurrentDictionary<string, ConcurrentBag<Vector2>> Examples = new();

        public float Area(long probes) => probes * PROBE * PROBE;

        public string Describe()
        {
            return $"проб {Probes:N0} ({Area(Probes) / 1e6f:0.00} км² у воды)\n"
                + $"  дыры (земля ниже воды, воды нет)          {Area(Holes),10:N0} м²\n"
                + $"  вода висит над сушей                       {Area(Hanging),10:N0} м², до {WorstHang:0.00} м\n"
                + $"  обрыв кромки воды (рядом земля ниже уровня)  {Area(Cliffs),10:N0} м², до {WorstCliff:0.00} м{(CliffPatches.Length > 0 ? "; пятна " + CliffPatches : "")}\n"
                + $"  z-fighting двух вод                         {Area(ZFight),10:N0} м²\n"
                + $"  двойная вода (две поверхности)              {Area(Stacked),10:N0} м²\n"
                + $"  мерцание берега (вода и земля в 3 см)       {Area(Shore),10:N0} м²\n"
                + $"  LOD: вода видна над сушей                   {Area(LodFlood),10:N0} м²\n"
                + $"  LOD: вода скрыта грубой землёй              {Area(LodMissing),10:N0} м²\n"
                + $"  LOD по кольцам, видна над сушей / скрыта (м²): {ByLod()}\n"
                + $"  ступеньки уровня реки круче {STEP_GRADE:P0}              {Steps,10:N0} (до {WorstStep:0.00} м/м) из {RiverPoints:N0} точек\n"
                + $"  траншеи глубже {TRENCH:0} м                          {TrenchPoints,10:N0} точек (до {WorstTrench:0.0} м в ({WorstTrenchAt.x:0}, {WorstTrenchAt.y:0}))\n"
                + (DriftArea >= 0f ? $"  русло врезано не по итоговому уровню          {DriftArea,10:N0} м², до {WorstDrift:0.00} м{(DriftPatches.Length > 0 ? "; пятна " + DriftPatches : "")}\n" : "")
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
            int cached = Array.IndexOf(args, "--raw-cache");
            HeightMap relief = cached >= 0 && File.Exists(args[cached + 1]) ? ReliefCache.Read(args[cached + 1], config) : null;

            if (cached >= 0 && relief == null)
            {
                relief = new HeightMapGenerator(config, biomes).Generate(new BiomeMapGenerator(config, biomes).Generate());
                ReliefCache.Write(args[cached + 1], relief);
            }

            int trace = Array.IndexOf(args, "--stage-trace");

            if (trace >= 0)
            {
                var points = new List<Vector2>();
                var culture = System.Globalization.CultureInfo.InvariantCulture;

                for (int k = trace + 1; k + 1 < args.Length && float.TryParse(args[k], System.Globalization.NumberStyles.Float, culture, out float tx); k += 2)
                    points.Add(new Vector2(tx, float.Parse(args[k + 1], culture)));

                StageTrace.Run(config, biomes, pois, relief, points);
                return;
            }

            WorldMapResult world = new WorldMapPipeline(config, biomes, pois).Generate(null, default, relief);

            if (Array.IndexOf(args, "--stage-times") >= 0)
                PrintStageTimes();

            map = world.Heights;
            water = world.Water;
            origin = $"полный конвейер текущего кода за {clock.Elapsed.TotalSeconds:0} с";

            if (Array.IndexOf(args, "--compare-bake") >= 0)
            {
                CompareBake(map, $"{GENERATED}/HeightMap.bytes", config);
                BakeParity.Compare(world, GENERATED, config, args);
            }
        }
        else if (Array.IndexOf(args, "--generate") >= 0)
        {
            string cache = null;
            int at = Array.IndexOf(args, "--raw-cache");
            BiomeMap climateMap = new BiomeMapGenerator(config, biomes).Generate();

            if (at >= 0)
                cache = args[at + 1];

            if (cache != null && File.Exists(cache))
            {
                raw = ReliefCache.Read(cache, config);
            }
            else
            {
                BiomeMap biomeMap = new BiomeMapGenerator(config, biomes).Generate();
                raw = new HeightMapGenerator(config, biomes).Generate(biomeMap);

                if (cache != null)
                    ReliefCache.Write(cache, raw);
            }

            if (Array.IndexOf(args, "--profile") >= 0)
                Profile(config, raw);

            clock.Restart();
            map = WorldMapPipeline.Hydrate(config, raw, out water, out Hydrology hydrology, WaterClimate.From(climateMap, biomes));
            _valleys = hydrology.Valleys;

            int near = Array.IndexOf(args, "--valley-at");

            if (near >= 0 && _valleys != null)
            {
                var spot = new Vector2(float.Parse(args[near + 1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[near + 2], System.Globalization.CultureInfo.InvariantCulture));

                foreach (DrainageValley valley in _valleys.Paths)
                {
                    for (int i = 0; i < valley.Points.Count; i++)
                    {
                        if ((valley.Points[i] - spot).sqrMagnitude < 120f * 120f)
                            Console.WriteLine($"  долина {valley.Id}: точка {i} ({valley.Points[i].x:0}, {valley.Points[i].y:0}) дно {valley.Floor[i]:0.00} земля {valley.Ground[i]:0.00} сток {valley.Discharge[i] / 1e6f:0.00} км²");
                    }
                }
            }

            if (_valleys != null)
            {
                foreach (DrainageValley valley in _valleys.Paths)
                {
                    if (!valley.Terminal)
                        continue;

                    Vector2 end = valley.Points[^1];
                    short body = water.BodyIds[water.CellIndex(end.x, end.y)];
                    Console.WriteLine($"  концевая котловина долины {valley.Id}: ({end.x:0}, {end.y:0}), дно {valley.Floor[^1]:0.00} м при земле {valley.Ground[^1]:0.00} м, радиус {valley.TerminalRadius:0} м, {(body >= 0 ? $"водоём {body} на {water.Bodies[body].Surface:0.00} м" : "воды нет")}");
                }
            }

            if (Array.IndexOf(args, "--events") >= 0)
            {
                foreach (HydrologyEvent entry in hydrology.Log.Events)
                    Console.WriteLine($"  событие: {entry}");
            }

            Console.WriteLine($"гидрология: озёр {hydrology.Lakes}, прудов {hydrology.Ponds}, отброшено по зазору берегов {hydrology.CrowdedBodies}, снято прудов над рекой {hydrology.PerchedBodies}, рек снято с висящим устьем {hydrology.DroppedCourses}, укорочено до озера {hydrology.ShortenedCourses}, продлено до опустившегося озера {hydrology.ExtendedCourses}, влито в другую реку при первом касании {hydrology.CapturedCourses}, снято притоков-обрубков {hydrology.StubCourses}, клеток главной долины на главном стволе {hydrology.MainCells}");
            Console.WriteLine(WaterPolicyAudit.Measure(config, climateMap, biomes, map, water, null).Describe());
            origin = $"вода текущего кода на рельефе {(cache != null ? "из кеша" : "заново")}, гидрология {clock.ElapsedMilliseconds} мс";
        }
        else
        {
            map = HeightMap.FromRaw16(File.ReadAllBytes($"{GENERATED}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
            byte[] bytes = File.ReadAllBytes($"{GENERATED}/WaterMap.bytes");

            clock.Restart();
            water = WaterMapFormat.Read(bytes);
            origin = $"запечённый мир из Generated/, декодирование {clock.ElapsedMilliseconds} мс";
            Console.WriteLine(RiverTopologyAudit.Measure(water).Describe());
        }

        int inspect = Array.IndexOf(args, "--at");

        for (int at = inspect; at >= 0 && at + 2 < args.Length && float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ix); at += 2)
        {
            Inspect(map, water, ix, float.Parse(args[at + 2], System.Globalization.CultureInfo.InvariantCulture));

            if (at + 3 >= args.Length || args[at + 3].StartsWith("--"))
                return;
        }

        int partsAt = Array.IndexOf(args, "--parts");

        if (partsAt >= 0)
        {
            string filter = partsAt + 1 < args.Length && !args[partsAt + 1].StartsWith("--") ? args[partsAt + 1] : "";

            foreach (WaterMeshPart part in WaterMeshes.Build(water))
            {
                if (!part.Name.Contains(filter))
                    continue;

                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

                foreach (Vector3 vertex in part.Vertices)
                {
                    minX = Mathf.Min(minX, vertex.x);
                    maxX = Mathf.Max(maxX, vertex.x);
                    minZ = Mathf.Min(minZ, vertex.z);
                    maxZ = Mathf.Max(maxZ, vertex.z);
                    minY = Mathf.Min(minY, vertex.y);
                    maxY = Mathf.Max(maxY, vertex.y);
                }

                Console.WriteLine($"  часть {part.Name}: {part.Triangles.Count / 3} треугольников, x {minX:0}..{maxX:0}, z {minZ:0}..{maxZ:0}, уровень {minY:0.00}..{maxY:0.00}");
            }
        }

        if (Array.IndexOf(args, "--river-list") >= 0)
            ListRivers(water);

        var viewer = new Vector2(2036.5f, 4577.5f);
        string spots = Environment.GetEnvironmentVariable("WATER_AT");

        if (!string.IsNullOrEmpty(spots))
        {
            foreach (string spot in spots.Split(';'))
            {
                string[] xz = spot.Split(',');
                Inspect(map, water, float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Result result = Measure(map, water, viewer, "water_audit.png");

        if (raw != null)
            ChannelDrift(map, water, config.Water.BridgeMinWidth, result);

        WaterShoreChecks.Result shore = WaterShoreChecks.Measure(map, raw ?? map, water, config.Water.RiverBankWidth);

        Console.WriteLine($"аудит воды ({water.Rivers.Count} рек, {water.Bodies.Count} озёр и прудов), {origin}:");
        Console.WriteLine($"отпечаток: вода {Fingerprint(WaterMapFormat.Write(water))}, рельеф {Fingerprint(Bytes(map.Heights))}");

        if (water.Stamps != null)
            Console.WriteLine(WaterStampChecks.Describe(water.Stamps));
        Console.WriteLine(result.Describe());
        Console.WriteLine(shore.Describe());
        Console.WriteLine(DescribeEnds(water, config.Water.RiverStartArea * 1e6f));
        WaterContract contract = WaterContract.Check(water, config.Water.RiverStartArea * 1e6f, !CoastShaper.Active(config));
        Console.WriteLine(contract.Describe());

        foreach (WaterViolation violation in contract.Violations)
            Console.WriteLine($"  нарушение: {violation}");
        RiverShapeAudit.Result shape = RiverShapeAudit.Measure(map, water);
        Console.WriteLine(shape.Describe());
        PrintExamples(result);
        bool accepted = Array.IndexOf(args, "--strict") < 0 || Strict(water, result, shore, shape, contract);

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

        if (!accepted)
            Environment.Exit(1);
    }

    private static void PrintStageTimes()
    {
        var rows = new List<(string Name, double Ms, int Count)>();

        for (var stage = (WorldGenStage)0; stage < WorldGenStage.Count; stage++)
        {
            WorldGenStageStat stat = WorldGenProbe.Stat(stage);

            if (stat.Count > 0)
                rows.Add((stage.ToString(), WorldGenProbe.ToMs(stat.Ticks), stat.Count));
        }

        rows.Sort((a, b) => b.Ms.CompareTo(a.Ms));

        foreach ((string name, double ms, int count) in rows)
            Console.WriteLine($"  этап {name}: {ms / 1000d:0.0} с ({count})");
    }

    private static void CompareBake(HeightMap map, string path, WorldGenerationConfig config)
    {
        HeightMap baked = HeightMap.FromRaw16(File.ReadAllBytes(path), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        float[] ours = map.Heights, theirs = baked.Heights;
        double sum = 0;
        float worst = 0f;
        long close = 0, far = 0;
        int worstAt = 0;

        for (int i = 0; i < ours.Length; i++)
        {
            float difference = Mathf.Abs(ours[i] - theirs[i]) * config.MaxHeight;
            sum += difference;

            if (difference < 0.05f)
                close++;

            if (difference > 1f)
                far++;

            if (difference <= worst)
                continue;

            worst = difference;
            worstAt = i;
        }

        int resolution = config.HeightMapResolution;
        float cell = config.WorldSize / (float)(resolution - 1);
        Console.WriteLine($"сверка с запечённым миром: средняя разница высот {sum / ours.Length:0.000} м, ближе 5 см {close * 100.0 / ours.Length:0.0} %, дальше 1 м {far * 100.0 / ours.Length:0.00} %, "
            + $"наибольшая {worst:0.00} м в ({worstAt % resolution * cell:0}, {worstAt / resolution * cell:0})");
    }

    private static string Geometry(VoxelConfig voxels, float worldSize, Vector2 viewer, WaterMap water)
    {
        var plan = new VoxelStreamPlan(voxels, 6, 96f, worldSize, water);
        var columns = new List<VoxelColumnKey>();
        var budget = new VoxelBudget();
        plan.Around(viewer, columns);

        foreach (VoxelColumnKey key in columns)
        {
            float size = plan.ChunkMetres(key.Lod);
            budget += VoxelBudget.Estimate(plan.VoxelSize(key.Lod), size * size, true);
        }

        return $"{columns.Count} колонок, {budget.Describe()}";
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

    private static DrainageValleyReport _valleys;

    private static byte[] ValleyImage(HeightMap map, WaterMap water, DrainageValleyReport valleys, int size)
    {
        byte[] pixels = WaterDebugImages.Overview(map, water, Array.Empty<TerrainStampPlacement>(), size);
        float step = (float)map.WorldSize / size;

        foreach (DrainageValley valley in valleys.Paths)
        {
            for (int i = 0; i + 1 < valley.Points.Count; i++)
            {
                bool open = valley.Floor[i] - valley.Ground[i] > 0f;
                byte r = valley.Main ? (byte)255 : open ? (byte)255 : (byte)250;
                byte g = valley.Main ? (byte)40 : open ? (byte)140 : (byte)230;
                byte b = valley.Main ? (byte)40 : open ? (byte)0 : (byte)60;

                for (int k = 0; k <= 16; k++)
                {
                    Vector2 p = Vector2.Lerp(valley.Points[i], valley.Points[i + 1], k / 16f);
                    int column = Mathf.Clamp((int)(p.x / step), 0, size - 1), row = size - 1 - Mathf.Clamp((int)(p.y / step), 0, size - 1);
                    int at = (row * size + column) * 3;
                    pixels[at] = r;
                    pixels[at + 1] = g;
                    pixels[at + 2] = b;
                }
            }
        }

        return pixels;
    }

    private static void Images(string directory, HeightMap map, WaterMap water, WorldGenerationConfig config)
    {
        const int OVERVIEW = 2048;
        const int DETAIL = 4096;

        Directory.CreateDirectory(directory);

        Png.Write(Path.Combine(directory, "water_mask.png"), WaterDebugImages.Mask(water), water.Resolution, water.Resolution);
        Png.Write(Path.Combine(directory, "body_id.png"), WaterDebugImages.BodyIds(water), water.Resolution, water.Resolution);
        Png.Write(Path.Combine(directory, "overview.png"), WaterDebugImages.Overview(map, water, Array.Empty<TerrainStampPlacement>(), OVERVIEW), OVERVIEW, OVERVIEW);

        if (_valleys != null)
            Png.Write(Path.Combine(directory, "valleys.png"), ValleyImage(map, water, _valleys, OVERVIEW), OVERVIEW, OVERVIEW);
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

    internal static string DescribeEnds(WaterMap water, float startArea)
    {
        var starts = new Dictionary<RiverEnd, int>();
        var ends = new Dictionary<RiverEnd, int>();
        var loose = new List<string>();
        int springs = 0;

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;

            if (points.Count < 2)
                continue;

            RiverEnd start = RiverEnds.Start(water, river);
            RiverEnd end = RiverEnds.End(water, river);

            if (start == RiverEnd.Loose && RiverEnds.Headwater(water, river, startArea))
            {
                springs++;
                start = RiverEnd.River;
                starts[start] = starts.GetValueOrDefault(start);
            }
            else
                starts[start] = starts.GetValueOrDefault(start) + 1;
            ends[end] = ends.GetValueOrDefault(end) + 1;

            if (start == RiverEnd.Loose)
                loose.Add($"исток реки {river} ({points[0].Position.x:0}, {points[0].Position.y:0})");

            if (end == RiverEnd.Loose)
                loose.Add($"устье реки {river} ({points[^1].Position.x:0}, {points[^1].Position.y:0})");
        }

        string Line(Dictionary<RiverEnd, int> counts) => $"в озере или пруду {counts.GetValueOrDefault(RiverEnd.Standing)}, у края карты {counts.GetValueOrDefault(RiverEnd.Border)}, в другой реке {counts.GetValueOrDefault(RiverEnd.River)}, висят {counts.GetValueOrDefault(RiverEnd.Loose)}";

        return $"  истоки: {Line(starts)}\n  устья: {Line(ends)}" + (loose.Count == 0 ? "" : $"\n  висячие концы: {string.Join(", ", loose)}");
    }

    internal static int LooseEnds(WaterMap water, float startArea)
    {
        int loose = 0;

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            if (water.Rivers[river].Points.Count < 2)
                continue;

            loose += RiverEnds.Start(water, river) == RiverEnd.Loose && !RiverEnds.Headwater(water, river, startArea) ? 1 : 0;
            loose += RiverEnds.End(water, river) == RiverEnd.Loose ? 1 : 0;
        }

        return loose;
    }

    private static void ListRivers(WaterMap water)
    {
        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;

            if (points.Count == 0)
                continue;

            int open = points.FindAll(point => !point.Submerged).Count;
            Console.WriteLine($"  река {r}: {points.Count} точек ({open} открытых), {water.Rivers[r].Length:0} м, ({points[0].Position.x:0}, {points[0].Position.y:0}) {RiverEnds.Start(water, r)}/{water.Rivers[r].Source} sub0 {points[0].Submerged} → ({points[^1].Position.x:0}, {points[^1].Position.y:0}) {RiverEnds.End(water, r)}, ширина {points[0].Width:0}..{points[^1].Width:0} м, уровень {points[0].Surface:0.0} → {points[^1].Surface:0.0}");
        }
    }

    public static void Inspect(HeightMap map, WaterMap water, float x, float z)
    {
        float ground = map.SampleWorldSmooth(x, z);
        WaterSample s = water.Sample(x, z);
        int c = water.CellIndex(x, z);
        Console.WriteLine($"  uncarved {(water.Uncarved != null ? water.Uncarved.SampleWorldSmooth(x, z) : float.NaN):0.00}, filled {(water.Filled != null ? water.Filled[water.CellIndex(x, z)] : float.NaN):0.00}, bodies {string.Join(" ", System.Linq.Enumerable.Select(water.Bodies, (body, id) => $"{id}:{body.Kind}@{body.Surface:0.00}"))}");
        Console.WriteLine($"({x},{z}) ground {ground:0.00} sample {s.Kind} surface {s.Surface:0.00} width {s.Width:0.0} body {s.Body} | cell kind {(WaterKind)water.Kinds[c]} bodyId {water.BodyIds[c]} shoreDist {water.ShoreDistance[c]:0.0} shoreSurf {water.ShoreSurface[c]:0.00}");

        int detail = water.DetailStart(c);

        if (detail >= 0 && Environment.GetEnvironmentVariable("WATER_NODES") != null)
        {
            for (int j = WaterMap.CELL_SIDE_NODES - 1; j >= 0; j--)
            {
                var line = new System.Text.StringBuilder("   ");

                for (int i = 0; i < WaterMap.CELL_SIDE_NODES; i++)
                {
                    int node = detail + j * WaterMap.CELL_SIDE_NODES + i;
                    line.Append($" {water.DetailOwner(node),3}@{water.DetailGround(node):0.00}/{map.SampleWorldSmooth(c % water.Resolution * water.CellSize + i * water.NodeStep, c / water.Resolution * water.CellSize + j * water.NodeStep):0.00}");
                }

                Console.WriteLine(line.ToString());
            }
        }

        foreach (WaterCrossing crossing in water.Crossings)
        {
            if ((crossing.Position - new Vector2(x, z)).sqrMagnitude < 150f * 150f)
                Console.WriteLine($"  crossing {(crossing.Bridge ? "bridge" : "culvert")} at ({crossing.Position.x:0}, {crossing.Position.y:0}) span {crossing.Span:0.0} deck {crossing.DeckHeight:0.0} road {crossing.RoadWidth:0.0}");
        }

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
        var surface = new VoxelSurfaceProbe(field.Sampler, new VoxelStreamPlan(voxels, 6, 96f, map.WorldSize, water), viewer, voxels.ChunkSize);
        Console.WriteLine($"  бюджет земли без воды / с водой: {Geometry(voxels, map.WorldSize, viewer, null)} / {Geometry(voxels, map.WorldSize, viewer, water)}");

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

        long probes = 0, holes = 0, hanging = 0, zfight = 0, stacked = 0, shore = 0, lodFlood = 0, lodMissing = 0, cliffs = 0;
        float worstCliff = 0f;
        var floodByLod = new long[8];
        var missingByLod = new long[8];
        float worstHang = 0f;
        var marks = new ConcurrentBag<(Vector2 Point, byte R, byte G, byte B)>();
        var nearRings = new ConcurrentBag<Vector2>();
        object gate = new();

        Parallel.For(0, resolution, row =>
        {
            long p = 0, h = 0, hg = 0, zf = 0, st = 0, sh = 0, lf = 0, lm = 0, cf = 0;
            float hangMax = 0f, cliffMax = 0f;
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

                        if (covers.Count == 0 && !wet)
                        {
                            float edge = EdgeAbove(index, water, x, z) - ground;

                            if (edge > CLIFF)
                            {
                                cf++;
                                cliffMax = Mathf.Max(cliffMax, edge);
                                marks.Add((new Vector2(x, z), 0, 0, 255));

                                if (edge > 1f)
                                    result.Example("обрыв кромки выше 1 м", new Vector2(x, z));
                            }
                        }

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

                            if (surface.LodAt(x, z) <= NEAR_LOD)
                                nearRings.Add(new Vector2(x, z));
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
                cliffs += cf;
                worstCliff = Mathf.Max(worstCliff, cliffMax);

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
        result.Cliffs = cliffs;
        result.WorstCliff = worstCliff;
        result.LargestCliff = Largest(marks, 0, 0, 255);
        result.CliffPatches = DescribePatches(marks, 0, 0, 255, 6);

        result.LargestHole = Largest(marks, 255, 0, 0);
        result.LargestHang = Largest(marks, 255, 0, 255);
        result.LargestZFight = Largest(marks, 255, 255, 0);
        result.LargestStack = Largest(marks, 255, 140, 0);
        result.LargestNearLod = Largest(nearRings);

        foreach ((Vector2 point, byte r, byte g, byte b) in marks)
        {
            string kind = r == 255 && g == 0 && b == 0 ? "дыра" : r == 255 && b == 255 ? "висит" : r == 255 && g == 255 ? "z-fight"
                : r == 255 ? "двойная" : r == 0 && g == 0 && b == 255 ? "обрыв кромки" : "LOD";
            result.Example(kind, point);
        }

        Rivers(map, water, result, marks);

        if (image != null)
            Draw(image, map, water, marks);

        return result;
    }

    private static float Largest(ConcurrentBag<(Vector2 Point, byte R, byte G, byte B)> marks, byte r, byte g, byte b)
    {
        var points = new List<Vector2>();

        foreach ((Vector2 point, byte mr, byte mg, byte mb) in marks)
        {
            if (mr == r && mg == g && mb == b)
                points.Add(point);
        }

        return Largest(points);
    }

    private static float Largest(IEnumerable<Vector2> points)
    {
        float largest = 0f;

        foreach ((float area, Vector2 _) in Patches(points))
            largest = Mathf.Max(largest, area);

        return largest;
    }

    private static List<(float Area, Vector2 Center)> Patches(IEnumerable<Vector2> points)
    {
        var keys = new HashSet<long>();

        foreach (Vector2 point in points)
            keys.Add(Key(Mathf.RoundToInt(point.x / PROBE), Mathf.RoundToInt(point.y / PROBE)));

        var seen = new HashSet<long>();
        var queue = new Queue<long>();
        var patches = new List<(float Area, Vector2 Center)>();

        foreach (long start in keys)
        {
            if (!seen.Add(start))
                continue;

            int size = 0;
            Vector2 sum = Vector2.zero;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                long current = queue.Dequeue();
                size++;
                int x = (int)(current >> 32), z = (int)(uint)current;
                sum += new Vector2(x, z) * PROBE;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long next = Key(x + dx, z + dz);

                        if (keys.Contains(next) && seen.Add(next))
                            queue.Enqueue(next);
                    }
                }
            }

            patches.Add((size * PROBE * PROBE, sum / size));
        }

        patches.Sort((a, b) => b.Area.CompareTo(a.Area));
        return patches;
    }

    private static string DescribePatches(ConcurrentBag<(Vector2 Point, byte R, byte G, byte B)> marks, byte r, byte g, byte b, int count)
    {
        var points = new List<Vector2>();

        foreach ((Vector2 point, byte mr, byte mg, byte mb) in marks)
        {
            if (mr == r && mg == g && mb == b)
                points.Add(point);
        }

        var parts = new List<string>();

        foreach ((float area, Vector2 center) in Patches(points))
        {
            if (parts.Count == count)
                break;

            parts.Add($"({center.x:0}, {center.y:0}) {area:0} м²");
        }

        return string.Join(" ", parts);
    }

    private static long Key(int x, int z)
    {
        return ((long)x << 32) | (uint)z;
    }

    private const float STRICT_PATCH = 16f;
    private const float STRICT_HANG = 0.3f;
    private const float STRICT_NEAR_LOD = 32f;
    private const float STRICT_TRENCH = 8f;
    private const float STRICT_PLANE = 40f;
    private const float STRICT_RIVER_KM_PER_KM2 = 0.12f;
    private const float STRICT_RIVERS_PER_KM2 = 0.1f;
    private const float STRICT_BODIES_PER_KM2 = 0.15f;

    private static float WetArea(WaterMap water)
    {
        int wet = 0;

        foreach (bool dry in water.Dry)
        {
            if (!dry)
                wet++;
        }

        return wet * water.CellSize * water.CellSize / 1e6f;
    }

    public static bool Strict(WaterMap water, Result result, WaterShoreChecks.Result shore, RiverShapeAudit.Result shape, WaterContract contract)
    {
        float area = WetArea(water);
        int bodies = 0;

        foreach (WaterBody body in water.Bodies)
        {
            if (body.Area > 0f)
                bodies++;
        }

        var failures = new List<string>();

        void Limit(bool ok, string what)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");

            if (!ok)
                failures.Add(what);
        }

        int intoDry = 0;

        foreach (RiverPath river in water.Rivers)
        {
            if (river.Points.Count < 2 || river.Terminal == RiverTerminal.Sea || river.Terminal == RiverTerminal.Lake || river.Terminal == RiverTerminal.Junction)
                continue;

            Vector2 end = river.Points[^1].Position;

            if (water.DryNear(end.x, end.y, DRY_END_REACH))
                intoDry++;
        }

        Console.WriteLine($"строгая приёмка воды (земля вне сухих биомов {area:0.0} км²):");
        Limit(contract.Violations.Count == 0, $"граф рек без нарушений: {contract.Violations.Count}");
        Limit(intoDry == 0, $"рек, упирающихся в сухой биом: {intoDry}");
        Limit(result.WorstHang <= STRICT_HANG && result.LargestHang <= STRICT_PATCH, $"вода над сушей: до {result.WorstHang:0.00} м (<= {STRICT_HANG}), наибольшее пятно {result.LargestHang:0} м² (<= {STRICT_PATCH})");
        Limit(result.LargestCliff <= STRICT_PATCH, $"обрыв кромки воды выше {CLIFF} м: до {result.WorstCliff:0.00} м, наибольшее пятно {result.LargestCliff:0} м² (<= {STRICT_PATCH})");
        Limit(result.LargestHole <= STRICT_PATCH, $"дыра под уровнем воды: наибольшее пятно {result.LargestHole:0} м² (<= {STRICT_PATCH})");
        Limit(result.LargestZFight <= STRICT_PATCH && result.LargestStack <= STRICT_PATCH, $"z-fighting / двойная вода: пятна {result.LargestZFight:0} / {result.LargestStack:0} м² (<= {STRICT_PATCH})");
        Limit(result.LargestNearLod <= STRICT_NEAR_LOD, $"вода под землёй ближних колец L0–L{NEAR_LOD}: пятно {result.LargestNearLod:0} м² (<= {STRICT_NEAR_LOD})");
        Limit(result.Steps == 0, $"ступеньки уровня реки круче {STEP_GRADE:P0}: {result.Steps}");
        Limit(result.WorstTrench <= STRICT_TRENCH, $"траншея: до {result.WorstTrench:0.0} м (<= {STRICT_TRENCH})");
        Limit(shore.MouthGaps == 0, $"сухие разрывы в устьях: {shore.MouthGaps}");
        Limit(shore.TJunctions == 0 && shore.NonManifold == 0, $"T-стыки / лишние рёбра стоячей воды: {shore.TJunctions} / {shore.NonManifold}");
        Limit(shape.LongestPlane <= STRICT_PLANE, $"наклонная плоскость без плёсов: длиннейшая {shape.LongestPlane:0} м (<= {STRICT_PLANE})");
        Limit(shape.OpenLength / 1000f >= STRICT_RIVER_KM_PER_KM2 * area, $"открытых рек {shape.OpenLength / 1000f:0.0} км (>= {STRICT_RIVER_KM_PER_KM2 * area:0.0})");
        Limit(contract.Rivers >= STRICT_RIVERS_PER_KM2 * area, $"рек {contract.Rivers} (>= {STRICT_RIVERS_PER_KM2 * area:0})");
        Limit(bodies >= STRICT_BODIES_PER_KM2 * area, $"озёр и прудов {bodies} (>= {STRICT_BODIES_PER_KM2 * area:0})");
        Console.WriteLine(failures.Count == 0 ? "строгая приёмка: пройдена" : $"строгая приёмка: не пройдена, {failures.Count} пунктов");

        return failures.Count == 0;
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

                if (incision > TRENCH && !UnderBridge(water, point.Position))
                {
                    result.TrenchPoints++;
                    if (incision > result.WorstTrench)
                        result.WorstTrenchAt = point.Position;

                    result.WorstTrench = Mathf.Max(result.WorstTrench, incision);
                    result.Example("траншея", point.Position);
                    marks.Add((point.Position, 150, 80, 20));
                }
            }
        }
    }

    private static void ChannelDrift(HeightMap map, WaterMap water, float bridgeWidth, Result result)
    {
        var copy = new HeightMap(map.Resolution, map.WorldSize, map.MaxHeight);
        Array.Copy(map.Heights, copy.Heights, map.Heights.Length);
        RiverCarver.Restore(copy, water, bridgeWidth);

        float cell = (float)map.WorldSize / (map.Resolution - 1);
        var points = new List<Vector2>();
        float worst = 0f;

        for (int i = 0; i < copy.Heights.Length; i++)
        {
            float drop = (map.Heights[i] - copy.Heights[i]) * map.MaxHeight;

            if (drop <= DRIFT)
                continue;

            worst = Mathf.Max(worst, drop);
            points.Add(new Vector2(i % map.Resolution * cell, i / map.Resolution * cell));
        }

        result.DriftArea = points.Count * cell * cell;
        result.WorstDrift = worst;

        var parts = new List<string>();

        foreach ((float area, Vector2 center) in Patches(points))
        {
            result.LargestDrift = Mathf.Max(result.LargestDrift, area);

            if (parts.Count < 6)
                parts.Add($"({center.x:0}, {center.y:0}) {area:0} м²");
        }

        result.DriftPatches = string.Join(" ", parts);
    }

    private static float EdgeAbove(Dictionary<long, List<Tri>> index, WaterMap water, float x, float z)
    {
        float top = float.NegativeInfinity;

        foreach ((float ox, float oz) in new[] { (PROBE, 0f), (-PROBE, 0f), (0f, PROBE), (0f, -PROBE) })
        {
            float px = x + ox, pz = z + oz;

            if (!index.TryGetValue(((long)Mathf.FloorToInt(px / INDEX_CELL) << 32) ^ (uint)Mathf.FloorToInt(pz / INDEX_CELL), out List<Tri> list))
                continue;

            foreach (Tri tri in list)
            {
                if (Inside(tri, px, pz, out float y))
                    top = Mathf.Max(top, tri.Source >= 0 && tri.Source < water.Rivers.Count ? Mathf.Min(y, RiverSurfaceAt(water.Rivers[tri.Source].Points, x, z)) : y);
            }
        }

        return top;
    }

    private static float RiverSurfaceAt(List<RiverPoint> points, float x, float z)
    {
        var point = new Vector2(x, z);
        float nearest = float.MaxValue, surface = float.PositiveInfinity;

        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 axis = points[i + 1].Position - points[i].Position;
            float length = axis.sqrMagnitude;
            float t = length < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - points[i].Position, axis) / length);
            float distance = (point - (points[i].Position + axis * t)).sqrMagnitude;

            if (distance >= nearest)
                continue;

            nearest = distance;
            surface = Mathf.Lerp(points[i].Surface, points[i + 1].Surface, t);
        }

        return surface;
    }

    private static bool UnderBridge(WaterMap water, Vector2 point)
    {
        foreach (WaterCrossing crossing in water.Crossings)
        {
            float reach = Mathf.Max(BRIDGE_APPROACH, crossing.Span);

            if (crossing.Bridge && (crossing.Position - point).sqrMagnitude <= reach * reach)
                return true;
        }

        return false;
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

        byte[] bytes = WaterMapFormat.Write(water);
        double encode = clock.Elapsed.TotalMilliseconds;
        clock.Restart();

        WaterMapFormat.Read(bytes);
        double decode = clock.Elapsed.TotalMilliseconds;
        clock.Restart();

        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        double meshes = clock.Elapsed.TotalMilliseconds;

        Console.WriteLine($"скорость: IsWater {isWater:0} нс, IsWet {isWet:0} нс, CrossesRiver {crosses:0} нс на вызов; "
            + $"ToBytes {encode:0} мс, FromBytes {decode:0} мс, меши {meshes:0} мс ({parts.Count} частей); контроль {hits}");
    }
}
