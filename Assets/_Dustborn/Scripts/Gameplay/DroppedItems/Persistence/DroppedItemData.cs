using System;
using UnityEngine;

[Serializable]
public class DroppedItemData
{
    public InventorySlotData Stack = new();
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;
}
