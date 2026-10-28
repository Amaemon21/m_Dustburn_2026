using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class WorldPlacementFile
{
    [Serializable]
    public sealed class PoiRecord
    {
        public string Prefab;
        public Vector3 Position;
        public float Rotation;
        public Vector2 Footprint;
        public DistrictType District;
        public Vector2 PivotOffset;
    }

    [Serializable]
    public sealed class PropRecord
    {
        public string Prefab;
        public Vector3 Position;
        public float Yaw;
        public float GroundOffset;
        public bool TiltToGround;
        public PropContext Context;
    }

    public List<PoiRecord> Pois = new();
    public List<PropRecord> Props = new();

    public static string Write(IReadOnlyList<PoiPlacement> pois, IReadOnlyList<PropPlacement> props, WorldPrefabIndex prefabs)
    {
        var file = new WorldPlacementFile();

        foreach (PoiPlacement poi in pois)
        {
            file.Pois.Add(new PoiRecord
            {
                Prefab = prefabs.PoiKey(poi.Prefab),
                Position = poi.Position,
                Rotation = poi.Rotation,
                Footprint = poi.Footprint,
                District = poi.District,
                PivotOffset = poi.PivotOffset
            });
        }

        foreach (PropPlacement prop in props)
        {
            file.Props.Add(new PropRecord
            {
                Prefab = prefabs.PropKey(prop.Prefab),
                Position = prop.Position,
                Yaw = prop.Yaw,
                GroundOffset = prop.GroundOffset,
                TiltToGround = prop.TiltToGround,
                Context = prop.Context
            });
        }

        return JsonUtility.ToJson(file);
    }

    public static PoiPlacementAsset Read(string json, WorldPrefabIndex prefabs)
    {
        WorldPlacementFile file = JsonUtility.FromJson<WorldPlacementFile>(json);
        var missing = new SortedSet<string>();
        var pois = new List<PoiPlacement>(file.Pois.Count);
        var props = new List<PropPlacement>(file.Props.Count);

        foreach (PoiRecord record in file.Pois)
        {
            if (!prefabs.TryPoi(record.Prefab, out GameObject prefab))
                missing.Add(record.Prefab);

            var placement = new PoiPlacement(prefab, new Vector2(record.Position.x, record.Position.z), record.Rotation,
                record.Footprint, record.District, record.PivotOffset);
            placement.SetHeight(record.Position.y);
            pois.Add(placement);
        }

        foreach (PropRecord record in file.Props)
        {
            if (!prefabs.TryProp(record.Prefab, out GameObject prefab))
                missing.Add(record.Prefab);

            props.Add(new PropPlacement(prefab, record.Context, record.Position, record.Yaw, record.GroundOffset, record.TiltToGround));
        }

        if (missing.Count > 0)
            throw new InvalidOperationException($"The world places prefabs this build does not ship: {string.Join(", ", missing)}");

        return PoiPlacementAsset.Create(pois, props);
    }
}
