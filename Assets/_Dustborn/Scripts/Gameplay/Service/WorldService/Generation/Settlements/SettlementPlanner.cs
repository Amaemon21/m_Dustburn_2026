using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public partial class SettlementPlanner
{
    private const int TILE_SAMPLES = 5;
    private const float GATEWAY_SWING = 95f;
    private const float GATEWAY_SLOPE_SHARE = 0.9f;
    private const float APPROACH_STEP = 8f;
    private const int CORE_SEARCH = 2;
    private const int BLOCK_BEYOND_GATEWAY = 2;
    private const float GROWTH_NOISE = 0.35f;
    private const float ARTERIAL_PULL = 0.6f;
    private const float ZONE_NOISE = 0.25f;

    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;
    private readonly HeightMap _map;
    private readonly WaterMap _water;
    private readonly Dictionary<long, bool> _buildable = new();

    public int RefusedSteep { get; private set; }
    public int RefusedFlooded { get; private set; }
    public int RefusedCrowded { get; private set; }
    public int RefusedOutside { get; private set; }
    public int MergedGateways { get; private set; }
    public int TopologyViolations { get; private set; }
    public int Courts { get; private set; }

    public SettlementPlanner(WorldGenerationConfig config, PoiDatabase pois, HeightMap map, WaterMap water)
    {
        _config = config;
        _pois = pois;
        _map = map;
        _water = water;
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
        List<GatewaySpec> specs = AssignGateways(layout, profile, outgoing, core);

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
}
