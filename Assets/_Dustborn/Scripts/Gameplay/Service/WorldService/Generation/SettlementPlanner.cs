using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class SettlementPlanner
{
    private const int TILE_SAMPLES = 5;
    private const int CORE_SEARCH = 2;
    private const int BLOCK_BEYOND_GATEWAY = 2;
    private const float GROWTH_NOISE = 0.35f;
    private const float ARTERIAL_PULL = 0.6f;
    private const float ZONE_NOISE = 0.25f;

    private sealed class GatewaySpec
    {
        public TilePorts Side;
        public int Offset;
        public float Angle;
        public SettlementTile Tile;
        public readonly List<int> Neighbours = new();
    }

    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;
    private readonly HeightMap _map;
    private readonly Dictionary<long, bool> _buildable = new();

    public int RefusedSteep { get; private set; }
    public int RefusedFlooded { get; private set; }
    public int RefusedCrowded { get; private set; }
    public int RefusedOutside { get; private set; }
    public int MergedGateways { get; private set; }
    public int TopologyViolations { get; private set; }
    public int Courts { get; private set; }

    public SettlementPlanner(WorldGenerationConfig config, PoiDatabase pois, HeightMap map)
    {
        _config = config;
        _pois = pois;
        _map = map;
    }

    public List<SettlementLayout> Plan(IReadOnlyList<Hub> hubs, IReadOnlyList<RegionalLink> links)
    {
        var layouts = new List<SettlementLayout>(hubs.Count);

        if (hubs.Count == 0)
        {
            Debug.LogWarning("No settlements planned: there is not a single site. Lower SiteBuildableShare or raise MaxTileRelief");

            return layouts;
        }

        WarnMissingDistricts();

        var random = new Random(((uint)_config.Seed | 1u) * 2654435761u + 7u);
        var solver = new TileTopologySolver(_config, _map);
        var builder = new TileStreetBuilder(_config);

        for (int index = 0; index < hubs.Count; index++)
        {
            var local = new Random(random.NextUInt() | 1u);

            layouts.Add(PlanSettlement(hubs, links ?? new List<RegionalLink>(), index, solver, builder, ref local));
            TopologyViolations += solver.Violations;
            Courts += builder.Courts;
        }

        Report(layouts);

        return layouts;
    }

    public int CutLots(IReadOnlyList<SettlementLayout> layouts, RoadProximity roads)
    {
        var subdivider = new LotSubdivider(_config, _pois);
        var random = new Random(((uint)_config.Seed | 1u) * 2246822519u + 29u);

        int lots = 0;
        int frontages = 0;

        foreach (SettlementLayout layout in layouts)
        {
            subdivider.Fill(layout, roads, ref random);
            layout.Hub.SetHouses(layout.Lots.Count);

            lots += layout.Lots.Count;
            frontages += layout.Frontages.Count;
        }

        Debug.Log($"Lots: {lots} cut along {frontages} tile frontages. Dropped {subdivider.SkippedOnRoad} for covering a road, {subdivider.SkippedOverlap} for overlapping a neighbour, {subdivider.SkippedDepth} deeper than their parcel, {subdivider.SkippedOutside} past the world border, {subdivider.SkippedDensity} left open by the settlement density");

        if (subdivider.SkippedNoPrefab > 0)
            Debug.LogWarning($"Lots: {subdivider.SkippedNoPrefab} street positions had no PoiDefinition for their district, not even a Residential one");

        return lots;
    }

    private SettlementLayout PlanSettlement(IReadOnlyList<Hub> hubs, IReadOnlyList<RegionalLink> links, int index,
        TileTopologySolver solver, TileStreetBuilder builder, ref Random random)
    {
        Hub hub = hubs[index];
        SettlementTypeProfile profile = _config.Profile(hub.Type);
        List<(int Neighbour, Vector2 Direction, float Priority)> outgoing = Outgoing(hubs, links, index);
        float angle = Orientation(profile, outgoing, ref random);
        var layout = new SettlementLayout(hub, index, angle, _config.TileSize);

        foreach ((int neighbour, Vector2 _, float _) in outgoing)
            layout.Neighbours.Add(neighbour);

        _buildable.Clear();

        if (!FindCore(hubs, layout, out int coreI, out int coreJ))
        {
            layout.Measure();
            return layout;
        }

        var blocked = new HashSet<long>();
        SettlementTile core = layout.AddTile(coreI, coreJ);
        List<GatewaySpec> specs = AssignGateways(layout, profile, outgoing);

        foreach (GatewaySpec spec in specs)
            LayArm(hubs, layout, profile, core, spec, blocked);

        ExtendMainStreet(hubs, layout, profile, core, blocked);
        Grow(hubs, layout, profile, core, blocked, ref random);
        Zone(layout, profile, core, specs, ref random);
        CreateGateways(layout, specs);
        solver.Solve(layout, profile, ref random);
        builder.Build(layout, profile, ref random);

        layout.Measure();
        hub.SetRadius(layout.Radius);

        return layout;
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

    private bool FindCore(IReadOnlyList<Hub> hubs, SettlementLayout layout, out int coreI, out int coreJ)
    {
        var order = new List<(int I, int J)>();

        for (int j = -CORE_SEARCH; j <= CORE_SEARCH; j++)
        {
            for (int i = -CORE_SEARCH; i <= CORE_SEARCH; i++)
                order.Add((i, j));
        }

        order.Sort((left, right) =>
        {
            int compare = (left.I * left.I + left.J * left.J).CompareTo(right.I * right.I + right.J * right.J);

            if (compare != 0)
                return compare;

            compare = left.J.CompareTo(right.J);
            return compare != 0 ? compare : left.I.CompareTo(right.I);
        });

        foreach ((int i, int j) in order)
        {
            if (!Buildable(hubs, layout, i, j))
                continue;

            coreI = i;
            coreJ = j;
            return true;
        }

        coreI = 0;
        coreJ = 0;
        return false;
    }

    private List<GatewaySpec> AssignGateways(SettlementLayout layout, SettlementTypeProfile profile,
        List<(int Neighbour, Vector2 Direction, float Priority)> outgoing)
    {
        var specs = new List<GatewaySpec>();
        float merge = _config.GatewayMergeAngle * Mathf.Deg2Rad;
        int maxGateways = Mathf.Max(1, profile.MaxGateways);

        foreach ((int neighbour, Vector2 direction, float _) in outgoing)
        {
            float local = Mathf.Atan2(Vector2.Dot(direction, layout.AxisV), Vector2.Dot(direction, layout.AxisU));
            TilePorts side = profile.Shape == SettlementShape.Linear
                ? (Vector2.Dot(direction, layout.AxisU) >= 0f ? TilePorts.East : TilePorts.West)
                : TilePortRules.Nearest(local);

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

    private void LayArm(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core,
        GatewaySpec spec, HashSet<long> blocked)
    {
        int stepI = TilePortRules.StepI(spec.Side);
        int stepJ = TilePortRules.StepJ(spec.Side);
        bool alongU = stepI != 0;
        TilePorts offsetSide = alongU
            ? (spec.Offset > 0 ? TilePorts.North : TilePorts.South)
            : (spec.Offset > 0 ? TilePorts.East : TilePorts.West);

        SettlementTile current = core;

        for (int k = 0; k < Mathf.Abs(spec.Offset); k++)
        {
            SettlementTile next = Occupy(hubs, layout, current, offsetSide, blocked);

            if (next == null)
                break;

            current = next;
        }

        for (int k = 0; k < Mathf.Max(0, profile.ArmTiles); k++)
        {
            SettlementTile next = Occupy(hubs, layout, current, spec.Side, blocked);

            if (next == null)
                break;

            current = next;
        }

        if (TilePortRules.Has(current.GatewayPorts, spec.Side) || layout.TileAt(current.I + stepI, current.J + stepJ) != null)
            return;

        current.GatewayPorts |= spec.Side;
        current.ArterialPorts |= spec.Side;
        spec.Tile = current;

        for (int k = 1; k <= BLOCK_BEYOND_GATEWAY; k++)
            blocked.Add(SettlementLayout.Key(current.I + stepI * k, current.J + stepJ * k));
    }

    private SettlementTile Occupy(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTile from, TilePorts side, HashSet<long> blocked)
    {
        int i = from.I + TilePortRules.StepI(side);
        int j = from.J + TilePortRules.StepJ(side);

        if (blocked.Contains(SettlementLayout.Key(i, j)))
            return null;

        SettlementTile next = layout.TileAt(i, j);

        if (next == null)
        {
            if (!Buildable(hubs, layout, i, j))
                return null;

            next = layout.AddTile(i, j);
        }

        from.ArterialPorts |= side;
        next.ArterialPorts |= TilePortRules.Opposite(side);

        return next;
    }

    private void ExtendMainStreet(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, HashSet<long> blocked)
    {
        if (profile.Shape == SettlementShape.Compact)
            return;

        foreach (TilePorts side in new[] { TilePorts.East, TilePorts.West })
        {
            if (TilePortRules.Has(core.ArterialPorts, side))
                continue;

            SettlementTile current = core;

            for (int k = 0; k < Mathf.Max(1, profile.ArmTiles); k++)
            {
                SettlementTile next = Occupy(hubs, layout, current, side, blocked);

                if (next == null)
                    break;

                current = next;
            }
        }
    }

    private void Grow(IReadOnlyList<Hub> hubs, SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core,
        HashSet<long> blocked, ref Random random)
    {
        int low = profile.LowTiles;
        int high = profile.HighTiles;
        int target = Mathf.Max(layout.Tiles.Count, low + random.NextInt(high - low + 1));
        int reach = Mathf.Max(1, profile.ArmTiles) + Mathf.CeilToInt(Mathf.Sqrt(high)) + 2;
        int side = reach * 2 + 1;

        var heap = new MinHeap(64);
        var queued = new HashSet<long>();

        foreach (SettlementTile tile in new List<SettlementTile>(layout.Tiles))
            Enqueue(layout, profile, core, tile, heap, queued, reach, side, ref random);

        while (layout.Tiles.Count < target && heap.TryPop(out int id))
        {
            int i = id % side - reach + core.I;
            int j = id / side - reach + core.J;

            if (layout.TileAt(i, j) != null || blocked.Contains(SettlementLayout.Key(i, j)))
                continue;

            if (!Buildable(hubs, layout, i, j))
                continue;

            SettlementTile added = layout.AddTile(i, j);

            Enqueue(layout, profile, core, added, heap, queued, reach, side, ref random);
        }
    }

    private static void Enqueue(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, SettlementTile tile,
        MinHeap heap, HashSet<long> queued, int reach, int side, ref Random random)
    {
        foreach (TilePorts direction in TilePortRules.SIDES)
        {
            int i = tile.I + TilePortRules.StepI(direction);
            int j = tile.J + TilePortRules.StepJ(direction);
            int du = i - core.I;
            int dv = j - core.J;

            if (Mathf.Abs(du) > reach || Mathf.Abs(dv) > reach)
                continue;

            if (profile.Shape == SettlementShape.Linear && dv != 0)
                continue;

            if (profile.Shape == SettlementShape.Cross && du != 0 && dv != 0)
                continue;

            long key = SettlementLayout.Key(i, j);

            if (layout.TileAt(i, j) != null || !queued.Add(key))
                continue;

            float priority = profile.Shape == SettlementShape.Compact
                ? du * du + dv * dv
                : Mathf.Abs(du) + Mathf.Abs(dv) * 4f;

            if (profile.Shape == SettlementShape.Compact && TouchesArterial(layout, i, j))
                priority *= ARTERIAL_PULL;

            priority *= 1f + GROWTH_NOISE * random.NextFloat(-1f, 1f);

            heap.Push((dv + reach) * side + du + reach, priority);
        }
    }

    private static bool TouchesArterial(SettlementLayout layout, int i, int j)
    {
        foreach (TilePorts direction in TilePortRules.SIDES)
        {
            SettlementTile neighbour = layout.TileAt(i + TilePortRules.StepI(direction), j + TilePortRules.StepJ(direction));

            if (neighbour != null && neighbour.IsArterial)
                return true;
        }

        return false;
    }

    private static void Zone(SettlementLayout layout, SettlementTypeProfile profile, SettlementTile core, List<GatewaySpec> specs, ref Random random)
    {
        int count = layout.Tiles.Count;
        var distance = new Dictionary<SettlementTile, float>(count);

        foreach (SettlementTile tile in layout.Tiles)
        {
            int du = tile.I - core.I;
            int dv = tile.J - core.J;

            distance[tile] = du * du + dv * dv + ZONE_NOISE * random.NextFloat();
            tile.District = DistrictType.Residential;
        }

        var free = new List<SettlementTile>(layout.Tiles);

        free.Sort((left, right) => distance[left].CompareTo(distance[right]));
        Take(free, Share(count, profile.DowntownShare), DistrictType.Downtown);

        free.Sort((left, right) => (distance[left] * (left.IsArterial ? 0.5f : 1f)).CompareTo(distance[right] * (right.IsArterial ? 0.5f : 1f)));
        Take(free, Share(count, profile.CommercialShare), DistrictType.Commercial);

        int industrial = Share(count, profile.IndustrialShare);

        if (industrial > 0)
        {
            Vector2 direction = specs.Count > 0
                ? TilePortRules.Direction(specs[random.NextInt(specs.Count)].Side, layout.AxisU, layout.AxisV)
                : new Vector2(Mathf.Cos(random.NextFloat(0f, Mathf.PI * 2f)), Mathf.Sin(random.NextFloat(0f, Mathf.PI * 2f)));

            free.Sort((left, right) => Vector2.Dot(right.Center - core.Center, direction).CompareTo(Vector2.Dot(left.Center - core.Center, direction)));
            Take(free, industrial, DistrictType.Industrial);
        }

        free.RemoveAll(tile => Occupied(layout, tile) > 2);
        free.Sort((left, right) => distance[right].CompareTo(distance[left]));
        Take(free, Share(count, profile.RuralShare), DistrictType.Rural);
    }

    private static void Take(List<SettlementTile> free, int count, DistrictType district)
    {
        int taken = Mathf.Min(count, free.Count);

        for (int i = 0; i < taken; i++)
            free[i].District = district;

        free.RemoveRange(0, taken);
    }

    private static int Share(int count, float share)
    {
        if (share <= 0f)
            return 0;

        return Mathf.Max(count >= 3 ? 1 : 0, Mathf.RoundToInt(count * share));
    }

    private static int Occupied(SettlementLayout layout, SettlementTile tile)
    {
        int neighbours = 0;

        foreach (TilePorts side in TilePortRules.SIDES)
        {
            if (layout.Neighbour(tile, side) != null)
                neighbours++;
        }

        return neighbours;
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

    private bool Buildable(IReadOnlyList<Hub> hubs, SettlementLayout layout, int i, int j)
    {
        long key = SettlementLayout.Key(i, j);

        if (_buildable.TryGetValue(key, out bool cached))
            return cached;

        bool result = Evaluate(hubs, layout, i, j);
        _buildable[key] = result;

        return result;
    }

    private bool Evaluate(IReadOnlyList<Hub> hubs, SettlementLayout layout, int i, int j)
    {
        Vector2 center = layout.TileCenter(i, j);
        float half = layout.TileSize * 0.5f;
        float margin = _config.HighwaySettlementClearance;
        float low = float.MaxValue;
        float high = float.MinValue;
        bool wet = false;

        for (int b = 0; b < TILE_SAMPLES; b++)
        {
            for (int a = 0; a < TILE_SAMPLES; a++)
            {
                float u = (a / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                float v = (b / (TILE_SAMPLES - 1f) - 0.5f) * 2f * half;
                Vector2 point = center + layout.AxisU * u + layout.AxisV * v;

                if (point.x < margin || point.y < margin || point.x > _config.WorldSize - margin || point.y > _config.WorldSize - margin)
                {
                    RefusedOutside++;
                    return false;
                }

                float height = _map.SampleWorldSmooth(point.x, point.y);

                wet |= WaterMap.Wet(_config, _map, point.x, point.y, height);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);

                if ((a == 0 || a == TILE_SAMPLES - 1 || a == TILE_SAMPLES / 2) && (b == 0 || b == TILE_SAMPLES - 1 || b == TILE_SAMPLES / 2)
                    && IsCrowded(hubs, layout.Index, point))
                {
                    RefusedCrowded++;
                    return false;
                }
            }
        }

        if (wet)
        {
            RefusedFlooded++;
            return false;
        }

        if (high - low > _config.MaxTileRelief)
        {
            RefusedSteep++;
            return false;
        }

        return true;
    }

    private bool IsCrowded(IReadOnlyList<Hub> hubs, int index, Vector2 point)
    {
        float own = (point - hubs[index].Position).magnitude + _config.SettlementGap;
        float ownSqr = own * own;

        for (int other = 0; other < hubs.Count; other++)
        {
            if (other != index && (point - hubs[other].Position).sqrMagnitude < ownSqr)
                return true;
        }

        return false;
    }

    private static float Wrap(float angle)
    {
        while (angle > Mathf.PI)
            angle -= Mathf.PI * 2f;

        while (angle < -Mathf.PI)
            angle += Mathf.PI * 2f;

        return angle;
    }

    private void WarnMissingDistricts()
    {
        foreach (DistrictType district in new[] { DistrictType.Downtown, DistrictType.Commercial, DistrictType.Residential, DistrictType.Industrial })
        {
            if (_pois.HasDistrict(district))
                continue;

            Debug.LogWarning($"PoiDatabase has no PoiDefinition for the {district} district: those tiles fall back to other buildings");
        }

        if (!_pois.HasDistrict(DistrictType.Rural))
            Debug.LogWarning("PoiDatabase has no Rural PoiDefinition: outskirts and dirt spurs stay empty");
    }

    private void Report(List<SettlementLayout> layouts)
    {
        var counts = new int[4];
        var tiles = new int[4];
        int gateways = 0;
        int streets = 0;
        int empty = 0;

        foreach (SettlementLayout layout in layouts)
        {
            if (layout.IsEmpty)
            {
                empty++;
                continue;
            }

            counts[(int)layout.Type]++;
            tiles[(int)layout.Type] += layout.Tiles.Count;
            gateways += layout.Gateways.Count;
            streets += layout.Streets.Count;
        }

        Debug.Log($"Settlements: {counts[0]} cities ({tiles[0]} tiles), {counts[1]} towns ({tiles[1]}), {counts[2]} country towns ({tiles[2]}), {counts[3]} ghost towns ({tiles[3]}), {empty} without a buildable core; {gateways} gateways, {streets} streets, {Courts} courts, {MergedGateways} links sharing a gateway, {TopologyViolations} topology rule violations");
        Debug.Log($"Tiles refused: {RefusedSteep} steeper than MaxTileRelief {_config.MaxTileRelief} m, {RefusedFlooded} under water, {RefusedCrowded} inside SettlementGap {_config.SettlementGap} m of a neighbour, {RefusedOutside} past the world border");
    }
}
