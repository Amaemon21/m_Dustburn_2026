using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

// Читает скалярные поля из Unity .asset (YAML), чтобы харнесс гонял настоящий конфиг,
// а не дефолты из кода. Вложенные блоки, ссылки на ассеты и списки пропускаются.
static class AssetReader
{
    public static T Load<T>(string path) where T : new()
    {
        var target = new T();

        Apply(target, path);

        return target;
    }

    public static void Apply(object target, string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"нет ассета {path}");

        Type type = target.GetType();
        int applied = 0;

        foreach (string line in File.ReadAllLines(path))
        {
            if (!line.StartsWith("  <") || line.StartsWith("   "))
                continue;

            int close = line.IndexOf(">k__BackingField: ", StringComparison.Ordinal);

            if (close < 0)
                continue;

            string name = line.Substring(3, close - 3);
            string value = line.Substring(close + 18).Trim();

            if (value.Length == 0 || value[0] == '{')
                continue;

            FieldInfo field = type.GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

            if (field == null || !TryParse(field.FieldType, value, out object parsed))
                continue;

            field.SetValue(target, parsed);
            applied++;
        }

        if (applied == 0)
            throw new InvalidOperationException($"из {path} не прочитано ни одного поля");
    }

    private static bool TryParse(Type type, string text, out object value)
    {
        value = null;

        if (type == typeof(float))
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                return false;

            value = number;

            return true;
        }

        if (type == typeof(int) || type.IsEnum)
        {
            if (!int.TryParse(text, out int number))
                return false;

            value = type.IsEnum ? Enum.ToObject(type, number) : number;

            return true;
        }

        if (type == typeof(string))
        {
            value = text;

            return true;
        }

        return false;
    }

    private static readonly Dictionary<string, PoiDefinition> _poiByGuid = new();

    public static PoiDatabase LoadPois(string databasePath, string searchRoot)
    {
        var list = new List<PoiDefinition>();

        foreach (string guid in ReadGuidList(databasePath, "_definitions:"))
        {
            string path = ResolveGuid(searchRoot, guid);

            if (path == null)
                throw new FileNotFoundException($"{databasePath}: не найден ассет с guid {guid}");

            var definition = new PoiDefinition { name = Path.GetFileNameWithoutExtension(path) };

            _poiByGuid[guid] = definition;

            Apply(definition, path);

            typeof(PoiDefinition)
                .GetField("<Prefab>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(definition, new UnityEngine.GameObject { name = definition.name });

            list.Add(definition);
        }

        var database = new PoiDatabase();

        typeof(PoiDatabase)
            .GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(database, list);

        return database;
    }

    // Вложенные SettlementProfile лежат на отступе 4, поэтому Apply их не видит: он читает
    // только верхний уровень. Без этого харнесс мерил бы дефолты из кода, а не ассет.
    public static void ApplyProfiles(WorldGenerationConfig config, string path)
    {
        ApplyProfile(config, path, "CityProfile");
        ApplyProfile(config, path, "TownProfile");
        ApplyProfile(config, path, "VillageProfile");
    }

    private static void ApplyProfile(WorldGenerationConfig config, string path, string property)
    {
        FieldInfo slot = typeof(WorldGenerationConfig)
            .GetField($"<{property}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        var profile = (SettlementProfile)slot.GetValue(config);
        var composition = new List<PoiRequirement>();

        bool inside = false;
        bool inComposition = false;
        PoiRequirement current = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line == $"  <{property}>k__BackingField:")
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            if (!line.StartsWith("    ", StringComparison.Ordinal))
                break;

            string body = line.Substring(4);

            if (body == "<Composition>k__BackingField:")
            {
                inComposition = true;
                continue;
            }

            if (body.StartsWith("- ", StringComparison.Ordinal))
            {
                current = new PoiRequirement();
                composition.Add(current);
                body = body.Substring(2);
            }
            else if (line.StartsWith("      ", StringComparison.Ordinal))
            {
                body = line.Substring(6);
            }

            if (!Split(body, out string name, out string value))
                continue;

            if (!inComposition)
            {
                Assign(profile, name, value);
                continue;
            }

            if (current == null)
                continue;

            if (name == "Definition")
            {
                int guid = value.IndexOf("guid: ", StringComparison.Ordinal);

                if (guid >= 0 && _poiByGuid.TryGetValue(value.Substring(guid + 6, 32), out PoiDefinition definition))
                    Assign(current, name, definition);

                continue;
            }

            Assign(current, name, value);
        }

        Assign(profile, "Composition", composition);
    }

    private static bool Split(string body, out string name, out string value)
    {
        name = null;
        value = null;

        if (!body.StartsWith("<", StringComparison.Ordinal))
            return false;

        int close = body.IndexOf(">k__BackingField: ", StringComparison.Ordinal);

        if (close < 0)
            return false;

        name = body.Substring(1, close - 1);
        value = body.Substring(close + 18).Trim();

        return value.Length > 0;
    }

    private static void Assign(object target, string name, string value)
    {
        FieldInfo field = target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null && TryParse(field.FieldType, value, out object parsed))
            field.SetValue(target, parsed);
    }

    private static void Assign(object target, string name, object value)
    {
        target.GetType()
            .GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(target, value);
    }

    private static List<string> ReadGuidList(string path, string header)
    {
        var guids = new List<string>();
        bool inside = false;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Trim() == header)
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            int start = line.IndexOf("guid: ", StringComparison.Ordinal);

            if (start < 0)
                break;

            guids.Add(line.Substring(start + 6, 32));
        }

        return guids;
    }

    private static Dictionary<string, string> _guidIndex;

    private static string ResolveGuid(string searchRoot, string guid)
    {
        if (_guidIndex == null)
        {
            _guidIndex = new Dictionary<string, string>();

            foreach (string meta in Directory.GetFiles(searchRoot, "*.asset.meta", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadAllLines(meta))
                {
                    if (!line.StartsWith("guid: ", StringComparison.Ordinal))
                        continue;

                    _guidIndex[line.Substring(6).Trim()] = meta.Substring(0, meta.Length - 5);
                    break;
                }
            }
        }

        return _guidIndex.TryGetValue(guid, out string path) ? path : null;
    }

    public static BiomeDatabase LoadBiomes(string folder, params string[] names)
    {
        var list = new List<BiomeDefinition>();

        foreach (string name in names)
        {
            var biome = new BiomeDefinition { name = name };

            Apply(biome, Path.Combine(folder, name + ".asset"));

            list.Add(biome);
        }

        var database = new BiomeDatabase();

        typeof(BiomeDatabase)
            .GetField("_biomes", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(database, list);

        return database;
    }
}
