#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WorldGenBenchBatch
{
    public static void Preflight()
    {
        int exit = 0;

        try
        {
            WorldGenBenchPaths.CleanRoot();
            WorldGenBenchFixture fixture = WorldGenBenchFixtureBuilder.BuildFixture();
            WorldGenBenchRecorder.Note("preflight fixture: " + fixture.Describe());
            WorldGenBenchRecorder.Write("preflight.json", WorldGenBench.Environment());
            WorldGenBenchRecorder.Write("catalog.csv", WorldGenBenchCatalog.Csv());
            WorldGenBenchRecorder.Write("operations.txt", WorldGenBench.Operations());

            if (!fixture.HasSources)
            {
                WorldGenBenchRecorder.Note("preflight failed: source assets are missing");
                exit = 2;
            }

            if (!fixture.HasBake)
                WorldGenBenchRecorder.Note("preflight warning: no valid BakedWorld, runtime scenarios will report blocked_dependency");

            Debug.Log("WorldGenPerf preflight complete: " + fixture.Describe());
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WorldGenBenchRecorder.Note("preflight exception: " + exception);
            exit = 3;
        }

        Quit(exit);
    }

    public static void BuildScene()
    {
        int exit = 0;

        try
        {
            string path = WorldGenBenchFixtureBuilder.CreateScene();
            WorldGenBenchRecorder.Note("benchmark scene: " + path);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exit = 3;
        }

        Quit(exit);
    }

    public static void BuildPlayer()
    {
        int exit = 0;

        try
        {
            WorldGenBenchFixtureBuilder.CreateScene();

            string output = Argument("-worldGenPerfPlayer") ?? Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "BenchmarkResults", "Player", "WorldGenPerf.exe"));

            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            WorldGenBenchRecorder.Note($"player build {report.summary.result} at {output}, {report.summary.totalSize} bytes");

            if (report.summary.result != BuildResult.Succeeded)
                exit = 4;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exit = 3;
        }

        Quit(exit);
    }

    public static void Kernels()
    {
        int exit = 0;

        try
        {
            WorldGenBenchFixtureBuilder.BuildFixture();

            string profile = Argument("-worldGenPerfProfile") ?? "S512";
            string filter = Argument("-worldGenPerfFilter");
            int warmup = int.TryParse(Argument("-worldGenPerfWarmup"), out int parsedWarmup) ? parsedWarmup : 1;
            int samples = int.TryParse(Argument("-worldGenPerfSamples"), out int parsedSamples) ? parsedSamples : 3;

            var failures = new List<string>();

            foreach (WorldGenBenchSpec spec in WorldGenBenchCatalog.All)
            {
                if (spec.Mode == "PL" || spec.Mode == "PM")
                    continue;

                if (filter != null && !spec.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (string caseArgs in spec.Cases)
                {
                    string result = WorldGenBench.Run(spec.Op, profile, caseArgs, warmup, samples);
                    WorldGenBenchRecorder.Record(spec.Id, caseArgs, result);

                    if (result.Contains("\"status\":\"failed\""))
                        failures.Add(spec.Id + " " + caseArgs);
                }
            }

            WorldGenBenchRecorder.Note($"kernels finished with {failures.Count} failures");

            foreach (string failure in failures)
                WorldGenBenchRecorder.Note("failed: " + failure);

            if (failures.Count > 0)
                exit = 5;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            exit = 3;
        }

        Quit(exit);
    }

    private static string Argument(string name)
    {
        string[] arguments = System.Environment.GetCommandLineArgs();

        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }

        return null;
    }

    private static void Quit(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }
}
#endif
