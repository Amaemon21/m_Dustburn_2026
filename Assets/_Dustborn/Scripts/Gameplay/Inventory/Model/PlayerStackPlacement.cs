public readonly struct PlayerStackStep
{
    public IReadOnlyInventoryGrid Grid { get; }
    public bool EmptySlots { get; }

    public PlayerStackStep(IReadOnlyInventoryGrid grid, bool emptySlots)
    {
        Grid = grid;
        EmptySlots = emptySlots;
    }
}

public static class PlayerStackPlacement
{
    public static PlayerStackStep[] Route(PlayerInventoryProxy player, string itemId)
    {
        bool hotbarHoldsItem = player.Hotbar.GetAmount(itemId) > 0;

        return hotbarHoldsItem
            ? new[]
            {
                new PlayerStackStep(player.Hotbar, false),
                new PlayerStackStep(player.Backpack, false),
                new PlayerStackStep(player.Hotbar, true),
                new PlayerStackStep(player.Backpack, true)
            }
            : new[]
            {
                new PlayerStackStep(player.Hotbar, false),
                new PlayerStackStep(player.Backpack, false),
                new PlayerStackStep(player.Backpack, true)
            };
    }
}
