using System;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem;

public sealed class MainMenuBackInput : IDisposable
{
    private readonly IReadOnlyList<MainMenuScreenViewModel> _screens;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly InputAction _back = new("MainMenuBack", InputActionType.Button, "<Keyboard>/escape");
    private readonly CompositeDisposable _subscriptions = new();

    public MainMenuBackInput(IReadOnlyList<MainMenuScreenViewModel> screens, ConfirmDialogViewModel dialog)
    {
        _screens = screens;
        _dialog = dialog;
    }

    public void Activate()
    {
        _subscriptions.Clear();
        _subscriptions.Add(Observable.FromEvent<InputAction.CallbackContext>(
            handler => _back.performed += handler,
            handler => _back.performed -= handler).Subscribe(_ => GoBack()));
        _back.Enable();
    }

    private void GoBack()
    {
        if (_dialog.IsOpen.CurrentValue)
        {
            _dialog.Hide();
            return;
        }

        foreach (MainMenuScreenViewModel screen in _screens)
        {
            if (!screen.IsVisible.CurrentValue)
                continue;

            screen.GoBack();
            return;
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _back.Disable();
        _back.Dispose();
    }
}
