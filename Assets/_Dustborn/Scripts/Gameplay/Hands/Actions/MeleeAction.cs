public sealed class MeleeAction : IItemAction
{
    private readonly MeleeSwing _swing;
    private readonly IHeldItemPresenter _presenter;
    private readonly MeleePresentationConfig _presentation;

    public MeleeAction(MeleeSwing swing, IHeldItemPresenter presenter, MeleePresentationConfig presentation)
    {
        _swing = swing;
        _presenter = presenter;
        _presentation = presentation ?? new();
    }

    public bool TryStart()
    {
        if (!_swing.TryStart())
            return false;

        _presenter.Play(_presentation.Motion);
        return true;
    }

    public bool Tick(float deltaTime)
    {
        bool active = _swing.Tick(deltaTime, out MeleeImpact? impact);

        if (impact.HasValue)
            _presenter.Impact(_presentation.ImpactShake, _presentation.ImpactParticleMaterial, impact.Value);

        return active;
    }

    public void Cancel() => _swing.Cancel();
}
