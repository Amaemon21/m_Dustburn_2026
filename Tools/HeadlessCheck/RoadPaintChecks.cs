using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

static class RoadPaintChecks
{
    private const int WORLD = 512;
    private const int RESOLUTION = 256;

    public static void Run()
    {
        var config = new WorldGenerationConfig();
        Set(config, "WorldSize", WORLD);

        var ground = new TerrainLayer { name = "ground" };
        var carriage = new TerrainLayer { name = "carriage" };
        var verge = new TerrainLayer { name = "verge" };

        Set(config, "RoadLayer", carriage);
        Set(config, "RoadEdgeLayer", verge);

        var biome = new BiomeDefinition();
        var layer = new GroundLayer();
        Set(layer, "Layer", ground);
        Set(layer, "Opacity", 1f);
        biome.Ground.Add(layer);

        var database = new BiomeDatabase();
        typeof(BiomeDatabase).GetField("_biomes", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(database, new List<BiomeDefinition> { biome });

        var field = new BiomeWeightField(new BiomeMap(64, WORLD), 1, 0f);

        float texel = (float)WORLD / RESOLUTION;
        float halfWidth = config.RoadHalfWidth;
        float vergeEdge = halfWidth + config.RoadVergeWidth;

        float carriageDrift = 0f;
        float groundDrift = 0f;
        float carriageBias = 0f;
        float groundBias = 0f;
        float wobble = 0f;

        foreach (float degrees in new[] { 0f, 4f, 11f, 18.5f, 27f, 36f, 45f })
        {
            float radians = degrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            var normal = new Vector2(-direction.y, direction.x);
            var origin = new Vector2(96.37f, 128.21f);
            var road = new Road(new[] { origin, origin + direction * 300f }, halfWidth * 2f);

            using var painter = new GroundSplatPainter(config, database, field, new List<Road> { road });
            using var baked = painter.BakeWorld(RESOLUTION, new float[RESOLUTION * RESOLUTION], new float[RESOLUTION * RESOLUTION]);

            var weights = new float[baked.Length];
            baked.CopyTo(weights);

            int layers = painter.Layers.Length;
            int carriageIndex = Array.IndexOf(painter.Layers, carriage);
            int groundIndex = Array.IndexOf(painter.Layers, ground);

            RequireCoverage(weights, layers);

            var carriageCrossings = new List<float>();
            var groundCrossings = new List<float>();
            var lowest = new float[81];
            var highest = new float[81];

            Array.Fill(lowest, float.MaxValue);
            Array.Fill(highest, float.MinValue);

            for (float along = 60f; along <= 240f; along += 0.25f)
            {
                Vector2 axis = origin + direction * along;

                carriageCrossings.Add(Crossing(weights, layers, carriageIndex, axis, normal, 0f, 12f, true));
                groundCrossings.Add(Crossing(weights, layers, groundIndex, axis, normal, 0f, 12f, false));

                for (int step = 0; step < lowest.Length; step++)
                {
                    float offset = -8f + step * 0.25f;
                    float value = Bilinear(weights, layers, carriageIndex, axis + normal * offset, texel);

                    lowest[step] = Mathf.Min(lowest[step], value);
                    highest[step] = Mathf.Max(highest[step], value);
                }
            }

            carriageDrift = Mathf.Max(carriageDrift, Spread(carriageCrossings));
            groundDrift = Mathf.Max(groundDrift, Spread(groundCrossings));
            carriageBias = Mathf.Max(carriageBias, Mathf.Abs(Mean(carriageCrossings) - halfWidth));
            groundBias = Mathf.Max(groundBias, Mathf.Abs(Mean(groundCrossings) - vergeEdge));

            for (int step = 0; step < lowest.Length; step++)
                wobble = Mathf.Max(wobble, highest[step] - lowest[step]);
        }

        Console.WriteLine($"road paint at {texel:0.#} m control texels, soft edge {Mathf.Max(config.RoadEdgeSoftness, 2f * texel):0.#} m:");
        Console.WriteLine($"  carriageway edge wanders {carriageDrift * 100f:0.0} cm along the road, sits {carriageBias * 100f:0.0} cm off its true line");
        Console.WriteLine($"  verge edge wanders {groundDrift * 100f:0.0} cm along the road, sits {groundBias * 100f:0.0} cm off its true line");
        Console.WriteLine($"  carriageway weight varies by at most {wobble:0.000} along the road at a fixed offset");

        Require(carriageDrift < 0.1f, $"The carriageway edge must follow its line, it wanders {carriageDrift:0.00} m.");
        Require(groundDrift < 0.1f, $"The verge edge must follow its line, it wanders {groundDrift:0.00} m.");
        Require(carriageBias < 0.25f && groundBias < 0.25f, "The painted edges must sit on the carriageway and verge lines.");
        Require(wobble < 0.1f, $"Weights along a straight road must not beat against the texel grid, they vary by {wobble:0.000}.");

        CheckJunction(config, database, field, carriage);

        Console.WriteLine("Road paint checks passed: straight edges at every angle, edges on their lines, full coverage, junctions filled.");
    }

    private static void CheckJunction(WorldGenerationConfig config, BiomeDatabase database, BiomeWeightField field, TerrainLayer carriage)
    {
        var through = new Road(new[] { new Vector2(100f, 256f), new Vector2(412f, 256f) }, config.RoadHalfWidth * 2f);
        var branch = new Road(new[] { new Vector2(256f, 256f), new Vector2(256f, 420f) }, config.StreetHalfWidth * 2f);

        using var painter = new GroundSplatPainter(config, database, field, new List<Road> { through, branch });
        using var baked = painter.BakeWorld(RESOLUTION, new float[RESOLUTION * RESOLUTION], new float[RESOLUTION * RESOLUTION]);

        var weights = new float[baked.Length];
        baked.CopyTo(weights);

        int layers = painter.Layers.Length;
        int index = Array.IndexOf(painter.Layers, carriage);
        float texel = (float)WORLD / RESOLUTION;

        RequireCoverage(weights, layers);

        Require(Bilinear(weights, layers, index, new Vector2(256f, 256f), texel) > 0.95f, "A junction must be paved where the roads meet.");
        Require(Bilinear(weights, layers, index, new Vector2(256f, 300f), texel) > 0.5f, "A branch must stay paved along its axis.");
        Require(Bilinear(weights, layers, index, new Vector2(256f, 200f), texel) < 0.01f, "Paint must not leak past the far side of the through road.");
    }

    private static float Crossing(float[] weights, int layers, int layer, Vector2 axis, Vector2 normal, float from, float to, bool falling)
    {
        float texel = (float)WORLD / RESOLUTION;
        float previous = Bilinear(weights, layers, layer, axis + normal * from, texel);

        for (float offset = from + 0.02f; offset <= to; offset += 0.02f)
        {
            float value = Bilinear(weights, layers, layer, axis + normal * offset, texel);
            bool crossed = falling ? previous >= 0.5f && value < 0.5f : previous < 0.5f && value >= 0.5f;

            if (crossed)
                return offset - 0.02f + 0.02f * (previous - 0.5f) / (previous - value);

            previous = value;
        }

        return float.NaN;
    }

    private static float Bilinear(float[] weights, int layers, int layer, Vector2 point, float texel)
    {
        float fx = Mathf.Clamp(point.x / texel - 0.5f, 0f, RESOLUTION - 1.001f);
        float fz = Mathf.Clamp(point.y / texel - 0.5f, 0f, RESOLUTION - 1.001f);

        int x = (int)fx;
        int z = (int)fz;
        float ax = fx - x;
        float az = fz - z;

        float bottom = weights[(z * RESOLUTION + x) * layers + layer] * (1f - ax) + weights[(z * RESOLUTION + x + 1) * layers + layer] * ax;
        float top = weights[((z + 1) * RESOLUTION + x) * layers + layer] * (1f - ax) + weights[((z + 1) * RESOLUTION + x + 1) * layers + layer] * ax;

        return bottom * (1f - az) + top * az;
    }

    private static void RequireCoverage(float[] weights, int layers)
    {
        for (int texel = 0; texel < weights.Length / layers; texel++)
        {
            float sum = 0f;

            for (int layer = 0; layer < layers; layer++)
            {
                float value = weights[texel * layers + layer];
                Require(!float.IsNaN(value) && value >= 0f && value <= 1f, "Road weights must be finite and bounded.");
                sum += value;
            }

            Require(Math.Abs(sum - 1f) < 1e-4f, "Every texel must keep full coverage next to a road.");
        }
    }

    private static float Spread(List<float> values)
    {
        float low = float.MaxValue;
        float high = float.MinValue;

        foreach (float value in values)
        {
            Require(!float.IsNaN(value), "Every sample across the road must find its edge.");
            low = Mathf.Min(low, value);
            high = Mathf.Max(high, value);
        }

        return high - low;
    }

    private static float Mean(List<float> values)
    {
        double total = 0d;

        foreach (float value in values)
            total += value;

        return (float)(total / values.Count);
    }

    private static void Set(object target, string name, object value)
    {
        target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void Require(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }
}
