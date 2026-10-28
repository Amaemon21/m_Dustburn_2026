public interface IHeldItemEffects
{
    bool IsUsable(HeldItem item);
    bool CanExert(float stamina);
    void Exert(float stamina);
    void Strike(HeldItem item, IDamageable target, in StrikeInfo strike);
}
