public interface IHeldItemEffects
{
    bool IsUsable(HeldItem item);
    void Strike(HeldItem item, IDamageable target, in StrikeInfo strike);
}
