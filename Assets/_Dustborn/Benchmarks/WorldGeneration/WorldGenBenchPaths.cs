using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public static class WorldGenBenchPaths
{
    public const string ROOT = "Assets/WorldGenPerfTemp";

    private static readonly List<string> OWNED = new();
    private static string _runId;

    public static string RunId => _runId ??= DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

    public static string Directory => ROOT + "/" + RunId;

    public static string Temporary(string fileName)
    {
        string path = Directory + "/" + fileName;
        Ensure();
        Own(path);
        return path;
    }

    public static string Output(string fileName)
    {
        string root = Path.Combine(Application.dataPath, "..", "BenchmarkResults", RunId);
        System.IO.Directory.CreateDirectory(root);
        return Path.GetFullPath(Path.Combine(root, fileName));
    }

    public static void Own(string path)
    {
        if (!OWNED.Contains(path))
            OWNED.Add(path);
    }

    public static void Ensure()
    {
        System.IO.Directory.CreateDirectory(Path.GetFullPath(Directory));
        WriteManifest();
    }

    public static void Clean()
    {
#if UNITY_EDITOR
        foreach (string path in OWNED)
        {
            if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null)
                UnityEditor.AssetDatabase.DeleteAsset(path);
        }

        OWNED.Clear();

        string full = Path.GetFullPath(Directory);

        if (System.IO.Directory.Exists(full) && System.IO.Directory.GetFiles(full, "*", SearchOption.AllDirectories).Length <= 1)
        {
            UnityEditor.AssetDatabase.DeleteAsset(Directory);
            UnityEditor.AssetDatabase.Refresh();
        }
#else
        OWNED.Clear();
#endif
    }

    public static void CleanRoot()
    {
        OWNED.Clear();

#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(ROOT) == null && !System.IO.Directory.Exists(Path.GetFullPath(ROOT)))
            return;

        UnityEditor.AssetDatabase.DeleteAsset(ROOT);
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    private static void WriteManifest()
    {
        string manifest = Path.Combine(Path.GetFullPath(Directory), "ownership.txt");

        var text = new StringBuilder();
        text.AppendLine("Owner: WorldGenPerf harness");
        text.AppendLine("RunId: " + RunId);
        text.AppendLine("Created: " + DateTime.UtcNow.ToString("O"));
        text.AppendLine("Rule: only files listed below are deleted by the harness.");

        foreach (string path in OWNED)
            text.AppendLine(path);

        File.WriteAllText(manifest, text.ToString());
    }
}
