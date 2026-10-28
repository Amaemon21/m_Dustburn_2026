using R3;

public interface IReadOnlyInventoryWeight
{
    ReadOnlyReactiveProperty<float> Current { get; }
    IReadOnlyStat Capacity { get; }
    bool HasLimit { get; }
    ReadOnlyReactiveProperty<bool> IsOverloaded { get; }
    ReadOnlyReactiveProperty<float> Load { get; }
    ReadOnlyReactiveProperty<CarryZone> Zone { get; }
}
