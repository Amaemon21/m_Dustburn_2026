using System.Collections.Generic;
using UnityEngine;

public sealed class LocalHeldItemEffects : IHeldItemEffects
{
    private const float DROP_OFFSET = 0.4f;

    private readonly IInventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly IItemRewards _rewards;
    private readonly DamageFeed _feed;
    private readonly List<InventorySlotState> _yields = new();

    public LocalHeldItemEffects(IInventoryService inventory, ItemCatalog catalog, IItemRewards rewards, DamageFeed feed)
    {
        _inventory = inventory;
        _catalog = catalog;
        _rewards = rewards;
        _feed = feed;
    }

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
            Damage(target, strike);
        if (!item.IsEmpty && strike.Wear > 0)
            _inventory.AddWear(item.OwnerId, item.Coordinates, item.ItemId, strike.Wear);
    }

    private void Damage(IDamageable target, in StrikeInfo strike)
    {
        MaterialBonus bonus = MaterialBonus.Find(strike.Bonuses, target.Material);
        int damage = bonus == null ? strike.Damage : Mathf.RoundToInt(strike.Damage * bonus.Damage);
        DamageResult result = target.TakeDamage(damage);

        if (target is IHarvestable harvestable)
            Harvest(harvestable, result, bonus == null ? 1f : bonus.Harvest, strike.Point + strike.Normal * DROP_OFFSET);

        _feed.Publish(new DamageReport(target, result));
    }

    private void Harvest(IHarvestable harvestable, DamageResult result, float multiplier, Vector3 dropOrigin)
    {
        _yields.Clear();
        harvestable.Harvest(result, multiplier, _yields);
        foreach (InventorySlotState stack in _yields)
            _rewards.Give(stack, dropOrigin);
    }
}
