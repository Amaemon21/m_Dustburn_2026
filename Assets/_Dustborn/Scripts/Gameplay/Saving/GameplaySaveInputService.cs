using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class GameplaySaveInputService : IGameplayActivatable, IDisposable
{
    private readonly ISaveService _saves;
    private readonly InputAction _saveAction = new("SaveGame", InputActionType.Button, "<Keyboard>/f5");
    private readonly CompositeDisposable _subscriptions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _saving;

    public GameplaySaveInputService(ISaveService saves)
    {
        _saves = saves;
    }

    public void Activate()
    {
        _subscriptions.Clear();
        _subscriptions.Add(Observable.FromEvent<InputAction.CallbackContext>(
            handler => _saveAction.performed += handler,
            handler => _saveAction.performed -= handler).Subscribe(_ => SaveAsync().Forget()));
        _saveAction.Enable();
    }

    private async UniTask SaveAsync()
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            await _saves.SaveScopeAsync(SaveScope.Slot, _lifetime.Token);
            Debug.Log("Game saved");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogWarning($"The game could not be saved: {exception.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _subscriptions.Dispose();
        _saveAction.Disable();
        _saveAction.Dispose();
    }
}
