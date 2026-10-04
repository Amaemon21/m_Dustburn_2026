using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InventoryGridData
{
    public string OwnerId;
    public List<InventorySlotData> Slots = new();
    public Vector2Int Size;
    public InventoryGridKind Kind;
}
