using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;

public abstract class InputService : IDisposable
{
    private readonly List<InputAction> _actions = new();
    private readonly List<InputAction> _enabledActions = new();
    private bool _enabled;
    
    protected CompositeDisposable Bindings { get; } = new();

    protected Observable<InputAction.CallbackContext> ObservePerformed(InputActionReference reference)
    {
        InputAction action = RegisterAction(reference);
        
        return Observable.FromEvent<InputAction.CallbackContext>(
            handler => action.performed += handler,
            handler => action.performed -= handler).Where(_ => _enabled);
    }

    protected InputAction RegisterAction(InputActionReference reference)
    {
        InputAction action = reference.action;
        _actions.Add(action);
        return action;
    }

    public void Enable()
    {
        _enabled = true;
        
        foreach (InputAction action in _actions)
        {
            if (action.enabled)
                continue;
            
            action.Enable();
            _enabledActions.Add(action);
        }
    }

    public void Disable()
    {
        _enabled = false;
        
        foreach (InputAction action in _enabledActions)
        {
            action.Disable();
        }

        _enabledActions.Clear();
    }

    public void Dispose()
    {
        Bindings.Dispose();
        Disable();
        OnDisposed();
    }

    protected virtual void OnDisposed() { }
}
