using System;
using R3;
using UnityEngine;

public sealed class PlayerBackpack : IDisposable
{
    public static readonly Vector2Int SLOT = new((int)EquipmentSlot.Backpack, 0);

    private readonly InventoryService _inventory;
    private readonly ItemCatalog _catalog;
    private readonly PlayerInventoryProxy _player;
    private readonly Stat _capacity;
    private readonly Vector2Int _baseSize;
    private readonly IDisposable _subscription;

    public Vector2Int BaseSize => _baseSize;

    public PlayerBackpack(InventoryService inventory, ItemCatalog catalog, PlayerInventoryProxy player, Stat capacity, Vector2Int baseSize)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        if (baseSize.x <= 0 || baseSize.y <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseSize));
        _baseSize = baseSize;

        _inventory.Lock(player.Equipment.OwnerId, IsLocked);
        _inventory.Restrict(player.Backpack.OwnerId, Accepts);
        _subscription = player.Equipment.GetSlot(SLOT).State.Subscribe(Apply);
    }

    public bool HoldsExtraItems()
    {
        IReadOnlyInventoryGrid grid = _player.Backpack;
        for (int x = 0; x < grid.Size.x; x++)
            for (int y = 0; y < grid.Size.y; y++)
                if ((x >= _baseSize.x || y >= _baseSize.y) && !grid.GetSlot(new Vector2Int(x, y)).IsEmpty)
                    return true;
        return false;
    }

    private bool IsLocked(Vector2Int coords) => coords == SLOT && HoldsExtraItems();

    private bool Accepts(Vector2Int coords, string itemId)
        => (coords.x < _baseSize.x && coords.y < _baseSize.y) || !(_catalog.GetItem(itemId) is BackpackInventoryItem);

    private void Apply(InventorySlotState state)
    {
        BackpackInventoryItem backpack = state.IsEmpty ? null : _catalog.GetItem(state.ItemId) as BackpackInventoryItem;
        _capacity.RemoveModifiersFrom(this);
        if (backpack != null && backpack.CarryBonus > 0f)
            _capacity.AddModifier(new StatModifier(StatModifierType.Flat, backpack.CarryBonus, this));

        Resize(new Vector2Int(_baseSize.x, _baseSize.y + (backpack != null ? backpack.ExtraRows : 0)));
    }

    private void Resize(Vector2Int size)
    {
        Vector2Int current = _player.Backpack.Size;
        if (size == current)
            return;
        if (size.x < current.x || size.y < current.y)
        {
            for (int x = 0; x < current.x; x++)
                for (int y = 0; y < current.y; y++)
                    if ((x >= size.x || y >= size.y) && !_player.Backpack.GetSlot(new Vector2Int(x, y)).IsEmpty)
                    {
                        Debug.LogWarning($"Backpack '{_player.Backpack.OwnerId}' keeps size {current}: slots past {size} are occupied");
                        return;
                    }
        }

        _inventory.SetSize(_player.Backpack.OwnerId, size);
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _inventory.Lock(_player.Equipment.OwnerId, null);
        _inventory.Restrict(_player.Backpack.OwnerId, null);
        _capacity.RemoveModifiersFrom(this);
    }
}
