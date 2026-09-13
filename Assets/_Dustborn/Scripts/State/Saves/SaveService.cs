using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class SaveService : ISaveService
{
    public const string DEFAULT_SLOT = "slot_0";

    private const string FILE_EXTENSION = ".json";

    private readonly SaveRegistry _registry;
    private readonly ISaveStorage _storage;
    private readonly ISaveSerializer _serializer;

    private readonly Dictionary<Type, object> _state = new();
    private readonly HashSet<string> _dirtyFiles = new();
    private readonly List<string> _dirtyBuffer = new();

    private string _activeSlot = DEFAULT_SLOT;

    public SaveRegistry Registry => _registry;
    public string ActiveSlot => _activeSlot;
    public bool HasDirtyFiles => _dirtyFiles.Count > 0;

    public SaveService(SaveRegistry registry, ISaveStorage storage, ISaveSerializer serializer)
    {
        _registry = registry;
        _storage = storage;
        _serializer = serializer;
    }

    public T Get<T>() where T : class, new()
    {
        SaveSection section = _registry.Get(typeof(T));

        if (_state.TryGetValue(section.DataType, out object value))
            return (T)value;

        T created = new T();
        _state[section.DataType] = created;

        return created;
    }

    public void MarkDirty<T>() where T : class, new()
    {
        _dirtyFiles.Add(_registry.Get(typeof(T)).FileName);
    }

    public bool IsDirty(string fileName) => _dirtyFiles.Contains(fileName);

    public void SetActiveSlot(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            throw new ArgumentException("Slot id must not be empty");

        if (slotId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Slot id '{slotId}' contains invalid characters");

        if (_activeSlot == slotId)
            return;

        _activeSlot = slotId;
        ResetScope(SaveScope.Slot);
    }

    public void ResetScope(SaveScope scope)
    {
        foreach (SaveSection section in _registry.Sections)
        {
            if (section.Scope != scope)
                continue;

            _state.Remove(section.DataType);
            _dirtyFiles.Remove(section.FileName);
        }
    }

    public async UniTask LoadScopeAsync(SaveScope scope, CancellationToken token)
    {
        List<UniTask> loads = new List<UniTask>();

        foreach (string fileName in _registry.FileNames(scope))
            loads.Add(LoadFileAsync(fileName, token));

        await UniTask.WhenAll(loads);
    }

    public async UniTask LoadFileAsync(string fileName, CancellationToken token)
    {
        IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);

        if (sections.Count == 0)
            return;

        string text = await _storage.ReadAsync(RelativePath(sections[0]), token);

        if (string.IsNullOrWhiteSpace(text))
        {
            Fill(sections, null);
            return;
        }

        SaveDocument document;

        try
        {
            document = _serializer.Deserialize(text, sections);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Save file '{fileName}' is corrupt, falling back to defaults: {exception.Message}");
            Fill(sections, null);
            return;
        }

        Fill(sections, document);
    }

    public async UniTask SaveScopeAsync(SaveScope scope, CancellationToken token)
    {
        foreach (string fileName in _registry.FileNames(scope))
            await SaveFileAsync(fileName, token);
    }

    public async UniTask SaveFileAsync(string fileName, CancellationToken token)
    {
        IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);

        if (sections.Count == 0)
            return;

        SaveDocument document = new SaveDocument();

        foreach (SaveSection section in sections)
        {
            if (!_state.TryGetValue(section.DataType, out object value))
                value = Activator.CreateInstance(section.DataType);

            document.Sections[section.Key] = value;
        }

        string text = _serializer.Serialize(document);

        await _storage.WriteAsync(RelativePath(sections[0]), text, token);

        _dirtyFiles.Remove(fileName);
    }

    public async UniTask SaveDirtyAsync(CancellationToken token)
    {
        if (_dirtyFiles.Count == 0)
            return;

        _dirtyBuffer.Clear();
        _dirtyBuffer.AddRange(_dirtyFiles);

        foreach (string fileName in _dirtyBuffer)
            await SaveFileAsync(fileName, token);
    }

    public UniTask<string[]> ListSlotsAsync(CancellationToken token) => _storage.ListDirectoriesAsync(null, token);

    public UniTask<bool> SlotExistsAsync(string slotId, CancellationToken token)
    {
        foreach (SaveSection section in _registry.Sections)
        {
            if (section.Scope == SaveScope.Slot)
                return _storage.ExistsAsync($"{slotId}/{section.FileName}{FILE_EXTENSION}", token);
        }

        return UniTask.FromResult(false);
    }

    public async UniTask DeleteSlotAsync(string slotId, CancellationToken token)
    {
        await _storage.DeleteDirectoryAsync(slotId, token);

        if (_activeSlot == slotId)
            ResetScope(SaveScope.Slot);
    }

    private void Fill(IReadOnlyList<SaveSection> sections, SaveDocument document)
    {
        foreach (SaveSection section in sections)
        {
            object value = null;

            document?.Sections.TryGetValue(section.Key, out value);

            _state[section.DataType] = value ?? Activator.CreateInstance(section.DataType);
            _dirtyFiles.Remove(section.FileName);
        }
    }

    private string RelativePath(SaveSection section)
    {
        if (section.Scope == SaveScope.Global)
            return section.FileName + FILE_EXTENSION;

        return $"{_activeSlot}/{section.FileName}{FILE_EXTENSION}";
    }
}
