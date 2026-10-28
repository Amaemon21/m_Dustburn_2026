using System;
using R3;

public sealed class VitalViewModel : ViewModel
{
    private readonly IReadOnlyVital _vital;

    public VitalType Type => _vital.Type;
    public ReadOnlyReactiveProperty<float> Current => _vital.Current;
    public ReadOnlyReactiveProperty<float> Max => _vital.Max;

    public VitalViewModel(IReadOnlyVital vital)
    {
        _vital = vital ?? throw new ArgumentNullException(nameof(vital));
    }
}
