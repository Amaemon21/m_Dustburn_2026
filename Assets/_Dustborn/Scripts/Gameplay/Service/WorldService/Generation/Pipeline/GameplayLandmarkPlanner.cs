using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public sealed class GameplayLandmarkPlanner
{
    private const int DIRECTIONS = 16;
    private const float CLEARANCE = 12f;
    private const float GROUND_CLEARANCE = 3f;
    private static readonly Vector2[] Directions = BuildDirections();
    private readonly WorldGenerationConfig _config;
    private readonly WorldGameplaySettings _settings;
    private readonly BiomeDatabase _biomes;
    private readonly BiomeMap _map;
    private readonly Func<float, float, float> _ground;
    private readonly IReadOnlyList<PoiPlacement> _pois;
    private readonly RoadProximity _roads;
    private readonly PoiSpatialIndex _index;
    private readonly List<int> _nearby = new();
    private readonly List<int> _starters = new();
    private readonly List<int> _restricted = new();
    private readonly WorldWalkability _walkability;
    private readonly bool _forest;

    public GameplayLandmarkPlanner(WorldGenerationConfig config, BiomeDatabase biomes, BiomeMap map,
        Func<float, float, float> ground, WaterMap water, IReadOnlyList<PoiPlacement> pois, RoadProximity roads)
    {
        _config = config;
        _settings = config.Gameplay ?? new WorldGameplaySettings();
        _biomes = biomes;
        _map = map;
        _ground = ground;
        _pois = pois ?? Array.Empty<PoiPlacement>();
        _roads = roads;
        _index = new PoiSpatialIndex(config.WorldSize, _pois);
        _walkability = new WorldWalkability(config, ground, water, _index);

        foreach (byte cell in map.Cells)
        {
            if (biomes.Get(cell).Type == BiomeType.PineForest)
            {
                _forest = true;
                break;
            }
        }

        for (int i = 0; i < _pois.Count; i++)
        {
            if (_pois[i].Gameplay.MinDistanceFromSpawn > 0f)
                _restricted.Add(i);
        }
    }

    public WorldGameplayPlan Plan(bool requireStarters = true, CancellationToken cancellationToken = default, Vector2? preferred = null)
        => Create(FindStart(requireStarters, cancellationToken, preferred));

    private Vector2 FindStart(bool requireStarters, CancellationToken cancellationToken, Vector2? preferred = null)
    {
        ValidateSettings();
        int required = requireStarters && HasBuildings() ? _settings.StarterPoiCount : 0;
        cancellationToken.ThrowIfCancellationRequested();
        if (preferred.HasValue && Accepts(preferred.Value, required))
            return preferred.Value;

        var anchors = new List<int>();

        for (int i = 0; i < _pois.Count; i++)
        {
            if (IsStarterTarget(_pois[i].Gameplay))
                anchors.Add(i);
        }

        anchors.Sort((left, right) =>
        {
            int compare = Hash(left).CompareTo(Hash(right));
            return compare != 0 ? compare : left.CompareTo(right);
        });

        for (int anchor = 0; anchor < anchors.Count; anchor++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector2 center = _pois[anchors[anchor]].Ground;

            for (int ring = 0; ring < 3; ring++)
            {
                float radius = Mathf.Lerp(_settings.StarterMinDistance, _settings.StarterMaxDistance * 0.8f, ring * 0.5f);

                for (int direction = 0; direction < DIRECTIONS; direction++)
                {
                    int heading = (direction + (int)(Hash(anchors[anchor]) % DIRECTIONS)) % DIRECTIONS;
                    Vector2 point = center + Directions[heading] * radius;
                    if (Accepts(point, required))
                        return point;
                }
            }
        }

        if (required > 0)
            throw new InvalidOperationException($"Seed {_config.Seed}: no dry walkable start with {required} accessible buildings within {_settings.StarterMaxDistance:0} m. Try another seed or increase forest coverage and settlement density.");

        int resolution = Math.Max(1, _config.WorldSize / 32);
        int count = resolution * resolution;
        int offset = (int)(Hash(_config.Seed) % (uint)count);
        float step = _config.WorldSize / (float)resolution;

        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cell = (i + offset) % count;
            Vector2 point = new((cell % resolution + 0.5f) * step, (cell / resolution + 0.5f) * step);
            if (Accepts(point, 0))
                return point;
        }

        throw new InvalidOperationException($"Seed {_config.Seed}: no dry walkable start exists. Change the seed or terrain settings.");
    }

    private bool Accepts(Vector2 point, int required)
    {
        _starters.Clear();
        float margin = Mathf.Min(_settings.EdgeMargin, _config.WorldSize * 0.2f);
        if (point.x < margin || point.y < margin || point.x > _config.WorldSize - margin || point.y > _config.WorldSize - margin)
            return false;

        if (!_walkability.Dry(point, _ground(point.x, point.y)) || (_forest && Biome(point) != BiomeType.PineForest)
            || _index.Occupied(point, CLEARANCE) || (_roads != null && _roads.IsWithin(point, 3f)))
            return false;

        for (int i = 0; i < DIRECTIONS; i++)
        {
            Vector2 direction = Directions[i];
            Vector2 edge = point + direction * _settings.BiomeMargin;
            if ((_forest && Biome(edge) != BiomeType.PineForest) || !_walkability.CanWalk(point, point + direction * CLEARANCE))
                return false;
        }

        foreach (int index in _restricted)
        {
            float distance = _pois[index].Gameplay.MinDistanceFromSpawn;
            if ((_pois[index].Ground - point).sqrMagnitude < distance * distance)
                return false;
        }

        _index.Query(point, Mathf.Max(_settings.StarterMaxDistance, _settings.SafeRadius), _nearby);
        foreach (int index in _nearby)
        {
            PoiPlacement poi = _pois[index];
            float square = (poi.Ground - point).sqrMagnitude;
            float clearance = _settings.SafeRadius + _index.Radius(index);
            if (!poi.Gameplay.Starter && square < clearance * clearance)
                return false;

            if (IsStarterTarget(poi.Gameplay) && square >= _settings.StarterMinDistance * _settings.StarterMinDistance
                && square <= _settings.StarterMaxDistance * _settings.StarterMaxDistance && Reachable(point, poi, index))
                _starters.Add(index);
        }

        return _starters.Count >= required;
    }

    private bool Reachable(Vector2 point, PoiPlacement poi, int index)
    {
        Vector2 entrance = WorldWalkability.Approach(poi);
        return _walkability.CanWalk(point, entrance) && _walkability.CanWalk(entrance, poi.Ground, index);
    }

    private BiomeType Biome(Vector2 point) => _biomes.Get(_map.SampleWorld(new Vector3(point.x, 0f, point.y))).Type;

    private WorldGameplayPlan Create(Vector2 point)
    {
        _starters.Sort((left, right) =>
        {
            int order = (WorldWalkability.Approach(_pois[left]) - point).sqrMagnitude.CompareTo((WorldWalkability.Approach(_pois[right]) - point).sqrMagnitude);
            return order != 0 ? order : left.CompareTo(right);
        });
        var zones = new WorldThreatZone[_pois.Count];
        for (int i = 0; i < _pois.Count; i++)
        {
            PoiPlacement poi = _pois[i];
            zones[i] = new WorldThreatZone
            {
                PoiIndex = i, Center = poi.Ground, Radius = Mathf.Max(30f, _index.Radius(i) + 20f),
                Tier = poi.Gameplay.Tier, Profile = poi.Gameplay.ThreatProfile
            };
        }

        Vector2 facing = _starters.Count > 0 ? WorldWalkability.Approach(_pois[_starters[0]]) - point : new Vector2(0f, 1f);
        var plan = new WorldGameplayPlan
        {
            Version = WorldGameplayPlan.CURRENT_VERSION,
            Spawn = new Vector3(point.x, _ground(point.x, point.y) + GROUND_CLEARANCE, point.y),
            Facing = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg,
            SpawnBiome = Biome(point), SafeRadius = _settings.SafeRadius,
            StarterPois = _starters.ToArray(), ThreatZones = zones
        };
        plan.Validate(_config.WorldSize, _pois.Count);
        return plan;
    }

    private bool HasBuildings() => (_config.CityProfile?.Count ?? 0) + (_config.TownProfile?.Count ?? 0)
        + (_config.CountryTownProfile?.Count ?? 0) + (_config.GhostTownProfile?.Count ?? 0) > 0;

    public static Vector2 Reserve(WorldGenerationConfig config, BiomeDatabase biomes, BiomeMap map, HeightMap heights, WaterMap water,
        IReadOnlyList<SettlementLayout> layouts, PoiDatabase definitions, RoadProximity roads, CancellationToken token)
    {
        var candidates = new List<PoiPlacement>();
        foreach (SettlementLayout layout in layouts)
        {
            token.ThrowIfCancellationRequested();
            foreach (Lot lot in layout.Lots)
            {
                PoiDefinition smallest = null;
                foreach (DistrictType district in LotSubdivider.Fallback(lot.District))
                {
                    foreach (PoiDefinition definition in definitions.Definitions)
                    {
                        if (definition == null || !IsStarterTarget(definition.Gameplay) || definition.District != district
                            || definition.FootprintWidth > lot.Width - 2f * config.LotMargin
                            || definition.FootprintDepth > lot.Depth - config.LotSetback - config.LotMargin)
                            continue;

                        Vector2 position = PoiPlacer.PositionOnLot(config, lot, definition);
                        if (!definition.Gameplay.Allows(biomes.Get(map.SampleWorld(new Vector3(position.x, 0f, position.y))).Type))
                            continue;

                        if (smallest == null || definition.FootprintArea < smallest.FootprintArea)
                            smallest = definition;
                    }

                    if (smallest != null)
                        break;
                }

                if (smallest == null)
                    continue;

                Vector2 center = PoiPlacer.PositionOnLot(config, lot, smallest);
                float rotation = Mathf.Atan2(lot.Forward.x, lot.Forward.y) * Mathf.Rad2Deg;
                var candidate = new PoiPlacement(smallest.Prefab, center, rotation, smallest.Footprint, smallest.District, smallest.PivotOffset);
                candidate.SetGameplay(smallest.Gameplay, 0);
                candidates.Add(candidate);
            }
        }

        return new GameplayLandmarkPlanner(config, biomes, map, heights.SampleWorldSmooth, water, candidates, roads).FindStart(true, token);
    }

    private void ValidateSettings()
    {
        if (!Finite(_settings.SafeRadius) || !Finite(_settings.StarterMinDistance) || !Finite(_settings.StarterMaxDistance)
            || !Finite(_settings.MaxWalkSlope) || !Finite(_settings.EdgeMargin) || !Finite(_settings.BiomeMargin)
            || _settings.SafeRadius < 0f || _settings.StarterMinDistance < 0f || _settings.StarterMaxDistance <= _settings.StarterMinDistance
            || _settings.StarterMaxDistance > _config.WorldSize || _settings.StarterPoiCount < 0 || _settings.StarterPoiCount > 8
            || _settings.EdgeMargin < 0f || _settings.BiomeMargin < 0f || _settings.MaxWalkSlope <= 0f || _settings.MaxWalkSlope >= 90f)
            throw new InvalidOperationException("Invalid gameplay planning settings.");
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsStarterTarget(PoiGameplay gameplay) => gameplay?.Starter == true && gameplay.LootProfile != PoiLootProfile.None;

    private static Vector2[] BuildDirections()
    {
        var directions = new Vector2[DIRECTIONS];
        for (int i = 0; i < directions.Length; i++)
        {
            float angle = i * (2f * Mathf.PI / DIRECTIONS);
            directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        return directions;
    }

    private uint Hash(int index) => ((uint)index * 2654435761u ^ (uint)_config.Seed * 2246822519u) | 1u;
}
