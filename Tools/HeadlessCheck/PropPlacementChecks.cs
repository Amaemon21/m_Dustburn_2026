using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class PropPlacementChecks
{
    public static PropDatabase Database()
    {
        var database = new PropDatabase();
        database.EditorAdd(Definition("car", PropContext.Road | PropContext.Street, 3f, 2.4f, true, true, 14f));
        database.EditorAdd(Definition("tire", PropContext.Road | PropContext.Street | PropContext.Yard | PropContext.Wild, 2f, 0.5f, false, false, 25f, 1, 3));
        database.EditorAdd(Definition("crate", PropContext.Yard | PropContext.Street, 2f, 0.8f, false, false, 20f));
        database.EditorAdd(Definition("tent", PropContext.Wild, 1f, 2f, false, false, 10f));
        database.EditorAdd(Definition("tire", PropContext.Yard, 1f, 0.5f, false, false, 25f));

        Set(database, nameof(database.HighwayPer100m), 6f);
        Set(database, nameof(database.StreetPer100m), 8f);
        Set(database, nameof(database.WildPerKm2), 200f);
        return database;
    }

    public static void Check(WorldGenerationConfig config, PropDatabase database, WorldMapResult first, WorldMapResult second)
    {
        Require(database.Definitions.Count == 4, "A prop database must refuse a second definition of the same prefab.");

        List<PropPlacement> props = first.Props;
        Require(props.Count > 0, "The prop stage must place props.");
        Require(props.Count == second.Props.Count, "Repeated generation must place the same number of props.");

        for (int i = 0; i < props.Count; i++)
        {
            PropPlacement a = props[i];
            PropPlacement b = second.Props[i];
            Require(a.Prefab.name == b.Prefab.name && a.Position.x == b.Position.x && a.Position.y == b.Position.y && a.Position.z == b.Position.z && a.Yaw == b.Yaw && a.Context == b.Context,
                "Prop placements must be deterministic.");
        }

        Dictionary<string, PropDefinition> byName = database.Definitions.ToDictionary(definition => definition.Prefab.name);
        var roads = new RoadProximity(first.Roads.Roads, config.WorldSize, config.RoadCellSize);
        roads.AddRange(first.Roads.Streets);
        var pads = new PoiPadIndex(first.Placements, config.WorldSize, 64f, config.PoiPadMargin, database.WidestRadius);

        int wet = 0;
        int onPads = 0;
        int overlaps = 0;
        int offRoad = 0;
        int onVerge = 0;
        int border = 0;

        for (int i = 0; i < props.Count; i++)
        {
            PropPlacement prop = props[i];
            PropDefinition definition = byName[prop.Prefab.name];
            Vector2 point = new(prop.Position.x, prop.Position.z);

            if (point.x < definition.Radius || point.y < definition.Radius || point.x > config.WorldSize - definition.Radius || point.y > config.WorldSize - definition.Radius)
                border++;
            if (first.Water.IsWet(point.x, point.y, prop.Position.y, 0f) || WaterCrossings.OverSpan(first.Water, point))
                wet++;
            if (pads.Covers(point, definition.Radius))
                onPads++;

            bool roadside = prop.Context == PropContext.Road || prop.Context == PropContext.Street;
            if (roadside && definition.OnRoad && !roads.IsWithin(point, 0.01f))
                offRoad++;
            if (!definition.OnRoad && roads.IsWithin(point, definition.Radius))
                onVerge++;

            for (int j = i + 1; j < props.Count; j++)
            {
                PropPlacement other = props[j];
                float limit = definition.Radius + byName[other.Prefab.name].Radius;
                if ((new Vector2(other.Position.x, other.Position.z) - point).sqrMagnitude < limit * limit - 1e-4f)
                    overlaps++;
            }
        }

        Require(border == 0, $"{border} props stand past the world border.");
        Require(wet == 0, $"{wet} props stand in water or on a bridge.");
        Require(onPads == 0, $"{onPads} props stand on a building pad.");
        Require(overlaps == 0, $"{overlaps} prop pairs overlap.");
        Require(offRoad == 0, $"{offRoad} cars stand off the carriageway.");
        Require(onVerge == 0, $"{onVerge} roadside props stand on a carriageway.");

        int road = props.Count(prop => prop.Context == PropContext.Road || prop.Context == PropContext.Street);
        int yard = props.Count(prop => prop.Context == PropContext.Yard);
        int wild = props.Count(prop => prop.Context == PropContext.Wild);
        Require(road > 0 && yard > 0 && wild > 0, $"Every context must place props: road and street {road}, yard {yard}, wild {wild}.");

        Console.WriteLine($"PASS: props — {props.Count} placed (road and street {road}, yard {yard}, wild {wild}), deterministic, dry, off pads, no overlaps, cars on the carriageway.");
    }

    public static void Report(WorldGenerationConfig config, BiomeDatabase biomes, BiomeMap biomeMap, HeightMap heights, WaterMap water,
        RoadNetwork network, List<PoiPlacement> placements)
    {
        var database = new PropDatabase();
        database.EditorAdd(Definition("car", PropContext.Road | PropContext.Street, 18f, 2.6f, true, true, 14f));
        database.EditorAdd(Definition("cone", PropContext.Road | PropContext.Street, 1.2f, 0.3f, true, false, 20f, 2, 5));
        database.EditorAdd(Definition("verge", PropContext.Road | PropContext.Street, 20f, 0.6f, false, false, 25f, 1, 3));
        database.EditorAdd(Definition("street", PropContext.Street | PropContext.Yard, 14f, 0.7f, false, false, 20f, 1, 2));
        database.EditorAdd(Definition("yard", PropContext.Yard, 16f, 0.9f, false, false, 20f, 1, 3));
        database.EditorAdd(Definition("camp", PropContext.Wild, 3f, 2f, false, false, 10f, 1, 2));
        database.EditorAdd(Definition("wild", PropContext.Wild, 4f, 0.7f, false, false, 30f, 1, 4));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<PropPlacement> props = new PropPlacer(config, database, biomes, biomeMap, heights, water, network, placements).Place();
        clock.Stop();

        int Count(PropContext context) => props.Count(prop => prop.Context == context);
        Console.WriteLine($"реквизит: {props.Count} объектов за {clock.Elapsed.TotalSeconds:F1} с — трассы {Count(PropContext.Road)}, улицы {Count(PropContext.Street)}, "
            + $"дворы {Count(PropContext.Yard)}, дикая местность {Count(PropContext.Wild)}; машин {props.Count(prop => prop.Prefab.name == "car")}");
    }

    private static PropDefinition Definition(string name, PropContext contexts, float weight, float radius, bool onRoad, bool alongRoad,
        float maxSlope, int clusterMin = 1, int clusterMax = 1)
    {
        var definition = new PropDefinition(Prefab(name), contexts, weight, onRoad, alongRoad, 20f, maxSlope, clusterMin, clusterMax, 2f, null);
        definition.SetMeasurements(radius, 0f);
        return definition;
    }

    private static readonly Dictionary<string, GameObject> PREFABS = new();

    private static GameObject Prefab(string name)
    {
        if (!PREFABS.TryGetValue(name, out GameObject prefab))
            PREFABS[name] = prefab = new GameObject { name = name };
        return prefab;
    }

    private static void Set(object target, string property, object value)
    {
        target.GetType().GetProperty(property).SetValue(target, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
