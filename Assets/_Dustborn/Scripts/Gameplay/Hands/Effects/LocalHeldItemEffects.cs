public sealed class LocalHeldItemEffects : IHeldItemEffects
{
    private readonly IInventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly DamageService _damage;
    private readonly IPlayerStamina _stamina;

    public LocalHeldItemEffects(IInventoryService inventory, ItemCatalog catalog, DamageService damage, IPlayerStamina stamina)
    {
        _inventory = inventory;
        _catalog = catalog;
        _damage = damage;
        _stamina = stamina;
    }

    public bool CanExert(float stamina) => _stamina.CanSpend(stamina);

    public void Exert(float stamina) => _stamina.Spend(stamina);

    public bool IsUsable(HeldItem item)
    {
        if (item.IsEmpty)
            return true;
        if (!_inventory.TryGetInventory(item.OwnerId, out IReadOnlyInventoryGrid grid))
            return false;

        InventorySlotState state = grid.GetSlot(item.Coordinates).State.CurrentValue;
        return state.ItemId == item.ItemId && !_catalog.IsWornOut(state);
    }

    public void Strike(HeldItem item, IDamageable target, in StrikeInfo strike)
    {
        if (!IsUsable(item))
            return;
        if (target != null && !target.IsDestroyed)
            _damage.Apply(target, new DamageHit(strike.Damage, strike.Bonuses, strike.Point, strike.Normal));
        if (!item.IsEmpty && strike.Wear > 0)
            _inventory.AddWear(item.OwnerId, item.Coordinates, item.ItemId, strike.Wear);
    }
}
