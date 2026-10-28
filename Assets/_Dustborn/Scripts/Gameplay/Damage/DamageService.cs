using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class DamageService
{
    private const float DROP_OFFSET = 0.4f;

    private readonly DamageFeed _feed;
    private readonly IItemRewards _rewards;
    private readonly List<InventorySlotState> _yields = new();

    public DamageService(DamageFeed feed, IItemRewards rewards)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
    }

    public DamageResult Apply(IDamageable target, in DamageHit hit)
    {
        if (target == null || target.IsDestroyed)
            return new DamageResult(0, target != null);

        MaterialBonus bonus = MaterialBonus.Find(hit.Bonuses, target.Material);
        int damage = bonus == null ? hit.Damage : Mathf.RoundToInt(hit.Damage * bonus.Damage);
        DamageResult result = target.TakeDamage(damage);

        if (target is IHarvestable harvestable)
            Harvest(harvestable, result, bonus == null ? 1f : bonus.Harvest, hit.Point + hit.Normal * DROP_OFFSET);

        _feed.Publish(new DamageReport(target, result));
        return result;
    }

    private void Harvest(IHarvestable harvestable, DamageResult result, float multiplier, Vector3 dropOrigin)
    {
        _yields.Clear();
        harvestable.Harvest(result, multiplier, _yields);
        foreach (InventorySlotState stack in _yields)
            _rewards.Give(stack, dropOrigin);
    }
}
