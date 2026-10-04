using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

public sealed class InventoryRepository : IDisposable
{
    private readonly ISaveService _saves;
    private readonly string _fileName;
    private readonly CompositeDisposable _subscriptions = new();
    public InventoryData Origin { get; }

    public InventoryRepository(ISaveService saves)
    {
        _saves = saves;
        _fileName = saves.Registry.Get(typeof(InventoryData)).FileName;
        Origin = saves.Get<InventoryData>();
        Origin.Grids ??= new List<InventoryGridData>();
        Origin.Players ??= new List<PlayerInventoryData>();
    }

    public void Track(InventoryGridProxy proxy)
    {
        _subscriptions.Add(proxy.Changed.Subscribe(_ => MarkDirty()));
        if (Origin.Grids.Contains(proxy.Origin))
            return;
        Origin.Grids.Add(proxy.Origin);
        MarkDirty();
    }

    public void Track(PlayerInventoryProxy proxy)
    {
        _subscriptions.Add(proxy.SelectedHotbarSlot.Skip(1).Subscribe(_ => MarkDirty()));
        if (Origin.Players.Contains(proxy.Origin))
            return;
        Origin.Players.Add(proxy.Origin);
        MarkDirty();
    }

    internal void MarkDirty() => _saves.MarkDirty<InventoryData>();

    public UniTask SaveAsync(CancellationToken token)
    {
        return _saves.IsDirty(_fileName)
            ? _saves.SaveFileAsync(_fileName, token)
            : UniTask.CompletedTask;
    }

    public void Dispose() => _subscriptions.Dispose();
}
