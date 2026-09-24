using System;
using System.Collections;
using System.IO;
using System.Reflection;
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

    private static readonly Assembly Game = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Assembly-CSharp");

    private static Type TypeOf(string name)
    {
        Type type = Game?.GetType(name);
        Assert.IsNotNull(type, $"{name} is not compiled into Assembly-CSharp");
        return type;
    }

    private static object Get(object target, string member)
    {
        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);

        if (property != null)
            return property.GetValue(target);

        FieldInfo field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(field, $"{type.Name}.{member} is missing");
        return field.GetValue(target);
    }

    private static void Set(object target, string property, object value)
    {
        FieldInfo field = target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, $"{target.GetType().Name}.{property} has no backing field");
        field.SetValue(target, value);
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance).Invoke(target, arguments);
    }

    private static object Manifest()
    {
        string path = Path.Combine(PACK, "manifest.json");
        Assert.IsTrue(File.Exists(path), $"{path} is missing");
        return JsonUtility.FromJson(File.ReadAllText(path), TypeOf("WaterStampManifest"));
    }

    [Test]
    public void ManifestParsesAndValidates()
    {
        object manifest = Manifest();
        var errors = (IList)Call(manifest, "Validate");
        var stamps = (Array)Get(manifest, "stamps");

        Assert.IsEmpty(errors, "Manifest errors: " + string.Join("; ", ToStrings(errors)));
        Assert.AreEqual(26, stamps.Length);

        int rivers = 0, lakes = 0, ponds = 0;

        foreach (object stamp in stamps)
        {
            switch ((string)Get(stamp, "kind"))
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
        MethodInfo sha = TypeOf("WaterStampManifest").GetMethod("Sha256", BindingFlags.Public | BindingFlags.Static);

        foreach (object stamp in (Array)Get(Manifest(), "stamps"))
        {
            string path = Path.Combine(PACK, (string)Get(stamp, "mask_raw16"));
            Assert.IsTrue(File.Exists(path), $"{path} is missing");

            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(RAW_BYTES, bytes.LongLength, $"{path} has the wrong size");
            Assert.AreEqual((string)Get(stamp, "sha256_raw16"), (string)sha.Invoke(null, new object[] { bytes }), $"{path} does not match its SHA-256");
        }
    }

    [Test]
    public void ShortRawIsRefused()
    {
        MethodInfo decode = TypeOf("WaterStampShape").GetMethod("Decode", BindingFlags.Public | BindingFlags.Static);
        var error = Assert.Throws<TargetInvocationException>(() => decode.Invoke(null, new object[] { new byte[RAW_BYTES - 2], 1024 }));

        Assert.IsInstanceOf<ArgumentException>(error.InnerException);
    }

    [Test]
    public void TracingKeepsSocketsAndBranches()
    {
        object manifest = Manifest();
        int resolution = (int)Get(manifest, "Resolution");
        MethodInfo decode = TypeOf("WaterStampShape").GetMethod("Decode", BindingFlags.Public | BindingFlags.Static);
        MethodInfo trace = TypeOf("WaterStampTracer").GetMethod("Trace", BindingFlags.Public | BindingFlags.Static);

        foreach (object stamp in (Array)Get(manifest, "stamps"))
        {
            if ((string)Get(stamp, "kind") != "river")
                continue;

            object definition = Call(stamp, "ToDefinition", null, resolution, new System.Collections.Generic.List<string>());
            object shape = decode.Invoke(null, new object[] { File.ReadAllBytes(Path.Combine(PACK, (string)Get(stamp, "mask_raw16"))), resolution });
            trace.Invoke(null, new[] { definition, shape });

            var centerline = (Vector2[])Get(definition, "Centerline");
            var entry = (Vector2)Call(definition, "ToStamp", (Vector2)Get(definition, "Entry"));
            var exit = (Vector2)Call(definition, "ToStamp", (Vector2)Get(definition, "Exit"));
            string name = (string)Get(definition, "Name");

            Assert.GreaterOrEqual(centerline.Length, 2, name);
            Assert.Less(Vector2.Distance(centerline[0], entry), 0.01f, $"{name}: centreline does not start on the entry socket");
            Assert.Less(Vector2.Distance(centerline[^1], exit), 0.01f, $"{name}: centreline does not end on the exit socket");

            foreach (object branch in (IList)Get(definition, "Branches"))
            {
                var path = (Vector2[])Get(branch, "Path");
                int junction = (int)Get(branch, "JunctionIndex");

                Assert.GreaterOrEqual(path.Length, 2, $"{name}: branch has no traced path");
                Assert.That(junction, Is.InRange(1, centerline.Length - 2), $"{name}: branch junction is not inside the main channel");
            }
        }
    }

    [Test]
    public void DatabaseHoldsTheWholePack()
    {
        ScriptableObject database = Database();
        var stamps = (IList)Get(database, "Stamps");

        Assert.AreEqual(26, stamps.Count);

        int branches = 0;

        foreach (object definition in stamps)
        {
            Assert.IsTrue((bool)Get(definition, "IsValid"), $"{Get(definition, "Name")} is not usable");
            branches += ((IList)Get(definition, "Branches")).Count;
        }

        Assert.AreEqual(2, branches, "The tributary join and the fork carry one branch socket each");
    }

    [Test]
    public void StampedWaterIsDeterministic()
    {
        ScriptableObject database = Database();

        object first = Hydrate(Config(database, true), out object firstLayout);
        object second = Hydrate(Config(database, true), out object secondLayout);

        Assert.IsNotNull(firstLayout, "Stamps are on but the water map carries no layout");
        Assert.Greater(((IList)Get(firstLayout, "Placements")).Count, 0, "No water stamp was placed");
        Assert.AreEqual(((IList)Get(firstLayout, "Placements")).Count, ((IList)Get(secondLayout, "Placements")).Count);
        CollectionAssert.AreEqual((byte[])Call(first, "ToBytes"), (byte[])Call(second, "ToBytes"), "The same seed produced different water");
        AssertRivers(first);
    }

    [Test]
    public void DisabledStampsFallBackToProceduralWater()
    {
        ScriptableObject database = Database();
        object water = Hydrate(Config(database, false), out object layout);

        Assert.IsNull(layout, "Stamps are off but the water map carries a layout");
        Assert.Greater(((IList)Get(water, "Rivers")).Count, 0, "The procedural generator formed no river");
        AssertRivers(water);
    }

    private static void AssertRivers(object water)
    {
        foreach (object river in (IList)Get(water, "Rivers"))
        {
            var points = (IList)Get(river, "Points");

            for (int i = 1; i < points.Count; i++)
            {
                float before = (float)Get(points[i - 1], "Surface");
                float after = (float)Get(points[i], "Surface");
                float width = (float)Get(points[i], "Width");

                Assert.LessOrEqual(after, before + 1e-3f, "A river flows uphill");
                Assert.Greater(width, 0f, "A river point has no width");
                Assert.Less((float)Get(points[i], "Bed"), after, "A river bed sits at or above its surface");
            }
        }
    }

    private static ScriptableObject Database()
    {
        var database = (ScriptableObject)AssetDatabase.LoadAssetAtPath(DATABASE, TypeOf("WaterStampDatabase"));

        if (database == null)
            Assert.Ignore($"{DATABASE} is missing: run Мир/Импорт штампов воды first.");

        return database;
    }

    private static ScriptableObject Config(ScriptableObject database, bool enabled)
    {
        var config = ScriptableObject.CreateInstance(TypeOf("WorldGenerationConfig"));

        Set(config, "Seed", 1337);
        Set(config, "WorldSize", WORLD);
        Set(config, "SeaLevel", 50f);

        object water = Get(config, "Water");
        Set(water, "RiverStartArea", 0.15f);

        object stamps = Get(water, "Stamps");
        Set(stamps, "Database", database);
        Set(stamps, "Enabled", enabled);

        return config;
    }

    private static object Hydrate(ScriptableObject config, out object layout)
    {
        Type type = TypeOf("HeightMap");
        object map = Activator.CreateInstance(type, WORLD + 1, WORLD, MAX_HEIGHT);
        var heights = (float[])Get(map, "Heights");

        for (int z = 0; z <= WORLD; z++)
        {
            for (int x = 0; x <= WORLD; x++)
            {
                float axis = 1024f + 150f * Mathf.Sin(x / 400f);
                float valley = 14f * Mathf.Exp(-Mathf.Pow((z - axis) / 180f, 2f));
                float pond = Mathf.Max(0f, 1f - ((x - 1400f) * (x - 1400f) + (z - 380f) * (z - 380f)) / 8100f);
                float height = 40f + 110f * x / WORLD - valley - 7f * pond * pond + 1.5f * Mathf.Sin(x * 0.011f + z * 0.007f);

                heights[z * (WORLD + 1) + x] = height / MAX_HEIGHT;
            }
        }

        MethodInfo hydrate = TypeOf("WorldMapPipeline").GetMethod("Hydrate", BindingFlags.Public | BindingFlags.Static, null,
            new[] { TypeOf("WorldGenerationConfig"), type, TypeOf("Hydrology").MakeByRefType() }, null);
        object carved = hydrate.Invoke(null, new object[] { config, map, null });
        object water = Get(carved, "Water");

        layout = Get(water, "Stamps");
        UnityEngine.Object.DestroyImmediate(config);

        return water;
    }

    private static string[] ToStrings(IList values)
    {
        var strings = new string[values.Count];

        for (int i = 0; i < values.Count; i++)
            strings[i] = values[i]?.ToString();

        return strings;
    }
}
