using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

public sealed class BiomeShareLayout
{
    private const int FIT_GRID = 160;
    private const int FIT_ITERATIONS = 48;
    private const float FIT_RATE = 0.4f;
    private const float FIT_FLOOR = 1e-3f;
    private const float MIN_WEIGHT = 0.02f;
    private const float MAX_WEIGHT = 50f;
    private const float STRIPE_TILT = 0.35f;
    private const uint ORDER_SALT = 2891336453u;

    private readonly WorldGenerationConfig _config;
    private readonly BiomeDatabase _biomes;
    private readonly float2 _warpOffset;
    private readonly float[] _targets;

    public BiomeShareLayout(WorldGenerationConfig config, BiomeDatabase biomes, float2 warpOffset)
    {
        _config = config;
        _biomes = biomes;
        _warpOffset = warpOffset;
        _targets = Targets(config, biomes);
    }

    public IReadOnlyList<float> Shares => _targets;

    public static float[] Targets(WorldGenerationConfig config, BiomeDatabase biomes)
    {
        var targets = new float[biomes.Count];
        var copies = new int[Enum.GetValues(typeof(BiomeType)).Length];

        for (int i = 0; i < biomes.Count; i++)
            copies[(int)biomes.Get(i).Type]++;

        float total = 0f;

        for (int i = 0; i < biomes.Count; i++)
        {
            BiomeType type = biomes.Get(i).Type;
            targets[i] = config.BiomeShare(type) / copies[(int)type];
            total += targets[i];
        }

        for (int i = 0; i < targets.Length; i++)
            targets[i] = total > 0f ? targets[i] / total : 1f / targets.Length;

        return targets;
    }

    public BiomeSeed[] Fit(BiomeSeed[] seeds)
    {
        var kept = new List<BiomeSeed>(seeds.Length);

        foreach (BiomeSeed seed in seeds)
        {
            if (_targets[seed.Biome] > 0f)
                kept.Add(seed);
        }

        BiomeSeed[] fitted = kept.ToArray();
        float2[] points = WarpedGrid(FIT_GRID);
        var weights = new float[_biomes.Count];
        var counts = new int[_biomes.Count];

        for (int i = 0; i < weights.Length; i++)
            weights[i] = math.max(0.05f, _biomes.Get(i).RegionWeight);

        for (int iteration = 0; iteration < FIT_ITERATIONS; iteration++)
        {
            Weigh(fitted, weights);
            Count(fitted, points, counts);

            for (int biome = 0; biome < weights.Length; biome++)
            {
                if (_targets[biome] <= 0f)
                    continue;

                float actual = counts[biome] / (float)points.Length;
                float ratio = (_targets[biome] + FIT_FLOOR) / (actual + FIT_FLOOR);
                weights[biome] = math.clamp(weights[biome] * (float)Math.Pow(ratio, FIT_RATE), MIN_WEIGHT, MAX_WEIGHT);
            }
        }

        Weigh(fitted, weights);
        return fitted;
    }

    public void Classify(BiomeMap map)
    {
        int resolution = map.Resolution;
        float2[] points = WarpedGrid(resolution);
        var random = new Random(((uint)_config.Seed | 1u) * ORDER_SALT + 7u);
        byte[] cells = map.Cells;

        switch (_config.BiomeLayout)
        {
            case BiomeLayout.ForestCenter:
                Centred(points, cells, Index(BiomeType.PineForest), ref random);
                break;
            case BiomeLayout.WastelandCenter:
                Centred(points, cells, Index(BiomeType.Wasteland), ref random);
                break;
            case BiomeLayout.Rings:
                Rings(points, cells, ref random);
                break;
            default:
                Stripes(points, cells, ref random);
                break;
        }
    }

    private void Centred(float2[] points, byte[] cells, int centre, ref Random random)
    {
        var all = new int[points.Length];
        for (int i = 0; i < all.Length; i++)
            all[i] = i;

        int inner = centre >= 0 ? Cells(points.Length, _targets[centre]) : 0;
        int[] radial = Sorted(all, i => Radius(points[i]));

        for (int i = 0; i < inner; i++)
            cells[radial[i]] = (byte)centre;

        var rest = new int[points.Length - inner];
        Array.Copy(radial, inner, rest, 0, rest.Length);

        float turn = random.NextFloat();
        int[] around = Sorted(rest, i => Fraction(Angle(points[i]) + turn));
        List<int> order = Shuffled(Others(centre), ref random);

        Fill(around, cells, order);
    }

    private void Rings(float2[] points, byte[] cells, ref Random random)
    {
        var all = new int[points.Length];
        for (int i = 0; i < all.Length; i++)
            all[i] = i;

        int centre = Index(BiomeType.PineForest);
        var order = new List<int>();

        if (centre >= 0)
            order.Add(centre);

        order.AddRange(Shuffled(Others(centre), ref random));
        Fill(Sorted(all, i => Radius(points[i])), cells, order);
    }

    private void Stripes(float2[] points, byte[] cells, ref Random random)
    {
        var all = new int[points.Length];
        for (int i = 0; i < all.Length; i++)
            all[i] = i;

        float tilt = random.NextFloat(-STRIPE_TILT, STRIPE_TILT);
        var axis = new float2(math.sin(tilt), math.cos(tilt));
        var order = new List<int>();

        foreach (BiomeType type in new[] { BiomeType.Desert, BiomeType.Wasteland, BiomeType.BurntForest, BiomeType.PineForest, BiomeType.Snow })
        {
            for (int i = 0; i < _biomes.Count; i++)
            {
                if (_biomes.Get(i).Type == type)
                    order.Add(i);
            }
        }

        Fill(Sorted(all, i => math.dot(points[i] - 0.5f, axis)), cells, order);
    }

    private void Fill(int[] sorted, byte[] cells, List<int> order)
    {
        float total = 0f;
        foreach (int biome in order)
            total += _targets[biome];

        if (sorted.Length == 0)
            return;

        int start = 0;
        float cumulative = 0f;
        int last = -1;

        foreach (int biome in order)
        {
            if (_targets[biome] <= 0f)
                continue;

            last = biome;
            cumulative += total > 0f ? _targets[biome] / total : 0f;
            int end = Math.Min(sorted.Length, (int)Math.Round(cumulative * sorted.Length));

            for (int i = start; i < end; i++)
                cells[sorted[i]] = (byte)biome;

            start = Math.Max(start, end);
        }

        if (last < 0)
            last = order.Count > 0 ? order[order.Count - 1] : 0;

        for (int i = start; i < sorted.Length; i++)
            cells[sorted[i]] = (byte)last;
    }

    private List<int> Others(int centre)
    {
        var others = new List<int>();

        for (int i = 0; i < _biomes.Count; i++)
        {
            if (i != centre && _targets[i] > 0f)
                others.Add(i);
        }

        return others;
    }

    private static List<int> Shuffled(List<int> items, ref Random random)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    private int Index(BiomeType type)
    {
        for (int i = 0; i < _biomes.Count; i++)
        {
            if (_biomes.Get(i).Type == type)
                return i;
        }

        return -1;
    }

    private float2[] WarpedGrid(int resolution)
    {
        var points = new float2[resolution * resolution];
        WorldGenerationConfig config = _config;
        float2 offset = _warpOffset;

        Parallel.For(0, resolution, row =>
        {
            float v = (row + 0.5f) / resolution;

            for (int x = 0; x < resolution; x++)
            {
                var uv = new float2((x + 0.5f) / resolution, v);
                points[row * resolution + x] = uv + BiomeClassifyJob.Warp(uv, offset, config.WarpStrength, config.WarpFrequency,
                    config.WarpOctaves, config.WarpLacunarity, config.WarpPersistence);
            }
        });

        return points;
    }

    private static void Weigh(BiomeSeed[] seeds, float[] weights)
    {
        for (int i = 0; i < seeds.Length; i++)
        {
            float weight = weights[seeds[i].Biome];
            seeds[i].InverseWeightSqr = 1f / (weight * weight);
        }
    }

    private static void Count(BiomeSeed[] seeds, float2[] points, int[] counts)
    {
        Array.Clear(counts, 0, counts.Length);

        foreach (float2 point in points)
        {
            byte best = 0;
            float bestScore = float.MaxValue;

            foreach (BiomeSeed seed in seeds)
            {
                float score = math.distancesq(point, seed.Position) * seed.InverseWeightSqr;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = seed.Biome;
            }

            counts[best]++;
        }
    }

    private static int Cells(int total, float share) => Math.Min(total, (int)Math.Round(total * (double)share));

    private static float Radius(float2 point) => math.length(point - 0.5f);

    private static float Angle(float2 point)
    {
        float2 offset = point - 0.5f;
        return (math.atan2(offset.y, offset.x) + math.PI) / (2f * math.PI);
    }

    private static float Fraction(float value) => value - math.floor(value);

    private static int[] Sorted(int[] cells, Func<int, float> key)
    {
        var keys = new ulong[cells.Length];

        for (int i = 0; i < cells.Length; i++)
            keys[i] = ((ulong)Ordered(key(cells[i])) << 32) | (uint)cells[i];

        Array.Sort(keys);

        var sorted = new int[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            sorted[i] = (int)(keys[i] & uint.MaxValue);

        return sorted;
    }

    private static uint Ordered(float value)
    {
        uint bits = math.asuint(value);
        return (bits & 0x80000000u) != 0 ? ~bits : bits | 0x80000000u;
    }
}
