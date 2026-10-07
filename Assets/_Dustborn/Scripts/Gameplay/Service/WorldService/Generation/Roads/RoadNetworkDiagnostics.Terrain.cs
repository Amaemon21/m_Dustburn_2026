using System.Collections.Generic;
using UnityEngine;

public static partial class RoadNetworkDiagnostics
{
    private static void CheckGrades(WorldGenerationConfig config, List<Road> roads, HeightMap carved, WaterMap water, RoadNetworkReport report)
    {
        if (carved == null)
            return;

        var grades = new Dictionary<RoadKind, List<float>>();
        int window = Mathf.RoundToInt(GRADE_WINDOW / GRADE_STEP);

        foreach (Road road in roads)
        {
            Vector2[] points = RoadSmoother.Resample(road.Points, GRADE_STEP);

            if (points.Length <= window)
                continue;

            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);

            if (!grades.TryGetValue(road.Kind, out List<float> list))
            {
                list = new List<float>();
                grades[road.Kind] = list;
            }

            var heights = new float[points.Length];
            var distance = new float[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                heights[i] = WaterCrossings.TryDeck(water, points[i], out float deck) ? deck : carved.SampleWorldSmooth(points[i].x, points[i].y);
                distance[i] = i == 0 ? 0f : distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }

            for (int i = window; i < points.Length; i++)
            {
                float span = distance[i] - distance[i - window];

                if (span < GRADE_WINDOW * 0.5f)
                    continue;

                float grade = Mathf.Abs(heights[i] - heights[i - window]) / span;
                list.Add(grade);

                if (grade > profile.MaxGrade)
                    report.Violation(GRADE, points[i]);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in grades)
            Percentiles(report, $"grade.{pair.Key}", pair.Value);
    }

    private static void CheckWater(IReadOnlyList<Road> roads, HeightMap carved, WaterMap water, RoadNetworkReport report)
    {
        if (water == null || carved == null)
            return;

        int samples = 0;

        foreach (Road road in roads)
        {
            Vector2[] points = RoadSmoother.Resample(road.Points, WATER_STEP);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 tangent = (points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                var side = new Vector2(-tangent.y, tangent.x) * road.HalfWidth;
                samples++;

                if (WaterCrossings.OverSpan(water, points[i]))
                    continue;

                if (Wet(carved, water, points[i]) || Wet(carved, water, points[i] + side) || Wet(carved, water, points[i] - side))
                    report.Violation(ROAD_ON_WATER, points[i]);
            }
        }

        report.Metric("water.roadSamples", samples);
    }

    private static bool Wet(HeightMap carved, WaterMap water, Vector2 point)
    {
        if (point.x < 0f || point.y < 0f || point.x > water.WorldSize || point.y > water.WorldSize)
            return false;

        WaterSample sample = water.Sample(point.x, point.y);

        if (!sample.IsWater)
            return false;

        float ground = carved.SampleWorldSmooth(point.x, point.y);

        return sample.Kind == WaterKind.River ? ground < sample.Surface - WATER_MARGIN : ground < sample.Surface + WATER_MARGIN;
    }

    private static void CheckCrossSlopes(WorldGenerationConfig config, IReadOnlyList<Road> roads, HeightMap prepared, RoadNetworkReport report)
    {
        if (prepared == null)
            return;

        var slopes = new Dictionary<RoadKind, List<float>>();

        foreach (Road road in roads)
        {
            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);

            if (profile.MaxCrossSlope >= float.MaxValue)
                continue;

            if (!slopes.TryGetValue(road.Kind, out List<float> list))
            {
                list = new List<float>();
                slopes[road.Kind] = list;
            }

            float probe = profile.HalfWidth + profile.Shoulder;
            Vector2[] points = RoadSmoother.Resample(road.Points, CROSS_STEP);

            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 tangent = (points[i + 1] - points[i - 1]).normalized;
                Vector2 normal = new(-tangent.y, tangent.x);
                Vector2 left = points[i] - normal * probe;
                Vector2 right = points[i] + normal * probe;
                float slope = Mathf.Abs(prepared.SampleWorldSmooth(right.x, right.y) - prepared.SampleWorldSmooth(left.x, left.y)) / (2f * probe);

                list.Add(slope);

                if (slope > profile.MaxCrossSlope)
                    report.Violation(CROSS_SLOPE, points[i]);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in slopes)
            Percentiles(report, $"crossSlope.{pair.Key}", pair.Value);
    }

    private static void CheckCurvature(WorldGenerationConfig config, List<Road> roads, RoadNetworkReport report)
    {
        var turns = new Dictionary<RoadKind, List<float>>();
        var radii = new Dictionary<RoadKind, List<float>>();

        foreach (Road road in roads)
        {
            RoadKindProfile profile = RoadKindProfile.For(config, road.Kind);
            float spacing = RoadKindProfile.SampleSpacing(road.Kind);
            Vector2[] even = RoadSmoother.Resample(road.Points, spacing);

            if (!radii.TryGetValue(road.Kind, out List<float> radiusList))
            {
                radiusList = new List<float>();
                radii[road.Kind] = radiusList;
                turns[road.Kind] = new List<float>();
            }

            for (int i = 1; i < even.Length - 1; i++)
            {
                if (Vector2.Distance(even[i - 1], even[i]) < spacing * 0.5f || Vector2.Distance(even[i], even[i + 1]) < spacing * 0.5f)
                    continue;

                float radius = RoadSmoother.Circumradius(even[i - 1], even[i], even[i + 1]);

                if (radius >= float.MaxValue)
                    continue;

                radiusList.Add(radius);

                if (radius < profile.MinCurveRadius * RADIUS_TOLERANCE)
                    report.Violation(CURVE_RADIUS, even[i]);
            }

            Vector2[] coarse = RoadSmoother.Resample(road.Points, CURVE_WINDOW);

            for (int i = 1; i < coarse.Length - 1; i++)
            {
                Vector2 back = coarse[i] - coarse[i - 1];
                Vector2 forward = coarse[i + 1] - coarse[i];

                if (back.sqrMagnitude < 1f || forward.sqrMagnitude < 1f)
                    continue;

                turns[road.Kind].Add(Mathf.Acos(Mathf.Clamp(Vector2.Dot(back.normalized, forward.normalized), -1f, 1f)) * Mathf.Rad2Deg);
            }
        }

        foreach (KeyValuePair<RoadKind, List<float>> pair in turns)
            Percentiles(report, $"turnPer30m.{pair.Key}", pair.Value);

        foreach (KeyValuePair<RoadKind, List<float>> pair in radii)
        {
            pair.Value.Sort();
            report.Metric($"curveRadius.{pair.Key}.min", pair.Value.Count == 0 ? 0.0 : pair.Value[0]);
            report.Metric($"curveRadius.{pair.Key}.p05", pair.Value.Count == 0 ? 0.0 : pair.Value[(int)((pair.Value.Count - 1) * 0.05f)]);
        }
    }
}
