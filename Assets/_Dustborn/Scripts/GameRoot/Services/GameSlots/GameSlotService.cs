using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed class GameSlotService
{
    private const string SLOT_PREFIX = "game_";
    private const string SLOT_TIME_FORMAT = "yyyyMMdd_HHmmss";
    private const string SAVE_PATTERN = "*" + SaveService.FILE_EXTENSION;

    private readonly ISaveService _saves;
    private readonly ISaveStorage _storage;
    private readonly ISaveSerializer _serializer;

    public GameSlotService(ISaveService saves, ISaveStorage storage, ISaveSerializer serializer)
    {
        _saves = saves;
        _storage = storage;
        _serializer = serializer;
    }

    public async UniTask<IReadOnlyList<GameSlotInfo>> ListAsync(CancellationToken token)
    {
        string[] ids = await _saves.ListSlotsAsync(token);
        List<GameSlotInfo> slots = new();

        foreach (string id in ids)
        {
            if (id == GameplayEnterParams.TEST_SLOT)
                continue;

            DateTime? written = await _storage.LastWriteTimeAsync(id, SAVE_PATTERN, token);
            if (written == null)
                continue;

            ProgressData progress = await PeekAsync<ProgressData>(id, token);
            WorldSaveData world = await PeekAsync<WorldSaveData>(id, token);
            slots.Add(new GameSlotInfo(id, NameOf(id, progress), written.Value.ToLocalTime(),
                world?.WorldName ?? string.Empty, world?.Seed ?? 0, world?.WorldSize ?? 0));
        }

        slots.Sort((a, b) => b.LastPlayed.CompareTo(a.LastPlayed));
        return slots;
    }

    public async UniTask<string> CreateSlotIdAsync(CancellationToken token)
    {
        HashSet<string> taken = new(await _saves.ListSlotsAsync(token));
        string baseId = SLOT_PREFIX + DateTime.Now.ToString(SLOT_TIME_FORMAT, CultureInfo.InvariantCulture);
        string id = baseId;

        for (int suffix = 2; taken.Contains(id); suffix++)
            id = $"{baseId}_{suffix}";

        return id;
    }

    public UniTask DeleteAsync(string slotId, CancellationToken token) => _saves.DeleteSlotAsync(slotId, token);

    public void BeginNewGame(string name, string worldId)
    {
        ProgressData progress = _saves.Get<ProgressData>();
        progress.SlotName = name;
        progress.CreatedUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        progress.UpdatedUnixTime = progress.CreatedUnixTime;
        _saves.MarkDirty<ProgressData>();

        _saves.Get<WorldSaveData>().WorldId = worldId;
        _saves.MarkDirty<WorldSaveData>();
    }

    private static string NameOf(string id, ProgressData progress)
        => string.IsNullOrWhiteSpace(progress?.SlotName) ? id : progress.SlotName;

    private async UniTask<T> PeekAsync<T>(string slotId, CancellationToken token) where T : class
    {
        SaveSection section = _saves.Registry.Get(typeof(T));
        string path = $"{slotId}/{section.FileName}{SaveService.FILE_EXTENSION}";

        try
        {
            string text = await _storage.ReadAsync(path, token) ?? await _storage.ReadBackupAsync(path, token);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            SaveDocument document = _serializer.Deserialize(text, _saves.Registry.GetFile(section.FileName));
            return document.Sections.TryGetValue(section.Key, out object value) ? value as T : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Save slot '{slotId}' section '{section.Key}' is unreadable: {exception.Message}");
            return null;
        }
    }
}
