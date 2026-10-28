using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

public sealed class GameSlotCatalog : IDisposable
{
    private readonly GameSlotService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<IReadOnlyList<GameSlotInfo>> _slots = new(Array.Empty<GameSlotInfo>());
    private readonly ReactiveProperty<GameSlotInfo> _latest = new();

    public ReadOnlyReactiveProperty<IReadOnlyList<GameSlotInfo>> Slots => _slots;
    public ReadOnlyReactiveProperty<GameSlotInfo> Latest => _latest;

    public GameSlotCatalog(GameSlotService service)
    {
        _service = service;
    }

    public async UniTask RefreshAsync()
    {
        IReadOnlyList<GameSlotInfo> slots = await _service.ListAsync(_lifetime.Token);
        _slots.Value = slots;
        _latest.Value = slots.Count > 0 ? slots[0] : null;
    }

    public async UniTask DeleteAsync(string slotId)
    {
        await _service.DeleteAsync(slotId, _lifetime.Token);
        await RefreshAsync();
    }

    public UniTask<string> CreateSlotIdAsync() => _service.CreateSlotIdAsync(_lifetime.Token);

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _slots.Dispose();
        _latest.Dispose();
    }
}
