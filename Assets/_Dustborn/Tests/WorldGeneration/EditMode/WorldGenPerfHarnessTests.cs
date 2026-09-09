using System;
using System.Collections.Generic;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;

[Category("WorldGen.Smoke")]
public class WorldGenPerfHarnessTests
{
    [Test]
    public void BridgeIsAvailable()
    {
        Assert.IsTrue(WorldGenBridge.Available, WorldGenBridge.Failure);
    }

    [Test]
    public void CatalogCoversEveryFamily()
    {
        List<WorldGenCase> catalog = WorldGenBridge.Catalog();

        Assert.AreEqual(108, catalog.Count, "The catalogue must hold every specification of section four.");

        var families = new Dictionary<string, int>
        {
            { "PRE", 5 }, { "BIO", 8 }, { "HGT", 10 }, { "SET", 14 }, { "POI", 7 }, { "MAT", 8 },
            { "IO", 9 }, { "VOX", 10 }, { "QUE", 8 }, { "DEC", 11 }, { "E2E", 11 }, { "LIFE", 7 }
        };

        var seen = new Dictionary<string, int>();

        foreach (WorldGenCase entry in catalog)
        {
            string family = entry.Id.Substring(0, entry.Id.IndexOf('-'));
            seen.TryGetValue(family, out int count);
            seen[family] = count + 1;
        }

        foreach (KeyValuePair<string, int> pair in families)
        {
            Assert.IsTrue(seen.ContainsKey(pair.Key), $"Family {pair.Key} is missing from the catalogue.");
            Assert.AreEqual(pair.Value, seen[pair.Key], $"Family {pair.Key} must hold {pair.Value} specifications.");
        }
    }

    [Test]
    public void EveryCatalogOperationExists()
    {
        var operations = new HashSet<string>(WorldGenBridge.Operations().Split('\n'), StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (WorldGenCase entry in WorldGenBridge.Catalog())
        {
            if (entry.Op.StartsWith("runner.", StringComparison.Ordinal))
                continue;

            if (!operations.Contains(entry.Op))
                missing.Add(entry.Id + " -> " + entry.Op);
        }

        Assert.IsEmpty(missing, "Every catalogue row must name an implemented operation: " + string.Join(", ", missing));
    }

    [Test]
    public void EnvironmentIsReported()
    {
        string environment = WorldGenBridge.Environment();

        Assert.IsNotNull(environment);
        StringAssert.Contains("unityVersion", environment);
        StringAssert.Contains("stopwatchFrequency", environment);
        TestContext.WriteLine(environment);
    }

    [Test]
    public void UnknownOperationIsNotRunRatherThanPassed()
    {
        string result = WorldGenBridge.Run("does.not.exist", WorldGenPerfSettings.Profile, string.Empty, 0, 1);
        Assert.AreEqual("not_run", WorldGenBridge.Status(result));
    }
}
