using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using static GroundAppearanceChecks;

static class SplatChecks
{
    private const int WORLD = 1024;
    private const int MAP = 128;
    private const int RESOLUTION = 256;

    public static void Run()
    {
        CheckWeightsAndTiles();
        CheckRoadsAndCliffs();
        CheckTerrainResponse();
        CheckSamplerHasNoGrid();
        CheckBorderContinuity();
        CheckHydraulicUpsample();

        Console.WriteLine("Splat checks passed: bounded and normalized weights, deterministic bake, tiles without seams in any order and size, independent isotropic patches, road and cliff priority, patches that follow relief, slope and macro, base fills the remainder, no 8 m grid in biome weights or in the hydraulic cut, continuous biome border.");
    }

    private sealed class Fixture
    {
        public WorldGenerationConfig Config;
        public BiomeDatabase Biomes;
        public BiomeWeightField Field;
        public List<Road> Roads;
        public TerrainLayer Base;
        public TerrainLayer PatchA;
        public TerrainLayer PatchB;
        public TerrainLayer Shelf;
        public TerrainLayer Other;
        public float[] Slopes;
        public float[] Heights;
        public float[] Reliefs;
    }

    private static Fixture Build()
    {
        var fixture = new Fixture
        {
            Config = new WorldGenerationConfig(),
            Base = new TerrainLayer { name = "base" },
            PatchA = new TerrainLayer { name = "patch a" },
            PatchB = new TerrainLayer { name = "patch b" },
            Shelf = new TerrainLayer { name = "shelf" },
            Other = new TerrainLayer { name = "other" }
        };

        WorldGenerationConfig config = fixture.Config;
        Set(config, "WorldSize", WORLD);
        Set(config, "Seed", -762286179);
        Set(config, "BiomeBlendRadius", 28f);
        Set(config, "BiomeBorderWidth", 10f);
        Set(config, "BiomeBorderWarp", 24f);
        Set(config, "BiomeBorderWarpPeriod", 120f);
        Set(config, "RoadLayer", new TerrainLayer { name = "road" });
        Set(config, "RoadEdgeLayer", new TerrainLayer { name = "verge" });
        Set(config, "CliffLayer", new TerrainLayer { name = "cliff" });

        var forest = new BiomeDefinition();
        forest.Ground.Add(Ground(fixture.Base, 1f, 0f));
        forest.Ground.Add(Patch(fixture.PatchA, 120f, 0.5f));
        forest.Ground.Add(Patch(fixture.PatchB, 120f, 0.5f));
        forest.Ground.Add(Ground(fixture.Shelf, 1f, 26f));

        var desert = new BiomeDefinition();
        Set(desert, "Type", BiomeType.Desert);
        desert.Ground.Add(Ground(fixture.Other, 1f, 0f));

        fixture.Biomes = Database(forest, desert);

        var map = new BiomeMap(MAP, WORLD);

        for (int y = 0; y < MAP; y++)
            for (int x = 0; x < MAP; x++)
                if (x * 0.6f + y > MAP * 0.9f)
                    map.Cells[y * MAP + x] = 1;

        fixture.Field = new BiomeWeightField(map, 2, 28f);
        fixture.Roads = new List<Road> { new Road(new[] { new Vector2(100f, 150f), new Vector2(900f, 700f) }, 40f, RoadKind.Highway) };

        fixture.Slopes = new float[RESOLUTION * RESOLUTION];
        fixture.Heights = new float[RESOLUTION * RESOLUTION];
        fixture.Reliefs = new float[RESOLUTION * RESOLUTION];

        for (int y = 0; y < RESOLUTION; y++)
        {
            for (int x = 0; x < RESOLUTION; x++)
            {
                fixture.Slopes[y * RESOLUTION + x] = 20f + 18f * Mathf.Sin(x * 0.07f) * Mathf.Cos(y * 0.05f);
                fixture.Heights[y * RESOLUTION + x] = 0.4f;
                fixture.Reliefs[y * RESOLUTION + x] = Mathf.Sin(x * 0.11f + y * 0.03f);
            }
        }

        return fixture;
    }

    private static GroundLayer Patch(TerrainLayer layer, float frequency, float threshold)
    {
        GroundLayer ground = Ground(layer, 1f, 0f);

        Set(ground, "PatchFrequency", frequency);
        Set(ground, "PatchThreshold", threshold);
        Set(ground, "PatchFade", 0.08f);

        return ground;
    }

    private static void CheckWeightsAndTiles()
    {
        Fixture fixture = Build();

        using var painter = new GroundSplatPainter(fixture.Config, fixture.Biomes, fixture.Field, fixture.Roads);
        using var twin = new GroundSplatPainter(fixture.Config, fixture.Biomes, fixture.Field, fixture.Roads);

        using NativeArray<float> world = painter.BakeWorld(RESOLUTION, fixture.Slopes, fixture.Heights, fixture.Reliefs);
        using NativeArray<float> again = twin.BakeWorld(RESOLUTION, fixture.Slopes, fixture.Heights, fixture.Reliefs);

        int count = painter.Layers.Length;

        for (int pixel = 0; pixel < RESOLUTION * RESOLUTION; pixel++)
        {
            float sum = 0f;

            for (int layer = 0; layer < count; layer++)
            {
                float weight = world[pixel * count + layer];

                Require(!float.IsNaN(weight) && !float.IsInfinity(weight), $"Weight of layer {layer} at pixel {pixel} is not finite.");
                Require(weight >= 0f && weight <= 1f, $"Weight of layer {layer} at pixel {pixel} is {weight}, outside [0, 1].");
                Require(weight == again[pixel * count + layer], "The same seed must bake the same splatmap.");

                sum += weight;
            }

            Require(Math.Abs(sum - 1f) < 1e-5f, $"Weights at pixel {pixel} add up to {sum}.");
        }

        foreach (int tile in new[] { 32, 64, 128 })
        {
            float worst = 0f;

            for (int tileY = RESOLUTION - tile; tileY >= 0; tileY -= tile)
            {
                for (int tileX = RESOLUTION - tile; tileX >= 0; tileX -= tile)
                {
                    using NativeArray<float> part = painter.BakeTile(RESOLUTION, tile, tileX, tileY,
                        Cut(fixture.Slopes, tileX, tileY, tile), Cut(fixture.Heights, tileX, tileY, tile), Cut(fixture.Reliefs, tileX, tileY, tile));

                    for (int y = 0; y < tile; y++)
                        for (int x = 0; x < tile; x++)
                            for (int layer = 0; layer < count; layer++)
                            {
                                float tiled = part[(y * tile + x) * count + layer];
                                float whole = world[((tileY + y) * RESOLUTION + tileX + x) * count + layer];

                                worst = Math.Max(worst, Math.Abs(tiled - whole));
                            }
                }
            }

            Require(worst < 1e-6f, $"A bake in {tile}-texel tiles differs from the whole-world bake by {worst}: tiles have seams.");
        }

        CheckPatches(fixture, painter, world);
    }

    private static void CheckPatches(Fixture fixture, GroundSplatPainter painter, NativeArray<float> world)
    {
        int count = painter.Layers.Length;
        int baseIndex = Array.IndexOf(painter.Layers, fixture.Base);
        int a = Array.IndexOf(painter.Layers, fixture.PatchA);
        int b = Array.IndexOf(painter.Layers, fixture.PatchB);
        int shelf = Array.IndexOf(painter.Layers, fixture.Shelf);

        int pure = 0;
        double shareA = 0;
        double shareB = 0;
        double alongX = 0;
        double alongZ = 0;

        for (int y = 0; y < RESOLUTION - 1; y++)
        {
            for (int x = 0; x < RESOLUTION - 1; x++)
            {
                int pixel = y * RESOLUTION + x;
                int origin = pixel * count;

                float forest = world[origin + baseIndex] + world[origin + a] + world[origin + b] + world[origin + shelf];

                if (forest < 0.9999f || fixture.Slopes[pixel] > 20f)
                    continue;

                if (world[origin + count + baseIndex] + world[origin + count + a] + world[origin + count + b] < 0.9999f)
                    continue;

                pure++;
                shareA += world[origin + a];
                shareB += world[origin + b];

                float coverB = world[origin + b];
                alongX += Math.Abs(world[origin + count + b] - coverB);
                alongZ += Math.Abs(world[origin + RESOLUTION * count + b] - coverB);

                float rest = 1f - world[origin + a] - world[origin + b] - world[origin + shelf];
                Require(Math.Abs(world[origin + baseIndex] - rest) < 1e-5f, "The base ground must take exactly what the rules above it leave.");
            }
        }

        Require(pure > 10000, $"The fixture must hold plenty of pure forest, found {pure} texels.");

        shareA /= pure;
        shareB /= pure;

        Require(shareB > 0.3f && shareB < 0.7f, $"A patch at threshold 0.5 should cover about half its biome, got {shareB:0.00}.");
        Require(shareA > 0.12f, $"Two patches of the same frequency must not share one noise field: the lower one shows through on only {shareA:0.00} of the biome.");

        double ratio = alongX / Math.Max(1e-9, alongZ);
        Require(ratio > 0.7 && ratio < 1.4, $"Patch edges must have no preferred direction, the X/Z edge ratio is {ratio:0.00}.");
    }

    private static void CheckRoadsAndCliffs()
    {
        Fixture fixture = Build();

        var cliff = new TerrainLayer { name = "forest cliff" };
        Set(fixture.Biomes.Get(0), "CliffLayer", cliff);

        using var painter = new GroundSplatPainter(fixture.Config, fixture.Biomes, fixture.Field, fixture.Roads);

        var steep = new float[RESOLUTION * RESOLUTION];

        for (int i = 0; i < steep.Length; i++)
            steep[i] = i % RESOLUTION < RESOLUTION / 2 ? 70f : 4f;

        using NativeArray<float> world = painter.BakeWorld(RESOLUTION, steep, fixture.Heights, fixture.Reliefs);

        int count = painter.Layers.Length;
        int road = painter.RoadLayerIndex;
        int verge = painter.RoadEdgeLayerIndex;
        int cliffIndex = Array.IndexOf(painter.Layers, cliff);
        int sharedCliff = Array.IndexOf(painter.Layers, fixture.Config.CliffLayer);
        float texel = (float)WORLD / RESOLUTION;

        float axis = 0f;
        float vergePeak = 0f;
        float vergeOnAxis = 0f;
        int cliffTexels = 0;
        float cliffLow = 1f;

        for (int y = 0; y < RESOLUTION; y++)
        {
            for (int x = 0; x < RESOLUTION; x++)
            {
                var point = new Vector2((x + 0.5f) * texel, (y + 0.5f) * texel);
                float distance = DistanceToRoad(point, fixture.Roads[0]);
                int origin = (y * RESOLUTION + x) * count;

                if (distance < 1f)
                {
                    axis = Math.Max(axis, world[origin + road]);
                    vergeOnAxis = Math.Max(vergeOnAxis, world[origin + verge]);
                }

                vergePeak = Math.Max(vergePeak, world[origin + verge]);

                if (distance < 40f)
                    continue;

                if (x < RESOLUTION / 2 - 2)
                {
                    cliffTexels++;
                    cliffLow = Math.Min(cliffLow, world[origin + cliffIndex] + world[origin + sharedCliff]);
                }

                if (x > RESOLUTION / 2 + 2)
                    Require(world[origin + cliffIndex] + world[origin + sharedCliff] < 1e-4f, "Flat ground must not show a cliff layer.");
            }
        }

        Require(axis > 0.99f, $"The road axis must be pure road, got {axis:0.000}.");
        Require(vergePeak > 0.25f, $"The verge must show beside the carriageway, peaked at {vergePeak:0.000}.");
        Require(vergeOnAxis < 1e-4f, $"The carriageway must hide the verge, {vergeOnAxis:0.000} on the axis.");
        Require(cliffTexels > 1000 && cliffLow > 0.999f, $"A 70 degree slope must be all cliff ground, {cliffTexels} texels with a low of {cliffLow:0.000}.");
    }

    private static void CheckTerrainResponse()
    {
        Fixture fixture = Build();

        var ridge = new TerrainLayer { name = "ridge" };
        var hollow = new TerrainLayer { name = "hollow" };
        var steep = new TerrainLayer { name = "steep" };
        var wet = new TerrainLayer { name = "wet" };

        var biome = new BiomeDefinition();
        biome.Ground.Add(Ground(fixture.Base, 1f, 0f));
        biome.Ground.Add(Responsive(wet, 90f, "MacroBias", 1f));
        biome.Ground.Add(Responsive(steep, 70f, "SlopeBias", 1f));
        biome.Ground.Add(Responsive(hollow, 60f, "ReliefBias", -1f));
        biome.Ground.Add(Responsive(ridge, 50f, "ReliefBias", 1f));

        var map = new BiomeMap(MAP, WORLD);
        var field = new BiomeWeightField(map, 1, 28f);

        using var painter = new GroundSplatPainter(fixture.Config, Database(biome), field, null);
        using NativeArray<float> world = painter.BakeWorld(RESOLUTION, fixture.Slopes, fixture.Heights, fixture.Reliefs);

        int count = painter.Layers.Length;

        float Mean(TerrainLayer layer, Func<int, bool> where)
        {
            int index = Array.IndexOf(painter.Layers, layer);
            double sum = 0;
            int hits = 0;

            for (int pixel = 0; pixel < RESOLUTION * RESOLUTION; pixel++)
            {
                if (!where(pixel))
                    continue;

                sum += world[pixel * count + index];
                hits++;
            }

            return hits == 0 ? 0f : (float)(sum / hits);
        }

        float onRidge = Mean(ridge, p => fixture.Reliefs[p] > 0.6f);
        float ridgeInHollow = Mean(ridge, p => fixture.Reliefs[p] < -0.6f);
        float inHollow = Mean(hollow, p => fixture.Reliefs[p] < -0.6f && fixture.Reliefs[p] < 0f);
        float hollowOnRidge = Mean(hollow, p => fixture.Reliefs[p] > 0.6f);
        float onSteep = Mean(steep, p => fixture.Slopes[p] > 32f);
        float onFlat = Mean(steep, p => fixture.Slopes[p] < 8f);

        Require(onRidge > 3f * ridgeInHollow, $"A layer with positive ReliefBias must gather on ridges: {onRidge:0.00} on ridges against {ridgeInHollow:0.00} in hollows.");
        Require(inHollow > 3f * hollowOnRidge, $"A layer with negative ReliefBias must gather in hollows: {inHollow:0.00} against {hollowOnRidge:0.00}.");
        Require(onSteep > 3f * onFlat, $"A layer with positive SlopeBias must gather on slopes: {onSteep:0.00} against {onFlat:0.00}.");

        int wetIndex = Array.IndexOf(painter.Layers, wet);
        GroundNoise noise = GroundNoise.From(fixture.Config);
        float texel = (float)WORLD / RESOLUTION;
        double high = 0;
        double low = 0;
        int highCount = 0;
        int lowCount = 0;

        for (int y = 0; y < RESOLUTION; y++)
        {
            for (int x = 0; x < RESOLUTION; x++)
            {
                GroundSample sample = noise.Sample((x + 0.5f) * texel, (y + 0.5f) * texel, 0f, 0f);
                float weight = world[(y * RESOLUTION + x) * count + wetIndex];

                if (sample.Macro > 0.4f)
                {
                    high += weight;
                    highCount++;
                }

                if (sample.Macro < -0.4f)
                {
                    low += weight;
                    lowCount++;
                }
            }
        }

        Require(highCount > 0 && lowCount > 0, "The fixture must hold both ends of the macro field.");
        Require(high / highCount > 2 * low / lowCount, $"A layer with positive MacroBias must gather where the macro field is high: {high / highCount:0.00} against {low / lowCount:0.00}.");
    }

    private static GroundLayer Responsive(TerrainLayer layer, float frequency, string bias, float sign)
    {
        GroundLayer ground = Patch(layer, frequency, 0.6f);
        Set(ground, bias, 0.35f * sign);

        return ground;
    }

    private static float DistanceToRoad(Vector2 point, Road road)
    {
        float best = float.MaxValue;

        for (int i = 1; i < road.Points.Length; i++)
        {
            Vector2 a = road.Points[i - 1];
            Vector2 b = road.Points[i];
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);

            best = Math.Min(best, (a + ab * t - point).magnitude);
        }

        return best;
    }

    private static void CheckSamplerHasNoGrid()
    {
        const int SIZE = 32;

        var ramp = new float[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
            for (int x = 0; x < SIZE; x++)
                ramp[y * SIZE + x] = x / (float)(SIZE - 1);

        const float STEP = 1f / 64f;
        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (float cell = 8f; cell < 24f; cell += STEP)
        {
            float here = BiomeWeightSampler.At(cell / SIZE, 0.5f, SIZE).Sample(ramp);
            float next = BiomeWeightSampler.At((cell + STEP) / SIZE, 0.5f, SIZE).Sample(ramp);
            float slope = (next - here) / STEP;

            lowest = Math.Min(lowest, slope);
            highest = Math.Max(highest, slope);
        }

        Require(lowest > 0.9f * highest, $"Sampling a linear ramp must keep a steady gradient, it swings {lowest:0.000}..{highest:0.000}: the biome weights step on the map grid.");
    }

    private static void CheckBorderContinuity()
    {
        Fixture fixture = Build();
        var filter = new DecorFilter(fixture.Config, fixture.Field, 2, null, 0, null, 0f);

        float worstJump = 0f;
        int crossings = 0;

        for (float z = 100f; z < 900f; z += 37f)
        {
            float previous = filter.SurfaceWeight(new Vector2(0f, z), 0);

            for (float x = 0.25f; x < WORLD; x += 0.25f)
            {
                float current = filter.SurfaceWeight(new Vector2(x, z), 0);

                worstJump = Math.Max(worstJump, Math.Abs(current - previous));

                if ((previous - 0.5f) * (current - 0.5f) < 0f)
                    crossings++;

                previous = current;
            }
        }

        Require(crossings > 0, "The fixture border was never crossed.");
        Require(worstJump < 0.12f, $"The biome border must stay continuous, it jumps by {worstJump:0.000} in a quarter of a metre.");
    }

    private static void CheckHydraulicUpsample()
    {
        const int COARSE = 9;
        const int STEP = 8;
        const int FINE = (COARSE - 1) * STEP + 1;

        var random = new System.Random(7);
        var cut = new float[COARSE * COARSE];

        for (int i = 0; i < cut.Length; i++)
            cut[i] = 0.2f + 0.8f * (float)random.NextDouble();

        var heights = new float[FINE * FINE];

        for (int i = 0; i < heights.Length; i++)
            heights[i] = 1f;

        using var cutArray = new NativeArray<float>(cut, Allocator.TempJob);
        using var heightArray = new NativeArray<float>(heights, Allocator.TempJob);

        var job = new HydraulicApplyJob
        {
            Cut = cutArray,
            Heights = heightArray,
            Resolution = FINE,
            CoarseResolution = COARSE,
            Step = STEP
        };

        for (int row = 0; row < FINE; row++)
            job.Execute(row);

        float atNodes = 0f;
        float inCells = 0f;

        for (int row = STEP; row < FINE - STEP; row++)
        {
            for (int x = STEP; x < FINE - STEP; x++)
            {
                int index = row * FINE + x;
                float bend = Math.Abs(heightArray[index - 1] - 2f * heightArray[index] + heightArray[index + 1]);

                if (x % STEP == 0)
                    atNodes = Math.Max(atNodes, bend);
                else
                    inCells = Math.Max(inCells, bend);
            }
        }

        Require(atNodes < 2f * inCells, $"The hydraulic cut must be upsampled without a crease on every coarse cell line: bend {atNodes:0.0000} at the nodes against {inCells:0.0000} inside the cells.");
    }

    private static float[] Cut(float[] source, int originX, int originY, int tile)
    {
        var part = new float[tile * tile];

        for (int y = 0; y < tile; y++)
            Array.Copy(source, (originY + y) * RESOLUTION + originX, part, y * tile, tile);

        return part;
    }
}
