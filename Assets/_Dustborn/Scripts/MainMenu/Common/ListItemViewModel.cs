using System;
using R3;
using UnityEngine;

public sealed class ListItemViewModel : ViewModel
{
    private readonly ReactiveProperty<Texture2D> _thumbnail = new();
    public ReadOnlyReactiveProperty<Texture2D> Thumbnail => _thumbnail;

    public void SetThumbnail(Texture2D texture)
    {
        if (!IsDisposed)
            _thumbnail.Value = texture;
    }

    public string Title { get; }
    public string Details { get; }
    public ReadOnlyReactiveProperty<bool> IsSelected { get; }
    public ReactiveCommand<Unit> Select { get; } = new();

    public ListItemViewModel(string title, string details, Observable<bool> isSelected, Action select)
    {
        Title = title;
        Details = details;
        IsSelected = isSelected.ToReadOnlyReactiveProperty();

        Disposables.Add(Select.Executed.Subscribe(_ => select()));
    }
    protected override void OnDisposed()
    {
        IsSelected.Dispose();
        _thumbnail.Dispose();
        Select.Dispose();
    }
}
