using System;

public sealed class LocalPlayerInventory : IDisposable
{
    public PlayerInventoryProxy Proxy { get; }
    public string OwnerId => Proxy.OwnerId;
    public IReadOnlyInventoryWeight Weight { get; }
    public PlayerBackpack Backpack { get; }

    public LocalPlayerInventory(PlayerInventoryService players, InventoryService inventory, InventoryWeightService weights, ItemCatalog catalog,
        PlayerStatSheet stats, PlayerInventorySettings settings)
    {
        Proxy = players.GetOrCreatePlayerInventory(settings);
        Stat capacity = stats.Get(PlayerStatType.CarryCapacity);
        Backpack = new PlayerBackpack(inventory, catalog, Proxy, capacity, settings.Size);
        Weight = weights.Register(Proxy.OwnerId, capacity, settings.CarryZones, Proxy.Backpack, Proxy.Equipment);
    }

    public void Dispose() => Backpack.Dispose();
}
