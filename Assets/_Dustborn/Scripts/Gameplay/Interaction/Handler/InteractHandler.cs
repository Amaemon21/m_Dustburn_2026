using UnityEngine;

public sealed class InteractHandler
{
    private readonly IInteractionTargetProvider _targets;
    private readonly IScreenService _screen;
    private readonly InteractedViewModel _viewModel;
    private readonly IInteractionContext _context;
    private readonly PlayerInputService _input;
    private IInteractableObject _current;

    public InteractHandler(IInteractionTargetProvider targets, IScreenService screen, InteractedViewModel viewModel,
        IInteractionContext context, PlayerInputService input)
    {
        _targets = targets;
        _screen = screen;
        _viewModel = viewModel;
        _context = context;
        _input = input;
    }

    public void RefreshTarget()
    {
        if (_screen.HasAnyWindowOpen())
        {
            Clear();
            return;
        }

        IInteractableObject target = _targets.FindTarget();
        
        if (!IsAlive(target) || !target.IsInteractable())
        {
            Clear();
            return;
        }

        if (!ReferenceEquals(_current, target))
        {
            Clear();
            _current = target;
            target.ShowOutline();
        }
        
        _viewModel.Show(target.InteractKey, _input.InteractBindingDisplayString);
    }

    public void Interact()
    {
        RefreshTarget();
        if (!IsAlive(_current))
            return;

        _current.Interact(_context);
        RefreshTarget();
    }

    public void Clear()
    {
        if (IsAlive(_current))
            _current.HideOutline();
        _current = null;
        _viewModel.Hide();
    }

    private static bool IsAlive(IInteractableObject target)
        => target != null && (target is not Object unityObject || unityObject != null);
}
