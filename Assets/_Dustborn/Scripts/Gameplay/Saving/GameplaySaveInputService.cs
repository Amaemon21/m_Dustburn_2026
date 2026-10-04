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
    private readonly CancellationTokenSource _cancellation = new();
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
            handler => _saveAction.performed -= handler).Subscribe(_ => SaveAsync(_cancellation.Token).Forget()));
        _saveAction.Enable();
    }

    private async UniTask SaveAsync(CancellationToken token)
    {
        if (_saving)
            return;
        _saving = true;
        try
        {
            await _saves.SaveDirtyAsync(token);
            Debug.Log("Game saved");
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _saveAction.Disable();
        _saveAction.Dispose();
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
