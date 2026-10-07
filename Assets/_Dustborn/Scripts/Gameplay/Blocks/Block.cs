using System;
using System.Collections.Generic;
using R3;

public sealed class Block : IDamageable, IHarvestable, IDisposable
{
    private const double CARRY_EPSILON = 0.0001;

    private readonly BlockConfig _config;
    private readonly Random _random;
    private readonly ReactiveProperty<int> _health;
    private readonly double[] _carry;

    public string Id { get; }
    public DamageTargetKind Kind => DamageTargetKind.Block;
    public BlockMaterial Material => _config.Material;
    public int MaxHealth => _config.MaxHealth;
    public ReadOnlyReactiveProperty<int> Health => _health;
    public bool IsDestroyed => _health.Value <= 0;
    public int Damage => MaxHealth - _health.Value;

    public Block(string id, BlockConfig config, int damage, Random random)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A block needs an id", nameof(id));

        Id = id;
        _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _health = new ReactiveProperty<int>(Math.Clamp(config.MaxHealth - Math.Max(0, damage), 1, config.MaxHealth));
        _carry = new double[config.Harvest?.Length ?? 0];
    }

    public DamageResult TakeDamage(int damage)
    {
        int health = _health.Value;
        if (health <= 0 || damage <= 0)
            return new DamageResult(0, health <= 0);

        int dealt = Math.Min(damage, health);
        _health.Value = health - dealt;
        return new DamageResult(dealt, _health.Value <= 0);
    }

    public void Harvest(DamageResult result, float multiplier, ICollection<InventorySlotState> yields)
    {
        if (result.Dealt > 0 && multiplier > 0f)
            Yield(result.Dealt, multiplier, yields);

        if (result.Destroyed)
            Drop(yields);
    }

    private void Yield(int dealt, float multiplier, ICollection<InventorySlotState> yields)
    {
        HarvestYield[] harvest = _config.Harvest ?? Array.Empty<HarvestYield>();
        int count = Math.Min(harvest.Length, _carry.Length);
        for (int i = 0; i < count; i++)
        {
            HarvestYield yield = harvest[i];
            if (yield == null || yield.Item == null || yield.Count <= 0)
                continue;

            _carry[i] += (double)yield.Count * multiplier * dealt / MaxHealth;
            int whole = (int)Math.Floor(_carry[i] + CARRY_EPSILON);
            if (whole <= 0)
                continue;

            _carry[i] -= whole;
            yields.Add(new InventorySlotState(yield.Item.ItemId, whole));
        }
    }

    private void Drop(ICollection<InventorySlotState> yields)
    {
        foreach (DestroyDrop drop in _config.DestroyDrops ?? Array.Empty<DestroyDrop>())
        {
            if (drop == null || drop.Item == null || _random.NextDouble() >= drop.Chance)
                continue;

            int count = _random.Next(drop.MinCount, Math.Max(drop.MinCount, drop.MaxCount) + 1);
            if (count > 0)
                yields.Add(new InventorySlotState(drop.Item.ItemId, count));
        }
    }

    public void Dispose() => _health.Dispose();
}
