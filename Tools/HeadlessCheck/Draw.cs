using System;
using System.Collections.Generic;
using UnityEngine;

static class Draw
{
    public static void View(string path, HeightMap map, RoadNetwork network, List<CityLayout> layouts, List<PoiPlacement> placements,
        Vector2 center, float span, int size, bool drawLots)
    {
        var canvas = new Canvas(size, size);

        float scale = size / span;
        float originX = center.x - span * 0.5f;
        float originY = center.y - span * 0.5f;
        float cell = (float)map.WorldSize / (map.Resolution - 1);

        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            float wx = originX + (px + 0.5f) / scale;
            float wy = originY + (py + 0.5f) / scale;

            float h = Sample(map, wx, wy);
            float hx = Sample(map, wx + cell, wy);
            float hy = Sample(map, wx, wy + cell);

            float nx = (h - hx) / cell, ny = (h - hy) / cell;
            float light = Math.Clamp((nx * 0.55f + ny * 0.55f + 1f) * 0.5f, 0f, 1f);
            float tone = 0.25f + 0.55f * (h / map.MaxHeight);

            byte v = (byte)Math.Clamp((0.35f + 0.9f * light) * tone * 255f, 0f, 255f);
            canvas.Set(px, py, v, (byte)(v * 0.97f), (byte)(v * 0.9f));
        }

        foreach (Road road in network.Roads)
            Polyline(canvas, road.Points, 220, 60, 40, span < 2000f ? 3f : 1f);

        foreach (CityLayout layout in layouts)
        foreach (Road street in layout.Streets)
            Polyline(canvas, street.Points, 250, 210, 60, span < 2000f ? 1f : 0f);

        if (drawLots)
        {
            foreach (CityLayout layout in layouts)
            foreach (Lot lot in layout.Lots)
                Rect(canvas, lot.Center, new Vector2(lot.Width, lot.Depth), lot.Forward, 90, 160, 255, false);
        }

        foreach (PoiPlacement placement in placements)
        {
            (byte r, byte g, byte b) = ColorFor(placement.District);
            Rect(canvas, placement.Ground, placement.Footprint, placement.Forward, r, g, b, true);
        }

        foreach (Hub hub in network.Hubs)
            { (byte tr, byte tg, byte tb) = TierColor(hub.Tier); Circle(canvas, hub.Position, hub.Radius * 1.35f, tr, tg, tb); }

        canvas.Save(path);

        float PixelX(float wx) => (wx - originX) * scale;
        float PixelY(float wy) => (wy - originY) * scale;

        void Polyline(Canvas c, Vector2[] points, byte r, byte g, byte b, float thickness)
        {
            if (points == null || points.Length < 2) return;

            for (int i = 0; i < points.Length - 1; i++)
                c.Line(PixelX(points[i].x), PixelY(points[i].y), PixelX(points[i + 1].x), PixelY(points[i + 1].y), r, g, b, thickness);
        }

        void Rect(Canvas c, Vector2 middle, Vector2 sizeXY, Vector2 forward, byte r, byte g, byte b, bool fill)
        {
            Vector2 right = new(forward.y, -forward.x);
            float hw = sizeXY.x * 0.5f, hd = sizeXY.y * 0.5f;

            Vector2[] corners =
            {
                middle + right * -hw + forward * -hd,
                middle + right * hw + forward * -hd,
                middle + right * hw + forward * hd,
                middle + right * -hw + forward * hd
            };

            if (fill)
            {
                int steps = (int)Math.Max(4f, sizeXY.x * scale);
                int depth = (int)Math.Max(4f, sizeXY.y * scale);

                for (int i = 0; i <= steps; i++)
                for (int j = 0; j <= depth; j++)
                {
                    float u = (i / (float)steps - 0.5f) * sizeXY.x;
                    float v = (j / (float)depth - 0.5f) * sizeXY.y;
                    Vector2 p = middle + right * u + forward * v;
                    c.Blend((int)PixelX(p.x), (int)PixelY(p.y), r, g, b, 0.85f);
                }
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2 a = corners[i], e = corners[(i + 1) % 4];
                c.Line(PixelX(a.x), PixelY(a.y), PixelX(e.x), PixelY(e.y), r, g, b);
            }

            if (!fill) return;

            c.Line(PixelX(corners[3].x), PixelY(corners[3].y), PixelX(corners[2].x), PixelY(corners[2].y), 255, 255, 255);
        }

        void Circle(Canvas c, Vector2 middle, float radius, byte r, byte g, byte b)
        {
            for (int i = 0; i < 360; i++)
            {
                float t0 = i / 360f * MathF.PI * 2f, t1 = (i + 1) / 360f * MathF.PI * 2f;
                c.Line(PixelX(middle.x + MathF.Cos(t0) * radius), PixelY(middle.y + MathF.Sin(t0) * radius),
                       PixelX(middle.x + MathF.Cos(t1) * radius), PixelY(middle.y + MathF.Sin(t1) * radius), r, g, b);
            }
        }
    }

    static float Sample(HeightMap map, float wx, float wy)
    {
        return map.SampleWorld(new Vector3(wx, 0f, wy));
    }

    static (byte, byte, byte) TierColor(SettlementTier tier) => tier switch
    {
        SettlementTier.City => ((byte)60, (byte)255, (byte)200),
        SettlementTier.Town => ((byte)255, (byte)230, (byte)90),
        _ => ((byte)255, (byte)140, (byte)255)
    };

    static (byte, byte, byte) ColorFor(DistrictType district) => district switch
    {
        DistrictType.Downtown => ((byte)255, (byte)120, (byte)220),
        DistrictType.Residential => ((byte)120, (byte)230, (byte)130),
        DistrictType.Industrial => ((byte)255, (byte)160, (byte)60),
        _ => ((byte)200, (byte)200, (byte)255)
    };

    public static void Biomes(string path, BiomeMap map, BiomeDatabase biomes)
    {
        int resolution = map.Resolution;
        var rgb = new byte[resolution * resolution * 3];
        var palette = new byte[biomes.Count * 3];

        for (int i = 0; i < biomes.Count; i++)
        {
            (byte r, byte g, byte b) = Palette(biomes.Get(i).Type);

            palette[i * 3] = r;
            palette[i * 3 + 1] = g;
            palette[i * 3 + 2] = b;
        }

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                byte biome = map.Cells[(resolution - 1 - y) * resolution + x];
                int target = (y * resolution + x) * 3;

                rgb[target] = palette[biome * 3];
                rgb[target + 1] = palette[biome * 3 + 1];
                rgb[target + 2] = palette[biome * 3 + 2];
            }
        }

        Png.Write(path, rgb, resolution, resolution);
    }

    private static (byte, byte, byte) Palette(BiomeType type)
    {
        return type switch
        {
            BiomeType.PineForest => (0, 64, 0),
            BiomeType.BurntForest => (186, 0, 255),
            BiomeType.Desert => (255, 228, 119),
            BiomeType.Snow => (255, 255, 255),
            _ => (255, 168, 0)
        };
    }
}
