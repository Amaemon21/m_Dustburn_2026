using UnityEngine;

public readonly struct UnitSpawnRequest
{
    public UnitType UnitType { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public int MaxUnits { get; }
    public float SpawnDelay { get; }

    public UnitSpawnRequest(UnitType unitType, Vector3 position, Quaternion rotation, int maxUnits, float spawnDelay)
    {
        UnitType = unitType;
        Position = position;
        Rotation = rotation;
        MaxUnits = maxUnits;
        SpawnDelay = spawnDelay;
    }
}
