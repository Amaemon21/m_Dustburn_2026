using Unity.Mathematics;

public static class MainMenuVegetationPattern
{
    public static float Coverage(float2 world)
    {
        float broad = noise.snoise(world / 23f + new float2(73.1f, 19.7f)) * 0.5f + 0.5f;
        float small = noise.snoise(world / 7f + new float2(11.3f, 51.9f)) * 0.5f + 0.5f;
        return math.lerp(0.08f, 1f, math.smoothstep(0.31f, 0.66f, broad * 0.8f + small * 0.2f));
    }

    public static bool Keep(int x, int z, int layer, float coverage)
    {
        uint hash = math.hash(new int3(x, z, layer + 2026));
        float sample = (hash & 0x00ffffffu) / 16777216f;
        return sample < coverage;
    }
}
