using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WorldStats
{
    public int Cities;
    public int Towns;
    public int CountryTowns;
    public int GhostTowns;
    public int Buildings;
    public int WildernessBuildings;
    public int Props;
    public float HighwayKm;
    public float StreetKm;
    public float DirtKm;
    public int Rivers;
    public float RiverKm;
    public int Lakes;
    public int Craters;
    public int Rifts;
    public int Canyons;
    public float[] BiomeShares = Array.Empty<float>();
    public float Seconds;

    public int Settlements => Cities + Towns + CountryTowns + GhostTowns;

    public static WorldStats Of(WorldMapResult maps, BiomeDatabase biomes, TerrainStampDatabase stamps)
    {
        var stats = new WorldStats
        {
            Buildings = maps.Placements.Count,
            Props = maps.Props.Count,
            BiomeShares = Shares(maps.Biomes, biomes)
        };

        foreach (Hub hub in maps.Roads.Hubs)
            stats.Count(hub.Type);

        foreach (PoiPlacement placement in maps.Placements)
        {
            if (placement.District == DistrictType.Rural && !InsideSettlement(placement.Ground, maps.Settlements))
                stats.WildernessBuildings++;
        }

        stats.HighwayKm = Kilometres(maps.Roads.Roads, RoadKind.Highway);
        stats.DirtKm = Kilometres(maps.Roads.Roads, RoadKind.DirtAccess);
        stats.StreetKm = Kilometres(maps.Roads.Streets, RoadKind.Arterial) + Kilometres(maps.Roads.Streets, RoadKind.LocalStreet);

        if (maps.Water != null)
        {
            stats.Rivers = maps.Water.Rivers.Count;
            foreach (RiverPath river in maps.Water.Rivers)
                stats.RiverKm += river.Length / 1000f;

            stats.Lakes = maps.Water.Bodies.Count;
        }

        stats.CountFeatures(maps.Stamps, stamps);
        return stats;
    }

    public float BiomeShare(BiomeType type)
    {
        int index = (int)type;
        return index < BiomeShares.Length ? BiomeShares[index] : 0f;
    }

    private void Count(SettlementType type)
    {
        switch (type)
        {
            case SettlementType.City: Cities++; break;
            case SettlementType.Town: Towns++; break;
            case SettlementType.CountryTown: CountryTowns++; break;
            default: GhostTowns++; break;
        }
    }

    private void CountFeatures(IReadOnlyList<TerrainStampPlacement> placements, TerrainStampDatabase stamps)
    {
        if (placements == null)
            return;

        int first = TerrainStampShapes.FirstProcedural(stamps);

        foreach (TerrainStampPlacement placement in placements)
        {
            if (placement.StampIndex == first + TerrainStampShapes.CRATER_BOWL)
            {
                Craters++;
                continue;
            }

            if (placement.StampIndex >= first)
                continue;

            TerrainStampCategory category = stamps.Get(placement.StampIndex).Category;

            if (TerrainFeaturePlacer.IsRift(category))
                Rifts++;
            else if (TerrainFeaturePlacer.IsCanyon(category))
                Canyons++;
        }
    }

    private static float[] Shares(BiomeMap map, BiomeDatabase biomes)
    {
        var shares = new float[Enum.GetValues(typeof(BiomeType)).Length];

        if (map == null || map.Cells.Length == 0)
            return shares;

        var counts = new int[biomes.Count];
        foreach (byte cell in map.Cells)
        {
            if (cell < counts.Length)
                counts[cell]++;
        }

        for (int i = 0; i < counts.Length; i++)
            shares[(int)biomes.Get(i).Type] += counts[i] / (float)map.Cells.Length;

        return shares;
    }

    private static bool InsideSettlement(Vector2 point, IReadOnlyList<SettlementLayout> settlements)
    {
        if (settlements == null)
            return false;

        foreach (SettlementLayout settlement in settlements)
        {
            if (point.x >= settlement.Min.x && point.x <= settlement.Max.x && point.y >= settlement.Min.y && point.y <= settlement.Max.y)
                return true;
        }

        return false;
    }

    private static float Kilometres(IEnumerable<Road> roads, RoadKind kind)
    {
        float metres = 0f;

        foreach (Road road in roads)
        {
            if (road.Kind != kind || road.Points == null)
                continue;

            for (int i = 1; i < road.Points.Length; i++)
                metres += Vector2.Distance(road.Points[i - 1], road.Points[i]);
        }

        return metres / 1000f;
    }
}
