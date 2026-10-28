using System.Collections.Generic;
using R3;

public interface IReadOnlyStat
{
    float BaseValue { get; }
    ReadOnlyReactiveProperty<float> Value { get; }
    IReadOnlyList<StatModifier> Modifiers { get; }
}
