using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

// Рисует землю так, как её покажет VoxelMaterial: настоящие слои биомов, настоящие текстуры
// и карты из Generated/, правила — через тот же GroundSplatPainter. Вид сверху, без Unity.
static class GroundPreview
{
    const string WORLD = "Assets/_Dustborn/Content/World";
    const string CONTENT = "Assets/_Dustborn/Content";
    const string GENERATED = "Assets/_Dustborn/Generated";

    const int TEXTURE_SIZE = 1024;
    const int STAT_RESOLUTION = 1024;
    const float AMBIENT = 0.42f;
    const float DIFFUSE = 0.72f;
    const float CARD_CUTOFF = 0.35f;

    static readonly (float X, float Y, float Z) Sun = Normalize(-0.35f, 0.8f, 0.45f);
    static readonly float[] ToLinear = BuildLinear();

    static string _root;
    static readonly Dictionary<string, string> _paths = new();
    static readonly Dictionary<string, TerrainLayer> _layers = new();
    static readonly Dictionary<TerrainLayer, (string Texture, float Tile)> _layerInfo = new();
    static readonly Dictionary<string, Texture2D> _textures = new();
    static readonly Dictionary<Texture2D, string> _texturePaths = new();
    static readonly Dictionary<string, Mipmapped> _decoded = new();

    class Context
    {
        public WorldGenerationConfig Config;
        public BiomeDatabase Biomes;
        public HeightMap Map;
        public GroundSplatPainter Painter;
        public Mipmapped[] Textures;
        public float[] Tiles;
        public int ControlResolution;
        public VoxelDecorPlacer Placer;
        public DecorFilter Filter;
    }

    public static void Run(string[] args)
    {
        _root = FindRoot();
        string output = OutputDirectory(args);
        Directory.CreateDirectory(output);

        IndexMeta(Path.Combine(_root, CONTENT));

        string configPath = Path.Combine(_root, WORLD, "WorldGenerationConfig.asset");
        var config = AssetReader.Load<WorldGenerationConfig>(configPath);
        string[] configLines = File.ReadAllLines(configPath);

        foreach (string slot in new[] { "CliffLayer", "RoadLayer", "RoadEdgeLayer" })
            SetField(config, slot, LayerRef(TopLevel(configLines, slot)));

        var settings = AssetReader.Load<WorldBuildSettings>(Path.Combine(_root, WORLD, "WorldBuildSettings.asset"));

        Override(config, args);

        BiomeDatabase biomes = LoadBiomes(Path.Combine(_root, WORLD, "BiomeDatabase.asset"));

        Console.WriteLine($"ground preview: {biomes.Count} biomes, control {settings.ControlResolution}, border {config.BiomeBorderWidth} m wide, warp {config.BiomeBorderWarp} m over {config.BiomeBorderWarpPeriod} m");

        var clock = System.Diagnostics.Stopwatch.StartNew();

        HeightMap map = HeightMap.FromRaw16(File.ReadAllBytes(Path.Combine(_root, GENERATED, "HeightMap.bytes")),
            config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        BiomeMap biomeMap = ReadBiomeMap(Path.Combine(_root, GENERATED, "BiomeMap.png"), biomes, config.WorldSize);
        float[] roads = ReadMask(Path.Combine(_root, GENERATED, "RoadMask.png"), out int roadResolution);

        Console.WriteLine($"  maps read in {clock.ElapsedMilliseconds} ms");

        var field = new BiomeWeightField(biomeMap, biomes.Count, config.BiomeBlendRadius);
        using var painter = new GroundSplatPainter(config, biomes, field, ReadRoads(Path.Combine(_root, GENERATED, "RoadNetwork.asset")));

        var context = new Context
        {
            Config = config,
            Biomes = biomes,
            Map = map,
            Painter = painter,
            ControlResolution = settings.ControlResolution,
            Textures = new Mipmapped[painter.Layers.Length],
            Tiles = new float[painter.Layers.Length]
        };

        for (int i = 0; i < painter.Layers.Length; i++)
        {
            (string texture, float tile) = _layerInfo[painter.Layers[i]];

            context.Textures[i] = Decode(texture);
            context.Tiles[i] = tile;

            Console.WriteLine($"  layer {i,2} {painter.Layers[i].name,-18} tile {tile,4:0.#} m  {Path.GetFileName(texture)}");
        }

        ReportWorldShares(context, field);
        ReportSlopes(context, field);

        float lowest = float.MaxValue;

        foreach (float h in map.Heights)
            lowest = Mathf.Min(lowest, h);

        using var density = new VoxelDensityField(map, new VoxelConfig(), lowest * config.MaxHeight);

        context.Filter = new DecorFilter(config, field, biomes.Count, roads, roadResolution, null, 0f);
        context.Placer = new VoxelDecorPlacer(config, biomes, density, context.Filter);

        int index = 0;

        foreach ((string name, float x, float z, bool border) in PickWindows(biomeMap, biomes))
        {
            float span = border ? 384f : 1024f;
            int pixels = border ? 768 : 1024;

            string stem = Path.Combine(output, $"{index++:00}_{name}");

            byte[] ground = RenderGround(context, x - span / 2f, z - span / 2f, span, pixels, out string shares);
            Png.Write(stem + ".png", ground, pixels, pixels);

            Console.WriteLine($"  {Path.GetFileName(stem)} at ({x:0}, {z:0}), {span:0} m: {shares}");

            if (border)
            {
                DrawGrass(context, ground, x - span / 2f, z - span / 2f, span, pixels, out int placed, out int foreign);
                Png.Write(stem + "_grass.png", ground, pixels, pixels);

                Console.WriteLine($"    grass: {placed} tufts, {foreign} on ground mostly of another biome");
            }

            byte[] close = RenderGround(context, x - 36f, z - 36f, 72f, 720, out string closeShares);
            Png.Write(stem + "_close.png", close, 720, 720);
        }

        RenderSwatches(context, Path.Combine(output, "grass_swatches.png"));

        Console.WriteLine($"ground preview written to {Path.GetFullPath(output)} in {clock.ElapsedMilliseconds} ms");
    }

    static void ReportWorldShares(Context context, BiomeWeightField field)
    {
        int resolution = STAT_RESOLUTION;
        WorldGenerationConfig config = context.Config;

        VoxelSplatBaker.Surface(config, context.Map, resolution, out float[] steepness, out float[] height);

        var native = context.Painter.BakeWorld(resolution, steepness, height);
        int layers = context.Painter.Layers.Length;
        var weights = new float[resolution * resolution * layers];
        native.CopyTo(weights);
        native.Dispose();

        int biomeCount = context.Biomes.Count;
        var totals = new double[biomeCount, layers];
        var cells = new int[biomeCount];
        var sample = new float[biomeCount];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                field.Sample((x + 0.5f) / resolution, (y + 0.5f) / resolution, sample);

                int dominant = 0;

                for (int b = 1; b < biomeCount; b++)
                    if (sample[b] > sample[dominant])
                        dominant = b;

                cells[dominant]++;

                int origin = (y * resolution + x) * layers;

                for (int l = 0; l < layers; l++)
                    totals[dominant, l] += weights[origin + l];
            }
        }

        Console.WriteLine("  ground shares per biome (dominant biome of each texel):");

        for (int b = 0; b < biomeCount; b++)
        {
            var parts = new List<(double Share, string Name)>();

            for (int l = 0; l < layers; l++)
            {
                double share = cells[b] == 0 ? 0 : totals[b, l] / cells[b];

                if (share >= 0.005)
                    parts.Add((share, context.Painter.Layers[l].name));
            }

            parts.Sort((left, right) => right.Share.CompareTo(left.Share));

            Console.WriteLine($"    {context.Biomes.Get(b).Type,-12} {100.0 * cells[b] / (resolution * resolution),4:0}% of world: "
                + string.Join(", ", parts.ConvertAll(p => $"{p.Name} {p.Share * 100:0}%")));
        }
    }

    static void ReportSlopes(Context context, BiomeWeightField field)
    {
        const int TILE = 1024;

        int resolution = context.ControlResolution;
        int biomeCount = context.Biomes.Count;
        var histogram = new long[biomeCount, 91];
        var sample = new float[biomeCount];

        for (int tileY = 0; tileY < resolution; tileY += TILE)
        {
            for (int tileX = 0; tileX < resolution; tileX += TILE)
            {
                int span = Math.Min(TILE, resolution - Math.Max(tileX, tileY));

                VoxelSplatBaker.Surface(context.Config, context.Map, resolution, tileX, tileY, span, out float[] steepness, out _);

                for (int y = 0; y < span; y++)
                {
                    for (int x = 0; x < span; x++)
                    {
                        field.Sample((tileX + x + 0.5f) / resolution, (tileY + y + 0.5f) / resolution, sample);

                        int dominant = 0;

                        for (int b = 1; b < biomeCount; b++)
                            if (sample[b] > sample[dominant])
                                dominant = b;

                        histogram[dominant, Math.Min(90, (int)steepness[y * span + x])]++;
                    }
                }
            }
        }

        Console.WriteLine($"  slope per biome at {resolution} control texels (degrees):");

        for (int b = 0; b < biomeCount; b++)
        {
            long total = 0;

            for (int d = 0; d <= 90; d++)
                total += histogram[b, d];

            if (total == 0)
                continue;

            int Percentile(double fraction)
            {
                long running = 0;

                for (int d = 0; d <= 90; d++)
                {
                    running += histogram[b, d];

                    if (running >= fraction * total)
                        return d;
                }

                return 90;
            }

            double Above(int degrees)
            {
                long count = 0;

                for (int d = degrees; d <= 90; d++)
                    count += histogram[b, d];

                return 100.0 * count / total;
            }

            Console.WriteLine($"    {context.Biomes.Get(b).Type,-12} median {Percentile(0.5)}, p75 {Percentile(0.75)}, p90 {Percentile(0.9)}, p97 {Percentile(0.97)}; "
                + $"past 20° {Above(20):0}%, past 26° {Above(26):0}%, past 32° {Above(32):0}%, past 40° {Above(40):0}%");
        }
    }

    static IEnumerable<(string Name, float X, float Z, bool Border)> PickWindows(BiomeMap map, BiomeDatabase biomes)
    {
        int resolution = map.Resolution;
        float cell = map.CellSize;
        byte[] cells = map.Cells;
        int margin = Mathf.CeilToInt(400f / cell);

        var borders = new SortedDictionary<int, List<int>>();

        for (int y = margin; y < resolution - margin; y++)
        {
            for (int x = margin; x < resolution - margin; x++)
            {
                int i = y * resolution + x;

                AddBorder(borders, cells[i], cells[i + 1], i);
                AddBorder(borders, cells[i], cells[i + resolution], i);
            }
        }

        foreach (KeyValuePair<int, List<int>> pair in borders)
        {
            int i = pair.Value[pair.Value.Count / 2];
            string name = $"border_{biomes.Get(pair.Key / 16).Type}_{biomes.Get(pair.Key % 16).Type}";

            yield return (name, (i % resolution + 1) * cell, (i / resolution + 0.5f) * cell, true);
        }

        int[] distance = DistanceToOtherBiome(map);

        for (int b = 0; b < biomes.Count; b++)
        {
            int best = -1;

            for (int i = 0; i < cells.Length; i++)
                if (cells[i] == b && (best < 0 || distance[i] > distance[best]))
                    best = i;

            if (best >= 0)
                yield return ($"inside_{biomes.Get(b).Type}", (best % resolution + 0.5f) * cell, (best / resolution + 0.5f) * cell, false);
        }
    }

    static void AddBorder(SortedDictionary<int, List<int>> borders, byte a, byte b, int cell)
    {
        if (a == b)
            return;

        int key = Math.Min(a, b) * 16 + Math.Max(a, b);

        if (!borders.TryGetValue(key, out List<int> list))
            borders[key] = list = new List<int>();

        list.Add(cell);
    }

    static int[] DistanceToOtherBiome(BiomeMap map)
    {
        int n = map.Resolution;
        byte[] cells = map.Cells;
        var distance = new int[cells.Length];

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1
                    || cells[i - 1] != cells[i] || cells[i + 1] != cells[i] || cells[i - n] != cells[i] || cells[i + n] != cells[i];

                distance[i] = edge ? 0 : int.MaxValue / 2;
            }
        }

        for (int y = 1; y < n; y++)
            for (int x = 1; x < n; x++)
            {
                int i = y * n + x;
                distance[i] = Math.Min(distance[i], Math.Min(distance[i - 1], distance[i - n]) + 1);
            }

        for (int y = n - 2; y >= 0; y--)
            for (int x = n - 2; x >= 0; x--)
            {
                int i = y * n + x;
                distance[i] = Math.Min(distance[i], Math.Min(distance[i + 1], distance[i + n]) + 1);
            }

        return distance;
    }

    static List<Road> ReadRoads(string path)
    {
        var roads = new List<Road>();

        if (!File.Exists(path))
        {
            Console.WriteLine($"  no {Path.GetFileName(path)}, roads are not painted");
            return roads;
        }

        bool inside = false;
        var points = new List<Vector2>();
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimEnd();

            if (line.StartsWith("  _", StringComparison.Ordinal))
            {
                inside = line == "  _roads:" || line == "  _streets:";
                continue;
            }

            if (!inside)
                continue;

            string body = line.TrimStart(' ', '-').Trim();

            if (body.StartsWith("<Points>k__BackingField", StringComparison.Ordinal))
            {
                points.Clear();
                continue;
            }

            if (body.StartsWith("{x:", StringComparison.Ordinal))
            {
                string[] parts = body.Trim('{', '}').Split(',');

                points.Add(new Vector2(
                    float.Parse(parts[0].Substring(parts[0].IndexOf(':') + 1), culture),
                    float.Parse(parts[1].Substring(parts[1].IndexOf(':') + 1), culture)));
                continue;
            }

            if (!body.StartsWith("<Width>k__BackingField:", StringComparison.Ordinal))
                continue;

            if (points.Count >= 2)
                roads.Add(new Road(points.ToArray(), float.Parse(body.Substring(body.IndexOf(':') + 1), culture)));

            points.Clear();
        }

        Console.WriteLine($"  {roads.Count} roads and streets read from {Path.GetFileName(path)}");

        return roads;
    }

    static byte[] RenderGround(Context context, float originX, float originZ, float span, int pixels, out string shares)
    {
        WorldGenerationConfig config = context.Config;
        int resolution = context.ControlResolution;
        float texel = (float)config.WorldSize / resolution;

        int tile = Mathf.Min(resolution, Mathf.CeilToInt(span / texel) + 6);
        int tileX = Mathf.Clamp(Mathf.FloorToInt(originX / texel) - 3, 0, resolution - tile);
        int tileZ = Mathf.Clamp(Mathf.FloorToInt(originZ / texel) - 3, 0, resolution - tile);

        VoxelSplatBaker.Surface(config, context.Map, resolution, tileX, tileZ, tile, out float[] steepness, out float[] height);

        var native = context.Painter.BakeTile(resolution, tile, tileX, tileZ, steepness, height);
        int layers = context.Painter.Layers.Length;
        var weights = new float[tile * tile * layers];
        native.CopyTo(weights);
        native.Dispose();

        var rgb = new byte[pixels * pixels * 3];
        var totals = new double[layers];
        float metresPerPixel = span / pixels;
        object gate = new object();

        Parallel.For(0, pixels, () => new double[layers], (py, state, local) =>
        {
            var blend = new float[layers];

            for (int px = 0; px < pixels; px++)
            {
                float worldX = originX + (px + 0.5f) * metresPerPixel;
                float worldZ = originZ + span - (py + 0.5f) * metresPerPixel;

                float fx = Mathf.Clamp(worldX / texel - 0.5f - tileX, 0f, tile - 1.001f);
                float fz = Mathf.Clamp(worldZ / texel - 0.5f - tileZ, 0f, tile - 1.001f);

                int cx = (int)fx;
                int cz = (int)fz;
                float ax = fx - cx;
                float az = fz - cz;

                int c00 = (cz * tile + cx) * layers;
                int c10 = (cz * tile + cx + 1) * layers;
                int c01 = ((cz + 1) * tile + cx) * layers;
                int c11 = ((cz + 1) * tile + cx + 1) * layers;

                float r = 0f, g = 0f, b = 0f;

                for (int l = 0; l < layers; l++)
                {
                    float w = (weights[c00 + l] * (1f - ax) + weights[c10 + l] * ax) * (1f - az)
                        + (weights[c01 + l] * (1f - ax) + weights[c11 + l] * ax) * az;

                    local[l] += w;

                    if (w < 0.002f)
                        continue;

                    Mipmapped texture = context.Textures[l];
                    float repeat = context.Tiles[l];

                    texture.Sample(worldX / repeat, worldZ / repeat, metresPerPixel * texture.Size / repeat, out float tr, out float tg, out float tb);

                    r += w * tr;
                    g += w * tg;
                    b += w * tb;
                }

                float shade = Shade(context.Map, worldX, worldZ);
                int o = (py * pixels + px) * 3;

                rgb[o] = ToSrgb(r * shade);
                rgb[o + 1] = ToSrgb(g * shade);
                rgb[o + 2] = ToSrgb(b * shade);
            }

            return local;
        }, local =>
        {
            lock (gate)
                for (int l = 0; l < layers; l++)
                    totals[l] += local[l];
        });

        var parts = new List<(double Share, string Name)>();

        for (int l = 0; l < layers; l++)
        {
            double share = totals[l] / ((double)pixels * pixels);

            if (share >= 0.01)
                parts.Add((share, context.Painter.Layers[l].name));
        }

        parts.Sort((left, right) => right.Share.CompareTo(left.Share));
        shares = string.Join(", ", parts.ConvertAll(p => $"{p.Name} {p.Share * 100:0}%"));

        return rgb;
    }

    static float Shade(HeightMap map, float x, float z)
    {
        float dx = (map.SampleWorldSmooth(x + 1f, z) - map.SampleWorldSmooth(x - 1f, z)) * 0.5f;
        float dz = (map.SampleWorldSmooth(x, z + 1f) - map.SampleWorldSmooth(x, z - 1f)) * 0.5f;

        (float nx, float ny, float nz) = Normalize(-dx, 1f, -dz);

        return AMBIENT + DIFFUSE * MathF.Max(0f, nx * Sun.X + ny * Sun.Y + nz * Sun.Z);
    }

    static void DrawGrass(Context context, byte[] rgb, float originX, float originZ, float span, int pixels, out int placed, out int foreign)
    {
        placed = 0;
        foreign = 0;

        var instances = new List<DecorInstance>();
        float metresPerPixel = span / pixels;

        for (int layer = 0; layer < context.Placer.Layers.Count; layer++)
        {
            VoxelDecorLayer decor = context.Placer.Layers[layer];

            if (decor.Kind != DecorKind.Grass)
                continue;

            instances.Clear();
            context.Placer.Place(layer, new Vector2(originX, originZ), span, instances);

            Color leaves = decor.Style.Leaves;
            float lr = ToLinear[(int)(Mathf.Clamp01(leaves.r) * 255f)];
            float lg = ToLinear[(int)(Mathf.Clamp01(leaves.g) * 255f)];
            float lb = ToLinear[(int)(Mathf.Clamp01(leaves.b) * 255f)];

            foreach (DecorInstance instance in instances)
            {
                placed++;

                if (context.Filter.SurfaceWeight(new Vector2(instance.Position.x, instance.Position.z), decor.Biome) < 0.5f)
                    foreign++;

                float shade = Shade(context.Map, instance.Position.x, instance.Position.z) * 1.05f;
                float cx = (instance.Position.x - originX) / metresPerPixel;
                float cy = (originZ + span - instance.Position.z) / metresPerPixel;
                float radius = MathF.Max(1.1f, instance.Scale * 0.35f / metresPerPixel);

                byte r = ToSrgb(lr * shade), g = ToSrgb(lg * shade), b = ToSrgb(lb * shade);

                for (int y = (int)(cy - radius); y <= (int)(cy + radius); y++)
                {
                    for (int x = (int)(cx - radius); x <= (int)(cx + radius); x++)
                    {
                        if (x < 0 || y < 0 || x >= pixels || y >= pixels)
                            continue;

                        float ox = x + 0.5f - cx, oy = y + 0.5f - cy;

                        if (ox * ox + oy * oy > radius * radius)
                            continue;

                        int o = (y * pixels + x) * 3;
                        rgb[o] = r;
                        rgb[o + 1] = g;
                        rgb[o + 2] = b;
                    }
                }
            }
        }
    }

    static void RenderSwatches(Context context, string path)
    {
        const int CARD = 160;
        const int COLUMNS = 6;

        int width = CARD * COLUMNS;
        int height = CARD * context.Biomes.Count;
        var rgb = new byte[width * height * 3];

        for (int b = 0; b < context.Biomes.Count; b++)
        {
            BiomeDefinition biome = context.Biomes.Get(b);
            Mipmapped ground = biome.Ground.Count > 0 && biome.Ground[0].Layer != null ? Decode(_layerInfo[biome.Ground[0].Layer].Texture) : null;

            var groundLinear = new float[CARD * width * 3];

            for (int y = 0; y < CARD; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float r = 0.5f, g = 0.5f, bl = 0.5f;

                    ground?.Sample(x / (float)(CARD * 3), 1f - y / (float)(CARD * 3), ground.Size / (float)(CARD * 3), out r, out g, out bl);

                    int o = (y * width + x) * 3;
                    groundLinear[o] = r;
                    groundLinear[o + 1] = g;
                    groundLinear[o + 2] = bl;
                }
            }

            for (int column = 0; column < COLUMNS && column < biome.Grass.Count; column++)
                CompositeCard(biome.Grass[column], groundLinear, width, column * CARD, CARD);

            for (int i = 0; i < groundLinear.Length; i++)
                rgb[b * CARD * width * 3 + i] = ToSrgb(groundLinear[i]);

            Console.WriteLine($"  swatch row {b}: {biome.Type}, {biome.Grass.Count} grass layers");
        }

        Png.Write(path, rgb, width, height);
    }

    static void CompositeCard(GrassLayer layer, float[] target, int width, int left, int size)
    {
        if (layer.Card == null)
            return;

        byte[] card = PngReader.Read(_texturePaths[layer.Card], out int cw, out int ch, out int cc);
        int maskWidth = 0, maskHeight = 0, maskChannels = 0;
        byte[] mask = layer.Mask != null ? PngReader.Read(_texturePaths[layer.Mask], out maskWidth, out maskHeight, out maskChannels) : null;

        (float r, float g, float b) leaves = Linear(layer.LeavesColor);
        (float r, float g, float b) stems = Linear(layer.StemsColor);
        (float r, float g, float b) flowers = Linear(layer.FlowersColor);
        (float r, float g, float b) tip = Linear(layer.TipTint);
        (float r, float g, float b) snowColor = Linear(new Color(0.933f, 0.957f, 0.969f));

        for (int y = 0; y < size; y++)
        {
            float v = 1f - (y + 0.5f) / size;

            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;

                int sx = Math.Min(cw - 1, (int)(u * cw));
                int sy = Math.Min(ch - 1, (int)((1f - v) * ch));
                int si = (sy * cw + sx) * cc;

                float alpha = cc == 4 ? card[si + 3] / 255f : 1f;

                if (alpha < CARD_CUTOFF)
                    continue;

                float wr = 1f, wg = 0f, wb = 0f;

                if (mask != null)
                {
                    int mx = Math.Min(maskWidth - 1, (int)(u * maskWidth));
                    int my = Math.Min(maskHeight - 1, (int)((1f - v) * maskHeight));
                    int mi = (my * maskWidth + mx) * maskChannels;

                    float mr = mask[mi] / 255f, mg = mask[mi + Math.Min(1, maskChannels - 1)] / 255f, mb = mask[mi + Math.Min(2, maskChannels - 1)] / 255f;
                    float total = mr + mg + mb;

                    if (total > 0.001f)
                    {
                        wr = mr / total;
                        wg = mg / total;
                        wb = mb / total;
                    }
                }

                float tr = wr * leaves.r + wg * stems.r + wb * flowers.r;
                float tg = wr * leaves.g + wg * stems.g + wb * flowers.g;
                float tb = wr * leaves.b + wg * stems.b + wb * flowers.b;

                tr *= Lerp(1f, tip.r, v);
                tg *= Lerp(1f, tip.g, v);
                tb *= Lerp(1f, tip.b, v);

                float snow = SmoothStep(0.55f, 1f, v) * layer.SnowAmount;

                tr = Lerp(tr, snowColor.r, snow);
                tg = Lerp(tg, snowColor.g, snow);
                tb = Lerp(tb, snowColor.b, snow);

                float root = 1f - layer.RootDarkening * (1f - SmoothStep(0f, 0.55f, v));

                tr *= Lerp(1f, ToLinear[card[si]], layer.TextureShading) * root;
                tg *= Lerp(1f, ToLinear[card[si + 1]], layer.TextureShading) * root;
                tb *= Lerp(1f, ToLinear[card[si + 2]], layer.TextureShading) * root;

                int o = (y * width + left + x) * 3;
                target[o] = tr;
                target[o + 1] = tg;
                target[o + 2] = tb;
            }
        }
    }

    static BiomeDatabase LoadBiomes(string databasePath)
    {
        var list = new List<BiomeDefinition>();

        foreach (string guid in GuidList(File.ReadAllLines(databasePath), "_biomes:"))
        {
            string path = _paths[guid];
            string[] lines = File.ReadAllLines(path);

            var biome = new BiomeDefinition { name = Path.GetFileNameWithoutExtension(path) };

            AssetReader.Apply(biome, path);

            SetField(biome, "MapColor", MapColor(lines));
            SetField(biome, "CliffLayer", LayerRef(TopLevel(lines, "CliffLayer")));

            foreach (Dictionary<string, string> item in ReadList(lines, "Ground"))
            {
                var ground = new GroundLayer();
                Fill(ground, item);
                biome.Ground.Add(ground);
            }

            foreach (Dictionary<string, string> item in ReadList(lines, "Grass"))
            {
                var grass = new GrassLayer();
                Fill(grass, item);
                biome.Grass.Add(grass);
            }

            list.Add(biome);
        }

        var database = new BiomeDatabase();

        typeof(BiomeDatabase).GetField("_biomes", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(database, list);

        return database;
    }

    static List<Dictionary<string, string>> ReadList(string[] lines, string name)
    {
        var items = new List<Dictionary<string, string>>();
        int start = Array.IndexOf(lines, $"  <{name}>k__BackingField:");

        if (start < 0)
            return items;

        Dictionary<string, string> current = null;

        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            string body;

            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                current = new Dictionary<string, string>();
                items.Add(current);
                body = line.Substring(4);
            }
            else if (line.StartsWith("    ", StringComparison.Ordinal))
            {
                body = line.Substring(4);
            }
            else
            {
                break;
            }

            int close = body.IndexOf(">k__BackingField:", StringComparison.Ordinal);

            if (current == null || !body.StartsWith("<", StringComparison.Ordinal) || close < 0)
                continue;

            current[body.Substring(1, close - 1)] = body.Substring(close + 17).Trim();
        }

        return items;
    }

    static void Fill(object target, Dictionary<string, string> item)
    {
        foreach ((string name, string value) in item)
        {
            FieldInfo field = target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null)
                continue;

            if (field.FieldType == typeof(float))
                field.SetValue(target, float.Parse(value, CultureInfo.InvariantCulture));
            else if (field.FieldType == typeof(int))
                field.SetValue(target, int.Parse(value, CultureInfo.InvariantCulture));
            else if (field.FieldType == typeof(Color))
                field.SetValue(target, ParseColor(value));
            else if (field.FieldType == typeof(TerrainLayer))
                field.SetValue(target, LayerRef(value));
            else if (field.FieldType == typeof(Texture2D))
                field.SetValue(target, TextureRef(value));
        }
    }

    static Color ParseColor(string value)
    {
        float Part(string key)
        {
            int at = value.IndexOf(key + ": ", StringComparison.Ordinal);
            int end = value.IndexOfAny(new[] { ',', '}' }, at);

            return float.Parse(value.Substring(at + key.Length + 2, end - at - key.Length - 2), CultureInfo.InvariantCulture);
        }

        return new Color(Part("r"), Part("g"), Part("b"), Part("a"));
    }

    static Color32 MapColor(string[] lines)
    {
        int start = Array.IndexOf(lines, "  <MapColor>k__BackingField:");

        for (int i = start + 1; i < start + 4 && i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (!line.StartsWith("rgba: ", StringComparison.Ordinal))
                continue;

            uint rgba = uint.Parse(line.Substring(6), CultureInfo.InvariantCulture);

            return new Color32((byte)rgba, (byte)(rgba >> 8), (byte)(rgba >> 16), (byte)(rgba >> 24));
        }

        throw new InvalidDataException("biome has no MapColor");
    }

    static string TopLevel(string[] lines, string name)
    {
        string prefix = $"  <{name}>k__BackingField: ";

        foreach (string line in lines)
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line.Substring(prefix.Length);

        return null;
    }

    static TerrainLayer LayerRef(string value)
    {
        string guid = Guid(value);

        if (guid == null)
            return null;

        if (_layers.TryGetValue(guid, out TerrainLayer cached))
            return cached;

        string path = _paths[guid];
        string texture = null;
        float tile = 1f;

        foreach (string line in File.ReadAllLines(path))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("m_DiffuseTexture:", StringComparison.Ordinal))
                texture = _paths[Guid(trimmed)];

            if (trimmed.StartsWith("m_TileSize:", StringComparison.Ordinal))
            {
                int at = trimmed.IndexOf("x: ", StringComparison.Ordinal) + 3;
                tile = float.Parse(trimmed.Substring(at, trimmed.IndexOf(',', at) - at), CultureInfo.InvariantCulture);
            }
        }

        var layer = new TerrainLayer { name = Path.GetFileNameWithoutExtension(path) };

        _layers[guid] = layer;
        _layerInfo[layer] = (texture, tile);

        return layer;
    }

    static Texture2D TextureRef(string value)
    {
        string guid = Guid(value);

        if (guid == null)
            return null;

        if (_textures.TryGetValue(guid, out Texture2D cached))
            return cached;

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(_paths[guid]) };

        _textures[guid] = texture;
        _texturePaths[texture] = _paths[guid];

        return texture;
    }

    static string Guid(string value)
    {
        if (value == null)
            return null;

        int at = value.IndexOf("guid: ", StringComparison.Ordinal);

        return at < 0 ? null : value.Substring(at + 6, 32);
    }

    static IEnumerable<string> GuidList(string[] lines, string header)
    {
        bool inside = false;

        foreach (string line in lines)
        {
            if (line.Trim() == header)
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            string guid = Guid(line);

            if (guid == null)
                yield break;

            yield return guid;
        }
    }

    static void IndexMeta(string folder)
    {
        foreach (string meta in Directory.EnumerateFiles(folder, "*.meta", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(meta))
            {
                if (!line.StartsWith("guid: ", StringComparison.Ordinal))
                    continue;

                _paths[line.Substring(6).Trim()] = meta.Substring(0, meta.Length - 5);
                break;
            }
        }
    }

    static BiomeMap ReadBiomeMap(string path, BiomeDatabase biomes, int worldSize)
    {
        byte[] pixels = PngReader.Read(path, out int width, out int height, out int channels);
        var map = new BiomeMap(width, worldSize);

        var palette = new Color32[biomes.Count];

        for (int b = 0; b < biomes.Count; b++)
            palette[b] = biomes.Get(b).MapColor;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((height - 1 - y) * width + x) * channels;
                int best = 0;
                int bestDistance = int.MaxValue;

                for (int b = 0; b < palette.Length; b++)
                {
                    int dr = pixels[i] - palette[b].r, dg = pixels[i + 1] - palette[b].g, db = pixels[i + 2] - palette[b].b;
                    int d = dr * dr + dg * dg + db * db;

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = b;
                    }
                }

                map.Cells[y * width + x] = (byte)best;
            }
        }

        return map;
    }

    static float[] ReadMask(string path, out int resolution)
    {
        byte[] pixels = PngReader.Read(path, out int width, out int height, out int channels);
        var mask = new float[width * height];

        Parallel.For(0, height, y =>
        {
            int row = (height - 1 - y) * width;

            for (int x = 0; x < width; x++)
                mask[y * width + x] = pixels[(row + x) * channels] / 255f;
        });

        resolution = width;

        return mask;
    }

    static Mipmapped Decode(string path)
    {
        if (_decoded.TryGetValue(path, out Mipmapped cached))
            return cached;

        byte[] pixels = PngReader.Read(path, out int width, out int height, out int channels);
        var texture = new Mipmapped(pixels, width, height, channels, TEXTURE_SIZE);

        _decoded[path] = texture;

        return texture;
    }

    static void Override(WorldGenerationConfig config, string[] args)
    {
        foreach (string arg in args)
        {
            int equals = arg.IndexOf('=');

            if (arg.StartsWith("--", StringComparison.Ordinal) || equals <= 0)
                continue;

            string name = arg.Substring(0, equals);
            string text = arg.Substring(equals + 1);
            FieldInfo field = typeof(WorldGenerationConfig).GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new ArgumentException($"WorldGenerationConfig has no {name}");

            object value = field.FieldType == typeof(int)
                ? int.Parse(text, CultureInfo.InvariantCulture)
                : field.FieldType == typeof(bool) ? bool.Parse(text) : float.Parse(text, CultureInfo.InvariantCulture);

            field.SetValue(config, value);
            Console.WriteLine($"  override {name} = {value}");
        }
    }

    static void SetField(object target, string name, object value)
    {
        target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }

    static string FindRoot()
    {
        string directory = Directory.GetCurrentDirectory();

        while (directory != null && !Directory.Exists(Path.Combine(directory, "Assets", "_Dustborn")))
            directory = Path.GetDirectoryName(directory);

        return directory ?? throw new DirectoryNotFoundException("run the preview from inside the project");
    }

    static string OutputDirectory(string[] args)
    {
        int flag = Array.IndexOf(args, "--ground-preview");

        if (flag >= 0 && flag + 1 < args.Length && !args[flag + 1].Contains('='))
            return args[flag + 1];

        return "ground_preview";
    }

    static (float r, float g, float b) Linear(Color color)
    {
        return (SrgbToLinear(color.r), SrgbToLinear(color.g), SrgbToLinear(color.b));
    }

    static float SrgbToLinear(float c)
    {
        c = Mathf.Clamp01(c);

        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    static byte ToSrgb(float linear)
    {
        linear = MathF.Max(0f, MathF.Min(1f, linear));
        float c = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

        return (byte)MathF.Round(c * 255f);
    }

    static float[] BuildLinear()
    {
        var table = new float[256];

        for (int i = 0; i < 256; i++)
            table[i] = SrgbToLinear(i / 255f);

        return table;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = MathF.Max(0f, MathF.Min(1f, (x - edge0) / (edge1 - edge0)));

        return t * t * (3f - 2f * t);
    }

    static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        float length = MathF.Sqrt(x * x + y * y + z * z);

        return (x / length, y / length, z / length);
    }

    class Mipmapped
    {
        readonly byte[][] _levels;
        readonly int[] _sizes;

        public int Size => _sizes[0];

        public Mipmapped(byte[] pixels, int width, int height, int channels, int size)
        {
            int count = 1;

            for (int s = size; s > 1; s >>= 1)
                count++;

            _levels = new byte[count][];
            _sizes = new int[count];

            _levels[0] = Resample(pixels, width, height, channels, size);
            _sizes[0] = size;

            for (int level = 1; level < count; level++)
            {
                int previous = _sizes[level - 1];
                int next = Math.Max(1, previous / 2);
                byte[] source = _levels[level - 1];
                var target = new byte[next * next * 3];

                for (int y = 0; y < next; y++)
                    for (int x = 0; x < next; x++)
                        for (int c = 0; c < 3; c++)
                        {
                            int sum = source[(y * 2 * previous + x * 2) * 3 + c] + source[(y * 2 * previous + Math.Min(previous - 1, x * 2 + 1)) * 3 + c]
                                + source[(Math.Min(previous - 1, y * 2 + 1) * previous + x * 2) * 3 + c]
                                + source[(Math.Min(previous - 1, y * 2 + 1) * previous + Math.Min(previous - 1, x * 2 + 1)) * 3 + c];

                            target[(y * next + x) * 3 + c] = (byte)((sum + 2) / 4);
                        }

                _levels[level] = target;
                _sizes[level] = next;
            }
        }

        static byte[] Resample(byte[] pixels, int width, int height, int channels, int size)
        {
            var target = new byte[size * size * 3];

            for (int y = 0; y < size; y++)
            {
                float fy = (y + 0.5f) * height / size - 0.5f;
                int y0 = Math.Clamp((int)MathF.Floor(fy), 0, height - 1);
                int y1 = Math.Min(height - 1, y0 + 1);
                float ty = Math.Clamp(fy - y0, 0f, 1f);

                int rowTop = height - 1 - y0;
                int rowBottom = height - 1 - y1;

                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) * width / size - 0.5f;
                    int x0 = Math.Clamp((int)MathF.Floor(fx), 0, width - 1);
                    int x1 = Math.Min(width - 1, x0 + 1);
                    float tx = Math.Clamp(fx - x0, 0f, 1f);

                    for (int c = 0; c < 3; c++)
                    {
                        int channel = Math.Min(c, channels - 1);

                        float a = pixels[(rowTop * width + x0) * channels + channel] * (1f - tx) + pixels[(rowTop * width + x1) * channels + channel] * tx;
                        float b = pixels[(rowBottom * width + x0) * channels + channel] * (1f - tx) + pixels[(rowBottom * width + x1) * channels + channel] * tx;

                        target[(y * size + x) * 3 + c] = (byte)MathF.Round(a * (1f - ty) + b * ty);
                    }
                }
            }

            return target;
        }

        public void Sample(float u, float v, float footprint, out float r, out float g, out float b)
        {
            int level = footprint <= 1f ? 0 : Math.Min(_levels.Length - 1, (int)MathF.Log2(footprint));
            int size = _sizes[level];
            byte[] data = _levels[level];

            float fx = u * size - 0.5f;
            float fy = v * size - 0.5f;

            int x0 = (int)MathF.Floor(fx);
            int y0 = (int)MathF.Floor(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            x0 = ((x0 % size) + size) % size;
            y0 = ((y0 % size) + size) % size;
            int x1 = (x0 + 1) % size;
            int y1 = (y0 + 1) % size;

            int i00 = (y0 * size + x0) * 3, i10 = (y0 * size + x1) * 3, i01 = (y1 * size + x0) * 3, i11 = (y1 * size + x1) * 3;

            r = Mix(data, i00, i10, i01, i11, 0, tx, ty);
            g = Mix(data, i00, i10, i01, i11, 1, tx, ty);
            b = Mix(data, i00, i10, i01, i11, 2, tx, ty);
        }

        static float Mix(byte[] data, int i00, int i10, int i01, int i11, int c, float tx, float ty)
        {
            float bottom = ToLinear[data[i00 + c]] * (1f - tx) + ToLinear[data[i10 + c]] * tx;
            float top = ToLinear[data[i01 + c]] * (1f - tx) + ToLinear[data[i11 + c]] * tx;

            return bottom * (1f - ty) + top * ty;
        }
    }
}

static class PngReader
{
    public static byte[] Read(string path, out int width, out int height, out int channels)
    {
        byte[] file = File.ReadAllBytes(path);

        width = height = 0;
        int depth = 0, type = 0, interlace = 0;
        byte[] palette = null;
        byte[] transparency = null;
        var compressed = new MemoryStream();

        for (int at = 8; at + 8 <= file.Length;)
        {
            int length = (file[at] << 24) | (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
            string chunk = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            int data = at + 8;

            switch (chunk)
            {
                case "IHDR":
                    width = (file[data] << 24) | (file[data + 1] << 16) | (file[data + 2] << 8) | file[data + 3];
                    height = (file[data + 4] << 24) | (file[data + 5] << 16) | (file[data + 6] << 8) | file[data + 7];
                    depth = file[data + 8];
                    type = file[data + 9];
                    interlace = file[data + 12];
                    break;
                case "PLTE":
                    palette = file.AsSpan(data, length).ToArray();
                    break;
                case "tRNS":
                    transparency = file.AsSpan(data, length).ToArray();
                    break;
                case "IDAT":
                    compressed.Write(file, data, length);
                    break;
            }

            at = data + length + 4;
        }

        if (depth != 8 || interlace != 0)
            throw new NotSupportedException($"{path}: only 8-bit non-interlaced PNG is read, got depth {depth} interlace {interlace}");

        int stored = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"{path}: colour type {type}") };
        int stride = width * stored;

        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;

        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress))
            inflate.ReadExactly(raw);

        var pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            int source = y * (stride + 1) + 1;
            int row = y * stride;
            int above = row - stride;

            for (int x = 0; x < stride; x++)
            {
                int left = x >= stored ? pixels[row + x - stored] : 0;
                int up = y > 0 ? pixels[above + x] : 0;
                int corner = x >= stored && y > 0 ? pixels[above + x - stored] : 0;

                int value = raw[source + x];

                value += filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, corner),
                    _ => 0
                };

                pixels[row + x] = (byte)value;
            }
        }

        if (type != 3)
        {
            channels = stored;
            return pixels;
        }

        channels = transparency != null ? 4 : 3;
        var expanded = new byte[width * height * channels];

        for (int i = 0; i < width * height; i++)
        {
            int entry = pixels[i];

            expanded[i * channels] = palette[entry * 3];
            expanded[i * channels + 1] = palette[entry * 3 + 1];
            expanded[i * channels + 2] = palette[entry * 3 + 2];

            if (channels == 4)
                expanded[i * channels + 3] = entry < transparency.Length ? transparency[entry] : (byte)255;
        }

        return expanded;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
