using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;

internal static partial class Harness
{
    private static async Task VerifyGameSlotPersistence()
    {
        var storage = new SlotStorage();
        SaveRegistry registry = new SaveRegistry()
            .Register<ProgressData>("progress", SaveScope.Slot, SaveInstaller.PROGRESS_FILE)
            .Register<WorldSaveData>("world", SaveScope.Slot, SaveInstaller.WORLD_FILE);
        var serializer = ProjectSerializer();
        var saves = new SaveService(registry, storage, serializer);
        int savedEvents = 0;
        string savedId = null;
        using var saved = saves.SubscribeSlotSaved((id, token) => { savedEvents++; savedId = id; return UniTask.CompletedTask; });
        var slots = new GameSlotService(saves, storage, serializer);
        saves.SetActiveSlot("playthrough");
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        slots.BeginNewGame("First game", "shared_map");
        await saves.SaveDirtyAsync(CancellationToken.None);
        Check(savedEvents == 1 && savedId == "playthrough", "a successful save batch requests one preview for the correct slot");
        await saves.SaveDirtyAsync(CancellationToken.None);
        Check(savedEvents == 1, "an empty dirty save does not request another preview");
        await saves.SaveScopeAsync(SaveScope.Global, CancellationToken.None);
        Check(savedEvents == 1, "saving global settings does not replace a playthrough screenshot");
        var previewGate = new UniTaskCompletionSource();
        using (saves.SubscribeSlotSaved((id, token) => previewGate.Task))
        {
            UniTask operation = saves.SaveScopeAsync(SaveScope.Slot, CancellationToken.None);
            Check(operation.Status == UniTaskStatus.Pending, "saving waits for its preview before reporting completion");
            previewGate.TrySetResult();
            await operation;
        }
        Check(savedEvents == 2, "a full slot save triggers exactly one preview");
        using (saves.SubscribeSlotSaved((id, token) => UniTask.FromException(new InvalidOperationException("Simulated image failure"))))
            await saves.SaveScopeAsync(SaveScope.Slot, CancellationToken.None);
        Check(savedEvents == 3, "a failed after-save image cannot fail a completed save batch");
        storage.Memory.OnWrite = () => throw new System.IO.IOException("Simulated write failure");
        try
        {
            await saves.SaveScopeAsync(SaveScope.Slot, CancellationToken.None);
            Check(false, "a failed save must propagate its storage failure");
        }
        catch (System.IO.IOException) { }
        Check(savedEvents == 3, "a failed save cannot replace the previous screenshot");
        IReadOnlyList<GameSlotInfo> listed = await slots.ListAsync(CancellationToken.None);
        Check(listed.Count == 1 && listed[0].WorldId == "shared_map" && listed[0].CanLoad,
            "saved playthrough metadata preserves its map dependency");
        string progressPath = "playthrough/" + SaveInstaller.PROGRESS_FILE + SaveService.FILE_EXTENSION;
        string worldPath = "playthrough/" + SaveInstaller.WORLD_FILE + SaveService.FILE_EXTENSION;
        storage.Memory.Backups[progressPath] = storage.Memory.Files[progressPath];
        storage.Memory.Backups[worldPath] = storage.Memory.Files[worldPath];
        storage.Memory.Files.Remove(progressPath);
        storage.Memory.Files.Remove(worldPath);
        listed = await slots.ListAsync(CancellationToken.None);
        Check(listed.Count == 1 && listed[0].CanLoad && listed[0].WorldId == "shared_map",
            "backup-only playthroughs remain visible and protect their map");
        storage.Memory.Files[progressPath] = storage.Memory.Backups[progressPath];
        storage.Memory.Files[worldPath] = "broken json";
        listed = await slots.ListAsync(CancellationToken.None);
        Check(listed.Count == 1 && listed[0].WorldId == "shared_map" && listed[0].CanLoad,
            "menu metadata recovers map identity from a healthy backup");
        storage.Memory.Backups.Remove(worldPath);
        listed = await slots.ListAsync(CancellationToken.None);
        Check(listed.Count == 1 && listed[0].Status == GameSlotStatus.Unreadable,
            "unrecoverable metadata remains visible as an unreadable save");
        Expect<InvalidOperationException>(() => WorldDeletionPolicy.EnsureUnused("shared_map", listed),
            "unrecoverable map identity cannot silently permit deleting a dependency");
        await slots.DeleteAsync("playthrough", CancellationToken.None);
        listed = await slots.ListAsync(CancellationToken.None);
        Check(listed.Count == 0, "deleting a damaged playthrough removes it from the save list");
        WorldDeletionPolicy.EnsureUnused("shared_map", listed);
        Check(true, "deleting the last dependent save releases its map");
    }

    private sealed class SlotStorage : ISaveStorage
    {
        public MemoryStorage Memory { get; } = new();
        public string Root { get; }
        public SlotStorage(string root = "memory") => Root = root;
        public UniTask<string> ReadAsync(string path, CancellationToken token) => Memory.ReadAsync(path, token);
        public UniTask<string> ReadBackupAsync(string path, CancellationToken token) => Memory.ReadBackupAsync(path, token);
        public UniTask WriteAsync(string path, string text, CancellationToken token) => Memory.WriteAsync(path, text, token);
        public UniTask<bool> ExistsAsync(string path, CancellationToken token) => Memory.ExistsAsync(path, token);
        public UniTask DeleteAsync(string path, CancellationToken token) => Memory.DeleteAsync(path, token);
        public UniTask QuarantineAsync(string path, CancellationToken token) => Memory.QuarantineAsync(path, token);
        public UniTask<string[]> ListDirectoriesAsync(string path, CancellationToken token)
            => UniTask.FromResult(Memory.Files.Keys.Concat(Memory.Backups.Keys).Select(key => key.Split('/')[0]).Distinct().ToArray());
        public UniTask<DateTime?> LastWriteTimeAsync(string path, string pattern, CancellationToken token)
            => UniTask.FromResult<DateTime?>(Memory.Files.Keys.Concat(Memory.Backups.Keys.Select(key => key + ".bak"))
                .Any(key => key.StartsWith(path + "/") && key.EndsWith(pattern.TrimStart('*'))) ? DateTime.UtcNow : null);
        public UniTask DeleteDirectoryAsync(string path, CancellationToken token)
        {
            foreach (string key in Memory.Files.Keys.Where(key => key.StartsWith(path + "/")).ToArray())
                Memory.Files.Remove(key);
            foreach (string key in Memory.Backups.Keys.Where(key => key.StartsWith(path + "/")).ToArray())
                Memory.Backups.Remove(key);
            return UniTask.CompletedTask;
        }
    }
}
