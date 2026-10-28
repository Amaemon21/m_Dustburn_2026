using System.Collections.Generic;
using R3;
using UnityEngine;

public sealed class OptionFieldViewModel : ViewModel
{
    public IReadOnlyList<string> Labels { get; }
    public ReactiveProperty<int> Index { get; }

    public OptionFieldViewModel(IReadOnlyList<string> labels, int defaultIndex)
    {
        Labels = labels;
        Index = new ReactiveProperty<int>(Mathf.Clamp(defaultIndex, 0, labels.Count - 1));
        Disposables.Add(Index);
    }
}
