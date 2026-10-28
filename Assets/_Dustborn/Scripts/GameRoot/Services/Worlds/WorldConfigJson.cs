using System.Text.RegularExpressions;
using UnityEngine;

public static class WorldConfigJson
{
    private static readonly Regex OBJECT_REFERENCE = new("\"[^\"]+\":(\\{\"instanceID\":-?\\d+\\}|\\[(\\{\"instanceID\":-?\\d+\\},?)*\\]),?");
    private static readonly Regex DANGLING_COMMA = new(",(\\s*[}\\]])");

    public static string Write(WorldGenerationConfig config)
    {
        string json = OBJECT_REFERENCE.Replace(JsonUtility.ToJson(config), string.Empty);
        return DANGLING_COMMA.Replace(json, "$1");
    }

    public static WorldGenerationConfig Read(string json, WorldGenerationConfig template)
    {
        WorldGenerationConfig config = Object.Instantiate(template);
        config.name = template.name;
        JsonUtility.FromJsonOverwrite(json, config);
        return config;
    }
}
