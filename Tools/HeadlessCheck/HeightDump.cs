using System;
using System.Globalization;
using UnityEngine;

static class HeightDump
{
    public static bool Run(string[] args, HeightMap map, WaterMap water)
    {
        int at = Array.IndexOf(args, "--dump-window");

        if (at < 0)
            return false;

        var culture = CultureInfo.InvariantCulture;
        float cx = float.Parse(args[at + 1], culture), cz = float.Parse(args[at + 2], culture), size = float.Parse(args[at + 3], culture);
        float baseline = float.Parse(args[at + 4], culture);
        float step = at + 5 < args.Length && float.TryParse(args[at + 5], NumberStyles.Float, culture, out float parsed) ? parsed : 1f;
        int n = (int)(size / step) + 1;
        Console.WriteLine($"высоты (h - {baseline}) в дм, шаг {step} м; ~ стоячая вода, = река");

        for (int j = n - 1; j >= 0; j--)
        {
            var line = new System.Text.StringBuilder();
            float z = (float)Math.Round(cz - size / 2 + j * step);
            line.Append($"{z,6:0} ");

            for (int i = 0; i < n; i++)
            {
                float x = (float)Math.Round(cx - size / 2 + i * step);
                float h = map.SampleWorldSmooth(x, z);
                char mark = water.Covers(x, z, out _) ? '~' : water.TryRiver(x, z, out _) ? '=' : ' ';
                line.Append($"{Mathf.RoundToInt((h - baseline) * 10f),4}{mark}");
            }

            Console.WriteLine(line);
        }

        return true;
    }
}
