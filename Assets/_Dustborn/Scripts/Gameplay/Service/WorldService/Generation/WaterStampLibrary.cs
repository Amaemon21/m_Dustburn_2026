using System.Collections.Generic;
using UnityEngine;

public sealed class WaterStampLibrary
{
    private readonly WaterStampShape[] _shapes;
    private readonly object _gate = new();

    public WaterStampSettings Settings { get; }
    public WaterStampDatabase Database { get; }
    public List<int> Rivers { get; } = new();
    public List<int> Lakes { get; } = new();
    public List<int> Ponds { get; } = new();
    public float ReferenceWidth { get; }

    private WaterStampLibrary(WaterStampSettings settings)
    {
        Settings = settings;
        Database = settings.Database;
        _shapes = new WaterStampShape[Database.Count];

        var widths = new List<float>();

        for (int i = 0; i < Database.Count; i++)
        {
            WaterStampDefinition definition = Database.Get(i);

            if (definition == null || !definition.IsValid)
            {
                Debug.LogWarning($"Water stamp {definition?.Name ?? i.ToString()} is not usable (no mask, wrong size or not traced). Run the water stamp import again.");
                continue;
            }

            switch (definition.Kind)
            {
                case WaterStampKind.River:
                    Rivers.Add(i);
                    widths.Add(definition.NominalChannelWidth);
                    break;
                case WaterStampKind.Lake:
                    Lakes.Add(i);
                    break;
                default:
                    Ponds.Add(i);
                    break;
            }
        }

        widths.Sort();
        ReferenceWidth = widths.Count == 0 ? 1f : widths[widths.Count / 2];
    }

    public static WaterStampLibrary Create(WaterGenerationSettings water)
    {
        WaterStampSettings settings = water?.Stamps;

        if (settings == null || !settings.Enabled || settings.Database == null || settings.Database.Count == 0)
            return null;

        var library = new WaterStampLibrary(settings);

        return library.Rivers.Count + library.Lakes.Count + library.Ponds.Count == 0 ? null : library;
    }

    public WaterStampDefinition Get(int index)
    {
        return Database.Get(index);
    }

    public WaterStampShape Shape(int index)
    {
        lock (_gate)
            return _shapes[index] ??= WaterStampShape.From(Database.Get(index));
    }

    public int Find(WaterStampCategory category)
    {
        foreach (int index in Rivers)
        {
            if (Database.Get(index).Category == category)
                return index;
        }

        return -1;
    }

    public bool Mirrors(WaterStampDefinition definition)
    {
        return Settings.AllowMirroring && definition.AllowMirror;
    }

    public static float Hash01(int seed, int a, int b, int c)
    {
        unchecked
        {
            uint hash = (uint)seed * 2654435761u ^ (uint)(a + 1) * 2246822519u ^ (uint)(b + 7) * 3266489917u ^ (uint)(c + 13) * 668265263u;

            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;

            return ((hash & 0xFFFFFF) + 0.5f) / 0x1000000;
        }
    }

    public static float Race(float weight, float roll)
    {
        return weight <= 0f ? float.MaxValue : -Mathf.Log(Mathf.Max(1e-7f, roll)) / weight;
    }

    public static float SmoothStep(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / (to - from));

        return t * t * (3f - 2f * t);
    }

    public static float Angle(Vector2 a, Vector2 b)
    {
        float magnitude = a.magnitude * b.magnitude;

        if (magnitude < 1e-8f)
            return 0f;

        return Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b) / magnitude, -1f, 1f)) * Mathf.Rad2Deg;
    }

    public static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
