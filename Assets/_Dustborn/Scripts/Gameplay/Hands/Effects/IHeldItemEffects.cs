public interface IHeldItemEffects
{
    bool IsUsable(HeldItem item);
    bool CanExert(float stamina);
    void Exert(float stamina);
    bool TryStrike(HeldItem item, IDamageable target, in StrikeInfo strike);
}
