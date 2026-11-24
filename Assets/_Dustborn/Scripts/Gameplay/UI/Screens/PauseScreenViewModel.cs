using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public enum PauseSaveStatus
{
    None,
    Saving,
    Saved,
    Failed
}

public sealed class PauseScreenViewModel : WindowViewModel
{
    private readonly ISaveService _saves;
    private readonly IGameplayExit _exit;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ReactiveProperty<PauseSaveStatus> _status = new();

    public ReactiveCommand<Unit> Save { get; } = new();
    public ReactiveCommand<Unit> SaveAndExit { get; } = new();
    public ReadOnlyReactiveProperty<PauseSaveStatus> Status => _status;

    public PauseScreenViewModel(ISaveService saves, IGameplayExit exit)
    {
        _saves = saves;
        _exit = exit;
        Disposables.Add(Save.Executed.Subscribe(_ => SaveAsync(false).Forget()));
        Disposables.Add(SaveAndExit.Executed.Subscribe(_ => SaveAsync(true).Forget()));
    }

    private async UniTask SaveAsync(bool leave)
    {
        if (_status.Value == PauseSaveStatus.Saving)
            return;

        CancellationToken token = _lifetime.Token;
        _status.Value = PauseSaveStatus.Saving;
        SetCommandsEnabled(false);

        try
        {
            await _saves.SaveScopeAsync(SaveScope.Slot, token);
            token.ThrowIfCancellationRequested();
            _status.Value = PauseSaveStatus.Saved;

            if (leave)
            {
                Close.SetCanExecute(true);
                Close.Execute(Unit.Default);
                _exit.ReturnToMainMenu("Saved and left to the main menu");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsDisposed)
                _status.Value = PauseSaveStatus.Failed;

            Debug.LogWarning($"The game could not be saved: {exception.Message}");
        }
        finally
        {
            if (!IsDisposed)
                SetCommandsEnabled(true);
        }
    }

    private void SetCommandsEnabled(bool enabled)
    {
        Close.SetCanExecute(enabled);
        Save.SetCanExecute(enabled);
        SaveAndExit.SetCanExecute(enabled);
    }

    protected override void OnDisposed()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        Save.Dispose();
        SaveAndExit.Dispose();
        _status.Dispose();
        base.OnDisposed();
    }
}
