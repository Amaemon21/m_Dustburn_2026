using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public partial class RoadPlanner
{
    private void Rasterise(RoadEdge edge)
    {
        float spacing = _cellSize / 3f;

        for (float along = 0f; along <= edge.Length; along += spacing)
        {
            Vector2 point = edge.PointAt(along);
            int cell = CellOf(point);
            int x = cell % _resolution;
            int y = cell / _resolution;

            _corridorEdge[cell] = edge.Id;
            _corridorAlong[cell] = along;

            for (int dy = -BAND_CELLS; dy <= BAND_CELLS; dy++)
            {
                for (int dx = -BAND_CELLS; dx <= BAND_CELLS; dx++)
                {
                    int bandX = x + dx;
                    int bandY = y + dy;

                    if (bandX < 0 || bandY < 0 || bandX >= _resolution || bandY >= _resolution)
                        continue;

                    int band = bandY * _resolution + bandX;
                    byte ring = (byte)Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

                    if (ring > _bandDistance[band])
                        continue;

                    _bandDistance[band] = ring;
                    _bandEdge[band] = edge.Id;
                    _bandAlong[band] = along;
                }
            }
        }
    }

    private void Penalise(Vector2 point)
    {
        int cell = CellOf(point);
        int x = cell % _resolution;
        int y = cell / _resolution;

        for (int dy = -PENALTY_CELLS; dy <= PENALTY_CELLS; dy++)
        {
            for (int dx = -PENALTY_CELLS; dx <= PENALTY_CELLS; dx++)
            {
                int px = x + dx;
                int py = y + dy;

                if (px < 0 || py < 0 || px >= _resolution || py >= _resolution)
                    continue;

                _penalty[py * _resolution + px] = REROUTE_PENALTY;
            }
        }
    }

    private void MarkSettlements(IReadOnlyList<SettlementLayout> layouts, float clearanceShare = 1f)
    {
        Array.Clear(_blocked, 0, _blocked.Length);
        _layouts = layouts;
        SettledCells = 0;
        StubCells = 0;

        if (layouts == null)
            return;

        float reach = _config.HighwaySettlementClearance * clearanceShare + _cellSize * 0.75f;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout == null || layout.IsEmpty)
                continue;

            int minX = Mathf.Clamp(Mathf.FloorToInt((layout.Min.x - reach) / _cellSize), 0, _resolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((layout.Max.x + reach) / _cellSize), 0, _resolution - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((layout.Min.y - reach) / _cellSize), 0, _resolution - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((layout.Max.y + reach) / _cellSize), 0, _resolution - 1);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = y * _resolution + x;

                    if (_blocked[cell] || !layout.IsWithin(CellCenter(cell), reach))
                        continue;

                    _blocked[cell] = true;
                    SettledCells++;
                }
            }

            foreach (SettlementGateway gateway in layout.Gateways)
                BlockStub(gateway);
        }
    }

    private void BlockStub(SettlementGateway gateway)
    {
        float width = _config.RoadHalfWidth + _config.RoadShoulder + _cellSize * 0.5f;
        float length = _config.GatewayApproachLength - _cellSize;
        Vector2 far = gateway.Port + gateway.Tangent * length;

        int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(gateway.Port.x, far.x) - width) / _cellSize), 0, _resolution - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(gateway.Port.x, far.x) + width) / _cellSize), 0, _resolution - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(gateway.Port.y, far.y) - width) / _cellSize), 0, _resolution - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(gateway.Port.y, far.y) + width) / _cellSize), 0, _resolution - 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int cell = y * _resolution + x;
                Vector2 offset = CellCenter(cell) - gateway.Port;
                float along = Vector2.Dot(offset, gateway.Tangent);
                float across = Mathf.Abs(offset.x * gateway.Tangent.y - offset.y * gateway.Tangent.x);

                if (_blocked[cell] || along < 0f || along > length || across > width)
                    continue;

                _blocked[cell] = true;
                StubCells++;
            }
        }
    }

    private bool IsBlocked(Vector2 point)
    {
        return _blocked[CellOf(point)];
    }

    private void PrecomputeSteps()
    {
        float[] valley = ValleyField();

        Parallel.For(0, _resolution, y =>
        {
            for (int x = 0; x < _resolution; x++)
            {
                for (int step = 0; step < HEADINGS; step++)
                {
                    int nextX = x + STEP_X[step];
                    int nextY = y + STEP_Y[step];
                    int index = (y * _resolution + x) * HEADINGS + step;

                    if (nextX < 0 || nextY < 0 || nextX >= _resolution || nextY >= _resolution)
                    {
                        _grade[index] = float.MaxValue;
                        continue;
                    }

                    BaseCost(x, y, nextX, nextY, valley[nextY * _resolution + nextX], out _stepCost[index], out _grade[index], out _cross[index]);
                }
            }
        });

        for (int heading = 0; heading < STATES; heading++)
        {
            for (int step = 0; step < HEADINGS; step++)
            {
                _turnCost[heading * HEADINGS + step] = TurnCost(heading, step);
                _turnRadius[heading * HEADINGS + step] = TurnRadius(heading, step);
            }
        }

        for (int step = 0; step < HEADINGS; step++)
            _stepLength[step] = new Vector2(STEP_X[step], STEP_Y[step]).magnitude * _cellSize;
    }

    private float[] ValleyField()
    {
        int cellCount = _resolution * _resolution;
        var heights = new float[cellCount];
        var multiplier = new float[cellCount];

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
                heights[y * _resolution + x] = SampleMeters(x, y);
        }

        if (_config.ValleyPreference <= 0f)
        {
            Array.Fill(multiplier, 1f);
            return multiplier;
        }

        int radius = Mathf.Max(1, Mathf.RoundToInt(_config.ValleyRadius / _cellSize));
        float[] blurred = BoxBlur(heights, _resolution, radius);

        for (int i = 0; i < cellCount; i++)
        {
            float above = Mathf.Max(0f, heights[i] - blurred[i]) / VALLEY_SCALE;

            multiplier[i] = 1f + _config.ValleyPreference * Mathf.Min(above, VALLEY_CAP);
        }

        return multiplier;
    }

    private static float[] BoxBlur(float[] source, int size, int radius)
    {
        var horizontal = new float[source.Length];
        var result = new float[source.Length];
        float scale = 1f / (2 * radius + 1);

        for (int y = 0; y < size; y++)
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += source[y * size + Mathf.Clamp(k, 0, size - 1)];

            for (int x = 0; x < size; x++)
            {
                horizontal[y * size + x] = sum * scale;
                sum += source[y * size + Mathf.Min(x + radius + 1, size - 1)] - source[y * size + Mathf.Max(x - radius, 0)];
            }
        }

        for (int x = 0; x < size; x++)
        {
            float sum = 0f;

            for (int k = -radius; k <= radius; k++)
                sum += horizontal[Mathf.Clamp(k, 0, size - 1) * size + x];

            for (int y = 0; y < size; y++)
            {
                result[y * size + x] = sum * scale;
                sum += horizontal[Mathf.Min(y + radius + 1, size - 1) * size + x] - horizontal[Mathf.Max(y - radius, 0) * size + x];
            }
        }

        return result;
    }

    private void BaseCost(int fromX, int fromY, int toX, int toY, float valley, out float cost, out float grade, out float cross)
    {
        float deltaX = (toX - fromX) * _cellSize;
        float deltaY = (toY - fromY) * _cellSize;
        float distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

        float fromHeight = SampleMeters(fromX, fromY);
        float toHeight = SampleMeters(toX, toY);

        grade = StepGrade(fromX, fromY, toX, toY, distance);
        cross = CrossSlope(fromX, fromY, toX, toY, deltaX, deltaY, distance);

        float gradeOvershoot = Mathf.Max(0f, grade - _config.RoadMaxGrade) / Mathf.Max(_config.RoadMaxGrade, 1e-3f);
        float crossOvershoot = Mathf.Max(0f, cross - _config.HighwayMaxCrossSlope) / Mathf.Max(_config.HighwayMaxCrossSlope, 1e-3f);
        float overshoot = OVERSHOOT_PENALTY * gradeOvershoot * gradeOvershoot + CROSS_OVERSHOOT_PENALTY * crossOvershoot * crossOvershoot;

        cost = distance * (1f + _config.RoadSlopePenalty * grade * grade + _config.RoadCrossSlopePenalty * cross * cross + overshoot) * valley;

        cost *= WaterCost(fromX, fromY, toX, toY, toHeight);
    }

    private float WaterCost(int fromX, int fromY, int toX, int toY, float toHeight)
    {
        var from = new Vector2((fromX + 0.5f) * _cellSize, (fromY + 0.5f) * _cellSize);
        var to = new Vector2((toX + 0.5f) * _cellSize, (toY + 0.5f) * _cellSize);

        WaterMap water = _water;

        if (water == null)
            return _config.SeaLevel > 0f && toHeight < _config.SeaLevel + _config.ShoreMargin ? FORD_PENALTY : 1f;

        int samples = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / water.CellSize));

        for (int i = 1; i <= samples; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)samples);
            WaterSample sample = water.Sample(point.x, point.y);

            if (sample.IsWater && sample.Kind != WaterKind.River && _map.SampleWorldSmooth(point.x, point.y) < sample.Surface + _config.ShoreMargin)
                return STANDING_PENALTY;
        }

        Vector2 step = (to - from).normalized;

        if (!water.CrossesRiver(from, to, out float width, out Vector2 flow))
            return RiverContact(to, step) ? BANK_PENALTY : 1f;

        float along = Mathf.Abs(Vector2.Dot(step, flow));
        float cost = 1f + _config.Water.RiverCrossingPenalty * width * 0.1f * (1f + 2f * along);

        return width >= _config.Water.BridgeMinWidth && along > MAX_BRIDGE_COSINE ? cost * OBLIQUE_PENALTY : cost;
    }

    private void CheckRiverAngle(Route route, Vector2 from, Vector2 to, Vector2 direction)
    {
        WaterMap water = _water;

        if (water == null || water.Rivers.Count == 0)
            return;

        if (!water.CrossesRiver(from, to, out float width, out Vector2 flow, out Vector2 point, out _) || width < _config.Water.BridgeMinWidth)
            return;

        if (flow.sqrMagnitude < 1e-6f || Mathf.Abs(Vector2.Dot(direction, flow.normalized)) <= MAX_BRIDGE_COSINE_HARD)
            return;

        route.Oblique++;
        route.Problems.Add(point);
    }

    private bool RiverContact(Vector2 point, Vector2 direction)
    {
        WaterMap water = _water;
        var side = new Vector2(-direction.y, direction.x) * (_config.RoadHalfWidth + 0.5f * _config.RoadShoulder);

        for (int k = -1; k <= 1; k++)
        {
            Vector2 at = point + side * k;

            if (water.TryRiver(at.x, at.y, out _))
                return true;
        }

        return false;
    }

    private bool LooseRiverContact(Vector2 point, Vector2 direction)
    {
        WaterMap water = _water;

        if (water == null || water.Rivers.Count == 0)
            return false;

        var side = new Vector2(-direction.y, direction.x) * (_config.RoadHalfWidth + 0.5f * _config.RoadShoulder);

        for (int k = -1; k <= 1; k++)
        {
            Vector2 at = point + side * k;

            if (!water.TryRiver(at.x, at.y, out WaterSample sample))
                continue;

            float reach = sample.Width + _config.RoadHalfWidth + _config.RoadShoulder;

            if (!water.CrossesRiver(point - direction * reach, point + direction * reach, out _, out _))
                return true;
        }

        return false;
    }

    private bool AlongRiver(Vector2 point, Vector2 direction)
    {
        WaterMap water = _water;

        if (water == null || water.Rivers.Count == 0)
            return false;

        var side = new Vector2(-direction.y, direction.x) * (_config.RoadHalfWidth + 0.5f * _config.RoadShoulder);

        for (int k = -1; k <= 1; k++)
        {
            Vector2 at = point + side * k;

            if (water.TryRiver(at.x, at.y, out WaterSample sample) && sample.Flow.sqrMagnitude > 1e-6f && Mathf.Abs(Vector2.Dot(direction, sample.Flow.normalized)) > MAX_BRIDGE_COSINE_HARD)
                return true;
        }

        return false;
    }

    private bool Standing(Vector2 point, float ground)
    {
        WaterMap water = _water;

        if (water == null)
            return false;

        WaterSample sample = water.Sample(point.x, point.y);

        return sample.IsWater && sample.Kind != WaterKind.River && ground < sample.Surface + FLOOD_MARGIN;
    }

    private float TurnCost(int heading, int step)
    {
        if (heading == FREE_HEADING || _config.RoadTurnPenalty <= 0f)
            return 0f;

        float dot = Mathf.Clamp(Vector2.Dot(_headings[heading], _headings[step]), -1f, 1f);

        return Mathf.Acos(dot) * _config.RoadTurnPenalty;
    }

    private float TurnRadius(int heading, int step)
    {
        if (heading == FREE_HEADING)
            return float.MaxValue;

        float dot = Vector2.Dot(_headings[heading], _headings[step]);

        if (dot > 0.9999f)
            return float.MaxValue;

        if (dot <= 0f)
            return 0f;

        Vector2 before = -new Vector2(STEP_X[heading], STEP_Y[heading]) * _cellSize;
        Vector2 after = new Vector2(STEP_X[step], STEP_Y[step]) * _cellSize;

        return RoadSmoother.Circumradius(before, Vector2.zero, after);
    }

    private float CrossSlope(int fromX, int fromY, int toX, int toY, float deltaX, float deltaY, float distance)
    {
        float perpendicularX = -deltaY / distance;
        float perpendicularY = deltaX / distance;
        float probe = _config.RoadHalfWidth + _config.RoadShoulder;
        float steepest = 0f;

        for (int k = 1; k <= CROSS_SAMPLES; k++)
        {
            float t = k / (CROSS_SAMPLES + 1f);
            float x = (fromX + 0.5f + (toX - fromX) * t) * _cellSize;
            float y = (fromY + 0.5f + (toY - fromY) * t) * _cellSize;
            float left = _map.SampleWorldSmooth(x - perpendicularX * probe, y - perpendicularY * probe);
            float right = _map.SampleWorldSmooth(x + perpendicularX * probe, y + perpendicularY * probe);

            steepest = Mathf.Max(steepest, Mathf.Abs(right - left) / (2f * probe));
        }

        return steepest;
    }

    private int HeadingOf(Vector2 direction)
    {
        int best = 0;
        float bestDot = float.MinValue;

        for (int i = 0; i < HEADINGS; i++)
        {
            float dot = Vector2.Dot(_headings[i], direction);

            if (dot <= bestDot)
                continue;

            bestDot = dot;
            best = i;
        }

        return best;
    }

    private float SampleMeters(int cellX, int cellY)
    {
        return _map.SampleWorld(new Vector3((cellX + 0.5f) * _cellSize, 0f, (cellY + 0.5f) * _cellSize));
    }

    private int CellOf(Vector2 position)
    {
        int x = Mathf.Clamp((int)(position.x / _cellSize), 0, _resolution - 1);
        int y = Mathf.Clamp((int)(position.y / _cellSize), 0, _resolution - 1);

        return y * _resolution + x;
    }

    private Vector2 CellCenter(int cell)
    {
        return new Vector2((cell % _resolution + 0.5f) * _cellSize, (cell / _resolution + 0.5f) * _cellSize);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Length(Vector2[] points)
    {
        float total = 0f;

        for (int i = 1; i < points.Length; i++)
            total += Vector2.Distance(points[i - 1], points[i]);

        return total;
    }

    private static float[] Cumulative(Vector2[] points)
    {
        var distance = new float[points.Length];

        for (int i = 1; i < points.Length; i++)
            distance[i] = distance[i - 1] + Vector2.Distance(points[i - 1], points[i]);

        return distance;
    }

    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t, out float u)
    {
        t = 0f;
        u = 0f;

        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = r.x * s.y - r.y * s.x;

        if (Mathf.Abs(denominator) < 1e-8f)
            return false;

        Vector2 delta = c - a;

        t = (delta.x * s.y - delta.y * s.x) / denominator;
        u = (delta.x * r.y - delta.y * r.x) / denominator;

        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }
}
