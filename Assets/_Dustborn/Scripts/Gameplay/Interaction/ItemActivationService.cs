using System.Collections.Generic;

public sealed class ItemActivationService
{
    private readonly WorldSaveData _world;
    private readonly ISaveService _saves;
    private readonly IInventoryService _inventory;

    public ItemActivationService(ISaveService saves, IInventoryService inventory)
    {
        _world = saves.Get<WorldSaveData>();
        _saves = saves;
        _inventory = inventory;
    }

    public bool IsActivated(string id) => _world.ActivatedObjects?.Contains(id) == true;

    public bool HasRequiredItem(PlayerInventoryProxy player, string itemId)
        => _inventory.Has(player.Backpack.OwnerId, itemId) || _inventory.Has(player.Hotbar.OwnerId, itemId);

    public bool TryActivate(string id, PlayerInventoryProxy player, string itemId)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(itemId) || IsActivated(id))
            return false;

        string backpack = player.Backpack.OwnerId;
        string hotbar = player.Hotbar.OwnerId;
        string source = _inventory.Has(backpack, itemId) ? backpack : _inventory.Has(hotbar, itemId) ? hotbar : null;

        if (source == null)
            return false;

        HashSet<string> activated = _world.ActivatedObjects ??= new HashSet<string>();
        activated.Add(id);

        if (!_inventory.RemoveItemsFromInventory(source, itemId).Success)
        {
            activated.Remove(id);
            return false;
        }

        _saves.MarkDirty<WorldSaveData>();
        return true;
    }
}
