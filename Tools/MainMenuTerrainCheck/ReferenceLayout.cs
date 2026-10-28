using Unity.Mathematics;

public sealed class ReferenceLayout
{
    public const float BASE_HEIGHT = 8f;
    public const float HEIGHT_RANGE = 96f;
    public const int LAYER_COUNT = 11;

    public static readonly string[] LayerNames =
    {
        "Forest_Base", "Forest_Moss", "Forest_Shade", "Forest_Soil", "Forest_Stones",
        "Burnt_Base", "Burnt_Ash", "Burnt_Charcoal", "Road_Asphalt", "Road_Dirt", "Road_Verge"
    };

    private readonly float2 _camera;
    private readonly float2 _forward;
    private readonly float2 _right;
    private readonly float2 _noiseOffset;
    private readonly float _hillHeight;
    private readonly float _roadHalfWidth;
    private readonly float _surfaceRelief;

    public float Size { get; }
    public float2 Origin { get; }
    public float2 HouseCenter { get; } = new(-20f, 36f);
    public float2 HouseSize { get; } = new(28f, 24f);

    public ReferenceLayout(int seed, float size, float hillHeight, float roadWidth, float2 camera, float2 forward, float surfaceRelief = 1.2f)
    {
        Size = size;
        _camera = camera;
        _forward = math.normalizesafe(forward, new float2(0f, 1f));
        _right = new float2(_forward.y, -_forward.x);
        _hillHeight = hillHeight;
        _roadHalfWidth = roadWidth * 0.5f;
        _surfaceRelief = surfaceRelief;
        uint hash = math.hash(new int2(seed, 7919));
        _noiseOffset = new float2(hash & 65535u, hash >> 16) * 0.031f;
        Origin = camera + _forward * (size * 0.18f) - size * 0.5f;
    }

    public float2 World(float u, float v) => Origin + new float2(u, v) * Size;
    public float2 FromLocal(float2 local) => _camera + _right * local.x + _forward * local.y;

    public float2 ToLocal(float2 world)
    {
        float2 delta = world - _camera;
        return new float2(math.dot(delta, _right), math.dot(delta, _forward));
    }

    public float RoadX(float z) => 15f + 9f * math.sin(z * 0.017f) + 4f * math.sin(z * 0.037f);

    public float RoadDistance(float2 world)
    {
        float2 local = ToLocal(world);
        float derivative = 0.153f * math.cos(local.y * 0.017f) + 0.148f * math.cos(local.y * 0.037f);
        float main = math.abs(local.x - RoadX(local.y)) * math.rsqrt(1f + derivative * derivative);
        float houseEdge = HouseCenter.x + HouseSize.x * 0.5f;
        float drivewayX = math.clamp(local.x, houseEdge, RoadX(HouseCenter.y));
        float driveway = math.length(local - new float2(drivewayX, HouseCenter.y));
        return math.min(main, driveway);
    }

    public float HouseClearance(float2 world)
    {
        float2 delta = math.max(math.abs(ToLocal(world) - HouseCenter) - HouseSize * 0.5f, 0f);
        return math.length(delta);
    }

    public float Height(float2 world)
    {
        float2 local = ToLocal(world);
        float2 roadPoint = FromLocal(new float2(RoadX(local.y), local.y));
        float roadHeight = RoadHeight(roadPoint);
        float driveway = 1f - Smooth(3f, 10f, math.abs(local.y - HouseCenter.y));
        roadHeight = math.lerp(roadHeight, BASE_HEIGHT, driveway * 0.65f);
        float road = 1f - Smooth(_roadHalfWidth, _roadHalfWidth + 10f, RoadDistance(world));
        float height = math.lerp(RawHeight(world), roadHeight, road);
        float house = 1f - Smooth(0f, 10f, HouseClearance(world));
        return math.lerp(height, BASE_HEIGHT, house);
    }

    public float BurntWeight(float2 world)
    {
        float2 local = ToLocal(world);
        return Smooth(-35f, 55f, local.x + (Noise(world, 110f, 6f) - 0.5f) * 100f);
    }

    public float GrassWeight(float2 world, float maxSlope, float slope, bool burnt)
    {
        float distance = RoadDistance(world);
        float road = Smooth(_roadHalfWidth + 1.5f, _roadHalfWidth + 4.5f, distance);
        float house = Smooth(1f, 5f, HouseClearance(world));
        float camera = Smooth(3f, 6f, math.length(ToLocal(world)));
        float slopeWeight = 1f - Smooth(math.max(0f, maxSlope - 8f), maxSlope, slope);
        float biome = BurntWeight(world);
        return road * house * camera * slopeWeight * (burnt ? biome : 1f - biome);
    }

    public void Paint(float2 world, float slope, float[] weights)
    {
        for (int i = 0; i < LAYER_COUNT; i++)
            weights[i] = 0f;

        float burnt = BurntWeight(world);
        weights[0] = 1f;
        Overlay(weights, 1, (1f - burnt) * Smooth(0.43f, 0.7f, Noise(world, 29f, 1f)) * 0.65f);
        Overlay(weights, 2, (1f - burnt) * Smooth(0.45f, 0.75f, Noise(world, 61f, 2f)) * 0.6f);
        Overlay(weights, 3, (1f - burnt) * Smooth(0.4f, 0.7f, Noise(world, 38f, 3f)) * 0.7f);
        Overlay(weights, 4, (1f - burnt) * Smooth(12f, 30f, slope) * 0.85f);
        Overlay(weights, 5, burnt);
        Overlay(weights, 6, burnt * Smooth(0.4f, 0.72f, Noise(world, 43f, 4f)) * 0.8f);
        Overlay(weights, 7, burnt * math.max(Smooth(14f, 32f, slope), Smooth(0.52f, 0.8f, Noise(world, 24f, 5f))) * 0.8f);

        float distance = RoadDistance(world);
        Overlay(weights, 10, (1f - Smooth(_roadHalfWidth, _roadHalfWidth + 3f, distance)) * 0.85f);
        float roadEdge = _roadHalfWidth + (Noise(world, 4f, 7f) - 0.5f) * 0.65f;
        Overlay(weights, 9, 1f - Smooth(roadEdge - 0.45f, roadEdge + 0.45f, distance));
        float pad = 1f - Smooth(0f, 3f, HouseClearance(world));
        Overlay(weights, 3, pad * 0.8f);

        float total = 0f;
        for (int i = 0; i < LAYER_COUNT; i++)
            total += weights[i];
        for (int i = 0; i < LAYER_COUNT; i++)
            weights[i] /= total;
    }

    public float Noise(float2 world, float period, float salt)
    {
        return math.saturate(noise.snoise(world / period + _noiseOffset + salt * 17.31f) * 0.5f + 0.5f);
    }

    private float RawHeight(float2 world)
    {
        float2 normalized = (world - Origin) / Size * 2f - 1f;
        float2 squared = normalized * normalized;
        float edge = Smooth(0.53f, 1f, math.sqrt(math.sqrt(math.csum(squared * squared))));
        float hills = _hillHeight * edge * (0.42f + Noise(world, 87f, 0f) * 0.58f);
        hills *= math.lerp(0.16f, 1f, Smooth(_roadHalfWidth + 12f, _roadHalfWidth + 52f, RoadDistance(world)));
        float ripples = (Noise(world, 54f, 9f) - 0.5f) * _surfaceRelief * 2f;
        ripples += (Noise(world, 14f, 10f) - 0.5f) * _surfaceRelief * 0.45f;
        float cameraDistance = math.length(ToLocal(world));
        return BASE_HEIGHT + hills * Smooth(16f, 48f, cameraDistance) + ripples * Smooth(2f, 10f, cameraDistance);
    }

    private float RoadHeight(float2 world)
    {
        float height = RawHeight(world) * 3f;
        height += (RawHeight(world - _forward * 4f) + RawHeight(world + _forward * 4f)) * 2f;
        height += RawHeight(world - _forward * 8f) + RawHeight(world + _forward * 8f);
        return height / 9f;
    }

    private static void Overlay(float[] weights, int layer, float opacity)
    {
        opacity = math.saturate(opacity);
        for (int i = 0; i < LAYER_COUNT; i++)
            weights[i] *= 1f - opacity;
        weights[layer] += opacity;
    }

    private static float Smooth(float min, float max, float value)
    {
        float t = math.saturate((value - min) / (max - min));
        return t * t * (3f - 2f * t);
    }
}
