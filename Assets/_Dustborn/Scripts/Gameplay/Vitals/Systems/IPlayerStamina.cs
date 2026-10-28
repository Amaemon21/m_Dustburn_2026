using R3;

public interface IPlayerStamina
{
    ReadOnlyReactiveProperty<bool> IsExhausted { get; }
    Observable<float> Spent { get; }
    bool CanSpend(float amount);
    void Spend(float amount);
}
