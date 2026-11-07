using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

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
        object nested = null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (nested != null && line.StartsWith("    ") && !line.StartsWith("     ") && !line.StartsWith("    <"))
            {
                int colon = line.IndexOf(':');
                if (colon > 4)
                {
                    FieldInfo field = nested.GetType().GetField(line.Substring(4, colon - 4), BindingFlags.Public | BindingFlags.Instance);
                    string value = line.Substring(colon + 1).Trim();
                    if (field != null && TryParse(field.FieldType, value, out object parsed))
                        field.SetValue(nested, parsed);
                }

                continue;
            }

            if (nested != null && line.StartsWith("    <") && !line.StartsWith("     "))
            {
                if (AssignLine(nested, line, 5))
                    applied++;

                continue;
            }

            if (!line.StartsWith("  <") || line.StartsWith("   "))
            {
                if (!line.StartsWith("    "))
                    nested = null;

                continue;
            }

            nested = null;

            int header = line.IndexOf(">k__BackingField:", StringComparison.Ordinal);

            if (header > 0 && line.Substring(header + 17).Trim().Length == 0)
            {
                FieldInfo holder = type.GetField($"<{line.Substring(3, header - 3)}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

                if (holder != null && holder.FieldType.IsClass && holder.FieldType != typeof(string))
                {
                    nested = holder.GetValue(target) ?? Activator.CreateInstance(holder.FieldType, true);
                    holder.SetValue(target, nested);
                }

                continue;
            }

            if (AssignLine(target, line, 3))
                applied++;
        }

        if (applied == 0)
            throw new InvalidOperationException($"из {path} не прочитано ни одного поля");
    }

    private static bool AssignLine(object target, string line, int prefix)
    {
        int close = line.IndexOf(">k__BackingField: ", StringComparison.Ordinal);

        if (close < 0)
            return false;

        string name = line.Substring(prefix, close - prefix);
        string value = line.Substring(close + 18).Trim();

        if (value.Length == 0 || value[0] == '{')
            return false;

        FieldInfo field = target.GetType().GetField($"<{name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        if (field == null || !TryParse(field.FieldType, value, out object parsed))
            return false;

        field.SetValue(target, parsed);

        return true;
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

        if (type == typeof(bool))
        {
            if (!int.TryParse(text, out int flag))
                return false;

            value = flag != 0;

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

    public static HashSet<UnityEngine.GameObject> LoadStarterDecor(BiomeDatabase biomes, string folder, string content)
    {
        string wood = ReadMetaGuid(Path.Combine(content, "Configs/Block/BlockDamage/Wood_BlockConfig.asset.meta"));
        var woodGuids = new HashSet<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(content, "Prefabs/World/Trees"), "*.prefab"))
        {
            if (File.ReadAllText(path).Contains("guid: " + wood, StringComparison.Ordinal))
                woodGuids.Add(ReadMetaGuid(path + ".meta"));
        }

        var harvestable = new HashSet<UnityEngine.GameObject>();
        for (int i = 0; i < biomes.Count; i++)
        {
            BiomeDefinition biome = biomes.Get(i);
            string[] lines = File.ReadAllLines(Path.Combine(folder, biome.name + ".asset"));
            biome.Trees.Clear();
            biome.Rocks.Clear();
            biome.Grass.Clear();
            ReadScatter("Trees", biome.Trees);
            ReadScatter("Rocks", biome.Rocks);
            foreach (Dictionary<string, string> item in ReadList(lines, "  <Grass>k__BackingField:"))
            {
                var grass = new GrassLayer();
                if (RefGuid(item, "Card") != null)
                    Assign(grass, "Card", new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBA32, false));
                if (RefGuid(item, "Mask") != null)
                    Assign(grass, "Mask", new UnityEngine.Texture2D(1, 1, UnityEngine.TextureFormat.RGBA32, false));
                if (RefGuid(item, "Prefab") != null)
                    Assign(grass, "Prefab", new UnityEngine.GameObject());
                foreach (var (name, value) in item)
                    Assign(grass, name, value);
                biome.Grass.Add(grass);
            }

            void ReadScatter(string name, List<ScatterLayer> layers)
            {
                foreach (Dictionary<string, string> item in ReadList(lines, $"  <{name}>k__BackingField:"))
                {
                    string guid = RefGuid(item, "Prefab");
                    if (guid == null)
                        continue;
                    var prefab = new UnityEngine.GameObject { name = guid };
                    var layer = new ScatterLayer();
                    Assign(layer, "Prefab", prefab);
                    foreach (var (key, value) in item)
                        Assign(layer, key, value);
                    layers.Add(layer);
                    if (woodGuids.Contains(guid))
                        harvestable.Add(prefab);
                }
            }
        }

        return harvestable;
    }

    public static PropDatabase LoadProps(string path, string content)
    {
        PropDatabase database = Load<PropDatabase>(path);
        var names = new Dictionary<string, string>();
        foreach (string meta in Directory.EnumerateFiles(Path.Combine(content, "Prefabs/World/Props"), "*.prefab.meta", SearchOption.AllDirectories))
            names.Add(ReadMetaGuid(meta), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(meta)));

        var definitions = new List<PropDefinition>();
        foreach (Dictionary<string, string> item in ReadList(File.ReadAllLines(path), "  _definitions:"))
        {
            string guid = RefGuid(item, "Prefab");
            if (guid == null)
                continue;
            if (!names.TryGetValue(guid, out string name))
                throw new InvalidOperationException($"Missing prop prefab {guid}");
            var definition = new PropDefinition();
            Assign(definition, "Prefab", new UnityEngine.GameObject { name = name });
            foreach (var (key, value) in item)
                Assign(definition, key, value);
            definitions.Add(definition);
        }

        typeof(PropDatabase).GetField("_definitions", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(database, definitions);
        return database;
    }

    public static List<Dictionary<string, string>> ReadList(string[] lines, string header)
    {
        var items = new List<Dictionary<string, string>>();
        int start = Array.IndexOf(lines, header);
        if (start < 0)
            return items;

        Dictionary<string, string> current = null;
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("  - ", StringComparison.Ordinal))
            {
                current = new Dictionary<string, string>();
                items.Add(current);
            }
            else if (!line.StartsWith("    ", StringComparison.Ordinal))
            {
                break;
            }

            string body = line.Substring(4);
            int close = body.IndexOf(">k__BackingField:", StringComparison.Ordinal);
            if (current != null && body.StartsWith("<", StringComparison.Ordinal) && close >= 0)
                current[body.Substring(1, close - 1)] = body.Substring(close + 17).Trim();
        }

        return items;
    }

    private static string RefGuid(Dictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out string value))
            return null;
        int guid = value.IndexOf("guid: ", StringComparison.Ordinal);
        return guid < 0 ? null : value.Substring(guid + 6, 32);
    }

    private static string ReadMetaGuid(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (line.StartsWith("guid: ", StringComparison.Ordinal))
                return line.Substring(6).Trim();
        }

        throw new InvalidOperationException($"No GUID in {path}");
    }
}
