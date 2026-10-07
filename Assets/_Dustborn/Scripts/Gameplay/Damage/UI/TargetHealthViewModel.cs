using System;
using R3;

public sealed class TargetHealthViewModel : ViewModel
{
    private readonly TimeSpan _hideDelay;
    private readonly TimeSpan _destroyedHideDelay;
    private readonly ReactiveProperty<bool> _visible = new(false);
    private readonly ReactiveProperty<HealthBarFrame> _frame = new(new HealthBarFrame(0, 0, 0));
    private readonly SerialDisposable _health = new();
    private readonly SerialDisposable _hide = new();
    private IDamageable _target;
    private int _serial;

    public ReadOnlyReactiveProperty<bool> Visible => _visible;
    public ReadOnlyReactiveProperty<HealthBarFrame> Frame => _frame;
    public IDamageable Target => _target;

    public TargetHealthViewModel(TimeSpan hideDelay, TimeSpan destroyedHideDelay)
    {
        _hideDelay = hideDelay;
        _destroyedHideDelay = destroyedHideDelay;
    }

    public void Show(IDamageable target, int dealt)
    {
        if (!ReferenceEquals(_target, target))
            Track(target, dealt);

        _visible.Value = true;
        _hide.Disposable = Observable.Timer(target.IsDestroyed ? _destroyedHideDelay : _hideDelay).Subscribe(_ => Hide());
    }

    public void Hide()
    {
        _hide.Disposable = Disposable.Empty;
        _health.Disposable = Disposable.Empty;
        _target = null;
        _visible.Value = false;
    }

    private void Track(IDamageable target, int dealt)
    {
        _target = target;
        _serial++;
        int serial = _serial;
        int maxHealth = target.MaxHealth;
        _frame.Value = new HealthBarFrame(Math.Min(maxHealth, target.Health.CurrentValue + dealt), maxHealth, serial);
        _health.Disposable = target.Health.Subscribe(health => _frame.Value = new HealthBarFrame(health, maxHealth, serial));
    }

    protected override void OnDisposed()
    {
        _hide.Dispose();
        _health.Dispose();
        _visible.Dispose();
        _frame.Dispose();
        base.OnDisposed();
    }
}
