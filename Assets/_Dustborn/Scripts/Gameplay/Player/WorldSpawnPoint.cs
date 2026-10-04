using Unity.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public static class WorldSpawnPoint
{
    private const int SPAWN_ATTEMPTS = 64;
    private const float EDGE_MARGIN = 256f;
    private const float SHORE_MARGIN = 4f;
    private const float GROUND_CLEARANCE = 3f;

    public static Vector3 Find(BakedWorld world)
    {
        WorldGenerationConfig config = world.Config;

        int resolution = config.HeightMapResolution;
        float cellSize = config.WorldSize / (float)(resolution - 1);
        float dryGround = config.SeaLevel + SHORE_MARGIN;

        float low = Mathf.Min(EDGE_MARGIN, config.WorldSize * 0.25f);
        float high = config.WorldSize - low;

        NativeArray<byte> raw = world.HeightMap.GetData<byte>();
        WaterMap water = world.LoadWater();

        Vector3 highest = Vector3.zero;
        float highestGround = float.NegativeInfinity;

        for (int attempt = 0; attempt < SPAWN_ATTEMPTS; attempt++)
        {
            float x = Random.Range(low, high);
            float z = Random.Range(low, high);
            float ground = Sample(raw, resolution, cellSize, config.MaxHeight, x, z);
            var candidate = new Vector3(x, ground + GROUND_CLEARANCE, z);

            if (ground >= dryGround && (water == null || !water.IsWet(x, z, ground, SHORE_MARGIN)))
                return candidate;

            if (ground <= highestGround)
                continue;

            highestGround = ground;
            highest = candidate;
        }

        Debug.LogWarning($"{nameof(WorldSpawnPoint)}: no dry ground in {SPAWN_ATTEMPTS} tries, spawning at the highest one, {highestGround:0.0} m against a sea level of {config.SeaLevel:0.0} m");

        return highest;
    }

    private static float Sample(NativeArray<byte> raw, int resolution, float cellSize, float maxHeight, float worldX, float worldZ)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(worldX / cellSize), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(worldZ / cellSize), 0, resolution - 1);
        int index = (y * resolution + x) * 2;

        if (index < 0 || index + 1 >= raw.Length)
            return 0f;

        int value = raw[index] | (raw[index + 1] << 8);

        return value / (float)ushort.MaxValue * maxHeight;
    }
}
