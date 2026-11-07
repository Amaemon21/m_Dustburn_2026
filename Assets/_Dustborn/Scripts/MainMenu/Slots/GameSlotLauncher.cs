using System;
using Cysharp.Threading.Tasks;
using R3;

public sealed class GameSlotLauncher : IDisposable
{
    private readonly GameSlotCatalog _catalog;
    private readonly IMainMenuExit _exit;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly ReactiveProperty<bool> _busy = new();
    private bool _disposed;

    public ReadOnlyReactiveProperty<bool> Busy => _busy;

    public GameSlotLauncher(GameSlotCatalog catalog, IMainMenuExit exit, ConfirmDialogViewModel dialog)
    {
        _catalog = catalog;
        _exit = exit;
        _dialog = dialog;
    }

    public async UniTask LoadAsync(string slotId)
    {
        if (_disposed || _busy.Value || slotId == null)
            return;

        bool dispatched = false;
        _busy.Value = true;
        try
        {
            await _catalog.RefreshAsync();
            if (_disposed)
                return;

            if (!string.IsNullOrEmpty(_catalog.Error.CurrentValue))
            {
                _dialog.ShowMessage(_catalog.Error.CurrentValue);
                return;
            }

            foreach (GameSlotInfo slot in _catalog.Slots.CurrentValue)
            {
                if (slot.Id != slotId)
                    continue;
                if (!slot.CanLoad)
                {
                    _dialog.ShowMessage(MainMenuText.SlotProblem(slot));
                    return;
                }
                _exit.LoadGame(slot.Id);
                dispatched = true;
                return;
            }
            _dialog.ShowMessage("\u0421\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435 \u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435 \u0441\u0443\u0449\u0435\u0441\u0442\u0432\u0443\u0435\u0442. \u0421\u043f\u0438\u0441\u043e\u043a \u043e\u0431\u043d\u043e\u0432\u043b\u0451\u043d.");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed)
                _dialog.ShowMessage("\u041d\u0435 \u0443\u0434\u0430\u043b\u043e\u0441\u044c \u043f\u0440\u043e\u0432\u0435\u0440\u0438\u0442\u044c \u0441\u043e\u0445\u0440\u0430\u043d\u0435\u043d\u0438\u0435. \u041f\u043e\u043f\u0440\u043e\u0431\u0443\u0439\u0442\u0435 \u0435\u0449\u0451 \u0440\u0430\u0437.");
        }
        finally
        {
            if (!_disposed && !dispatched)
                _busy.Value = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _busy.Dispose();
    }
}
