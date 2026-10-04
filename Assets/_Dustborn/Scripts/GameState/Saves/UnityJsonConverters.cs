using System;
using Newtonsoft.Json;
using UnityEngine;

public sealed class Vector3Converter : JsonConverter<Vector3>
{
    public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.z);
        writer.WriteEndObject();
    }

    public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        Vector3 result = existingValue;

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string)reader.Value;
            float value = Convert.ToSingle(reader.ReadAsDouble() ?? 0d);

            if (name == "x")
                result.x = value;
            else if (name == "y")
                result.y = value;
            else if (name == "z")
                result.z = value;
        }

        return result;
    }
}

public sealed class Vector3IntConverter : JsonConverter<Vector3Int>
{
    public override void WriteJson(JsonWriter writer, Vector3Int value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.z);
        writer.WriteEndObject();
    }

    public override Vector3Int ReadJson(JsonReader reader, Type objectType, Vector3Int existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        Vector3Int result = existingValue;

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string)reader.Value;
            int value = (int)(reader.ReadAsInt32() ?? 0);

            if (name == "x")
                result.x = value;
            else if (name == "y")
                result.y = value;
            else if (name == "z")
                result.z = value;
        }

        return result;
    }
}

public sealed class QuaternionConverter : JsonConverter<Quaternion>
{
    public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WritePropertyName("z");
        writer.WriteValue(value.z);
        writer.WritePropertyName("w");
        writer.WriteValue(value.w);
        writer.WriteEndObject();
    }

    public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        Quaternion result = existingValue;

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string)reader.Value;
            float value = Convert.ToSingle(reader.ReadAsDouble() ?? 0d);

            if (name == "x")
                result.x = value;
            else if (name == "y")
                result.y = value;
            else if (name == "z")
                result.z = value;
            else if (name == "w")
                result.w = value;
        }

        return result;
    }
}
