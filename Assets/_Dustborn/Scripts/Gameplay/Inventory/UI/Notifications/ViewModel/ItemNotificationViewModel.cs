using System;
using R3;
using UnityEngine;

public sealed class ItemNotificationViewModel : ViewModel
{
    private const string ITEM_FORMAT = "+{0}({1})";
    private const string EXPERIENCE_FORMAT = "+{0}";

    private readonly TimeSpan _lifetime;
    private readonly Action<ItemNotificationViewModel> _expired;
    private readonly ReactiveProperty<string> _text;
    private readonly ReactiveProperty<bool> _isExpired = new(false);
    private readonly SerialDisposable _timer = new();

    public NotificationKind Kind { get; }
    public string Key { get; }
    public Sprite Icon { get; }
    public int Amount { get; private set; }
    public int Total { get; private set; }
    public ReadOnlyReactiveProperty<string> Text => _text;
    public ReadOnlyReactiveProperty<bool> IsExpired => _isExpired;

    public ItemNotificationViewModel(NotificationKind kind, string key, Sprite icon, int amount, int total, TimeSpan lifetime,
        Action<ItemNotificationViewModel> expired)
    {
        Kind = kind;
        Key = key;
        Icon = icon;
        Amount = amount;
        Total = total;
        _lifetime = lifetime;
        _expired = expired;
        _text = new ReactiveProperty<string>(Format());
        Restart();
    }

    public void Add(int amount, int total)
    {
        if (_isExpired.Value)
            return;

        Amount += amount;
        Total = total;
        _text.Value = Format();
        Restart();
    }

    public void Expire()
    {
        if (IsDisposed || _isExpired.Value)
            return;

        _timer.Disposable = Disposable.Empty;
        _isExpired.Value = true;
        _expired?.Invoke(this);
    }

    private string Format() => Kind == NotificationKind.Experience
        ? string.Format(EXPERIENCE_FORMAT, Amount)
        : string.Format(ITEM_FORMAT, Amount, Total);

    private void Restart() => _timer.Disposable = Observable.Timer(_lifetime).Subscribe(_ => Expire());

    protected override void OnDisposed()
    {
        _timer.Dispose();
        _text.Dispose();
        _isExpired.Dispose();
        base.OnDisposed();
    }
}
