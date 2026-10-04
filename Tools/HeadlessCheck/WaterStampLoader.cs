using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

static class WaterStampLoader
{
    public const string PACK = "../../Assets/_Dustborn/Content/WaterStamps";

    private static WaterStampDatabase _cached;

    public static WaterStampManifest Manifest()
    {
        string path = $"{PACK}/manifest.json";

        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<WaterStampManifest>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
    }

    public static WaterStampDatabase Load()
    {
        if (_cached != null)
            return _cached;

        WaterStampManifest manifest = Manifest();

        if (manifest == null)
            return null;

        List<string> errors = manifest.Validate();

        if (errors.Count > 0)
            throw new InvalidDataException("Water stamp manifest: " + string.Join("; ", errors));

        var warnings = new List<string>();
        var definitions = new List<WaterStampDefinition>();

        foreach (WaterStampManifestEntry entry in manifest.stamps)
        {
            string raw = $"{PACK}/{entry.mask_raw16}";
            byte[] bytes = File.ReadAllBytes(raw);

            if (bytes.Length != manifest.ExpectedBytes)
                throw new InvalidDataException($"{raw} is {bytes.Length} bytes, expected {manifest.ExpectedBytes}");

            WaterStampDefinition definition = entry.ToDefinition(new TextAsset(bytes) { name = entry.name }, manifest.Resolution, warnings);
            WaterStampTracer.Trace(definition, WaterStampShape.From(definition));
            definitions.Add(definition);
        }

        foreach (string warning in warnings)
            Console.WriteLine("[warning] " + warning);

        _cached = new WaterStampDatabase();
        _cached.Replace(definitions);

        return _cached;
    }

    public static void Attach(WorldGenerationConfig config, bool enabled = true)
    {
        WaterStampDatabase database = enabled ? Load() : null;

        StampLoader.Set(config.Water.Stamps, "Database", database);
        StampLoader.Set(config.Water.Stamps, "Enabled", enabled);
        StampLoader.Set(config.Water.Stamps, "RecordRejections", enabled);
    }
}
