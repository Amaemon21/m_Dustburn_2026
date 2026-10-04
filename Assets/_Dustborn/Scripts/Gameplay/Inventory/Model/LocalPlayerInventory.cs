public sealed class LocalPlayerInventory
{
    public PlayerInventoryProxy Proxy { get; }
    public string OwnerId => Proxy.OwnerId;

    public LocalPlayerInventory(PlayerInventoryService players, PlayerInventorySettings settings)
    {
        Proxy = players.GetOrCreatePlayerInventory(settings);
    }
}
