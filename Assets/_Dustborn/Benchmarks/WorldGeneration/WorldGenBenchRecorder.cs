using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class WorldGenBenchRecorder
{
    public const string OUTPUT_VARIABLE = "WORLDGENPERF_OUTPUT";
    public const string RUN_VARIABLE = "WORLDGENPERF_RUNID";
    public const string MODE_VARIABLE = "WORLDGENPERF_MODE";

    private static string _directory;
    private static bool _manifest;

    public static string Directory
    {
        get
        {
            if (_directory != null)
                return _directory;

            string configured = System.Environment.GetEnvironmentVariable(OUTPUT_VARIABLE);

            _directory = string.IsNullOrEmpty(configured)
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BenchmarkResults", RunId))
                : Path.GetFullPath(configured);

            System.IO.Directory.CreateDirectory(_directory);
            return _directory;
        }
    }

    public static string RunId
    {
        get
        {
            string configured = System.Environment.GetEnvironmentVariable(RUN_VARIABLE);
            return string.IsNullOrEmpty(configured) ? WorldGenBenchPaths.RunId : configured;
        }
    }

    public static string Mode
    {
        get
        {
            string configured = System.Environment.GetEnvironmentVariable(MODE_VARIABLE);

            if (!string.IsNullOrEmpty(configured))
                return configured;

            if (!Application.isEditor)
                return "Player";

            return Application.isPlaying ? "PlayMode" : "EditMode";
        }
    }

    public static void Record(string catalogId, string caseArgs, string resultJson)
    {
        Manifest();

        var text = new StringBuilder();
        text.Append('{')
            .Append("\"runId\":").Append(WorldGenBenchJson.Escape(RunId)).Append(',')
            .Append("\"caseId\":").Append(WorldGenBenchJson.Escape(catalogId + (string.IsNullOrEmpty(caseArgs) ? string.Empty : "[" + caseArgs + "]"))).Append(',')
            .Append("\"catalogId\":").Append(WorldGenBenchJson.Escape(catalogId)).Append(',')
            .Append("\"caseArgs\":").Append(WorldGenBenchJson.Escape(caseArgs ?? string.Empty)).Append(',')
            .Append("\"executionMode\":").Append(WorldGenBenchJson.Escape(Mode)).Append(',')
            .Append("\"utc\":").Append(WorldGenBenchJson.Escape(DateTime.UtcNow.ToString("O"))).Append(',')
            .Append("\"result\":").Append(resultJson)
            .Append('}');

        Append("results.jsonl", text.ToString());
    }

    public static void Note(string message)
    {
        Append("harness.log", DateTime.UtcNow.ToString("O") + " " + message);
    }

    public static void Write(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(Directory, fileName), content, new UTF8Encoding(false));
    }

    private static void Append(string fileName, string line)
    {
        using var stream = new FileStream(Path.Combine(Directory, fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.WriteLine(line);
    }

    private static void Manifest()
    {
        if (_manifest)
            return;

        _manifest = true;

        var json = new WorldGenBenchJson();
        json.Value("schemaVersion", WorldGenBench.SCHEMA_VERSION)
            .Text("runId", RunId)
            .Text("executionMode", Mode)
            .Text("startedUtc", DateTime.UtcNow.ToString("O"))
            .Text("sourceCommit", WorldGenBench.SOURCE_COMMIT)
            .Text("harnessCommit", System.Environment.GetEnvironmentVariable("WORLDGENPERF_HARNESS_COMMIT"))
            .Text("command", System.Environment.CommandLine);

        Write("run_manifest." + Mode + ".json", json.ToString());
        Write("environment." + Mode + ".json", WorldGenBench.Environment());
        Write("catalog.csv", WorldGenBenchCatalog.Csv());
        Write("operations.txt", WorldGenBench.Operations());
    }
}
