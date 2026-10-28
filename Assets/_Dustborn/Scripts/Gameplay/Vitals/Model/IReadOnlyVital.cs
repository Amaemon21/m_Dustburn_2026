using R3;

public interface IReadOnlyVital
{
    VitalType Type { get; }
    ReadOnlyReactiveProperty<float> Current { get; }
    ReadOnlyReactiveProperty<float> Max { get; }
    float Fraction { get; }
    bool IsEmpty { get; }
    bool IsFull { get; }
}
