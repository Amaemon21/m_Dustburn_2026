using System.Collections;
using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.TestTools;

public class WorldGenPlayModeTests
{
    public static IEnumerable<TestCaseData> PlayModeCases()
    {
        return WorldGenPerfSettings.Cases("PM");
    }

    public static IEnumerable<TestCaseData> RuntimeCases()
    {
        return WorldGenPerfSettings.CoroutineCases("PL");
    }

    [Test, Performance]
    [TestCaseSource(nameof(PlayModeCases))]
    public void PlayModeKernel(string catalogId, string caseArgs, string op)
    {
        if (catalogId == "BRIDGE")
            Assert.Fail(WorldGenBridge.Failure);

        string result = WorldGenBridge.Run(op, WorldGenPerfSettings.Profile, caseArgs,
            WorldGenPerfSettings.Warmup, WorldGenPerfSettings.Samples);

        WorldGenPerfSettings.Report(catalogId, caseArgs, result);
    }

    [UnityTest, Performance]
    [TestCaseSource(nameof(RuntimeCases))]
    [Timeout(1800000)]
    public IEnumerator Runtime(string catalogId, string caseArgs, string op)
    {
        if (catalogId == "BRIDGE")
        {
            Assert.Fail(WorldGenBridge.Failure);
            yield break;
        }

        WorldGenBridge.StartRuntime(op, WorldGenPerfSettings.Profile, caseArgs);

        float timeout = Time.realtimeSinceStartup + WorldGenPerfSettings.Number("WORLDGENPERF_TIMEOUT", 1500);

        while (!WorldGenBridge.RuntimeFinished() && Time.realtimeSinceStartup < timeout)
            yield return null;

        string result = WorldGenBridge.RuntimeResult();
        WorldGenBridge.StopRuntime();

        if (result == null)
        {
            WorldGenBridge.Record(catalogId, caseArgs, "{\"status\":\"timeout\",\"error\":\"the runtime scenario did not finish\"}");
            Assert.Fail($"{catalogId} timed out before finishing.");
            yield break;
        }

        WorldGenPerfSettings.Report(catalogId, caseArgs, result);
    }
}
