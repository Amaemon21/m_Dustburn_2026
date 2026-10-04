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
    private readonly HashSet<string> _blockedPaths = new();
    private readonly Dictionary<string, long> _fileRevisions = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private long _revision;
    private long _globalSession;
    private long _slotSession;

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
        string fileName = _registry.Get(typeof(T)).FileName;
        _dirtyFiles.Add(fileName);
        _fileRevisions[fileName] = ++_revision;
    }

    public bool IsDirty(string fileName) => _dirtyFiles.Contains(fileName);

    public void SetActiveSlot(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
            throw new ArgumentException("Slot id must not be empty");

        if (slotId == "." || slotId == ".." || slotId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"Slot id '{slotId}' contains invalid characters");

        if (_activeSlot == slotId)
            return;

        _activeSlot = slotId;
        ResetScope(SaveScope.Slot);
    }

    public void ResetScope(SaveScope scope)
    {
        AdvanceSession(scope);
        foreach (SaveSection section in _registry.Sections)
        {
            if (section.Scope != scope)
                continue;

            _state.Remove(section.DataType);
            _dirtyFiles.Remove(section.FileName);
            _fileRevisions[section.FileName] = ++_revision;
        }
    }

    public async UniTask LoadScopeAsync(SaveScope scope, CancellationToken token)
    {
        long session = AdvanceSession(scope);
        await _writeLock.WaitAsync(token);
        try
        {
            if (session != Session(scope))
                return;
            List<string> files = new(_registry.FileNames(scope));
            List<long> revisions = new();
            List<UniTask<SaveDocument>> loads = new();
            foreach (string fileName in files)
            {
                _fileRevisions.TryGetValue(fileName, out long revision);
                revisions.Add(revision);
                loads.Add(ReadDocumentAsync(fileName, RelativePath(_registry.GetFile(fileName)[0]), token));
            }
            SaveDocument[] documents = await UniTask.WhenAll(loads);
            token.ThrowIfCancellationRequested();
            if (session != Session(scope))
                return;
            for (int i = 0; i < files.Count; i++)
            {
                _fileRevisions.TryGetValue(files[i], out long currentRevision);
                if (currentRevision != revisions[i])
                    return;
            }
            for (int i = 0; i < files.Count; i++)
                Fill(_registry.GetFile(files[i]), documents[i]);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async UniTask LoadFileAsync(string fileName, CancellationToken token)
    {
        IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);
        if (sections.Count == 0)
            return;
        long session = Session(sections[0].Scope);
        long revision = ++_revision;
        _fileRevisions[fileName] = revision;
        string path = RelativePath(sections[0]);
        await _writeLock.WaitAsync(token);
        try
        {
            if (session != Session(sections[0].Scope) || _fileRevisions[fileName] != revision)
                return;
            SaveDocument document = await ReadDocumentAsync(fileName, path, token);
            token.ThrowIfCancellationRequested();
            _fileRevisions.TryGetValue(fileName, out long currentRevision);
            if (session == Session(sections[0].Scope) && revision == currentRevision)
                Fill(sections, document);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async UniTask<SaveDocument> ReadDocumentAsync(string fileName, string path, CancellationToken token)
    {
        IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);
        string text;
        try
        {
            text = await _storage.ReadAsync(path, token);
        }
        catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogWarning($"Save read failed for '{fileName}': {exception.Message}");
            text = null;
        }
        bool corrupt = text != null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                SaveDocument document = _serializer.Deserialize(text, sections);
                _blockedPaths.Remove(path);
                return document;
            }
            catch (UnsupportedSaveVersionException)
            {
                _blockedPaths.Add(path);
                throw;
            }
            catch (Exception exception)
            {
                corrupt = true;
                Debug.LogWarning($"Save file '{fileName}' is corrupt: {exception.Message}");
            }
        }
        string backup = await _storage.ReadBackupAsync(path, token);
        SaveDocument recovered = null;
        bool corruptBackup = false;
        if (!string.IsNullOrWhiteSpace(backup))
        {
            try
            {
                recovered = _serializer.Deserialize(backup, sections);
                _blockedPaths.Remove(path);
            }
            catch (UnsupportedSaveVersionException)
            {
                _blockedPaths.Add(path);
                throw;
            }
            catch (Exception exception)
            {
                corruptBackup = true;
                Debug.LogWarning($"Save backup '{fileName}' is corrupt: {exception.Message}");
            }
        }
        token.ThrowIfCancellationRequested();
        if (corrupt)
            await _storage.QuarantineAsync(path, token);
        if (corruptBackup)
            await _storage.QuarantineAsync(path + ".bak", token);
        if (recovered != null)
        {
            recovered.RequiresSave = true;
            Debug.LogWarning($"Recovered save file '{fileName}' from backup");
        }
        return recovered ?? (corrupt || corruptBackup ? new SaveDocument { RequiresSave = true } : null);
    }

    private long AdvanceSession(SaveScope scope)
    {
        if (scope == SaveScope.Global)
            return ++_globalSession;
        return ++_slotSession;
    }

    private long Session(SaveScope scope) => scope == SaveScope.Global ? _globalSession : _slotSession;

    public UniTask SaveScopeAsync(SaveScope scope, CancellationToken token)
        => SaveFilesAsync(new List<string>(_registry.FileNames(scope)), token);

    public UniTask SaveFileAsync(string fileName, CancellationToken token)
        => SaveFilesAsync(new[] { fileName }, token);

    private async UniTask SaveFilesAsync(IReadOnlyList<string> files, CancellationToken token)
    {
        long globalSession = _globalSession;
        long slotSession = _slotSession;
        await _writeLock.WaitAsync(token);
        try
        {
            foreach (string fileName in files)
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);
                if (sections.Count == 0)
                    continue;
                long expected = sections[0].Scope == SaveScope.Global ? globalSession : slotSession;
                if (expected != Session(sections[0].Scope))
                    throw new InvalidOperationException("Save state changed while saving");
                await WriteFileAsync(fileName, token);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async UniTask WriteFileAsync(string fileName, CancellationToken token)
    {
        IReadOnlyList<SaveSection> sections = _registry.GetFile(fileName);

        if (sections.Count == 0)
            return;

        string path = RelativePath(sections[0]);
        if (_blockedPaths.Contains(path))
            throw new InvalidOperationException($"Save file '{fileName}' has an unsupported version and cannot be overwritten");
        SaveDocument document = new SaveDocument();

        foreach (SaveSection section in sections)
        {
            if (!_state.TryGetValue(section.DataType, out object value))
                value = Activator.CreateInstance(section.DataType);

            document.Sections[section.Key] = value;
        }

        string text = _serializer.Serialize(document);
        _fileRevisions.TryGetValue(fileName, out long revision);

        await _storage.WriteAsync(path, text, token);

        _fileRevisions.TryGetValue(fileName, out long currentRevision);
        if (currentRevision == revision)
            _dirtyFiles.Remove(fileName);
    }

    public UniTask SaveDirtyAsync(CancellationToken token)
        => _dirtyFiles.Count == 0 ? UniTask.CompletedTask : SaveFilesAsync(new List<string>(_dirtyFiles), token);

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
        if (string.IsNullOrWhiteSpace(slotId) || slotId == "." || slotId == ".." ||
            slotId.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid save slot id", nameof(slotId));
        await _writeLock.WaitAsync(token);
        try
        {
            await _storage.DeleteDirectoryAsync(slotId, token);
            _blockedPaths.RemoveWhere(path => path.StartsWith(slotId + "/", StringComparison.Ordinal));
            if (_activeSlot == slotId)
                ResetScope(SaveScope.Slot);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Fill(IReadOnlyList<SaveSection> sections, SaveDocument document)
    {
        foreach (SaveSection section in sections)
        {
            object value = null;

            document?.Sections.TryGetValue(section.Key, out value);

            _state[section.DataType] = value ?? Activator.CreateInstance(section.DataType);
            _dirtyFiles.Remove(section.FileName);
            _fileRevisions[section.FileName] = ++_revision;
            if (document?.RequiresSave == true)
                _dirtyFiles.Add(section.FileName);
        }
    }

    private string RelativePath(SaveSection section)
    {
        if (section.Scope == SaveScope.Global)
            return section.FileName + FILE_EXTENSION;

        return $"{_activeSlot}/{section.FileName}{FILE_EXTENSION}";
    }
}
