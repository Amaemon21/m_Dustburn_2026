using System;
using System.Collections.Generic;
using System.IO;
using Dustborn.WorldGen.Testing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("WorldGen.WaterStamps")]
public class WaterStampTests
{
    private const string PACK = "Assets/_Dustborn/Content/WaterStamps";
    private const string DATABASE = "Assets/_Dustborn/Content/World/WaterStampDatabase.asset";
    private const int WORLD = 2048;
    private const float MAX_HEIGHT = 384f;
    private const long RAW_BYTES = 2097152;

    private static WaterStampManifest Manifest()
    {
        string path = Path.Combine(PACK, "manifest.json");
        Assert.IsTrue(File.Exists(path), $"{path} is missing");
        return JsonUtility.FromJson<WaterStampManifest>(File.ReadAllText(path));
    }

    [Test]
    public void ManifestParsesAndValidates()
    {
        WaterStampManifest manifest = Manifest();
        List<string> errors = manifest.Validate();

        Assert.IsEmpty(errors, "Manifest errors: " + string.Join("; ", errors));
        Assert.AreEqual(26, manifest.stamps.Length);

        int rivers = 0, lakes = 0, ponds = 0;

        foreach (WaterStampManifestEntry stamp in manifest.stamps)
        {
            switch (stamp.kind)
            {
                case "river":
                    rivers++;
                    break;
                case "lake":
                    lakes++;
                    break;
                case "pond":
                    ponds++;
                    break;
            }
        }

        Assert.AreEqual(16, rivers);
        Assert.AreEqual(8, lakes);
        Assert.AreEqual(2, ponds);
    }

    [Test]
    public void RawMasksMatchSizeAndChecksum()
    {
        foreach (WaterStampManifestEntry stamp in Manifest().stamps)
        {
            string path = Path.Combine(PACK, stamp.mask_raw16);
            Assert.IsTrue(File.Exists(path), $"{path} is missing");

            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(RAW_BYTES, bytes.LongLength, $"{path} has the wrong size");
            Assert.AreEqual(stamp.sha256_raw16, WaterStampManifest.Sha256(bytes), $"{path} does not match its SHA-256");
        }
    }

    [Test]
    public void ShortRawIsRefused()
    {
        Assert.Catch<ArgumentException>(() => WaterStampShape.Decode(new byte[RAW_BYTES - 2], 1024));
    }

    [Test]
    public void TracingKeepsSocketsAndBranches()
    {
        WaterStampManifest manifest = Manifest();
        int resolution = manifest.Resolution;

        foreach (WaterStampManifestEntry stamp in manifest.stamps)
        {
            if (stamp.kind != "river")
                continue;

            WaterStampDefinition definition = stamp.ToDefinition(null, resolution, new List<string>());
            WaterStampShape shape = WaterStampShape.Decode(File.ReadAllBytes(Path.Combine(PACK, stamp.mask_raw16)), resolution);
            WaterStampTracer.Trace(definition, shape);

            Vector2[] centerline = definition.Centerline;
            Vector2 entry = definition.ToStamp(definition.Entry);
            Vector2 exit = definition.ToStamp(definition.Exit);
            string name = definition.Name;

            Assert.GreaterOrEqual(centerline.Length, 2, name);
            Assert.Less(Vector2.Distance(centerline[0], entry), 0.01f, $"{name}: centreline does not start on the entry socket");
            Assert.Less(Vector2.Distance(centerline[^1], exit), 0.01f, $"{name}: centreline does not end on the exit socket");

            foreach (WaterStampBranch branch in definition.Branches)
            {
                Assert.GreaterOrEqual(branch.Path.Length, 2, $"{name}: branch has no traced path");
                Assert.That(branch.JunctionIndex, Is.InRange(1, centerline.Length - 2), $"{name}: branch junction is not inside the main channel");
            }
        }
    }

    [Test]
    public void DatabaseHoldsTheWholePack()
    {
        WaterStampDatabase database = Database();

        Assert.AreEqual(26, database.Stamps.Count);

        int branches = 0;

        foreach (WaterStampDefinition definition in database.Stamps)
        {
            Assert.IsTrue(definition.IsValid, $"{definition.Name} is not usable");
            branches += definition.Branches.Count;
        }

        Assert.AreEqual(2, branches, "The tributary join and the fork carry one branch socket each");
    }

    [Test]
    public void StampedWaterIsDeterministic()
    {
        WaterStampDatabase database = Database();

        WaterMap first = Hydrate(Config(database, true));
        WaterMap second = Hydrate(Config(database, true));

        Assert.IsNotNull(first.Stamps, "Stamps are on but the water map carries no layout");
        Assert.Greater(first.Stamps.Placements.Count, 0, "No water stamp was placed");
        Assert.AreEqual(first.Stamps.Placements.Count, second.Stamps.Placements.Count);
        CollectionAssert.AreEqual(WaterMapFormat.Write(first), WaterMapFormat.Write(second), "The same seed produced different water");
        AssertRivers(first);
    }

    [Test]
    public void DisabledStampsFallBackToProceduralWater()
    {
        WaterMap water = Hydrate(Config(Database(), false));

        Assert.IsNull(water.Stamps, "Stamps are off but the water map carries a layout");
        Assert.Greater(water.Rivers.Count, 0, "The procedural generator formed no river");
        AssertRivers(water);
    }

    private static void AssertRivers(WaterMap water)
    {
        foreach (RiverPath river in water.Rivers)
        {
            List<RiverPoint> points = river.Points;

            for (int i = 1; i < points.Count; i++)
            {
                Assert.LessOrEqual(points[i].Surface, points[i - 1].Surface + 1e-3f, "A river flows uphill");
                Assert.Greater(points[i].Width, 0f, "A river point has no width");
                Assert.Less(points[i].Bed, points[i].Surface, "A river bed sits at or above its surface");
            }
        }
    }

    private static WaterStampDatabase Database()
    {
        var database = AssetDatabase.LoadAssetAtPath<WaterStampDatabase>(DATABASE);

        if (database == null)
            Assert.Ignore($"{DATABASE} is missing: run Мир/Импорт штампов воды first.");

        return database;
    }

    private static WorldGenerationConfig Config(WaterStampDatabase database, bool enabled)
    {
        var config = ScriptableObject.CreateInstance<WorldGenerationConfig>();

        SerializedFields.Set(config, "Seed", 1337);
        SerializedFields.Set(config, "WorldSize", WORLD);
        SerializedFields.Set(config, "SeaLevel", 50f);
        SerializedFields.Set(config.Water, "RiverStartArea", 0.15f);
        SerializedFields.Set(config.Water.Stamps, "Database", database);
        SerializedFields.Set(config.Water.Stamps, "Enabled", enabled);

        return config;
    }

    private static WaterMap Hydrate(WorldGenerationConfig config)
    {
        var map = new HeightMap(WORLD + 1, WORLD, MAX_HEIGHT);

        for (int z = 0; z <= WORLD; z++)
        {
            for (int x = 0; x <= WORLD; x++)
            {
                float axis = 1024f + 150f * Mathf.Sin(x / 400f);
                float valley = 14f * Mathf.Exp(-Mathf.Pow((z - axis) / 180f, 2f));
                float pond = Mathf.Max(0f, 1f - ((x - 1400f) * (x - 1400f) + (z - 380f) * (z - 380f)) / 8100f);
                float height = 40f + 110f * x / WORLD - valley - 7f * pond * pond + 1.5f * Mathf.Sin(x * 0.011f + z * 0.007f);

                map.Heights[z * (WORLD + 1) + x] = height / MAX_HEIGHT;
            }
        }

        WorldMapPipeline.Hydrate(config, map, out WaterMap water, out _);
        UnityEngine.Object.DestroyImmediate(config);

        return water;
    }
}
