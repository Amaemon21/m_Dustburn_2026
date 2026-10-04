using System;
using System.Collections.Generic;
using UnityEngine;

static class Draw
{
    public static void View(string path, HeightMap map, RoadNetwork network, List<SettlementLayout> layouts, List<PoiPlacement> placements,
        Vector2 center, float span, int size, bool drawLots, RoadNetworkReport report = null)
    {
        var canvas = new Canvas(size, size);

        float scale = size / span;
        float originX = center.x - span * 0.5f;
        float originY = center.y - span * 0.5f;
        float cell = (float)map.WorldSize / (map.Resolution - 1);
        bool detailed = span < 2000f;

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

        foreach (SettlementLayout layout in layouts)
        foreach (SettlementTile tile in layout.Tiles)
        {
            (byte r, byte g, byte b) = ColorFor(tile.District);

            if (detailed)
                Polygon(canvas, tile.Corners, (byte)(r / 2), (byte)(g / 2), (byte)(b / 2), 1f);
            else
                Fill(canvas, tile.Center, layout.TileSize, layout.AxisU, (byte)(r / 2), (byte)(g / 2), (byte)(b / 2), 0.35f);
        }

        foreach (Road road in network.Roads)
        {
            (byte r, byte g, byte b, float thickness) = Style(road.Kind, detailed);
            Polyline(canvas, road.Points, r, g, b, thickness);
        }

        foreach (Road street in network.Streets)
        {
            (byte r, byte g, byte b, float thickness) = Style(street.Kind, detailed);
            Polyline(canvas, street.Points, r, g, b, thickness);
        }

        if (drawLots)
        {
            foreach (SettlementLayout layout in layouts)
            foreach (Lot lot in layout.Lots)
                Rect(canvas, lot.Center, new Vector2(lot.Width, lot.Depth), lot.Forward, 90, 160, 255, false);
        }

        foreach (PoiPlacement placement in placements)
        {
            (byte r, byte g, byte b) = ColorFor(placement.District);
            Rect(canvas, placement.Ground, placement.Footprint, placement.Forward, r, g, b, true);
        }

        foreach (Hub hub in network.Hubs)
        {
            (byte r, byte g, byte b) = ColorFor(hub.Type);
            Circle(canvas, hub.Position, Mathf.Max(hub.Radius, 20f), r, g, b, detailed ? 1f : 2f);
        }

        foreach (RoadJunction junction in network.Junctions)
        {
            float radius = Math.Max(detailed ? 3f : 12f, 2.5f / scale);

            if (junction.Kind == RoadNodeKind.Junction && junction.Degree >= 3)
                Square(canvas, junction.Position, radius, 60, 255, 255);
            else if (junction.Kind == RoadNodeKind.Terminal)
                Square(canvas, junction.Position, radius * 0.6f, 150, 100, 50);
        }

        foreach (SettlementLayout layout in layouts)
        foreach (SettlementGateway gateway in layout.Gateways)
        {
            float arm = Math.Max(detailed ? 30f : 90f, 12f / scale);

            Line(canvas, gateway.Port, gateway.Port + gateway.Tangent * arm, 255, 60, 255, detailed ? 3f : 2f);
            Square(canvas, gateway.Port, Math.Max(detailed ? 4f : 14f, 3f / scale), 255, 60, 255);
        }

        if (report != null)
        {
            foreach ((Vector2 point, string _) in report.Problems)
                Circle(canvas, point, Math.Max(detailed ? 10f : 40f, 6f / scale), 255, 0, 0, 2f);
        }

        canvas.Save(path);

        float PixelX(float wx) => (wx - originX) * scale;
        float PixelY(float wy) => (wy - originY) * scale;

        void Line(Canvas c, Vector2 from, Vector2 to, byte r, byte g, byte b, float thickness)
        {
            c.Line(PixelX(from.x), PixelY(from.y), PixelX(to.x), PixelY(to.y), r, g, b, thickness);
        }

        void Polyline(Canvas c, Vector2[] points, byte r, byte g, byte b, float thickness)
        {
            if (points == null || points.Length < 2) return;

            for (int i = 0; i < points.Length - 1; i++)
                c.Line(PixelX(points[i].x), PixelY(points[i].y), PixelX(points[i + 1].x), PixelY(points[i + 1].y), r, g, b, thickness);
        }

        void Polygon(Canvas c, Vector2[] corners, byte r, byte g, byte b, float thickness)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 a = corners[i], e = corners[(i + 1) % corners.Length];
                c.Line(PixelX(a.x), PixelY(a.y), PixelX(e.x), PixelY(e.y), r, g, b, thickness);
            }
        }

        void Fill(Canvas c, Vector2 middle, float side, Vector2 axisU, byte r, byte g, byte b, float alpha)
        {
            Vector2 axisV = new(-axisU.y, axisU.x);
            int steps = (int)Math.Max(2f, side * scale);

            for (int i = 0; i <= steps; i++)
            for (int j = 0; j <= steps; j++)
            {
                Vector2 p = middle + axisU * ((i / (float)steps - 0.5f) * side) + axisV * ((j / (float)steps - 0.5f) * side);
                c.Blend((int)PixelX(p.x), (int)PixelY(p.y), r, g, b, alpha);
            }
        }

        void Square(Canvas c, Vector2 middle, float half, byte r, byte g, byte b)
        {
            int radius = (int)Math.Max(1f, half * scale);
            int x = (int)PixelX(middle.x);
            int y = (int)PixelY(middle.y);

            for (int oy = -radius; oy <= radius; oy++)
            for (int ox = -radius; ox <= radius; ox++)
                c.Set(x + ox, y + oy, r, g, b);
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

        void Circle(Canvas c, Vector2 middle, float radius, byte r, byte g, byte b, float thickness)
        {
            for (int i = 0; i < 90; i++)
            {
                float t0 = i / 90f * MathF.PI * 2f, t1 = (i + 1) / 90f * MathF.PI * 2f;
                c.Line(PixelX(middle.x + MathF.Cos(t0) * radius), PixelY(middle.y + MathF.Sin(t0) * radius),
                       PixelX(middle.x + MathF.Cos(t1) * radius), PixelY(middle.y + MathF.Sin(t1) * radius), r, g, b, thickness);
            }
        }
    }

    public static void Mask(string path, float[] mask, int resolution, int size)
    {
        var pixels = new byte[size * size * 3];
        float step = (resolution - 1) / (float)size;

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                int fromX = (int)(px * step);
                int fromY = (int)(py * step);
                int toX = Math.Min(resolution - 1, (int)((px + 1) * step));
                int toY = Math.Min(resolution - 1, (int)((py + 1) * step));
                float value = 0f;

                for (int y = fromY; y <= toY; y++)
                for (int x = fromX; x <= toX; x++)
                    value = Math.Max(value, mask[y * resolution + x]);

                byte tone = (byte)Math.Clamp(value * 255f, 0f, 255f);
                int target = ((size - 1 - py) * size + px) * 3;

                pixels[target] = tone;
                pixels[target + 1] = tone;
                pixels[target + 2] = tone;
            }
        }

        Png.Write(path, pixels, size, size);
    }

    static float Sample(HeightMap map, float wx, float wy)
    {
        return map.SampleWorld(new Vector3(wx, 0f, wy));
    }

    static (byte, byte, byte, float) Style(RoadKind kind, bool detailed) => kind switch
    {
        RoadKind.Highway => ((byte)235, (byte)80, (byte)40, detailed ? 4f : 2f),
        RoadKind.Arterial => ((byte)250, (byte)205, (byte)50, detailed ? 3f : 1f),
        RoadKind.LocalStreet => ((byte)250, (byte)240, (byte)170, detailed ? 1.5f : 0f),
        _ => ((byte)170, (byte)120, (byte)60, detailed ? 2f : 1f)
    };

    static (byte, byte, byte) ColorFor(DistrictType district) => district switch
    {
        DistrictType.Downtown => ((byte)255, (byte)120, (byte)220),
        DistrictType.Commercial => ((byte)90, (byte)170, (byte)255),
        DistrictType.Residential => ((byte)120, (byte)230, (byte)130),
        DistrictType.Industrial => ((byte)255, (byte)160, (byte)60),
        _ => ((byte)200, (byte)200, (byte)255)
    };

    static (byte, byte, byte) ColorFor(SettlementType type) => type switch
    {
        SettlementType.City => ((byte)255, (byte)60, (byte)60),
        SettlementType.Town => ((byte)255, (byte)170, (byte)40),
        SettlementType.CountryTown => ((byte)80, (byte)200, (byte)255),
        _ => ((byte)190, (byte)190, (byte)190)
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
