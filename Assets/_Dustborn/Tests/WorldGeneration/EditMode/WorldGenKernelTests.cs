using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;
using Unity.PerformanceTesting;

public class WorldGenKernelTests
{
    public static IEnumerable<TestCaseData> EditorCases()
    {
        return WorldGenPerfSettings.Cases("EM");
    }

    public static IEnumerable<TestCaseData> EditorProcessCases()
    {
        return WorldGenPerfSettings.Cases("ED");
    }

    [Test, Performance]
    [TestCaseSource(nameof(EditorCases))]
    public void Kernel(string catalogId, string caseArgs, string op)
    {
        Run(catalogId, caseArgs, op);
    }

    [Test, Performance]
    [TestCaseSource(nameof(EditorProcessCases))]
    public void EditorProcess(string catalogId, string caseArgs, string op)
    {
        Run(catalogId, caseArgs, op);
    }

    private static void Run(string catalogId, string caseArgs, string op)
    {
        if (catalogId == "BRIDGE")
            Assert.Fail(WorldGenBridge.Failure);

        string result = WorldGenBridge.Run(op, WorldGenPerfSettings.Profile, caseArgs,
            WorldGenPerfSettings.Warmup, WorldGenPerfSettings.Samples);

        WorldGenPerfSettings.Report(catalogId, caseArgs, result);
    }
}
