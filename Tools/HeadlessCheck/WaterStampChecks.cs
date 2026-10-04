using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

static partial class WaterStampChecks
{
    public static void RunImport()
    {
        var clock = Stopwatch.StartNew();
        WaterStampManifest manifest = WaterStampLoader.Manifest();

        StampChecks.Expect(manifest != null, "no water stamp manifest");

        if (manifest == null)
            return;

        List<string> errors = manifest.Validate();
        StampChecks.Expect(errors.Count == 0, "manifest errors: " + string.Join("; ", errors));
        StampChecks.Expect(manifest.stamps.Length == 26, $"the manifest lists {manifest.stamps.Length} stamps, the pack has 26");

        int rivers = 0, lakes = 0, ponds = 0, hashes = 0;

        foreach (WaterStampManifestEntry entry in manifest.stamps)
        {
            entry.TryKind(out WaterStampKind kind);

            if (kind == WaterStampKind.River)
                rivers++;
            else if (kind == WaterStampKind.Lake)
                lakes++;
            else
                ponds++;

            byte[] bytes = File.ReadAllBytes($"{WaterStampLoader.PACK}/{entry.mask_raw16}");
            StampChecks.Expect(bytes.Length == 2097152, $"{entry.name}: RAW16 is {bytes.Length} bytes");

            if (WaterStampManifest.Sha256(bytes) == entry.sha256_raw16)
                hashes++;
            else
                StampChecks.Expect(false, $"{entry.name}: SHA-256 does not match the manifest");
        }

        StampChecks.Expect(rivers == 16 && lakes == 8 && ponds == 2, $"{rivers} rivers, {lakes} lakes, {ponds} ponds instead of 16, 8, 2");

        CheckValidation(manifest);

        WaterStampDatabase database = WaterStampLoader.Load();
        long traced = clock.ElapsedMilliseconds;

        StampChecks.Expect(database.Count == manifest.stamps.Length, $"the database holds {database.Count} of {manifest.stamps.Length} stamps");

        foreach (WaterStampDefinition definition in database.Stamps)
        {
            StampChecks.Expect(definition.IsValid, $"{definition.Name} did not import as a valid definition");

            if (definition.IsRiver)
                CheckRiver(definition);
            else
                Console.WriteLine($"  {definition.Name,-26} {definition.Category,-20} вода {definition.WaterArea / 10000f,6:0.0} га, ось {definition.WaterAxis,6:0}°, "
                    + $"{2f * definition.WaterMajor,5:0}x{2f * definition.WaterMinor,-5:0} м (σ×2)");
        }

        DrawTraces(database, "../../Temp/ws/stamp_traces");
        Console.WriteLine($"  импорт и трассировка {database.Count} штампов за {traced} мс, SHA-256 совпал у {hashes}");
        StampChecks.Finish("Импорт штампов воды", "манифест, 26 RAW16 по 2 МБ с верным SHA-256, 16 рек, 8 озёр и 2 пруда, сокеты и ветки на осях русел.");
    }

    private static void CheckValidation(WaterStampManifest manifest)
    {
        bool threw = false;

        try
        {
            WaterStampShape.Decode(new byte[2097150], 1024);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        StampChecks.Expect(threw, "a short RAW16 decoded without an error");

        long size = manifest.raw_format.file_size_bytes;
        manifest.raw_format.file_size_bytes = size - 2;
        StampChecks.Expect(manifest.Validate().Count > 0, "a wrong file_size_bytes passed validation");
        manifest.raw_format.file_size_bytes = size;

        string category = manifest.stamps[0].category;
        manifest.stamps[0].category = "Waterfall";
        StampChecks.Expect(manifest.Validate().Count > 0, "an unknown category passed validation");
        manifest.stamps[0].category = category;
    }

    private static void CheckRiver(WaterStampDefinition definition)
    {
        WaterStampShape shape = WaterStampShape.From(definition);
        Vector2[] line = definition.Centerline;
        int onCore = 0;

        foreach (Vector2 point in line)
        {
            Vector2 uv = definition.ToUv(point);

            if (shape.SampleClamped(uv.x, uv.y) >= 0.5f)
                onCore++;
        }

        StampChecks.Expect(Vector2.Distance(line[0], definition.ToStamp(definition.Entry)) < 0.01f, $"{definition.Name}: centreline does not start on the entry socket");
        StampChecks.Expect(Vector2.Distance(line[^1], definition.ToStamp(definition.Exit)) < 0.01f, $"{definition.Name}: centreline does not end on the exit socket");
        StampChecks.Expect(onCore >= line.Length * 0.97f, $"{definition.Name}: {line.Length - onCore} of {line.Length} centreline points leave the channel");

        string branches = "";

        foreach (WaterStampBranch branch in definition.Branches)
        {
            StampChecks.Expect(branch.Path.Length >= 2, $"{definition.Name}: branch {branch.Kind} has no traced path");

            if (branch.Path.Length < 2)
                continue;

            Vector2 socket = definition.ToStamp(branch.Position);
            Vector2 atSocket = branch.Kind == WaterStampBranchKind.TributaryIn ? branch.Path[0] : branch.Path[^1];
            Vector2 atJunction = branch.Kind == WaterStampBranchKind.TributaryIn ? branch.Path[^1] : branch.Path[0];

            StampChecks.Expect(Vector2.Distance(atSocket, socket) < 0.01f, $"{definition.Name}: branch {branch.Kind} does not end on its socket");
            StampChecks.Expect(Vector2.Distance(atJunction, line[branch.JunctionIndex]) < 0.01f, $"{definition.Name}: branch {branch.Kind} does not meet the main channel");

            branches += $", ветка {branch.Kind} {Length(branch.Path):0} м до точки {branch.JunctionIndex}";
        }

        Console.WriteLine($"  {definition.Name,-26} {definition.Category,-14} хорда {definition.ChordLength,4:0} м, путь {definition.PathLength,5:0} м, отклонение {definition.MaxDeviation,4:0} м, "
            + $"ядро ±{definition.MedianCoreHalfWidth,4:0.0} м, коридор ±{definition.CorridorHalfWidth,4:0} м, плечо {definition.BankShoulder:0.00}, точек {line.Length}{branches}");
    }

    private static float Length(Vector2[] line)
    {
        float length = 0f;

        for (int i = 1; i < line.Length; i++)
            length += Vector2.Distance(line[i - 1], line[i]);

        return length;
    }
}

static partial class WaterStampChecks
{
    public static void DrawTraces(WaterStampDatabase database, string directory)
    {
        const int SIZE = 512;
        Directory.CreateDirectory(directory);

        foreach (WaterStampDefinition definition in database.Stamps)
        {
            WaterStampShape shape = WaterStampShape.From(definition);
            var rgb = new byte[SIZE * SIZE * 3];

            for (int y = 0; y < SIZE; y++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float m = shape.Sample((x + 0.5f) / SIZE, (y + 0.5f) / SIZE);
                    byte grey = (byte)(40 + 150 * m);
                    int at = (y * SIZE + x) * 3;
                    rgb[at] = grey;
                    rgb[at + 1] = grey;
                    rgb[at + 2] = grey;
                }
            }

            if (definition.IsRiver)
            {
                Plot(rgb, SIZE, definition, definition.Centerline, 255, 40, 40);

                foreach (WaterStampBranch branch in definition.Branches)
                    Plot(rgb, SIZE, definition, branch.Path, 255, 220, 0);

                Dot(rgb, SIZE, definition.Entry, 0, 255, 0);
                Dot(rgb, SIZE, definition.Exit, 0, 120, 255);
            }

            Png.Write(Path.Combine(directory, definition.Name + ".png"), rgb, SIZE, SIZE);
        }
    }

    private static void Plot(byte[] rgb, int size, WaterStampDefinition definition, Vector2[] line, byte r, byte g, byte b)
    {
        foreach (Vector2 point in line)
            Dot(rgb, size, definition.ToUv(point), r, g, b, 1);
    }

    private static void Dot(byte[] rgb, int size, Vector2 uv, byte r, byte g, byte b, int radius = 4)
    {
        int cx = (int)(uv.x * (size - 1)), cy = (int)(uv.y * (size - 1));

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, y = cy + dy;

                if (x < 0 || y < 0 || x >= size || y >= size)
                    continue;

                int at = (y * size + x) * 3;
                rgb[at] = r;
                rgb[at + 1] = g;
                rgb[at + 2] = b;
            }
        }
    }
}
