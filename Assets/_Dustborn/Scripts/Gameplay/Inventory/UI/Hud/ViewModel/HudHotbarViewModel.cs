using System.Collections.Generic;
using UnityEngine;

public sealed class HudHotbarViewModel : ViewModel
{
    public IReadOnlyList<HudHotbarSlotViewModel> Slots { get; }

    public HudHotbarViewModel(PlayerInventoryProxy player, PlayerInventoryService players, ItemCatalog catalog)
    {
        List<HudHotbarSlotViewModel> slots = new();
        for (int i = 0; i < player.Hotbar.Size.x; i++)
        {
            HudHotbarSlotViewModel slot = new(player.Hotbar.GetSlot(new Vector2Int(i, 0)), i, player.SelectedHotbarSlot, catalog,
                index => players.SelectHotbarSlot(player.OwnerId, index));
            slots.Add(slot);
        }
        Slots = slots.AsReadOnly();
    }

    protected override void OnDisposed()
    {
        foreach (HudHotbarSlotViewModel slot in Slots)
            slot.Dispose();
        base.OnDisposed();
    }
}
