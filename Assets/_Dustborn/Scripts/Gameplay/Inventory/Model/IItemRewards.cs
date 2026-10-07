using UnityEngine;

public interface IItemRewards
{
    void Give(InventorySlotState stack, Vector3 dropOrigin);
}
