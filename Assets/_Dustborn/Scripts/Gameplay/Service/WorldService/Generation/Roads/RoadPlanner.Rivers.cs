using UnityEngine;

public partial class RoadPlanner
{
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
}
