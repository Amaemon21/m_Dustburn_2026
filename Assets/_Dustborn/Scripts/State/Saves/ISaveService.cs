using System.Threading;
using Cysharp.Threading.Tasks;

public interface ISaveService
{
    SaveRegistry Registry { get; }
    string ActiveSlot { get; }
    bool HasDirtyFiles { get; }

    T Get<T>() where T : class, new();
    void MarkDirty<T>() where T : class, new();
    bool IsDirty(string fileName);

    void SetActiveSlot(string slotId);
    void ResetScope(SaveScope scope);

    UniTask LoadScopeAsync(SaveScope scope, CancellationToken token);
    UniTask LoadFileAsync(string fileName, CancellationToken token);

    UniTask SaveScopeAsync(SaveScope scope, CancellationToken token);
    UniTask SaveFileAsync(string fileName, CancellationToken token);
    UniTask SaveDirtyAsync(CancellationToken token);

    UniTask<string[]> ListSlotsAsync(CancellationToken token);
    UniTask<bool> SlotExistsAsync(string slotId, CancellationToken token);
    UniTask DeleteSlotAsync(string slotId, CancellationToken token);
}
