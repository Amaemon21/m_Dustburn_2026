using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

public sealed class GameSlotCatalog : IDisposable
{
    private readonly GameSlotService _service;
    private readonly WorldCatalog _worlds;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<IReadOnlyList<GameSlotInfo>> _slots = new(Array.Empty<GameSlotInfo>());
    private readonly ReactiveProperty<GameSlotInfo> _latest = new();
    private readonly ReactiveProperty<string> _error = new(string.Empty);
    private bool _disposed;

    public ReadOnlyReactiveProperty<IReadOnlyList<GameSlotInfo>> Slots => _slots;
    public ReadOnlyReactiveProperty<GameSlotInfo> Latest => _latest;
    public ReadOnlyReactiveProperty<string> Error => _error;

    public GameSlotCatalog(GameSlotService service, WorldCatalog worlds)
    {
        _service = service;
        _worlds = worlds;
    }

    public async UniTask RefreshAsync()
    {
        if (_disposed)
            return;
        CancellationToken token = _lifetime.Token;
        bool entered = false;
        try
        {
            await _refresh.WaitAsync(token);
            entered = true;
            IReadOnlyList<GameSlotInfo> saved = await _service.ListAsync(token);
            IReadOnlyList<WorldInfo> worlds = await _worlds.ListAsync(token);
            var ids = new HashSet<string> { WorldInfo.SCENE_WORLD_ID };
            foreach (WorldInfo world in worlds)
                ids.Add(world.Id);

            var slots = new GameSlotInfo[saved.Count];
            for (int i = 0; i < saved.Count; i++)
            {
                slots[i] = saved[i].WithWorldAvailable(ids.Contains(saved[i].WorldId));
            }
            if (token.IsCancellationRequested)
                return;
            _slots.Value = slots;
            _latest.Value = GameSlotAvailability.LatestAvailable(slots);
            _error.Value = string.Empty;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed)
            {
                _latest.Value = null;
                _error.Value = "\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0432\u0435\u0440\u0438\u0442\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u044f. \u041e\u0442\u043a\u0440\u043e\u0439\u0442\u0435 \u0441\u043f\u0438\u0441\u043e\u043a \u043f\u0440\u043e\u0445\u043e\u0436\u0434\u0435\u043d\u0438\u0439 \u0435\u0449\u0451 \u0440\u0430\u0437.";
            }
        }
        finally
        {
            if (entered)
                _refresh.Release();
        }
    }

    public async UniTask DeleteAsync(string slotId)
    {
        await _service.DeleteAsync(slotId, _lifetime.Token);
        await RefreshAsync();
    }

    public UniTask<string> CreateSlotIdAsync() => _service.CreateSlotIdAsync(_lifetime.Token);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _slots.Dispose();
        _latest.Dispose();
        _error.Dispose();
    }
}
