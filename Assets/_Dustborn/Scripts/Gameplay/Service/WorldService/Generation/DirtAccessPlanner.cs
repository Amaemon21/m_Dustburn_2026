using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed class DirtAccessPlanner
{
    private const float END_CLEARANCE = 80f;
    private const float JUNCTION_GAP = 30f;
    private const float SAMPLE_STEP = 8f;
    private const float GRADE_WINDOW = 16f;
    private const float BEND = 0.25f;
    private const float ANGLE_JITTER = 25f;
    private const float SITE_SPACING = 90f;
    private const float ROAD_GAP = 6f;
    private const float SITE_BUDGET = 16f;
    private const int FIT_SAMPLES = 4;
    private const int CURVE_STEPS = 12;

    private readonly WorldGenerationConfig _config;
    private readonly HeightMap _map;

    public int Candidates { get; private set; }
    public int Built { get; private set; }
    public int RefusedTerrain { get; private set; }
    public int RefusedWater { get; private set; }
    public int RefusedSettlement { get; private set; }
    public int RefusedRoad { get; private set; }
    public int RefusedSite { get; private set; }

    public DirtAccessPlanner(WorldGenerationConfig config, HeightMap map)
    {
        _config = config;
        _map = map;
    }

    public List<RuralSite> Plan(RoadGraph graph, IReadOnlyList<SettlementLayout> layouts, IReadOnlyList<Road> streets)
    {
        var sites = new List<RuralSite>();

        if (graph == null || _config.DirtChance <= 0f)
            return sites;

        var random = new Random(((uint)_config.Seed | 1u) * 2166136261u + 41u);
        var proximity = new RoadProximity(_config.WorldSize, _config.RoadCellSize);
        var highways = new List<int>();

        foreach (RoadEdge edge in graph.Edges)
        {
            if (!edge.Alive)
                continue;

            proximity.Add(RoadKindProfile.Create(_config, edge.Points, edge.Kind));

            if (edge.Kind == RoadKind.Highway)
                highways.Add(edge.Id);
        }

        proximity.AddRange(streets);

        var centers = new List<Vector2>();
        float side = 1f;

        foreach (int id in highways)
        {
            float length = graph.Edges[id].Length;
            float along = END_CLEARANCE + _config.DirtSpacing * random.NextFloat(0.3f, 1f);

            while (along < length - END_CLEARANCE)
            {
                float current = along;

                side = -side;
                along += _config.DirtSpacing * random.NextFloat(0.7f, 1.3f);

                if (random.NextFloat() >= _config.DirtChance)
                    continue;

                Candidates++;

                float resolved = current;
                int edge = graph.Resolve(id, ref resolved);
                RoadEdge host = graph.Edges[edge];

                if (resolved < JUNCTION_GAP || resolved > host.Length - JUNCTION_GAP)
                    continue;

                Vector2 start = host.PointAt(resolved);
                Vector2 tangent = host.TangentAt(resolved);
                Vector2[] points = Curve(start, tangent, side, ref random);

                if (!Accept(points, layouts, proximity, centers, out Vector2 center))
                    continue;

                int junction = graph.Split(edge, resolved);
                int terminal = graph.AddNode(points[^1], RoadNodeKind.Terminal);

                points[0] = graph.Nodes[junction].Position;
                graph.AddEdge(junction, terminal, points, RoadKind.DirtAccess, graph.NewRoute());
                proximity.Add(RoadKindProfile.Create(_config, points, RoadKind.DirtAccess));

                Vector2 arrival = (points[^1] - points[^2]).normalized;
                int buildings = random.NextFloat() < _config.FarmsteadChance ? 2 + random.NextInt(2) : 1;

                sites.Add(new RuralSite(points[^1], -arrival, edge, buildings));
                centers.Add(center);
                Built++;
            }
        }

        Debug.Log($"Dirt access: {Built} spurs from {Candidates} candidate junctions. Refused {RefusedSettlement} near a settlement, {RefusedTerrain} too steep, {RefusedWater} into water, {RefusedRoad} touching another road, {RefusedSite} without room for a building");

        return sites;
    }

    private Vector2[] Curve(Vector2 start, Vector2 tangent, float side, ref Random random)
    {
        Vector2 normal = new Vector2(-tangent.y, tangent.x) * side;
        float jitter = random.NextFloat(-ANGLE_JITTER, ANGLE_JITTER) * Mathf.Deg2Rad;
        float cosine = Mathf.Cos(jitter);
        float sine = Mathf.Sin(jitter);
        var direction = new Vector2(normal.x * cosine - normal.y * sine, normal.x * sine + normal.y * cosine);
        float length = random.NextFloat(_config.DirtMinLength, Mathf.Max(_config.DirtMinLength, _config.DirtMaxLength));
        float bend = length * BEND * random.NextFloat(-1f, 1f);
        Vector2 control = start + direction * (length * 0.5f) + new Vector2(-direction.y, direction.x) * bend;
        Vector2 end = start + direction * length;

        Vector2[] curve = Bezier(start, control, end);

        if (RoadSmoother.MinRadius(curve, SAMPLE_STEP, 0f, 0f) < _config.DirtMinCurveRadius)
            curve = Bezier(start, start + direction * (length * 0.5f), end);

        return RoadSmoother.Resample(curve, SAMPLE_STEP);
    }

    private static Vector2[] Bezier(Vector2 start, Vector2 control, Vector2 end)
    {
        var points = new Vector2[CURVE_STEPS + 1];

        for (int i = 0; i <= CURVE_STEPS; i++)
        {
            float t = i / (float)CURVE_STEPS;
            float inverse = 1f - t;

            points[i] = start * (inverse * inverse) + control * (2f * inverse * t) + end * (t * t);
        }

        return points;
    }

    private bool Accept(Vector2[] points, IReadOnlyList<SettlementLayout> layouts, RoadProximity proximity, List<Vector2> centers, out Vector2 center)
    {
        center = Vector2.zero;

        float margin = SITE_BUDGET + _config.DirtHalfWidth;

        foreach (Vector2 point in points)
        {
            if (point.x < margin || point.y < margin || point.x > _config.WorldSize - margin || point.y > _config.WorldSize - margin)
            {
                RefusedSite++;
                return false;
            }

            if (NearSettlement(layouts, point))
            {
                RefusedSettlement++;
                return false;
            }

            if (WaterMap.Wet(_config, _map, point.x, point.y, _map.SampleWorldSmooth(point.x, point.y)))
            {
                RefusedWater++;
                return false;
            }
        }

        if (!GentleEnough(points))
        {
            RefusedTerrain++;
            return false;
        }

        float release = _config.RoadHalfWidth + _config.DirtHalfWidth + ROAD_GAP;
        float travelled = 0f;

        for (int i = 1; i < points.Length; i++)
        {
            travelled += Vector2.Distance(points[i - 1], points[i]);

            if (travelled > release && proximity.IsWithin(points[i], _config.DirtHalfWidth + ROAD_GAP))
            {
                RefusedRoad++;
                return false;
            }
        }

        Vector2 arrival = (points[^1] - points[^2]).normalized;

        center = points[^1] + arrival * (_config.DirtHalfWidth + _config.LotFrontGap + SITE_BUDGET * 0.5f);

        if (!SiteFits(center, arrival, _map.SampleWorldSmooth(points[^1].x, points[^1].y), proximity, centers, layouts))
        {
            RefusedSite++;
            return false;
        }

        return true;
    }

    private bool NearSettlement(IReadOnlyList<SettlementLayout> layouts, Vector2 point)
    {
        if (layouts == null)
            return false;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout != null && layout.IsWithin(point, _config.DirtSettlementClearance))
                return true;
        }

        return false;
    }

    private bool GentleEnough(Vector2[] points)
    {
        float probe = _config.DirtHalfWidth + _config.DirtShoulder;
        var heights = new float[points.Length];
        var distance = new float[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            heights[i] = _map.SampleWorldSmooth(points[i].x, points[i].y);

            if (i > 0)
                distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

            Vector2 direction = (points[Mathf.Min(points.Length - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
            Vector2 normal = new(-direction.y, direction.x);
            float left = _map.SampleWorldSmooth(points[i].x - normal.x * probe, points[i].y - normal.y * probe);
            float right = _map.SampleWorldSmooth(points[i].x + normal.x * probe, points[i].y + normal.y * probe);

            if (Mathf.Abs(right - left) / (2f * probe) > _config.DirtMaxCrossSlope)
                return false;
        }

        int back = 0;

        for (int i = 0; i < points.Length; i++)
        {
            while (distance[i] - distance[back] > GRADE_WINDOW)
                back++;

            float span = distance[i] - distance[back];

            if (span >= GRADE_WINDOW * 0.5f && Mathf.Abs(heights[i] - heights[back]) / span > _config.DirtMaxGrade)
                return false;
        }

        return true;
    }

    private bool SiteFits(Vector2 center, Vector2 forward, float street, RoadProximity proximity, List<Vector2> centers,
        IReadOnlyList<SettlementLayout> layouts)
    {
        float spacingSqr = SITE_SPACING * SITE_SPACING;

        foreach (Vector2 other in centers)
        {
            if ((other - center).sqrMagnitude < spacingSqr)
                return false;
        }

        if (NearSettlement(layouts, center))
            return false;

        Vector2 right = new(forward.y, -forward.x);
        float size = SITE_BUDGET + 2f * Mathf.Max(_config.PoiPadMargin, _config.HeightCellSize);

        for (int i = 0; i < FIT_SAMPLES; i++)
        {
            for (int j = 0; j < FIT_SAMPLES; j++)
            {
                float u = (i / (FIT_SAMPLES - 1f) - 0.5f) * size;
                float v = (j / (FIT_SAMPLES - 1f) - 0.5f) * size;
                Vector2 point = center + right * u + forward * v;
                float ground = _map.SampleWorldSmooth(point.x, point.y);

                if (WaterMap.Wet(_config, _map, point.x, point.y, ground))
                    return false;

                if (ground - street > _config.MaxPoiCut || street - ground > _config.MaxPoiFill)
                    return false;

                if (proximity.IsWithin(point, _config.LotFrontGap * 0.5f))
                    return false;
            }
        }

        return true;
    }
}
