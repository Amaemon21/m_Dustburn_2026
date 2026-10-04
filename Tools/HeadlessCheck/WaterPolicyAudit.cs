using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

static class WaterPolicyAudit
{
    private const float ROAD_STEP = 2f;
    private const float ROAD_WET = 0.3f;
    private const float CROWD_GAP = 120f;
    private const float SMALL_BODY = 20000f;

    public sealed class Result
    {
        public int RoadSamples, RoadOnStanding, RoadOnIce, RoadInRiver, BridgeSamples;
        public readonly List<Vector2> RoadWetExamples = new();
        public readonly Dictionary<string, int> RoadWetByBiome = new();
        public int DryBiomeWater, FrozenOutsideSnow, LiquidInsideSnow, InlandSeaCells, SeaCells;
        public int LooseStarts, LooseEnds, PondCaps;
        public int CrowdedBodies, Bodies;
        public readonly List<Vector2> CrowdedExamples = new();
        public readonly List<string> CrowdedPairs = new();
        public float[] CoastSea = new float[4];
        public float CoastMean, CoastStd, CoastMin, CoastMax;
        public string Trunk = "нет";
        public bool TrunkOk;
        public RiverTopologyAudit.Result Topology;
        public int Violations;
        public WaterShapeAudit.Result Shape;

        public string Near = "";

        public string Describe()
        {
            var text = new StringBuilder();
            text.AppendLine("политика воды:");
            text.AppendLine($"  дороги: {RoadSamples:N0} проб по 2 м, на стоячей воде {RoadOnStanding} (из них на льду {RoadOnIce}), в русле без моста {RoadInRiver}, на мостах {BridgeSamples}");

            if (RoadWetExamples.Count > 0)
                text.AppendLine($"  дороги на воде по биомам: {string.Join(", ", RoadWetByBiome.Select(pair => $"{pair.Key} {pair.Value}"))}; примеры {string.Join(" ", RoadWetExamples.Take(6).Select(Format))}{Near}");

            text.AppendLine($"  биомы: воды в сухих биомах {DryBiomeWater} ячеек, льда вне снега {FrozenOutsideSnow}, жидкой воды в снегу {LiquidInsideSnow}");
            text.AppendLine($"  море: {SeaCells} ячеек, из них оторванных от края мира {InlandSeaCells}");
            text.AppendLine($"  побережье по сторонам (доля моря на краю: запад, восток, юг, север): {string.Join(", ", CoastSea.Select(share => share.ToString("P0")))}; берег от края {CoastMin:0}..{CoastMax:0} м, среднее {CoastMean:0} м, разброс {CoastStd:0} м");
            text.AppendLine($"  концы рек: висящих истоков {LooseStarts}, висящих устьев {LooseEnds}, прудов-заглушек на концах рек {PondCaps}");
            text.AppendLine(Topology.Describe());
            text.AppendLine($"  водоёмы: {Bodies}, малых водоёмов ближе {CROWD_GAP:0} м к другой воде {CrowdedBodies}" + (CrowdedExamples.Count == 0 ? "" : $" ({string.Join(" ", CrowdedExamples.Take(6).Select(Format))}; {string.Join(" ", CrowdedPairs.Take(6))})"));
            text.Append($"  главная река: {Trunk}");

            if (Shape != null)
                text.Append("\n" + Shape.Describe());

            return text.ToString();
        }

        private static string Format(Vector2 point) => $"({point.x:0}, {point.y:0})";
    }

    public static Result Measure(WorldGenerationConfig config, BiomeMap biomes, BiomeDatabase database, HeightMap final, WaterMap water, IEnumerable<Road> roads)
    {
        var result = new Result();
        _startArea = config.Water.RiverStartArea * 1e6f;

        if (roads != null)
            MeasureRoads(result, biomes, database, final, water, roads);

        if (biomes != null)
            MeasureBiomes(result, biomes, database, water);

        MeasureSea(result, water);
        MeasureEnds(result, water);
        MeasureCrowding(result, water);
        result.Topology = RiverTopologyAudit.Measure(water);

        if (biomes != null)
            MeasureTrunk(result, config, biomes, database, water);

        if (final != null)
            result.Shape = WaterShapeAudit.Measure(config, final, water);

        result.Violations = result.RoadOnStanding + result.RoadInRiver + result.DryBiomeWater + result.FrozenOutsideSnow + result.InlandSeaCells + result.LooseEnds + result.PondCaps + result.CrowdedBodies + result.Topology.Violations;
        return result;
    }

    private static string BiomeName(BiomeMap biomes, BiomeDatabase database, Vector2 point)
    {
        return database.Get(BiomeAt(biomes, point)).Type.ToString();
    }

    private static int BiomeAt(BiomeMap biomes, Vector2 point)
    {
        int x = Mathf.Clamp((int)(point.x / biomes.CellSize), 0, biomes.Resolution - 1);
        int z = Mathf.Clamp((int)(point.y / biomes.CellSize), 0, biomes.Resolution - 1);

        return biomes.Get(x, z);
    }

    private static void MeasureRoads(Result result, BiomeMap biomes, BiomeDatabase database, HeightMap final, WaterMap water, IEnumerable<Road> roads)
    {
        foreach (Road road in roads)
        {
            Vector2[] points = road.Points;

            for (int i = 0; i + 1 < points.Length; i++)
            {
                float length = Vector2.Distance(points[i], points[i + 1]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / ROAD_STEP));
                Vector2 direction = length < 1e-4f ? new Vector2(1f, 0f) : (points[i + 1] - points[i]) / length;
                var side = new Vector2(-direction.y, direction.x) * road.HalfWidth;

                for (int step = 0; step < steps; step++)
                {
                    Vector2 axis = Vector2.Lerp(points[i], points[i + 1], step / (float)steps);
                    result.RoadSamples++;

                    if (WaterCrossings.OverSpan(water, axis))
                    {
                        result.BridgeSamples++;
                        continue;
                    }

                    foreach (Vector2 point in new[] { axis, axis + side, axis - side })
                    {
                        WaterKind wet = Wet(final, water, point);

                        if (wet == WaterKind.None)
                            continue;

                        if (wet == WaterKind.River)
                            result.RoadInRiver++;
                        else
                        {
                            result.RoadOnStanding++;

                            if (water.FrozenAt(point.x, point.y))
                                result.RoadOnIce++;

                            string biome = BiomeName(biomes, database, point);
                            result.RoadWetByBiome[biome] = result.RoadWetByBiome.GetValueOrDefault(biome) + 1;
                        }

                        if (result.RoadWetExamples.Count < 12 && result.RoadWetExamples.TrueForAll(other => (other - point).sqrMagnitude > 400f * 400f))
                            result.RoadWetExamples.Add(point);

                        if (result.RoadWetExamples.Count == 1 && result.Near.Length == 0)
                            result.Near = Crossing(water, point) + $", дорога {road.Kind}";

                        break;
                    }
                }
            }
        }
    }

    private static string Crossing(WaterMap water, Vector2 point)
    {
        foreach (WaterCrossing crossing in water.Crossings)
        {
            if ((crossing.Position - point).sqrMagnitude < 60f * 60f)
                return $"; рядом {(crossing.Bridge ? "мост" : "труба")} в ({crossing.Position.x:0}, {crossing.Position.y:0}), река {crossing.RiverWidth:0.0} м, пролёт {crossing.Span:0.0} м";
        }

        return $"; переправы ближе 60 м нет, река {water.Sample(point.x, point.y).Width:0.0} м";
    }

    private static WaterKind Wet(HeightMap final, WaterMap water, Vector2 point)
    {
        if (point.x < 0f || point.y < 0f || point.x > water.WorldSize || point.y > water.WorldSize)
            return WaterKind.None;

        float ground = final.SampleWorldSmooth(point.x, point.y);
        WaterSample sample = water.Sample(point.x, point.y);

        if (!sample.IsWater)
            return WaterKind.None;

        if (sample.Kind == WaterKind.River)
            return ground < sample.Surface - ROAD_WET ? WaterKind.River : WaterKind.None;

        return ground < sample.Surface + ROAD_WET ? sample.Kind : WaterKind.None;
    }

    private static void MeasureBiomes(Result result, BiomeMap biomes, BiomeDatabase database, WaterMap water)
    {
        for (int cell = 0; cell < water.Kinds.Length; cell++)
        {
            var kind = (WaterKind)water.Kinds[cell];
            Vector2 center = water.CellCenter(cell);
            BiomeWater policy = database.Get(BiomeAt(biomes, center)).Water;
            bool wet = kind == WaterKind.Lake || kind == WaterKind.Pond || water.BodyIds[cell] >= 0;

            if (wet && policy == BiomeWater.None && !NearBiomeOtherThan(biomes, database, center, BiomeWater.None, WaterClimate.DRY_MARGIN))
                result.DryBiomeWater++;

            if (kind == WaterKind.None)
                continue;

            if (water.Frozen[cell] && policy != BiomeWater.Frozen && !NearBiomeOtherThan(biomes, database, center, policy, WaterClimate.SNOW_BORDER))
                result.FrozenOutsideSnow++;

            if (!water.Frozen[cell] && policy == BiomeWater.Frozen && kind != WaterKind.Sea && !NearBiomeOtherThan(biomes, database, center, BiomeWater.Frozen, WaterClimate.SNOW_BORDER + water.CellSize * 4f))
                result.LiquidInsideSnow++;
        }

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (point.Submerged)
                    continue;

                BiomeWater policy = database.Get(BiomeAt(biomes, point.Position)).Water;

                if (policy == BiomeWater.None && !NearBiomeOtherThan(biomes, database, point.Position, BiomeWater.None, WaterClimate.DRY_MARGIN))
                    result.DryBiomeWater++;
            }
        }
    }

    private static bool NearBiomeOtherThan(BiomeMap biomes, BiomeDatabase database, Vector2 center, BiomeWater policy, float reach)
    {
        for (int k = 0; k < 16; k++)
        {
            float angle = k * Mathf.PI / 8f;
            Vector2 probe = center + reach * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            if (database.Get(BiomeAt(biomes, probe)).Water != policy)
                return true;
        }

        return false;
    }

    private static void MeasureSea(Result result, WaterMap water)
    {
        int n = water.Resolution;
        var connected = new bool[n * n];
        var queue = new Queue<int>();

        for (int i = 0; i < n * n; i++)
        {
            if (water.Kinds[i] != (byte)WaterKind.Sea)
                continue;

            result.SeaCells++;
            int x = i % n, z = i / n;

            if (x != 0 && z != 0 && x != n - 1 && z != n - 1)
                continue;

            connected[i] = true;
            queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % n, z = cell / n;

            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz;

                if (nx < 0 || nz < 0 || nx >= n || nz >= n)
                    continue;

                int next = nz * n + nx;

                if (connected[next] || water.Kinds[next] != (byte)WaterKind.Sea)
                    continue;

                connected[next] = true;
                queue.Enqueue(next);
            }
        }

        for (int i = 0; i < n * n; i++)
            if (water.Kinds[i] == (byte)WaterKind.Sea && !connected[i])
                result.InlandSeaCells++;

        var reach = new List<float>();

        for (int side = 0; side < 4; side++)
        {
            int sea = 0, measured = 0;

            for (int t = n / 8; t < n - n / 8; t++)
            {
                int depth = 0;

                while (depth < n / 2 && Kind(water, side, t, depth) == WaterKind.Sea)
                    depth++;

                measured++;

                if (depth > 0)
                    sea++;

                reach.Add(depth * water.CellSize);
            }

            result.CoastSea[side] = sea / (float)measured;
        }

        result.CoastMean = reach.Average();
        result.CoastMin = reach.Min();
        result.CoastMax = reach.Max();
        result.CoastStd = Mathf.Sqrt(reach.Select(value => (value - result.CoastMean) * (value - result.CoastMean)).Average());
    }

    private static WaterKind Kind(WaterMap water, int side, int along, int depth)
    {
        int n = water.Resolution;
        int x = side switch { 0 => depth, 1 => n - 1 - depth, _ => along };
        int z = side switch { 2 => depth, 3 => n - 1 - depth, _ => along };

        return (WaterKind)water.Kinds[z * n + x];
    }

    private static void MeasureEnds(Result result, WaterMap water)
    {
        for (int river = 0; river < water.Rivers.Count; river++)
        {
            if (water.Rivers[river].Points.Count < 2)
                continue;

            if (RiverEnds.Start(water, river) == RiverEnd.Loose && !RiverEnds.Headwater(water, river, _startArea))
                result.LooseStarts++;

            if (RiverEnds.End(water, river) == RiverEnd.Loose)
                result.LooseEnds++;
        }

        foreach (WaterBody body in water.Bodies)
            if (body.SeedRadius > 0f && body.Kind == WaterKind.Pond && body.Area < 2000f)
                result.PondCaps++;
    }

    private static float _startArea = float.MaxValue;

    private static void MeasureCrowding(Result result, WaterMap water)
    {
        int n = water.Resolution;
        var owner = new int[n * n];
        var area = new Dictionary<int, int>();

        for (int i = 0; i < owner.Length; i++)
        {
            var kind = (WaterKind)water.Kinds[i];
            owner[i] = kind == WaterKind.Sea ? -2 : kind == WaterKind.Lake || kind == WaterKind.Pond ? water.BodyIds[i] : -1;

            if (owner[i] >= 0)
                area[owner[i]] = area.GetValueOrDefault(owner[i]) + 1;
        }

        result.Bodies = area.Count;
        int reach = Mathf.CeilToInt(CROWD_GAP / water.CellSize);
        var riverCells = new HashSet<int>();

        foreach (RiverPath river in water.Rivers)
            foreach (RiverPoint point in river.Points)
                riverCells.Add(water.CellIndex(point.Position.x, point.Position.y));

        foreach ((int body, int cells) in area)
        {
            float square = cells * water.CellSize * water.CellSize;

            if (square >= SMALL_BODY)
                continue;

            bool onRiver = false;
            bool crowded = false;
            Vector2 at = default;

            for (int i = 0; i < owner.Length && !crowded; i++)
            {
                if (owner[i] != body)
                    continue;

                onRiver |= riverCells.Contains(i);
                int x = i % n, z = i / n;

                for (int dz = -reach; dz <= reach && !crowded; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int nx = x + dx, nz = z + dz;

                        if (nx < 0 || nz < 0 || nx >= n || nz >= n || dx * dx + dz * dz > reach * reach)
                            continue;

                        int other = owner[nz * n + nx];

                        if (other == -1 || other == body)
                            continue;

                        crowded = true;
                        at = water.CellCenter(i);
                        result.CrowdedPairs.Add($"{body}({square:0} м²)~{(other == -2 ? "море" : other.ToString())}");
                        break;
                    }
                }
            }

            if (!crowded || onRiver)
                continue;

            result.CrowdedBodies++;

            if (result.CrowdedExamples.Count < 12)
                result.CrowdedExamples.Add(at);
        }
    }

    private static void MeasureTrunk(Result result, WorldGenerationConfig config, BiomeMap biomes, BiomeDatabase database, WaterMap water)
    {
        if (!config.Water.ThroughRiver || water.Rivers.Count == 0)
            return;

        RiverPath trunk = water.Rivers.OrderByDescending(river => river.Points.Count == 0 ? 0f : river.Points.Max(point => point.Width) * river.Length).First();
        List<RiverPoint> points = trunk.Points;

        if (points.Count < 2)
            return;

        float rise = 0f, minWidth = float.MaxValue, maxWidth = 0f;
        var shares = new Dictionary<string, int>();
        int open = 0;

        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
                rise = Mathf.Max(rise, points[i].Surface - points[i - 1].Surface);

            if (points[i].Submerged)
                continue;

            open++;
            minWidth = Mathf.Min(minWidth, points[i].Width);
            maxWidth = Mathf.Max(maxWidth, points[i].Width);
            string biome = BiomeName(biomes, database, points[i].Position);
            shares[biome] = shares.GetValueOrDefault(biome) + 1;
        }

        int index = water.Rivers.IndexOf(trunk);
        RiverEnd start = RiverEnds.Start(water, index);
        RiverEnd end = RiverEnds.End(water, index);
        float span = Vector2.Distance(points[0].Position, points[^1].Position);

        result.TrunkOk = trunk.Length >= 0.3f * config.WorldSize && end != RiverEnd.Loose && rise <= 1e-3f && maxWidth >= 24f;
        result.Trunk = $"река {index}, длина {trunk.Length / 1000f:0.0} км, от истока до устья по прямой {span / 1000f:0.0} км, ширина {minWidth:0}..{maxWidth:0} м, "
            + $"уровень {points[0].Surface:0.0} → {points[^1].Surface:0.0} м (подъём не больше {rise:0.000} м), исток {start}, устье {end}, "
            + $"по биомам {string.Join(", ", shares.Select(pair => $"{pair.Key} {pair.Value / (float)Mathf.Max(1, open):P0}"))}"
            + (result.TrunkOk ? "" : " — НЕ ПРОХОДИТ");
    }
}

static class WaterShapeAudit
{
    private const float GRID = 4f;
    private const float PLAIN_GRID = 8f;
    private const float SHORE_GAP = 40f;
    private const float PARALLEL = 0.5f;
    private const float MOUTH_REACH = 60f;
    private const float MOUTH_WIDTHS = 3f;
    private const float APPROACH = 40f;
    private const float NORMAL_REACH = 60f;
    private const float STRIP = 30f;
    private const float STRIP_MOUTH = 40f;
    private const float STRIP_LAND = 0.5f;
    private const float CHANNEL_PAD = 8f;
    private const float FLAT = 0.05f;
    private const float PLAIN_AREA = 20000f;
    private const float PLAIN_LAND = 0.05f;
    private const float MOAT_DIP = 2f;
    private const float TRANSECT_STEP = 32f;
    private const float MOAT_LAND = 0.3f;
    private const float MOAT_TOLERANCE = 24f;
    private const int MOAT_RUN = 5;
    private const float SPREAD = 300f;
    private const int EXAMPLES = 8;

    private readonly struct Mouth
    {
        public readonly Vector2 At;
        public readonly Vector2 Direction;
        public readonly float Width;
        public readonly bool Inflow;
        public readonly short Owner;

        public Mouth(Vector2 at, Vector2 direction, float width, bool inflow, short owner)
        {
            At = at;
            Direction = direction;
            Width = width;
            Inflow = inflow;
            Owner = owner;
        }
    }

    public sealed class Result
    {
        public float AlongShore, OpenLength;
        public int Stretches;
        public readonly List<(Vector2 At, float Length, int River)> LongStretches = new();
        public float StripArea;
        public readonly List<Vector2> StripExamples = new();
        public readonly List<float> MouthAngles = new();
        public readonly List<(Vector2 At, float Angle, WaterKind Into)> SteepMouths = new();
        public float FlatArea;
        public int Plains;
        public readonly List<(Vector2 At, float Area, float Level)> BigPlains = new();
        public int Lakes, RiverLakes, NoOutflow, NoRiver;
        public readonly List<Vector2> NoOutflowExamples = new();
        public int Transects;
        public float CoastTrough, ControlTrough;
        public readonly List<(Vector2 At, float Length)> Moats = new();

        public string Describe()
        {
            var text = new StringBuilder();
            float[] angles = MouthAngles.OrderBy(angle => angle).ToArray();
            float mean = angles.Length == 0 ? 0f : angles.Average();
            float median = angles.Length == 0 ? 0f : angles[angles.Length / 2];
            int over45 = angles.Count(angle => angle > 45f), over60 = angles.Count(angle => angle > 60f);

            text.AppendLine("форма воды:");
            text.AppendLine($"  реки вдоль берега (ближе {SHORE_GAP:0} м к стоячей воде, под углом больше 60° к нормали берега, вне устьев): {AlongShore:0} м из {OpenLength / 1000f:0.0} км открытых рек, участков {Stretches}"
                + (LongStretches.Count == 0 ? "" : $"; самые длинные: {string.Join(" ", LongStretches.OrderByDescending(item => item.Length).Take(EXAMPLES).Select(item => $"{Format(item.At)} {item.Length:0} м р.{item.River}"))}"));
            text.AppendLine($"  тонкие полосы суши между рекой и стоячей водой вне её русла (уже {STRIP:0} м, выше воды на {STRIP_LAND:0.0} м, вне устьев): {StripArea:N0} м²" + (StripExamples.Count == 0 ? "" : $"; примеры {string.Join(" ", StripExamples.Take(EXAMPLES).Select(Format))}"));
            text.AppendLine($"  устья в стоячую воду: {angles.Length}, угол к нормали берега средний {mean:0}°, медиана {median:0}°, круче 45° {over45}, круче 60° {over60}"
                + (SteepMouths.Count == 0 ? "" : $"; худшие {string.Join(" ", SteepMouths.OrderByDescending(item => item.Angle).Take(EXAMPLES).Select(item => $"{Format(item.At)} {item.Angle:0}° {item.Into}"))}"));
            text.AppendLine($"  мёртвые равнины (перепад меньше {FLAT * 100f:0} см на {2f * PLAIN_GRID:0} м, пятна от {PLAIN_AREA:N0} м²): {FlatArea / 1e6f:0.00} км² в {Plains} пятнах"
                + (BigPlains.Count == 0 ? "" : $"; крупнейшие {string.Join(" ", BigPlains.OrderByDescending(item => item.Area).Take(EXAMPLES).Select(item => $"{Format(item.At)} {item.Area / 1e6f:0.00} км² на {item.Level:0.0} м"))}"));
            text.AppendLine($"  озёра и пруды: {Lakes}, с впадающей рекой {RiverLakes}, из них без вытекающей {NoOutflow}, без рек {NoRiver}" + (NoOutflowExamples.Count == 0 ? "" : $"; без стока {string.Join(" ", NoOutflowExamples.Take(EXAMPLES).Select(Format))}"));
            text.Append($"  ров вдоль берега (замкнутый провал глубже {MOAT_DIP:0} м на одном расстоянии от берега ±{MOAT_TOLERANCE:0} м хотя бы на {MOAT_RUN * TRANSECT_STEP:0} м вдоль берега; {Transects} разрезов через {TRANSECT_STEP:0} м): в прибрежной полосе {CoastTrough:0} м, в такой же полосе дальше от моря {ControlTrough:0} м"
                + (Moats.Count == 0 ? "" : $"; длиннейшие {string.Join(" ", Moats.OrderByDescending(item => item.Length).Take(EXAMPLES).Select(item => $"{Format(item.At)} {item.Length:0} м"))}"));

            return text.ToString();
        }

        private static string Format(Vector2 point) => $"({point.x:0}, {point.y:0})";
    }

    public static Result Measure(WorldGenerationConfig config, HeightMap map, WaterMap water)
    {
        var result = new Result();
        int n = Mathf.CeilToInt(water.WorldSize / GRID);
        var standing = new bool[n * n];
        var river = new bool[n * n];
        water.TryRiver(0f, 0f, out _);

        Parallel.For(0, n, row =>
        {
            for (int column = 0; column < n; column++)
            {
                float x = (column + 0.5f) * GRID, z = (row + 0.5f) * GRID;
                int index = row * n + column;
                standing[index] = water.StandingAt(x, z, out _);
                river[index] = !standing[index] && water.TryRiver(x, z, out _);
            }
        });

        float[] toStanding = Distance(standing, n);
        float[] toRiver = Distance(river, n);
        List<Mouth> mouths = Mouths(water, out List<float>[] transitions);

        bool[] open = Beside(water, standing, n);

        AlongShore(result, water, toStanding, n, transitions);
        Strips(result, map, water, standing, river, Distance(open, n), toRiver, n, mouths);
        MouthAngles(result, water, standing, n, mouths);
        Plains(result, config, map, standing, river, n);
        Lakes(result, water, mouths);
        Moat(result, config, map, water);

        return result;
    }

    private static bool[] Beside(WaterMap water, bool[] standing, int n)
    {
        var open = (bool[])standing.Clone();

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                float radius = 0.5f * point.Width + CHANNEL_PAD;
                int reach = Mathf.CeilToInt(radius / GRID);
                int cx = (int)(point.Position.x / GRID), cz = (int)(point.Position.y / GRID);

                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = cx + dx, z = cz + dz;

                        if (x >= 0 && z >= 0 && x < n && z < n && (dx * dx + dz * dz) * GRID * GRID <= radius * radius)
                            open[z * n + x] = false;
                    }
                }
            }
        }

        return open;
    }

    private static float[] Distance(bool[] seed, int n)
    {
        const double FAR = 1e18;
        var squared = new double[n * n];

        Parallel.For(0, n, row =>
        {
            var f = new double[n];
            var d = new double[n];
            var v = new int[n];
            var z = new double[n + 1];

            for (int column = 0; column < n; column++)
                f[column] = seed[row * n + column] ? 0.0 : FAR;

            Transform(f, d, v, z, n);

            for (int column = 0; column < n; column++)
                squared[row * n + column] = d[column];
        });

        var result = new float[n * n];

        Parallel.For(0, n, column =>
        {
            var f = new double[n];
            var d = new double[n];
            var v = new int[n];
            var z = new double[n + 1];

            for (int row = 0; row < n; row++)
                f[row] = squared[row * n + column];

            Transform(f, d, v, z, n);

            for (int row = 0; row < n; row++)
                result[row * n + column] = (float)Math.Sqrt(Math.Min(d[row], 1e12)) * GRID;
        });

        return result;
    }

    private static void Transform(double[] f, double[] d, int[] v, double[] z, int n)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;

        for (int q = 1; q < n; q++)
        {
            double s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);

            while (s <= z[k])
            {
                k--;
                s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;

        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
                k++;

            d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
        }
    }

    private static int Cell(Vector2 point, int n)
    {
        int column = Mathf.Clamp((int)(point.x / GRID), 0, n - 1);
        int row = Mathf.Clamp((int)(point.y / GRID), 0, n - 1);

        return row * n + column;
    }

    private static List<Mouth> Mouths(WaterMap water, out List<float>[] transitions)
    {
        var mouths = new List<Mouth>();
        transitions = new List<float>[water.Rivers.Count];

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            transitions[r] = new List<float>();

            if (points.Count < 2)
                continue;

            float[] along = Along(points);

            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (points[i].Submerged == points[i + 1].Submerged)
                    continue;

                bool inflow = !points[i].Submerged;
                int open = inflow ? i : i + 1;
                transitions[r].Add(along[open]);
                short owner = Owner(water, points[inflow ? i + 1 : i].Position, Vector2.zero, 0f);
                mouths.Add(new Mouth(points[open].Position, Direction(points, along, open, inflow), points[open].Width, inflow, owner));
            }

            if (!points[^1].Submerged && RiverEnds.End(water, r) == RiverEnd.Standing)
            {
                int last = points.Count - 1;
                Vector2 direction = Direction(points, along, last, true);
                transitions[r].Add(along[last]);
                mouths.Add(new Mouth(points[last].Position, direction, points[last].Width, true, Owner(water, points[last].Position, direction, points[last].Width + 16f)));
            }

            if (!points[0].Submerged && RiverEnds.Start(water, r) == RiverEnd.Standing)
            {
                Vector2 direction = Direction(points, along, 0, false);
                transitions[r].Add(0f);
                mouths.Add(new Mouth(points[0].Position, direction, points[0].Width, false, Owner(water, points[0].Position, -direction, points[0].Width + 16f)));
            }
        }

        return mouths;
    }

    private static float[] Along(List<RiverPoint> points)
    {
        var along = new float[points.Count];

        for (int i = 1; i < points.Count; i++)
            along[i] = along[i - 1] + Vector2.Distance(points[i - 1].Position, points[i].Position);

        return along;
    }

    private static Vector2 Direction(List<RiverPoint> points, float[] along, int at, bool upstream)
    {
        int other = at;

        if (upstream)
        {
            while (other > 0 && along[at] - along[other] < APPROACH)
                other--;
        }
        else
        {
            while (other + 1 < points.Count && along[other] - along[at] < APPROACH)
                other++;
        }

        Vector2 delta = upstream ? points[at].Position - points[other].Position : points[other].Position - points[at].Position;

        return delta.sqrMagnitude < 1e-6f ? Vector2.zero : delta.normalized;
    }

    private static short Owner(WaterMap water, Vector2 at, Vector2 direction, float reach)
    {
        for (float step = 0f; step <= reach; step += 2f)
        {
            Vector2 probe = at + direction * step;

            if (water.Covers(probe.x, probe.y, out short owner))
                return owner;
        }

        for (int k = 0; k < 8; k++)
        {
            float angle = k * Mathf.PI / 4f;
            Vector2 probe = at + (reach + 8f) * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            if (water.Covers(probe.x, probe.y, out short owner))
                return owner;
        }

        return WaterMap.OWNER_NONE;
    }

    private static bool NearMouth(List<float> transitions, float along, float width)
    {
        float reach = MOUTH_REACH + MOUTH_WIDTHS * width;

        foreach (float transition in transitions)
        {
            if (Mathf.Abs(transition - along) < reach)
                return true;
        }

        return false;
    }

    private static Vector2 Gradient(float[] field, int n, Vector2 point)
    {
        int column = Mathf.Clamp((int)(point.x / GRID), 2, n - 3);
        int row = Mathf.Clamp((int)(point.y / GRID), 2, n - 3);

        return new Vector2(field[row * n + column + 2] - field[row * n + column - 2], field[(row + 2) * n + column] - field[(row - 2) * n + column]);
    }

    private static void AlongShore(Result result, WaterMap water, float[] toStanding, int n, List<float>[] transitions)
    {
        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;

            if (points.Count < 2)
                continue;

            float[] along = Along(points);
            float stretch = 0f;
            Vector2 start = default;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                bool flagged = false;

                if (!points[i].Submerged && !points[i + 1].Submerged)
                {
                    Vector2 delta = points[i + 1].Position - points[i].Position;
                    float length = delta.magnitude;
                    Vector2 middle = 0.5f * (points[i].Position + points[i + 1].Position);
                    result.OpenLength += length;
                    float gap = toStanding[Cell(middle, n)] - 0.5f * points[i].Width;

                    if (length > 1e-3f && gap < SHORE_GAP && !NearMouth(transitions[r], 0.5f * (along[i] + along[i + 1]), points[i].Width))
                    {
                        Vector2 normal = Gradient(toStanding, n, middle);

                        if (normal.sqrMagnitude > 1e-2f && Mathf.Abs(Vector2.Dot(delta / length, normal.normalized)) < PARALLEL)
                        {
                            flagged = true;
                            result.AlongShore += length;

                            if (stretch <= 0f)
                                start = middle;

                            stretch += length;
                        }
                    }
                }

                if (flagged || stretch <= 0f)
                    continue;

                Close(result, start, stretch, r);
                stretch = 0f;
            }

            if (stretch > 0f)
                Close(result, start, stretch, r);
        }
    }

    private static void Close(Result result, Vector2 start, float stretch, int river)
    {
        result.Stretches++;
        result.LongStretches.Add((start, stretch, river));
    }

    private static void Strips(Result result, HeightMap map, WaterMap water, bool[] standing, bool[] river, float[] toStanding, float[] toRiver, int n, List<Mouth> mouths)
    {
        var mouthZone = new bool[n * n];

        foreach (Mouth mouth in mouths)
        {
            float radius = STRIP_MOUTH + mouth.Width;
            int reach = Mathf.CeilToInt(radius / GRID);
            int cx = (int)(mouth.At.x / GRID), cz = (int)(mouth.At.y / GRID);

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;

                    if (x >= 0 && z >= 0 && x < n && z < n && dx * dx + dz * dz <= reach * reach)
                        mouthZone[z * n + x] = true;
                }
            }
        }

        for (int i = 0; i < n * n; i++)
        {
            if (standing[i] || river[i] || mouthZone[i] || toRiver[i] + toStanding[i] >= STRIP)
                continue;

            var at = new Vector2((i % n + 0.5f) * GRID, (i / n + 0.5f) * GRID);

            if (map.SampleWorldSmooth(at.x, at.y) < water.ShoreSurface[water.CellIndex(at.x, at.y)] + STRIP_LAND)
                continue;

            result.StripArea += GRID * GRID;

            if (result.StripExamples.Count < EXAMPLES && result.StripExamples.TrueForAll(other => (other - at).sqrMagnitude > SPREAD * SPREAD))
                result.StripExamples.Add(at);
        }
    }

    private static void MouthAngles(Result result, WaterMap water, bool[] standing, int n, List<Mouth> mouths)
    {
        int reach = Mathf.CeilToInt(NORMAL_REACH / GRID);

        foreach (Mouth mouth in mouths)
        {
            if (!mouth.Inflow || mouth.Direction.sqrMagnitude < 1e-6f)
                continue;

            int cx = (int)(mouth.At.x / GRID), cz = (int)(mouth.At.y / GRID);
            Vector2 sum = Vector2.zero;
            int count = 0;

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx, z = cz + dz;

                    if (x < 0 || z < 0 || x >= n || z >= n || dx * dx + dz * dz > reach * reach || !standing[z * n + x])
                        continue;

                    sum += new Vector2(dx, dz);
                    count++;
                }
            }

            if (count == 0 || sum.sqrMagnitude < 1e-6f)
                continue;

            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(mouth.Direction, sum.normalized), -1f, 1f)) * 57.29578f;
            result.MouthAngles.Add(angle);

            if (angle > 45f)
                result.SteepMouths.Add((mouth.At, angle, water.KindOf(mouth.Owner)));
        }
    }

    private static void Plains(Result result, WorldGenerationConfig config, HeightMap map, bool[] standing, bool[] river, int n)
    {
        int m = Mathf.CeilToInt(map.WorldSize / PLAIN_GRID);
        var heights = new float[m * m];
        var land = new bool[m * m];
        float sea = config.SeaLevel;

        Parallel.For(0, m, row =>
        {
            for (int column = 0; column < m; column++)
            {
                var at = new Vector2((column + 0.5f) * PLAIN_GRID, (row + 0.5f) * PLAIN_GRID);
                int index = row * m + column;
                int fine = Cell(at, n);
                heights[index] = map.SampleWorldSmooth(at.x, at.y);
                land[index] = heights[index] > sea + PLAIN_LAND && !standing[fine] && !river[fine];
            }
        });

        var flat = new bool[m * m];

        Parallel.For(1, m - 1, row =>
        {
            for (int column = 1; column < m - 1; column++)
            {
                int index = row * m + column;

                if (!land[index])
                    continue;

                float low = float.MaxValue, high = float.MinValue;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float h = heights[index + dz * m + dx];
                        low = Mathf.Min(low, h);
                        high = Mathf.Max(high, h);
                    }
                }

                flat[index] = high - low < FLAT;
            }
        });

        var seen = new bool[m * m];
        var queue = new Queue<int>();

        for (int seed = 0; seed < m * m; seed++)
        {
            if (!flat[seed] || seen[seed])
                continue;

            seen[seed] = true;
            queue.Enqueue(seed);
            int cells = 0;
            double sumX = 0, sumZ = 0, sumH = 0;

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int x = cell % m, z = cell / m;
                cells++;
                sumX += x;
                sumZ += z;
                sumH += heights[cell];

                foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;

                    if (nx < 0 || nz < 0 || nx >= m || nz >= m)
                        continue;

                    int next = nz * m + nx;

                    if (!flat[next] || seen[next])
                        continue;

                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }

            float area = cells * PLAIN_GRID * PLAIN_GRID;

            if (area < PLAIN_AREA)
                continue;

            result.FlatArea += area;
            result.Plains++;
            result.BigPlains.Add((new Vector2((float)(sumX / cells + 0.5) * PLAIN_GRID, (float)(sumZ / cells + 0.5) * PLAIN_GRID), area, (float)(sumH / cells)));
        }
    }

    private static void Lakes(Result result, WaterMap water, List<Mouth> mouths)
    {
        int count = water.Bodies.Count;
        var inflow = new int[count];
        var outflow = new int[count];

        foreach (Mouth mouth in mouths)
        {
            if (mouth.Owner < 0 || mouth.Owner >= count)
                continue;

            if (mouth.Inflow)
                inflow[mouth.Owner]++;
            else
                outflow[mouth.Owner]++;
        }

        for (int body = 0; body < count; body++)
        {
            WaterKind kind = water.Bodies[body].Kind;

            if (kind != WaterKind.Lake && kind != WaterKind.Pond)
                continue;

            result.Lakes++;

            if (inflow[body] == 0 && outflow[body] == 0)
                result.NoRiver++;

            if (inflow[body] == 0)
                continue;

            result.RiverLakes++;

            if (outflow[body] > 0)
                continue;

            result.NoOutflow++;
            result.NoOutflowExamples.Add(water.Bodies[body].Center);
        }
    }

    private static void Moat(Result result, WorldGenerationConfig config, HeightMap map, WaterMap water)
    {
        if (!CoastShaper.Active(config))
            return;

        float band = CoastShaper.Width(config) * (1f + config.Water.CoastVariation);
        float world = map.WorldSize;
        int samples = Mathf.CeilToInt(3f * band / GRID);
        var heights = new float[samples + 1];
        var coast = new List<float>();
        var control = new List<float>();
        var at = new List<Vector2>();

        for (int side = 0; side < 4; side++)
        {
            coast.Clear();
            control.Clear();
            at.Clear();

            for (float t = band + TRANSECT_STEP * 0.5f; t < world - band; t += TRANSECT_STEP)
            {
                Vector2 origin = side switch { 0 => new Vector2(0f, t), 1 => new Vector2(world, t), 2 => new Vector2(t, 0f), _ => new Vector2(t, world) };
                Vector2 inward = side switch { 0 => new Vector2(1f, 0f), 1 => new Vector2(-1f, 0f), 2 => new Vector2(0f, 1f), _ => new Vector2(0f, -1f) };
                int shore = -1;

                for (int s = 0; s <= samples; s++)
                {
                    Vector2 point = origin + inward * (s * GRID);
                    float ground = map.SampleWorldSmooth(point.x, point.y);

                    if (water.StandingAt(point.x, point.y, out float surface))
                        ground = Mathf.Max(ground, surface);

                    heights[s] = ground;

                    if (shore < 0 && ground > config.SeaLevel + MOAT_LAND)
                        shore = s;
                }

                int width = Mathf.CeilToInt(band / GRID);
                result.Transects++;
                coast.Add(shore < 0 || shore + 2 * width > samples ? -1f : Trough(heights, shore, shore + width, config.SeaLevel) * GRID);
                control.Add(shore < 0 || shore + 2 * width > samples ? -1f : Trough(heights, shore + width, shore + 2 * width, config.SeaLevel) * GRID);
                at.Add(origin + inward * ((shore < 0 ? 0 : shore) * GRID));
            }

            result.CoastTrough += Runs(coast, at, result.Moats);
            result.ControlTrough += Runs(control, at, null);
        }
    }

    private static int Trough(float[] heights, int from, int to, float sea)
    {
        float peak = heights[from], deepest = 0f;
        int at = -1;

        for (int s = from; s <= to; s++)
        {
            peak = Mathf.Max(peak, heights[s]);

            if (heights[s] <= sea + MOAT_LAND || peak - heights[s] <= deepest)
                continue;

            float rise = 0f;

            for (int k = s + 1; k <= to; k++)
                rise = Mathf.Max(rise, heights[k] - heights[s]);

            if (rise < MOAT_DIP)
                continue;

            deepest = Mathf.Min(peak - heights[s], rise);
            at = s;
        }

        return deepest > MOAT_DIP ? at - from : -1;
    }

    private static float Runs(List<float> dips, List<Vector2> at, List<(Vector2 At, float Length)> examples)
    {
        float total = 0f;
        int start = 0;

        for (int i = 1; i <= dips.Count; i++)
        {
            bool continues = i < dips.Count && dips[i] >= 0f && dips[i - 1] >= 0f && Mathf.Abs(dips[i] - dips[i - 1]) <= MOAT_TOLERANCE;

            if (continues)
                continue;

            int length = i - start;

            if (dips[start] >= 0f && length >= MOAT_RUN)
            {
                total += length * TRANSECT_STEP;
                examples?.Add((at[start], length * TRANSECT_STEP));
            }

            start = i;
        }

        return total;
    }
}
