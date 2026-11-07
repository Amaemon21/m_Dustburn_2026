using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class OptionFieldViewModel : ViewModel
{
    private readonly ReactiveProperty<bool> _interactable = new(true);
    private int _maximum;

    public IReadOnlyList<string> Labels { get; }
    public ReactiveProperty<int> Index { get; }
    public ReadOnlyReactiveProperty<string> Label { get; }
    public ReactiveCommand<Unit> Previous { get; } = new();
    public ReactiveCommand<Unit> Next { get; } = new();
    public int Maximum => _maximum;

    public OptionFieldViewModel(IReadOnlyList<string> labels, int defaultIndex, int maximum = int.MaxValue)
    {
        Labels = labels;
        _maximum = Mathf.Clamp(maximum, 0, labels.Count - 1);
        Index = new ReactiveProperty<int>(Clamp(defaultIndex));
        Label = Index.Select(index => Labels[Clamp(index)]).ToReadOnlyReactiveProperty();

        Disposables.Add(Index);
        Disposables.Add(Label);
        Disposables.Add(Previous);
        Disposables.Add(Next);
        Disposables.Add(_interactable);
        Disposables.Add(Previous.Executed.Subscribe(_ => Step(-1)));
        Disposables.Add(Next.Executed.Subscribe(_ => Step(1)));
        Disposables.Add(Index.CombineLatest(_interactable, (index, interactable) => (index, interactable)).Subscribe(Refresh));
    }

    public void SetInteractable(bool interactable) => _interactable.Value = interactable;

    public void Step(int delta) => Index.Value = Clamp(Index.Value + delta);

    public int Clamp(int index) => Mathf.Clamp(index, 0, _maximum);

    private void Refresh((int Index, bool Interactable) state)
    {
        Previous.SetCanExecute(state.Interactable && state.Index > 0);
        Next.SetCanExecute(state.Interactable && state.Index < _maximum);
    }
}
