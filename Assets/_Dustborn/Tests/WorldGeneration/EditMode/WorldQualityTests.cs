using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("WorldGen.Quality")]
public class WorldQualityTests
{
    private const string CONTENT = "Assets/_Dustborn/Content/World";

    [Test]
    public void GenerationConfigMatchesItsCodeDefaults()
    {
        Compare<WorldGenerationConfig>("WorldGenerationConfig.asset");
    }

    [Test]
    public void VoxelConfigMatchesItsCodeDefaults()
    {
        Compare<VoxelConfig>("VoxelConfig.asset");
    }

    [Test]
    public void BuildSettingsMatchTheirCodeDefaults()
    {
        Compare<WorldBuildSettings>("WorldBuildSettings.asset");
    }

    private static void Compare<T>(string file) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>($"{CONTENT}/{file}");
        Assert.IsNotNull(asset, $"{CONTENT}/{file} is missing");

        var defaults = ScriptableObject.CreateInstance<T>();
        int compared = 0;

        try
        {
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetCustomAttribute<QualityAttribute>() == null)
                    continue;

                compared++;
                Assert.AreEqual(field.GetValue(defaults), field.GetValue(asset), $"{typeof(T).Name}.{Name(field)} in {file} differs from its code default");
            }
        }
        finally
        {
            Object.DestroyImmediate(defaults);
        }

        Assert.Greater(compared, 0, $"No field of {typeof(T).Name} carries the Quality attribute");
    }

    private static string Name(FieldInfo field)
    {
        string name = field.Name;
        int end = name.IndexOf(">k__BackingField", System.StringComparison.Ordinal);

        return name.StartsWith("<") && end > 0 ? name.Substring(1, end - 1) : name;
    }
}
