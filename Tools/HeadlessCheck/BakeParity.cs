using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

static class BakeParity
{
    private const int IMAGE = 2048;
    private const float FAR = 1f;
    private const float NEAR_POI = 20f;
    private const float POINT_MATCH = 0.5f;

    public static void Compare(WorldMapResult world, string generated, WorldGenerationConfig config, string[] args)
    {
        HeightMap baked = HeightMap.FromRaw16(File.ReadAllBytes($"{generated}/HeightMap.bytes"), config.HeightMapResolution, config.WorldSize, config.MaxHeight);
        WaterMap bakedWater = WaterMapFormat.Read(File.ReadAllBytes($"{generated}/WaterMap.bytes"));

        Water(world.Water, bakedWater);
        Roads(world.Roads, $"{generated}/RoadNetwork.asset");
        Pois(world.Placements, $"{generated}/PoiPlacement.asset");
        Heights(world, baked, config, Image(args));
    }

    public static void Stamps(string assetPath)
    {
        WaterStampDatabase traced = WaterStampLoader.Load(false);
        Dictionary<string, Dictionary<string, List<string>>> stored = ReadStamps(assetPath);
        int differing = 0;

        foreach (WaterStampDefinition definition in traced.Stamps)
        {
            if (!stored.TryGetValue(definition.Name, out Dictionary<string, List<string>> fields))
            {
                Console.WriteLine($"  {definition.Name}: нет в ассете");
                differing++;
                continue;
            }

            var notes = new List<string>();

            foreach (var property in typeof(WaterStampDefinition).GetProperties())
            {
                if (!fields.TryGetValue(property.Name, out List<string> values))
                    continue;

                object ours = property.GetValue(definition);
                string note = Difference(ours, values);

                if (note != null)
                    notes.Add($"{property.Name} {note}");
            }

            if (notes.Count == 0)
                continue;

            differing++;
            Console.WriteLine($"  {definition.Name}: {string.Join("; ", notes)}");
        }

        Console.WriteLine($"сверка трассировки штампов воды с ассетом: {traced.Stamps.Count} штампов, расходятся {differing}");
    }

    private static string Difference(object ours, List<string> values)
    {
        switch (ours)
        {
            case float number:
                float stored = float.Parse(values[0], CultureInfo.InvariantCulture);
                return Mathf.Abs(number - stored) > 1e-4f * Mathf.Max(1f, Mathf.Abs(stored)) ? $"{number} / {stored}" : null;
            case Vector2 vector:
                Vector2 parsed = Vector(values[0]);
                return (vector - parsed).sqrMagnitude > 1e-8f ? $"{vector} / {parsed}" : null;
            case Vector2[] array:
                return Arrays(array.Length, values.Count, i => (array[i] - Vector(values[i])).magnitude);
            case float[] array:
                return Arrays(array.Length, values.Count, i => Mathf.Abs(array[i] - float.Parse(values[i], CultureInfo.InvariantCulture)));
            default:
                return null;
        }
    }

    private static string Arrays(int ours, int stored, Func<int, float> difference)
    {
        if (ours != stored)
            return $"длина {ours} / {stored}";

        float worst = 0f;

        for (int i = 0; i < ours; i++)
            worst = Mathf.Max(worst, difference(i));

        return worst > 1e-3f ? $"до {worst:0.000}" : null;
    }

    private static Vector2 Vector(string text)
    {
        string[] parts = text.Trim('{', '}', ' ').Split(',');

        return new Vector2(float.Parse(parts[0].Split(':')[1], CultureInfo.InvariantCulture), float.Parse(parts[1].Split(':')[1], CultureInfo.InvariantCulture));
    }

    public static void ApplyStoredTraces(IEnumerable<WaterStampDefinition> definitions, string assetPath)
    {
        Dictionary<string, Dictionary<string, List<string>>> stored = ReadStamps(assetPath);

        foreach (WaterStampDefinition definition in definitions)
        {
            if (!stored.TryGetValue(definition.Name, out Dictionary<string, List<string>> fields))
                continue;

            foreach (string name in Traced)
            {
                if (!fields.TryGetValue(name, out List<string> values))
                    continue;

                System.Reflection.FieldInfo field = typeof(WaterStampDefinition).GetField($"<{name}>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field.SetValue(definition, Parse(field.FieldType, values));
            }

            for (int b = 0; b < definition.Branches.Count; b++)
            {
                if (fields.TryGetValue($"Branch{b}.Path", out List<string> path) && fields.TryGetValue($"Branch{b}.JunctionIndex", out List<string> junction))
                    definition.Branches[b].SetPath((Vector2[])Parse(typeof(Vector2[]), path), int.Parse(junction[0], CultureInfo.InvariantCulture));
            }
        }
    }

    private static readonly string[] Traced =
    {
        "Centerline", "CoreHalfWidths", "MedianCoreHalfWidth", "CorridorHalfWidth", "BankShoulder", "ChordLength", "PathLength", "MaxDeviation",
        "WaterArea", "WaterCentroid", "WaterAxis", "WaterMajor", "WaterMinor"
    };

    private static object Parse(Type type, List<string> values)
    {
        if (type == typeof(float))
            return float.Parse(values[0], CultureInfo.InvariantCulture);

        if (type == typeof(Vector2))
            return Vector(values[0]);

        if (type == typeof(float[]))
            return values.ConvertAll(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        return values.ConvertAll(Vector).ToArray();
    }

    private static Dictionary<string, Dictionary<string, List<string>>> ReadStamps(string path)
    {
        var stamps = new Dictionary<string, Dictionary<string, List<string>>>();
        Dictionary<string, List<string>> current = null;
        List<string> list = null;
        int branch = -1;
        const string FIELD = "    <";
        const string SUFFIX = ">k__BackingField:";

        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("  - <Id>", StringComparison.Ordinal))
            {
                current = new Dictionary<string, List<string>>();
                list = null;
                branch = -1;
                continue;
            }

            if (current == null)
                continue;

            if (line.StartsWith("    - <Kind>", StringComparison.Ordinal))
            {
                branch++;
                list = null;
                continue;
            }

            if (branch >= 0 && line.StartsWith("      <", StringComparison.Ordinal))
            {
                int end = line.IndexOf(SUFFIX, StringComparison.Ordinal);
                string name = line.Substring(7, end - 7);
                string value = line.Substring(end + SUFFIX.Length).Trim();
                list = new List<string>();
                current[$"Branch{branch}.{name}"] = list;

                if (value.Length > 0 && value != "[]")
                    list.Add(value);

                continue;
            }

            if (branch >= 0 && list != null && line.StartsWith("      - ", StringComparison.Ordinal))
            {
                list.Add(line.Substring(8).Trim());
                continue;
            }

            if (line.StartsWith(FIELD, StringComparison.Ordinal) || line.StartsWith("  - ", StringComparison.Ordinal) && line.Contains(SUFFIX))
            {
                int end = line.IndexOf(SUFFIX, StringComparison.Ordinal);

                if (end < 0)
                    continue;

                string name = line.Substring(line.IndexOf('<') + 1, end - line.IndexOf('<') - 1);
                string value = line.Substring(end + SUFFIX.Length).Trim();
                list = new List<string>();
                current[name] = list;

                if (value.Length > 0 && value != "[]")
                    list.Add(value);

                if (name == "Name")
                    stamps[value] = current;

                continue;
            }

            if (list != null && line.StartsWith("    - ", StringComparison.Ordinal))
                list.Add(line.Substring(6).Trim());
            else
                list = null;
        }

        return stamps;
    }

    private static string Image(string[] args)
    {
        int at = Array.IndexOf(args, "--compare-bake");

        return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : null;
    }

    private static void Water(WaterMap ours, WaterMap theirs)
    {
        Console.WriteLine($"сверка воды: рек {ours.Rivers.Count} / {theirs.Rivers.Count}, водоёмов {ours.Bodies.Count} / {theirs.Bodies.Count}");
        int rivers = Math.Min(ours.Rivers.Count, theirs.Rivers.Count);

        for (int r = 0; r < rivers; r++)
        {
            List<RiverPoint> a = ours.Rivers[r].Points, b = theirs.Rivers[r].Points;
            float position = 0f, surface = 0f, width = 0f;
            int submerged = 0;

            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                position = Mathf.Max(position, Vector2.Distance(a[i].Position, b[i].Position));
                surface = Mathf.Max(surface, Mathf.Abs(a[i].Surface - b[i].Surface));
                width = Mathf.Max(width, Mathf.Abs(a[i].Width - b[i].Width));
                submerged += a[i].Submerged != b[i].Submerged ? 1 : 0;
            }

            if (a.Count != b.Count || position > 0.01f || surface > 0.01f || width > 0.01f || submerged > 0)
                Console.WriteLine($"  река {r}: точек {a.Count} / {b.Count}, сдвиг до {position:0.00} м, уровень до {surface:0.00} м, ширина до {width:0.00} м, затопление расходится в {submerged}");
        }

        int bodies = Math.Min(ours.Bodies.Count, theirs.Bodies.Count);

        for (int k = 0; k < bodies; k++)
        {
            WaterBody a = ours.Bodies[k], b = theirs.Bodies[k];

            if (a.Kind != b.Kind || Mathf.Abs(a.Surface - b.Surface) > 0.01f || Mathf.Abs(a.Area - b.Area) > 64f)
                Console.WriteLine($"  водоём {k}: {a.Kind} / {b.Kind}, уровень {a.Surface:0.00} / {b.Surface:0.00}, площадь {a.Area:0} / {b.Area:0} м²");
        }

        int kinds = 0, ids = 0;

        for (int c = 0; c < Math.Min(ours.Kinds.Length, theirs.Kinds.Length); c++)
        {
            kinds += ours.Kinds[c] != theirs.Kinds[c] ? 1 : 0;
            ids += ours.BodyIds[c] != theirs.BodyIds[c] ? 1 : 0;
        }

        Console.WriteLine($"  клеток с другим видом воды {kinds}, с другим водоёмом {ids} из {ours.Kinds.Length}");
    }

    private static void Roads(RoadNetwork ours, string path)
    {
        (List<Road> regional, List<Road> streets, _) = RoadAudit.Read(path);

        Console.WriteLine($"сверка дорог: трасс {ours.Roads.Count} / {regional.Count}, {Length(ours.Roads) / 1000f:0.00} / {Length(regional) / 1000f:0.00} км, совпало точно {Same(ours.Roads, regional)}; "
            + $"улиц {ours.Streets.Count} / {streets.Count}, {Length(ours.Streets) / 1000f:0.00} / {Length(streets) / 1000f:0.00} км, совпало точно {Same(ours.Streets, streets)}");
    }

    private static float Length(List<Road> roads)
    {
        float total = 0f;

        foreach (Road road in roads)
        {
            for (int i = 1; i < road.Points.Length; i++)
                total += Vector2.Distance(road.Points[i - 1], road.Points[i]);
        }

        return total;
    }

    private static int Same(List<Road> ours, List<Road> theirs)
    {
        var keys = new HashSet<string>();

        foreach (Road road in theirs)
            keys.Add(Key(road));

        int same = 0;

        foreach (Road road in ours)
            same += keys.Contains(Key(road)) ? 1 : 0;

        return same;
    }

    private static string Key(Road road)
    {
        Vector2 first = road.Points[0], last = road.Points[^1];

        return FormattableString.Invariant($"{road.Points.Length}:{first.x:0.0},{first.y:0.0}:{last.x:0.0},{last.y:0.0}");
    }

    private static List<Vector2> BakedPois(string path)
    {
        var points = new List<Vector2>();
        const string MARK = "<Position>k__BackingField: {x: ";

        foreach (string line in File.ReadLines(path))
        {
            int at = line.IndexOf(MARK, StringComparison.Ordinal);

            if (at < 0)
                continue;

            string[] parts = line.Substring(at + MARK.Length).TrimEnd('}').Split(',');
            float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
            float z = float.Parse(parts[2].Replace("z:", "").Trim(), CultureInfo.InvariantCulture);
            points.Add(new Vector2(x, z));
        }

        return points;
    }

    private static void Pois(List<PoiPlacement> ours, string path)
    {
        List<Vector2> theirs = BakedPois(path);
        var keys = new HashSet<(int, int)>();

        foreach (Vector2 point in theirs)
            keys.Add((Mathf.RoundToInt(point.x / POINT_MATCH), Mathf.RoundToInt(point.y / POINT_MATCH)));

        int same = 0;

        foreach (PoiPlacement placement in ours)
            same += keys.Contains((Mathf.RoundToInt(placement.Position.x / POINT_MATCH), Mathf.RoundToInt(placement.Position.z / POINT_MATCH))) ? 1 : 0;

        Console.WriteLine($"сверка зданий: {ours.Count} / {theirs.Count}, на тех же местах {same}");
    }

    private static void Heights(WorldMapResult world, HeightMap baked, WorldGenerationConfig config, string image)
    {
        float[] ours = world.Heights.Heights, theirs = baked.Heights;
        int resolution = config.HeightMapResolution;
        float cell = config.WorldSize / (float)(resolution - 1);
        float[] roadMask = world.RoadMask;
        var pois = new PoiIndex(world.Placements);
        long road = 0, poi = 0, water = 0, other = 0;
        long exact = 0, oneStep = 0;
        var worst = new List<(float Difference, int Index)>();
        var rgb = image != null ? new byte[IMAGE * IMAGE * 3] : null;
        var steps = image != null ? new int[IMAGE * IMAGE] : null;

        for (int i = 0; i < ours.Length; i++)
        {
            float difference = Mathf.Abs(ours[i] - theirs[i]) * config.MaxHeight;
            int step = Math.Abs(Mathf.Clamp(Mathf.RoundToInt(ours[i] * ushort.MaxValue), 0, ushort.MaxValue) - Mathf.RoundToInt(theirs[i] * ushort.MaxValue));
            exact += step == 0 ? 1 : 0;
            oneStep += step == 1 ? 1 : 0;

            if (steps != null && step > 0)
                steps[(IMAGE - 1 - Math.Min(IMAGE - 1, (int)((long)(i / resolution) * IMAGE / resolution))) * IMAGE + Math.Min(IMAGE - 1, (int)((long)(i % resolution) * IMAGE / resolution))]++;

            if (rgb != null)
                Paint(rgb, i % resolution, i / resolution, resolution, difference);

            if (difference <= FAR)
                continue;

            float x = i % resolution * cell, z = i / resolution * cell;

            if (roadMask != null && i < roadMask.Length && roadMask[i] > 0f)
                road++;
            else if (pois.Near(x, z))
                poi++;
            else if (world.Water.TryRiver(x, z, out _) || world.Water.StandingAt(x, z, out _))
                water++;
            else
            {
                other++;

                if (worst.Count < 400 || difference > worst[^1].Difference)
                {
                    worst.Add((difference, i));
                    worst.Sort((a, b) => b.Difference.CompareTo(a.Difference));

                    if (worst.Count > 400)
                        worst.RemoveAt(worst.Count - 1);
                }
            }
        }

        Console.WriteLine($"16-битная высота совпадает точно в {exact * 100.0 / ours.Length:0.000} % клеток, на одну ступень 6 мм в {oneStep * 100.0 / ours.Length:0.000} %");
        Console.WriteLine($"расхождения высот больше {FAR:0} м: под дорогами {road * cell * cell:N0} м², у зданий {poi * cell * cell:N0} м², в воде {water * cell * cell:N0} м², прочее {other * cell * cell:N0} м²");

        var shown = new List<Vector2>();

        foreach ((float difference, int index) in worst)
        {
            var at = new Vector2(index % resolution * cell, index / resolution * cell);

            if (shown.Exists(point => (point - at).sqrMagnitude < 200f * 200f))
                continue;

            shown.Add(at);
            Console.WriteLine($"  прочее расхождение {difference:0.00} м в ({at.x:0}, {at.y:0}): наш {ours[index] * config.MaxHeight:0.00}, Unity {theirs[index] * config.MaxHeight:0.00}");

            if (shown.Count == 8)
                break;
        }

        if (rgb == null)
            return;

        Png.Write(image, rgb, IMAGE, IMAGE);
        var gray = new byte[IMAGE * IMAGE * 3];

        for (int p = 0; p < steps.Length; p++)
            gray[p * 3] = gray[p * 3 + 1] = gray[p * 3 + 2] = (byte)Math.Min(255, steps[p] * 16);

        Png.Write(Path.ChangeExtension(image, null) + "_steps.png", gray, IMAGE, IMAGE);
        Console.WriteLine($"  карта расхождений: {image}");
    }

    private static void Paint(byte[] rgb, int column, int row, int resolution, float difference)
    {
        int px = Math.Min(IMAGE - 1, (int)((long)column * IMAGE / resolution));
        int py = IMAGE - 1 - Math.Min(IMAGE - 1, (int)((long)row * IMAGE / resolution));
        int at = (py * IMAGE + px) * 3;
        byte level = difference < 0.05f ? (byte)0 : difference < FAR ? (byte)1 : difference < 5f ? (byte)2 : (byte)3;
        byte current = rgb[at] == 255 ? (byte)3 : rgb[at + 1] == 160 ? (byte)2 : rgb[at + 2] == 200 ? (byte)1 : (byte)0;

        if (level < current)
            return;

        (byte r, byte g, byte b) = level switch
        {
            3 => ((byte)255, (byte)30, (byte)30),
            2 => ((byte)250, (byte)160, (byte)0),
            1 => ((byte)60, (byte)90, (byte)200),
            _ => ((byte)20, (byte)20, (byte)20)
        };

        rgb[at] = r;
        rgb[at + 1] = g;
        rgb[at + 2] = b;
    }

    private sealed class PoiIndex
    {
        private const float CELL = 64f;
        private readonly Dictionary<(int, int), List<Vector2>> _cells = new();

        public PoiIndex(List<PoiPlacement> placements)
        {
            foreach (PoiPlacement placement in placements)
            {
                var at = new Vector2(placement.Position.x, placement.Position.z);
                var key = (Mathf.FloorToInt(at.x / CELL), Mathf.FloorToInt(at.y / CELL));

                if (!_cells.TryGetValue(key, out List<Vector2> list))
                    _cells[key] = list = new List<Vector2>();

                list.Add(at);
            }
        }

        public bool Near(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / CELL), cz = Mathf.FloorToInt(z / CELL);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_cells.TryGetValue((cx + dx, cz + dz), out List<Vector2> list))
                        continue;

                    foreach (Vector2 point in list)
                    {
                        if ((point - new Vector2(x, z)).sqrMagnitude < NEAR_POI * NEAR_POI)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}

static class ReliefCache
{
    public const string EXACT = ".f32";

    public static HeightMap Read(string path, WorldGenerationConfig config)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (!path.EndsWith(EXACT, StringComparison.OrdinalIgnoreCase))
            return HeightMap.FromRaw16(bytes, config.HeightMapResolution, config.WorldSize, config.MaxHeight);

        var map = new HeightMap(config.HeightMapResolution, config.WorldSize, config.MaxHeight);

        if (bytes.Length != map.Heights.Length * sizeof(float))
            throw new InvalidDataException($"{path} is {bytes.Length} bytes, expected {map.Heights.Length * sizeof(float)}");

        Buffer.BlockCopy(bytes, 0, map.Heights, 0, bytes.Length);
        return map;
    }

    public static void Write(string path, HeightMap map)
    {
        if (!path.EndsWith(EXACT, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(path, map.ToRaw16());
            return;
        }

        var bytes = new byte[map.Heights.Length * sizeof(float)];
        Buffer.BlockCopy(map.Heights, 0, bytes, 0, bytes.Length);
        File.WriteAllBytes(path, bytes);
    }
}

static class StageTrace
{
    public static void Run(WorldGenerationConfig config, BiomeDatabase biomes, PoiDatabase pois, HeightMap relief, List<Vector2> points)
    {
        const System.Reflection.BindingFlags ANY = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        Type pipeline = typeof(WorldMapPipeline);
        Type contextType = pipeline.GetNestedType("Context", ANY);
        object context = Activator.CreateInstance(contextType, true);

        contextType.GetField("Config").SetValue(context, config);
        contextType.GetField("BiomeSet").SetValue(context, biomes);
        contextType.GetField("Pois").SetValue(context, pois);
        contextType.GetField("Heights").SetValue(context, relief);

        var stages = (Array)pipeline.GetField("Stages", ANY).GetValue(null);

        foreach (object stage in stages)
        {
            Type stageType = stage.GetType();
            string title = (string)stageType.GetField("Title").GetValue(stage);
            var run = (Delegate)stageType.GetField("Run").GetValue(stage);
            run.DynamicInvoke(context);

            var heights = (HeightMap)contextType.GetField("Heights").GetValue(context);
            var line = new List<string>();

            foreach (Vector2 point in points)
                line.Add($"{heights.SampleWorldSmooth(point.x, point.y),7:0.00}");

            Console.WriteLine($"  после «{title}»: {string.Join(" ", line)}");
        }

        var network = (RoadNetwork)contextType.GetField("Roads").GetValue(context);

        if (network == null || points.Count == 0)
            return;

        var all = new List<Road>(network.Roads);
        all.AddRange(network.Streets);

        foreach (Road road in all)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i + 1 < road.Points.Length; i++)
            {
                Vector2 a = road.Points[i], b = road.Points[i + 1], axis = b - a;
                float t = axis.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(points[0] - a, axis) / axis.sqrMagnitude);
                nearest = Mathf.Min(nearest, Vector2.Distance(points[0], a + axis * t));
            }

            if (nearest < 80f)
                Console.WriteLine($"  дорога {road.Kind} шириной {road.Width:0.0} м в {nearest:0.0} м от ({points[0].x:0}, {points[0].y:0})");
        }
    }
}
