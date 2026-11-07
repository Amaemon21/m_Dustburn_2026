using System;
using System.Collections.Generic;

public static class TerrainStampShapes
{
    public const int CRATER_BOWL = 0;
    public const int CRATER_RIM = 1;
    public const int PROCEDURAL_COUNT = 2;
    public const float CRATER_RADIUS = 0.55f;
    public const string CRATER_BOWL_NAME = "crater_bowl";
    public const string CRATER_RIM_NAME = "crater_rim";

    private const int CRATER_RESOLUTION = 256;
    private const float RIM_WIDTH = 0.09f;
    private const float EJECTA = 0.3f;
    private const float FADE_START = 0.85f;
    private const float BOWL_POWER = 1.5f;

    private static readonly Lazy<TerrainStampShape> Bowl = new(() => Crater(BowlProfile, TerrainStampOperation.Subtract));
    private static readonly Lazy<TerrainStampShape> Rim = new(() => Crater(RimProfile, TerrainStampOperation.Add));

    public static int FirstProcedural(TerrainStampDatabase database) => database == null ? 0 : database.Count;

    public static TerrainStampShape[] For(TerrainStampDatabase database, IReadOnlyList<TerrainStampPlacement> placements)
    {
        int first = FirstProcedural(database);
        var shapes = new TerrainStampShape[first + PROCEDURAL_COUNT];

        foreach (TerrainStampPlacement placement in placements)
        {
            int index = placement.StampIndex;

            if (shapes[index] != null)
                continue;

            shapes[index] = index < first ? TerrainStampShape.From(database.Get(index)) : Procedural(index - first);
        }

        return shapes;
    }

    public static TerrainStampShape Procedural(int index)
    {
        return index switch
        {
            CRATER_BOWL => Bowl.Value,
            CRATER_RIM => Rim.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "Unknown procedural stamp")
        };
    }

    public static float BowlProfile(float radius)
    {
        float t = radius / CRATER_RADIUS;

        if (t >= 1f)
            return 0f;

        return (float)Math.Pow(1f - t * t, BOWL_POWER);
    }

    public static float RimProfile(float radius)
    {
        float ring = (radius - CRATER_RADIUS) / RIM_WIDTH;
        float crest = (float)Math.Exp(-ring * ring);
        float ejecta = radius > CRATER_RADIUS ? EJECTA * (1f - Smooth(CRATER_RADIUS, 1f, radius)) : 0f;
        float fade = 1f - Smooth(FADE_START, 1f, radius);

        return Math.Min(1f, Math.Max(crest, ejecta)) * fade;
    }

    private static TerrainStampShape Crater(Func<float, float> profile, TerrainStampOperation operation)
    {
        var samples = new ushort[CRATER_RESOLUTION * CRATER_RESOLUTION];
        float last = CRATER_RESOLUTION - 1;

        for (int row = 0; row < CRATER_RESOLUTION; row++)
        {
            for (int column = 0; column < CRATER_RESOLUTION; column++)
            {
                float u = column / last * 2f - 1f;
                float v = row / last * 2f - 1f;
                float value = profile((float)Math.Sqrt(u * u + v * v));
                samples[row * CRATER_RESOLUTION + column] = (ushort)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 65535f);
            }
        }

        return new TerrainStampShape(samples, CRATER_RESOLUTION, operation);
    }

    private static float Smooth(float edge0, float edge1, float value)
    {
        float t = Math.Max(0f, Math.Min(1f, (value - edge0) / (edge1 - edge0)));
        return t * t * (3f - 2f * t);
    }
}
