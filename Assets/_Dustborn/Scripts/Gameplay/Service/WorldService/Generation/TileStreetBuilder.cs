using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed class TileStreetBuilder
{
    private const float CORNER_CLEARANCE = 4f;
    private const float MIN_FRONTAGE = 6f;
    private const float COURT_ANCHOR = 0.25f;
    private const float MIN_TURN_DEGREES = 1f;
    private const float ARC_STEP_RADIANS = Mathf.PI / 48f;

    private sealed class HalfStreet
    {
        public SettlementTile Tile;
        public TilePorts Side;
        public RoadKind Kind;
        public Vector2 Center;
        public Vector2 Port;
        public HalfStreet Pair;
        public HalfStreet Link;
        public bool Used;
        public readonly List<float> Anchors = new();
    }

    private readonly struct Court
    {
        public readonly HalfStreet Host;
        public readonly float Along;
        public readonly TilePorts Toward;
        public readonly float Length;

        public Court(HalfStreet host, float along, TilePorts toward, float length)
        {
            Host = host;
            Along = along;
            Toward = toward;
            Length = length;
        }
    }

    private readonly WorldGenerationConfig _config;

    public int Courts { get; private set; }

    public TileStreetBuilder(WorldGenerationConfig config)
    {
        _config = config;
    }

    public void Build(SettlementLayout layout, SettlementTypeProfile profile, ref Random random)
    {
        layout.Streets.Clear();
        layout.StreetNodes.Clear();
        layout.Frontages.Clear();

        if (layout.IsEmpty)
            return;

        List<HalfStreet> halves = Halves(layout);
        List<Court> courts = PlanCourts(layout, halves, profile, ref random);

        Chain(layout, halves);

        foreach (Court court in courts)
            AddCourt(layout, court);

        foreach (HalfStreet half in halves)
            AddHalfFrontages(layout, half, profile);

        foreach (Court court in courts)
            AddCourtFrontages(layout, court, profile);

        foreach (SettlementTile tile in layout.Tiles)
        {
            if (TilePortRules.Count(tile.Ports) != 2)
                AddNode(layout, tile.Center);
        }
    }

    private List<HalfStreet> Halves(SettlementLayout layout)
    {
        var halves = new List<HalfStreet>();
        var lookup = new Dictionary<(SettlementTile, TilePorts), HalfStreet>();

        foreach (SettlementTile tile in layout.Tiles)
        {
            foreach (TilePorts side in TilePortRules.SIDES)
            {
                if (!TilePortRules.Has(tile.Ports, side))
                    continue;

                var half = new HalfStreet
                {
                    Tile = tile,
                    Side = side,
                    Kind = TilePortRules.Has(tile.ArterialPorts, side) || TilePortRules.Has(tile.GatewayPorts, side) ? RoadKind.Arterial : RoadKind.LocalStreet,
                    Center = tile.Center,
                    Port = layout.PortPoint(tile, side)
                };

                halves.Add(half);
                lookup[(tile, side)] = half;
            }
        }

        foreach (HalfStreet half in halves)
        {
            SettlementTile neighbour = layout.Neighbour(half.Tile, half.Side);

            if (neighbour != null && lookup.TryGetValue((neighbour, TilePortRules.Opposite(half.Side)), out HalfStreet across))
                half.Link = across;

            if (half.Pair != null)
                continue;

            TilePorts opposite = TilePortRules.Opposite(half.Side);

            if (lookup.TryGetValue((half.Tile, opposite), out HalfStreet straight))
            {
                half.Pair = straight;
                straight.Pair = half;
                continue;
            }

            if (half.Tile.Shape != TileShape.Corner)
                continue;

            foreach (TilePorts side in TilePortRules.SIDES)
            {
                if (side == half.Side || !lookup.TryGetValue((half.Tile, side), out HalfStreet bend))
                    continue;

                half.Pair = bend;
                bend.Pair = half;
                break;
            }
        }

        return halves;
    }

    private List<Court> PlanCourts(SettlementLayout layout, List<HalfStreet> halves, SettlementTypeProfile profile, ref Random random)
    {
        var courts = new List<Court>();
        float half = layout.TileSize * 0.5f;
        float along = layout.TileSize * COURT_ANCHOR;
        float limit = half - _config.StreetHalfWidth - _config.LotFrontGap - CORNER_CLEARANCE;
        float length = Mathf.Min(_config.CourtDepth, limit);
        float typeChance = TypeCourtChance(layout.Type);

        Courts = 0;

        if (length < MIN_FRONTAGE || typeChance <= 0f)
            return courts;

        var industrialTaken = new HashSet<SettlementTile>();

        foreach (HalfStreet host in halves)
        {
            float chance = DistrictCourtChance(host.Tile.District) * typeChance;
            float roll = random.NextFloat();

            if (roll >= chance)
                continue;

            if (host.Tile.District == DistrictType.Industrial && !industrialTaken.Add(host.Tile))
                continue;

            TilePorts toward = Rotate(host.Side);

            host.Anchors.Add(along);
            courts.Add(new Court(host, along, toward, host.Tile.District == DistrictType.Industrial ? limit : length));
            Courts++;
        }

        return courts;
    }

    private void Chain(SettlementLayout layout, List<HalfStreet> halves)
    {
        foreach (HalfStreet half in halves)
        {
            if (half.Used)
                continue;

            if (half.Pair == null)
                Walk(layout, half, true);
        }

        foreach (HalfStreet half in halves)
        {
            if (half.Used || half.Link != null)
                continue;

            Walk(layout, half, false);
        }

        foreach (HalfStreet half in halves)
        {
            if (!half.Used && half.Tile.Shape == TileShape.Straight)
                Walk(layout, half, true);
        }

        foreach (HalfStreet half in halves)
        {
            if (!half.Used)
                Walk(layout, half, true);
        }
    }

    private void Walk(SettlementLayout layout, HalfStreet start, bool fromCenter)
    {
        var points = new List<Vector2>();
        HalfStreet current = start;
        bool enteringCenter = fromCenter;
        RoadKind kind = start.Kind;

        while (current != null && !current.Used)
        {
            if (current.Kind != kind && points.Count >= 2)
            {
                Flush(layout, points, kind);
                Vector2 joint = points[^1];
                points.Clear();
                points.Add(joint);
                kind = current.Kind;
            }

            current.Used = true;
            AppendHalf(current, enteringCenter, points);

            HalfStreet next;

            if (enteringCenter)
            {
                next = current.Link;
                enteringCenter = false;
            }
            else
            {
                next = current.Pair;
                enteringCenter = true;
            }

            current = next;
        }

        Flush(layout, points, kind);
    }

    private void AppendHalf(HalfStreet half, bool fromCenter, List<Vector2> points)
    {
        Vector2 direction = half.Port - half.Center;
        float length = direction.magnitude;
        Vector2 unit = direction / Mathf.Max(0.001f, length);
        var anchors = new List<float>(half.Anchors);

        anchors.Sort();

        if (!fromCenter)
            anchors.Reverse();

        Add(points, fromCenter ? half.Center : half.Port);

        foreach (float anchor in anchors)
            Add(points, half.Center + unit * anchor);

        Add(points, fromCenter ? half.Port : half.Center);
    }

    private static void Add(List<Vector2> points, Vector2 point)
    {
        if (points.Count > 0 && (points[^1] - point).sqrMagnitude < 1e-4f)
            return;

        points.Add(point);
    }

    private void Flush(SettlementLayout layout, List<Vector2> points, RoadKind kind)
    {
        if (points.Count < 2)
            return;

        AddNode(layout, points[0]);
        AddNode(layout, points[^1]);

        Vector2[] shaped = Fillet(points, RoadKindProfile.For(_config, kind).MinCurveRadius);

        layout.Streets.Add(RoadKindProfile.Create(_config, shaped, kind));
    }

    private void AddCourt(SettlementLayout layout, Court court)
    {
        Vector2 unit = (court.Host.Port - court.Host.Center).normalized;
        Vector2 anchor = court.Host.Center + unit * court.Along;
        Vector2 direction = TilePortRules.Direction(court.Toward, layout.AxisU, layout.AxisV);
        Vector2 end = anchor + direction * court.Length;

        AddNode(layout, anchor);
        AddNode(layout, end);

        layout.Streets.Add(RoadKindProfile.Create(_config, new[] { anchor, end }, RoadKind.LocalStreet));
    }

    private void AddHalfFrontages(SettlementLayout layout, HalfStreet half, SettlementTypeProfile profile)
    {
        RoadKindProfile road = RoadKindProfile.For(_config, half.Kind);
        Vector2 unit = (half.Port - half.Center).normalized;
        Vector2 left = new(-unit.y, unit.x);
        float halfTile = layout.TileSize * 0.5f;
        float centerTrim = CenterTrim(half.Tile);
        float depth = halfTile - road.HalfWidth - _config.LotFrontGap;
        float density = Density(half.Tile.District, profile);

        foreach (float sign in new[] { 1f, -1f })
        {
            Vector2 normal = left * sign;
            TilePorts sideTowards = SideOf(layout, normal);
            var cuts = new List<(float From, float To)> { (centerTrim, halfTile) };

            foreach (float anchor in half.Anchors)
            {
                if (Rotate(half.Side) != sideTowards)
                    continue;

                float trim = _config.StreetHalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
                cuts = Cut(cuts, anchor - trim, anchor + trim);
            }

            foreach ((float from, float to) in cuts)
            {
                if (to - from < MIN_FRONTAGE)
                    continue;

                Vector2 start = half.Center + unit * from;
                Vector2 end = half.Center + unit * to;

                layout.Frontages.Add(new Frontage(start, end, normal, road.HalfWidth, half.Tile.District, depth, density, half.Kind));
            }
        }
    }

    private void AddCourtFrontages(SettlementLayout layout, Court court, SettlementTypeProfile profile)
    {
        Vector2 hostUnit = (court.Host.Port - court.Host.Center).normalized;
        Vector2 anchor = court.Host.Center + hostUnit * court.Along;
        Vector2 direction = TilePortRules.Direction(court.Toward, layout.AxisU, layout.AxisV);
        RoadKindProfile host = RoadKindProfile.For(_config, court.Host.Kind);
        float start = host.HalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
        float halfTile = layout.TileSize * 0.5f;
        float depthTowardCenter = court.Along - _config.StreetHalfWidth - _config.LotFrontGap;
        float depthTowardEdge = halfTile - court.Along - _config.StreetHalfWidth - _config.LotFrontGap;
        float density = Density(court.Host.Tile.District, profile);

        if (court.Length - start < MIN_FRONTAGE)
            return;

        Vector2 from = anchor + direction * start;
        Vector2 to = anchor + direction * court.Length;

        layout.Frontages.Add(new Frontage(from, to, -hostUnit, _config.StreetHalfWidth, court.Host.Tile.District, depthTowardCenter, density, RoadKind.LocalStreet));
        layout.Frontages.Add(new Frontage(from, to, hostUnit, _config.StreetHalfWidth, court.Host.Tile.District, depthTowardEdge, density, RoadKind.LocalStreet));
    }

    private float CenterTrim(SettlementTile tile)
    {
        switch (tile.Shape)
        {
            case TileShape.Tee:
            case TileShape.Intersection:
                return _config.ArterialHalfWidth + _config.LotFrontGap + CORNER_CLEARANCE;
            case TileShape.Corner:
                return _config.StreetCornerRadius + CORNER_CLEARANCE;
            default:
                return 0f;
        }
    }

    private static List<(float From, float To)> Cut(List<(float From, float To)> ranges, float from, float to)
    {
        var result = new List<(float, float)>();

        foreach ((float start, float end) in ranges)
        {
            if (to <= start || from >= end)
            {
                result.Add((start, end));
                continue;
            }

            if (from > start)
                result.Add((start, from));

            if (to < end)
                result.Add((to, end));
        }

        return result;
    }

    private static TilePorts SideOf(SettlementLayout layout, Vector2 normal)
    {
        float u = Vector2.Dot(normal, layout.AxisU);
        float v = Vector2.Dot(normal, layout.AxisV);

        return TilePortRules.Nearest(Mathf.Atan2(v, u));
    }

    private static TilePorts Rotate(TilePorts side)
    {
        return side switch
        {
            TilePorts.East => TilePorts.North,
            TilePorts.North => TilePorts.West,
            TilePorts.West => TilePorts.South,
            _ => TilePorts.East
        };
    }

    private static void AddNode(SettlementLayout layout, Vector2 point)
    {
        foreach (Vector2 existing in layout.StreetNodes)
        {
            if ((existing - point).sqrMagnitude < 1e-2f)
                return;
        }

        layout.StreetNodes.Add(point);
    }

    public static Vector2[] Fillet(List<Vector2> points, float radius)
    {
        if (points.Count < 3 || radius <= 0f)
            return points.ToArray();

        var result = new List<Vector2>(points.Count * 4) { points[0] };
        float minTurn = MIN_TURN_DEGREES * Mathf.Deg2Rad;

        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 previous = result[^1];
            Vector2 corner = points[i];
            Vector2 next = points[i + 1];

            Vector2 incoming = corner - previous;
            Vector2 outgoing = next - corner;
            float incomingLength = incoming.magnitude;
            float outgoingLength = outgoing.magnitude;

            if (incomingLength < 1e-3f || outgoingLength < 1e-3f)
            {
                result.Add(corner);
                continue;
            }

            Vector2 d1 = incoming / incomingLength;
            Vector2 d2 = outgoing / outgoingLength;
            float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));

            if (turn < minTurn || turn > Mathf.PI - minTurn)
            {
                result.Add(corner);
                continue;
            }

            float halfTurn = Mathf.Tan(turn * 0.5f);
            float tangent = Mathf.Min(radius * halfTurn, incomingLength * 0.5f, outgoingLength * 0.5f);
            float arcRadius = tangent / halfTurn;
            float side = d1.x * d2.y - d1.y * d2.x > 0f ? 1f : -1f;

            Vector2 entry = corner - d1 * tangent;
            Vector2 normal = side > 0f ? new Vector2(-d1.y, d1.x) : new Vector2(d1.y, -d1.x);
            Vector2 center = entry + normal * arcRadius;
            Vector2 arm = entry - center;
            int segments = Mathf.Max(2, Mathf.CeilToInt(turn / ARC_STEP_RADIANS));

            result.Add(entry);

            for (int step = 1; step <= segments; step++)
            {
                float angle = side * turn * step / segments;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);

                result.Add(center + new Vector2(arm.x * cosine - arm.y * sine, arm.x * sine + arm.y * cosine));
            }
        }

        result.Add(points[^1]);

        return result.ToArray();
    }

    private static float TypeCourtChance(SettlementType type)
    {
        return type switch
        {
            SettlementType.City => 1f,
            SettlementType.Town => 0.8f,
            SettlementType.CountryTown => 0.35f,
            _ => 0f
        };
    }

    private static float DistrictCourtChance(DistrictType district)
    {
        return district switch
        {
            DistrictType.Downtown => 1f,
            DistrictType.Commercial => 0.5f,
            DistrictType.Residential => 0.6f,
            DistrictType.Industrial => 0.5f,
            _ => 0f
        };
    }

    private static float Density(DistrictType district, SettlementTypeProfile profile)
    {
        float factor = district switch
        {
            DistrictType.Residential => 0.9f,
            DistrictType.Industrial => 0.8f,
            DistrictType.Rural => 0.45f,
            _ => 1f
        };

        return Mathf.Clamp01(profile.PoiDensity * factor);
    }
}
