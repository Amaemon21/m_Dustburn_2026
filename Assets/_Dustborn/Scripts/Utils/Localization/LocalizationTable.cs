using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LocalizationTable", menuName = "Dustborn/UI/Localization Table")]
public sealed class LocalizationTable : ScriptableObject
{
    [Serializable]
    private struct Entry
    {
        [SerializeField] private string _key;
        [SerializeField, TextArea] private string _text;

        public KeyValuePair<string, string> Value => new(_key, _text);
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    public void Install()
    {
        try
        {
            LocalizationService.SetTexts(Array.ConvertAll(_entries, entry => entry.Value));
        }
        catch (ArgumentException exception)
        {
            Debug.LogError($"Invalid localization table '{name}': {exception.Message}", this);
        }
    }
}
