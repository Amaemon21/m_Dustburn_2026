using System;
using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;

public static class WorldGenPerfSettings
{
    public static string Profile => Text("WORLDGENPERF_PROFILE", "S512");

    public static int Warmup => Number("WORLDGENPERF_WARMUP", 1);

    public static int Samples => Number("WORLDGENPERF_SAMPLES", 3);

    public static string Filter => Text("WORLDGENPERF_FILTER", null);

    public static string Text(string name, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    public static int Number(string name, int fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    public static IEnumerable<TestCaseData> Cases(params string[] modes)
    {
        return Build(false, modes);
    }

    public static IEnumerable<TestCaseData> CoroutineCases(params string[] modes)
    {
        return Build(true, modes);
    }

    private static IEnumerable<TestCaseData> Build(bool coroutine, string[] modes)
    {
        if (!WorldGenBridge.Available)
        {
            yield return Expect(new TestCaseData("BRIDGE", string.Empty, "bridge"), coroutine)
                .SetName("bridge unavailable")
                .SetCategory("WorldGen.Broken");

            yield break;
        }

        string filter = Filter;

        foreach (WorldGenCase entry in WorldGenBridge.Catalog())
        {
            if (Array.IndexOf(modes, entry.Mode) < 0)
                continue;

            if (filter != null && !entry.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            WorldGenCase spec = entry;

            foreach (string caseArgs in spec.Cases)
            {
                yield return Expect(new TestCaseData(spec.Id, caseArgs, spec.Op), coroutine)
                    .SetName($"{spec.Id} {spec.Op} {(caseArgs.Length == 0 ? "default" : caseArgs)}")
                    .SetCategory("WorldGen." + spec.Priority)
                    .SetCategory("WorldGen." + spec.Mode)
                    .SetCategory("WorldGen." + spec.Id.Substring(0, spec.Id.IndexOf('-')));
            }
        }
    }

    private static TestCaseData Expect(TestCaseData data, bool coroutine)
    {
        return coroutine ? data.Returns(null) : data;
    }

    public static void Report(string catalogId, string caseArgs, string result)
    {
        WorldGenBridge.Record(catalogId, caseArgs, result);

        string status = WorldGenBridge.Status(result);
        string error = WorldGenBridge.Error(result);
        double p50 = WorldGenBridge.Number(result, "p50");

        Publish(catalogId, caseArgs, result, p50);

        TestContext.WriteLine($"{catalogId} {caseArgs} -> {status}{(error == null ? string.Empty : ": " + error)}");

        switch (status)
        {
            case "passed":
                Assert.Pass($"{catalogId} p50 {p50:0.###} ms");
                break;

            case "blocked_dependency":
            case "blocked_resource":
            case "not_run":
                Assert.Ignore($"{catalogId} {status}: {error}");
                break;

            default:
                Assert.Fail($"{catalogId} {status}: {error}");
                break;
        }
    }

    private static void Publish(string catalogId, string caseArgs, string result, double p50)
    {
        if (double.IsNaN(p50))
            return;

        string name = catalogId + (caseArgs.Length == 0 ? string.Empty : " " + caseArgs);

        try
        {
            Unity.PerformanceTesting.Measure.Custom(
                new Unity.PerformanceTesting.SampleGroup(name, Unity.PerformanceTesting.SampleUnit.Millisecond), p50);
        }
        catch (Exception exception)
        {
            TestContext.WriteLine("performance package unavailable: " + exception.Message);
        }
    }
}
