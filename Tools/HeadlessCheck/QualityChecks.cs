using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public static class QualityChecks
{
    private const string CONTENT = "../../Assets/_Dustborn/Content/World";

    public static void Run()
    {
        var mismatches = new List<string>();
        int compared = 0;

        compared += Compare<WorldGenerationConfig>($"{CONTENT}/WorldGenerationConfig.asset", mismatches);
        compared += Compare<VoxelConfig>($"{CONTENT}/VoxelConfig.asset", mismatches);
        compared += Compare<WorldBuildSettings>($"{CONTENT}/WorldBuildSettings.asset", mismatches);

        foreach (string mismatch in mismatches)
            Console.WriteLine($"  расходится {mismatch}");

        Console.WriteLine($"качество мира: сравнено полей {compared}, расхождений ассета с кодом {mismatches.Count}");

        if (compared == 0)
            throw new InvalidOperationException("No field carries the Quality attribute.");

        if (mismatches.Count > 0)
            throw new InvalidOperationException($"{mismatches.Count} quality fields differ between the assets in Content/World and the code defaults.");
    }

    private static int Compare<T>(string path, List<string> mismatches) where T : new()
    {
        T asset = AssetReader.Load<T>(path);
        var defaults = new T();
        string text = File.ReadAllText(path);
        int compared = 0;

        foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.GetCustomAttribute<QualityAttribute>() == null)
                continue;

            compared++;

            if (!text.Contains($"\n  {field.Name}:"))
            {
                mismatches.Add($"{typeof(T).Name}.{Name(field)}: в ассете нет этого поля");
                continue;
            }

            object stored = field.GetValue(asset);
            object coded = field.GetValue(defaults);

            if (!Equals(stored, coded))
                mismatches.Add($"{typeof(T).Name}.{Name(field)}: в ассете {stored}, в коде {coded}");
        }

        return compared;
    }

    private static string Name(FieldInfo field)
    {
        string name = field.Name;
        int end = name.IndexOf(">k__BackingField", StringComparison.Ordinal);

        return name.StartsWith("<") && end > 0 ? name.Substring(1, end - 1) : name;
    }
}
