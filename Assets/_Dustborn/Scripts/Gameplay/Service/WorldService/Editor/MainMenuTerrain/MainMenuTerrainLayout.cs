using Unity.Mathematics;

public readonly struct MainMenuTerrainLayout
{
    public const float BASE_HEIGHT = 8f;
    public const float HEIGHT_RANGE = 96f;
    public const int LAYER_COUNT = 11;

    private readonly float2 _camera;
    private readonly float2 _forward;
    private readonly float2 _right;
    private readonly float2 _noiseOffset;
    private readonly float _hillHeight;
    private readonly float _roadHalfWidth;
    private readonly float _surfaceRelief;

    public float Size { get; }
    public float2 Origin { get; }
    public float2 HouseCenter => new(-20f, 36f);
    public float2 HouseSize => new(28f, 24f);

    public MainMenuTerrainLayout(int seed, float size, float hillHeight, float roadWidth, float2 camera, float2 forward, float surfaceRelief = 1.2f)
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
    public MainMenuTerrainLayout WithOrigin(float2 origin) => new(this, origin);

    private MainMenuTerrainLayout(MainMenuTerrainLayout source, float2 origin)
    {
        this = source;
        Origin = origin;
    }

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
        float clearance = HouseClearance(world);
        if (clearance == 0f)
            return BASE_HEIGHT;
        float distance = RoadDistance(world);
        float height = RawHeight(world, distance);
        float road = 1f - Smooth(_roadHalfWidth, _roadHalfWidth + 10f, distance);
        if (road == 0f)
            return math.lerp(height, BASE_HEIGHT, 1f - Smooth(0f, 10f, clearance));
        float2 local = ToLocal(world);
        float2 roadPoint = FromLocal(new float2(RoadX(local.y), local.y));
        float roadHeight = RoadHeight(roadPoint);
        float driveway = 1f - Smooth(3f, 10f, math.abs(local.y - HouseCenter.y));
        roadHeight = math.lerp(roadHeight, BASE_HEIGHT, driveway * 0.65f);
        height = math.lerp(height, roadHeight, road);
        float house = 1f - Smooth(0f, 10f, clearance);
        return math.lerp(height, BASE_HEIGHT, house);
    }

    public float BurntWeight(float2 world)
    {
        float2 local = ToLocal(world);
        return Smooth(-35f, 55f, local.x + (Noise(world, 110f, 6f) - 0.5f) * 100f);
    }

    public float GrassWeight(float2 world, float maxSlope, float slope, bool burnt)
    {
        float2 surface = GrassSurface(world);
        float slopeWeight = 1f - Smooth(math.max(0f, maxSlope - 8f), maxSlope, slope);
        return surface.x * slopeWeight * (burnt ? surface.y : 1f - surface.y);
    }

    public float2 GrassSurface(float2 world)
    {
        float distance = RoadDistance(world);
        float road = Smooth(_roadHalfWidth + 1.5f, _roadHalfWidth + 4.5f, distance);
        float house = Smooth(1f, 5f, HouseClearance(world));
        float camera = Smooth(3f, 6f, math.length(ToLocal(world)));
        float cover = road * house * camera;
        return new float2(cover, cover > 0f ? BurntWeight(world) : 0f);
    }

    public void Paint(float2 world, float slope, float[] weights)
    {
        MainMenuTerrainWeights packed = Paint(world, slope);
        for (int i = 0; i < LAYER_COUNT; i++)
            weights[i] = packed[i];
    }

    public MainMenuTerrainWeights Paint(float2 world, float slope)
    {
        float burnt = BurntWeight(world);
        var weights = new MainMenuTerrainWeights { First = new float4(1f, 0f, 0f, 0f) };
        weights.Overlay(1, (1f - burnt) * Smooth(0.43f, 0.7f, Noise(world, 29f, 1f)) * 0.65f);
        weights.Overlay(2, (1f - burnt) * Smooth(0.45f, 0.75f, Noise(world, 61f, 2f)) * 0.6f);
        weights.Overlay(3, (1f - burnt) * Smooth(0.4f, 0.7f, Noise(world, 38f, 3f)) * 0.7f);
        weights.Overlay(4, (1f - burnt) * Smooth(12f, 30f, slope) * 0.85f);
        weights.Overlay(5, burnt);
        weights.Overlay(6, burnt * Smooth(0.4f, 0.72f, Noise(world, 43f, 4f)) * 0.8f);
        weights.Overlay(7, burnt * math.max(Smooth(14f, 32f, slope), Smooth(0.52f, 0.8f, Noise(world, 24f, 5f))) * 0.8f);

        float distance = RoadDistance(world);
        weights.Overlay(10, (1f - Smooth(_roadHalfWidth, _roadHalfWidth + 3f, distance)) * 0.85f);
        float roadEdge = _roadHalfWidth + (Noise(world, 4f, 7f) - 0.5f) * 0.65f;
        weights.Overlay(9, 1f - Smooth(roadEdge - 0.45f, roadEdge + 0.45f, distance));
        float pad = 1f - Smooth(0f, 3f, HouseClearance(world));
        weights.Overlay(3, pad * 0.8f);
        weights.Normalize();
        return weights;
    }

    public float Noise(float2 world, float period, float salt)
    {
        return math.saturate(noise.snoise(world / period + _noiseOffset + salt * 17.31f) * 0.5f + 0.5f);
    }

    private float RawHeight(float2 world)
    {
        return RawHeight(world, RoadDistance(world));
    }

    private float RawHeight(float2 world, float roadDistance)
    {
        float2 normalized = (world - Origin) / Size * 2f - 1f;
        float2 squared = normalized * normalized;
        float edge = Smooth(0.53f, 1f, math.sqrt(math.sqrt(math.csum(squared * squared))));
        float hills = _hillHeight * edge * (0.42f + Noise(world, 87f, 0f) * 0.58f);
        hills *= math.lerp(0.16f, 1f, Smooth(_roadHalfWidth + 12f, _roadHalfWidth + 52f, roadDistance));
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

    private static float Smooth(float min, float max, float value)
    {
        float t = math.saturate((value - min) / (max - min));
        return t * t * (3f - 2f * t);
    }
}

public struct MainMenuTerrainWeights
{
    public float4 First;
    public float4 Second;
    public float4 Third;

    public float this[int layer] => layer < 4 ? First[layer] : layer < 8 ? Second[layer - 4] : Third[layer - 8];

    public void Overlay(int layer, float opacity)
    {
        opacity = math.saturate(opacity);
        Scale(1f - opacity);
        if (layer < 4)
            First[layer] += opacity;
        else if (layer < 8)
            Second[layer - 4] += opacity;
        else
            Third[layer - 8] += opacity;
    }

    public void Normalize()
    {
        Scale(1f / (math.csum(First) + math.csum(Second) + math.csum(Third)));
    }

    private void Scale(float value)
    {
        First *= value;
        Second *= value;
        Third *= value;
    }
}
