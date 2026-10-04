using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static partial class WaterDebugImages
{
    private static readonly (byte, byte, byte) MacroColor = (235, 235, 235);
    private static readonly (byte, byte, byte) StampedRiverColor = (0, 230, 255);
    private static readonly (byte, byte, byte) ProceduralRiverColor = (255, 220, 0);
    private static readonly (byte, byte, byte) RiverStampColor = (255, 140, 0);
    private static readonly (byte, byte, byte) LakeStampColor = (230, 60, 230);
    private static readonly (byte, byte, byte) PondStampColor = (60, 220, 90);
    private static readonly (byte, byte, byte) EntryColor = (0, 255, 0);
    private static readonly (byte, byte, byte) ExitColor = (255, 30, 30);
    private static readonly (byte, byte, byte) BranchColor = (255, 255, 0);
    private static readonly (byte, byte, byte) RejectionColor = (255, 0, 160);

    public static byte[] Stamps(HeightMap map, WaterMap water, WaterStampLibrary library, int size, Vector2 origin, float extent)
    {
        var pixels = new byte[size * size * 3];
        float step = extent / size;
        WaterStampLayout layout = water.Stamps;

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (int column = 0; column < size; column++)
            {
                float x = origin.x + (column + 0.5f) * step;
                float z = origin.y + (row + 0.5f) * step;
                float ground = map.SampleWorldSmooth(x, z);
                (byte r, byte g, byte b) color = Relief(map, x, z, step);

                if (water.IsWater(x, z, ground))
                    color = water.KindAt(x, z, ground) == WaterKind.River ? ((byte)30, (byte)120, (byte)220) : ((byte)40, (byte)90, (byte)170);

                Put(pixels, (size - 1 - row) * size + column, color.r, color.g, color.b);
            }
        });

        if (layout == null)
            return pixels;

        Vector2 Pixel(Vector2 world) => (world - origin) / step;

        foreach (Vector2[] macro in layout.MacroRivers)
        {
            for (int i = 0; i + 1 < macro.Length; i++)
                Line(pixels, size, Pixel(macro[i]), Pixel(macro[i + 1]), 0f, MacroColor);
        }

        foreach (WaterStampPlacement placement in layout.Placements)
        {
            (byte, byte, byte) color = placement.Kind switch
            {
                WaterStampKind.River => RiverStampColor,
                WaterStampKind.Lake => LakeStampColor,
                _ => PondStampColor
            };

            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 from = new((corner == 1 || corner == 2 ? 1f : 0f) * placement.Size.x, (corner >= 2 ? 1f : 0f) * placement.Size.y);
                Vector2 to = new((corner == 0 || corner == 1 ? 1f : 0f) * placement.Size.x, (corner == 1 || corner == 2 ? 1f : 0f) * placement.Size.y);
                Line(pixels, size, Pixel(placement.World(from)), Pixel(placement.World(to)), 0f, color);
            }

            if (library == null || placement.Kind != WaterStampKind.River)
                continue;

            WaterStampDefinition definition = library.Get(placement.StampIndex);
            Vector2[] centerline = definition.Centerline;
            int last = Mathf.Clamp(placement.Last, 0, centerline.Length - 1);

            for (int k = placement.First; k < last; k++)
                Line(pixels, size, Pixel(placement.World(centerline[k])), Pixel(placement.World(centerline[k + 1])), 0f, RiverStampColor);

            foreach (WaterStampBranch branch in definition.Branches)
            {
                for (int k = 0; k + 1 < branch.Path.Length; k++)
                    Line(pixels, size, Pixel(placement.World(branch.Path[k])), Pixel(placement.World(branch.Path[k + 1])), 0f, RiverStampColor);

                if (branch.Path.Length > 0)
                    Line(pixels, size, Pixel(placement.World(definition.ToStamp(branch.Position))), Pixel(placement.World(definition.ToStamp(branch.Position))), 3f, BranchColor);
            }

            Line(pixels, size, Pixel(placement.World(centerline[placement.First])), Pixel(placement.World(centerline[placement.First])), 3f, EntryColor);
            Line(pixels, size, Pixel(placement.World(centerline[last])), Pixel(placement.World(centerline[last])), 3f, ExitColor);
        }

        for (int river = 0; river < water.Rivers.Count; river++)
        {
            List<RiverPoint> points = water.Rivers[river].Points;
            WaterStampTrack track = river < layout.Tracks.Count ? layout.Tracks[river] : null;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                bool stamped = track != null && track.Stamped(i);
                Line(pixels, size, Pixel(points[i].Position), Pixel(points[i + 1].Position), 0f, stamped ? StampedRiverColor : ProceduralRiverColor);
            }
        }

        foreach (WaterStampRejection rejection in layout.Rejections)
        {
            Vector2 at = Pixel(rejection.Position);
            Line(pixels, size, at - new Vector2(2f, 2f), at + new Vector2(2f, 2f), 0f, RejectionColor);
            Line(pixels, size, at - new Vector2(2f, -2f), at + new Vector2(2f, -2f), 0f, RejectionColor);
        }

        foreach (Vector2 fallback in layout.FallbackPoints)
            Line(pixels, size, Pixel(fallback), Pixel(fallback), 2f, ExitColor);

        return pixels;
    }

    public static byte[] StampMasks(WaterMap water, WaterStampLibrary library, int size)
    {
        var pixels = new byte[size * size * 3];
        WaterStampLayout layout = water.Stamps;

        if (layout == null || library == null)
            return pixels;

        float step = water.WorldSize / size;

        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            var world = new Vector2(0f, (row + 0.5f) * step);

            for (int column = 0; column < size; column++)
            {
                world.x = (column + 0.5f) * step;
                float river = 0f, lake = 0f;

                foreach (WaterStampPlacement placement in layout.Placements)
                {
                    Vector2 uv = placement.Uv(placement.Stamp(world));

                    if (uv.x < 0f || uv.y < 0f || uv.x > 1f || uv.y > 1f)
                        continue;

                    float mask = library.Shape(placement.StampIndex).Sample(uv.x, uv.y);

                    if (placement.Kind == WaterStampKind.River)
                        river = Mathf.Max(river, mask);
                    else
                        lake = Mathf.Max(lake, mask);
                }

                Put(pixels, (size - 1 - row) * size + column, (byte)(255f * river), (byte)(90f * Mathf.Max(river, lake)), (byte)(255f * lake));
            }
        });

        return pixels;
    }

    public static string StampReport(WaterStampLayout layout)
    {
        if (layout == null)
            return "Water stamps are off.";

        var lines = new List<string> { layout.Summary(), "" };
        CultureInfo invariant = CultureInfo.InvariantCulture;

        for (int i = 0; i < layout.Placements.Count; i++)
        {
            WaterStampPlacement placement = layout.Placements[i];
            Vector2 center = placement.Center;
            string owner = placement.Kind == WaterStampKind.River ? $"river {placement.River} points {placement.First}..{placement.Last}" : $"body {placement.Body}";

            lines.Add(string.Format(invariant, "{0}\t{1}\t{2}\t{3}\tcentre ({4:0}, {5:0})\tscale {6:0.00}\trotation {7:0}\tmirror {8}", i, placement.Name, placement.Category, owner,
                center.x, center.y, placement.Scale, placement.Rotation, placement.Mirror));
        }

        if (layout.Rejections.Count > 0)
        {
            lines.Add("");
            lines.Add($"rejected candidates: {layout.Rejections.Count}");

            foreach (WaterStampRejection rejection in layout.Rejections)
                lines.Add(string.Format(invariant, "({0:0}, {1:0})\t{2}\t{3}", rejection.Position.x, rejection.Position.y, rejection.Name, rejection.Reason));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
