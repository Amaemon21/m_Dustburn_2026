using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class WorldGenBench
{
    public const int SCHEMA_VERSION = 1;
    public const string SOURCE_COMMIT = "865a6ed75bbe9a96ac8a9f5615ceb2762dd5c23c";

    private static Dictionary<string, Func<WorldGenBenchOp>> _registry;

    public static Dictionary<string, Func<WorldGenBenchOp>> Registry
    {
        get
        {
            if (_registry != null)
                return _registry;

            _registry = new Dictionary<string, Func<WorldGenBenchOp>>(StringComparer.OrdinalIgnoreCase);

            WorldGenBenchOpsPre.Register(_registry);
            WorldGenBenchOpsBio.Register(_registry);
            WorldGenBenchOpsHgt.Register(_registry);
            WorldGenBenchOpsSet.Register(_registry);
            WorldGenBenchOpsPoi.Register(_registry);
            WorldGenBenchOpsMat.Register(_registry);
            WorldGenBenchOpsIo.Register(_registry);
            WorldGenBenchOpsVox.Register(_registry);
            WorldGenBenchOpsQue.Register(_registry);
            WorldGenBenchOpsDec.Register(_registry);
            WorldGenBenchOpsLife.Register(_registry);

            foreach (Action<Dictionary<string, Func<WorldGenBenchOp>>> extra in EXTENSIONS)
                extra(_registry);

            return _registry;
        }
    }

    private static readonly List<Action<Dictionary<string, Func<WorldGenBenchOp>>>> EXTENSIONS = new();

    public static void Extend(Action<Dictionary<string, Func<WorldGenBenchOp>>> register)
    {
        if (EXTENSIONS.Contains(register))
            return;

        EXTENSIONS.Add(register);
        _registry = null;
    }

    public static string Operations()
    {
        var names = new List<string>(Registry.Keys);
        names.Sort(StringComparer.Ordinal);
        return string.Join("\n", names);
    }

    public static string Run(string opId, string profileId, string args, int warmup, int samples)
    {
        var parsed = new WorldGenBenchArgs(args);
        WorldGenBenchProfile profile = null;

        try
        {
            if (!Registry.TryGetValue(opId ?? string.Empty, out Func<WorldGenBenchOp> factory))
                return Failure(opId, profileId, args, "not_run", $"Unknown operation '{opId}'.");

            profile = WorldGenBenchProfile.Create(profileId, parsed);
            WorldGenBenchOp op = factory();

            return WorldGenBenchSampler.Run(op, profile, parsed, Mathf.Max(0, warmup), Mathf.Max(1, samples));
        }
        catch (WorldGenBenchBlockedException exception)
        {
            return Failure(opId, profileId, args, exception.Status, exception.Message);
        }
        catch (Exception exception)
        {
            return Failure(opId, profileId, args, "failed", exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            WorldGenBenchFixtures.Release();
            profile?.Release();
        }
    }

    public static string Environment()
    {
        var json = new WorldGenBenchJson();

        json.Value("schemaVersion", SCHEMA_VERSION)
            .Text("sourceCommit", SOURCE_COMMIT)
            .Text("unityVersion", Application.unityVersion)
            .Text("platform", Application.platform.ToString())
            .Text("dataPath", Application.dataPath)
            .Value("isPlaying", Application.isPlaying)
            .Value("isEditor", Application.isEditor)
            .Text("graphicsDevice", SystemInfo.graphicsDeviceType.ToString())
            .Text("graphicsName", SystemInfo.graphicsDeviceName)
            .Text("graphicsVersion", SystemInfo.graphicsDeviceVersion)
            .Text("processor", SystemInfo.processorType)
            .Value("processorCount", SystemInfo.processorCount)
            .Value("processorFrequency", SystemInfo.processorFrequency)
            .Value("systemMemoryMB", SystemInfo.systemMemorySize)
            .Value("graphicsMemoryMB", SystemInfo.graphicsMemorySize)
            .Text("operatingSystem", SystemInfo.operatingSystem)
            .Text("deviceModel", SystemInfo.deviceModel)
            .Value("burstEnabled", Unity.Burst.BurstCompiler.IsEnabled)
            .Value("targetFrameRate", Application.targetFrameRate)
            .Value("vSyncCount", QualitySettings.vSyncCount)
            .Text("qualityLevel", QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : null)
            .Value("jobWorkerCount", Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount)
            .Value("maxJobThreadCount", Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount)
            .Value("stopwatchFrequency", System.Diagnostics.Stopwatch.Frequency)
            .Value("stopwatchHighResolution", System.Diagnostics.Stopwatch.IsHighResolution)
            .Value("gcIncremental", UnityEngine.Scripting.GarbageCollector.isIncremental)
            .Value("developmentBuild", Debug.isDebugBuild)
            .Text("scriptingBackend", ScriptingBackend());

        WorldGenBenchFixture fixture = WorldGenBenchFixtures.Resolve();

        if (fixture == null)
        {
            json.Null("fixture");
        }
        else
        {
            json.Object("fixture")
                .Text("describe", fixture.Describe())
                .Value("hasSources", fixture.HasSources)
                .Value("hasBake", fixture.HasBake)
                .End();
        }

        return json.ToString();
    }

    public static string Catalog()
    {
        return WorldGenBenchCatalog.Csv();
    }

    private static string Failure(string opId, string profileId, string args, string status, string message)
    {
        var json = new WorldGenBenchJson();

        json.Text("op", opId)
            .Text("profile", profileId)
            .Text("args", args)
            .Null("fixtureHash")
            .Value("warmup", 0)
            .Value("requestedSamples", 0)
            .Text("status", status)
            .Text("error", message)
            .Object("wallMs").Value("count", 0).Null("p50").Null("max").End()
            .Array("samplesMs").End()
            .Object("work").End()
            .Object("fingerprints").End()
            .Array("notes").End();

        return json.ToString();
    }

    private static string ScriptingBackend()
    {
#if ENABLE_IL2CPP
        return "IL2CPP";
#elif ENABLE_MONO
        return "Mono";
#else
        return "unknown";
#endif
    }

    public static string Describe(string prefix)
    {
        var text = new StringBuilder();

        foreach (string name in Operations().Split('\n'))
        {
            if (prefix == null || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text.AppendLine(name);
        }

        return text.ToString();
    }
}
