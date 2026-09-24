using System;
using System.Collections.Generic;
using UnityEngine;

static partial class WaterStampChecks
{
    public static void Report(WaterMap water, HeightMap carved, WaterMap repeat)
    {
        WaterStampLayout layout = water.Stamps;

        StampChecks.Expect(layout != null, "stamps are enabled but the water map carries no stamp layout");

        if (layout == null)
            return;

        Console.WriteLine("  " + Describe(layout));

        var lengths = new List<string>();

        foreach (Vector2[] macro in layout.MacroRivers)
        {
            float length = 0f;

            for (int i = 1; i < macro.Length; i++)
                length += Vector2.Distance(macro[i - 1], macro[i]);

            lengths.Add($"{length:0}");
        }

        Console.WriteLine($"  макро-маршруты рек, м: {string.Join(", ", lengths)}");

        string report = Environment.GetEnvironmentVariable("WATER_STAMP_REPORT");

        if (!string.IsNullOrEmpty(report))
            System.IO.File.WriteAllText(report, WaterDebugImages.StampReport(layout));

        CheckSameLayout(layout, repeat.Stamps);
        CheckGeometry(water, layout);
        CheckConnections(water);
        CheckStampQueries(water, carved, layout);
        CheckLakeOutlines(water, layout);
    }

    public static string Describe(WaterStampLayout layout)
    {
        var categories = new SortedDictionary<string, int>();

        foreach (WaterStampPlacement placement in layout.Placements)
            categories[placement.Category.ToString()] = categories.TryGetValue(placement.Category.ToString(), out int count) ? count + 1 : 1;

        var used = new List<string>();

        foreach (KeyValuePair<string, int> pair in categories)
            used.Add($"{pair.Key} {pair.Value}");

        var reasons = new Dictionary<string, int>();

        foreach (WaterStampRejection rejection in layout.Rejections)
        {
            string reason = System.Text.RegularExpressions.Regex.Replace(rejection.Reason, "[0-9.,]+", "#");
            reasons[reason] = reasons.TryGetValue(reason, out int seen) ? seen + 1 : 1;
        }

        var rejected = new List<string>();

        foreach (KeyValuePair<string, int> pair in reasons)
            rejected.Add($"{pair.Key}: {pair.Value}");

        rejected.Sort();

        int stampedPoints = 0, trackedPoints = 0;

        foreach (WaterStampTrack track in layout.Tracks)
        {
            if (track == null)
                continue;

            for (int i = 0; i < track.Placement.Length; i++)
            {
                trackedPoints++;

                if (track.Placement[i] >= 0)
                    stampedPoints++;
            }
        }

        return $"точек рек по штампам {stampedPoints} из {trackedPoints} ({(trackedPoints == 0 ? 0f : stampedPoints / (float)trackedPoints):P0}); "
            + $"штампы воды: рек {layout.RiverPlacements}, озёр {layout.LakePlacements}, прудов {layout.PondPlacements}, слияний {layout.TributaryJoins}, развилок {layout.Forks}; "
            + $"процедурных кусков рек {layout.FallbackChunks} (без подходящего штампа {layout.FailedChunks}), процедурных водоёмов {layout.ProceduralBodies}; "
            + $"кандидатов {layout.Candidates}; клеток высоты: котловины {layout.LakeCells:N0}, коридоры {layout.CorridorCells:N0}\n    по категориям: {string.Join(", ", used)}\n    отказы: {string.Join("; ", rejected)}";
    }

    private static void CheckSameLayout(WaterStampLayout first, WaterStampLayout second)
    {
        bool same = second != null && first.Placements.Count == second.Placements.Count;

        for (int i = 0; same && i < first.Placements.Count; i++)
        {
            WaterStampPlacement a = first.Placements[i], b = second.Placements[i];
            same = a.StampIndex == b.StampIndex && a.Mirror == b.Mirror && a.Scale == b.Scale && a.Rotation == b.Rotation && a.Origin.x == b.Origin.x
                && a.Origin.y == b.Origin.y && a.River == b.River && a.Body == b.Body;
        }

        StampChecks.Expect(same, "the same seed placed different water stamps");
        Console.WriteLine($"  детерминизм штампов: {(same ? "одинаковые" : "РАЗНЫЕ")} {first.Placements.Count} постановок в двух прогонах");
    }

    private static void CheckGeometry(WaterMap water, WaterStampLayout layout)
    {
        int stamped = 0, outside = 0, degenerate = 0, gaps = 0, thin = 0, bedAbove = 0, bad = 0;

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            WaterStampTrack track = r < layout.Tracks.Count ? layout.Tracks[r] : null;

            for (int i = 0; i < points.Count; i++)
            {
                RiverPoint point = points[i];

                if (track != null && track.Stamped(i))
                    stamped++;

                if (point.Position.x < 0f || point.Position.y < 0f || point.Position.x > water.WorldSize || point.Position.y > water.WorldSize)
                    outside++;

                if (!(point.Width > 0f))
                    thin++;

                if (point.Bed >= point.Surface)
                    bedAbove++;

                if (i == 0)
                    continue;

                float step = Vector2.Distance(points[i - 1].Position, point.Position);

                if (step < 1e-3f)
                    degenerate++;

                if (step > 3f * Hydrology.RESAMPLE_STEP)
                    gaps++;
            }
        }

        foreach (WaterStampPlacement placement in layout.Placements)
        {
            if (!float.IsFinite(placement.Origin.x) || !float.IsFinite(placement.Origin.y) || !(placement.Scale > 0f) || !float.IsFinite(placement.Rotation))
                bad++;
        }

        StampChecks.Expect(layout.Placements.Count > 0, "stamps are enabled but none was placed");
        StampChecks.Expect(stamped > 0 || layout.RiverPlacements == 0, "river stamps were placed but no river point follows one");
        StampChecks.Expect(outside == 0, $"{outside} river points outside the world");
        StampChecks.Expect(degenerate == 0, $"{degenerate} zero-length river segments");
        StampChecks.Expect(gaps == 0, $"{gaps} river segments longer than {3f * Hydrology.RESAMPLE_STEP} m, a gap in the course");
        StampChecks.Expect(thin == 0, $"{thin} river points without a positive width");
        StampChecks.Expect(bedAbove == 0, $"{bedAbove} river points with the bed at or above the surface");
        StampChecks.Expect(bad == 0, $"{bad} water stamp placements with a non-finite transform");

        Console.WriteLine($"  геометрия: точек рек по штампам {stamped}, вне мира {outside}, нулевых отрезков {degenerate}, разрывов {gaps}, ширина <= 0 {thin}, дно над водой {bedAbove}");
    }

    private static void CheckConnections(WaterMap water)
    {
        int orphans = 0, steps = 0, joined = 0, forks = 0;
        float worstStep = 0f;
        float margin = 3f * water.CellSize;

        for (int r = 0; r < water.Rivers.Count; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            RiverPoint end = points[^1];
            RiverPoint start = points[0];

            bool border = end.Position.x < margin || end.Position.y < margin || end.Position.x > water.WorldSize - margin || end.Position.y > water.WorldSize - margin;
            bool standing = end.Submerged || water.StandingAt(end.Position.x, end.Position.y, out _) || water.SeaLevel > 0f && end.Surface <= water.SeaLevel + 0.05f;
            bool trunk = water.InsideEarlierRiver(r, end.Position, 2f, out float trunkSurface);

            if (trunk && !(water.StandingAt(end.Position.x, end.Position.y, out _) || water.SeaLevel > 0f && end.Surface <= water.SeaLevel + 0.05f))
            {
                joined++;
                float step = Mathf.Abs(end.Surface - trunkSurface);

                if (end.Surface > trunkSurface + Hydrology.MERGE_TOLERANCE + 0.05f || end.Surface < trunkSurface - 1f)
                {
                    steps++;
                    worstStep = Mathf.Max(worstStep, step);
                }
            }

            if (!border && !standing && !trunk)
                orphans++;

            if (r > 0 && water.InsideEarlierRiver(r, start.Position, 2f, out _))
                forks++;
        }

        StampChecks.Expect(orphans == 0, $"{orphans} rivers end on dry land away from any other water");
        StampChecks.Expect(steps == 0, $"{steps} tributaries meet their trunk with a level step, up to {worstStep:0.00} m");
        Console.WriteLine($"  связность: рек {water.Rivers.Count}, впадают в другую реку {joined}, начинаются из другой реки {forks}, висящих концов {orphans}, ступенек на слиянии {steps}");
    }

    private static void CheckStampQueries(WaterMap water, HeightMap carved, WaterStampLayout layout)
    {
        int probes = 0, wrong = 0;

        for (int r = 0; r < water.Rivers.Count && probes < 200; r++)
        {
            List<RiverPoint> points = water.Rivers[r].Points;
            WaterStampTrack track = r < layout.Tracks.Count ? layout.Tracks[r] : null;

            if (track == null)
                continue;

            for (int i = 2; i + 2 < points.Count && probes < 200; i += 25)
            {
                RiverPoint point = points[i];

                if (!track.Stamped(i) || point.Submerged || point.Width < 3f || !water.RiverOpen(point))
                    continue;

                probes++;
                Vector2 tangent = (points[i + 1].Position - points[i - 1].Position).normalized;
                var normal = new Vector2(-tangent.y, tangent.x);
                float ground = carved.SampleWorldSmooth(point.Position.x, point.Position.y);
                WaterSample sample = water.Sample(point.Position.x, point.Position.y);
                Vector2 bank = point.Position + normal * (0.5f * point.Width + 1f);

                bool ok = sample.Kind == WaterKind.River
                    && water.KindAt(point.Position.x, point.Position.y, ground) == WaterKind.River
                    && water.IsWater(point.Position.x, point.Position.y, ground)
                    && water.Depth(point.Position.x, point.Position.y, ground) > 0.05f
                    && Mathf.Abs(water.SurfaceHeight(point.Position.x, point.Position.y) - point.Surface) < 0.25f
                    && Vector2.Dot(water.FlowDirection(point.Position.x, point.Position.y), tangent) > 0.7f
                    && water.CrossesRiver(point.Position - normal * point.Width, point.Position + normal * point.Width, out _, out _)
                    && water.IsWet(bank.x, bank.y, point.Surface + 1f, 6f);

                if (!ok)
                    wrong++;
            }
        }

        StampChecks.Expect(probes > 0 || layout.RiverPlacements == 0, "no stamped river point wide enough to query");
        StampChecks.Expect(wrong <= probes / 50, $"{wrong} of {probes} stamped river points answer water queries wrongly");
        Console.WriteLine($"  запросы WaterMap на реках по штампам: {probes} проб, неверных {wrong}");
    }

    private static void CheckLakeOutlines(WaterMap water, WaterStampLayout layout)
    {
        int lakes = 0, dry = 0;
        var shares = new List<string>();

        foreach (WaterStampPlacement placement in layout.Placements)
        {
            if (placement.Body < 0)
                continue;

            lakes++;
            int inside = 0, total = 0;

            for (int cell = 0; cell < water.Kinds.Length; cell++)
            {
                if (water.BodyIds[cell] != placement.Body || water.Kinds[cell] == 0)
                    continue;

                total++;
                Vector2 uv = placement.Uv(placement.Stamp(water.CellCenter(cell)));

                if (uv.x >= 0f && uv.y >= 0f && uv.x <= 1f && uv.y <= 1f)
                    inside++;
            }

            if (total == 0)
                dry++;

            if (shares.Count < 12)
                shares.Add($"{placement.Name} {(total == 0 ? 0f : inside / (float)total):P0}");
        }

        StampChecks.Expect(dry == 0, $"{dry} stamped lakes hold no water");
        Console.WriteLine($"  озёра по штампам: {lakes}, без воды {dry}; доля воды внутри штампа: {string.Join(", ", shares)}");
    }
}
