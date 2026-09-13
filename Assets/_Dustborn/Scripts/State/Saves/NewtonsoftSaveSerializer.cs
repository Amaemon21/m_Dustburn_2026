using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class NewtonsoftSaveSerializer : ISaveSerializer
{
    private const string VERSION_FIELD = "version";
    private const string SECTIONS_FIELD = "sections";

    private readonly JsonSerializer _serializer;

    public NewtonsoftSaveSerializer()
    {
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

        if (root[VERSION_FIELD] is JValue version)
            document.Version = version.Value<int>();

        if (root[SECTIONS_FIELD] is not JObject payload)
            return document;

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
