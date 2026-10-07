using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal static partial class Harness
{
    private static SaveRegistry WorldRegistry()
        => new SaveRegistry()
            .Register<WorldSaveData>("world", SaveScope.Slot, SaveInstaller.WORLD_FILE)
            .Register<DroppedItemsData>(SaveInstaller.DROPPED_ITEMS_KEY, SaveScope.Slot, SaveInstaller.WORLD_FILE);

    private static async Task VerifyDroppedItemSave()
    {
        MemoryStorage storage = new();
        SaveService saves = new(WorldRegistry(), storage, ProjectSerializer());
        await saves.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        DroppedItemRepository repository = new(saves);
        Check(repository.Items.Count == 0, "a slot with no world file loads no dropped items");

        Vector3 axePosition = new(12.5f, 40f, -3f);
        Quaternion axeRotation = new(0f, 0.70710677f, 0f, 0.70710677f);
        repository.Add(new InventorySlotState("axe", 1, 4), axePosition, axeRotation);
        DroppedItemData wood = repository.Add(new InventorySlotState("wood", 50), Vector3.one, Quaternion.identity);
        DroppedItemData stone = repository.Add(new InventorySlotState("stone", 3), Vector3.zero, Quaternion.identity);
        Check(saves.IsDirty(SaveInstaller.WORLD_FILE), "dropping marks the world file dirty");

        ItemPickup woodPickup = new(wood.Stack.ToState());
        TestPickupContext partial = new() { Capacity = 20 };
        woodPickup.PickUp(partial);
        repository.SetStack(wood, woodPickup.Stack.CurrentValue);
        Check(wood.Stack.ToState().Equals(new InventorySlotState("wood", 30)), "a partly picked up drop keeps what is left");

        ItemPickup stonePickup = new(stone.Stack.ToState());
        stonePickup.PickUp(new TestPickupContext { Capacity = 99 });
        repository.SetStack(stone, stonePickup.Stack.CurrentValue);
        Check(repository.Items.Count == 2 && !repository.Items.Contains(stone), "a fully picked up drop leaves the save");

        await saves.SaveDirtyAsync(CancellationToken.None);
        SaveService reloaded = new(WorldRegistry(), storage, ProjectSerializer());
        await reloaded.LoadScopeAsync(SaveScope.Slot, CancellationToken.None);
        DroppedItemRepository restored = new(reloaded);
        DroppedItemData axe = restored.Items.FirstOrDefault(item => item.Stack.ItemId == "axe");
        DroppedItemData woodLeft = restored.Items.FirstOrDefault(item => item.Stack.ItemId == "wood");
        Check(restored.Items.Count == 2 && axe != null && woodLeft != null, "dropped items survive a save and reload");
        Check(axe != null && axe.Stack.ToState().Equals(new InventorySlotState("axe", 1, 4)), "a dropped item keeps its wear through a save");
        Check(axe != null && axe.Position == axePosition && Quaternion.Angle(axe.Rotation, axeRotation) < 0.01f,
            "a dropped item keeps its place through a save");
        Check(woodLeft != null && woodLeft.Stack.ToState().Equals(new InventorySlotState("wood", 30)),
            "a dropped stack keeps its amount through a save");
    }

    private sealed class TestPickupContext : IInteractionContext
    {
        public int Capacity;
        public InventorySlotState Received;

        public int PickUp(InventorySlotState stack)
        {
            int taken = Mathf.Min(Capacity, stack.Amount);
            Capacity -= taken;
            Received = stack.WithAmount(taken);
            return taken;
        }

        public void OpenContainer(string containerId, ContainerConfig config) { }
    }
}
