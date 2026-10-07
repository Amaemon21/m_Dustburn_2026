using System.Collections.Generic;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public sealed partial class TileStreetBuilder
{
    private const float CORNER_CLEARANCE = 4f;
    private const float MIN_FRONTAGE = 6f;
    private const float COURT_ANCHOR = 0.25f;
    private const float MIN_TURN_DEGREES = 1f;
    private const float ARC_STEP_RADIANS = Mathf.PI / 48f;

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
}
