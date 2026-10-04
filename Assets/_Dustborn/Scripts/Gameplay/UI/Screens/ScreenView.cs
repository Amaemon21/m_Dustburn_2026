using System.Collections.Generic;
using UnityEngine;
using VContainer;

public sealed class ScreenView : MonoBehaviour
{
    [SerializeField] private WindowView[] _windows = new WindowView[0];
    public IReadOnlyList<WindowView> Windows => _windows;
    private WindowBindingService _windowBindings;

    [Inject]
    public void Construct(WindowBindingServiceFactory factory)
    {
        _windowBindings?.Dispose();
        _windowBindings = factory.Create(_windows);
    }

    private void OnDestroy()
    {
        _windowBindings?.Dispose();
        _windowBindings = null;
    }
}
