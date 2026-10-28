using System;
using R3;

public sealed class ListItemViewModel : ViewModel
{
    public string Title { get; }
    public string Details { get; }
    public ReadOnlyReactiveProperty<bool> IsSelected { get; }
    public ReactiveCommand<Unit> Select { get; } = new();

    public ListItemViewModel(string title, string details, Observable<bool> isSelected, Action select)
    {
        Title = title;
        Details = details;
        IsSelected = isSelected.ToReadOnlyReactiveProperty();

        Disposables.Add(IsSelected);
        Disposables.Add(Select);
        Disposables.Add(Select.Executed.Subscribe(_ => select()));
    }
}
