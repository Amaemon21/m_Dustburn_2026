using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

public static class LocalizationService
{
    private static Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    public static void SetTexts(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                throw new ArgumentException("Localization entries need a key and a text value.", nameof(entries));

            if (!texts.TryAdd(entry.Key, entry.Value))
                throw new ArgumentException($"Duplicate localization key '{entry.Key}'.", nameof(entries));
        }

        Volatile.Write(ref _texts, texts);
    }

    public static string Text(string key)
    {
        if (Volatile.Read(ref _texts).TryGetValue(key, out string text))
            return text;

#if UNITY_EDITOR
        if (EditorLocalization.TryGetText(key, out text))
            return text;
#endif

        return key;
    }

    public static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Text(key), args);
        }
        catch (FormatException)
        {
            return key;
        }
    }
}
