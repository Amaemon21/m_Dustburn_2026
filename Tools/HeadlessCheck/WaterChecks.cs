using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

static class WaterChecks
{
    private const int WORLD = 2048;
    private const float MAX_HEIGHT = 384f;
    private const float EPSILON = 1e-3f;

    private static int _world = WORLD;

    public static void Run(WorldGenerationConfig source, bool stamped = false)
    {
        Run(Config(source, WORLD), stamped, WORLD, Terrain, stamped ? "Вода со штампами" : "Вода");
    }

    public static WaterAudit.Result Run(WorldGenerationConfig config, bool stamped, int world, Func<HeightMap> terrain, string label, float hangLimit = 1.5f)
    {
        _world = world;
        WaterStampLoader.Attach(config, stamped);

        var clock = Stopwatch.StartNew();
        HeightMap first = WorldMapPipeline.Hydrate(config, terrain(), out Hydrology hydrology);
        double millis = clock.Elapsed.TotalMilliseconds;

        HeightMap second = WorldMapPipeline.Hydrate(config, terrain(), out _);

        WaterMap water = first.Water;

        string inspect = Environment.GetEnvironmentVariable("WATER_AT");

        if (!string.IsNullOrEmpty(inspect))
        {
            foreach (string spot in inspect.Split(';'))
            {
                string[] xz = spot.Split(',');
                WaterAudit.Inspect(first, water, float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
            }

            return null;
        }

        Console.WriteLine($"{label}: синтетический мир {world} м, seed {config.Seed}: {water.Rivers.Count} рек, {hydrology.Lakes} озёр, {hydrology.Ponds} прудов, "
            + $"{hydrology.Basins.Count} замкнутых впадин, {millis:0} мс");

        if (stamped)
            WaterStampChecks.Report(water, first, second.Water);

        Console.WriteLine($"  отпечаток: вода {Fingerprint(water.ToBytes())}, рельеф {Fingerprint(Bytes(first.Heights))}");

        CheckRivers(water, first);
        CheckBodies(water, first);
        CheckFinite(water);
        CheckDeterminism(water, second.Water);
        CheckRoundTrip(water);
        CheckMeshes(water);
        CheckQueries(water, first);
        WaterAudit.Result audit = CheckArtifacts(first, water, hangLimit);
        CheckShore(first, terrain(), water, second.Water, config.Water.RiverBankWidth);

        StampChecks.Finish(label, "реки текут только вниз, расход и ширина вниз по течению не убывают, озёра плоские и выше дна, без NaN, внутри мира, одинаковый seed даёт одинаковую воду и меши, сериализация без потерь; берега без сетки гидрологии, обрывков, дырок и T-стыков, устья без сухих разрывов, сечения рек без обрывов.");

        return audit;
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

    public static WorldGenerationConfig Config(WorldGenerationConfig source, int world)
    {
        var config = new WorldGenerationConfig();

        StampLoader.Set(config, "Seed", source.Seed);
        StampLoader.Set(config, "WorldSize", world);
        StampLoader.Set(config, "SeaLevel", 60f);

        WaterGenerationSettings water = config.Water;
        StampLoader.Set(water, "RiverStartArea", 0.12f);
        StampLoader.Set(water, "LakeDensity", 1f);
        StampLoader.Set(water, "PondDensity", 1f);
        StampLoader.Set(water, "MinLakeArea", 8000f);
        StampLoader.Set(water, "LakeSpacing", 200f);

        return config;
    }

    private static HeightMap Terrain()
    {
        var map = new HeightMap(WORLD + 1, WORLD, MAX_HEIGHT);

        for (int z = 0; z <= WORLD; z++)
        {
            for (int x = 0; x <= WORLD; x++)
            {
                float slope = 150f - 70f * x / WORLD;
                float valleys = 9f * Mathf.Sin(z * 0.009f + Mathf.Sin(x * 0.004f) * 1.5f);
                float hills = 4f * Mathf.Sin(x * 0.021f) * Mathf.Cos(z * 0.017f);
                float bowl = -26f * Bowl(x, z, 700f, 1300f, 160f) - 18f * Bowl(x, z, 1500f, 500f, 110f) - 5f * Bowl(x, z, 400f, 400f, 40f);

                map.Heights[z * (WORLD + 1) + x] = (slope + valleys + hills + bowl) / MAX_HEIGHT;
            }
        }

        return map;
    }

    private static float Bowl(float x, float z, float cx, float cz, float radius)
    {
        float d2 = ((x - cx) * (x - cx) + (z - cz) * (z - cz)) / (radius * radius);

        return Mathf.Max(0f, 1f - d2) * Mathf.Max(0f, 1f - d2);
    }

    private static WaterAudit.Result CheckArtifacts(HeightMap carved, WaterMap water, float hangLimit)
    {
        WaterAudit.Result audit = WaterAudit.Measure(carved, water, new Vector2(_world * 0.5f, _world * 0.5f), "water_checks_audit.png");
        WaterAudit.PrintExamples(audit);
        float probed = audit.Area(audit.Probes);

        StampChecks.Expect(audit.Probes > 0, "the water audit probed nothing");
        StampChecks.Expect(audit.Area(audit.Holes) <= probed * 0.001f, $"{audit.Area(audit.Holes):0} m² of ground under water with no water mesh");
        StampChecks.Expect(audit.Area(audit.Hanging) <= probed * 0.001f && audit.WorstHang < hangLimit, $"{audit.Area(audit.Hanging):0} m² of water hanging over dry land, up to {audit.WorstHang:0.00} m (limit {hangLimit:0.00} m)");
        StampChecks.Expect(audit.Area(audit.ZFight) <= probed * 0.0005f, $"{audit.Area(audit.ZFight):0} m² of coplanar water surfaces");
        StampChecks.Expect(audit.Area(audit.Stacked) <= probed * 0.002f, $"{audit.Area(audit.Stacked):0} m² of water drawn twice");
        StampChecks.Expect(audit.Steps == 0, $"{audit.Steps} river level steps steeper than 15%, up to {audit.WorstStep:0.00}");
        StampChecks.Expect(audit.WorstTrench < 9f, $"a river trench {audit.WorstTrench:0.0} m deep");

        long nearFlood = audit.FloodByLod[0] + audit.FloodByLod[1] + audit.FloodByLod[2];
        long nearMissing = audit.MissingByLod[0] + audit.MissingByLod[1] + audit.MissingByLod[2];

        StampChecks.Expect(audit.Area(nearFlood + nearMissing) <= probed * 0.001f, $"{audit.Area(nearFlood + nearMissing):0} m² of water disagreeing with the near LOD rings");

        Console.WriteLine($"  аудит артефактов на {probed / 1e6f:0.00} км² у воды: дыры {audit.Area(audit.Holes):0} м², висит {audit.Area(audit.Hanging):0} м², "
            + $"z-fight {audit.Area(audit.ZFight):0} м², двойная {audit.Area(audit.Stacked):0} м², ступеньки {audit.Steps}, траншея до {audit.WorstTrench:0.0} м, "
            + $"ближние кольца LOD {audit.Area(nearFlood + nearMissing):0} м²");

        return audit;
    }

    private static void CheckShore(HeightMap carved, HeightMap raw, WaterMap water, WaterMap repeat, float bankWidth)
    {
        WaterShoreChecks.Result shore = WaterShoreChecks.Measure(carved, raw, water, bankWidth);

        StampChecks.Expect(shore.Bodies > 0 && shore.SplitBodies == 0, $"{shore.SplitBodies} lakes carry a detached piece smaller than a pond");
        StampChecks.Expect(shore.Fragments == 0, $"{shore.Fragments} standing water fragments under 16 m²");
        StampChecks.Expect(shore.TinyHoles == 0, $"{shore.TinyHoles} tiny holes in standing water with ground below the surface");
        StampChecks.Expect(shore.GridShare < 0.05f, $"{shore.GridShare:P1} of the shoreline runs along hydrology grid lines");
        StampChecks.Expect(shore.ShoreP95 < WaterShore.SHELF, $"shoreline sits {shore.ShoreP95:0.00} m off the waterline at p95");
        StampChecks.Expect(shore.ShoreHighP95 < 0.5f, $"the water mesh reaches {shore.ShoreHighP95:0.00} m up dry ground at p95");
        StampChecks.Expect(shore.Hanging <= shore.ShoreSamples / 50, $"{shore.Hanging} of {shore.ShoreSamples} shoreline samples end over submerged ground");
        StampChecks.Expect(shore.SpikesPerKm <= 1f, $"{shore.SpikesPerKm:0.00} one-node spikes per km of shoreline");
        StampChecks.Expect(shore.Mouths > 0 && shore.MouthGaps <= shore.MouthSamples / 100, $"{shore.MouthGaps} of {shore.MouthSamples} river mouth samples have no water");
        StampChecks.Expect(shore.Sections > 0 && shore.WetCenters == shore.Sections, $"{shore.Sections - shore.WetCenters} of {shore.Sections} river sections are dry in the middle");
        StampChecks.Expect(shore.FallingBanks <= shore.Sections / 25, $"{shore.FallingBanks} river banks fall away from the water on rising ground");
        StampChecks.Expect(shore.Cliffs <= shore.Sections / 25, $"{shore.Cliffs} river banks carved steeper than 45° on gentler ground");
        StampChecks.Expect(shore.TJunctions == 0 && shore.NonManifold == 0, $"{shore.TJunctions} T-junctions and {shore.NonManifold} over-shared edges in standing water");
        StampChecks.Expect(WaterShoreChecks.SameMeshes(water, repeat), "the same seed produced different water meshes");

        Console.WriteLine(shore.Describe());
    }

    private static void CheckRivers(WaterMap water, HeightMap carved)
    {
        StampChecks.Expect(water.Rivers.Count > 0, "no river formed");

        int uphill = 0, narrowing = 0, shrinking = 0, dry = 0, points = 0;
        float steepest = 0f;

        foreach (RiverPath river in water.Rivers)
        {
            for (int i = 1; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i - 1];
                RiverPoint b = river.Points[i];
                points++;

                if (b.Surface > a.Surface + EPSILON)
                {
                    uphill++;
                    steepest = Mathf.Max(steepest, b.Surface - a.Surface);
                }

                if (b.Flow < a.Flow - EPSILON)
                    shrinking++;

                if (b.Width < a.Width - EPSILON - (water.Stamps != null ? 0.15f * Vector2.Distance(a.Position, b.Position) : 0f))
                    narrowing++;

                if (!b.Submerged && carved.SampleWorldSmooth(b.Position.x, b.Position.y) > b.Surface + 0.05f)
                    dry++;
            }
        }

        StampChecks.Expect(uphill == 0, $"{uphill} river steps flow uphill, up to {steepest:0.000} m");
        StampChecks.Expect(shrinking == 0, $"{shrinking} river steps lose flow downstream");
        StampChecks.Expect(narrowing == 0, $"{narrowing} river steps narrow downstream");
        StampChecks.Expect(dry <= points / 100, $"{dry} of {points} river points have ground above the water after carving");

        Console.WriteLine($"  реки: {points} точек, вверх по склону {uphill}, расход убывает {shrinking}, сужение {narrowing}, "
            + $"русло над водой после врезки {dry}");
    }

    private static void CheckBodies(WaterMap water, HeightMap carved)
    {
        StampChecks.Expect(water.Bodies.Count > 0, "no lake or pond formed");

        int above = 0, cells = 0;

        for (int i = 0; i < water.Kinds.Length; i++)
        {
            var kind = (WaterKind)water.Kinds[i];

            if (kind != WaterKind.Lake && kind != WaterKind.Pond)
                continue;

            cells++;

            Vector2 center = water.CellCenter(i);
            float surface = water.Bodies[water.BodyIds[i]].Surface;
            float lowest = float.MaxValue;

            for (int dz = 0; dz <= 2; dz++)
            {
                for (int dx = 0; dx <= 2; dx++)
                    lowest = Mathf.Min(lowest, carved.SampleWorldSmooth(center.x + (dx - 1) * water.CellSize * 0.5f, center.y + (dz - 1) * water.CellSize * 0.5f));
            }

            if (lowest >= surface)
                above++;
        }

        StampChecks.Expect(above == 0, $"{above} of {cells} lake cells have no ground under the water surface");

        var surfaces = new List<string>();

        foreach (WaterBody body in water.Bodies)
            surfaces.Add($"{body.Kind} {body.Area / 10000f:0.0} га на {body.Surface:0.0} м");

        Console.WriteLine($"  озёра и пруды: {water.Bodies.Count}, клеток {cells}, без дна под водой {above}; {string.Join(", ", surfaces)}");
    }

    private static void CheckFinite(WaterMap water)
    {
        int bad = 0, outside = 0;

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (!float.IsFinite(point.Surface) || !float.IsFinite(point.Bed) || !float.IsFinite(point.Width) || !float.IsFinite(point.Position.x) || !float.IsFinite(point.Position.y))
                    bad++;

                if (point.Position.x < 0f || point.Position.y < 0f || point.Position.x > water.WorldSize || point.Position.y > water.WorldSize)
                    outside++;
            }
        }

        foreach (WaterBody body in water.Bodies)
        {
            if (!float.IsFinite(body.Surface) || !float.IsFinite(body.Area))
                bad++;
        }

        for (int i = 0; i < water.ShoreDistance.Length; i++)
        {
            if (float.IsNaN(water.ShoreDistance[i]) || float.IsNaN(water.ShoreSurface[i]))
                bad++;

            if (float.IsFinite(water.ShoreDistance[i]) && !float.IsFinite(water.ShoreSurface[i]))
                bad++;
        }

        StampChecks.Expect(bad == 0, $"{bad} NaN or infinite water values");
        StampChecks.Expect(outside == 0, $"{outside} river points outside the world");
        Console.WriteLine($"  значения: NaN/Infinity {bad}, точек вне мира {outside}");
    }

    private static void CheckDeterminism(WaterMap first, WaterMap second)
    {
        byte[] a = first.ToBytes();
        byte[] b = second.ToBytes();
        bool same = a.Length == b.Length;

        for (int i = 0; same && i < a.Length; i++)
            same = a[i] == b[i];

        StampChecks.Expect(same, "the same seed produced different water");
        Console.WriteLine($"  детерминизм: два прогона дают побайтно одинаковую карту воды ({a.Length / 1024} КБ)");
    }

    private static void CheckRoundTrip(WaterMap water)
    {
        byte[] bytes = water.ToBytes();
        WaterMap read = WaterMap.FromBytes(bytes);
        byte[] again = read.ToBytes();
        bool same = bytes.Length == again.Length;

        for (int i = 0; same && i < bytes.Length; i++)
            same = bytes[i] == again[i];

        StampChecks.Expect(same, "water map changes through serialization");
    }

    private static void CheckMeshes(WaterMap water)
    {
        List<WaterMeshPart> parts = WaterMeshes.Build(water);
        int triangles = 0, bad = 0;

        foreach (WaterMeshPart part in parts)
        {
            triangles += part.Triangles.Count / 3;

            foreach (Vector3 vertex in part.Vertices)
            {
                if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                    bad++;
            }

            for (int t = 0; t < part.Triangles.Count; t += 3)
            {
                Vector3 a = part.Vertices[part.Triangles[t]];
                Vector3 b = part.Vertices[part.Triangles[t + 1]];
                Vector3 c = part.Vertices[part.Triangles[t + 2]];

                float facing = (b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z);

                if (facing < -1e-4f)
                    bad++;

                if (part.Kind != WaterKind.River && (Mathf.Abs(a.y - b.y) > 1e-4f || Mathf.Abs(a.y - c.y) > 1e-4f))
                    bad++;
            }
        }

        StampChecks.Expect(parts.Count > 0 && triangles > 0, "no water mesh");
        StampChecks.Expect(bad == 0, $"{bad} water mesh problems: NaN, faces pointing down, or a tilted lake quad");
        Console.WriteLine($"  меши воды: {parts.Count} частей, {triangles} треугольников, проблем {bad}");
    }

    private static void CheckQueries(WaterMap water, HeightMap carved)
    {
        RiverPoint probe = default;
        bool found = false;

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (point.Submerged || point.Width < 3f)
                    continue;

                probe = point;
                found = true;
                break;
            }

            if (found)
                break;
        }

        StampChecks.Expect(found, "no river wide enough to query");

        if (!found)
            return;

        float ground = carved.SampleWorldSmooth(probe.Position.x, probe.Position.y);
        WaterSample sample = water.Sample(probe.Position.x, probe.Position.y);

        StampChecks.Expect(sample.Kind == WaterKind.River, $"river centre reads as {sample.Kind}");
        StampChecks.Expect(water.IsWater(probe.Position.x, probe.Position.y, ground), "river centre is not water");
        StampChecks.Expect(water.Depth(probe.Position.x, probe.Position.y, ground) > 0.1f, "river centre has no depth");
        StampChecks.Expect(sample.Flow.sqrMagnitude > 0.9f, "river centre has no flow direction");
        StampChecks.Expect(water.IsWet(probe.Position.x + probe.Width, probe.Position.y, probe.Surface + 1f, 6f), "a bank a metre above the river is not wet within ShoreMargin");
    }
}
