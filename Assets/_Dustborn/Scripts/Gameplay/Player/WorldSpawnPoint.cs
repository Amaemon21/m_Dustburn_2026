using System;
using UnityEngine;

public static class WorldSpawnPoint
{
    public static Vector3 Find(WorldData world)
    {
        if (world.Gameplay != null)
        {
            world.Gameplay.Validate(world.Config.WorldSize, world.Placement.Placements.Count);
            return world.Gameplay.Spawn;
        }

        if (world.BiomeMap == null || world.Biomes == null)
            throw new InvalidOperationException("A world needs a readable biome map to choose a safe start.");

        WorldGenerationConfig config = world.Config;
        BiomeMap biomes = BiomeMapTexture.Read(world.BiomeMap, world.Biomes, config.WorldSize);
        if (biomes == null)
            throw new InvalidOperationException("The world biome map could not be read.");

        byte[] raw = world.Heights;
        int resolution = config.HeightMapResolution;
        if (raw.LongLength != (long)resolution * resolution * sizeof(ushort))
            throw new InvalidOperationException("The world height data does not match its resolution.");

        float cell = config.WorldSize / (float)(resolution - 1);
        float Sample(float x, float z)
        {
            float u = Mathf.Clamp(x / cell, 0f, resolution - 1.001f);
            float v = Mathf.Clamp(z / cell, 0f, resolution - 1.001f);
            int left = (int)u, bottom = (int)v;
            float Read(int column, int row)
            {
                int index = (row * resolution + column) * 2;
                return (raw[index] | raw[index + 1] << 8) * (config.MaxHeight / ushort.MaxValue);
            }

            return Mathf.Lerp(Mathf.Lerp(Read(left, bottom), Read(left + 1, bottom), u - left),
                Mathf.Lerp(Read(left, bottom + 1), Read(left + 1, bottom + 1), u - left), v - bottom);
        }

        var roads = world.Roads == null ? null : new RoadProximity(world.Roads.Paved(), config.WorldSize, 64f);
        return new GameplayLandmarkPlanner(config, world.Biomes, biomes, Sample, world.Water,
            world.Placement?.Placements, roads).Plan(false).Spawn;
    }
}
