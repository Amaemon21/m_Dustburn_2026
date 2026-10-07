public sealed class PlayerInteractionContext : IInteractionContext
{
    private readonly PlayerInventoryService _players;
    private readonly LocalPlayerInventory _player;
    private readonly ContainerScreenService _containers;

    public PlayerInteractionContext(PlayerInventoryService players, LocalPlayerInventory player, ContainerScreenService containers)
    {
        _players = players;
        _player = player;
        _containers = containers;
    }

    public int PickUp(InventorySlotState stack) => _players.PickUp(_player.OwnerId, stack);

    public void OpenContainer(string containerId, ContainerConfig config) => _containers.Open(containerId, config);
}
