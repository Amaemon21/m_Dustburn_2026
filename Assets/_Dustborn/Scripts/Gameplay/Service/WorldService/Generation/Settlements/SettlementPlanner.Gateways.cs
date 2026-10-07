using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial class SettlementPlanner
{
    private sealed class GatewaySpec
    {
        public TilePorts Side;
        public int Offset;
        public float Angle;
        public SettlementTile Tile;
        public readonly List<int> Neighbours = new();
    }

    private List<GatewaySpec> AssignGateways(SettlementLayout layout, SettlementTypeProfile profile,
        List<(int Neighbour, Vector2 Direction, float Priority)> outgoing, SettlementTile core)
    {
        var specs = new List<GatewaySpec>();
        float merge = _config.GatewayMergeAngle * Mathf.Deg2Rad;
        int maxGateways = Mathf.Max(1, profile.MaxGateways);

        foreach ((int neighbour, Vector2 direction, float _) in outgoing)
        {
            float local = Mathf.Atan2(Vector2.Dot(direction, layout.AxisV), Vector2.Dot(direction, layout.AxisU));
            TilePorts side = profile.Shape == SettlementShape.Linear
                ? (Vector2.Dot(direction, layout.AxisU) >= 0f ? TilePorts.East : TilePorts.West)
                : GentleSide(layout, profile, core, local);

            GatewaySpec spec = null;
            int sameSide = 0;

            foreach (GatewaySpec existing in specs)
            {
                if (existing.Side != side)
                    continue;

                sameSide++;

                if (spec == null && Mathf.Abs(Wrap(local - existing.Angle)) <= merge)
                    spec = existing;
            }

            bool canSplit = layout.Type == SettlementType.City && sameSide == 1 && specs.Count < maxGateways;

            if (spec == null && sameSide > 0 && !canSplit)
                spec = specs.Find(existing => existing.Side == side);

            if (spec == null && specs.Count >= maxGateways)
                spec = Nearest(specs, local);

            if (spec != null)
            {
                spec.Neighbours.Add(neighbour);
                MergedGateways++;
                continue;
            }

            spec = new GatewaySpec { Side = side, Angle = local };

            if (sameSide > 0)
                spec.Offset = Wrap(local - TilePortRules.Angle(side)) >= 0f ? 1 : -1;

            spec.Neighbours.Add(neighbour);
            specs.Add(spec);
        }

        return specs;
    }

    private TilePorts GentleSide(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, float local)
    {
        TilePorts nearest = TilePortRules.Nearest(local);
        var candidates = new List<TilePorts>();

        foreach (TilePorts side in new[] { TilePorts.East, TilePorts.North, TilePorts.West, TilePorts.South })
        {
            if (side == nearest || Mathf.Abs(Wrap(local - TilePortRules.Angle(side))) <= GATEWAY_SWING * Mathf.Deg2Rad)
                candidates.Add(side);
        }

        candidates.Sort((a, b) => Mathf.Abs(Wrap(local - TilePortRules.Angle(a))).CompareTo(Mathf.Abs(Wrap(local - TilePortRules.Angle(b)))));

        foreach (TilePorts side in candidates)
        {
            if (ApproachSlope(layout, profile, core, side) <= _config.HighwayMaxCrossSlope * GATEWAY_SLOPE_SHARE)
                return side;
        }

        return nearest;
    }

    private float ApproachSlope(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, TilePorts side)
    {
        float angle = TilePortRules.Angle(side);
        Vector2 direction = layout.AxisU * Mathf.Cos(angle) + layout.AxisV * Mathf.Sin(angle);
        var normal = new Vector2(-direction.y, direction.x);
        float probe = _config.RoadHalfWidth + _config.RoadShoulder;
        Vector2 start = layout.LocalToWorld(core.I * layout.TileSize, core.J * layout.TileSize) + direction * ((Mathf.Max(0, profile.ArmTiles) + 0.5f) * layout.TileSize);
        float steepest = 0f;

        for (float along = 0f; along <= _config.GatewayApproachLength; along += APPROACH_STEP)
        {
            Vector2 point = start + direction * along;
            Vector2 left = point - normal * probe;
            Vector2 right = point + normal * probe;

            if (Outside(left) || Outside(right) || InWater(point) || InWater(left) || InWater(right))
                return float.MaxValue;

            steepest = Mathf.Max(steepest, Mathf.Abs(_map.SampleWorldSmooth(right.x, right.y) - _map.SampleWorldSmooth(left.x, left.y)) / (2f * probe));
        }

        return steepest;
    }

    private static GatewaySpec Nearest(List<GatewaySpec> specs, float local)
    {
        GatewaySpec best = null;
        float bestDifference = float.MaxValue;

        foreach (GatewaySpec spec in specs)
        {
            float difference = Mathf.Abs(Wrap(local - spec.Angle));

            if (difference >= bestDifference)
                continue;

            bestDifference = difference;
            best = spec;
        }

        return best;
    }

    private void CreateGateways(SettlementLayout layout, List<GatewaySpec> specs)
    {
        GatewaySpec fallback = specs.Find(spec => spec.Tile != null);

        foreach (GatewaySpec spec in specs)
        {
            if (spec.Tile != null || fallback == null)
                continue;

            fallback.Neighbours.AddRange(spec.Neighbours);
            MergedGateways += spec.Neighbours.Count;
        }

        foreach (GatewaySpec spec in specs)
        {
            if (spec.Tile == null)
                continue;

            Vector2 tangent = TilePortRules.Direction(spec.Side, layout.AxisU, layout.AxisV);
            var gateway = new SettlementGateway(layout.Gateways.Count, spec.Tile, spec.Side, layout.PortPoint(spec.Tile, spec.Side), tangent, _config.GatewayApproachLength);

            gateway.Neighbours.AddRange(spec.Neighbours);
            layout.Gateways.Add(gateway);
        }
    }

    private static List<(int Neighbour, Vector2 Direction, float Priority)> Outgoing(IReadOnlyList<Hub> hubs, IReadOnlyList<RegionalLink> links, int index)
    {
        var outgoing = new List<(int Neighbour, Vector2 Direction, float Priority)>();

        foreach (RegionalLink link in links)
        {
            if (link.From != index && link.To != index)
                continue;

            int other = link.Other(index);
            Vector2 direction = (hubs[other].Position - hubs[index].Position).normalized;

            outgoing.Add((other, direction, link.Priority));
        }

        outgoing.Sort((left, right) =>
        {
            int compare = right.Priority.CompareTo(left.Priority);
            return compare != 0 ? compare : left.Neighbour.CompareTo(right.Neighbour);
        });

        return outgoing;
    }

    private static float Orientation(SettlementTypeProfile profile, List<(int Neighbour, Vector2 Direction, float Priority)> outgoing, ref Random random)
    {
        float fallback = random.NextFloat(0f, Mathf.PI * 0.5f);

        if (outgoing.Count == 0)
            return fallback;

        float main = Mathf.Atan2(outgoing[0].Direction.y, outgoing[0].Direction.x);

        if (profile.Shape != SettlementShape.Compact)
            return main;

        float sine = 0f;
        float cosine = 0f;

        for (int rank = 0; rank < outgoing.Count; rank++)
        {
            float quarter = Mathf.Atan2(outgoing[rank].Direction.y, outgoing[rank].Direction.x) * 4f;
            float weight = 1f / (1f + rank);

            sine += Mathf.Sin(quarter) * weight;
            cosine += Mathf.Cos(quarter) * weight;
        }

        if (sine * sine + cosine * cosine < 1e-4f)
            return main;

        return Mathf.Atan2(sine, cosine) * 0.25f;
    }
}
