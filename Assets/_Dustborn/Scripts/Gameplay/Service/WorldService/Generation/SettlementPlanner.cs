using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public class SettlementPlanner
{
    private readonly WorldGenerationConfig _config;
    private readonly PoiDatabase _pois;
    private readonly HeightMap _map;

    public SettlementPlanner(WorldGenerationConfig config, PoiDatabase pois, HeightMap map)
    {
        _config = config;
        _pois = pois;
        _map = map;
    }

    public List<SettlementLayout> Plan(IReadOnlyList<Hub> hubs, IReadOnlyList<(int From, int To)> links)
    {
        var layouts = new List<SettlementLayout>(hubs.Count);

        if (hubs.Count == 0)
        {
            Debug.LogWarning("No settlements planned: there is not a single site. Lower MaxHubRelief or MinHubDistance");

            return layouts;
        }

        WarnMissingDistricts();

        List<int>[] neighbours = RoadGraph.Neighbours(hubs.Count, links);
        var lattice = new BlockLattice(_config, _map, _pois);
        var random = new Random(((uint)_config.Seed | 1u) * 2654435761u + 7u);

        int blocks = 0;
        int streets = 0;

        for (int index = 0; index < hubs.Count; index++)
        {
            var local = new Random(random.NextUInt() | 1u);

            float angle = Orientation(hubs, index, neighbours[index], ref local);
            SettlementLayout layout = lattice.Build(hubs, index, neighbours[index], angle, ref local);

            Zone(layout, ref local);

            if (!layout.IsEmpty)
                hubs[index].SetRadius(layout.Radius);

            blocks += layout.Blocks.Count;
            streets += layout.Streets.Count;

            layouts.Add(layout);
        }

        Report(hubs.Count, blocks, streets, lattice);

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

            lots += layout.Lots.Count;
            frontages += layout.Frontages.Count;
        }

        Debug.Log($"Lots: {lots} cut along {frontages} street sides. Dropped {subdivider.SkippedOnRoad} for covering a road, {subdivider.SkippedOverlap} for overlapping a neighbour, {subdivider.SkippedOutside} past the world border");

        if (subdivider.SkippedNoPrefab > 0)
            Debug.LogWarning($"Lots: {subdivider.SkippedNoPrefab} street positions had no PoiDefinition for their district, not even a Residential one");

        return lots;
    }

    private static float Orientation(IReadOnlyList<Hub> hubs, int index, List<int> neighbours, ref Random random)
    {
        float sine = 0f;
        float cosine = 0f;

        foreach (int neighbour in neighbours)
        {
            Vector2 direction = hubs[neighbour].Position - hubs[index].Position;
            float quarter = Mathf.Atan2(direction.y, direction.x) * 4f;

            sine += Mathf.Sin(quarter);
            cosine += Mathf.Cos(quarter);
        }

        float fallback = random.NextFloat(0f, Mathf.PI * 0.5f);

        if (sine * sine + cosine * cosine < 1e-4f)
            return fallback;

        return Mathf.Atan2(sine, cosine) * 0.25f;
    }

    private void Zone(SettlementLayout layout, ref Random random)
    {
        float heading = random.NextFloat(0f, Mathf.PI * 2f);
        int count = layout.Blocks.Count;

        if (count == 0)
            return;

        Vector2 origin = layout.Hub.Position;
        var order = new List<Block>(layout.Blocks);

        order.Sort((left, right) => (left.Center - origin).sqrMagnitude.CompareTo((right.Center - origin).sqrMagnitude));

        int downtown = count >= _config.DowntownMinBlocks ? Mathf.RoundToInt(count * _config.DowntownShare) : 0;

        for (int i = 0; i < downtown; i++)
            order[i].Zone(DistrictType.Downtown);

        int industrial = count >= _config.IndustrialMinBlocks ? Mathf.RoundToInt(count * _config.IndustrialShare) : 0;

        if (industrial <= 0 || downtown >= count)
            return;

        var direction = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
        List<Block> outer = order.GetRange(downtown, count - downtown);

        outer.Sort((left, right) => Vector2.Dot(right.Center - origin, direction).CompareTo(Vector2.Dot(left.Center - origin, direction)));

        for (int i = 0; i < Mathf.Min(industrial, outer.Count); i++)
            outer[i].Zone(DistrictType.Industrial);
    }

    private void WarnMissingDistricts()
    {
        foreach (DistrictType district in new[] { DistrictType.Downtown, DistrictType.Residential, DistrictType.Industrial })
        {
            if (_pois.HasDistrict(district))
                continue;

            Debug.LogWarning($"PoiDatabase has no PoiDefinition for the {district} district: those blocks fall back to Residential buildings");
        }

        if (!_pois.HasDistrict(DistrictType.Rural))
            Debug.LogWarning("PoiDatabase has no Rural PoiDefinition: nothing will stand along the highways between settlements");
    }

    private void Report(int sites, int blocks, int streets, BlockLattice lattice)
    {
        Debug.Log($"Settlements: {sites - lattice.Empty} laid out in {blocks} blocks and {streets} streets, {lattice.Empty} found no buildable block");
        Debug.Log($"Blocks refused: {lattice.Steep} steeper than MaxBlockRelief {_config.MaxBlockRelief} m, {lattice.Flooded} under water, {lattice.Crowded} inside SettlementGap {_config.SettlementGap} m of a neighbour, {lattice.Outside} past the world border");
        Debug.Log($"Streets: {lattice.DroppedStreets} block sides left without a street, {lattice.SteepStreets} steeper than MaxStreetSlope kept only to stay connected");
    }
}
