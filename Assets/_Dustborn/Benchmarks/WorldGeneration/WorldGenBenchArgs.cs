using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public sealed class WorldGenBenchArgs
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _raw;

    public WorldGenBenchArgs(string text)
    {
        _raw = text ?? string.Empty;

        foreach (string pair in _raw.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int split = pair.IndexOf('=');

            if (split <= 0)
                continue;

            _values[pair.Substring(0, split).Trim()] = pair.Substring(split + 1).Trim();
        }
    }

    public string Raw => _raw;

    public IReadOnlyDictionary<string, string> Values => _values;

    public bool Has(string key)
    {
        return _values.ContainsKey(key);
    }

    public string Text(string key, string fallback)
    {
        return _values.TryGetValue(key, out string value) ? value : fallback;
    }

    public int Int(string key, int fallback)
    {
        return _values.TryGetValue(key, out string value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    public float Float(string key, float fallback)
    {
        return _values.TryGetValue(key, out string value)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
    }

    public bool Bool(string key, bool fallback)
    {
        return _values.TryGetValue(key, out string value) && bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }

    public static string Hash(string text)
    {
        using SHA256 sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToBase64String(digest, 0, 16).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(byte[] bytes)
    {
        using SHA256 sha = SHA256.Create();
        byte[] digest = sha.ComputeHash(bytes ?? Array.Empty<byte>());
        return Convert.ToBase64String(digest, 0, 16).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(float[] values)
    {
        if (values == null)
            return Hash(Array.Empty<byte>());

        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Hash(bytes);
    }
}
