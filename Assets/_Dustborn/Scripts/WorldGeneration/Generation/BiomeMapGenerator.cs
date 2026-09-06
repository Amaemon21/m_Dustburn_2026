using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

public class BiomeMapGenerator
{
    private const int CLASSIFY_BATCH = 4;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;

    private BiomeSeed[] _seeds;
    private float2 _warpOffset;

    private byte[] _cells;
    private bool[] _visited;
    private int[] _borderCounts;
    private List<int> _region;
    private Stack<int> _stack;
    private int _resolution;

    public BiomeMapGenerator(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        _config = config;
        _biomes = biomes;
    }

    public BiomeMap Generate()
    {
        var random = new Random((uint)_config.Seed | 1u);

        _warpOffset = random.NextFloat2(-100f, 100f);

        PlaceSeeds(ref random);

        var map = new BiomeMap(_config.BiomeMapResolution, _config.WorldSize);

        Classify(map);

        byte[] buffer = new byte[map.Cells.Length];

        for (int pass = 0; pass < _config.SmoothingPasses; pass++)
            Smooth(map, buffer);

        RemoveSmallRegions(map);

        return map;
    }

    private void PlaceSeeds(ref Random random)
    {
        int count = _biomes.Count;
        int columns = (int)math.ceil(math.sqrt(count));
        int rows = (int)math.ceil((float)count / columns);

        if (columns * rows == count)
            columns++;

        int[] slots = new int[columns * rows];

        for (int i = 0; i < slots.Length; i++)
            slots[i] = i;

        for (int i = slots.Length - 1; i > 0; i--)
        {
            int j = random.NextInt(i + 1);

            (slots[i], slots[j]) = (slots[j], slots[i]);
        }

        _seeds = new BiomeSeed[count];

        for (int i = 0; i < count; i++)
        {
            int slot = slots[i];

            float2 center = new(
                (slot % columns + 0.5f) / columns,
                (slot / columns + 0.5f) / rows);

            float2 jitterRange = new(0.5f / columns, 0.5f / rows);
            float2 jitter = random.NextFloat2(-1f, 1f) * jitterRange * _config.SeedJitter;

            float weight = math.max(0.05f, _biomes.Get(i).RegionWeight);

            _seeds[i] = new BiomeSeed
            {
                Position = center + jitter,
                InverseWeightSqr = 1f / (weight * weight),
                Biome = (byte)i
            };
        }
    }

    private void Classify(BiomeMap map)
    {
        var seeds = new NativeArray<BiomeSeed>(_seeds, Allocator.TempJob);
        var cells = new NativeArray<byte>(map.Cells.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            var job = new BiomeClassifyJob
            {
                Seeds = seeds,
                Cells = cells,
                Resolution = map.Resolution,
                WarpOffset = _warpOffset,
                WarpStrength = _config.WarpStrength,
                WarpFrequency = _config.WarpFrequency,
                WarpOctaves = _config.WarpOctaves,
                WarpLacunarity = _config.WarpLacunarity,
                WarpPersistence = _config.WarpPersistence
            };

            job.Schedule(map.Resolution, CLASSIFY_BATCH).Complete();

            cells.CopyTo(map.Cells);
        }
        finally
        {
            seeds.Dispose();
            cells.Dispose();
        }
    }

    private void Smooth(BiomeMap map, byte[] buffer)
    {
        int resolution = map.Resolution;
        int biomeCount = _biomes.Count;
        byte[] cells = map.Cells;

        Array.Copy(cells, buffer, cells.Length);

        Parallel.For(0, resolution, () => new int[biomeCount], (y, state, counts) =>
        {
            for (int x = 0; x < resolution; x++)
            {
                Array.Clear(counts, 0, counts.Length);

                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        int sampleX = math.clamp(x + offsetX, 0, resolution - 1);
                        int sampleY = math.clamp(y + offsetY, 0, resolution - 1);

                        counts[buffer[sampleY * resolution + sampleX]]++;
                    }
                }

                byte current = buffer[y * resolution + x];
                byte dominant = current;
                int dominantCount = counts[current];

                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] <= dominantCount)
                        continue;

                    dominantCount = counts[i];
                    dominant = (byte)i;
                }

                cells[y * resolution + x] = dominant;
            }

            return counts;
        }, _ => { });
    }

    private void RemoveSmallRegions(BiomeMap map)
    {
        if (_config.MinRegionCells <= 0)
            return;

        _cells = map.Cells;
        _resolution = map.Resolution;
        _visited = new bool[_cells.Length];
        _borderCounts = new int[_biomes.Count];
        _region = new List<int>();
        _stack = new Stack<int>();

        for (int start = 0; start < _cells.Length; start++)
        {
            if (_visited[start])
                continue;

            byte biome = _cells[start];

            CollectRegion(start, biome);

            if (_region.Count >= _config.MinRegionCells)
                continue;

            byte replacement = PickDominantBorder(_borderCounts, biome);

            foreach (int cell in _region)
                _cells[cell] = replacement;
        }
    }

    private void CollectRegion(int start, byte biome)
    {
        _region.Clear();
        Array.Clear(_borderCounts, 0, _borderCounts.Length);

        _visited[start] = true;
        _stack.Push(start);

        while (_stack.Count > 0)
        {
            int current = _stack.Pop();
            _region.Add(current);

            int x = current % _resolution;
            int y = current / _resolution;

            TryVisit(x - 1, y, biome);
            TryVisit(x + 1, y, biome);
            TryVisit(x, y - 1, biome);
            TryVisit(x, y + 1, biome);
        }
    }

    private void TryVisit(int x, int y, byte biome)
    {
        if (x < 0 || y < 0 || x >= _resolution || y >= _resolution)
            return;

        int neighbour = y * _resolution + x;
        byte neighbourBiome = _cells[neighbour];

        if (neighbourBiome != biome)
        {
            _borderCounts[neighbourBiome]++;
            return;
        }

        if (_visited[neighbour])
            return;

        _visited[neighbour] = true;
        _stack.Push(neighbour);
    }

    private static byte PickDominantBorder(int[] borderCounts, byte fallback)
    {
        byte dominant = fallback;
        int dominantCount = 0;

        for (int i = 0; i < borderCounts.Length; i++)
        {
            if (borderCounts[i] <= dominantCount)
                continue;

            dominantCount = borderCounts[i];
            dominant = (byte)i;
        }

        return dominant;
    }
}
