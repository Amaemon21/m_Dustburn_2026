using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class NewtonsoftSaveSerializer : ISaveSerializer
{
    private const string VERSION_FIELD = "version";
    public const string SECTIONS_FIELD = "sections";

    private readonly JsonSerializer _serializer;
    private readonly SaveMigrationService _migrations;

    public NewtonsoftSaveSerializer(SaveMigrationService migrations = null)
    {
        _migrations = migrations ?? new SaveMigrationService(System.Array.Empty<ISaveMigration>());
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            TypeNameHandling = TypeNameHandling.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        settings.Converters.Add(new Vector3Converter());
        settings.Converters.Add(new Vector3IntConverter());
        settings.Converters.Add(new QuaternionConverter());

        _serializer = JsonSerializer.Create(settings);
    }

    public string Serialize(SaveDocument document)
    {
        JObject payload = new JObject();

        foreach (KeyValuePair<string, object> pair in document.Sections)
        {
            if (pair.Value == null)
                continue;

            payload[pair.Key] = JToken.FromObject(pair.Value, _serializer);
        }

        JObject root = new JObject
        {
            [VERSION_FIELD] = document.Version,
            [SECTIONS_FIELD] = payload
        };

        return root.ToString(Formatting.Indented);
    }

    public SaveDocument Deserialize(string text, IReadOnlyList<SaveSection> sections)
    {
        SaveDocument document = new SaveDocument();

        JObject root = JObject.Parse(text);
        document.RequiresSave = (root[VERSION_FIELD]?.Value<int>() ?? 1) != SaveDocument.CURRENT_VERSION;
        _migrations.Upgrade(root);

        if (root[VERSION_FIELD] is JValue version)
            document.Version = version.Value<int>();

        if (root[SECTIONS_FIELD] is not JObject payload)
            throw new JsonSerializationException("Save sections must be an object");

        foreach (SaveSection section in sections)
        {
            JToken token = payload[section.Key];

            if (token == null || token.Type == JTokenType.Null)
                continue;

            document.Sections[section.Key] = token.ToObject(section.DataType, _serializer);
        }

        return document;
    }
}
