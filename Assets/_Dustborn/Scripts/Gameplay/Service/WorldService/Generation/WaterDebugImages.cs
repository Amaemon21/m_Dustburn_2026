using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class WaterDebugImages
{
    public static byte[] Accumulation(HydrologyGrid grid)
    {
        int count = grid.Resolution * grid.Resolution;
        var pixels = new byte[count * 3];
        float top = Mathf.Log(Mathf.Max(2f, count), 2f);

        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Clamp01(Mathf.Log(Mathf.Max(1f, grid.Accumulation[i]), 2f) / top);
            Put(pixels, Flip(i, grid.Resolution), (byte)(t * 40f), (byte)(t * 140f), (byte)(40f + t * 215f));
        }

        return pixels;
    }

    public static byte[] Direction(HydrologyGrid grid)
    {
        int count = grid.Resolution * grid.Resolution;
        var pixels = new byte[count * 3];

        for (int i = 0; i < count; i++)
        {
            int receiver = grid.Receiver[i];

            if (receiver < 0)
            {
                Put(pixels, Flip(i, grid.Resolution), 0, 0, 0);
                continue;
            }

            float dx = receiver % grid.Resolution - i % grid.Resolution;
            float dz = receiver / grid.Resolution - i / grid.Resolution;

            Put(pixels, Flip(i, grid.Resolution), (byte)(127f + dx * 120f), (byte)(127f + dz * 120f), 160);
        }

        return pixels;
    }

    public static byte[] Basins(HydrologyGrid grid)
    {
        int count = grid.Resolution * grid.Resolution;
        var pixels = new byte[count * 3];

        for (int i = 0; i < count; i++)
        {
            int basin = grid.Basin[i];
            byte shade = (byte)Mathf.Clamp(grid.Height[i], 0f, 255f);

            if (basin < 0)
            {
                Put(pixels, Flip(i, grid.Resolution), shade, shade, shade);
                continue;
            }

            uint hash = (uint)(basin + 1) * 2654435761u;
            Put(pixels, Flip(i, grid.Resolution), (byte)(64 + (hash & 0x7F)), (byte)(64 + (hash >> 8 & 0x7F)), (byte)(64 + (hash >> 16 & 0x7F)));
        }

        return pixels;
    }

    public static byte[] Mask(WaterMap water)
    {
        int count = water.Resolution * water.Resolution;
        var pixels = new byte[count * 3];

        for (int i = 0; i < count; i++)
        {
            (byte r, byte g, byte b) = (WaterKind)water.Kinds[i] switch
            {
                WaterKind.Sea => ((byte)20, (byte)60, (byte)140),
                WaterKind.Lake => ((byte)40, (byte)110, (byte)200),
                WaterKind.Pond => ((byte)90, (byte)170, (byte)230),
                _ => ShoreShade(water, i)
            };

            Put(pixels, Flip(i, water.Resolution), r, g, b);
        }

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                if (point.Submerged)
                    continue;

                int index = water.CellIndex(point.Position.x, point.Position.y);
                Put(pixels, Flip(index, water.Resolution), 0, 200, 255);
            }
        }

        return pixels;
    }

    public static byte[] BodyIds(WaterMap water)
    {
        int count = water.Resolution * water.Resolution;
        var pixels = new byte[count * 3];

        for (int i = 0; i < count; i++)
        {
            int body = water.BodyIds[i];
            (byte r, byte g, byte b) color = body >= 0 ? Hash(body) : (WaterKind)water.Kinds[i] == WaterKind.Sea ? ((byte)20, (byte)60, (byte)140) : ((byte)230, (byte)225, (byte)210);

            if (water.DetailSlot[i] >= 0)
                color = ((byte)(color.r * 3 / 4), (byte)(color.g * 3 / 4), (byte)(color.b * 3 / 4));

            Put(pixels, Flip(i, water.Resolution), color.r, color.g, color.b);
        }

        return pixels;
    }

    public static byte[] Refined(HeightMap map, WaterMap water, int size)
    {
        var pixels = new byte[size * size * 3];
        float step = (float)map.WorldSize / size;

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
            {
                float x = (column + 0.5f) * step, z = (row + 0.5f) * step;
                (byte r, byte g, byte b) color = Relief(map, x, z, step);

                if (Standing(water, map, x, z, out short owner))
                    color = owner == WaterMap.OWNER_SEA ? ((byte)20, (byte)60, (byte)140) : Hash(owner);

                Put(pixels, (size - 1 - row) * size + column, color.r, color.g, color.b);
            }
        });

        return pixels;
    }

    public static byte[] Comparison(HeightMap map, WaterMap water, int size)
    {
        var pixels = new byte[size * size * 3];
        float step = (float)map.WorldSize / size;

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
            {
                float x = (column + 0.5f) * step, z = (row + 0.5f) * step;
                bool coarse = water.Kinds[water.CellIndex(x, z)] != 0;
                bool refined = Standing(water, map, x, z, out _);

                (byte r, byte g, byte b) color = coarse && refined ? ((byte)40, (byte)100, (byte)190)
                    : coarse ? ((byte)220, (byte)60, (byte)50)
                    : refined ? ((byte)60, (byte)200, (byte)90)
                    : Relief(map, x, z, step);

                Put(pixels, (size - 1 - row) * size + column, color.r, color.g, color.b);
            }
        });

        return pixels;
    }

    public static byte[] Shoreline(HeightMap map, WaterMap water, int size)
    {
        var wet = new bool[size * size];
        var pixels = new byte[size * size * 3];
        float step = (float)map.WorldSize / size;

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
                wet[row * size + column] = Standing(water, map, (column + 0.5f) * step, (row + 0.5f) * step, out _);
        });

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
            {
                int index = row * size + column;
                bool edge = column + 1 < size && wet[index] != wet[index + 1] || row + 1 < size && wet[index] != wet[index + size];
                (byte r, byte g, byte b) color = edge ? ((byte)255, (byte)255, (byte)255)
                    : wet[index] ? ((byte)40, (byte)90, (byte)170)
                    : Relief(map, (column + 0.5f) * step, (row + 0.5f) * step, step);

                Put(pixels, (size - 1 - row) * size + column, color.r, color.g, color.b);
            }
        });

        return pixels;
    }

    public static byte[] Rivers(HeightMap map, WaterMap water, int size, RiverTint tint)
    {
        var pixels = new byte[size * size * 3];
        float step = (float)map.WorldSize / size;
        float widest = 1f, deepest = 0.5f;

        foreach (RiverPath river in water.Rivers)
        {
            foreach (RiverPoint point in river.Points)
            {
                widest = Mathf.Max(widest, point.Width);
                deepest = Mathf.Max(deepest, point.Surface - point.Bed);
            }
        }

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
            {
                (byte r, byte g, byte b) color = Relief(map, (column + 0.5f) * step, (row + 0.5f) * step, step);
                byte grey = (byte)((color.r + color.g + color.b) / 3);
                Put(pixels, (size - 1 - row) * size + column, grey, grey, grey);
            }
        });

        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                RiverPoint a = points[i], b = points[i + 1];
                float t = tint switch
                {
                    RiverTint.Width => b.Width / widest,
                    RiverTint.Depth => (b.Surface - b.Bed) / deepest,
                    _ => water.RiverOpen(b) ? 1f : 0f
                };

                (byte r, byte g, byte bl) color = tint == RiverTint.Centerline
                    ? (t > 0.5f ? ((byte)0, (byte)220, (byte)255) : ((byte)20, (byte)40, (byte)160))
                    : ((byte)(255f * t), (byte)(80f + 100f * (1f - t)), (byte)(255f * (1f - t)));

                float radius = tint == RiverTint.Centerline ? 0f : 0.5f * b.Width / step;
                Line(pixels, size, a.Position / step, b.Position / step, radius, color);
            }
        }

        return pixels;
    }

    public enum RiverTint
    {
        Centerline,
        Width,
        Depth
    }

    private static void Line(byte[] pixels, int size, Vector2 from, Vector2 to, float radius, (byte r, byte g, byte b) color)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) * 2f));
        int reach = Mathf.CeilToInt(radius);

        for (int s = 0; s <= steps; s++)
        {
            Vector2 at = Vector2.Lerp(from, to, s / (float)steps);

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (dx * dx + dz * dz > radius * radius + 0.5f)
                        continue;

                    int column = (int)at.x + dx, row = (int)at.y + dz;

                    if (column < 0 || row < 0 || column >= size || row >= size)
                        continue;

                    Put(pixels, (size - 1 - row) * size + column, color.r, color.g, color.b);
                }
            }
        }
    }

    private static bool Standing(WaterMap water, HeightMap map, float x, float z, out short owner)
    {
        return water.Covers(x, z, out owner) && map.SampleWorldSmooth(x, z) < water.LevelOf(owner);
    }

    private static (byte, byte, byte) Relief(HeightMap map, float x, float z, float step)
    {
        float here = map.SampleWorldSmooth(x, z);
        float light = Mathf.Clamp01(0.55f + ((here - map.SampleWorldSmooth(x + step, z)) + (here - map.SampleWorldSmooth(x, z + step))) / step * 0.8f);
        float tint = here / map.MaxHeight;

        return ((byte)(light * (120f + 100f * tint)), (byte)(light * (130f + 80f * tint)), (byte)(light * (100f + 90f * tint)));
    }

    private static (byte, byte, byte) Hash(int id)
    {
        uint hash = (uint)(id + 1) * 2654435761u;

        return ((byte)(40 + (hash & 0x7F)), (byte)(80 + (hash >> 8 & 0x7F)), (byte)(120 + (hash >> 16 & 0x7F)));
    }

    private static (byte, byte, byte) ShoreShade(WaterMap water, int index)
    {
        float distance = water.ShoreDistance[index];

        if (float.IsInfinity(distance) || distance > water.ShoreReach)
            return (230, 225, 210);

        byte tone = (byte)(170f + 60f * distance / Mathf.Max(1f, water.ShoreReach));
        return (tone, tone, (byte)(tone - 20));
    }

    public static byte[] Overview(HeightMap map, WaterMap water, IReadOnlyList<TerrainStampPlacement> stamps, int size)
    {
        var pixels = new byte[size * size * 3];
        float step = (float)map.WorldSize / size;

        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                float x = (column + 0.5f) * step;
                float z = (row + 0.5f) * step;

                float here = map.SampleWorldSmooth(x, z);
                float east = map.SampleWorldSmooth(x + step, z);
                float north = map.SampleWorldSmooth(x, z + step);

                float light = Mathf.Clamp01(0.55f + ((here - east) + (here - north)) / step * 0.8f);
                float tint = here / map.MaxHeight;

                byte r = (byte)(light * (120f + 100f * tint));
                byte g = (byte)(light * (130f + 80f * tint));
                byte b = (byte)(light * (100f + 90f * tint));

                if (water != null && water.IsWater(x, z, here))
                {
                    WaterKind kind = water.KindAt(x, z, here);
                    (r, g, b) = kind == WaterKind.River ? ((byte)30, (byte)150, (byte)255) : ((byte)40, (byte)100, (byte)190);
                }

                Put(pixels, (size - 1 - row) * size + column, r, g, b);
            }
        }

        foreach (TerrainStampPlacement stamp in stamps)
            Outline(pixels, size, step, stamp);

        return pixels;
    }

    private static void Outline(byte[] pixels, int size, float step, TerrainStampPlacement stamp)
    {
        byte red = stamp.Operation == TerrainStampOperation.Add ? (byte)255 : (byte)60;
        byte blue = stamp.Operation == TerrainStampOperation.Add ? (byte)60 : (byte)255;

        for (int edge = 0; edge < 4; edge++)
        {
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f - 0.5f;

                Vector2 local = edge switch
                {
                    0 => new Vector2(t * stamp.Size.x, -0.5f * stamp.Size.y),
                    1 => new Vector2(t * stamp.Size.x, 0.5f * stamp.Size.y),
                    2 => new Vector2(-0.5f * stamp.Size.x, t * stamp.Size.y),
                    _ => new Vector2(0.5f * stamp.Size.x, t * stamp.Size.y)
                };

                Vector2 world = stamp.World(local.x, local.y);
                int column = (int)(world.x / step);
                int row = (int)(world.y / step);

                if (column < 0 || row < 0 || column >= size || row >= size)
                    continue;

                Put(pixels, (size - 1 - row) * size + column, red, 40, blue);
            }
        }
    }

    public static string StampList(IReadOnlyList<TerrainStampPlacement> stamps)
    {
        var lines = new List<string>();

        foreach (TerrainStampPlacement stamp in stamps)
        {
            lines.Add($"{stamp.Name}\t{stamp.Operation}\tcentre ({stamp.Center.x:0}, {stamp.Center.y:0})\tsize {stamp.Size.x:0}x{stamp.Size.y:0} m"
                + $"\tamplitude {stamp.Amplitude:0} m\trotation {stamp.Rotation:0} deg");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static int Flip(int index, int resolution)
    {
        return (resolution - 1 - index / resolution) * resolution + index % resolution;
    }

    private static void Put(byte[] pixels, int index, byte r, byte g, byte b)
    {
        pixels[index * 3] = r;
        pixels[index * 3 + 1] = g;
        pixels[index * 3 + 2] = b;
    }
}
