using System;
using System.Collections.Generic;

public sealed class SaveRegistry
{
    private readonly Dictionary<Type, SaveSection> _byType = new();
    private readonly Dictionary<string, List<SaveSection>> _byFile = new();

    public IReadOnlyCollection<SaveSection> Sections => _byType.Values;

    public SaveRegistry Register<T>(string key, SaveScope scope, string fileName = null) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Save section key must not be empty");

        Type type = typeof(T);

        if (_byType.ContainsKey(type))
            throw new InvalidOperationException($"Save section for {type.Name} is already registered");

        string file = string.IsNullOrWhiteSpace(fileName) ? key : fileName;

        if (!_byFile.TryGetValue(file, out List<SaveSection> siblings))
        {
            siblings = new List<SaveSection>();
            _byFile.Add(file, siblings);
        }

        foreach (SaveSection sibling in siblings)
        {
            if (sibling.Scope != scope)
                throw new InvalidOperationException($"File '{file}' mixes {sibling.Scope} and {scope} sections");

            if (sibling.Key == key)
                throw new InvalidOperationException($"File '{file}' already holds a section with key '{key}'");
        }

        SaveSection section = new SaveSection(type, key, file, scope);
        _byType.Add(type, section);
        siblings.Add(section);

        return this;
    }

    public SaveSection Get(Type dataType)
    {
        if (_byType.TryGetValue(dataType, out SaveSection section))
            return section;

        throw new InvalidOperationException($"{dataType.Name} is not registered in {nameof(SaveRegistry)}");
    }

    public bool TryGet(Type dataType, out SaveSection section) => _byType.TryGetValue(dataType, out section);

    public IReadOnlyList<SaveSection> GetFile(string fileName)
    {
        if (_byFile.TryGetValue(fileName, out List<SaveSection> sections))
            return sections;

        throw new InvalidOperationException($"Save file '{fileName}' is not registered in {nameof(SaveRegistry)}");
    }

    public IEnumerable<string> FileNames(SaveScope scope)
    {
        foreach (KeyValuePair<string, List<SaveSection>> pair in _byFile)
        {
            if (pair.Value.Count > 0 && pair.Value[0].Scope == scope)
                yield return pair.Key;
        }
    }
}
