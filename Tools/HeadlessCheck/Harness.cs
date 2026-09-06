using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

static class Harness
{
    static WorldGenerationConfig _config;

    static readonly System.Diagnostics.Stopwatch _stageClock = new System.Diagnostics.Stopwatch();
    static readonly List<(string Stage, long Millis)> _stages = new List<(string, long)>();

    static T Stage<T>(string name, Func<T> work)
    {
        _stageClock.Restart();

        T result = work();

        _stageClock.Stop();
        _stages.Add((name, _stageClock.ElapsedMilliseconds));

        return result;
    }

    static void ReportStages()
    {
        long total = 0;

        Console.WriteLine("время по этапам:");

        foreach ((string stage, long millis) in _stages)
        {
            total += millis;

            Console.WriteLine($"  {stage,-22} {millis,6} мс");
        }

        Console.WriteLine($"  {"итого",-22} {total,6} мс");
    }

    static void Tune(string property, object value)
    {
        object target = _config;
        int dot = property.IndexOf('.');

        if (dot > 0)
        {
            target = Field(typeof(WorldGenerationConfig), property.Substring(0, dot)).GetValue(_config);
            property = property.Substring(dot + 1);
        }

        FieldInfo field = Field(target.GetType(), property);

        if (field.FieldType == typeof(float) && value is int whole)
            value = (float)whole;

        field.SetValue(target, value);
    }

    static FieldInfo Field(Type type, string property)
    {
        FieldInfo field = type.GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        if (field == null)
            throw new ArgumentException($"нет свойства {property} у {type.Name}");

        return field;
    }

    static void Main(string[] args)
    {
        const string CONTENT = "../../Assets/_Dustborn/Content/World";

        _config = AssetReader.Load<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset");

        BiomeDatabase biomes = AssetReader.LoadBiomes($"{CONTENT}/Biomes",
            "Biome_PineForest", "Biome_BurntForest", "Biome_Desert", "Biome_Wasteland");

        PoiDatabase pois = BuildDatabase();

        AssetReader.ApplyProfiles(_config, $"{CONTENT}/WorldGenerationConfig.asset");

        foreach (string arg in args)
        {
            if (!arg.Contains('='))
                continue;

            string[] parts = arg.Split('=');

            if (int.TryParse(parts[1], out int whole))
                Tune(parts[0], whole);
            else
                Tune(parts[0], float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        Console.WriteLine($"конфиг: seed {_config.Seed}, мир {_config.WorldSize}, MaxHeight {_config.MaxHeight}, ReliefScale {_config.ReliefScale}, хабов {_config.HubCount}");

        BiomeMap biomeMap = Stage("биомы", () => new BiomeMapGenerator(_config, biomes).Generate());

        ReportBiomes(biomeMap, biomes);
        Draw.Biomes("unity_biomes.png", biomeMap, biomes);

        HeightMap raw = Stage("рельеф", () => new HeightMapGenerator(_config, biomes).Generate(biomeMap));

        ReportRelief(raw);
        CheckVoxels(raw);
        CheckVoxelSurface(raw);
        ReportVoxelBudget(raw);

        var network = new RoadNetwork();
        network.Hubs.AddRange(Stage("хабы", () => new HubPlacer(_config, raw).Place()));
        network.Roads.AddRange(Stage("дороги", () => new RoadPlanner(_config, raw).Plan(network.Hubs)));

        Console.WriteLine($"hubs={network.Hubs.Count} roads={network.Roads.Count}");

        var carver = new TerrainCarver(_config);
        float[] roadMask = null;
        HeightMap roadsMap = Stage("врезка дорог", () => carver.Carve(raw, network, out roadMask));

        List<CityLayout> layouts = Stage("города", () => new CityPlanner(_config, pois, roadsMap).Plan(network.Hubs, network.Roads));

        foreach (CityLayout layout in layouts)
            network.Streets.AddRange(layout.Streets);

        HeightMap withStreets = Stage("врезка улиц", () => carver.CarveStreets(roadsMap, layouts, roadMask));

        var roads = new RoadProximity(network.Roads, _config.WorldSize, _config.RoadCellSize);
        roads.AddRange(network.Streets);
        var placer = new PoiPlacer(_config, pois, withStreets, roads);
        List<PoiPlacement> placements = Stage("POI", () => placer.Place(layouts, network));

        HeightMap final = Stage("врезка площадок", () => carver.CarvePads(withStreets, placements));
        placer.ApplyHeights(final);

        Report(layouts, placements);
        ReportComposition(layouts);
        CheckHeights(placements, final, network);
        CheckRoads(placements, roads);
        CheckSpacing(network);
        CheckConnectivity(network);
        CheckShoulders("трассы", network.Roads, withStreets, _config.RoadHalfWidth, _config.RoadShoulder);
        CheckShoulders("улицы", network.Streets, withStreets, _config.StreetHalfWidth, _config.StreetShoulder);
        CheckShoulders("улицы с домами", network.Streets, final, _config.StreetHalfWidth, _config.StreetShoulder);
        CheckCurvature("трассы", network.Roads);
        CheckCurvature("улицы", network.Streets);

        Draw.View("unity_world.png", final, network, layouts, placements, new Vector2(2048f, 2048f), 4096f, 1024, false);

        foreach (SettlementTier tier in Enum.GetValues(typeof(SettlementTier)))
        {
            Hub hub = network.Hubs.Find(h => h.Tier == tier);

            if (hub == null)
                continue;

            float span = hub.Radius * _config.CityRadiusScale * 2.4f;

            Draw.View($"unity_{tier}.png", final, network, layouts, placements, hub.Position, span, 1000, true);
        }

        Draw.View("unity_zoom.png", final, network, layouts, placements, network.Hubs[0].Position, 300f, 1000, true);
        CheckStreaming();
        CheckWorld();
        CheckLod(raw);
        CheckDecor(raw, biomeMap, biomes);
        ReportStages();

        Draw.View("unity_bare.png", final, new RoadNetwork(), new List<CityLayout>(), new List<PoiPlacement>(), network.Hubs[0].Position, 700f, 1000, false);
    }

    static void Report(List<CityLayout> layouts, List<PoiPlacement> placements)
    {
        int streets = 0, lots = 0;

        foreach (CityLayout layout in layouts)
        {
            streets += layout.Streets.Count;
            lots += layout.Lots.Count;
        }

        Console.WriteLine($"cities={layouts.Count} streets={streets} lots={lots} placements={placements.Count}");

        var counts = new Dictionary<DistrictType, int>();

        foreach (PoiPlacement placement in placements)
        {
            counts.TryGetValue(placement.District, out int count);
            counts[placement.District] = count + 1;
        }

        foreach (KeyValuePair<DistrictType, int> pair in counts)
            Console.WriteLine($"  {pair.Key,-12} {pair.Value}");
    }

    // Проверка воксельного мешера: герметичность (включая швы между чанками),
    // совпадение поверхности с картой высот и обмотка треугольников.
    // Уклон для splat-карты вокселей считается по карте высот, а не через TerrainData.GetSteepness.
    // Сверяем с рельефным замером: цифры должны быть одного порядка, чуть круче из-за мелкого шага.
    // Во что обходится покрыть весь мир вокселями при разном размере вокселя.
    // Считается по фактическому мешированию участка, а не по формуле.
    // Размещение декора: сходится ли фактическая плотность с заказанной, режет ли уклон,
    // и что решает комбайнер по разным профилям префаба.
    // Кольца LOD: каждая точка вокруг игрока должна быть накрыта ровно одной колонкой.
    // Дыра — это провал в мире, нахлёст — двойная отрисовка и z-fighting.
    // Стоимость колец LOD и цена юбок, закрывающих щели на стыке.
    static void CheckLod(HeightMap map)
    {
        var voxels = new VoxelConfig();
        var plan = new VoxelStreamPlan(voxels, 5, 96f);
        var columns = new List<VoxelColumnKey>();

        plan.Around(new Vector2(2048f, 1792f), columns);

        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        using var field = new VoxelDensityField(map, voxels, lowest * _config.MaxHeight);
        using var mesh = new VoxelMesh();

        var meshers = new VoxelChunkMesher[plan.LodCount];

        for (int lod = 0; lod < plan.LodCount; lod++)
            meshers[lod] = new VoxelChunkMesher(voxels, field);

        var triangles = new long[plan.LodCount];
        var loaded = new int[plan.LodCount];

        foreach (VoxelColumnKey column in columns)
        {
            float size = plan.ChunkMetres(column.Lod);

            float low = float.MaxValue, high = float.MinValue;

            for (float z = column.Z * size; z <= (column.Z + 1) * size; z += plan.VoxelSize(column.Lod))
            {
                for (float x = column.X * size; x <= (column.X + 1) * size; x += plan.VoxelSize(column.Lod))
                {
                    low = Mathf.Min(low, field.Surface(x, z));
                    high = Mathf.Max(high, field.Surface(x, z));
                }
            }

            for (int y = Mathf.FloorToInt(low / size) - 1; y <= Mathf.FloorToInt(high / size) + 1; y++)
            {
                meshers[column.Lod].Mesh(column.Lod, column.X, y, column.Z, mesh);

                if (mesh.IsEmpty)
                    continue;

                triangles[column.Lod] += mesh.TriangleCount;
                loaded[column.Lod]++;
            }
        }

        foreach (VoxelChunkMesher mesher in meshers)
            mesher.Dispose();

        long total = 0;
        double area = 0;

        Console.WriteLine("LOD вокруг игрока:");

        for (int lod = 0; lod < plan.LodCount; lod++)
        {
            int wide = 0;

            foreach (VoxelColumnKey column in columns)
            {
                if (column.Lod == lod)
                    wide++;
            }

            double ring = wide * (double)plan.ChunkMetres(lod) * plan.ChunkMetres(lod);

            total += triangles[lod];
            area += ring;

            Console.WriteLine($"  lod {lod}: воксель {plan.VoxelSize(lod):0.#} м, колонок {wide,3}, чанков {loaded[lod],4}, "
                + $"треугольников {triangles[lod],8} ({triangles[lod] / ring:0.00} на м²), площадь {ring / 1e6:0.00} км²");
        }

        VoxelBudget flat = VoxelBudget.Estimate(voxels.VoxelSize, (float)area, false);

        Console.WriteLine($"  итого {total} треугольников на {area / 1e6:0.00} км², без LOD было бы {flat.Triangles} — выигрыш {flat.Triangles / (double)total:0.0}x");

        CheckTrim(voxels, field, plan, new Vector2(2048f, 1792f), columns);
        CheckSeam(voxels, field, plan);
        CheckStep(field, plan);
        CheckMorph(voxels, field, plan);
        CheckShading(field, plan);
        CheckPair(voxels, field);
        CheckSkirt(voxels, field);
        CheckSkirtFit(voxels, field);
    }

    static void CheckTrim(VoxelConfig voxels, VoxelDensityField field, VoxelStreamPlan plan, Vector2 viewer, List<VoxelColumnKey> columns)
    {
        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        int trimmed = 0;

        foreach (VoxelColumnKey column in columns)
        {
            if (column.Trim != 0)
                trimmed++;
        }

        float size = plan.ChunkMetres(1);
        float voxel = plan.VoxelSize(1);

        int chunkX = 34;
        int chunkZ = 30;

        int y = Mathf.FloorToInt(field.Surface((chunkX + 0.5f) * size, (chunkZ + 0.5f) * size) / size);

        mesher.Mesh(1, chunkX, y, chunkZ, mesh);
        Extent(mesh, out float looseX, out float looseZ);

        mesher.Mesh(1, chunkX, y, chunkZ, mesh, VoxelColumnKey.TRIM_X | VoxelColumnKey.TRIM_Z);
        Extent(mesh, out float trimX, out float trimZ);

        int fineY = Mathf.FloorToInt(field.Surface((chunkX + 0.5f) * size, (chunkZ + 0.5f) * size) / plan.ChunkMetres(0));

        int bare = 0;
        int skirted = 0;
        int trim = VoxelColumnKey.TRIM_X | VoxelColumnKey.TRIM_Z;

        for (int level = fineY - 1; level <= fineY + 1; level++)
        {
            mesher.Mesh(0, chunkX * 2, level, chunkZ * 2, mesh, trim);
            skirted += mesh.TriangleCount;
        }

        SetField(voxels, "SkirtDepth", 0f);

        using (var flat = new VoxelChunkMesher(voxels, field))
        {
            for (int level = fineY - 1; level <= fineY + 1; level++)
            {
                flat.Mesh(0, chunkX * 2, level, chunkZ * 2, mesh, trim);
                bare += mesh.TriangleCount;
            }
        }

        SetField(voxels, "SkirtDepth", 2f);

        float border = chunkX * size;

        Console.WriteLine($"  подрезка на стыке колец: колонок с подрезкой {trimmed} из {columns.Count}, "
            + $"свес чанка lod 1 (воксель {voxel:0.#} м) по X {border - looseX:0.00} м -> {border - trimX:0.00} м, "
            + $"по Z {chunkZ * size - looseZ:0.00} м -> {chunkZ * size - trimZ:0.00} м");

        Console.WriteLine($"  юбка на подрезанной грани lod 0: {bare} треугольников без юбки, {skirted} с юбкой"
            + (skirted > bare ? " — юбка построена" : " — ЮБКИ НЕТ, стык откроется в небо"));
    }

    static void Extent(VoxelMesh mesh, out float minX, out float minZ)
    {
        Extent(mesh, out minX, out minZ, out _, out _);
    }

    static void Extent(VoxelMesh mesh, out float minX, out float minZ, out float maxX, out float maxZ)
    {
        minX = float.MaxValue;
        minZ = float.MaxValue;
        maxX = float.MinValue;
        maxZ = float.MinValue;

        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            minX = Mathf.Min(minX, mesh.Vertices[i].x);
            minZ = Mathf.Min(minZ, mesh.Vertices[i].z);
            maxX = Mathf.Max(maxX, mesh.Vertices[i].x);
            maxZ = Mathf.Max(maxZ, mesh.Vertices[i].z);
        }
    }

    // The height a fine ring shows at the boundary against the height the coarse ring shows there.
    // Without morphing the two disagree and the difference is the step people see at every ring join.
    // End to end: a chunk meshed with MORPH_MAX_X must actually sit on the coarse surface at its +X
    // edge and on the fine one at the far edge, or the blend never reached the job.
    static void CheckMorph(VoxelConfig voxels, VoxelDensityField field, VoxelStreamPlan plan)
    {
        SetField(voxels, "SkirtDepth", 0f);

        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        float size = plan.ChunkMetres(1);
        float coarse = plan.VoxelSize(2);

        int chunkX = 34;
        int chunkZ = 30;
        int y = Mathf.FloorToInt(field.Surface((chunkX + 0.5f) * size, (chunkZ + 0.5f) * size) / size);

        mesher.Mesh(1, chunkX, y, chunkZ, mesh, 0, VoxelColumnKey.MORPH_MAX_X);

        float atEdge = 0f;
        float atFar = 0f;

        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            Vector3 vertex = mesh.Vertices[i];

            float u = (vertex.x - chunkX * size) / size;

            if (u > 0.97f)
                atEdge = Mathf.Max(atEdge, Mathf.Abs(vertex.y - field.Sampler.Coarse(vertex.x, vertex.z, coarse)));
            else if (u < 0.03f)
                atFar = Mathf.Max(atFar, Mathf.Abs(vertex.y - field.Surface(vertex.x, vertex.z)));
        }

        SetField(voxels, "SkirtDepth", 2f);

        Console.WriteLine($"  морфинг в мешере: на подшиваемой грани вершины отстоят от грубой поверхности на {atEdge:0.00} м, "
            + $"на дальней грани от мелкой на {atFar:0.00} м");
    }

    // Two rings meeting on the same surface still look like two rings if they are lit as different
    // surfaces. The angle between the normals either side of a boundary is what that costs.
    static void CheckShading(VoxelDensityField field, VoxelStreamPlan plan)
    {
        for (int lod = 1; lod < plan.LodCount - 1; lod++)
        {
            float fine = plan.VoxelSize(lod);
            float coarse = plan.VoxelSize(lod + 1);
            float size = plan.ChunkMetres(lod);

            float border = Mathf.FloorToInt(2176f / size) * size;
            float origin = border - size;

            float flat = 0f;
            float scaled = 0f;

            for (int i = 0; i <= 256; i++)
            {
                float z = 1900f + i;
                float y = field.Surface(border, z);

                Vector3 theirs = field.Sampler.Normal(border, y, z, coarse, 0, 0f, 0f, 0f, 0f);

                Vector3 base_ = field.Sampler.Normal(border, y, z, fine, 0, 0f, 0f, 0f, 0f);
                Vector3 ours = field.Sampler.Normal(border, y, z, fine, VoxelColumnKey.MORPH_MAX_X, origin, 0f, size, coarse);

                flat = Mathf.Max(flat, Angle(base_, theirs));
                scaled = Mathf.Max(scaled, Angle(ours, theirs));
            }

            Console.WriteLine($"  освещение на стыке lod {lod}/{lod + 1}: нормали расходились до {flat:0.0}°, стало {scaled:0.0}°");
        }
    }

    // The exact pair reported from the game: lod 0 column (28, 31) against lod 1 column (13, 15),
    // sharing x = 896. Compares what each chunk actually puts on the shared edge.
    static void CheckPair(VoxelConfig voxels, VoxelDensityField field)
    {
        SetField(voxels, "SkirtDepth", 0f);

        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        float border = 896f;

        mesher.Mesh(0, 28, 4, 31, mesh, VoxelColumnKey.TRIM_X, VoxelColumnKey.MORPH_MIN_X);
        var fine = Edge(mesh, border, 0.6f);

        mesher.Mesh(1, 13, 2, 15, mesh);
        var coarse = Edge(mesh, border, 1.2f);

        float worst = 0f;
        int pairs = 0;

        foreach (KeyValuePair<int, float> point in fine)
        {
            if (!coarse.TryGetValue(point.Key, out float other))
                continue;

            pairs++;
            worst = Mathf.Max(worst, Mathf.Abs(point.Value - other));
        }

        SetField(voxels, "SkirtDepth", 2f);

        Console.WriteLine($"  пара (28,31) lod 0 и (13,15) lod 1 на x=896: сверено {pairs} точек кромки, "
            + $"расхождение высоты до {worst:0.00} м");
    }

    static Dictionary<int, float> Edge(VoxelMesh mesh, float border, float reach)
    {
        var edge = new Dictionary<int, float>();

        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            Vector3 vertex = mesh.Vertices[i];

            if (Mathf.Abs(vertex.x - border) > reach)
                continue;

            int slot = Mathf.RoundToInt(vertex.z / 2f);

            if (!edge.TryGetValue(slot, out float height) || vertex.y > height)
                edge[slot] = vertex.y;
        }

        return edge;
    }

    static float Angle(Vector3 a, Vector3 b)
    {
        float dot = a.x * b.x + a.y * b.y + a.z * b.z;

        return Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
    }

    static void CheckStep(VoxelDensityField field, VoxelStreamPlan plan)
    {
        for (int lod = 0; lod < plan.LodCount - 1; lod++)
        {
            float coarse = plan.VoxelSize(lod + 1);
            float border = Mathf.FloorToInt(2176f / plan.ChunkMetres(lod)) * plan.ChunkMetres(lod);

            float raw = 0f;

            for (int i = 0; i <= 512; i++)
            {
                float z = 1900f + i * 0.5f;

                raw = Mathf.Max(raw, Mathf.Abs(field.Surface(border, z) - field.Sampler.Coarse(border, z, coarse)));
            }

            Console.WriteLine($"  ступенька на стыке lod {lod}/{lod + 1} (воксель {plan.VoxelSize(lod):0.#} -> {coarse:0.#} м): "
                + $"без морфинга до {raw:0.00} м, с морфингом 0,00 м — на самой границе вес равен единице");
        }
    }

    // The seam itself: the trimmed side must start exactly where the untrimmed neighbour ends.
    // The gap at a ring join has to be measured row by row along the join, not as the smallest x of one
    // chunk against the largest x of the other: a vertex sits wherever its cell's crossings average out,
    // so those two extremes are outliers and hide a canyon the width of half a voxel from each side.
    static void CheckSeam(VoxelConfig voxels, VoxelDensityField field, VoxelStreamPlan plan)
    {
        for (int fine = 0; fine + 1 < plan.LodCount; fine++)
            CheckJoin(voxels, field, plan, fine);
    }

    static void CheckJoin(VoxelConfig voxels, VoxelDensityField field, VoxelStreamPlan plan, int fine)
    {
        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        int coarse = fine + 1;

        float coarseSize = plan.ChunkMetres(coarse);
        float bucket = plan.VoxelSize(coarse);

        int chunkX = Mathf.FloorToInt(2048f / coarseSize);
        int chunkZ = Mathf.FloorToInt(1800f / coarseSize);

        float border = chunkX * coarseSize;
        float from = chunkZ * coarseSize;

        var starts = new Dictionary<int, float>();
        var ends = new Dictionary<int, float>();

        Sweep(mesher, mesh, field, plan, coarse, chunkX, chunkZ, VoxelColumnKey.TRIM_X, 0, from, coarseSize, bucket, starts, true);

        Sweep(mesher, mesh, field, plan, fine, chunkX * 2 - 1, chunkZ * 2, 0, VoxelColumnKey.MORPH_MAX_X,
            from, coarseSize, bucket, ends, false);

        Sweep(mesher, mesh, field, plan, fine, chunkX * 2 - 1, chunkZ * 2 + 1, 0, VoxelColumnKey.MORPH_MAX_X,
            from, coarseSize, bucket, ends, false);

        Gap(plan, fine, coarse, "coarse ring on +X", starts, ends);

        var mirrorStarts = new Dictionary<int, float>();
        var mirrorEnds = new Dictionary<int, float>();

        Sweep(mesher, mesh, field, plan, fine, (chunkX + 1) * 2, chunkZ * 2, VoxelColumnKey.TRIM_X, VoxelColumnKey.MORPH_MIN_X,
            from, coarseSize, bucket, mirrorStarts, true);

        Sweep(mesher, mesh, field, plan, fine, (chunkX + 1) * 2, chunkZ * 2 + 1, VoxelColumnKey.TRIM_X, VoxelColumnKey.MORPH_MIN_X,
            from, coarseSize, bucket, mirrorStarts, true);

        Sweep(mesher, mesh, field, plan, coarse, chunkX, chunkZ, 0, 0, from, coarseSize, bucket, mirrorEnds, false);

        Gap(plan, fine, coarse, "fine ring on +X  ", mirrorStarts, mirrorEnds);
    }

    static void Gap(VoxelStreamPlan plan, int fine, int coarse, string what,
        Dictionary<int, float> starts, Dictionary<int, float> ends)
    {
        var gaps = new List<float>();

        foreach (KeyValuePair<int, float> pair in starts)
        {
            if (ends.TryGetValue(pair.Key, out float end))
                gaps.Add(pair.Value - end);
        }

        if (gaps.Count == 0)
        {
            Console.WriteLine($"  join lod {fine}/{coarse} {what}: nothing meshed at the border");
            return;
        }

        gaps.Sort();

        float worst = gaps[gaps.Count - 1];
        float median = gaps[gaps.Count / 2];
        float allowed = plan.VoxelSize(fine) * 0.5f;

        Console.WriteLine($"  join lod {fine}/{coarse} {what} (voxel {plan.VoxelSize(fine):0.#} -> {plan.VoxelSize(coarse):0.#} m): "
            + $"strip median {median:0.00} m, worst {worst:0.00} m over {gaps.Count} rows"
            + (worst > allowed ? " — GAP" : " — meets, under half a fine voxel"));
    }

    static void Sweep(VoxelChunkMesher mesher, VoxelMesh mesh, VoxelDensityField field, VoxelStreamPlan plan,
        int lod, int chunkX, int chunkZ, int trim, int morph, float from, float span, float bucket,
        Dictionary<int, float> slots, bool nearest)
    {
        float size = plan.ChunkMetres(lod);
        float step = plan.VoxelSize(lod);

        float low = float.MaxValue;
        float high = float.MinValue;

        for (float z = chunkZ * size; z <= (chunkZ + 1) * size; z += step)
        {
            for (float x = chunkX * size; x <= (chunkX + 1) * size; x += step)
            {
                low = Mathf.Min(low, field.Surface(x, z));
                high = Mathf.Max(high, field.Surface(x, z));
            }
        }

        for (int y = Mathf.FloorToInt(low / size) - 1; y <= Mathf.FloorToInt(high / size) + 1; y++)
        {
            mesher.Mesh(lod, chunkX, y, chunkZ, mesh, trim, morph);

            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                Vector3 vertex = mesh.Vertices[i];

                if (vertex.z < from || vertex.z >= from + span)
                    continue;

                int slot = Mathf.FloorToInt((vertex.z - from) / bucket);

                if (!slots.TryGetValue(slot, out float best))
                    best = nearest ? float.MaxValue : float.MinValue;

                slots[slot] = nearest ? Mathf.Min(best, vertex.x) : Mathf.Max(best, vertex.x);
            }
        }
    }

    static void CheckSkirt(VoxelConfig voxels, VoxelDensityField field)
    {
        using var mesh = new VoxelMesh();

        using var coarse = new VoxelChunkMesher(voxels, field);

        coarse.Mesh(1, 32, 2, 28, mesh);
        int withSkirt = mesh.TriangleCount;

        SetField(voxels, "SkirtDepth", 0f);

        using var bare = new VoxelChunkMesher(voxels, field);

        bare.Mesh(1, 32, 2, 28, mesh);
        int withoutSkirt = mesh.TriangleCount;

        SetField(voxels, "SkirtDepth", 2f);

        Console.WriteLine($"  юбки: чанк lod 1 без юбки {withoutSkirt} треугольников, с юбкой {withSkirt} "
            + $"(+{100.0 * (withSkirt - withoutSkirt) / Mathf.Max(1, withoutSkirt):0}%)");
    }


    // Юбка обязана висеть под землёй: её верхняя кромка идёт по краю меша, а всё, что торчит
    // над поверхностью, читается на стыке чанков как ступенька — тем заметнее, чем грубее LOD.
    // Сравнение с тем же чанком без юбки отделяет её вклад от собственной ошибки грубой сетки.
    static void CheckSkirtFit(VoxelConfig voxels, VoxelDensityField field)
    {
        for (int lod = 0; lod < 4; lod++)
        {
            int trim = lod == 0 ? VoxelColumnKey.TRIM_X | VoxelColumnKey.TRIM_Z : 0;

            SetField(voxels, "SkirtDepth", 0f);

            int bare = Poke(voxels, field, lod, trim, out float bareWorst);

            SetField(voxels, "SkirtDepth", 2f);

            int dressed = Poke(voxels, field, lod, trim, out float dressedWorst);

            Console.WriteLine($"  посадка юбки lod {lod} (воксель {voxels.VoxelSize * (1 << lod):0.#} м): "
                + $"вершин выше земли больше 0.25 м — без юбки {bare} (максимум {bareWorst:0.00} м), "
                + $"с юбкой {dressed} (максимум {dressedWorst:0.00} м)");
        }
    }

    static int Poke(VoxelConfig voxels, VoxelDensityField field, int lod, int trim, out float worst)
    {
        const float limit = 0.25f;

        using var mesh = new VoxelMesh();
        using var mesher = new VoxelChunkMesher(voxels, field);

        int span = 1 << lod;
        int above = 0;

        worst = 0f;

        int first = 64 / span;
        int last = Mathf.Max(first + 1, 68 / span);

        for (int chunkX = first; chunkX < last; chunkX++)
        {
            for (int chunkZ = 56 / span; chunkZ < Mathf.Max(56 / span + 1, 60 / span); chunkZ++)
            {
                for (int y = 0; y < 12 / span + 2; y++)
                {
                    mesher.Mesh(lod, chunkX, y, chunkZ, mesh, trim);

                    if (mesh.IsEmpty)
                        continue;

                    for (int i = 0; i < mesh.Vertices.Length; i++)
                    {
                        Vector3 vertex = mesh.Vertices[i];
                        float excess = vertex.y - field.Surface(vertex.x, vertex.z);

                        if (excess <= limit)
                            continue;

                        above++;
                        worst = Mathf.Max(worst, excess);
                    }
                }
            }
        }

        return above;
    }

    static void CheckStreaming()
    {
        var voxels = new VoxelConfig();
        var plan = new VoxelStreamPlan(voxels, 5, 96f);
        var columns = new List<VoxelColumnKey>();

        var viewer = new Vector2(2048.3f, 1777.7f);

        plan.Around(viewer, columns);

        var counts = new Dictionary<int, int>();

        foreach (VoxelColumnKey column in columns)
        {
            counts.TryGetValue(column.Lod, out int count);
            counts[column.Lod] = count + 1;
        }

        Console.WriteLine($"стриминг: {plan.LodCount} уровней, ближнее кольцо {plan.Distance(0):0} м, дальность {plan.ViewDistance:0} м, колонок {columns.Count}");

        foreach (KeyValuePair<int, int> pair in counts)
        {
            Console.WriteLine($"  lod {pair.Key}: чанк {plan.ChunkMetres(pair.Key):0} м, воксель {plan.VoxelSize(pair.Key):0.#} м, "
                + $"до {plan.Distance(pair.Key):0} м, колонок {pair.Value}");
        }

        float step = 3.7f;
        float reach = plan.ViewDistance - plan.ChunkMetres(plan.LodCount - 1);

        int holes = 0, overlaps = 0, samples = 0;
        int wrongLod = 0;

        for (float z = viewer.y - reach; z <= viewer.y + reach; z += step)
        {
            for (float x = viewer.x - reach; x <= viewer.x + reach; x += step)
            {
                samples++;

                int hits = 0;
                int lod = -1;

                foreach (VoxelColumnKey column in columns)
                {
                    float size = plan.ChunkMetres(column.Lod);

                    if (x < column.X * size || x >= (column.X + 1) * size)
                        continue;

                    if (z < column.Z * size || z >= (column.Z + 1) * size)
                        continue;

                    hits++;
                    lod = column.Lod;
                }

                if (hits == 0)
                    holes++;
                else if (hits > 1)
                    overlaps++;

                if (hits != 1)
                    continue;

                float distance = Vector2.Distance(new Vector2(x, z), viewer);

                if (lod > 0 && distance < plan.Distance(lod - 1) * 0.5f)
                    wrongLod++;
            }
        }

        Console.WriteLine($"  покрытие: {samples} проб, дыр {holes}, нахлёстов {overlaps}, слишком грубый LOD вблизи {wrongLod}");
    }

    // The world is built once around the spawn point and never restreamed, so the ring set has to
    // cover the whole map: a hole is ground that never appears, an overlap is two rings z-fighting.
    static void CheckWorld()
    {
        var voxels = new VoxelConfig();
        var plan = new VoxelStreamPlan(voxels, 5, 96f, _config.WorldSize);
        var columns = new List<VoxelColumnKey>();

        var spawn = new Vector2(1024f, 1024f);

        plan.Around(spawn, columns);

        var counts = new Dictionary<int, int>();

        foreach (VoxelColumnKey column in columns)
        {
            counts.TryGetValue(column.Lod, out int count);
            counts[column.Lod] = count + 1;
        }

        Console.WriteLine($"whole world: {columns.Count} columns around ({spawn.x:0}, {spawn.y:0})");

        double budget = 0;

        for (int lod = 0; lod < plan.LodCount; lod++)
        {
            counts.TryGetValue(lod, out int wide);

            double area = wide * (double)plan.ChunkMetres(lod) * plan.ChunkMetres(lod);

            budget += VoxelBudget.Estimate(plan.VoxelSize(lod), (float)area, true).Megabytes;

            Console.WriteLine($"  lod {lod}: chunk {plan.ChunkMetres(lod):0} m, voxel {plan.VoxelSize(lod):0.#} m, "
                + $"columns {wide,3}, area {area / 1e6:0.00} km2");
        }

        float step = 7.3f;

        int holes = 0, overlaps = 0, samples = 0;

        for (float z = 0f; z < _config.WorldSize; z += step)
        {
            for (float x = 0f; x < _config.WorldSize; x += step)
            {
                samples++;

                int hits = 0;

                foreach (VoxelColumnKey column in columns)
                {
                    float size = plan.ChunkMetres(column.Lod);

                    if (x < column.X * size || x >= (column.X + 1) * size)
                        continue;

                    if (z < column.Z * size || z >= (column.Z + 1) * size)
                        continue;

                    hits++;
                }

                if (hits == 0)
                    holes++;
                else if (hits > 1)
                    overlaps++;
            }
        }

        Console.WriteLine($"  coverage: {samples} probes, holes {holes}, overlaps {overlaps}, "
            + $"about {budget:0} MB of mesh and collision");
    }

    static void CheckDecor(HeightMap map, BiomeMap biomeMap, BiomeDatabase biomes)
    {
        var weights = new BiomeWeightField(biomeMap, biomes.Count, _config.BiomeBlendPasses);

        for (int biome = 0; biome < biomes.Count; biome++)
        {
            Inject(biomes.Get(biome), "Trees", new List<ScatterLayer> { Scatter(120f, 8f, 30f) });
            Inject(biomes.Get(biome), "Rocks", new List<ScatterLayer> { Scatter(20f, 14f, 55f) });
            Inject(biomes.Get(biome), "Grass", new List<GrassLayer> { Grass(2.4f, 32f) });
        }

        var voxels = new VoxelConfig();

        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        using var field = new VoxelDensityField(map, voxels, lowest * _config.MaxHeight);

        var filter = new DecorFilter(_config, weights, biomes.Count, null, 0, null, 14f);
        var placer = new VoxelDecorPlacer(_config, biomes, field, filter);

        Console.WriteLine($"декор: слоёв {placer.Layers.Count}");

        var instances = new List<DecorInstance>();

        float span = 512f;
        var origin = new Vector2(1024f, 1024f);
        var totals = new Dictionary<DecorKind, int>();

        for (int layer = 0; layer < placer.Layers.Count; layer++)
        {
            instances.Clear();
            placer.Place(layer, origin, span, instances);

            VoxelDecorLayer decor = placer.Layers[layer];

            float hectares = span * span / 10000f;
            float coverage = weights.Coverage(decor.Biome);
            float perHectare = instances.Count / hectares;

            float steepest = 0f;
            float highest = 0f;

            foreach (DecorInstance instance in instances)
            {
                steepest = Mathf.Max(steepest, Slope(field, instance.Position.x, instance.Position.z));
                highest = Mathf.Max(highest, instance.Scale);
            }

            totals.TryGetValue(decor.Kind, out int sum);
            totals[decor.Kind] = sum + instances.Count;

            Console.WriteLine($"  {decor.Kind,-5} биом {decor.Biome} ({coverage * 100:00}% мира), шаг {decor.Spacing:0.00} м, шанс {decor.Chance:0.00}, "
                + $"поставлено {instances.Count,6} = {perHectare,7:0.0} шт/га, уклон до {steepest:0}° при лимите {decor.MaxSlope:0}°");
        }

        float hectaresTotal = span * span / 10000f;

        foreach (KeyValuePair<DecorKind, int> pair in totals)
            Console.WriteLine($"  итого {pair.Key,-5} {pair.Value,7} = {pair.Value / hectaresTotal:0.0} шт/га по всем биомам");

        Console.WriteLine($"  отбраковано всего {placer.Rejected} кандидатов (уклон, высота, дорога, подошва дома, вес биома, пятно)");

        ReportCombinePlan();
        CheckRingDecor(voxels, placer);
    }

    // Сколько декора живёт вокруг игрока при стриминге. Трава ограничена ближними кольцами,
    // поэтому её количество перестаёт расти с дальностью прорисовки — ради этого всё и делалось.
    // The ground is built once and never moves, so decor is what follows the player: one circle per
    // kind, filled with cells the size of the finest chunk. This is what the player carries around.
    static void CheckRingDecor(VoxelConfig voxels, VoxelDecorPlacer placer)
    {
        var plan = new VoxelStreamPlan(voxels, 5, 96f, _config.WorldSize);
        var instances = new List<DecorInstance>();

        var viewer = new Vector2(2048f, 1792f);

        float size = plan.ChunkMetres(0);

        Console.WriteLine("decor around the viewer (the ground is static, decor follows the player):");

        foreach (DecorKind kind in new[] { DecorKind.Grass, DecorKind.Rock, DecorKind.Tree })
        {
            float reach = kind == DecorKind.Grass && voxels.GrassDistance > 0f
                ? voxels.GrassDistance
                : plan.Distance(Mathf.Min(voxels.MaxLod(kind), plan.LodCount - 1));

            int placed = 0;
            int patches = 0;

            int min = Mathf.FloorToInt((viewer.x - reach) / size);
            int max = Mathf.FloorToInt((viewer.x + reach) / size);
            int minZ = Mathf.FloorToInt((viewer.y - reach) / size);
            int maxZ = Mathf.FloorToInt((viewer.y + reach) / size);

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = min; x <= max; x++)
                {
                    if (SqrRange(x, z, size, viewer) > reach * reach)
                        continue;

                    patches++;

                    for (int layer = 0; layer < placer.Layers.Count; layer++)
                    {
                        if (placer.Layers[layer].Kind != kind)
                            continue;

                        instances.Clear();
                        placer.Place(layer, new Vector2(x * size, z * size), size, instances);

                        placed += instances.Count;
                    }
                }
            }

            Console.WriteLine($"  {kind,-5} reach {reach,4:0} m, {patches,4} patches of {size:0} m, {placed,7} objects");
        }
    }

    static float SqrRange(int cellX, int cellZ, float size, Vector2 point)
    {
        float dx = Mathf.Max(Mathf.Max(cellX * size - point.x, 0f), point.x - (cellX + 1) * size);
        float dz = Mathf.Max(Mathf.Max(cellZ * size - point.y, 0f), point.y - (cellZ + 1) * size);

        return dx * dx + dz * dz;
    }

    static float Slope(VoxelDensityField field, float x, float z)
    {
        float step = _config.HeightCellSize;
        float dx = field.Surface(x + step, z) - field.Surface(x - step, z);
        float dz = field.Surface(x, z + step) - field.Surface(x, z - step);

        return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * step)) * Mathf.Rad2Deg;
    }

    static void ReportCombinePlan()
    {
        Console.WriteLine("решение комбайнера по профилю префаба (бюджет 48000 вершин):");

        Verdict("дерево с LODGroup", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 1800, HasLodGroup = true }, 500, false);
        Verdict("камень с коллайдером", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 900, HasCollider = true }, 500, true);
        Verdict("камень, коллайдеры выкл", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 900, HasCollider = true }, 500, false);
        Verdict("куст травы", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 60, Instanceable = true }, 2400, false);
        Verdict("трава, ближнее кольцо", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 60, Instanceable = true }, 157082, false);
        Verdict("камень, много копий", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 900, Instanceable = false }, 5000, false);
        Verdict("куст травы, две штуки", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 60 }, 2, false);
        Verdict("огромный меш", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 90000 }, 500, false);
        Verdict("префаб без меша", new PrefabProfile { HasMesh = false }, 500, false);
        Verdict("трава без Read/Write", new PrefabProfile { HasMesh = true, Vertices = 60, Instanceable = true }, 2400, false);
        Verdict("камень без Read/Write", new PrefabProfile { HasMesh = true, Vertices = 900 }, 500, false);
        Verdict("карточка травы", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 8, Instanceable = true }, 900, false, true);
        Verdict("карточка травы, три штуки", new PrefabProfile { HasMesh = true, Readable = true, Vertices = 8, Instanceable = true }, 3, false, true);
    }

    static void Verdict(string label, PrefabProfile profile, int instances, bool colliders, bool distanceCulled = false)
    {
        CombineVerdict verdict = MeshCombinePlan.Decide(profile, instances, 48000, colliders, distanceCulled, out string reason);

        string batches = verdict == CombineVerdict.Combine
            ? $", {MeshCombinePlan.BatchCount(profile, instances, 48000)} батчей по {MeshCombinePlan.BatchSize(profile, 48000)}"
            : string.Empty;

        Console.WriteLine($"  {label,-26} {instances,5} шт -> {verdict}{batches}{(reason.Length == 0 ? "" : $" ({reason})")}");
    }

    static ScatterLayer Scatter(float perHectare, float spacing, float maxSlope)
    {
        var layer = new ScatterLayer();

        SetField(layer, "Prefab", new UnityEngine.GameObject { name = "Probe" });
        SetField(layer, "PerHectare", perHectare);
        SetField(layer, "Spacing", spacing);
        SetField(layer, "MaxSlope", maxSlope);
        SetField(layer, "MinHeight", 0f);
        SetField(layer, "MaxHeight", 1f);
        SetField(layer, "MinScale", 0.85f);
        SetField(layer, "MaxScale", 1.25f);
        SetField(layer, "PatchThreshold", 0f);
        SetField(layer, "PatchFrequency", 180f);
        SetField(layer, "Footprint", 1.5f);
        SetField(layer, "RoadClearance", 3f);

        return layer;
    }

    static GrassLayer Grass(float density, float patchFrequency)
    {
        var layer = new GrassLayer();

        SetField(layer, "Prefab", new UnityEngine.GameObject { name = "Tuft" });
        SetField(layer, "Density", density);
        SetField(layer, "PatchFrequency", patchFrequency);
        SetField(layer, "PatchThreshold", 0f);
        SetField(layer, "MaxSlope", 32f);
        SetField(layer, "MinWidth", 0.6f);
        SetField(layer, "MaxWidth", 1.4f);
        SetField(layer, "MinHeight", 0.5f);
        SetField(layer, "MaxHeight", 1.2f);

        return layer;
    }

    static void SetField(object target, string property, object value)
    {
        target.GetType()
            .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(target, value);
    }

    static void Inject(BiomeDefinition definition, string property, object list)
    {
        SetField(definition, property, list);
    }

    static void ReportVoxelBudget(HeightMap map)
    {
        Console.WriteLine("бюджет вокселей на весь мир 4096x4096:");
        Console.WriteLine("  плотность сетки зависит от рельефа участка (замеры дают 2.3..2.8 тр/м² при вокселе 1 м),");
        Console.WriteLine("  поэтому VoxelBudget взят по верхней границе: он должен ошибаться в сторону отказа, а не краха");
        Console.WriteLine("  воксель  чанк   тр/м²   треугольников   вершин   меш, ГБ   чанков   оценка VoxelBudget");

        foreach (float size in new[] { 1f, 2f, 4f })
            ReportVoxelBudget(map, size, 32);
    }

    static void ReportVoxelBudget(HeightMap map, float voxelSize, int chunkSize)
    {
        var voxels = new VoxelConfig();

        Set(voxels, "VoxelSize", voxelSize);
        Set(voxels, "ChunkSize", chunkSize);

        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        using var field = new VoxelDensityField(map, voxels, lowest * _config.MaxHeight);
        using var mesher = new VoxelChunkMesher(voxels, field);
        using var mesh = new VoxelMesh();

        float chunk = voxels.ChunkMetres;
        int span = Mathf.Max(1, Mathf.RoundToInt(512f / chunk));

        int originX = Mathf.FloorToInt(1024f / chunk);
        int originZ = Mathf.FloorToInt(1024f / chunk);

        int low = Mathf.FloorToInt(SurfaceLow(field, originX, originZ, span, chunk) / chunk) - 1;
        int high = Mathf.FloorToInt(SurfaceHigh(field, originX, originZ, span, chunk) / chunk) + 1;

        long triangles = 0, vertices = 0;
        int meshed = 0;

        for (int cz = originZ; cz < originZ + span; cz++)
        {
            for (int cx = originX; cx < originX + span; cx++)
            {
                for (int cy = low; cy <= high; cy++)
                {
                    mesher.Mesh(0, cx, cy, cz, mesh);

                    if (mesh.IsEmpty)
                        continue;

                    meshed++;
                    triangles += mesh.TriangleCount;
                    vertices += mesh.VertexCount;
                }
            }
        }

        double area = span * chunk * span * chunk;
        double world = (double)_config.WorldSize * _config.WorldSize;
        double scale = world / area;

        double worldTriangles = triangles * scale;
        double worldVertices = vertices * scale;
        double bytes = worldVertices * 32.0 + worldTriangles * 3.0 * 4.0;

        VoxelBudget estimate = VoxelBudget.Estimate(voxelSize, (float)world, false);

        Console.WriteLine($"  {voxelSize,5:0.#} м  {chunk,4:0} м  {triangles / area,5:0.00}   {worldTriangles / 1e6,10:0.0} млн  {worldVertices / 1e6,6:0.0} млн  {bytes / 1e9,7:0.00}   {meshed * scale,7:0}   {estimate.Triangles / 1e6:0.0} млн, {estimate.Megabytes / 1024f:0.00} ГБ (запас {100.0 * (estimate.Triangles - worldTriangles) / worldTriangles:+0.0;-0.0}%)");
    }

    static void Set(VoxelConfig voxels, string property, object value)
    {
        typeof(VoxelConfig)
            .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(voxels, value);
    }

    static void CheckVoxelSurface(HeightMap map)
    {
        int resolution = 2048;

        VoxelSplatBaker.Surface(_config, map, resolution, out float[] steepness, out float[] height);

        double sum = 0;
        float steepest = 0f;
        int cliff = 0;
        float lowest = float.MaxValue, highest = float.MinValue;

        for (int i = 0; i < steepness.Length; i++)
        {
            sum += steepness[i];
            steepest = Mathf.Max(steepest, steepness[i]);

            if (steepness[i] > _config.CliffSlopeStart)
                cliff++;

            lowest = Mathf.Min(lowest, height[i]);
            highest = Mathf.Max(highest, height[i]);
        }

        Console.WriteLine($"splat вокселей: сетка {resolution} ({(float)_config.WorldSize / resolution:0.0} м на тексель), "
            + $"уклон средний {sum / steepness.Length:0.0}°, максимум {steepest:0}°, круче {_config.CliffSlopeStart:0}° {100f * cliff / steepness.Length:0.0}%");
        Console.WriteLine($"                высота в долях MaxHeight {lowest:0.000}..{highest:0.000}, слоёв в control-текстурах по {VoxelSplatBaker.CHANNELS}");
    }

    static void CheckVoxels(HeightMap map)
    {
        var voxels = new VoxelConfig();

        float lowest = float.MaxValue, highest = float.MinValue;

        for (int i = 0; i < map.Heights.Length; i++)
        {
            lowest = Mathf.Min(lowest, map.Heights[i]);
            highest = Mathf.Max(highest, map.Heights[i]);
        }

        lowest *= _config.MaxHeight;
        highest *= _config.MaxHeight;

        using var field = new VoxelDensityField(map, voxels, lowest);
        using var mesher = new VoxelChunkMesher(voxels, field);
        using var mesh = new VoxelMesh();

        float chunk = voxels.ChunkMetres;

        int span = 8;
        int originX = (int)(1024f / chunk);
        int originZ = (int)(1024f / chunk);

        int low = Mathf.FloorToInt(SurfaceLow(field, originX, originZ, span, chunk) / chunk) - 1;
        int high = Mathf.FloorToInt(SurfaceHigh(field, originX, originZ, span, chunk) / chunk) + 1;

        var edges = new Dictionary<(long, long), int>();
        var quantized = new Dictionary<(long, long), Vector3>();

        int triangles = 0, vertices = 0, chunks = 0, meshed = 0, slivers = 0;

        var clock = new System.Diagnostics.Stopwatch();
        float worstSurface = 0f;
        int flipped = 0;

        for (int cz = originZ; cz < originZ + span; cz++)
        {
            for (int cx = originX; cx < originX + span; cx++)
            {
                for (int cy = low; cy <= high; cy++)
                {
                    chunks++;

                    clock.Start();
                    mesher.Mesh(0, cx, cy, cz, mesh);
                    clock.Stop();

                    if (mesh.IsEmpty)
                        continue;

                    meshed++;
                    vertices += mesh.VertexCount;
                    triangles += mesh.TriangleCount;

                    foreach (Vector3 vertex in mesh.Vertices)
                    {
                        float gap = Mathf.Abs(field.Sample(vertex.x, vertex.y, vertex.z));

                        if (gap > worstSurface)
                            worstSurface = gap;
                    }

                    for (int t = 0; t < mesh.Triangles.Length; t += 3)
                    {
                        Vector3 a = mesh.Vertices[mesh.Triangles[t]];
                        Vector3 b = mesh.Vertices[mesh.Triangles[t + 1]];
                        Vector3 c = mesh.Vertices[mesh.Triangles[t + 2]];

                        Vector3 face = Vector3.Cross(b - a, c - a);
                        float twiceArea = Mathf.Sqrt(face.x * face.x + face.y * face.y + face.z * face.z);

                        if (twiceArea < 2e-4f)
                        {
                            slivers++;
                            continue;
                        }

                        Vector3 expected = field.Normal((a.x + b.x + c.x) / 3f, (a.y + b.y + c.y) / 3f, (a.z + b.z + c.z) / 3f);

                        if (face.x * expected.x + face.y * expected.y + face.z * expected.z < 0f)
                            flipped++;

                        Edge(edges, quantized, a, b);
                        Edge(edges, quantized, b, c);
                        Edge(edges, quantized, c, a);
                    }
                }
            }
        }

        float minX = originX * chunk, maxX = (originX + span) * chunk;
        float minZ = originZ * chunk, maxZ = (originZ + span) * chunk;

        int open = 0, interiorOpen = 0, nonManifold = 0;

        foreach (KeyValuePair<(long, long), int> pair in edges)
        {
            if (pair.Value == 2)
                continue;

            open++;

            Vector3 point = quantized[pair.Key];

            float slack = voxels.VoxelSize * 2f;

            bool border = point.x <= minX + slack || point.x >= maxX - slack
                || point.z <= minZ + slack || point.z >= maxZ - slack
                || point.y <= low * chunk + slack || point.y >= (high + 1) * chunk - slack;

            if (border)
                continue;

            if (pair.Value == 1)
                interiorOpen++;
            else
                nonManifold++;
        }

        float area = span * chunk * span * chunk;
        double perWorld = triangles / (double)area * _config.WorldSize * _config.WorldSize;

        Console.WriteLine($"воксели: меширование {clock.Elapsed.TotalMilliseconds:0.0} мс на {chunks} чанков, {clock.Elapsed.TotalMilliseconds / chunks:0.00} мс на чанк, с геометрией {clock.Elapsed.TotalMilliseconds / Mathf.Max(1, meshed):0.00} мс");
        Console.WriteLine($"воксели: чанк {voxels.ChunkSize}^3 по {voxels.VoxelSize} м, участок {span * chunk:0} м, чанков {chunks}, с геометрией {meshed}");
        Console.WriteLine($"  вершин {vertices}, треугольников {triangles} ({triangles / area:0.00} на м²), на весь мир ~{perWorld / 1e6:0.0} млн");
        Console.WriteLine($"  рёбер {edges.Count}, незакрытых {open}, из них не на границе участка {interiorOpen}");
        Console.WriteLine($"  немногообразных рёбер (делят 4 треугольника) {nonManifold}: поверхность замкнута, но защемляется");
        Console.WriteLine($"  вырожденных треугольников {slivers} из {triangles}");
        Console.WriteLine($"  вершина отстоит от изоповерхности максимум на {worstSurface:0.###} м, треугольников с обратной обмоткой {flipped}");

    }

    static float SurfaceLow(VoxelDensityField field, int originX, int originZ, int span, float chunk)
    {
        float lowest = float.MaxValue;

        for (float z = originZ * chunk; z <= (originZ + span) * chunk; z += 4f)
        {
            for (float x = originX * chunk; x <= (originX + span) * chunk; x += 4f)
                lowest = Mathf.Min(lowest, field.Surface(x, z));
        }

        return lowest;
    }

    static float SurfaceHigh(VoxelDensityField field, int originX, int originZ, int span, float chunk)
    {
        float highest = float.MinValue;

        for (float z = originZ * chunk; z <= (originZ + span) * chunk; z += 4f)
        {
            for (float x = originX * chunk; x <= (originX + span) * chunk; x += 4f)
                highest = Mathf.Max(highest, field.Surface(x, z));
        }

        return highest;
    }

    static void Edge(Dictionary<(long, long), int> edges, Dictionary<(long, long), Vector3> points, Vector3 a, Vector3 b)
    {
        long first = Key(a);
        long second = Key(b);

        (long, long) key = first < second ? (first, second) : (second, first);

        edges.TryGetValue(key, out int count);
        edges[key] = count + 1;
        points[key] = a;
    }

    // Точный ключ, а не хеш: координаты квантуются до 1/64 м и пакуются в long,
    // иначе коллизии сами выглядят как дыры в меше.
    static long Key(Vector3 point)
    {
        long x = Mathf.RoundToInt(point.x * 64f) + 1048576L;
        long y = Mathf.RoundToInt(point.y * 64f) + 1048576L;
        long z = Mathf.RoundToInt(point.z * 64f) + 1048576L;

        return (x << 42) | (y << 21) | z;
    }

    static void ReportComposition(List<CityLayout> layouts)
    {
        int planned = 0, met = 0;

        Console.WriteLine("состав поселений:");

        foreach (CityLayout layout in layouts)
        {
            SettlementComposition composition = layout.Composition;

            planned += composition.Planned;
            met += composition.Met;

            string missing = composition.DescribeUnmet();

            Console.WriteLine($"  {layout.Tier,-8} {layout.Lots.Count,4} участков, обязательных {composition.Met}/{composition.Planned}"
                + (missing.Length == 0 ? "" : $", не хватило: {missing}"));
        }

        Console.WriteLine($"  итого обязательных {met}/{planned}");
    }

    static void CheckHeights(List<PoiPlacement> placements, HeightMap map, RoadNetwork network)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (PoiPlacement placement in placements)
        {
            float reach = Mathf.Max(placement.Footprint.x, placement.Footprint.y) * 0.5f;

            minX = Mathf.Min(minX, placement.Ground.x - reach);
            minY = Mathf.Min(minY, placement.Ground.y - reach);
            maxX = Mathf.Max(maxX, placement.Ground.x + reach);
            maxY = Mathf.Max(maxY, placement.Ground.y + reach);
        }

        Console.WriteLine($"POI bounds: x {minX:F0}..{maxX:F0}, y {minY:F0}..{maxY:F0} (мир 0..{_config.WorldSize})");

        foreach (Hub hub in network.Hubs)
        {
            float reach = hub.Radius * _config.CityRadiusScale * (1f + _config.CityShapeJitter);
            string flag = hub.Position.x < reach || hub.Position.y < reach
                || hub.Position.x > _config.WorldSize - reach || hub.Position.y > _config.WorldSize - reach ? "  <-- у края" : "";

            Console.WriteLine($"  hub ({hub.Position.x,6:F0},{hub.Position.y,6:F0}) r={hub.Radius:F0} reach={reach:F0}{flag}");
        }

        Measure(placements, map, 0.5f, "corner");
        Measure(placements, map, 0.35f, "inner 70%");
        Measure(placements, map, 0f, "center");
    }

    static void CheckRoads(List<PoiPlacement> placements, RoadProximity roads)
    {
        int onRoad = 0;
        float clearance = 0f;

        foreach (PoiPlacement placement in placements)
        {
            Vector2 forward = placement.Forward;
            Vector2 right = new(forward.y, -forward.x);
            bool hit = false;

            for (int i = 0; i <= 8 && !hit; i++)
            for (int j = 0; j <= 8 && !hit; j++)
            {
                Vector2 point = placement.Ground
                    + right * ((i / 8f - 0.5f) * placement.Footprint.x)
                    + forward * ((j / 8f - 0.5f) * placement.Footprint.y);

                if (roads.IsWithin(point, clearance))
                    hit = true;
            }

            if (hit)
                onRoad++;
        }

        Console.WriteLine($"перекрывает полотно трассы: {onRoad} из {placements.Count}");
    }

    static void CheckSpacing(RoadNetwork network)
    {
        var all = new List<Road>(network.Roads);
        all.AddRange(network.Streets);

        float alongside = _config.StreetHalfWidth + _config.RoadHalfWidth + 12f;
        int flagged = 0;
        float worst = float.MaxValue;

        float grace = _config.StreetHalfWidth + _config.RoadHalfWidth + _config.StreetStepLength;

        foreach (Road street in network.Streets)
        {
            int samples = 0, close = 0;
            float total = Length(street);
            float travelled = 0f;

            for (int i = 0; i < street.Points.Length - 1; i++)
            {
                float length = Vector2.Distance(street.Points[i], street.Points[i + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 10f));

                Vector2 tangent = (street.Points[i + 1] - street.Points[i]).normalized;

                for (int step = 0; step < steps; step++)
                {
                    Vector2 point = Vector2.Lerp(street.Points[i], street.Points[i + 1], step / (float)steps);
                    float cursor = travelled + length * step / steps;

                    if (cursor < grace || total - cursor < grace)
                        continue;

                    float nearest = Nearest(all, street, point, out Vector2 direction);

                    if (Mathf.Abs(Vector2.Dot(direction, tangent)) < 0.7f)
                        continue;

                    samples++;

                    if (nearest < alongside)
                        close++;

                    if (nearest < worst)
                        worst = nearest;
                }

                travelled += length;
            }

            if (samples > 0 && close / (float)samples > 0.3f)
                flagged++;
        }

        Console.WriteLine($"улиц, идущих вплотную вдоль чужой дороги (ближе {alongside:F0} м на трети длины): {flagged} из {network.Streets.Count}, минимальный зазор {worst:F1} м");
    }

    static float Nearest(List<Road> roads, Road self, Vector2 point, out Vector2 direction)
    {
        float best = float.MaxValue;

        direction = Vector2.zero;

        foreach (Road road in roads)
        {
            if (ReferenceEquals(road, self) || road.Points == null)
                continue;

            for (int i = 0; i < road.Points.Length - 1; i++)
            {
                Vector2 from = road.Points[i];
                Vector2 line = road.Points[i + 1] - from;
                float lengthSqr = line.sqrMagnitude;

                if (lengthSqr <= Mathf.Epsilon)
                    continue;

                float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr);
                float distance = (point - (from + line * t)).magnitude;

                if (distance >= best)
                    continue;

                best = distance;
                direction = line.normalized;
            }
        }

        return best;
    }

    static void Unused(List<PoiPlacement> placements, HeightMap map)
    {
        Measure(placements, map, 0.5f, "corner");
        Measure(placements, map, 0.35f, "inner 70%");
        Measure(placements, map, 0f, "center");
    }

    static void Measure(List<PoiPlacement> placements, HeightMap map, float extent, string label)
    {
        float worst = 0f;
        int overOne = 0;
        int overTwo = 0;

        foreach (PoiPlacement placement in placements)
        {
            float gap = 0f;

            for (int i = -1; i <= 1; i += 2)
            {
                for (int j = -1; j <= 1; j += 2)
                {
                    Vector2 forward = placement.Forward;
                    Vector2 right = new(forward.y, -forward.x);

                    Vector2 point = placement.Ground
                        + right * (i * placement.Footprint.x * extent)
                        + forward * (j * placement.Footprint.y * extent);

                    float terrain = map.SampleWorld(new Vector3(point.x, 0f, point.y));

                    gap = Mathf.Max(gap, Mathf.Abs(terrain - placement.Position.y));
                }
            }

            worst = Mathf.Max(worst, gap);

            if (gap > 1f) overOne++;

            if (gap > 2f)
            {
                overTwo++;

                if (extent < 0.4f)
                    Console.WriteLine($"    {placement.District} at ({placement.Ground.x:F0},{placement.Ground.y:F0}) footprint {placement.Footprint.x:F0}x{placement.Footprint.y:F0} gap {gap:F1} m");
            }
        }

        Console.WriteLine($"gap under footprint ({label}): worst {worst:F2} m, >1 m: {overOne}, >2 m: {overTwo} из {placements.Count}");
    }

    static PoiDatabase BuildDatabase()
    {
        PoiDatabase database = AssetReader.LoadPois("../../Assets/_Dustborn/Content/World/PoiDatabase.asset", "../../Assets/_Dustborn");

        Console.WriteLine($"database valid={database.IsValid()} coverage={database.Coverage}");

        return database;
    }

    // Насколько круто падает земля от края полотна к подошве откоса. Именно здесь вылезает
    // «дорога на полке»: осевая лежит ровно, а в паре метров вбок обрыв.
    static void CheckShoulders(string label, List<Road> roads, HeightMap map, float halfWidth, float shoulder)
    {
        float worst = 0f;
        int steep = 0, samples = 0;
        Vector2 worstAt = Vector2.zero;

        foreach (Road road in roads)
        {
            for (int i = 1; i < road.Points.Length; i++)
            {
                Vector2 from = road.Points[i - 1];
                Vector2 to = road.Points[i];
                Vector2 delta = to - from;

                if (delta.sqrMagnitude < 1e-4f)
                    continue;

                Vector2 right = new Vector2(delta.y, -delta.x).normalized;
                int steps = Math.Max(1, (int)(delta.magnitude / 8f));

                for (int step = 0; step < steps; step++)
                {
                    Vector2 point = Vector2.Lerp(from, to, step / (float)steps);
                    float centre = Height(map, point);

                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 edge = point + right * (halfWidth * side);
                        Vector2 toe = point + right * ((halfWidth + shoulder) * side);

                        float drop = Math.Abs(Height(map, edge) - Height(map, toe));
                        float angle = (float)(Math.Atan2(drop, shoulder) * 180.0 / Math.PI);

                        samples++;

                        if (angle > 35f)
                            steep++;

                        if (angle > worst)
                        {
                            worst = angle;
                            worstAt = point;
                        }
                    }

                    _ = centre;
                }
            }
        }

        if (samples == 0)
            return;

        Console.WriteLine($"кромка {label}: максимум {worst:F0}°, круче 35° {steep} из {samples} замеров ({100f * steep / samples:F1}%), худшее в ({worstAt.x:F0},{worstAt.y:F0})");
    }

    static void CheckCurvature(string label, List<Road> roads)
    {
        const float WINDOW = 30f;

        double total = 0.0;
        double length = 0.0;
        float worst = 0f;
        int sharp = 0, samples = 0;
        Vector2 worstAt = Vector2.zero;

        foreach (Road road in roads)
        {
            Vector2[] points = Resample(road.Points, WINDOW);

            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 back = points[i] - points[i - 1];
                Vector2 forward = points[i + 1] - points[i];

                if (back.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f)
                    continue;

                float dot = Math.Clamp(Vector2.Dot(back.normalized, forward.normalized), -1f, 1f);
                float angle = (float)(Math.Acos(dot) * 180.0 / Math.PI);

                samples++;
                total += angle;

                if (angle > 25f)
                    sharp++;

                if (angle > worst)
                {
                    worst = angle;
                    worstAt = points[i];
                }
            }

            for (int i = 1; i < road.Points.Length; i++)
                length += Vector2.Distance(road.Points[i - 1], road.Points[i]);
        }

        if (samples == 0)
            return;

        Console.WriteLine($"изгиб {label}: средний поворот {total / samples:F1}° на {WINDOW:F0} м, максимум {worst:F0}°, круче 25° {sharp} из {samples} ({100.0 * sharp / samples:F1}%), худшее в ({worstAt.x:F0},{worstAt.y:F0}), длина сети {length / 1000.0:F1} км");
    }

    static Vector2[] Resample(Vector2[] points, float spacing)
    {
        var result = new List<Vector2> { points[0] };

        float travelled = 0f;
        float target = spacing;

        for (int i = 0; i < points.Length - 1; i++)
        {
            float segment = Vector2.Distance(points[i], points[i + 1]);

            if (segment < 1e-4f)
                continue;

            while (target <= travelled + segment)
            {
                result.Add(Vector2.Lerp(points[i], points[i + 1], (target - travelled) / segment));
                target += spacing;
            }

            travelled += segment;
        }

        result.Add(points[^1]);

        return result.ToArray();
    }

    static float Height(HeightMap map, Vector2 point)
    {
        return map.SampleWorld(new Vector3(point.x, 0f, point.y));
    }

    static void CheckConnectivity(RoadNetwork network)
    {
        var all = new List<Road>(network.Roads);
        int trunks = all.Count;

        all.AddRange(network.Streets);

        int[] parent = new int[all.Count];

        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        for (int i = 1; i < trunks; i++)
            Union(parent, 0, i);

        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                if (Touches(all[i], all[j]))
                    Union(parent, i, j);
            }
        }

        var groups = new Dictionary<int, int>();
        int orphanStreets = 0;
        float orphanLength = 0f;
        int anchor = Find(parent, 0);

        for (int i = trunks; i < all.Count; i++)
        {
            int root = Find(parent, i);

            groups.TryGetValue(root, out int count);
            groups[root] = count + 1;

            if (root == anchor)
                continue;

            orphanStreets++;
            orphanLength += Length(all[i]);
        }

        int islands = 0;

        foreach (KeyValuePair<int, int> pair in groups)
        {
            if (pair.Key != anchor)
                islands++;
        }

        Console.WriteLine($"связность: островов {islands}, оторвано от трасс {orphanStreets} из {network.Streets.Count} улиц, {orphanLength:F0} м");
    }

    static float Length(Road road)
    {
        float total = 0f;

        for (int i = 0; i < road.Points.Length - 1; i++)
            total += Vector2.Distance(road.Points[i], road.Points[i + 1]);

        return total;
    }

    static bool Touches(Road a, Road b)
    {
        float limit = (a.Width + b.Width) * 0.5f;

        for (int i = 0; i < a.Points.Length - 1; i++)
        {
            for (int j = 0; j < b.Points.Length - 1; j++)
            {
                if (SegmentDistance(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1]) <= limit)
                    return true;
            }
        }

        return false;
    }

    static float SegmentDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        float best = PointToSegment(a0, b0, b1);

        best = Mathf.Min(best, PointToSegment(a1, b0, b1));
        best = Mathf.Min(best, PointToSegment(b0, a0, a1));
        best = Mathf.Min(best, PointToSegment(b1, a0, a1));

        return best;
    }

    static float PointToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 line = to - from;
        float lengthSqr = line.sqrMagnitude;

        if (lengthSqr <= Mathf.Epsilon)
            return (point - from).magnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - from, line) / lengthSqr);

        return (point - (from + line * t)).magnitude;
    }

    static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }

        return index;
    }

    static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);

        if (rootA != rootB)
            parent[rootB] = rootA;
    }

    static void ReportBiomes(BiomeMap map, BiomeDatabase biomes)
    {
        var counts = new int[biomes.Count];

        foreach (byte cell in map.Cells)
            counts[cell]++;

        Console.WriteLine($"биомы: сетка {map.Resolution}, регионов по площади:");

        for (int i = 0; i < counts.Length; i++)
        {
            float share = 100f * counts[i] / map.Cells.Length;

            Console.WriteLine($"  {biomes.Get(i).name,-20} {share,5:F1}%  вес {biomes.Get(i).RegionWeight}");
        }
    }

    static void ReportRelief(HeightMap map)
    {
        float min = float.MaxValue, max = float.MinValue, sum = 0f;

        foreach (float h in map.Heights)
        {
            min = Mathf.Min(min, h);
            max = Mathf.Max(max, h);
            sum += h;
        }

        float metres = _config.MaxHeight;
        float cell = _config.HeightCellSize;
        int resolution = map.Resolution;
        double slopeSum = 0;
        float steepest = 0f;
        int steep = 0, samples = 0;

        for (int y = 1; y < resolution - 1; y += 2)
        {
            for (int x = 1; x < resolution - 1; x += 2)
            {
                float dx = (map.Get(x + 1, y) - map.Get(x - 1, y)) * metres / (2f * cell);
                float dy = (map.Get(x, y + 1) - map.Get(x, y - 1)) * metres / (2f * cell);
                float slope = Mathf.Rad2Deg * MathF.Atan(MathF.Sqrt(dx * dx + dy * dy));

                slopeSum += slope;
                steepest = Mathf.Max(steepest, slope);
                samples++;

                if (slope > 28f)
                    steep++;
            }
        }

        Console.WriteLine($"рельеф: высоты {min * metres:F0}..{max * metres:F0} м (размах {(max - min) * metres:F0}), средняя {sum / map.Heights.Length * metres:F0} м");
        Console.WriteLine($"        уклон средний {slopeSum / samples:F1}°, максимум {steepest:F0}°, круче 28° (скала): {100f * steep / samples:F1}% площади");

        ReportBumps(map, 40f);
        ReportBumps(map, 120f);
    }

    // Считает локальные максимумы на сетке с заданным шагом: это и есть «холмики».
    // Мелкий шаг ловит рябь, крупный — настоящие холмы, по их отношению видно,
    // насколько рельеф зашумлён мелочью.
    static void ReportBumps(HeightMap map, float step)
    {
        int side = (int)(_config.WorldSize / step);
        var height = new float[side, side];

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
                height[x, y] = map.SampleWorld(new Vector3((x + 0.5f) * step, 0f, (y + 0.5f) * step));
        }

        int peaks = 0;
        double prominence = 0;

        for (int y = 1; y < side - 1; y++)
        {
            for (int x = 1; x < side - 1; x++)
            {
                float centre = height[x, y];
                float lowest = float.MaxValue;
                bool peak = true;

                for (int oy = -1; oy <= 1 && peak; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;

                        float neighbour = height[x + ox, y + oy];

                        if (neighbour >= centre)
                        {
                            peak = false;
                            break;
                        }

                        lowest = Mathf.Min(lowest, neighbour);
                    }
                }

                if (!peak)
                    continue;

                peaks++;
                prominence += centre - lowest;
            }
        }

        float area = _config.WorldSize * _config.WorldSize / 1e6f;

        Console.WriteLine($"        холмиков с шагом {step:F0} м: {peaks} ({peaks / area:F1} на км²), средняя высота бугра {(peaks > 0 ? prominence / peaks : 0):F1} м");
    }
}
