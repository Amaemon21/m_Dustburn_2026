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
            _dialog.ShowMessage(LocalizationService.Text("menu.saves.missingSlot"));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed)
                _dialog.ShowMessage(LocalizationService.Text("menu.saves.readSlotFailed"));
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
